using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
{
    public class SyntaxNodeReplacer : CSharpSyntaxRewriter
    {
        readonly Dictionary<SyntaxNode, SyntaxNode> _replacements;

        public SyntaxNodeReplacer(Dictionary<SyntaxNode, SyntaxNode> replacements)
        {
            _replacements = replacements;
        }

        public override SyntaxNode Visit(SyntaxNode node)
        {
            if (node != null && _replacements.TryGetValue(node, out var replacement))
            {
                if (node is StatementSyntax && !(node is BlockSyntax))
                    return replacement.WithLineTrivia(node);
                else
                    return replacement;
            }

            var mewNode = base.Visit(node);
            if (node is StatementSyntax && !(node is BlockSyntax))
                return mewNode.WithLineTrivia(node);
            else
                return mewNode;
        }
    }
}


