using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Microsoft.CodeAnalysis.Text;
using System.IO;
using Unity.Entities.SourceGen.Common;
using static Unity.Entities.SourceGen.Common.SourceGenHelpers;

namespace Unity.Entities.SourceGen
{
    [Generator]
    public class IJobEntitySourceGenerator : ISourceGenerator
    {
        internal struct JobEntityData
        {
            internal JobEntityDescription UserWrittenJobEntity;
            internal StructDeclarationSyntax GeneratedIJobEntityBatchType;
        }

        internal static readonly Dictionary<string, JobEntityData> AllJobEntityTypes = new Dictionary<string, JobEntityData>();

        public void Execute(GeneratorExecutionContext sourceGeneratorContext)
        {
            try
            {
                var iJobEntityReceiver = (IJobEntityReceiver)sourceGeneratorContext.SyntaxReceiver;

                if (!iJobEntityReceiver.JobEntityTypeCandidates.Any())
                {
                    return;
                }

                var syntaxTreeToCandidates =
                        iJobEntityReceiver
                            .JobEntityTypeCandidates
                            .GroupBy(j => j.TypeNode.SyntaxTree)
                            .ToDictionary(group => group.Key, group => group.ToArray());

                foreach (var kvp in syntaxTreeToCandidates)
                {
                    var syntaxTree = kvp.Key;
                    var candidates = kvp.Value;

                    try
                    {
                        foreach (var (typeNode, onUpdateMethodNode) in candidates)
                        {
                            SemanticModel semanticModel = sourceGeneratorContext.Compilation.GetSemanticModel(typeNode.SyntaxTree);
                            ITypeSymbol candidateTypeSymbol = (ITypeSymbol)semanticModel.GetDeclaredSymbol(typeNode);

                            if (!candidateTypeSymbol.InheritsFromInterface("Unity.Entities.IJobEntity"))
                            {
                                continue;
                            }

                            var jobEntityDescription =
                                new JobEntityDescription(typeNode, onUpdateMethodNode, candidateTypeSymbol, sourceGeneratorContext);

                            if (jobEntityDescription.FieldDescriptions.Any(f => !f.IsValueType))
                            {
                                throw new ArgumentException("IJobEntity types may only contain value-type fields.");
                            }

                            var jobEntityData = new JobEntityData
                            {
                                UserWrittenJobEntity = jobEntityDescription,
                                GeneratedIJobEntityBatchType = JobEntityBatchTypeGenerator.GenerateFrom(jobEntityDescription),
                            };

                            AllJobEntityTypes[jobEntityDescription.DeclaringTypeFullyQualifiedName] = jobEntityData;

                            AddJobEntityBatchSourceToContext(jobEntityData, sourceGeneratorContext);
                        }
                    }
                    catch (Exception exception)
                    {
                        sourceGeneratorContext.LogError("SGICE001", "JobEntity", exception.ToString(), syntaxTree.GetRoot().GetLocation());
                    }
                }

                SyntaxNode[] entitiesInSystemBaseDerivedTypes =
                    GetEntitiesInSystemBaseDerivedTypes(
                        sourceGeneratorContext, iJobEntityReceiver.EntitiesGetterCandidates).ToArray();

                if (!entitiesInSystemBaseDerivedTypes.Any())
                {
                    return;
                }

                var syntaxTreesToEntities =
                    entitiesInSystemBaseDerivedTypes
                        .GroupBy(e => e.SyntaxTree)
                        .ToDictionary(group => group.Key, group => group.ToArray());

                foreach (var kvp in syntaxTreesToEntities)
                {
                    var syntaxTree = kvp.Key;
                    var entityNodes = kvp.Value;

                    try
                    {
                        foreach (SyntaxNode entityNode in entityNodes)
                        {
                            var systemBaseDescription = SystemBaseDescription.From(entityNode, sourceGeneratorContext);
                            var updatedSystemBaseDescription = UpdatedSystemBaseTypeGenerator.GenerateFrom(systemBaseDescription);

                            AddUpdatedSystemBaseSourceToContext(
                                systemBaseDescription,
                                updatedSystemBaseDescription,
                                sourceGeneratorContext);
                        }
                    }
                    catch (Exception exception)
                    {
                        sourceGeneratorContext.LogError("SGICE001", "JobEntity", exception.ToString(), syntaxTree.GetRoot().GetLocation());
                    }
                }
            }
            catch (Exception exception)
            {
                sourceGeneratorContext.LogError(
                    "SG0002",
                    "Unknown Exception",
                    exception.ToString(),
                    sourceGeneratorContext.Compilation.SyntaxTrees.First().GetRoot().GetLocation());
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new IJobEntityReceiver());
        }

