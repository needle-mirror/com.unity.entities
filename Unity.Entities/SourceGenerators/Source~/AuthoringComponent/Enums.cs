namespace Unity.Entities.SourceGen.AuthoringComponent
{
    enum AuthoringComponentInterface
    {
        IComponentData,
        IBufferElementData,
        None
    }

    enum FieldType
    {
        SingleEntity,
        EntityArray,
        NonEntityValueType,
        NonEntityReferenceType
    }
}
