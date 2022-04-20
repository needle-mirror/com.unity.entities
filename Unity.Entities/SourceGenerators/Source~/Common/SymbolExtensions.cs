using System;
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
            if (symbol is null)
                return false;

            if (symbol.ToDisplayString(QualifiedFormat) == fullyQualifiedName)
                return true;

            return checkBaseType && symbol.BaseType.Is(fullyQualifiedName);
        }

        public static bool IsInt(this ITypeSymbol symbol) => symbol.SpecialType == SpecialType.System_Int32;
        public static bool IsDynamicBuffer(this ITypeSymbol symbol) =>
            symbol.Name == "DynamicBuffer" && symbol.ContainingNamespace.ToDisplayString(QualifiedFormat) == "Unity.Entities";

        public static string ToFullName(this ITypeSymbol symbol) => symbol.ToDisplayString(QualifiedFormat);
        public static string ToFullName(this INamespaceSymbol symbol) => symbol.ToDisplayString(QualifiedFormat);
        public static string ToValidVariableName(this ITypeSymbol symbol) => symbol.ToDisplayString(QualifiedFormat).Replace('.', '_');

        public static bool ImplementsInterface(this ISymbol symbol, string interfaceName)
        {
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

        public static string GetSymbolTypeName(this ISymbol symbol) => GetSymbolType(symbol).ToFullName();

        public static bool InheritsFromInterface(this ITypeSymbol symbol, string interfaceName, bool checkBaseType = true)
        {
            if (symbol is null)
                return false;

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
            return typeSymbol.GetAttributes().Any(attribute => attribute.AttributeClass.ToFullName() == fullyQualifiedAttributeName);
        }

        public static bool HasAttributeOrFieldWithAttribute(this ITypeSymbol typeSymbol, string fullyQualifiedAttributeName)
        {
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

        public static bool IsInterfaceImplementation(this IMethodSymbol method)
        {
            return method.ContainingType.AllInterfaces.SelectMany(@interface => @interface.GetMembers().OfType<IMethodSymbol>()).Any(interfaceMethod => method.ContainingType.FindImplementationForInterfaceMember(interfaceMethod).Equals(method));
        }

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
    }
}
