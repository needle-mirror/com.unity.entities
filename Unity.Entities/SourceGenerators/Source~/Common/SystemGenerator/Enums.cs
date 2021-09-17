using System;

namespace Unity.Entities.SourceGen.SystemGeneratorCommon
{
    [Flags]
    public enum EntityQueryOptions
    {
        Default = 0,
        IncludePrefab = 1,
        IncludeDisabled = 2,
        FilterWriteGroup = 4,
    }
}