        private static void AddUpdatedSystemBaseSourceToContext(
            SystemBaseDescription systemBaseDescription,
            ClassDeclarationSyntax updatedSystemBaseType,
            in GeneratorExecutionContext sourceGeneratorContext)
        {
            CompilationUnitSyntax compilationUnit =
                CompilationUnit()
                    .AddMembers(updatedSystemBaseType
                        .AddNamespaces(systemBaseDescription.NamespacesFromMostToLeastNested)
                        .WithAttributeLists(
                            AttributeListFromAttributeName("System.Runtime.CompilerServices.CompilerGeneratedAttribute")))
                    .AddUsings(
                        UsingDirective(
                            ParseName("Unity.Collections.LowLevel.Unsafe")),
                        UsingDirective(
                            ParseName("Unity.Collections")),
                        UsingDirective(
                            ParseName("Unity.Entities")))
                    .NormalizeWhitespace();

            SourceText sourceText = compilationUnit.GetText(Encoding.UTF8);
            var generatedSourceHint = $"{systemBaseDescription.DeclaringType.Identifier.Text}.g.cs";
            string generatedSourceFilePath = $"{GetTempGeneratedPathToFile(systemBaseDescription.DeclaringType.Identifier.Text)}.g.cs";
            sourceText = sourceText.WithInitialLineDirectiveToGeneratedSource(generatedSourceFilePath);
            sourceGeneratorContext.AddSource(generatedSourceHint, sourceText);

            File.WriteAllLines(
                generatedSourceFilePath,
                sourceText.ToString().Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None));
        }

        private static void AddJobEntityBatchSourceToContext(JobEntityData jobEntityData, GeneratorExecutionContext sourceGeneratorContext)
        {
            CompilationUnitSyntax compilationUnit =
                CompilationUnit()
                    .AddMembers(jobEntityData.GeneratedIJobEntityBatchType
                        .AddNamespaces(jobEntityData.UserWrittenJobEntity.NamespacesFromMostToLeastNested)
                        .WithAttributeLists(
                            AttributeListFromAttributeName("System.Runtime.CompilerServices.CompilerGeneratedAttribute")))
                    .AddUsings(
                        UsingDirective(
                            ParseName("Unity.Collections.LowLevel.Unsafe")))
                    .NormalizeWhitespace();

            var generatedSourceHint = $"{jobEntityData.UserWrittenJobEntity.DeclaringTypeName}.g.cs";
            string generatedSourceFullPath = $"{GetTempGeneratedPathToFile(jobEntityData.UserWrittenJobEntity.DeclaringTypeName)}.g.cs";

            SourceText sourceTextForNewClass = compilationUnit.GetText(Encoding.UTF8);

            sourceGeneratorContext.AddSource(generatedSourceHint, sourceTextForNewClass);

            File.WriteAllLines(
                generatedSourceFullPath,
                sourceTextForNewClass.ToString().Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None));
        }

        private static IEnumerable<SyntaxNode> GetEntitiesInSystemBaseDerivedTypes(
            GeneratorExecutionContext context, IEnumerable<SyntaxNode> entitiesCandidates)
        {
            return entitiesCandidates.Where(candidate =>
            {
                var model = context.Compilation.GetSemanticModel(candidate.SyntaxTree);
                var containingTypeSymbol = model.GetSymbolInfo(candidate).Symbol?.ContainingType;

                return containingTypeSymbol != null && containingTypeSymbol.Is("Unity.Entities.SystemBase");
            });
        }
    }
}
