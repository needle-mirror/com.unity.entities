using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
{
    public class JobEntityDescription : ISourceGenerationDescription
    {
        public readonly string DeclaringTypeName;
        public readonly string DeclaringTypeFullyQualifiedName;

        public readonly IEnumerable<NamespaceDeclarationSyntax> NamespacesFromMostToLeastNested;
        public readonly IEnumerable<FieldDescription> FieldDescriptions;
        public readonly IEnumerable<OnUpdateMethodParameter> OnUpdateMethodParameters;

        public readonly string GeneratedJobEntityBatchTypeName;

        public GeneratorExecutionContext Context { get; }

        public JobEntityDescription(
            SyntaxNode jobEntityTypeNode,
            SyntaxNode jobEntityOnUpdateMethodNode,
            ITypeSymbol candidateTypeSymbol,
            GeneratorExecutionContext context)
        {
            Context = context;

            INamedTypeSymbol name = (INamedTypeSymbol)candidateTypeSymbol;

            DeclaringTypeName = name.Name;
            DeclaringTypeFullyQualifiedName = name.GetFullyQualifiedTypeName();

            NamespacesFromMostToLeastNested = jobEntityTypeNode.GetNamespacesFromMostToLeastNested();
            FieldDescriptions =
                jobEntityTypeNode
                     .ChildNodes()
                     .OfType<FieldDeclarationSyntax>()
                     .SelectMany(f => f.Declaration.Variables)
                     .Select(v => FieldDescription.From(v, Context));

            OnUpdateMethodParameters =
                jobEntityOnUpdateMethodNode
                     .ChildNodes()
                     .OfType<ParameterListSyntax>()
                     .SelectMany(pls => pls.Parameters)
                     .Select(p => new OnUpdateMethodParameter(p, Context));

            GeneratedJobEntityBatchTypeName = $"{DeclaringTypeName}_OnUpdate";
        }

        public class OnUpdateMethodParameter
        {
            public readonly string FullyQualifiedTypeName;
            public readonly bool IsReadOnly;
            public readonly string BatchFieldName;
            public readonly string BatchFieldDeclaration;
            public readonly string NativeArrayPointerName;

            public OnUpdateMethodParameter(ParameterSyntax parameterSyntax, GeneratorExecutionContext sourceGeneratorContext)
            {
                string typeName = parameterSyntax.Type.ToString();

                FullyQualifiedTypeName = GetFullyQualifiedTypeName(parameterSyntax, sourceGeneratorContext);

                IsReadOnly = parameterSyntax.IsReadOnly();
                BatchFieldName = $"__{typeName}Type";

                string fieldDeclaration = $"public Unity.Entities.ComponentTypeHandle<{FullyQualifiedTypeName}> {BatchFieldName};";

                BatchFieldDeclaration = IsReadOnly ? $"[Unity.Collections.ReadOnly] {fieldDeclaration}" : fieldDeclaration;
                NativeArrayPointerName = $"{Char.ToLowerInvariant(typeName[0])}{typeName.Substring(1)}s";
            }

            private static string GetFullyQualifiedTypeName(ParameterSyntax parameterSyntax, GeneratorExecutionContext sourceGeneratorContext)
            {
                SemanticModel semanticModel = sourceGeneratorContext.Compilation.GetSemanticModel(parameterSyntax.Type.SyntaxTree);
                return ((IParameterSymbol)semanticModel.GetDeclaredSymbol(parameterSyntax)).Type.GetFullyQualifiedTypeName();
            }
        }
    }
}
