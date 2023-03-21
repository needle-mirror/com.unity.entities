using System;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.Entities.SourceGen.SystemGenerator.Common
{
    [Flags]
    public enum EntityQueryOptions
    {
        Default = 0,
        IncludePrefab = 1,
        IncludeDisabledEntities = 2,
        FilterWriteGroup = 4,
        IgnoreComponentEnabledState = 8,
        IncludeSystems = 16,
    }

    public static class EntityQueryOptionsExtensions
    {
        public static EntityQueryOptions GetEntityQueryOptions(this MemberAccessExpressionSyntax expression)
        {
            return expression.Name.Identifier.ValueText switch
            {
                "Default" => EntityQueryOptions.Default,
                "IncludePrefab" => EntityQueryOptions.IncludePrefab,
                "IncludeDisabledEntities" => EntityQueryOptions.IncludeDisabledEntities,
                "FilterWriteGroup" => EntityQueryOptions.FilterWriteGroup,
                "IgnoreComponentEnabledState" => EntityQueryOptions.IgnoreComponentEnabledState,
                "IncludeSystems" => EntityQueryOptions.IncludeSystems,
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }
}
