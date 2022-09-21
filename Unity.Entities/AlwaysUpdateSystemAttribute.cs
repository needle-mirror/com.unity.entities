using System;

namespace Unity.Entities
{
    /// <summary>
    /// This attribute is deprecated, and has any effect.
    /// </summary>
    /// <remarks>
    /// Previously, this attribute would cause a system to update even if none of its registered queries matched any entities.
    ///
    /// Systems now always update by default. Use <see cref="RequireMatchingQueriesForUpdateAttribute"/> if you want a
    /// System to only update if one of its queries is non-empty.
    /// </remarks>
    [Obsolete("AlwaysUpdateSystem is deprecated. Systems now always update by default. Use RequireMatchingQueriesForUpdate if you want a System to only update if one of its queries matches. (RemovedAfter Entities 1.0)")]
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public class AlwaysUpdateSystemAttribute : Attribute
    {
    }
}
