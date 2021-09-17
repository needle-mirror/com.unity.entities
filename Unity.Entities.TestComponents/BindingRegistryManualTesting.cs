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

    public class BindingRegistryManualTestAuthoring : MonoBehaviour, IConvertGameObjectToEntity
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

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddComponentData(entity,
                new BindingRegistryManualTestComponent
                    {BindFloat = FloatField, BindInt = IntField, BindBool = BoolField});
        }
    }

    public struct BindingRegistryFieldTestComponent : IComponentData
    {
        public float2 BindFloat2;
    }

    public class BindingRegistryField1TestAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        [RegisterBinding(typeof(BindingRegistryFieldTestComponent),
            nameof(BindingRegistryFieldTestComponent.BindFloat2) + ".x")]
        public float2 FloatField = new float2(5.0f, 0.0f);

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddComponentData(entity,
                new BindingRegistryFieldTestComponent
                    {BindFloat2 = FloatField});
        }
    }

    public class BindingRegistryField2TestAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        [RegisterBinding(typeof(BindingRegistryFieldTestComponent),
            nameof(BindingRegistryFieldTestComponent.BindFloat2) + ".y")]
        public float2 FloatField = new float2(0.0f, 5.0f);

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddComponentData(entity,
                new BindingRegistryFieldTestComponent
                    {BindFloat2 = FloatField});
        }
    }
}
