using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.Entities.Tests
{
    public struct BindingRegistryManualTestComponent : IComponentData
    {
        public float BindFloat;
        public int BindInt;
        public bool BindBool;
    }

    public class BindingRegistryManualTestAuthoring : MonoBehaviour
    {
        [RegisterBinding(typeof(BindingRegistryManualTestComponent),
            nameof(BindingRegistryManualTestComponent.BindFloat))]
        public float FloatField = 10.0f;

        [RegisterBinding(typeof(BindingRegistryManualTestComponent),
            nameof(BindingRegistryManualTestComponent.BindInt))]
        public int IntField = 5;

        [RegisterBinding(typeof(BindingRegistryManualTestComponent),
            nameof(BindingRegistryManualTestComponent.BindBool))]
        public bool BoolField = true;

        class Baker : Baker<BindingRegistryManualTestAuthoring>
        {
            public override void Bake(BindingRegistryManualTestAuthoring authoring)
            {
                AddComponent(new BindingRegistryManualTestComponent
                        {BindFloat = authoring.FloatField, BindInt = authoring.IntField, BindBool = authoring.BoolField});
            }
        }
    }

    public struct BindingRegistryFieldTestComponent : IComponentData
    {
        public float2 BindFloat2;
    }

    public class BindingRegistryField1TestAuthoring : MonoBehaviour
    {
        [RegisterBinding(typeof(BindingRegistryFieldTestComponent),
            nameof(BindingRegistryFieldTestComponent.BindFloat2) + ".x")]
        public float2 FloatField = new float2(5.0f, 0.0f);

        class Baker : Baker<BindingRegistryField1TestAuthoring>
        {
            public override void Bake(BindingRegistryField1TestAuthoring authoring)
            {
                AddComponent(new BindingRegistryFieldTestComponent
                    {BindFloat2 = authoring.FloatField});
            }
        }
    }

    public class BindingRegistryField2TestAuthoring : MonoBehaviour
    {
        [RegisterBinding(typeof(BindingRegistryFieldTestComponent),
            nameof(BindingRegistryFieldTestComponent.BindFloat2) + ".y")]
        public float2 FloatField = new float2(0.0f, 5.0f);

        class Baker : Baker<BindingRegistryField2TestAuthoring>
        {
            public override void Bake(BindingRegistryField2TestAuthoring authoring)
            {
                AddComponent(new BindingRegistryFieldTestComponent
                        {BindFloat2 = authoring.FloatField});
            }
        }
    }
}
