using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.AuthoringComponent
{
    public class AuthoringComponentReceiver : ISyntaxReceiver
    {
        readonly List<SyntaxNode> _candidateSyntaxes = new List<SyntaxNode>();
        readonly CancellationToken _cancellationToken;

        public AuthoringComponentReceiver(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
        }

        public IEnumerable<SyntaxNode> CandidateSyntaxes => _candidateSyntaxes;

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (syntaxNode is TypeDeclarationSyntax typeDeclarationSyntax && IsCandidate(typeDeclarationSyntax))
            {
                _candidateSyntaxes.Add(syntaxNode);
            }
        }

        static bool IsCandidate(TypeDeclarationSyntax typeDeclarationSyntax)
        {
            return typeDeclarationSyntax.HasAttribute("GenerateAuthoringComponent");
        }
    }
}
