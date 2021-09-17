using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGeneratorCommon
{
    // Data used to define a single EntityQuery in a system.
    public class EntityQueryField
    {
        public EntityQueryDescription QueryDescription;
        public string FieldName;
        public FieldDeclarationSyntax FieldDeclaration;

        public EntityQueryField(EntityQueryDescription queryDescription, string queryFieldName)
        {
            QueryDescription = queryDescription;
            FieldName = queryFieldName;
            FieldDeclaration = (FieldDeclarationSyntax) SyntaxFactory.ParseMemberDeclaration($"Unity.Entities.EntityQuery {FieldName};");
        }

        public bool Matches(EntityQueryDescription entityQuery) => QueryDescription.Equals(entityQuery);
    }

    // Description of a EntityQuery used to request an EntityQuery field.
    // These can result in a unique EntityQueryField or a shared one (depending on the need of the request).
    public class EntityQueryDescription : IEquatable<EntityQueryDescription>
    {
        public (INamedTypeSymbol typeInfo, bool isReadOnly)[] All;
        public (INamedTypeSymbol typeInfo, bool isReadOnly)[] Any;
        public (INamedTypeSymbol typeInfo, bool isReadOnly)[] None;
        public (INamedTypeSymbol typeInfo, bool isReadOnly)[] ChangeFilterTypes;
        public EntityQueryOptions Options;
        public string StoreInQueryFieldName { get; }

        public EntityQueryDescription()
        {
            All = Array.Empty<(INamedTypeSymbol typeInfo, bool isReadOnly)>();
            Any = Array.Empty<(INamedTypeSymbol typeInfo, bool isReadOnly)>();
            None = Array.Empty<(INamedTypeSymbol typeInfo, bool isReadOnly)>();
            ChangeFilterTypes = Array.Empty<(INamedTypeSymbol typeInfo, bool isReadOnly)>();
        }

        public EntityQueryDescription(
            (INamedTypeSymbol typeInfo, bool isReadOnly)[] all,
            (INamedTypeSymbol typeInfo, bool isReadOnly)[] any,
            (INamedTypeSymbol typeInfo, bool isReadOnly)[] none,
            (INamedTypeSymbol typeInfo, bool isReadOnly)[] changeFilterTypes,
            EntityQueryOptions options, string storeInQueryFieldName = null)
        {
            All = all;
            Any = any;
            None = none;
            ChangeFilterTypes = changeFilterTypes;
            Options = options;
            StoreInQueryFieldName = storeInQueryFieldName;
        }

        public bool Equals(EntityQueryDescription other)
        {
            return All.IsStructurallyEqualTo(other.All)
                   && Any.IsStructurallyEqualTo(other.Any)
                   && None.IsStructurallyEqualTo(other.None)
                   && ChangeFilterTypes.IsStructurallyEqualTo(other.ChangeFilterTypes)
                   && Options == other.Options;
        }

        public override bool Equals(object obj)
        {
            return obj is EntityQueryDescription other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 19;

                foreach (var foo in All) hash = hash * 31 + foo.GetHashCode();
                foreach (var foo in Any) hash = hash * 31 + foo.GetHashCode();
                foreach (var foo in None) hash = hash * 31 + foo.GetHashCode();
                foreach (var changeFilterType in ChangeFilterTypes) hash = hash * 31 + changeFilterType.GetHashCode();

                hash = hash * 31 + ((int)Options).GetHashCode();
                return hash;
            }
        }
    }
}
