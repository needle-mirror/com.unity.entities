using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.SystemGenerator;
using Unity.Entities.SourceGen.SystemGenerator.Common;

namespace Unity.Entities.SourceGen.Common
{
    public interface ISystemCandidate
    {
        public string CandidateTypeName { get; }
        public SyntaxNode Node { get; }
    }

    public interface IAdditionalHandlesInfo
    {
        SemanticModel SemanticModel { get; }
        SystemType SystemType { get; }
        TypeDeclarationSyntax TypeSyntax { get; }
    }

    public static class NodeContainerExtensions
    {
        public static bool TryGetSystemStateParameterName<T1, T2>(this T1 desc, T2 candidate, out ExpressionSyntax systemStateExpression) where T1 : ISourceGeneratorDiagnosable, IAdditionalHandlesInfo where T2 : ISystemCandidate
        {
            switch (desc.SystemType)
            {
                case SystemType.ISystem:
                {
                    var methodDeclarationSyntax = candidate.Node.AncestorOfKindOrDefault<MethodDeclarationSyntax>();
                    if (methodDeclarationSyntax == null) {
                        SystemGeneratorErrors.SGSG0001(desc, candidate);
                        systemStateExpression = null;
                        return false;
                    }
                    var containingMethodSymbol = desc.SemanticModel.GetDeclaredSymbol(methodDeclarationSyntax);
                    var systemStateParameterName = containingMethodSymbol?.Parameters.FirstOrDefault(p => p.Type.Is("Unity.Entities.SystemState"))?.Name;
                    if (systemStateParameterName != null)
                    {
                        systemStateExpression = SyntaxFactory.IdentifierName(systemStateParameterName);
                        return true;
                    }

                    SystemGeneratorErrors.SGSG0002(desc, candidate);
                    systemStateExpression = null;
                    return false;
                }
                case SystemType.Unknown:
                    systemStateExpression = SyntaxFactory.IdentifierName("state");
                    return true;
            }

            // this.CheckedStateRef
            systemStateExpression = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.ThisExpression(), SyntaxFactory.IdentifierName("CheckedStateRef"));
            return true;
        }
    }
}
