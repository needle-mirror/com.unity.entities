namespace Unity.Entities.SourceGen.SystemGenerator.SystemAPI.Query;

enum QueryType
{
    RefRW,
    RefRO,
    UnmanagedSharedComponent,
    ManagedSharedComponent,
    Aspect,
    DynamicBuffer,
    ValueTypeComponent,
    ManagedComponent,
    UnityEngineComponent,
    EnabledRefRW_ComponentData,
    EnabledRefRO_ComponentData,
    EnabledRefRW_BufferElementData,
    EnabledRefRO_BufferElementData,
    TagComponent,
    Invalid
}
