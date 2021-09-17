using Unity.Mathematics;

namespace Unity.Entities.Tests
{
    [GenerateAuthoringComponent]
    public struct BindingRegistryFloatComponent : IComponentData
    {
        public float Float1;
        public float2 Float2;
        public float3 Float3;
        public float4 Float4;
    }
}
