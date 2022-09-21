using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.Entities.Tests
{
    [AddComponentMenu("")]
    public class TestComponentIsActiveAndEnabledAuthoring : MonoBehaviour
    {
        public struct ActiveAndEnabled : IComponentData
        {

        }

        public struct NoActiveAndEnabled : IComponentData
        {

        }

        public GameObject go;
        class Baker : Baker<TestComponentIsActiveAndEnabledAuthoring>
        {
            public override void Bake(TestComponentIsActiveAndEnabledAuthoring authoring)
            {
                var component = GetComponent<TestComponentEnableAuthoring>(authoring.go);
                if (IsActiveAndEnabled(component))
                {
                    AddComponent<ActiveAndEnabled>();
                }
                else
                {
                    AddComponent<NoActiveAndEnabled>();
                }
            }
        }
    }
}
