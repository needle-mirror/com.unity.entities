using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.SystemGenerator
{
    [Generator]
    public class SystemGenerator : ISourceGenerator
    {
        const string GeneratorName = "System";
        public void Initialize(GeneratorInitializationContext context) =>
            context.RegisterForSyntaxNotifications(() => new SystemSyntaxReceiver(context.CancellationToken));

        public void Execute(GeneratorExecutionContext context)
        {
            if (!SourceGenHelpers.IsBuildTime || !SourceGenHelpers.ShouldRun(context.Compilation, context.CancellationToken))
                return;

            SourceGenHelpers.Setup(context);

            // TODO: Disabled running parallel for now so we can shake out race conditions.
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 1 };

            // Please be aware that `lastLocation` is probably unreliable, because there is no way to control which thread updates its value.
            // Perhaps we should remove this entirely.
            Location lastLocation = null;
            SourceGenHelpers.LogInfo($"Source generating assembly {context.Compilation.Assembly.Name}...");

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var systemReceiver = (SystemSyntaxReceiver)context.SyntaxReceiver;
                var allModules = systemReceiver.SystemModules;
                var syntaxTreesWithCandidate = SystemGeneratorHelper.GetSyntaxTreesWithCandidates(allModules);
                var systemBaseDerivedTypesWithoutPartialKeyword = systemReceiver.SystemBaseDerivedTypesWithoutPartialKeyword;
                var assemblyHasReferenceToBurst = context.Compilation.ReferencedAssemblyNames.Any(n => n.Name == "Unity.Burst");
                var requiresMissingReferenceToBurst = false;

                // If multiple user-written parts exist for a given partial type, then each part might have its own syntax tree and semantic model, and thus its own `SystemDescription`.
                // Therefore we cannot group `SystemDescription`s by syntax tree or by semantic model, because we might end up wrongly treating partial parts of the same type
                // as distinct types. Instead, we group `SystemDescription`s by the fully qualified names of the system types they handle.
                var systemTypeFullNamesToDescriptions = new ConcurrentDictionary<string, ConcurrentBag<SystemDescription>>();

                // Process all candidates and create `SystemDescription`s for viable candidates in parallel.
                // Store results in `systemTypeFullNamesToDescriptions`.
                Parallel.ForEach(syntaxTreesWithCandidate, parallelOptions,
                    syntaxTree =>
                    {
                        context.CancellationToken.ThrowIfCancellationRequested();

                        var syntaxTreeInfo = new SyntaxTreeInfo { Tree = syntaxTree, IsSourceGenerationSuccessful = true };
                        var semanticModel = context.Compilation.GetSemanticModel(syntaxTree);

                        // Nested `Parallel.ForEach`s are actually OK to use: https://devblogs.microsoft.com/pfxteam/is-it-ok-to-use-nested-parallel-for-loops/
                        // But there is probably no need to use `Parallel.ForEach` here, since each syntax tree is very unlikely to contain a large number of types.
                        foreach (var systemTypeSyntax in SystemGeneratorHelper.GetSystemTypesInTree(syntaxTree, allModules))
                        {
                            context.CancellationToken.ThrowIfCancellationRequested();
                            if (systemTypeSyntax.Identifier.ValueText == "SystemBase")
                                continue;

                            lastLocation = systemTypeSyntax.GetLocation();

                            if (systemReceiver.ISystemDefinedAsClass.Contains(systemTypeSyntax))
                                continue;

                            var systemTypeSymbol = semanticModel.GetDeclaredSymbol(systemTypeSyntax);
                            var systemTypeInfo = systemTypeSymbol.TryGetSystemType();
                            if (!systemTypeInfo.IsSystemType)
                                continue;

                            var systemDescription = new SystemDescription(systemTypeSyntax, systemTypeInfo.SystemType, systemTypeSymbol, semanticModel, context.Compilation, context.ParseOptions.PreprocessorSymbolNames, syntaxTreeInfo);

                            // No need to use `Parallel.ForEach` here either, since we have so few modules.
                            foreach (var module in SystemGeneratorHelper.GetAllModulesWithCandidatesInSystemType(systemTypeSyntax, allModules, context))
                            {
                                context.CancellationToken.ThrowIfCancellationRequested();

                                if (!module.RegisterChangesInSystem(systemDescription))
                                    continue;

                                if (module.RequiresReferenceToBurst && !assemblyHasReferenceToBurst)
                                {
                                    requiresMissingReferenceToBurst = true;
                                    syntaxTreeInfo.IsSourceGenerationSuccessful = false;
                                }
                            }
                            foreach (var systemDescriptionDiagnostic in systemDescription.Diagnostics)
                                context.ReportDiagnostic(systemDescriptionDiagnostic);
                            systemDescription.Diagnostics.Clear();

                            if (!systemDescription.ContainsChangesToSystem())
                                continue;

                            switch (systemTypeSyntax)
                            {
                                // Only check these for valid partial after the type has had successful module generation.
                                // Otherwise we may catch systems that have no changes made to them, and use no generated code.
                                case ClassDeclarationSyntax classDeclarationSyntax
                                    when !classDeclarationSyntax.HasModifier(SyntaxKind.PartialKeyword):
                                case StructDeclarationSyntax structDeclarationSyntax
                                    when structDeclarationSyntax.Modifiers.All(modifier => modifier.Kind() != SyntaxKind.PartialKeyword):
                                {
                                    systemBaseDerivedTypesWithoutPartialKeyword.Add(systemTypeSyntax);
                                    syntaxTreeInfo.IsSourceGenerationSuccessful = false;
                                    break;
                                }
                            }

                            systemTypeFullNamesToDescriptions.AddOrUpdate(
                                systemDescription.SystemTypeFullName,
                                new ConcurrentBag<SystemDescription>{systemDescription},
                                updateValueFactory: (_, previousDescriptions) =>
                                {
                                    previousDescriptions.Add(systemDescription);
                                    return previousDescriptions;
                                });
                        }
                    });

                var partialSystemTypesGroupedBySyntaxTrees = new ConcurrentDictionary<SyntaxTreeInfo, ConcurrentDictionary<TypeDeclarationSyntax, TypeDeclarationSyntax>>();

                // Generate partial types in parallel and store results in `partialSystemTypesGroupedBySyntaxTrees`
                Parallel.ForEach(systemTypeFullNamesToDescriptions, parallelOptions,
                    kvp =>
                    {
                        var allDescriptionsForSameSystemType = kvp.Value;
                        var generatedPart = PartialSystemTypeGenerator.Generate(allDescriptionsForSameSystemType.ToArray());
                        foreach (var systemDescriptionDiagnostic in allDescriptionsForSameSystemType.SelectMany(d=>d.Diagnostics))
                            context.ReportDiagnostic(systemDescriptionDiagnostic);

                        partialSystemTypesGroupedBySyntaxTrees.AddOrUpdate(
                            generatedPart.SyntaxTreeInfo,
                            addValueFactory: syntaxTreeInfo =>
                            {
                                var originalToGeneratedPartialDictionary = new ConcurrentDictionary<TypeDeclarationSyntax, TypeDeclarationSyntax>();
                                originalToGeneratedPartialDictionary.TryAdd(generatedPart.OriginalSystem, generatedPart.GeneratedPartialSystem);

                                return originalToGeneratedPartialDictionary;
                            },
                            updateValueFactory: (_, previousDescriptions) =>
                            {
                                previousDescriptions.TryAdd(generatedPart.OriginalSystem, generatedPart.GeneratedPartialSystem);
                                return previousDescriptions;
                            });
                    });

                // Generate source files in parallel for debugging purposes (very useful to be able to visually inspect generated code!).
                // Add generated source to compilation only if there are no errors.
                Parallel.ForEach(partialSystemTypesGroupedBySyntaxTrees, parallelOptions,
                    kvp =>
                    {
                        var syntaxTreeInfo = kvp.Key;
                        var replacements = kvp.Value;

                        var rootNodes = TypeCreationHelpers.GetReplacedRootNodes(syntaxTreeInfo.Tree, replacements);
                        if (rootNodes.Count == 0)
                            return;

                        context.CancellationToken.ThrowIfCancellationRequested();
                        var outputSource = TypeCreationHelpers.GenerateSourceTextForRootNodes(GeneratorName, context, syntaxTreeInfo.Tree, rootNodes);

                        // Only output source to compilation if we are successful (failing early in this case will speed up compilation and avoid non-useful errors)
                        if (syntaxTreeInfo.IsSourceGenerationSuccessful)
                            context.AddSource(syntaxTreeInfo.Tree.GetGeneratedSourceFileName(GeneratorName), outputSource);

                        SourceGenHelpers.OutputSourceToFile(context,
                            syntaxTreeInfo.Tree.GetGeneratedSourceFilePath(context.Compilation.Assembly.Name, GeneratorName), outputSource);
                    });

                foreach (var iSystemDefinedAsClass in systemReceiver.ISystemDefinedAsClass)
                {
                    SystemGeneratorErrors.DC0065(context, iSystemDefinedAsClass.GetLocation(),
                        iSystemDefinedAsClass.Identifier.ValueText);
                }

                foreach (var systemBaseDerivedTypeWithoutPartialKeyword in systemBaseDerivedTypesWithoutPartialKeyword)
                {
                    var systemType = systemBaseDerivedTypeWithoutPartialKeyword is StructDeclarationSyntax
                        ? SystemGeneratorCommon.SystemType.ISystem
                        : SystemGeneratorCommon.SystemType.SystemBase;
                    SystemGeneratorErrors.DC0058(context, systemBaseDerivedTypeWithoutPartialKeyword.GetLocation(),
                        systemBaseDerivedTypeWithoutPartialKeyword.Identifier.ValueText, systemType);
                }

                if (requiresMissingReferenceToBurst)
                    SystemGeneratorErrors.DC0060(context, context.Compilation.SyntaxTrees.First().GetRoot().GetLocation(), context.Compilation.AssemblyName);

                stopwatch.Stop();
                SourceGenHelpers.LogInfo($"TIME : SystemGenerator : {context.Compilation.Assembly.Name} : {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception exception)
            {
                if (exception is OperationCanceledException)
                    throw;

                // Please be aware that `lastLocation` is probably unreliable, because there is no way to control which thread updates its value.
                // Perhaps we should remove `lastLocation` entirely.
                context.LogError("SGICE002", "SystemGenerator", exception.ToUnityPrintableString(), lastLocation ?? context.Compilation.SyntaxTrees.First().GetRoot().GetLocation());
            }
        }

        static void AddMissingPartialKeywords(IEnumerable<TypeDeclarationSyntax> systemBaseDerivedTypesWithoutPartialKeyword)
        {
            var syntaxTreeToSystemsMissingPartial =
                systemBaseDerivedTypesWithoutPartialKeyword
                    .GroupBy(type => type.SyntaxTree)
                    .ToDictionary(g => g.Key, g => g.ToArray());

            foreach (var kvp in syntaxTreeToSystemsMissingPartial)
            {
                var replacements = new Dictionary<SyntaxNode, SyntaxNode>();
                foreach (var systemType in kvp.Value)
                {
                    var modifiers = systemType.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(SyntaxFactory.Space));
                    replacements[systemType] = systemType.WithModifiers(modifiers);
                }

                var replacer = new UntrackedSystemNodeReplacer(replacements);
                var newSyntaxTreeWithReplacements = replacer.Visit(kvp.Key.GetRoot());
                File.WriteAllText(kvp.Key.FilePath, newSyntaxTreeWithReplacements.ToFullString());
            }
        }
    }
}
