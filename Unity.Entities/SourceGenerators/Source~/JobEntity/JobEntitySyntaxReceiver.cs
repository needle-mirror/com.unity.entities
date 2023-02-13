using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
{
    public static class JobEntitySyntaxFinder
    {
        public static bool IsSyntaxTargetForGeneration(SyntaxNode syntaxNode, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Is Struct
            if (syntaxNode is StructDeclarationSyntax structDeclarationSyntax)
            {
                // Has Base List
                if (structDeclarationSyntax.BaseList == null)
                    return false;

                // Has IJobEntity identifier
                var hasIJobEntityIdentifier = false;
                foreach (var baseType in structDeclarationSyntax.BaseList.Types)
                    if (baseType.Type is IdentifierNameSyntax { Identifier: { ValueText: "IJobEntity" } })
                    {
                        hasIJobEntityIdentifier = true;
                        break;
                    }
                if (!hasIJobEntityIdentifier)
                    return false;

                // Has Partial keyword
                var hasPartial = false;
                foreach (var m in structDeclarationSyntax.Modifiers)
                    if (m.IsKind(SyntaxKind.PartialKeyword))
                    {
                        hasPartial = true;
                        break;
                    }

                return hasPartial;
            }
            return false;
        }

        public static StructDeclarationSyntax GetSemanticTargetForGeneration(GeneratorSyntaxContext ctx, CancellationToken cancellationToken)
        {
            var structDeclarationSyntax = (StructDeclarationSyntax)ctx.Node;
            foreach (var baseTypeSyntax in structDeclarationSyntax.BaseList!.Types)
                if (ctx.SemanticModel.GetTypeInfo(baseTypeSyntax.Type).Type.ToFullName() == "global::Unity.Entities.IJobEntity")
                    return structDeclarationSyntax;
            return null;
        }
    }
}
