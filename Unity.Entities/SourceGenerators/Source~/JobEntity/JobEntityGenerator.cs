using System;
using System.Collections.Generic;
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

/*
    This is the JobEntityGenerator for the IJobEntity feature.
    Please read the comment in JobEntityModule for an overview of how these two generators interacts to make this feature work.
*/

namespace Unity.Entities.SourceGen.JobEntity
{
    [Generator]
    public class JobEntityGenerator : ISourceGenerator, ISourceGeneratorDiagnosable
    {
        internal static readonly string GeneratorName = "JobEntity";
        public List<Diagnostic> Diagnostics { get; }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new JobEntitySyntaxReceiver(context.CancellationToken));
        }

        public void Execute(GeneratorExecutionContext context)
        {
            SourceGenHelpers.Setup(context);
            var systemReceiver = (JobEntitySyntaxReceiver)context.SyntaxReceiver;

            try
            {
                foreach (var kvp in systemReceiver.JobCandidatesBySyntaxTree)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var syntaxTree = kvp.Key;
                    var semanticModel = context.Compilation.GetSemanticModel(syntaxTree);
                    var candidates = kvp.Value;
                    var originalToGeneratedPartial = new Dictionary<TypeDeclarationSyntax, TypeDeclarationSyntax>();
                    foreach (var candidate in candidates)
                    {
                        var jobEntityDescription = new JobEntityDescription(candidate, semanticModel, this);
                        if (jobEntityDescription.Valid)
                            originalToGeneratedPartial[candidate] = jobEntityDescription.Generate();
                    }

                    var rootNodes = TypeCreationHelpers.GetReplacedRootNodes(syntaxTree, originalToGeneratedPartial);
                    if (rootNodes.Count == 0)
                        continue;

                    var outputSource = TypeCreationHelpers.GenerateSourceTextForRootNodes(GeneratorName, context, syntaxTree, rootNodes);
                    context.AddSource(syntaxTree.GetGeneratedSourceFileName(GeneratorName), outputSource);
                    OutputNewSourceToFile(context, syntaxTree.GetGeneratedSourceFilePath(context.Compilation.Assembly, GeneratorName), outputSource);
                }
            }
            catch (Exception exception)
            {
                if (exception is OperationCanceledException)
                    throw;

                context.LogError("SGICE003", "IJobEntity Generator", exception.ToString(), context.Compilation.SyntaxTrees.First().GetRoot().GetLocation());
            }
        }

        static void OutputNewSourceToFile(GeneratorExecutionContext generatorExecutionContext, string generatedSourceFilePath, SourceText sourceTextForNewClass)
        {
            if(!SourceGenHelpers.CanWriteToProjectPath)
                return;

            try
            {
                SourceGenHelpers.LogInfo($"Outputting generated source to file {generatedSourceFilePath}...");
                File.WriteAllText(generatedSourceFilePath, sourceTextForNewClass.ToString());
            }
            catch (IOException ioException)
            {
                // Emit exception as info but don't block compilation or generate error to fail tests
                generatorExecutionContext.LogInfo("SGICE005", "JobEntity Generator",
                    ioException.ToString(), generatorExecutionContext.Compilation.SyntaxTrees.First().GetRoot().GetLocation());
            }
        }
    }
}
