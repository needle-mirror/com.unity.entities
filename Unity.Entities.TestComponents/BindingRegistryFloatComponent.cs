using Unity.Mathematics;

namespace Unity.Entities.Tests
{
    public struct BindingRegistryFloatComponent : IComponentData
    {
        public float Float1;
        public float2 Float2;
        public float3 Float3;
        public float4 Float4;
    }

    public class BindingRegistryFloatComponentAuthoring : UnityEngine.MonoBehaviour
    {
        [RegisterBinding(typeof(BindingRegistryFloatComponent), "Float1")]
        public float Float1;
        [RegisterBinding(typeof(BindingRegistryFloatComponent), "Float2.x", true)]
        [RegisterBinding(typeof(BindingRegistryFloatComponent), "Float2.y", true)]
        public float2 Float2;
        [RegisterBinding(typeof(BindingRegistryFloatComponent), "Float3.x", true)]
        [RegisterBinding(typeof(BindingRegistryFloatComponent), "Float3.y", true)]
        [RegisterBinding(typeof(BindingRegistryFloatComponent), "Float3.z", true)]
        public float3 Float3;
        [RegisterBinding(typeof(BindingRegistryFloatComponent), "Float4.x", true)]
        [RegisterBinding(typeof(BindingRegistryFloatComponent), "Float4.y", true)]
        [RegisterBinding(typeof(BindingRegistryFloatComponent), "Float4.z", true)]
        [RegisterBinding(typeof(BindingRegistryFloatComponent), "Float4.w", true)]
        public float4 Float4;
        class BindingRegistryFloatComponentBaker : Unity.Entities.Baker<BindingRegistryFloatComponentAuthoring>
        {
            public override void Bake(BindingRegistryFloatComponentAuthoring authoring)
            {
                Unity.Entities.Tests.BindingRegistryFloatComponent component = default(Unity.Entities.Tests.BindingRegistryFloatComponent);
                component.Float1 = authoring.Float1;
                component.Float2 = authoring.Float2;
                component.Float3 = authoring.Float3;
                component.Float4 = authoring.Float4;
                AddComponent(component);
            }
        }
    }
}
