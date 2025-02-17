using UnityEngine;

namespace Unity.Entities.Tests
{
    public class TestEntityRefComponentAuthoring : MonoBehaviour
    {
        public struct Component : IComponentData
        {
            public Entity other;
            public int value;
        }

        public GameObject other;
        public int value;

        class Baker : Baker<TestEntityRefComponentAuthoring>
        {
            public override void Bake(TestEntityRefComponentAuthoring authoring)
            {
                AddComponent(GetEntity(TransformUsageFlags.None), new Component
                {
                    other = GetEntity(authoring.other, TransformUsageFlags.None),
                    value = authoring.value,
                });
            }
        }
    }
}
