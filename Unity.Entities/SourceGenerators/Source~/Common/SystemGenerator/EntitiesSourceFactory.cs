using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGeneratorCommon
{
    public static class EntitiesSourceFactory
    {
        internal static MemberDeclarationSyntax OnCreateForCompilerMethod(IEnumerable<string> additionalSyntax,
            IEnumerable<ComponentTypeHandleFieldDescription> componentTypeHandleFieldDescriptions, IEnumerable<EntityQueryField> entityQueryFields,
            string accessModifiers, bool isInISystem)
        {
            string onCreateMethod;

            if (isInISystem)
            {
                onCreateMethod = $@"public void OnCreateForCompiler(ref SystemState systemState)
                    {{
                        {entityQueryFields.Select(field => EntityQueryFieldAssignment(field, true)).SeparateByNewLine()}
                        {componentTypeHandleFieldDescriptions.Select(field => field.FieldAssignment).SeparateBySemicolonAndNewLine()}
                        {additionalSyntax.SeparateByNewLine()}
                    }}";
            }
            else
            {
                onCreateMethod = $@"{accessModifiers} override void OnCreateForCompiler()
                    {{
                        base.OnCreateForCompiler();
                        {entityQueryFields.Select(field => EntityQueryFieldAssignment(field, false)).SeparateByNewLine()}
                        {componentTypeHandleFieldDescriptions.Select(field => field.FieldAssignment).SeparateBySemicolonAndNewLine()}
                        {additionalSyntax.SeparateByNewLine()}
                    }}";
            }

            return SyntaxFactory.ParseMemberDeclaration(onCreateMethod);
        }

        static string EntityQueryFieldAssignment(EntityQueryField field, bool isInISystem)
        {
            var entityQuerySetup =
                $@"{field.FieldName} = {"systemState.".EmitIfTrue(isInISystem)}GetEntityQuery(
                    new Unity.Entities.EntityQueryDesc
                    {{
                        All = new Unity.Entities.ComponentType[] {{
                            {DistinctQueryTypesFor(field.QueryDescription).Distinct().SeparateByCommaAndNewLine()}
                        }},
                        Any = new Unity.Entities.ComponentType[] {{
                            {field.QueryDescription.Any.Select(GetQueryTypeSnippet).Distinct().SeparateByCommaAndNewLine()}
                        }},
                        None = new Unity.Entities.ComponentType[] {{
                            {field.QueryDescription.None.Select(GetQueryTypeSnippet).Distinct().SeparateByCommaAndNewLine()}
                        }},
                        Options = {field.QueryDescription.Options.GetFlags().Select(flag => $"Unity.Entities.EntityQueryOptions.{flag.ToString()}").SeparateByBinaryOr()}
                    }});";

            if (field.QueryDescription.StoreInQueryFieldName != null)
                entityQuerySetup = $"{field.QueryDescription.StoreInQueryFieldName} = " + entityQuerySetup;

            if (field.QueryDescription.ChangeFilterTypes.Any())
            {
                entityQuerySetup +=
                    $@"{field.FieldName}.SetChangedVersionFilter(new ComponentType[{field.QueryDescription.ChangeFilterTypes.Length}]
				    {{
                        {field.QueryDescription.ChangeFilterTypes.Select(GetQueryTypeSnippet).SeparateByComma()}
                    }});";
            }

            return entityQuerySetup;
        }

        static string GetQueryTypeSnippet((INamedTypeSymbol typeInfo, bool isReadOnly) componentType)
        {
            return
                componentType.isReadOnly
                    ? $@"Unity.Entities.ComponentType.ReadOnly<{componentType.typeInfo.ToFullName()}>()"
                    : $@"Unity.Entities.ComponentType.ReadWrite<{componentType.typeInfo.ToFullName()}>()";
        }

        static IEnumerable<string> DistinctQueryTypesFor(EntityQueryDescription description)
        {
            var readOnlyTypeNames = new HashSet<string>();
            var readWriteTypeNames = new HashSet<string>();

            void AddQueryType(ITypeSymbol queryType, bool isReadOnly)
            {
                if (queryType == null)
                {
                    return;
                }

                var queryTypeFullName = queryType.ToFullName();

                if (!isReadOnly)
                {
                    readOnlyTypeNames.Remove(queryTypeFullName);
                    readWriteTypeNames.Add(queryTypeFullName);
                }
                else
                {
                    if (!readWriteTypeNames.Contains(queryTypeFullName) &&
                        !readOnlyTypeNames.Contains(queryTypeFullName))
                    {
                        readOnlyTypeNames.Add(queryTypeFullName);
                    }
                }
            }

            foreach (var allComponentType in description.All)
            {
                AddQueryType(allComponentType.typeInfo, allComponentType.isReadOnly);
            }

            foreach (var changeFilterType in description.ChangeFilterTypes)
            {
                AddQueryType(changeFilterType.typeInfo, changeFilterType.isReadOnly);
            }

            return
                readOnlyTypeNames
                    .Select(type => $@"ComponentType.ReadOnly<{type}>()")
                    .Concat(readWriteTypeNames.Select(type => $@"Unity.Entities.ComponentType.ReadWrite<{type}>()"));
        }
    }
}
