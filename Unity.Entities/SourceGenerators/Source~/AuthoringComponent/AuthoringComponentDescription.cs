using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.AuthoringComponent
{
    class AuthoringComponentDescription
    {
        public readonly (string Name, bool IsValueType) DeclaringType;
        public readonly bool FromValueType;
        public readonly IEnumerable<AuthoringComponentFieldDescription> FieldDescriptions;
        public readonly IEnumerable<NamespaceDeclarationSyntax> NamespacesFromMostToLeastNested;
        public readonly IEnumerable<ConstructorDeclarationSyntax> UserWrittenConstructors;
        public readonly IEnumerable<AttributeData> AttributesToPreserve;
        public readonly Location Location;
        public readonly AuthoringComponentInterface Interface;
        public readonly string FullyQualifiedTypeName;
        public readonly bool HasStructLayoutAttribute;
        public readonly bool IsValid;

        public bool HasDefaultConstructor =>
            !UserWrittenConstructors.Any() || UserWrittenConstructors.Any(constructor => !constructor.ParameterList.Parameters.Any());

        public bool ImplementIDeclareReferencedPrefabs =>
            FieldDescriptions.Any(d =>
                d.FieldType == FieldType.EntityArray
                || d.FieldType == FieldType.SingleEntity);

        public AuthoringComponentDescription(SyntaxNode node, ISymbol symbol, GeneratorExecutionContext context)
        {
            INamedTypeSymbol name = (INamedTypeSymbol)symbol;

            DeclaringType = (name.Name, name.IsValueType);
            FullyQualifiedTypeName = name.ToFullName();
            FromValueType = name.IsValueType;
            NamespacesFromMostToLeastNested = node.GetNamespacesFromMostToLeastNested();
            UserWrittenConstructors = node.ChildNodes().OfType<ConstructorDeclarationSyntax>();
            Location = node.GetLocation();
            IsValid = true;

            var attributes = symbol.GetAttributes();

            HasStructLayoutAttribute =
                attributes.Any(a => a.AttributeClass.Name == "StructLayoutAttribute");
            AttributesToPreserve = attributes.Where(PreserveAttribute);

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

            switch (Interface)
            {
                case AuthoringComponentInterface.IComponentData:
                    var entityArrayField = FieldDescriptions.FirstOrDefault(desc => desc.FieldType == FieldType.EntityArray);
                    if (FromValueType && entityArrayField != null)
                    {
                        AuthoringComponentErrors.DC0060(context, entityArrayField.Location, FullyQualifiedTypeName);
                        IsValid = false;
                    }

                    if (!FromValueType && !HasDefaultConstructor)
                    {
                        AuthoringComponentErrors.DC0030(context, Location, FullyQualifiedTypeName);
                        IsValid = false;
                    }
                    break;
                case AuthoringComponentInterface.IBufferElementData:
                    if (!FromValueType)
                    {
                        AuthoringComponentErrors.DC0041(context, Location, FullyQualifiedTypeName);
                        IsValid = false;
                    }

                    var referenceTypeField =
                        FieldDescriptions.FirstOrDefault(d =>
                            d.FieldType == FieldType.NonEntityReferenceType || d.FieldType == FieldType.EntityArray);

                    if (referenceTypeField != null)
                    {
                        AuthoringComponentErrors.DC0040(context, referenceTypeField.Location, referenceTypeField.FieldSymbol.Name, FullyQualifiedTypeName);
                        IsValid = false;
                    }
                    break;
                default:
                    AuthoringComponentErrors.DC3003(context, Location, FullyQualifiedTypeName);
                    IsValid = false;
                    break;
            }

            static bool PreserveAttribute(AttributeData attributeData)
            {
                if (attributeData.AttributeClass.Name == "GenerateAuthoringComponentAttribute"
                    || attributeData.AttributeClass.Name == "StructLayoutAttribute")
                    return false;

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
        }
    }
}
