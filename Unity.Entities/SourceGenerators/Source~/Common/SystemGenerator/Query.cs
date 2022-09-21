using System;
using Microsoft.CodeAnalysis;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGeneratorCommon
{
    public enum QueryType
    {
        All, None, Any, ChangeFilter
    }

    public struct Query : IEquatable<Query>
    {
        public QueryType Type;
        public ITypeSymbol TypeSymbol;
        public bool IsReadOnly;

        public bool Equals(Query other) => SymbolEqualityComparer.Default.Equals(TypeSymbol, other.TypeSymbol)
                                           && IsReadOnly == other.IsReadOnly;

        public override bool Equals(object obj) => obj is Query other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = TypeSymbol != null ? TypeSymbol.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ IsReadOnly.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(Query left, Query right) => left.Equals(right);
        public static bool operator !=(Query left, Query right) => !left.Equals(right);

        public override string ToString() =>
            IsReadOnly
                ? $@"Unity.Entities.ComponentType.ReadOnly<{TypeSymbol.ToFullName()}>()"
                : $@"Unity.Entities.ComponentType.ReadWrite<{TypeSymbol.ToFullName()}>()";
    }
}
