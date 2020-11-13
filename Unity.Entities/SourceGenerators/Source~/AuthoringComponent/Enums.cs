namespace Unity.Entities.SourceGen
{
    internal enum AuthoringComponentInterface
    {
        IComponentData,
        IBufferElementData,
        None
    }

    internal enum FieldType
    {
        SingleEntity,
        EntityArray,
        NonEntityValueType,
        NonEntityReferenceType
    }
}
