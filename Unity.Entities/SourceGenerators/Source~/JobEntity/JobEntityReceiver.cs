using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
{
    public class IJobEntityReceiver : ISyntaxReceiver
    {
        private readonly List<SyntaxNode> _entitiesGetterCandidates = new List<SyntaxNode>();
        private readonly List<(SyntaxNode TypeNode, SyntaxNode OnUpdateMethodNode)> _iJobEntityTypeCandidates =
            new List<(SyntaxNode TypeNode, SyntaxNode OnUpdateMethodNode)>();


        internal IEnumerable<(SyntaxNode TypeNode, SyntaxNode OnUpdateMethodNode)> JobEntityTypeCandidates => _iJobEntityTypeCandidates;
        internal IEnumerable<SyntaxNode> EntitiesGetterCandidates => _entitiesGetterCandidates;

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is StructDeclarationSyntax structDeclarationSyntax)
            {
                var onUpdateMethod = OnUpdateMethod(structDeclarationSyntax);

                if (onUpdateMethod.Exists)
                {
                    _iJobEntityTypeCandidates.Add((TypeNode: syntaxNode, OnUpdateMethodNode: onUpdateMethod.SyntaxNode));
                }
            }
            else if (
                syntaxNode is IdentifierNameSyntax ins
                && ins.Identifier.Text == "Entities"
                && syntaxNode.GetMethodInvocations().ContainsKey("OnUpdate"))
            {
                _entitiesGetterCandidates.Add(syntaxNode);
            }
        }

        private static (bool Exists, MethodDeclarationSyntax SyntaxNode) OnUpdateMethod(StructDeclarationSyntax tds)
        {
            MethodDeclarationSyntax onUpdateMethod =
                tds.ChildNodes()
                   .OfType<MethodDeclarationSyntax>()
                   .SingleOrDefault(method => method.Identifier.Text == "OnUpdate");

            return (onUpdateMethod != null, onUpdateMethod);
        }
    }
}
