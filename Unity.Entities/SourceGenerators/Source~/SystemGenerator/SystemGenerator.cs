using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGenerator.Common;
using Unity.Entities.SourceGen.SystemGenerator.EntityQueryBulkOperations;

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

            SourceOutputHelpers.Setup(context.ParseOptions, context.AdditionalFiles);

            Location lastLocation = null;
            SourceOutputHelpers.LogInfoToSourceGenLog($"Source generating assembly {context.Compilation.Assembly.Name}...");

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var systemReceiver = (SystemSyntaxReceiver)context.SyntaxReceiver;
                var allModules = systemReceiver.SystemModules;
                var syntaxTreesWithCandidate = SystemGeneratorHelper.GetSyntaxTreesWithCandidates(allModules);
                var assemblyHasReferenceToBurst = context.Compilation.ReferencedAssemblyNames.Any(n => n.Name == "Unity.Burst");
                var assemblyHasReferenceToCollections = context.Compilation.ReferencedAssemblyNames.Any(n => n.Name == "Unity.Collections");
                var requiresMissingReferenceToBurst = false;

                // If multiple user-written parts exist for a given partial type, then each part might have its own syntax tree and semantic model, and thus its own `SystemDescription`.
                // Therefore we cannot group `SystemDescription`s by syntax tree or by semantic model, because we might end up wrongly treating partial parts of the same type
                // as distinct types. Instead, we group `SystemDescription`s by the fully qualified names of the system types they handle.
                var systemTypeFullNamesToDescriptions = new Dictionary<string, List<SystemDescription>>();

                // Process all candidates and create `SystemDescription`s for viable candidates
                // Store results in `systemTypeFullNamesToDescriptions`.
                foreach (var syntaxTree in syntaxTreesWithCandidate)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();

                    var syntaxTreeInfo = new SyntaxTreeInfo { Tree = syntaxTree, IsSourceGenerationSuccessful = true };
                    var semanticModel = context.Compilation.GetSemanticModel(syntaxTree);

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

                        var systemDescription = new SystemDescription(systemTypeSyntax, systemTypeInfo.SystemType, systemTypeSymbol, semanticModel, context.ParseOptions.PreprocessorSymbolNames, syntaxTreeInfo);

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

                        systemTypeFullNamesToDescriptions.Add(systemDescription.SystemTypeFullName, systemDescription);
                    }
                }

                var partialSystemTypesGroupedBySyntaxTrees = new Dictionary<SyntaxTreeInfo, Dictionary<TypeDeclarationSyntax, TypeDeclarationSyntax>>();

                // Generate partial types and store results in `partialSystemTypesGroupedBySyntaxTrees`
                foreach (var kvp in systemTypeFullNamesToDescriptions)
                {
                    var allDescriptionsForSameSystemType = kvp.Value;
                    var generatedPart = PartialSystemTypeGenerator.Generate(allDescriptionsForSameSystemType.ToArray());
                    foreach (var systemDescriptionDiagnostic in allDescriptionsForSameSystemType.SelectMany(d=>d.Diagnostics))
                        context.ReportDiagnostic(systemDescriptionDiagnostic);

                    if (partialSystemTypesGroupedBySyntaxTrees.TryGetValue(generatedPart.SyntaxTreeInfo,
                            out var foundDict)) {
                        foundDict[generatedPart.OriginalSystem] = generatedPart.GeneratedPartialSystem;
                    } else {
                        partialSystemTypesGroupedBySyntaxTrees.Add(generatedPart.SyntaxTreeInfo,
                            new Dictionary<TypeDeclarationSyntax, TypeDeclarationSyntax>
                                { { generatedPart.OriginalSystem, generatedPart.GeneratedPartialSystem } });
                    }
                }

                // Generate source files in parallel for debugging purposes (very useful to be able to visually inspect generated code!).
                // Add generated source to compilation only if there are no errors.
                foreach (var kvp in partialSystemTypesGroupedBySyntaxTrees)
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

                    SourceOutputHelpers.OutputSourceToFile(
                        syntaxTreeInfo.Tree.GetGeneratedSourceFilePath(context.Compilation.Assembly.Name, GeneratorName),
                        () => outputSource.ToString());
                }

                foreach (var iSystemDefinedAsClass in systemReceiver.ISystemDefinedAsClass)
                {
                    SystemGeneratorErrors.DC0065(context, iSystemDefinedAsClass.GetLocation(),
                        iSystemDefinedAsClass.Identifier.ValueText);
                }

                if (requiresMissingReferenceToBurst)
                    SystemGeneratorErrors.DC0060(context, context.Compilation.SyntaxTrees.First().GetRoot().GetLocation(), context.Compilation.AssemblyName);
                else if (!assemblyHasReferenceToCollections)
                    SystemGeneratorErrors.DC0061(context, context.Compilation.SyntaxTrees.First().GetRoot().GetLocation(), context.Compilation.AssemblyName);

                stopwatch.Stop();
                SourceOutputHelpers.LogInfoToSourceGenLog(
                    $"TIME : SystemGenerator : {context.Compilation.Assembly.Name} : {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception exception)
            {
                if (exception is OperationCanceledException)
                    throw;

                context.LogError("SGICE002", "SystemGenerator", exception.ToUnityPrintableString(), lastLocation ?? context.Compilation.SyntaxTrees.First().GetRoot().GetLocation());
            }
        }
    }
}
