using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        static readonly string GeneratorName = "JobEntity";
        public List<Diagnostic> Diagnostics { get; }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new JobEntitySyntaxReceiver(context.CancellationToken));
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (!SourceGenHelpers.IsBuildTime && !SourceGenHelpers.ShouldRun(context))
                return;
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
                        if (!jobEntityDescription.Invalid)
                            originalToGeneratedPartial[candidate] = jobEntityDescription.Generate();
                    }

                    var rootNodes = TypeCreationHelpers.GetReplacedRootNodes(syntaxTree, originalToGeneratedPartial);
                    if (rootNodes.Count == 0)
                        continue;

                    var outputSource = TypeCreationHelpers.GenerateSourceTextForRootNodes(GeneratorName, context, syntaxTree, rootNodes);
                    context.AddSource(syntaxTree.GetGeneratedSourceFileName(GeneratorName), outputSource);
                    SourceGenHelpers.OutputSourceToFile(context, syntaxTree.GetGeneratedSourceFilePath(context.Compilation.Assembly, GeneratorName), outputSource);
                }
            }
            catch (Exception exception)
            {
                if (exception is OperationCanceledException)
                    throw;

                context.LogError("SGICE003", "IJobEntity Generator", exception.ToUnityPrintableString(), context.Compilation.SyntaxTrees.First().GetRoot().GetLocation());
            }
        }
    }
}
