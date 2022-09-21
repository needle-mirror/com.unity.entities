using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.SystemGenerator;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.Common
{
    public interface ISystemCandidate
    {
        public string CandidateTypeName { get; }
        public SyntaxNode Node { get; }
    }

    public static class NodeContainerExtensions
    {
        public static (bool Success, string SystemStateName) TryGetSystemStateParameterName<T>(this SystemDescription desc, T candidate) where T : ISystemCandidate
        {
            if (desc.SystemType == SystemType.ISystem) {
                var methodDeclarationSyntax = candidate.Node.AncestorOfKindOrDefault<MethodDeclarationSyntax>();
                if (methodDeclarationSyntax == null) {
                    SystemGeneratorErrors.SGSG0001(desc, candidate);
                    return (false, "");
                }
                var containingMethodSymbol = desc.SemanticModel.GetDeclaredSymbol(methodDeclarationSyntax);
                var systemStateParameterName = containingMethodSymbol.GetFirstOrDefaultParameterNameOfType("Unity.Entities.SystemState");
                if (systemStateParameterName != null)
                    return (true, systemStateParameterName);

                SystemGeneratorErrors.SGSG0002(desc, candidate);
                return (false, "");
            }

            return (true, "this.CheckedStateRef");
        }
    }
}
