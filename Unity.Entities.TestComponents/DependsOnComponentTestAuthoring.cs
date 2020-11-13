using System.Collections.Generic;
using UnityEngine;

namespace Unity.Entities.Tests
{
    public class DependsOnComponentTestAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public GameObject Other;
        public static Dictionary<GameObject, int> Versions = new Dictionary<GameObject,int>();
        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            conversionSystem.DeclareDependency(gameObject, Other);

            if (Other == null || Other.scene != gameObject.scene)
                dstManager.AddComponentData(entity, new Component {Value = -1});
            else
            {
                Versions.TryGetValue(Other, out var version);
                dstManager.AddComponentData(entity, new Component {Value = version});
            }
        }

        public struct Component : IComponentData
        {
            public int Value;
        }
    }
}
