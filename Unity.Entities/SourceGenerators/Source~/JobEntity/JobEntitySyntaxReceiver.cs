using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
{
    public class JobEntitySyntaxReceiver : ISyntaxReceiver
    {
        public Dictionary<SyntaxTree, List<StructDeclarationSyntax>> JobCandidatesBySyntaxTree = new Dictionary<SyntaxTree, List<StructDeclarationSyntax>>();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is StructDeclarationSyntax structDeclarationSyntax)
            {
                if (structDeclarationSyntax.BaseList == null)
                    return;

                if (!structDeclarationSyntax.BaseList.Types.Any(baseType => baseType.Type is IdentifierNameSyntax {Identifier: {ValueText: "IJobEntity"}}))
                    return;

                if (!structDeclarationSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                    return;

                JobCandidatesBySyntaxTree.Add(structDeclarationSyntax.SyntaxTree, structDeclarationSyntax);
            }
        }
    }
}
