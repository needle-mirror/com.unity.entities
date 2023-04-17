using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGenerator.Common
{
    public enum SystemType
    {
        Unknown,
        SystemBase,
        ISystem
    }

    public static class SystemTypeHelpers
    {
        public static (bool IsSystemType, SystemType SystemType) TryGetSystemType(this ITypeSymbol namedSystemTypeSymbol)
        {
            if (namedSystemTypeSymbol.Is("Unity.Entities.SystemBase"))
                return (true, SystemType.SystemBase);
            if (namedSystemTypeSymbol.InheritsFromInterface("Unity.Entities.ISystem"))
                return (true, SystemType.ISystem);
            return (false, default);
        }
    }

    public readonly struct SystemDescription : ISourceGeneratorDiagnosable, IAdditionalHandlesInfo
    {
        public List<Diagnostic> Diagnostics { get; }

        public HandlesDescription HandlesDescription { get; }

        public TypeDeclarationSyntax TypeSyntax => SystemTypeSyntax;

        public SystemType SystemType { get; }
        public readonly INamedTypeSymbol SystemTypeSymbol;
        public readonly TypeDeclarationSyntax SystemTypeSyntax;
        public SemanticModel SemanticModel { get; }
        public readonly IReadOnlyCollection<string> PreprocessorSymbolNames;
        public readonly string SystemTypeFullName;
        public readonly SyntaxTreeInfo SyntaxTreeInfo;
        public readonly IReadOnlyCollection<string> FullyQualifiedBaseTypeNames;
        public readonly List<SystemRewriter> Rewriters;
        public readonly Dictionary<SyntaxNode, SyntaxNode> NonNestedReplacementsInMethods;
        public readonly List<MemberDeclarationSyntax> NewMiscellaneousMembers;
        public readonly Dictionary<string, string> FullEcbSystemTypeNamesToGeneratedFieldNames;
        public readonly HashSet<string> AdditionalStatementsInOnCreateForCompilerMethod;

        public readonly bool IsForDotsRuntime;
        public readonly bool IsDotsRuntimeProfilerEnabled;
        public readonly bool IsProfilerEnabled;
        public readonly bool IsDotsDebugMode;
        public readonly bool IsUnityCollectionChecksEnabled;

        public SystemDescription(
            TypeDeclarationSyntax originalSystemTypeSyntax,
            SystemType systemType,
            INamedTypeSymbol systemTypeSymbol,
            SemanticModel semanticModel,
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
            FullyQualifiedBaseTypeNames = SystemTypeSymbol.GetAllFullyQualifiedInterfaceAndBaseTypeNames().ToArray();
            Diagnostics = new List<Diagnostic>();
            NewMiscellaneousMembers = new List<MemberDeclarationSyntax>();
            NonNestedReplacementsInMethods = new Dictionary<SyntaxNode, SyntaxNode>();
            AdditionalStatementsInOnCreateForCompilerMethod = new HashSet<string>();
            FullEcbSystemTypeNamesToGeneratedFieldNames = new Dictionary<string, string>();
            HandlesDescription = HandlesDescription.Create(originalSystemTypeSyntax);
            Rewriters = new List<SystemRewriter>();

            IsUnityCollectionChecksEnabled = false;
            IsForDotsRuntime = false;
            IsDotsRuntimeProfilerEnabled = false;
            IsProfilerEnabled = false;
            IsDotsDebugMode = false;

            foreach (var name in PreprocessorSymbolNames)
            {
                switch (name)
                {
                    case "ENABLE_UNITY_COLLECTIONS_CHECKS":
                        IsUnityCollectionChecksEnabled = true;
                        break;
                    case "UNITY_DOTSRUNTIME":
                        IsForDotsRuntime = true;
                        break;
                    case "ENABLE_DOTSRUNTIME_PROFILER":
                        IsDotsRuntimeProfilerEnabled = true;
                        break;
                    case "ENABLE_PROFILER":
                        IsProfilerEnabled = true;
                        break;
                    case "UNITY_DOTS_DEBUG":
                        IsDotsDebugMode = true;
                        break;
                }
            }
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
            || HandlesDescription.QueryFieldsToFieldNames.Any()
            || HandlesDescription.NonQueryFields.Any()
            || FullEcbSystemTypeNamesToGeneratedFieldNames.Any()
            || NewMiscellaneousMembers.Any()
            || Rewriters.Any();

        public string GetOrCreateEntityCommandBufferSystemField(ITypeSymbol ecbSystemTypeSymbol)
        {
            string fullEcbSystemTypeName = ecbSystemTypeSymbol.ToFullName();

            if (FullEcbSystemTypeNamesToGeneratedFieldNames.TryGetValue(fullEcbSystemTypeName, out var generatedFieldName))
                return generatedFieldName;

            generatedFieldName = $"__{ecbSystemTypeSymbol.ToValidIdentifier()}";
            FullEcbSystemTypeNamesToGeneratedFieldNames[fullEcbSystemTypeName] = generatedFieldName;

            return generatedFieldName;
        }

        public Dictionary<SyntaxAnnotation, MemberDeclarationSyntax> GetAnnotationsToOriginalSyntaxNodes(TypeDeclarationSyntax typeDeclarationSyntax)
        {
            var result = new Dictionary<SyntaxAnnotation, MemberDeclarationSyntax>();
            foreach (var memberDeclarationSyntax in SystemTypeSyntax.Members)
                result[typeDeclarationSyntax.GetCurrentNode(memberDeclarationSyntax).GetAnnotations(SourceGenHelpers.TrackedNodeAnnotationUsedByRoslyn).First()] = memberDeclarationSyntax;
            return result;
        }
    }
}
