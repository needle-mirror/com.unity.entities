using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.Entities.SourceGen.Common
{
    public class UntrackedSystemNodeReplacer : CSharpSyntaxRewriter
    {
        readonly Dictionary<SyntaxNode, SyntaxNode> _replacements;

        public UntrackedSystemNodeReplacer(Dictionary<SyntaxNode, SyntaxNode> replacements)
        {
            _replacements = replacements;
        }

        public override SyntaxNode Visit(SyntaxNode node)
        {
            if (node != null && _replacements.TryGetValue(node, out var replacement))
            {
                return replacement;
            }

            return base.Visit(node);
        }
    }
}
