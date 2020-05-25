using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Unity.Scenes.Editor.Tests
{
    [AddComponentMenu("")]
    [ConverterVersion("unity", 2)]
    public class SubSceneLoadTestUnmanagedAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public GameObject Entity;
        public int Int;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            var e = conversionSystem.GetPrimaryEntity(Entity);
            dstManager.AddComponentData(entity, new Component
            {
                Entity = conversionSystem.GetPrimaryEntity(Entity),
                Int = Int,
                BlobAsset = SubSceneLoadTestBlobAsset.Make(Int, Int + 1, gameObject.name, SubSceneLoadTestBlobAsset.MakeStrings(1))
            });
        }

        public struct Component : IComponentData
        {
            public int Int;
            public BlobAssetReference<SubSceneLoadTestBlobAsset> BlobAsset;
            public Entity Entity;
        }
    }
}
