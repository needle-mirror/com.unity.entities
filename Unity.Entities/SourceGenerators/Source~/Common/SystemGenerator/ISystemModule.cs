using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.Entities.SourceGen.SystemGeneratorCommon
{
    public interface ISystemModule
    {
        IEnumerable<(SyntaxNode SyntaxNode, TypeDeclarationSyntax SystemType)> Candidates { get; }
        bool RequiresReferenceToBurst { get; }

        void OnReceiveSyntaxNode(SyntaxNode node);
        bool GenerateSystemType(SystemGeneratorContext systemGeneratorContext);
        bool ShouldRun(ParseOptions parseOptions);
    }
}
