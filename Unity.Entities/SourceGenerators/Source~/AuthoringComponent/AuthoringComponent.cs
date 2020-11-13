using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen
{
    internal class AuthoringComponent
    {
        public readonly (string Name, bool IsValueType) DeclaringType;
        public readonly bool FromValueType;
        public readonly IEnumerable<AuthoringComponentFieldDescription> FieldDescriptions;
        public readonly IEnumerable<NamespaceDeclarationSyntax> NamespacesFromMostToLeastNested;
        public readonly IEnumerable<AttributeData> AttributesToPreserve;
        public readonly AuthoringComponentInterface Interface;
        public readonly string FullyQualifiedTypeName;

        public bool ImplementIDeclareReferencedPrefabs =>
            FieldDescriptions.Any(d =>
                d.FieldType == FieldType.EntityArray
                || d.FieldType == FieldType.SingleEntity);

        public AuthoringComponent(SyntaxNode node, ISymbol symbol, GeneratorExecutionContext context)
        {
            INamedTypeSymbol name = (INamedTypeSymbol)symbol;

            DeclaringType = (name.Name, name.IsValueType);
            FullyQualifiedTypeName = name.GetFullyQualifiedTypeName();
            FromValueType = name.IsValueType;
            NamespacesFromMostToLeastNested = node.GetNamespacesFromMostToLeastNested();

            static bool PreserveAttribute(AttributeData attributeData)
            {
                if (attributeData.AttributeClass.Name == "GenerateAuthoringComponentAttribute")
                    return false;

                if (attributeData.AttributeClass.Name == "StructLayoutAttribute")
                {
                    return false;
                }

                // Check if this attribute can be placed on a class (authoring component), this can be only true if one the following is true:
                // 1. It has no attribute usage attributes
                // 2. It has an attribute usage attribute and at least one targets class types
                AttributeData[] attributeUsages =
                    attributeData.AttributeClass.GetAttributes().Where(
                        a => a.AttributeClass.Name == "AttributeUsageAttribute").ToArray();

                if (!attributeUsages.Any())
                {
                    return true;
                }
                
                // See: https://stackoverflow.com/questions/64497819/checking-the-argument-passed-to-the-attributeusageattribute-constructor
                return
                    attributeUsages.Any(
                        attributeUsage =>
                            attributeUsage.ConstructorArguments.Any(
                                arg =>
                                    arg.Kind == TypedConstantKind.Enum
                                    && arg.Type.Name == nameof(AttributeTargets)
                                    && Enum.TryParse(arg.Value.ToString(), ignoreCase: false, out AttributeTargets parsedValue)
                                    && parsedValue == AttributeTargets.Class | parsedValue.ToString().Split(',').Contains("Class")
                                ));
            }

            AttributesToPreserve = symbol.GetAttributes().Where(PreserveAttribute);

            FieldDescriptions =
                node.ChildNodes()
                    .OfType<FieldDeclarationSyntax>()
                    .SelectMany(f => f.Declaration.Variables)
                    .Select(v => AuthoringComponentFieldDescription.From(v, context));

            var typeSymbol = (ITypeSymbol)symbol;

            Interface =
                typeSymbol.ImplementsInterface("Unity.Entities.IComponentData")
                    ? AuthoringComponentInterface.IComponentData
                    : typeSymbol.ImplementsInterface("Unity.Entities.IBufferElementData")
                        ? AuthoringComponentInterface.IBufferElementData
                        : AuthoringComponentInterface.None;
        }
    }
}
