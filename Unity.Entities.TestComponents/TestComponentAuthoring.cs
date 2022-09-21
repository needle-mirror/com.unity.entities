using UnityEngine;

namespace Unity.Entities.Tests
{
    [AddComponentMenu("")]
    public class TestComponentAuthoring : MonoBehaviour
    {
        public int IntValue;
        public Material Material;

#if !NET_DOTS && !UNITY_DISABLE_MANAGED_COMPONENTS
        public class ManagedTestComponent : IComponentData
        {
            public Material Material;
        }
#endif
        public struct UnmanagedTestComponent : IComponentData
        {
            public int IntValue;
        }

        class Baker : Baker<TestComponentAuthoring>
        {
            public override void Bake(TestComponentAuthoring authoring)
            {
                AddComponent(new TestComponentAuthoring.UnmanagedTestComponent
                {
                    IntValue = authoring.IntValue
                });
#if !NET_DOTS && !UNITY_DISABLE_MANAGED_COMPONENTS
                AddComponentObject(new TestComponentAuthoring.ManagedTestComponent
                {
                    Material = authoring.Material
                });
#endif
            }
        }
    }
}
