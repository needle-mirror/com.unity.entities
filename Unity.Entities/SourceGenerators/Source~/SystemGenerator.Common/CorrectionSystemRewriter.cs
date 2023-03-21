using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGenerator.Common
{
    /// <summary>
    /// The CorrectionSystemReplacer addresses 3 concerns.
    /// 1. Adding line directives to every statement (needed as other replacers might put in additional lines, which will offset subsequent nodes.
    /// 2. Ensuring valid syntax for block statements who are not in a block e.g. for, foreach etc. (as inserting syntax might otherwise fail).
    /// 3. Replacing non-nested nodes (Temporary while LambdaJobs and JobEntity still doesn't use SystemRewriter)
    /// </summary>
    /// <remarks>
    /// Has to be run before other SystemRewriters,
    /// as line offsetting will be expentionally harder to get as you keep replacing,
    /// this rewriter works on the fact that the System is still yet to be replaced.
    /// </remarks>
    public class CorrectionSystemReplacer : SystemRewriter
    {
        readonly Dictionary<SyntaxNode, SyntaxNode> m_OriginalToReplacementNodes;
        readonly Dictionary<SyntaxNode, SyntaxNode> m_NodesToReplacements;
        public CorrectionSystemReplacer(Dictionary<SyntaxNode,SyntaxNode> descriptionNonNestedReplacementsInMethods)
        {
            m_OriginalToReplacementNodes = descriptionNonNestedReplacementsInMethods;
            m_NodesToReplacements = new Dictionary<SyntaxNode, SyntaxNode>(descriptionNonNestedReplacementsInMethods.Count);
        }

        public override IEnumerable<SyntaxNode> NodesToTrack => m_OriginalToReplacementNodes.Keys;

        public void SetOffsetLineNumber(int offset) => m_OffsetLineNumber = offset;

        public override SyntaxNode VisitTrackedSystem(SyntaxNode systemRootNode, string originalFilePath)
        {
            m_OriginalFilePath = originalFilePath;
            m_OffsetLineNumber -= systemRootNode.GetLineNumber();

            foreach (var replacement in m_OriginalToReplacementNodes)
            {
                // `.GetCurrentNode()` returns the node in the tracked system that corresponds to the original node we want to replace
                var correspondingTrackedNode = systemRootNode.GetCurrentNode(replacement.Key);
                m_NodesToReplacements[correspondingTrackedNode] = replacement.Value;
            }

            return Visit(systemRootNode);
        }

        int m_OffsetLineNumber;

        bool m_ReplacedNode;
        public override SyntaxNode Visit(SyntaxNode node)
        {
            if (node == null)
                return null;

            var newNode = base.Visit(node);
            // If this is a node we want to replace
            if (m_NodesToReplacements.TryGetValue(node, out var replacement))
            {
                if (replacement == null)
                    return null;

                // Ensure that all the annotations made by Roslyn for tracking purposes are preserved
                newNode = node.CopyAnnotationsTo(replacement);
                m_ReplacedNode = true;
            }

            if (newNode is MemberDeclarationSyntax memberDeclarationSyntax && m_ReplacedNode)
            {
                RecordChangedMember(memberDeclarationSyntax);
                m_ReplacedNode = false;
            }
            newNode = newNode is StatementSyntax && !(newNode is BlockSyntax) ? newNode.WithLineTrivia(m_OriginalFilePath, node.GetLineNumber(), m_OffsetLineNumber) : newNode;
            return newNode;
        }

        public override SyntaxNode VisitForStatement(ForStatementSyntax node)
        {
            var replacedUpUntilThisNode = base.VisitForStatement(node) as ForStatementSyntax;

            if (!(node.Statement is BlockSyntax) && replacedUpUntilThisNode != null)
                if (SyntaxFactory.Block(replacedUpUntilThisNode.Statement) is StatementSyntax block)
                    replacedUpUntilThisNode = replacedUpUntilThisNode.WithStatement(block);

            return replacedUpUntilThisNode;
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var replacedUpUntilThisNode = base.VisitMethodDeclaration(node) as MethodDeclarationSyntax;

            if (replacedUpUntilThisNode?.ExpressionBody is { } expressionBody)
            {
                replacedUpUntilThisNode = replacedUpUntilThisNode.WithExpressionBody(null);
                replacedUpUntilThisNode = replacedUpUntilThisNode.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None));
                replacedUpUntilThisNode = replacedUpUntilThisNode.WithBody(
                    replacedUpUntilThisNode.ReturnType is PredefinedTypeSyntax predefinedTypeSyntax && predefinedTypeSyntax.Keyword.IsKind(SyntaxKind.VoidKeyword)
                        ? SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(expressionBody.Expression))
                        : SyntaxFactory.Block(SyntaxFactory.ReturnStatement(expressionBody.Expression)));
            }

            return replacedUpUntilThisNode;
        }

        public override SyntaxNode VisitDoStatement(DoStatementSyntax node)
        {
            var replacedUpUntilThisNode = base.VisitDoStatement(node) as DoStatementSyntax;

            if (!(node.Statement is BlockSyntax) && replacedUpUntilThisNode != null)
                if (SyntaxFactory.Block(replacedUpUntilThisNode.Statement) is StatementSyntax block)
                    replacedUpUntilThisNode = replacedUpUntilThisNode.WithStatement(block);

            return replacedUpUntilThisNode;
        }

        public override SyntaxNode VisitWhileStatement(WhileStatementSyntax node)
        {
            var replacedUpUntilThisNode = base.VisitWhileStatement(node) as WhileStatementSyntax;

            if (!(node.Statement is BlockSyntax) && replacedUpUntilThisNode != null)
                if (SyntaxFactory.Block(replacedUpUntilThisNode.Statement) is StatementSyntax block)
                    replacedUpUntilThisNode = replacedUpUntilThisNode.WithStatement(block);

            return replacedUpUntilThisNode;
        }

        public override SyntaxNode VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            var replacedUpUntilThisNode = base.VisitForEachVariableStatement(node) as ForEachVariableStatementSyntax;

            if (!(node.Statement is BlockSyntax) && replacedUpUntilThisNode != null)
                if (SyntaxFactory.Block(replacedUpUntilThisNode.Statement) is StatementSyntax block)
                    replacedUpUntilThisNode = replacedUpUntilThisNode.WithStatement(block);

            return replacedUpUntilThisNode;
        }

        public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
        {
            var replacedUpUntilThisNode = base.VisitForEachStatement(node) as ForEachStatementSyntax;

            if (!(node.Statement is BlockSyntax) && replacedUpUntilThisNode != null)
                if (SyntaxFactory.Block(replacedUpUntilThisNode.Statement) is StatementSyntax block)
                    replacedUpUntilThisNode = replacedUpUntilThisNode.WithStatement(block);

            return replacedUpUntilThisNode;
        }

        public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
        {
            var replacedUpUntilThisNode = base.VisitIfStatement(node) as IfStatementSyntax;

            if (!(node.Statement is BlockSyntax) && replacedUpUntilThisNode != null)
                if (SyntaxFactory.Block(replacedUpUntilThisNode.Statement) is StatementSyntax block)
                    replacedUpUntilThisNode = replacedUpUntilThisNode.WithStatement(block);

            return replacedUpUntilThisNode;
        }

        public override SyntaxNode VisitElseClause(ElseClauseSyntax node)
        {
            var replacedUpUntilThisNode = base.VisitElseClause(node) as ElseClauseSyntax;

            if (!(node.Statement is BlockSyntax) && replacedUpUntilThisNode != null)
                if (SyntaxFactory.Block(replacedUpUntilThisNode.Statement) is StatementSyntax block)
                    replacedUpUntilThisNode = replacedUpUntilThisNode.WithStatement(block);

            return replacedUpUntilThisNode;
        }
    }
}
