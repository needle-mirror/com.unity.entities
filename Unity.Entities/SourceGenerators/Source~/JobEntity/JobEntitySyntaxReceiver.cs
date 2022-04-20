using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
{
    public class JobEntitySyntaxReceiver : ISyntaxReceiver
    {
        public Dictionary<SyntaxTree, List<StructDeclarationSyntax>> JobCandidatesBySyntaxTree = new Dictionary<SyntaxTree, List<StructDeclarationSyntax>>();
        readonly CancellationToken _cancelationToken;

        public JobEntitySyntaxReceiver(CancellationToken cancellationToken)
        {
            _cancelationToken = cancellationToken;
        }

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            _cancelationToken.ThrowIfCancellationRequested();

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
