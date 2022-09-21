using System;
using UnityEngine;

namespace Unity.Entities.Tests
{
    [AddComponentMenu("")]
    public class MockSharedDataAuthoring : MonoBehaviour
    {
        public int Value;

        class Baker : Baker<MockSharedDataAuthoring>
        {
            public override void Bake(MockSharedDataAuthoring authoring)
            {
                AddSharedComponent(new MockSharedData
                {
                    Value = authoring.Value
                });
            }
        }
    }

    [Serializable]
    public struct MockSharedData : ISharedComponentData
    {
        public int Value;
    }
}
