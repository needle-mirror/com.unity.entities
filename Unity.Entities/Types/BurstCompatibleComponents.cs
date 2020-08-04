namespace Unity.Entities
{
    // This exists only to be able to make generic instances of generic methods to test burst compatiblity.
    internal struct BurstCompatibleComponentData : IComponentData
    {
        public int UnusedField;
    }

    // This exists only to be able to make generic instances of generic methods to test burst compatiblity.
    internal struct BurstCompatibleSharedComponentData : ISharedComponentData
    {
        public int UnusedField;
    }

    // This exists only to be able to make generic instances of generic methods to test burst compatiblity.
    internal struct BurstCompatibleBufferElement : IBufferElementData
    {
        public int UnusedField;
    }
}
