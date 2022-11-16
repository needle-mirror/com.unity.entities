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
                IEnumerable<INonQueryFieldDescription> fieldDescriptions,
                IEnumerable<KeyValuePair<IQueryFieldDescription, string>> entityQueryFieldsToFieldNames,
                string accessModifiers, bool isInISystem)
        {
            var queryFieldsToFieldNames = entityQueryFieldsToFieldNames.ToArray();
            bool useEntityQueryBuilder =
                queryFieldsToFieldNames.Any(kvp => kvp.Key is MultipleArchetypeQueryFieldDescription);

            var onCreateMethod =
                isInISystem
                    ? $@"public void OnCreateForCompiler(ref SystemState state)
                    {{
                        {"var entityQueryBuilder = new Unity.Entities.EntityQueryBuilder(Unity.Collections.Allocator.Temp);".EmitIfTrue(useEntityQueryBuilder)}
                        {queryFieldsToFieldNames.Select(kvp =>
                            kvp.Key.EntityQueryFieldAssignment("state", kvp.Value)).SeparateByNewLine()}
                        {"entityQueryBuilder.Dispose();".EmitIfTrue(useEntityQueryBuilder)}

                        {fieldDescriptions.Select(field => field.GetFieldAssignment("state")).SeparateByNewLine()}
                        {additionalSyntax.SeparateByNewLine()}
                    }}"
                    : $@"{accessModifiers} override void OnCreateForCompiler()
                    {{
                        base.OnCreateForCompiler();

                        {"var entityQueryBuilder = new Unity.Entities.EntityQueryBuilder(Unity.Collections.Allocator.Temp);".EmitIfTrue(useEntityQueryBuilder)}
                        {queryFieldsToFieldNames.Select(kvp =>
                            kvp.Key.EntityQueryFieldAssignment("this.CheckedStateRef", kvp.Value)).SeparateByNewLine()}
                        {"entityQueryBuilder.Dispose();".EmitIfTrue(useEntityQueryBuilder)}

                        {fieldDescriptions.Select(field => field.GetFieldAssignment("this.CheckedStateRef")).SeparateByNewLine()}
                        {additionalSyntax.SeparateByNewLine()}
                    }}";

            return (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(onCreateMethod);
        }
    }
}
