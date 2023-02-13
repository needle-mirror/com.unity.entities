using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGeneratorCommon
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

        public string GetOrCreateQueryField(IQueryFieldDescription queryFieldDescription)
        {
            if (QueryFieldsToFieldNames.TryGetValue(queryFieldDescription, out string matchingFieldName))
                return matchingFieldName;

            var generatedName = $"__query_{UniqueId}_{QueryFieldsToFieldNames.Count}";
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
            var entityTypeHandleFieldDescription = EntityTypeHandleFieldDescription.CreateInstance();
            NonQueryFields.Add(entityTypeHandleFieldDescription);

            return EntityTypeHandleFieldDescription.GeneratedFieldName;
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

        public string GetOrCreateBufferLookupField(ITypeSymbol typeSymbol, bool isReadOnly)
        {
            var bufferLookupField = new BufferLookupFieldDescription(typeSymbol, isReadOnly);
            NonQueryFields.Add(bufferLookupField);

            return bufferLookupField.GeneratedFieldName;
        }

        public string GetOrCreateEntityStorageInfoLookupField()
        {
            var storageLookupField = new EntityStorageInfoLookupFieldDescription();
            storageLookupField.Init();
            NonQueryFields.Add(storageLookupField);

            return storageLookupField.GeneratedFieldName;
        }

        // Expects the first handle description to be the one where the TypeHandle is generated.
        // By the point this is called all HandleDescriptions should have been filled out!
        public static string GetTypeHandleForInitialPartial(bool shouldHaveUpdate, bool injectable, int currentIndex, params HandlesDescription[] handleDescriptions)
        {
            if (handleDescriptions.Length == 0)
                throw new InvalidOperationException("Didn't pass in any handle Descriptions");

            // Should only generate a typehandle for the first one in handle description.
            if (currentIndex > 0)
                return "";

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
            var constructor = $@"{(injectable ? "private void __AssignHandles" : "public TypeHandle")}(ref SystemState state)
    {{
        {"var entityQueryBuilder = new global::Unity.Entities.EntityQueryBuilder(global::Unity.Collections.Allocator.Temp);".EmitIfTrue(useEntityQueryBuilder)}
        {allQueryFieldsToGenerate.Select(kvp => kvp.Key.EntityQueryFieldAssignment("state", kvp.Value)).SeparateByNewLine()}
        {"entityQueryBuilder.Dispose();".EmitIfTrue(useEntityQueryBuilder)}
        {nonQueryFields.Select(field => field.GetFieldAssignment("state")).SeparateByNewLine()}
    }}";

            var update = shouldHaveUpdate ? $@"public void Update(ref global::Unity.Entities.SystemState state) {{
        {string.Join("        ", nonQueryFields.Select(f=> $"{f.FieldDeclaration.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText}.Update(ref state);"))}
    }}" : "";

            return @$"public partial struct TypeHandle {{
    {nonQueryFields.Select(f => f.FieldDeclaration.ToString()).SeparateByNewLine()}
    {allQueryFieldsToGenerate.Select(f=>f.Key.GetFieldDeclarationSyntax(f.Value).ToString()).SeparateByNewLine()}
    {constructor}
    {update}
}}";
        }
    }
}
