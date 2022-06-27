using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using System.IO;
using Unity.Entities.SourceGen.Common;
using static Unity.Entities.SourceGen.Common.SourceGenHelpers;

namespace Unity.Entities.SourceGen.AuthoringComponent
{
    [Generator]
    public class AuthoringComponentGenerator : ISourceGenerator
    {
        internal static readonly string GeneratorName = "AuthoringComponent";
        public void Execute(GeneratorExecutionContext context)
        {
            if (context.ParseOptions.PreprocessorSymbolNames.Contains("UNITY_DOTSRUNTIME"))
                return;

            bool IsGeneratedAuthoringComponentAttribute(AttributeData data)
            {
                return data.AttributeClass.ContainingAssembly.Name == "Unity.Entities"
                       && data.AttributeClass.Name == "GenerateAuthoringComponentAttribute";
            }

            SourceGenHelpers.Setup(context);
            try
            {
                if (context.Compilation.Assembly.Name.Contains("CodeGen.Tests"))
                {
                    return;
                }
                var authoringComponentReceiver = (AuthoringComponentReceiver)context.SyntaxReceiver;
                if (!authoringComponentReceiver.CandidateSyntaxes.Any())
                    return;

                if (context.Compilation.ReferencedAssemblyNames.All(n => n.Name != "Unity.Entities.Hybrid"))
                {
                    AuthoringComponentErrors.DC0061(context, authoringComponentReceiver.CandidateSyntaxes.First().GetLocation(), context.Compilation.AssemblyName);
                    return;
                }

                LogInfo($"Source generating assembly {context.Compilation.Assembly.Name} for authoring components...");
                var stopwatch = Stopwatch.StartNew();

                var syntaxTreesToCandidateAuthoringComponents = authoringComponentReceiver.CandidateSyntaxes.GroupBy(node => node.SyntaxTree);
                foreach (var candidateAuthoringComponents in syntaxTreesToCandidateAuthoringComponents)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();

                    var syntaxTree = candidateAuthoringComponents.Key;
                    var authoringComponentsInSyntaxTree = new List<AuthoringComponentDescription>();
                    foreach (var candidateAuthoringComponentSyntax in candidateAuthoringComponents)
                    {
                        var semanticModel = context.Compilation.GetSemanticModel(syntaxTree);
                        var candidateSymbol = semanticModel.GetDeclaredSymbol(candidateAuthoringComponentSyntax);
                        if (!candidateSymbol.GetAttributes().Any(IsGeneratedAuthoringComponentAttribute))
                            continue;

                        LogInfo($"Parsing authoring component {candidateSymbol.Name}");
                        var authoringComponent = new AuthoringComponentDescription(candidateAuthoringComponentSyntax, candidateSymbol, context);
                        if (authoringComponent.IsValid)
                        {
                            authoringComponentsInSyntaxTree.Add(authoringComponent);
                        }
                    }

                    var compilationUnit =
                        CompilationUnit().AddMembers(authoringComponentsInSyntaxTree.Select(GenerateAuthoringTypeFrom).ToArray())
                            .NormalizeWhitespace();

                    var generatedSourceHint = syntaxTree.GetGeneratedSourceFileName(GeneratorName);
                    var generatedSourceFullPath = syntaxTree.GetGeneratedSourceFilePath(context.Compilation.Assembly, GeneratorName);
                    var sourceTextForNewClass = compilationUnit.GetText(Encoding.UTF8);
                    sourceTextForNewClass = sourceTextForNewClass.WithInitialLineDirectiveToGeneratedSource(generatedSourceFullPath);
                    context.AddSource(generatedSourceHint, sourceTextForNewClass);

                    if(!SourceGenHelpers.CanWriteToProjectPath)
                        continue;

                    // Output as generated source file for debugging/inspection
                    try
                    {
                        LogInfo($"Authoring Component Generator: Outputting generated source to file {generatedSourceFullPath}...");
                        File.WriteAllText(generatedSourceFullPath, sourceTextForNewClass.ToString());
                    }
                    catch (IOException ioException)
                    {
                        // Emit exception as info but don't block compilation or generate error to fail tests
                        context.LogInfo("SGICE006", "Authoring Component Generator", ioException.ToString(), context.Compilation.SyntaxTrees.First().GetRoot().GetLocation());
                    }
                }

                stopwatch.Stop();
                LogInfo($"TIME : AuthoringComponent : {context.Compilation.Assembly.Name} : {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception exception)
            {
                if (exception is OperationCanceledException)
                    throw;

                context.LogError("SGICE004", "Authoring Component", exception.ToString(), context.Compilation.SyntaxTrees.First().GetRoot().GetLocation());
            }
        }

        static MemberDeclarationSyntax GenerateAuthoringTypeFrom(AuthoringComponentDescription authoringComponentDescription)
        {
            switch (authoringComponentDescription.Interface)
            {
                case AuthoringComponentInterface.IComponentData:
                    return AuthoringComponentFactory.GenerateComponentDataAuthoring(authoringComponentDescription)
                        .AddNamespaces(authoringComponentDescription.NamespacesFromMostToLeastNested);
                case AuthoringComponentInterface.IBufferElementData:
                    return AuthoringComponentFactory.GenerateBufferingElementDataAuthoring(authoringComponentDescription)
                        .AddNamespaces(authoringComponentDescription.NamespacesFromMostToLeastNested);
                default:
                    return default;
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new AuthoringComponentReceiver(context.CancellationToken));
        }
    }
}
