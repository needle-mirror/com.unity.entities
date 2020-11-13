using System;
using System.Collections.Generic;
using System.Linq;
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

        public static bool Is(this ITypeSymbol symbol, string fullyQualifiedName, bool exact = false)
        {
            if (symbol is null)
                return false;

            if (symbol.ToDisplayString(QualifiedFormat) == fullyQualifiedName)
                return true;

            return !exact && symbol.BaseType.Is(fullyQualifiedName);
        }

        public static bool IsInt(this ITypeSymbol symbol) => symbol.SpecialType == SpecialType.System_Int32;
        public static bool IsDynamicBuffer(this ITypeSymbol symbol) =>
            (symbol.Name == "DynamicBuffer" && symbol.ContainingNamespace.ToDisplayString(QualifiedFormat) == "Unity.Entities");

        public static string ToFullName(this ITypeSymbol symbol) => symbol.ToDisplayString(QualifiedFormat);
        public static string ToValidVariableName(this ITypeSymbol symbol) => symbol.ToDisplayString(QualifiedFormat).Replace('.', '_');

        public static string GetFullyQualifiedTypeName(this ITypeSymbol typeSymbol)
        {
            var typeNameComponents = new List<string> {typeSymbol.Name};

            INamespaceSymbol namespaceSymbol = typeSymbol.ContainingNamespace;
            while (namespaceSymbol != null && !namespaceSymbol.IsGlobalNamespace)
            {
                typeNameComponents.Add(namespaceSymbol.Name);
                namespaceSymbol = namespaceSymbol.ContainingNamespace;
            }

            typeNameComponents.Reverse();
            return typeNameComponents.SeparateByDot();
        }

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

        public static string GetSymbolTypeName(this ISymbol symbol)
        {
            if (symbol is ILocalSymbol localSymbol)
                return localSymbol.Type.ToString();
            if (symbol is IParameterSymbol parameterSymbol)
                return parameterSymbol.Type.ToString();
            if (symbol is INamedTypeSymbol namedTypeSymbol)
                return namedTypeSymbol.ToString();
            throw new InvalidOperationException($"Unknown symbol type {symbol.GetType().Name}");
        }

        public static bool InheritsFromInterface(this ITypeSymbol symbol, string interfaceName, bool exact = false)
        {
            if (symbol is null)
                return false;

            foreach (var @interface in symbol.Interfaces)
            {
                if (@interface.ToDisplayString(QualifiedFormat) == interfaceName)
                    return true;

                if (!exact)
                {
                    foreach (var baseInterface in @interface.AllInterfaces)
                    {
                        if (baseInterface.ToDisplayString(QualifiedFormat) == interfaceName)
                            return true;
                        if (baseInterface.InheritsFromInterface(interfaceName, false))
                            return true;
                    }
                }
            }

            if (!exact && symbol.BaseType != null)
            {
                if (symbol.BaseType.InheritsFromInterface(interfaceName, false))
                    return true;
            }

            return false;
        }

        public static bool InheritsFromType(this ITypeSymbol symbol, string typeName, bool exact = false)
        {
            if (symbol is null)
                return false;

            if (symbol.ToDisplayString(QualifiedFormat) == typeName)
                return true;

            if (!exact && symbol.BaseType != null)
            {
                if (symbol.BaseType.InheritsFromType(typeName, false))
                    return true;
            }

            return false;
        }
    }
}
