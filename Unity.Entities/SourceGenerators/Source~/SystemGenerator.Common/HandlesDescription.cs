using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGenerator.Common
{
    public readonly struct HandlesDescription
    {
        public readonly HashSet<INonQueryFieldDescription> NonQueryFields;
        public readonly Dictionary<IQueryFieldDescription, string> QueryFieldsToFieldNames;
        public readonly int UniqueId;
        HandlesDescription(HashSet<INonQueryFieldDescription> nonQueryFields, Dictionary<IQueryFieldDescription, string> queryFieldsToFieldNames, SyntaxNode owningType)
        {
            NonQueryFields = nonQueryFields;
            QueryFieldsToFieldNames = queryFieldsToFieldNames;
            var systemLineSpan = owningType.GetLocation().GetLineSpan();
            UniqueId = (systemLineSpan.Span.Start.Line + SourceGenHelpers.GetStableHashCode(Path.GetFileName(systemLineSpan.Path))) & 0x7fffffff;
        }

        public static HandlesDescription Create(TypeDeclarationSyntax owningType)
            => new HandlesDescription(
                new HashSet<INonQueryFieldDescription>(),
                new Dictionary<IQueryFieldDescription, string>(), owningType);

        public string GetOrCreateQueryField(IQueryFieldDescription queryFieldDescription, string generatedName = null)
        {
            if (QueryFieldsToFieldNames.TryGetValue(queryFieldDescription, out string matchingFieldName))
                return matchingFieldName;
            generatedName ??= $"__query_{UniqueId}_{QueryFieldsToFieldNames.Count}";
            QueryFieldsToFieldNames.Add(queryFieldDescription, generatedName);
            return generatedName;
        }

        // We cannot call `GetOrCreateTypeHandleField(ITypeSymbol typeSymbol, bool isReadOnly)` when creating type handle
        // fields for source-generated types, because there aren't any type symbols available yet.
        public string GetOrCreateSourceGeneratedTypeHandleField(string containerTypeFullName)
        {
            var description = new ContainerTypeHandleFieldDescription(containerTypeFullName);
            NonQueryFields.Add(description);

            return description.GeneratedFieldName;
        }

        public string GetOrCreateAspectLookup(ITypeSymbol entityTypeLookup, bool isReadOnly)
        {
            var entityTypeLookupField = new AspectLookupFieldDescription(entityTypeLookup, isReadOnly);
            NonQueryFields.Add(entityTypeLookupField);

            return entityTypeLookupField.GeneratedFieldName;
        }

        public string GetOrCreateEntityTypeHandleField()
        {
            var entityTypeHandleFieldDescription = new EntityTypeHandleFieldDescription();
            NonQueryFields.Add(entityTypeHandleFieldDescription);

            return entityTypeHandleFieldDescription.GeneratedFieldName;
        }

        public string GetOrCreateTypeHandleField(ITypeSymbol typeSymbol, bool isReadOnly)
        {
            var typeHandleFieldDescription = new TypeHandleFieldDescription(typeSymbol, isReadOnly);
            NonQueryFields.Add(typeHandleFieldDescription);

            return typeHandleFieldDescription.GeneratedFieldName;
        }

        public string GetOrCreateComponentLookupField(ITypeSymbol typeSymbol, bool isReadOnly)
        {
            var lookupField = new ComponentLookupFieldDescription(typeSymbol, isReadOnly);
            NonQueryFields.Add(lookupField);

            return lookupField.GeneratedFieldName;
        }

        public string GetOrCreateJobEntityHandle(ITypeSymbol typeSymbol, bool assignDefaultQuery)
        {
            var lookupField = new JobEntityQueryAndHandleDescription(typeSymbol, assignDefaultQuery);
            NonQueryFields.Add(lookupField);
            return lookupField.GeneratedFieldName;
        }

        public string GetOrCreateBufferLookupField(ITypeSymbol typeSymbol, bool isReadOnly)
        {
            var bufferLookupField = new BufferLookupFieldDescription(typeSymbol, isReadOnly);
            NonQueryFields.Add(bufferLookupField);

            return bufferLookupField.GeneratedFieldName;
        }

        public string GetOrCreateEntityStorageInfoLookupField()
        {
            var storageLookupField = new EntityStorageInfoLookupFieldDescription();
            NonQueryFields.Add(storageLookupField);

            return storageLookupField.GeneratedFieldName;
        }

        // Expects the first handle description to be the one where the TypeHandle is generated.
        // By the point this is called all HandleDescriptions should have been filled out!
        public static string GetTypeHandleForInitialPartial(bool shouldHaveUpdate, bool fieldsArePublic, string additionalSyntax, params HandlesDescription[] handleDescriptions)
        {
            if (handleDescriptions.Length == 0)
                throw new InvalidOperationException("Didn't pass in any handle Descriptions");

            var allQueryFieldsToGenerate = handleDescriptions.SelectMany(d=> d.QueryFieldsToFieldNames).ToArray();

            // This handles the aliasing of lookups and typehandles
            var nonQueryFields = handleDescriptions[0].NonQueryFields;
            for (var i = 1; i < handleDescriptions.Length; i++)
            {
                var desc = handleDescriptions[i];
                foreach (var nonQueryField in desc.NonQueryFields)
                    nonQueryFields.Add(nonQueryField);
            }

            bool useEntityQueryBuilder = allQueryFieldsToGenerate.Any(kvp => kvp.Key is MultipleArchetypeQueryFieldDescription);

            var nonQueryFieldsStringBuilder = new StringBuilder(nonQueryFields.Count*2);
            foreach (var nonQueryField in nonQueryFields)
            {
                nonQueryFieldsStringBuilder.Append("        ");
                nonQueryFieldsStringBuilder.AppendLine(nonQueryField.GetFieldDeclaration(true));
            }

            var queryFieldsStringBuilder = new StringBuilder(allQueryFieldsToGenerate.Length*2);
            foreach (var kvp in allQueryFieldsToGenerate)
            {
                queryFieldsStringBuilder.Append("    ");
                queryFieldsStringBuilder.AppendLine(kvp.Key.GetFieldDeclaration(kvp.Value, fieldsArePublic));
            }

            var assignQueries = $@"[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    void __AssignQueries(ref global::Unity.Entities.SystemState state)
    {{
        {"var entityQueryBuilder = new global::Unity.Entities.EntityQueryBuilder(global::Unity.Collections.Allocator.Temp);".EmitIfTrue(useEntityQueryBuilder)}
        {allQueryFieldsToGenerate.Select(kvp => kvp.Key.EntityQueryFieldAssignment(kvp.Value)).SeparateByNewLine()}
        {"entityQueryBuilder.Dispose();".EmitIfTrue(useEntityQueryBuilder)}
    }}";

            var assignHandles = $@"[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public void __AssignHandles(ref global::Unity.Entities.SystemState state)
        {{
            {nonQueryFields.Select(field => field.GetFieldAssignment()).SeparateByNewLine()}
        }}";


            var update = shouldHaveUpdate ? $@"public void Update(ref global::Unity.Entities.SystemState state) {{
        {string.Join("\n        ", nonQueryFields.Select(f=> $"{f.GeneratedFieldName}.Update(ref state);"))}
    }}" : "";



            // Create structures.
            return @$"/// <summary> Used internally by the compiler, we won't promise this exists in the future </summary>
public struct InternalCompilerQueryAndHandleData
{{
    {"public ".EmitIfTrue(fieldsArePublic)}TypeHandle __TypeHandle;
{queryFieldsStringBuilder}

    {"public ".EmitIfTrue(fieldsArePublic)}struct TypeHandle
    {{
{nonQueryFieldsStringBuilder}
        {assignHandles}
        {update}
    }}

    {assignQueries}
    {additionalSyntax}
}}";
        }
    }
}
