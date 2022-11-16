using System;
using Unity.Entities.Tests;
using UnityEngine;

namespace Unity.Entities.TestComponents
{
    public struct MockCollider : IComponentData {}
    public class MockColliderComponent : MonoBehaviour
    {
        void OnEnable() { }

        class MockColliderComponentBaker : Baker<MockColliderComponent>
        {
            public override void Bake(MockColliderComponent authoring)
            {
                var body = GetComponent<MockBodyComponent>();
                if (body == null || !body.enabled)
                {
                    AddComponent(new RequiredComponent {AddedBy = nameof(MockColliderComponent)});
                }
                AddComponent<MockCollider>();
            }
        }
    }
}
