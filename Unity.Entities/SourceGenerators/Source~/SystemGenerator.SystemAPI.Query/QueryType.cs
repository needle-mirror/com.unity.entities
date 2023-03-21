namespace Unity.Entities.SourceGen.SystemGenerator.SystemAPI.Query
{
    enum QueryType
    {
        RefRW,
        RefRO,
        RefRW_TagComponent,
        RefRO_TagComponent,
        UnmanagedSharedComponent,
        ManagedSharedComponent,
        Aspect,
        DynamicBuffer,
        ValueTypeComponent,
        ManagedComponent,
        UnityEngineComponent,
        EnabledRefRW,
        EnabledRefRO,
        TagComponent,
        Invalid
    }
}
