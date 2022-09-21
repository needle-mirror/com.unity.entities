using Unity.Mathematics;
using UnityEngine;

namespace Unity.Entities.Tests
{
    public struct BindingRegistryIntComponent : IComponentData
    {
        public int Int1;
        public int2 Int2;
        public int3 Int3;
        public int4 Int4;
    }

    public class BindingRegistryIntComponentAuthoring : MonoBehaviour
    {
        [RegisterBinding(typeof(BindingRegistryIntComponent), "Int1")]
        public int Int1;
        [RegisterBinding(typeof(BindingRegistryIntComponent), "Int2.x", true)]
        [RegisterBinding(typeof(BindingRegistryIntComponent), "Int2.y", true)]
        public int2 Int2;
        [RegisterBinding(typeof(BindingRegistryIntComponent), "Int3.x", true)]
        [RegisterBinding(typeof(BindingRegistryIntComponent), "Int3.y", true)]
        [RegisterBinding(typeof(BindingRegistryIntComponent), "Int3.z", true)]
        public int3 Int3;
        [RegisterBinding(typeof(BindingRegistryIntComponent), "Int4.x", true)]
        [RegisterBinding(typeof(BindingRegistryIntComponent), "Int4.y", true)]
        [RegisterBinding(typeof(BindingRegistryIntComponent), "Int4.z", true)]
        [RegisterBinding(typeof(BindingRegistryIntComponent), "Int4.w", true)]
        public int4 Int4;

        class Baker : Baker<BindingRegistryIntComponentAuthoring>
        {
            public override void Bake(BindingRegistryIntComponentAuthoring authoring)
            {
                AddComponent(new BindingRegistryIntComponent
                {
                    Int1 = authoring.Int1,
                    Int2 = authoring.Int2,
                    Int3 = authoring.Int3,
                    Int4 = authoring.Int4,
                });
            }
        }
    }
}
