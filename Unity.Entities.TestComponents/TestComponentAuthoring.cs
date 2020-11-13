using UnityEngine;

namespace Unity.Entities.Tests
{
    [ConverterVersion("sschoener", 1)]
    public class TestComponentAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public int IntValue;
        public Material Material;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddComponentData(entity, new UnmanagedTestComponent
            {
                IntValue = IntValue
            });
#if !NET_DOTS && !UNITY_DISABLE_MANAGED_COMPONENTS
            dstManager.AddComponentObject(entity, new ManagedTestComponent
            {
                Material = Material
            });
#endif
        }

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
    }
}
