using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGenerator.Common
{
    public static class EntitiesSourceFactory
    {
        internal static MethodDeclarationSyntax
            OnCreateForCompilerMethod(IEnumerable<string> additionalSyntax, bool isInISystem)
        {

            var onCreateMethod =
                isInISystem
                    ? $@"public void OnCreateForCompiler(ref SystemState state)
                    {{
                        __AssignQueries(ref state);
                        __TypeHandle.__AssignHandles(ref state);
                        {additionalSyntax.SeparateByNewLine()}
                    }}"
                    : $@"protected override void OnCreateForCompiler()
                    {{
                        base.OnCreateForCompiler();
                        __AssignQueries(ref this.CheckedStateRef);
                        __TypeHandle.__AssignHandles(ref this.CheckedStateRef);
                        {additionalSyntax.SeparateByNewLine()}
                    }}";

            return (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(onCreateMethod);
        }

        internal static MethodDeclarationSyntax OnCreateForCompilerStub()
            => (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration("public void OnCreateForCompiler(ref SystemState state){}");
    }
}
