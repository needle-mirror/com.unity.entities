using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Unity.Entities.SourceGen.Common.SourceGenHelpers;
using CSharpExtensions = Microsoft.CodeAnalysis.CSharp.CSharpExtensions;

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
            if (context.Compilation.Assembly.Name != "Unity.Entities" &&
                context.Compilation.ReferencedAssemblyNames.All(n => n.Name != "Unity.Entities") || context.Compilation.Assembly.Name.Contains("CodeGen.Tests"))
                return;

            SourceGenHelpers.Setup(context);

            Location lastLocation = null;
            LogInfo($"Source generating assembly {context.Compilation.Assembly.Name}...");
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var systemReceiver = (SystemSyntaxReceiver)context.SyntaxReceiver;
                var allModules = systemReceiver.SystemModules;
                var syntaxTreesWithCandidate = SystemGeneratorHelper.GetSyntaxTreesWithCandidates(allModules);
                var systemBaseDerivedTypesWithoutPartialKeyword = systemReceiver.SystemBaseDerivedTypesWithoutPartialKeyword;
                var assemblyHasReferenceToBurst = context.Compilation.ReferencedAssemblyNames.Any(n => n.Name == "Unity.Burst");
                var requiresMissingReferenceToBurst = false;

                foreach (var syntaxTree in syntaxTreesWithCandidate)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();

                    var success = true;
                    var systemTypesInTree = SystemGeneratorHelper.GetSystemTypeInTreeWithCandidate(syntaxTree, allModules);
                    var originalToGeneratedPartial = new Dictionary<TypeDeclarationSyntax, TypeDeclarationSyntax>();
                    var semanticModel = context.Compilation.GetSemanticModel(syntaxTree);

                    foreach (var systemTypeSyntax in systemTypesInTree)
                    {
                        context.CancellationToken.ThrowIfCancellationRequested();

                        lastLocation = systemTypeSyntax.GetLocation();

                        var systemType = semanticModel.GetDeclaredSymbol(systemTypeSyntax);
                        if (!(systemType.InheritsFromType("Unity.Entities.SystemBase") ||
                              systemType.InheritsFromInterface("Unity.Entities.ISystem")))
                            continue;

                        var systemTypeGeneratorContext = new SystemGeneratorContext(systemTypeSyntax, semanticModel, context.Compilation,
                            context.ParseOptions.PreprocessorSymbolNames);
                        var allModulesAffectingSystemType = SystemGeneratorHelper.GetAllModulesAffectingSystemType(systemTypeSyntax, allModules, context);
                        foreach (var module in allModulesAffectingSystemType)
                        {
                            context.CancellationToken.ThrowIfCancellationRequested();

                            success &= module.GenerateSystemType(systemTypeGeneratorContext);
                            if (module.RequiresReferenceToBurst && !assemblyHasReferenceToBurst)
                            {
                                requiresMissingReferenceToBurst = true;
                                success = false;
                            }
                        }

                        foreach (var diagnostic in systemTypeGeneratorContext.Diagnostics)
                            context.ReportDiagnostic(diagnostic);

                        if (!systemTypeGeneratorContext.MadeChangesToSystem())
                            continue;

                        // Only check these for valid partial after the type has had successful module generation.
                        // Otherwise we may catch systems that have no changes made to them, and use no generated code.
                        if ((systemTypeSyntax is ClassDeclarationSyntax classDeclarationSyntax &&
                             !classDeclarationSyntax.HasModifier(SyntaxKind.PartialKeyword)) ||
                            (systemTypeSyntax is StructDeclarationSyntax structDeclarationSyntax &&
                             structDeclarationSyntax.Modifiers.All(modifier => modifier.Kind() != SyntaxKind.PartialKeyword)))
                        {
                            systemBaseDerivedTypesWithoutPartialKeyword.Add(systemTypeSyntax);
                            success = false;
                        }

                        originalToGeneratedPartial[systemTypeSyntax] = systemTypeGeneratorContext.GeneratePartialType();
                    }

                    var rootNodes = TypeCreationHelpers.GetReplacedRootNodes(syntaxTree, originalToGeneratedPartial);
                    if (rootNodes.Count == 0)
                        continue;
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var outputSource = TypeCreationHelpers.GenerateSourceTextForRootNodes(GeneratorName, context, syntaxTree, rootNodes);

                    // Only output source to compilation if we are successful (failing early in this case will speed up compilation and avoid non-useful errors)
                    if (success)
                        context.AddSource(syntaxTree.GetGeneratedSourceFileName(GeneratorName), outputSource);
                    OutputNewSourceToFile(context, syntaxTree.GetGeneratedSourceFilePath(context.Compilation.Assembly, GeneratorName), outputSource);
                }

                foreach (var systemBaseDerivedTypeWithoutPartialKeyword in systemBaseDerivedTypesWithoutPartialKeyword)
                    SystemGeneratorErrors.DC0058(context, systemBaseDerivedTypeWithoutPartialKeyword.GetLocation(), systemBaseDerivedTypeWithoutPartialKeyword.Identifier.ValueText);

                if (systemBaseDerivedTypesWithoutPartialKeyword.Any() && context.ParseOptions.PreprocessorSymbolNames.Contains("DOTS_ADD_PARTIAL_KEYWORD"))
                    AddMissingPartialKeywords(systemBaseDerivedTypesWithoutPartialKeyword);

                if (requiresMissingReferenceToBurst)
                    SystemGeneratorErrors.DC0060(context, context.Compilation.SyntaxTrees.First().GetRoot().GetLocation(), context.Compilation.AssemblyName);

                stopwatch.Stop();
                LogInfo($"TIME : SystemGenerator : {context.Compilation.Assembly.Name} : {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception exception)
            {
                if (exception is OperationCanceledException)
                    throw;

                context.LogError("SGICE002", "SystemGenerator", exception.ToString(), lastLocation ?? context.Compilation.SyntaxTrees.First().GetRoot().GetLocation());
            }
        }

        static void OutputNewSourceToFile(GeneratorExecutionContext context, string generatedSourceFilePath, SourceText sourceTextForNewClass)
        {
            // Output as generated source file for debugging/inspection
            if (SourceGenHelpers.CanWriteToProjectPath)
            {
                try
                {
                    LogInfo($"Outputting generated source to file {generatedSourceFilePath}...");
                    File.WriteAllText(generatedSourceFilePath, sourceTextForNewClass.ToString());
                }
                catch (IOException ioException)
                {
                    // Emit exception as info but don't block compilation or generate error to fail tests
                    context.LogInfo("SGICE005", "System Generator New",
                        ioException.ToString(), context.Compilation.SyntaxTrees.First().GetRoot().GetLocation());
                }
            }
        }

        static void AddMissingPartialKeywords(IEnumerable<TypeDeclarationSyntax> systemBaseDerivedTypesWithoutPartialKeyword)
        {
            var syntaxTreeToSystemsMissingPartial = systemBaseDerivedTypesWithoutPartialKeyword.GroupBy(type => type.SyntaxTree)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var item in syntaxTreeToSystemsMissingPartial)
            {
                var replacements = new Dictionary<SyntaxNode, SyntaxNode>();
                foreach (var systemType in item.Value)
                {
                    var modifiers = systemType.Modifiers.Add(Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(Space));
                    replacements[systemType] = systemType.WithModifiers(modifiers);
                }

                var replacer = new SyntaxNodeReplacer(replacements, false);
                var newSyntaxTreeWithReplacements = replacer.Visit(item.Key.GetRoot());
                File.WriteAllText(item.Key.FilePath, newSyntaxTreeWithReplacements.ToFullString());
            }
        }
    }
}
