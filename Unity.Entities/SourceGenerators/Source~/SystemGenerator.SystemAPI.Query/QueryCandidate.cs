using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGenerator.SystemAPI.Query
{
    public struct QueryCandidate : ISystemCandidate
    {
        public bool IsContainedInForEachStatement => ContainingStatementNode is CommonForEachStatementSyntax;
        public IReadOnlyCollection<TypeSyntax> QueryTypeNodes { get; private set; }
        public StatementSyntax ContainingStatementNode { get; private set; }
        public SyntaxNode FullInvocationChainSyntaxNode { get; private set; }
        public TypeDeclarationSyntax ContainingTypeNode { get; private set; }
        public InvocationExpressionSyntax[] MethodInvocationNodes { get; private set; }

        public static QueryCandidate From(
            InvocationExpressionSyntax queryInvocationSyntaxNode,
            IReadOnlyCollection<TypeSyntax> queryTypeSyntaxNodes)
        {
            var fullInvocationChainSyntaxNode = queryInvocationSyntaxNode.Ancestors().OfType<InvocationExpressionSyntax>().LastOrDefault() ?? queryInvocationSyntaxNode;
            return new QueryCandidate
            {
                ContainingStatementNode = queryInvocationSyntaxNode.Ancestors().OfType<StatementSyntax>().FirstOrDefault(),
                FullInvocationChainSyntaxNode = fullInvocationChainSyntaxNode,
                QueryTypeNodes = queryTypeSyntaxNodes,
                ContainingTypeNode = queryInvocationSyntaxNode.Ancestors().OfType<TypeDeclarationSyntax>().First(),
                MethodInvocationNodes = fullInvocationChainSyntaxNode.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().ToArray()
            };
        }

        public string CandidateTypeName => "SystemAPI.Query";
        public SyntaxNode Node => FullInvocationChainSyntaxNode;
    }
}
