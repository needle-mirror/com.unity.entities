using UnityEngine;

namespace Unity.Entities.Tests
{
    public class DependsOnComponentTransitiveTestAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public int SelfValue;
        public DependsOnComponentTransitiveTestAuthoring Dependency;
        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            conversionSystem.DeclareDependency(gameObject, Dependency);
            dstManager.AddComponentData(entity, new Component
            {
                Value = FindValue(this)
            });
        }

        static int FindValue(DependsOnComponentTransitiveTestAuthoring a)
        {
            int dist = 0;
            while (a.Dependency != null)
            {
                a = a.Dependency;
                dist++;
            }
            return a.SelfValue + dist;
        }

        public struct Component : IComponentData
        {
            public int Value;
        }
    }
}
