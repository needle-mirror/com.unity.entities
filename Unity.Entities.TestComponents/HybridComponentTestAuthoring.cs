using UnityEngine;

namespace Unity.Entities.Tests
{
    public class HybridComponentTestAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public int Value;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            conversionSystem.AddHybridComponent(this);
        }
    }
}
