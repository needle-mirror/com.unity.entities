using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.Entities.SourceGen.SystemGenerator.Common
{
    public interface ISystemModule
    {
        IEnumerable<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)> Candidates { get; }
        bool RequiresReferenceToBurst { get; }

        void OnReceiveSyntaxNode(SyntaxNode node);
        bool RegisterChangesInSystem(SystemDescription systemDescription);
    }
}
