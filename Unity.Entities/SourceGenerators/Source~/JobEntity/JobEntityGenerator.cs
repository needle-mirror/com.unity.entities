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
            context.RegisterForSyntaxNotifications(() => new JobEntitySyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var systemReceiver = (JobEntitySyntaxReceiver)context.SyntaxReceiver;

            foreach (var kvp in systemReceiver.JobCandidatesBySyntaxTree)
            {
                var syntaxTree = kvp.Key;
                var semanticModel = context.Compilation.GetSemanticModel(syntaxTree);
                var candidates = kvp.Value;
                var generatedStructs = candidates
                    .Select(candidate => new JobEntityDescription(candidate, semanticModel, this))
                    .Where(jobEntityDescription => jobEntityDescription.Valid)
                    .Select(jobEntityDescription => jobEntityDescription.Generate());

                var outputSource = GenerateSourceTextForSyntaxTree(context, syntaxTree, generatedStructs);
                OutputNewSourceToCompilation(context, syntaxTree.GetGeneratedSourceFileName(GeneratorName), outputSource);
                OutputNewSourceToFile(context, syntaxTree.GetGeneratedSourceFilePath(context.Compilation.Assembly, GeneratorName), outputSource);
            }
        }

        const string GeneratedLineTriviaToGeneratedSource = "// __generatedline__";

        static SourceText GenerateSourceTextForSyntaxTree(
            GeneratorExecutionContext generatorExecutionContext,
            SyntaxTree syntaxTree,
            IEnumerable<MemberDeclarationSyntax> generatedRootNodes,
            params string[] additionalUsings)
        {
            // Create compilation unit
            var existingUsings =
                syntaxTree
                    .GetCompilationUnitRoot(generatorExecutionContext.CancellationToken)
                    .WithoutPreprocessorTrivia().Usings;

            var compilationUnit =
                CompilationUnit()
                    .AddMembers(generatedRootNodes.ToArray())
                    .WithoutPreprocessorTrivia()
                    .WithUsings(existingUsings.AddUsingStatements(additionalUsings.ToArray()))
                    .NormalizeWhitespace();

            var generatedSourceFilePath = syntaxTree.GetGeneratedSourceFilePath(generatorExecutionContext.Compilation.Assembly, "JobEntity");

            // Output as source
            var sourceTextForNewClass =
                compilationUnit.GetText(Encoding.UTF8)
                    .WithInitialLineDirectiveToGeneratedSource(generatedSourceFilePath)
                    .WithIgnoreUnassignedVariableWarning();

            var textChanges = new List<TextChange>();
            foreach (var line in sourceTextForNewClass.Lines)
            {
                var lineText = line.ToString();
                if (lineText.Contains(GeneratedLineTriviaToGeneratedSource))
                {
                    textChanges.Add(new TextChange(line.Span,
                        lineText.Replace(GeneratedLineTriviaToGeneratedSource, $"#line {line.LineNumber + 2} \"{generatedSourceFilePath}\"")));
                }
                else if (lineText.Contains("#line") && lineText.TrimStart().IndexOf("#line") != 0)
                {
                    var indexOfLineDirective = lineText.IndexOf("#line");
                    textChanges.Add(new TextChange(line.Span,
                        lineText.Substring(0, indexOfLineDirective - 1) + Environment.NewLine +
                        lineText.Substring(indexOfLineDirective)));
                }
            }

            return sourceTextForNewClass.WithChanges(textChanges);
        }

        static void OutputNewSourceToFile(GeneratorExecutionContext generatorExecutionContext, string generatedSourceFilePath, SourceText sourceTextForNewClass)
        {
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

        static void OutputNewSourceToCompilation(GeneratorExecutionContext generatorExecutionContext, string generatedSourceFileName, SourceText sourceTextForNewClass)
        {
            generatorExecutionContext.AddSource(generatedSourceFileName, sourceTextForNewClass);
        }
    }
}
