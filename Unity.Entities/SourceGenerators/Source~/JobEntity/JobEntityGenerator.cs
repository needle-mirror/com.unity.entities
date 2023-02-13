using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

/*
    This is the JobEntityGenerator for the IJobEntity feature.
    Please read the comment in JobEntityModule for an overview of how these two generators interacts to make this feature work.
*/

namespace Unity.Entities.SourceGen.JobEntity
{
    [Generator]
    public class JobEntityGenerator : IIncrementalGenerator, ISourceGeneratorDiagnosable
    {
        public static readonly string GeneratorName = "JobEntity";
        public List<Diagnostic> Diagnostics { get; }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var projectPathProvider = SourceGenHelpers.GetProjectPathProvider(context);

            // Do a simple filter for enums
            var candidateProvider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: JobEntitySyntaxFinder.IsSyntaxTargetForGeneration,
                    transform: JobEntitySyntaxFinder.GetSemanticTargetForGeneration)
                .Where(t => t is {});

            var compilationProvider = context.CompilationProvider.WithComparer(new CompilationComparer());
            var combined = candidateProvider.Combine(compilationProvider).Combine(projectPathProvider);
            context.RegisterSourceOutput(combined, (productionContext, source) =>
                Execute(productionContext,
                    source.Left.Right, source.Left.Left,
                    source.Right.projectPath, source.Right.performSafetyChecks, source.Right.outputSourceGenFiles));
        }

        class CompilationComparer : IEqualityComparer<Compilation>
        {
            public bool Equals(Compilation x, Compilation y)
                => y != null && x != null && x.AssemblyName == y.AssemblyName;

            public int GetHashCode(Compilation obj)
                => obj.AssemblyName == null ? 0 : obj.AssemblyName.GetHashCode();
        }

        void Execute(SourceProductionContext context, Compilation compilation, StructDeclarationSyntax candidate, string projectPath, bool performSafetyChecks, bool outputSourceGenFiles)
        {
            if (!SourceGenHelpers.ShouldRun(compilation, context.CancellationToken))
                return;

            try
            {
                SourceGenHelpers.ProjectPath = projectPath;
                context.CancellationToken.ThrowIfCancellationRequested();
                var syntaxTree = candidate.SyntaxTree;
                var semanticModel = compilation.GetSemanticModel(syntaxTree);
                var jobEntityDescription = new JobEntityDescription(candidate, semanticModel, performSafetyChecks, this);
                if (jobEntityDescription.Invalid)
                    return;
                var generatedJobEntity = jobEntityDescription.Generate();
                var sourceFilePath = syntaxTree.GetGeneratedSourceFilePath(compilation.Assembly.Name, GeneratorName);
                var outputSource = TypeCreationHelpers.GenerateSourceTextForRootNodes(sourceFilePath, candidate, generatedJobEntity, context.CancellationToken);
                context.AddSource(syntaxTree.GetGeneratedSourceFileName(GeneratorName, candidate), outputSource);
                if (outputSourceGenFiles)
                    SourceGenHelpers.OutputSourceToFile(context, candidate.GetLocation(), sourceFilePath, outputSource);
            }
            catch (Exception exception)
            {
                if (exception is OperationCanceledException)
                    throw;
                context.ReportDiagnostic(
                    Diagnostic.Create(JobEntityDiagnostics.SGICE003Descriptor,
                        compilation.SyntaxTrees.First().GetRoot().GetLocation(),
                        exception.ToUnityPrintableString()));
            }
        }
    }

    public static class JobEntityDiagnostics
    {
        public const string ID_SGICE003 = "SGICE003";
        public static readonly DiagnosticDescriptor SGICE003Descriptor
            = new DiagnosticDescriptor(ID_SGICE003, "IJobEntity Generator",
                "This error indicates a bug in the DOTS source generators. We'd appreciate a bug report (Help -> Report a Bug...). Thanks! Error message: '{0}'.",
                JobEntityGenerator.GeneratorName, DiagnosticSeverity.Error, isEnabledByDefault: true, description: "");
    }
}
