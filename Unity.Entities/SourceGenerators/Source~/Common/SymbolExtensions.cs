using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Unity.Entities.SourceGen.Common
{
    public static class SymbolExtensions
    {
        static SymbolDisplayFormat QualifiedFormat { get; } =
            new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions:
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        static SymbolDisplayFormat QualifiedFormatWithoutGlobalPrefix { get; } =
            new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions:
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        static SymbolDisplayFormat QualifiedFormatWithoutSpecialTypeNames { get; } =
            new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions:
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

        public static bool Is(this ITypeSymbol symbol, string fullyQualifiedName, bool checkBaseType = true)
        {
            fullyQualifiedName = PrependGlobalIfMissing(fullyQualifiedName);

            if (symbol is null)
                return false;

            if (symbol.ToDisplayString(QualifiedFormat) == fullyQualifiedName)
                return true;

            return checkBaseType && symbol.BaseType.Is(fullyQualifiedName);
        }

        public static IEnumerable<string> GetAllFullyQualifiedInterfaceAndBaseTypeNames(this ITypeSymbol symbol)
        {
            if (symbol.BaseType != null)
            {
                var baseTypeName = symbol.BaseType.ToDisplayString(QualifiedFormat);
                if (baseTypeName != "global::System.ValueType")
                    yield return baseTypeName;
            }

            foreach (var _interface in symbol.Interfaces)
                yield return _interface.ToDisplayString(QualifiedFormat);
        }

        public static bool IsInt(this ITypeSymbol symbol) => symbol.SpecialType == SpecialType.System_Int32;
        public static bool IsDynamicBuffer(this ITypeSymbol symbol) =>
            symbol.Name == "DynamicBuffer" && symbol.ContainingNamespace.ToDisplayString(QualifiedFormat) == "global::Unity.Entities";
        public static bool IsSharedComponent(this ITypeSymbol symbol) => symbol.InheritsFromInterface("Unity.Entities.ISharedComponentData");
        public static bool IsComponent(this ITypeSymbol symbol) => symbol.InheritsFromInterface("Unity.Entities.IComponentData");
        public static bool IsZeroSizedComponent(this ITypeSymbol symbol, HashSet<ITypeSymbol> seenSymbols = null)
        {
// TODO: This was recently fixed (https://github.com/dotnet/roslyn-analyzers/issues/5804), remove pragmas after we update .net
#pragma warning disable RS1024
            if (seenSymbols == null)
                seenSymbols = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default) { symbol };
#pragma warning restore RS1024

            foreach (var field in symbol.GetMembers().OfType<IFieldSymbol>())
            {
                switch (symbol.SpecialType)
                {
                    case SpecialType.System_Void:
                        continue;
                    case SpecialType.None:
                        if (field.IsStatic || field.IsConst)
                            continue;

                        if (field.Type.TypeKind == TypeKind.Struct)
                        {
                            // Handle cycles in type (otherwise we will stack overflow)
                            if (!seenSymbols.Add(field.Type))
                                continue;

                            if (IsZeroSizedComponent(field.Type))
                                continue;
                        }
                        return false;
                    default:
                        return false;
                }
            }
            return true;
        }

        public static bool IsEnableableComponent(this ITypeSymbol symbol) => symbol.InheritsFromInterface("Unity.Entities.IEnableableComponent");

        public static string ToFullName(this ISymbol symbol) => symbol.ToDisplayString(QualifiedFormat);
        public static string ToSimpleName(this ITypeSymbol symbol) => symbol.ToDisplayString(QualifiedFormatWithoutGlobalPrefix);
        public static string ToValidIdentifier(this ITypeSymbol symbol)
        {
            var validIdentifier = symbol.ToDisplayString(QualifiedFormatWithoutGlobalPrefix).Replace('.', '_');
            if (symbol is INamedTypeSymbol { IsGenericType: true })
                validIdentifier = validIdentifier.Replace('<', '_').Replace('>', '_');
            return validIdentifier;
        }

        public static bool ImplementsInterface(this ISymbol symbol, string interfaceName)
        {
            interfaceName = PrependGlobalIfMissing(interfaceName);

            return symbol is ITypeSymbol typeSymbol
                   && typeSymbol.AllInterfaces.Any(i => i.ToFullName() == interfaceName || i.InheritsFromInterface(interfaceName));
        }

        public static bool Is(this ITypeSymbol symbol, string nameSpace, string typeName, bool checkBaseType = true)
        {
            if (symbol is null)
                return false;

            if (symbol.Name == typeName && symbol.ContainingNamespace?.Name == nameSpace)
                return true;

            return checkBaseType && symbol.BaseType.Is(nameSpace, typeName);
        }

        public static ITypeSymbol GetSymbolType(this ISymbol symbol)
        {
            return symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                INamedTypeSymbol namedTypeSymbol => namedTypeSymbol,
                IMethodSymbol methodSymbol => methodSymbol.ContainingType,
                IPropertySymbol propertySymbol => propertySymbol.ContainingType,
                _ => throw new InvalidOperationException($"Unknown typeSymbol type {symbol.GetType()}")
            };
        }

        public static bool InheritsFromInterface(this ITypeSymbol symbol, string interfaceName, bool checkBaseType = true)
        {
            if (symbol is null)
                return false;

            interfaceName = PrependGlobalIfMissing(interfaceName);

            foreach (var @interface in symbol.Interfaces)
            {
                if (@interface.ToDisplayString(QualifiedFormat) == interfaceName)
                    return true;

                if (checkBaseType)
                {
                    foreach (var baseInterface in @interface.AllInterfaces)
                    {
                        if (baseInterface.ToDisplayString(QualifiedFormat) == interfaceName)
                            return true;
                        if (baseInterface.InheritsFromInterface(interfaceName))
                            return true;
                    }
                }
            }

            if (checkBaseType && symbol.BaseType != null)
            {
                if (symbol.BaseType.InheritsFromInterface(interfaceName))
                    return true;
            }

            return false;
        }

        public static bool InheritsFromType(this ITypeSymbol symbol, string typeName, bool checkBaseType = true)
        {
            typeName = PrependGlobalIfMissing(typeName);

            if (symbol is null)
                return false;

            if (symbol.ToDisplayString(QualifiedFormat) == typeName)
                return true;

            if (checkBaseType && symbol.BaseType != null)
            {
                if (symbol.BaseType.InheritsFromType(typeName))
                    return true;
            }

            return false;
        }

        public static bool HasAttribute(this ISymbol typeSymbol, string fullyQualifiedAttributeName)
        {
            fullyQualifiedAttributeName = PrependGlobalIfMissing(fullyQualifiedAttributeName);

            return typeSymbol.GetAttributes().Any(attribute => attribute.AttributeClass.ToFullName() == fullyQualifiedAttributeName);
        }

        public static bool HasAttributeOrFieldWithAttribute(this ITypeSymbol typeSymbol, string fullyQualifiedAttributeName)
        {
            fullyQualifiedAttributeName = PrependGlobalIfMissing(fullyQualifiedAttributeName);

            return typeSymbol.HasAttribute(fullyQualifiedAttributeName) ||
                   typeSymbol.GetMembers().OfType<IFieldSymbol>().Any(f => !f.IsStatic && f.Type.HasAttributeOrFieldWithAttribute(fullyQualifiedAttributeName));
        }

        public static string GetMethodAndParamsAsString(this IMethodSymbol methodSymbol)
        {
            var strBuilder = new StringBuilder();
            strBuilder.Append(methodSymbol.Name);

            for (var typeIndex = 0; typeIndex < methodSymbol.TypeParameters.Length; typeIndex++)
                strBuilder.Append($"_T{typeIndex}");

            foreach (var param in methodSymbol.Parameters)
            {
                if (param.RefKind != RefKind.None)
                    strBuilder.Append($"_{param.RefKind.ToString().ToLower()}");
                strBuilder.Append($"_{param.Type.ToDisplayString(QualifiedFormatWithoutSpecialTypeNames).Replace(" ", string.Empty)}");
            }

            return strBuilder.ToString();
        }

        public static bool IsAspect(this ITypeSymbol typeSymbol) => typeSymbol.InheritsFromInterface("Unity.Entities.IAspect");

        public static TypedConstantKind GetTypedConstantKind(this ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_Byte:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Char:
                case SpecialType.System_String:
                case SpecialType.System_Object:
                    return TypedConstantKind.Primitive;
                default:
                    switch (type.TypeKind)
                    {
                            case TypeKind.Array:
                                return TypedConstantKind.Array;
                            case TypeKind.Enum:
                                return TypedConstantKind.Enum;
                            case TypeKind.Error:
                                return TypedConstantKind.Error;
                        }
                    return TypedConstantKind.Type;
            }
        }

        static string PrependGlobalIfMissing(this string typeOrNamespaceName) =>
            !typeOrNamespaceName.StartsWith("global::") ? $"global::{typeOrNamespaceName}" : typeOrNamespaceName;
    }
}
