using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Unity.Entities.SourceGen.SystemGeneratorCommon.EntitiesSourceFactory;

[assembly:InternalsVisibleTo("SystemGeneratorNew")]

namespace  Unity.Entities.SourceGen.SystemGeneratorCommon
{
    enum RewrittenSyntax
    {
        Method,
        Property
    }

    public readonly struct SystemGeneratorContext : ISourceGeneratorDiagnosable
    {
        public TypeDeclarationSyntax SystemType { get; }
        public SemanticModel SemanticModel { get; }
        public List<string> PreprocessorSymbolNames { get; }
        public List<Diagnostic> Diagnostics { get; }

        readonly Compilation _compilation;
        readonly List<EntityQueryField> _queryFields;
        readonly List<ComponentTypeHandleFieldDescription> _componentTypeFields;
        readonly Dictionary<MethodDeclarationSyntax, Dictionary<SyntaxNode, SyntaxNode>> _originalMethodToReplacements;
        readonly Dictionary<PropertyDeclarationSyntax, Dictionary<SyntaxNode, SyntaxNode>> _originalPropertyToReplacements;
        readonly HashSet<string> _onCreateForCompilerAdditionalSyntax;
        public readonly List<MemberDeclarationSyntax> NewMembers;

        public SystemGeneratorContext(TypeDeclarationSyntax originalSystemType, SemanticModel semanticModel, Compilation compilation,
            IEnumerable<string> preprocessorSymbolNames)
        {
            SystemType = originalSystemType;
            SemanticModel = semanticModel;
            _compilation = compilation;
            PreprocessorSymbolNames = preprocessorSymbolNames.ToList();
            Diagnostics = new List<Diagnostic>();

            _queryFields = new List<EntityQueryField>();
            _componentTypeFields = new List<ComponentTypeHandleFieldDescription>();
            _originalMethodToReplacements = new Dictionary<MethodDeclarationSyntax, Dictionary<SyntaxNode, SyntaxNode>>();
            _originalPropertyToReplacements = new Dictionary<PropertyDeclarationSyntax, Dictionary<SyntaxNode, SyntaxNode>>();
            _onCreateForCompilerAdditionalSyntax = new HashSet<string>();

            NewMembers = new List<MemberDeclarationSyntax>();
        }

        public void ReplaceNodeInMethod(SyntaxNode originalNode, SyntaxNode replacementNode)
        {
            var method = originalNode.Ancestors().OfType<MethodDeclarationSyntax>().First();
            ReplaceNodeInMethod(method, originalNode, replacementNode);
        }

        void ReplaceNodeInMethod(MethodDeclarationSyntax method, SyntaxNode originalNode, SyntaxNode replacementNode)
        {
            // TODO: Guard against replacing the same original node with two different replacement nodes
            if (_originalMethodToReplacements.TryGetValue(method, out var replacements))
                replacements.Add(originalNode, replacementNode);
            else
                _originalMethodToReplacements.Add(method, new Dictionary<SyntaxNode, SyntaxNode> {{originalNode, replacementNode}});
        }

        public void ReplaceNodeInProperty(PropertyDeclarationSyntax property, SyntaxNode originalNode, SyntaxNode replacementNode)
        {
            if (_originalPropertyToReplacements.TryGetValue(property, out var replacements))
                replacements.Add(originalNode, replacementNode);
            else
                _originalPropertyToReplacements.Add(property, new Dictionary<SyntaxNode, SyntaxNode> {{originalNode, replacementNode}});
        }

        public bool MadeChangesToSystem()
        {
            return _originalMethodToReplacements.Count > 0 || _queryFields.Count > 0 || _originalPropertyToReplacements.Count > 0;
        }

        public string GetOrCreateQueryField(EntityQueryDescription queryDescription)
        {
            var matchingField = _queryFields.SingleOrDefault(field => field.Matches(queryDescription));
            if (matchingField != null)
                return matchingField.FieldName;

            var queryFieldName = $"__query_{_queryFields.Count}";
            _queryFields.Add(new EntityQueryField(queryDescription, queryFieldName));

            return queryFieldName;
        }

        public void CreateUniqueQueryField(EntityQueryDescription queryDescription, string queryFieldName)
        {
            if (_queryFields.Any(field => field.FieldName == queryFieldName))
                throw new ArgumentException($"{queryFieldName} already exists.");

            _queryFields.Add(new EntityQueryField(queryDescription, queryFieldName));
        }

        public string GetOrCreateComponentTypeField(ITypeSymbol typeSymbol, bool isReadOnly)
        {
            var matchingField = _componentTypeFields.SingleOrDefault(field => field.TypeSymbol.Equals(typeSymbol) && field.IsReadOnly == isReadOnly);
            if (matchingField != null)
                return matchingField.FieldName;

            var isInISystem = SystemType is StructDeclarationSyntax;
            var newField = new ComponentTypeHandleFieldDescription(typeSymbol, isReadOnly, isInISystem);
            _componentTypeFields.Add(newField);

            return newField.FieldName;
        }

        public void AddOnCreateForCompilerSyntax(string syntax)
        {
            _onCreateForCompilerAdditionalSyntax.Add(syntax);
        }

        string GetAccessModifiers()
        {
            // Access must be protected unless this assembly has InternalsVisibleTo access to Unity.Entities (or is Unity.Entities),
            // in which case it should be `protected internal`
            var currentAssembly = _compilation.Assembly;
            if (currentAssembly.Name == "Unity.Entities")
                return "protected internal";
            var entitiesAssembly = currentAssembly.Modules.First().ReferencedAssemblySymbols.First(asm => asm.Name == "Unity.Entities");
            return entitiesAssembly.GivesAccessTo(currentAssembly) ? "protected internal" : "protected";
        }

        public TypeDeclarationSyntax GeneratePartialType()
        {
            TypeDeclarationSyntax generatedClassDeclaration;
            var baseList = SystemType.BaseList;
            var isInISystem = SystemType is StructDeclarationSyntax;

            if (isInISystem)
            {
                generatedClassDeclaration = StructDeclaration(SystemType.Identifier);
                baseList = baseList.AddTypes(SimpleBaseType(ParseTypeName("Unity.Entities.ISystemCompilerGenerated")));
            }
            else
                generatedClassDeclaration = ClassDeclaration(SystemType.Identifier);

            generatedClassDeclaration = generatedClassDeclaration
                .WithBaseList(baseList)
                .WithModifiers(SystemType.Modifiers)
                .WithAttributeLists(SourceGenHelpers.GetCompilerGeneratedAttribute());

            var typeParameterList = SystemType.ChildNodes().OfType<TypeParameterListSyntax>().SingleOrDefault();
            if (typeParameterList != null)
                generatedClassDeclaration = generatedClassDeclaration.WithTypeParameterList(typeParameterList);

            foreach (var kvp in _originalMethodToReplacements)
            {
                var methodReplacer = new SyntaxNodeReplacer(kvp.Value);
                var rewrittenMethod = (MethodDeclarationSyntax) methodReplacer.Visit(kvp.Key);
                if (rewrittenMethod != kvp.Key)
                    rewrittenMethod = rewrittenMethod.WithoutPreprocessorTrivia();
                else
                    break;

                var originalMethodSymbol = SemanticModel.GetDeclaredSymbol(kvp.Key);
                var targetMethodNameAndSignature = originalMethodSymbol.GetMethodAndParamsAsString();

                var (modifiers, attributeList) = GetModifiersAndAttributes(targetMethodNameAndSignature, rewrittenMethod, RewrittenSyntax.Method);

                var stableHashCode =
                    SourceGenHelpers.GetStableHashCode($"{originalMethodSymbol.ContainingType.ToFullName()}_{targetMethodNameAndSignature}") & 0x7fffffff;

                generatedClassDeclaration =
                    generatedClassDeclaration
                        .AddMembers(
                            rewrittenMethod
                                .WithoutPreprocessorTrivia()
                                .WithIdentifier(Identifier($"__{kvp.Key.Identifier.Text}_{stableHashCode:X}"))
                                .WithModifiers(modifiers)
                                .WithAttributeLists(attributeList));
            }

            foreach (var kvp in _originalPropertyToReplacements)
            {
                var methodReplacer = new SyntaxNodeReplacer(kvp.Value);
                var rewrittenProperty = (PropertyDeclarationSyntax) methodReplacer.Visit(kvp.Key);

                if (rewrittenProperty == kvp.Key)
                {
                    break;
                }

                rewrittenProperty = rewrittenProperty.WithoutPreprocessorTrivia();
                var originalPropertySymbol = SemanticModel.GetDeclaredSymbol(kvp.Key);

                var (modifiers, attributeList) = GetModifiersAndAttributes(originalPropertySymbol.OriginalDefinition.ToString(), rewrittenProperty, RewrittenSyntax.Property);

                var stableHashCode =
                    SourceGenHelpers.GetStableHashCode($"{originalPropertySymbol.ContainingType.ToFullName()}_{originalPropertySymbol.OriginalDefinition}") & 0x7fffffff;

                generatedClassDeclaration =
                    generatedClassDeclaration
                        .AddMembers(
                            rewrittenProperty
                                .WithoutPreprocessorTrivia()
                                .WithIdentifier(Identifier($"__{kvp.Key.Identifier.Text}_{stableHashCode:X}"))
                                .WithModifiers(modifiers)
                                .WithAttributeLists(attributeList));
            }

            foreach (var queryField in _queryFields)
                generatedClassDeclaration = generatedClassDeclaration.AddMembers(queryField.FieldDeclaration);

            foreach (var typeHandle in _componentTypeFields)
                generatedClassDeclaration = generatedClassDeclaration.AddMembers(typeHandle.FieldDeclaration);

            generatedClassDeclaration = generatedClassDeclaration.AddMembers(NewMembers.ToArray());

            return generatedClassDeclaration.AddMembers(
                OnCreateForCompilerMethod(_onCreateForCompilerAdditionalSyntax, _componentTypeFields, _queryFields, GetAccessModifiers(), isInISystem));

            static (SyntaxTokenList, SyntaxList<AttributeListSyntax>) GetModifiersAndAttributes(string targetMethodNameAndSignature, MemberDeclarationSyntax rewritten, RewrittenSyntax rewrittenSyntax)
            {
                var modifiers = new SyntaxTokenList(rewritten.Modifiers.Where(m => !m.IsKind(SyntaxKind.OverrideKeyword)));

                var dotsCompilerPatchedMethodArguments = ParseAttributeArgumentList($"(\"{targetMethodNameAndSignature}\")");

                string attributeName = rewrittenSyntax == RewrittenSyntax.Method
                    ? "Unity.Entities.DOTSCompilerPatchedMethod"
                    : "Unity.Entities.DOTSCompilerPatchedProperty";

                var dotsCompilerPatchedMethodAttribute = Attribute(IdentifierName(attributeName), dotsCompilerPatchedMethodArguments);

                var attributeList = new SyntaxList<AttributeListSyntax>();
                attributeList = attributeList.Add(AttributeList(SeparatedList(new[] {dotsCompilerPatchedMethodAttribute})));

                return (modifiers, attributeList);
            }
        }
    }

