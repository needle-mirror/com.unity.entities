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

static class QueryTypeExtensions
{
    // Will match GenerateCompleteDependenciesMethod found in IfeStructWriter.cs
    public static bool DoesRequireTypeParameter(this QueryType queryType) =>
        queryType is QueryType.RefRO or QueryType.RefRW
            or QueryType.DynamicBuffer or QueryType.UnityEngineComponent
            or QueryType.EnabledRefRO_ComponentData or QueryType.EnabledRefRW_ComponentData
            or QueryType.EnabledRefRO_BufferElementData or QueryType.EnabledRefRW_BufferElementData;
}
