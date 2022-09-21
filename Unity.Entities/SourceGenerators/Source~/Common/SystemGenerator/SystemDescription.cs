using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using static Unity.Entities.SourceGen.Common.SourceGenHelpers;

[assembly:InternalsVisibleTo("SystemGeneratorNew")]

namespace  Unity.Entities.SourceGen.SystemGeneratorCommon
{
    public enum SystemType
    {
        SystemBase,
        ISystem
    }

    public readonly struct SystemDescription : ISourceGeneratorDiagnosable
    {
        public List<Diagnostic> Diagnostics { get; }

        public readonly SystemType SystemType;
        public readonly INamedTypeSymbol SystemTypeSymbol;
        public readonly TypeDeclarationSyntax SystemTypeSyntax;
        public readonly SemanticModel SemanticModel;
        public readonly Compilation Compilation;
        public readonly IReadOnlyCollection<string> PreprocessorSymbolNames;
        public readonly string SystemTypeFullName;
        public readonly SyntaxTreeInfo SyntaxTreeInfo;
        public readonly IReadOnlyCollection<string> FullyQualifiedBaseTypeNames;
        public readonly List<SystemRewriter> Rewriters;
        public readonly Dictionary<SyntaxNode, SyntaxNode> NonNestedReplacementsInMethods;
        public readonly List<MemberDeclarationSyntax> NewMiscellaneousMembers;
        public readonly Dictionary<string, string> FullEcbSystemTypeNamesToGeneratedFieldNames;
        public readonly HashSet<string> AdditionalStatementsInOnCreateForCompilerMethod;
        public readonly HashSet<INonQueryFieldDescription> NonQueryFields;
        public readonly Dictionary<IQueryFieldDescription, string> QueryFieldsToFieldNames;
        public readonly int UniqueId;

        public SystemDescription(
            TypeDeclarationSyntax originalSystemTypeSyntax,
            SystemType systemType,
            INamedTypeSymbol systemTypeSymbol,
            SemanticModel semanticModel,
            Compilation compilation,
            IEnumerable<string> preprocessorSymbolNames,
            SyntaxTreeInfo syntaxTreeInfo)
        {
            SystemTypeSyntax = originalSystemTypeSyntax;
            SemanticModel = semanticModel;
            PreprocessorSymbolNames = preprocessorSymbolNames.ToArray();
            SystemTypeSymbol = systemTypeSymbol;
            SystemType = systemType;
            SystemTypeFullName = SystemTypeSymbol.ToFullName();
            SyntaxTreeInfo = syntaxTreeInfo;
            Compilation = compilation;
            FullyQualifiedBaseTypeNames = SystemTypeSymbol.GetAllFullyQualifiedInterfaceAndBaseTypeNames().ToArray();
            Diagnostics = new List<Diagnostic>();
            NewMiscellaneousMembers = new List<MemberDeclarationSyntax>();
            NonNestedReplacementsInMethods = new Dictionary<SyntaxNode, SyntaxNode>();
            AdditionalStatementsInOnCreateForCompilerMethod = new HashSet<string>();
            FullEcbSystemTypeNamesToGeneratedFieldNames = new Dictionary<string, string>();
            QueryFieldsToFieldNames = new Dictionary<IQueryFieldDescription, string>();
            NonQueryFields = new HashSet<INonQueryFieldDescription>();
            Rewriters = new List<SystemRewriter>();
            UniqueId = GetStableHashCode(originalSystemTypeSyntax.GetLocation().ToString()) & 0x7fffffff;
        }

        /// <summary>
        /// Please avoid using this method if possible, as it does not support node replacements at arbitrary levels of nesting.
        /// Instead, implement your own `SystemRewriter` and then add it to the `Rewriters` list.
        /// Ideally all existing modules will implement their own rewriters to handle replacements, so that we can remove this method once and for all,
        /// which will also greatly simplify the implementation of `PartialSystemTypeGenerator`.
        /// </summary>
        public void ReplaceNodeNonNested(SyntaxNode original, SyntaxNode replacement)
            => NonNestedReplacementsInMethods[original] = replacement;

        public bool ContainsChangesToSystem() =>
            NonNestedReplacementsInMethods.Any()
            || QueryFieldsToFieldNames.Any()
            || NonQueryFields.Any()
            || FullEcbSystemTypeNamesToGeneratedFieldNames.Any()
            || NewMiscellaneousMembers.Any()
            || Rewriters.Any();

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

        public string GetOrCreateEntityTypeHandleField(ITypeSymbol typeSymbol)
        {
            var entityTypeHandleFieldDescription = new EntityTypeHandleFieldDescription(typeSymbol);
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

        public string GetOrCreateBufferLookupField(ITypeSymbol typeSymbol, bool isReadOnly)
        {
            var bufferLookupField = new BufferLookupFieldDescription(typeSymbol, isReadOnly);
            NonQueryFields.Add(bufferLookupField);

            return bufferLookupField.GeneratedFieldName;
        }

        public string GetOrCreateEntityCommandBufferSystemField(ITypeSymbol ecbSystemTypeSymbol)
        {
            string fullEcbSystemTypeName = ecbSystemTypeSymbol.ToFullName();

            if (FullEcbSystemTypeNamesToGeneratedFieldNames.TryGetValue(fullEcbSystemTypeName, out var generatedFieldName))
                return generatedFieldName;

            generatedFieldName = $"__{ecbSystemTypeSymbol.ToValidVariableName()}";
            FullEcbSystemTypeNamesToGeneratedFieldNames[fullEcbSystemTypeName] = generatedFieldName;

            return generatedFieldName;
        }

        public Dictionary<SyntaxAnnotation, MemberDeclarationSyntax> GetAnnotationsToOriginalSyntaxNodes(TypeDeclarationSyntax typeDeclarationSyntax)
        {
            var result = new Dictionary<SyntaxAnnotation, MemberDeclarationSyntax>();
            foreach (var memberDeclarationSyntax in SystemTypeSyntax.Members)
                result[typeDeclarationSyntax.GetCurrentNode(memberDeclarationSyntax).GetAnnotations(TrackedNodeAnnotationUsedByRoslyn).First()] = memberDeclarationSyntax;
            return result;
        }

        public string GetOrCreateEntityStorageInfoLookupField()
        {
            var storageLookupField = new EntityStorageInfoLookupFieldDescription();
            storageLookupField.Init();
            NonQueryFields.Add(storageLookupField);

            return storageLookupField.GeneratedFieldName;
        }
    }
}
