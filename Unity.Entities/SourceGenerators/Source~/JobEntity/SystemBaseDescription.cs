using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using static Unity.Entities.SourceGen.IJobEntitySourceGenerator;

namespace Unity.Entities.SourceGen
{
    internal class SystemBaseDescription : ISourceGenerationDescription
    {
        public class EntitiesOnUpdate
        {
            public string JobEntityTypeName;
            public string JobEntityVariableName;
            public IEnumerable<(ExpressionSyntax Left, ExpressionSyntax Right)> FieldAssignmentsInJobEntity;

            public bool HasFieldAssignmentsDuringJobEntityCreation =>
                FieldAssignmentsInJobEntity.All(f => f.Left != null && f.Right != null);
        }

        private SemanticModel SemanticModel { get ;  set ; }

        public JobEntityData JobEntityData { get; private set; }
        public EntitiesOnUpdate EntitiesOnUpdateInvocation { get ; private set ; }
        public GeneratorExecutionContext Context { get ; private set ; }
        public MethodDeclarationSyntax OriginalSystemBaseOnUpdateMethod { get ; private set ; }
        public IEnumerable<NamespaceDeclarationSyntax> NamespacesFromMostToLeastNested { get; private set; }
        public ClassDeclarationSyntax DeclaringType { get ; private set ; }

        public static SystemBaseDescription From(SyntaxNode entitiesSyntaxNode, GeneratorExecutionContext context)
        {
            var declaringType = entitiesSyntaxNode.Ancestors().OfType<ClassDeclarationSyntax>().First();

            var updatedSystemBaseType = new SystemBaseDescription
            {
                Context = context,
                SemanticModel = context.Compilation.GetSemanticModel(entitiesSyntaxNode.SyntaxTree),
                DeclaringType = declaringType,
                OriginalSystemBaseOnUpdateMethod = entitiesSyntaxNode.Ancestors().OfType<MethodDeclarationSyntax>().First(),
                NamespacesFromMostToLeastNested = declaringType.GetNamespacesFromMostToLeastNested(),
            };

            var result = entitiesSyntaxNode.FindMemberInvocationWithName("OnUpdate");

            if (!result.Success)
            {
                throw new ArgumentException("Entities.OnUpdate(IJobEntity jobEntity) is not invoked.");
            }

            InvocationExpressionSyntax onUpdateMethodInvocation = result.invocationExpressionSyntax;
            ArgumentSyntax jobEntitySyntax = onUpdateMethodInvocation.ArgumentList.Arguments.Single();
            ISymbol jobEntitySymbol = updatedSystemBaseType.SemanticModel.GetSymbolInfo(jobEntitySyntax.Expression).Symbol;

            updatedSystemBaseType.EntitiesOnUpdateInvocation = new EntitiesOnUpdate
            {
                JobEntityTypeName = jobEntitySymbol.GetSymbolTypeName(),
                JobEntityVariableName = jobEntitySymbol.Name,
                FieldAssignmentsInJobEntity = GetJobEntityFieldAssignments(updatedSystemBaseType, jobEntitySymbol.Name)
            };

            if (!AllJobEntityTypes.TryGetValue(updatedSystemBaseType.EntitiesOnUpdateInvocation.JobEntityTypeName, out JobEntityData jobEntity))
            {
                throw new Exception($"Cannot find any type named {updatedSystemBaseType.EntitiesOnUpdateInvocation.JobEntityTypeName} " +
                                    "implementing the Unity.Entities.IJobEntity interface.");
            }

            updatedSystemBaseType.JobEntityData = jobEntity;

            return updatedSystemBaseType;

            IEnumerable<(ExpressionSyntax Left, ExpressionSyntax Right)>
                GetJobEntityFieldAssignments(SystemBaseDescription onUpdateMethod, string jobEntityVariableName)
            {
                var localDeclarations =
                    onUpdateMethod
                        .OriginalSystemBaseOnUpdateMethod
                        .DescendantNodes()
                        .OfType<LocalDeclarationStatementSyntax>();

                var variableDeclarators =
                    localDeclarations.SelectMany(l => l.DescendantNodes().OfType<VariableDeclaratorSyntax>());

                var jobEntityVariableDeclarator =
                    variableDeclarators.SingleOrDefault(v => v.Identifier.Text == jobEntityVariableName);

                if (jobEntityVariableDeclarator == null)
                {
                    yield return (null, null);
                }
                else
                {
                    var jobEntityCreation = jobEntityVariableDeclarator.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();

                    if (jobEntityCreation?.Initializer == null)
                    {
                        yield return (null, null);
                    }
                    else
                    {
                        foreach (var assignment in jobEntityCreation.Initializer.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                        {
                            yield return (assignment.Left, assignment.Right);
                        }
                    }
                }
            }
        }
    }
}