    public static class DiagnosticsLogger
    {
        public static void LogError(this ISourceGeneratorDiagnosable diagnosable, string errorCode, string title, string errorMessage, Location location, string description = "")
        {
            if (errorCode.Contains("ICE"))
                errorMessage = $"Seeing this error indicates a bug in the dots compiler. We'd appreciate a bug report (About->Report a Bug...). Thnx! <3 {errorMessage}";

            Log(diagnosable, DiagnosticSeverity.Error, errorCode, title, errorMessage, location, description);
        }

        public static void LogWarning(this ISourceGeneratorDiagnosable diagnosable, string errorCode, string title, string errorMessage, Location location, string description = "")
            => Log(diagnosable, DiagnosticSeverity.Warning, errorCode, title, errorMessage, location, description);

        public static void LogInfo(this ISourceGeneratorDiagnosable diagnosable, string errorCode, string title, string errorMessage, Location location, string description = "")
            => Log(diagnosable, DiagnosticSeverity.Info, errorCode, title, errorMessage, location, description);

        static void Log(this ISourceGeneratorDiagnosable diagnosable, DiagnosticSeverity diagnosticSeverity, string errorCode, string title, string errorMessage, Location location, string description = "")
        {
            SourceGenHelpers.LogInfo($"{diagnosticSeverity}: {errorCode}, {title}, {errorMessage}");
            var rule = new DiagnosticDescriptor(errorCode, title, errorMessage, "Source Generator", diagnosticSeverity, true, description);
            diagnosable.Diagnostics?.Add(Diagnostic.Create(rule, location));
        }
    }

    public interface ISourceGeneratorDiagnosable
    {
        public List<Diagnostic> Diagnostics { get; }
    }
}
