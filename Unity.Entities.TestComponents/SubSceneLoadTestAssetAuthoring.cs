using System;
using Unity.Entities;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.Scenes.Editor.Tests
{
    [AddComponentMenu("")]
    [ConverterVersion("unity", 1)]
    public class SubSceneLoadTestAssetAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public Object Asset;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
#if !NET_DOTS && !UNITY_DISABLE_MANAGED_COMPONENTS
            dstManager.AddComponentData(entity, new Component
            {
                Asset = Asset
            });
#endif
        }

#if !NET_DOTS && !UNITY_DISABLE_MANAGED_COMPONENTS
        public class Component : IComponentData
        {
            public Object Asset;
        }
#endif
    }
}
