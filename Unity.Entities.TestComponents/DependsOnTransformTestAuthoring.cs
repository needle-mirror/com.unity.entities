using UnityEngine;

namespace Unity.Entities.Tests
{
    public class DependsOnTransformTestAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public Transform Dependency;
        public bool SkipDependency;
        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            if (Dependency == null)
                return;
            if (!SkipDependency)
                conversionSystem.DeclareDependency(gameObject, Dependency);
            dstManager.AddComponentData(entity, new Component
            {
                LocalToWorld = Dependency.localToWorldMatrix
            });
        }

        public struct Component : IComponentData
        {
            public Matrix4x4 LocalToWorld;
        }
    }
}
