using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGeneratorCommon
{
    public static class EntitiesSourceFactory
    {
        internal static MethodDeclarationSyntax
            OnCreateForCompilerMethod(IEnumerable<string> additionalSyntax,
                string accessModifiers, bool isInISystem)
        {

            var onCreateMethod =
                isInISystem
                    ? $@"public void OnCreateForCompiler(ref SystemState state)
                    {{
                        __AssignHandles(ref state);
                        {additionalSyntax.SeparateByNewLine()}
                    }}"
                    : $@"{accessModifiers} override void OnCreateForCompiler()
                    {{
                        base.OnCreateForCompiler();
                        __AssignHandles(ref this.CheckedStateRef);
                        {additionalSyntax.SeparateByNewLine()}
                    }}";

            return (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(onCreateMethod);
        }
    }
}
