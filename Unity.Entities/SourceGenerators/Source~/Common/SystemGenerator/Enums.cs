using System;

namespace Unity.Entities.SourceGen.SystemGeneratorCommon
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
}
