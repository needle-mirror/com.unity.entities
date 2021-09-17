using Unity.Mathematics;

namespace Unity.Entities.Tests
{
    [GenerateAuthoringComponent]
    public struct BindingRegistryIntComponent : IComponentData
    {
        public int Int1;
        public int2 Int2;
        public int3 Int3;
        public int4 Int4;
    }
}
