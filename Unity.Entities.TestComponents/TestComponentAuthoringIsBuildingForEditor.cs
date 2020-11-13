using UnityEngine;

namespace Unity.Entities.Tests
{
    [ConverterVersion("christopherr", 5)]
    public class TestComponentAuthoringIsBuildingForEditor : MonoBehaviour, IConvertGameObjectToEntity
    {

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
#if UNITY_EDITOR
            if (conversionSystem.IsBuildingForEditor)
                dstManager.AddComponentData(entity, new IntTestData(1));
            else
                dstManager.AddComponentData(entity, new IntTestData(2));
#else
            dstManager.AddComponentData(entity, new IntTestData(3));
#endif
        }
    }
}
