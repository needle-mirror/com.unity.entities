#if !UNITY_DISABLE_MANAGED_COMPONENTS
using UnityEngine;

namespace Unity.Entities.Tests
{
    [AddComponentMenu("")]
    public class CompanionComponentTestAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public int Value;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            conversionSystem.AddTypeToCompanionWhiteList(typeof(CompanionComponentTestAuthoring));
            dstManager.AddComponentObject(entity, this);
        }
    }
}
#endif
