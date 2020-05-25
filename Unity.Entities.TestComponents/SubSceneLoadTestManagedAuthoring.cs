using System;
using Unity.Entities;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.Scenes.Editor.Tests
{
    [AddComponentMenu("")]
    [ConverterVersion("unity", 1)]
    public class SubSceneLoadTestManagedAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public GameObject Entity;
        public int Int;
        public string String;
        public Object Asset;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
#if !NET_DOTS && !UNITY_DISABLE_MANAGED_COMPONENTS
            dstManager.AddComponentData(entity, new Component
            {
                Entity = conversionSystem.GetPrimaryEntity(Entity),
                Int = Int,
                String = String,
                Asset = Asset,
            });
#endif
        }

#if !NET_DOTS && !UNITY_DISABLE_MANAGED_COMPONENTS
        public class Component : IComponentData
        {
            // Managed components do not support BlobAssetReference typed fields, hence not tested
            public int Int;
            public string String;
            public Object Asset;
            public Entity Entity;
        }
#endif
    }
}
