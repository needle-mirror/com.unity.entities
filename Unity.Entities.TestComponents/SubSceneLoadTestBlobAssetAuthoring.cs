using System;
using Unity.Entities;
using UnityEngine;

namespace Unity.Scenes.Editor.Tests
{
    [AddComponentMenu("")]
    [ConverterVersion("unity", 1)]
    public class SubSceneLoadTestBlobAssetAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public bool UseNullBlobAsset;
        public int Int;
        public int PtrInt;
        public string String;
        public string[] Strings;
        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            if (UseNullBlobAsset)
            {
                dstManager.AddComponentData(entity, new Component
                {
                    BlobAsset = BlobAssetReference<SubSceneLoadTestBlobAsset>.Null
                });
            }
            else
            {
                dstManager.AddComponentData(entity, new Component
                {
                    BlobAsset = SubSceneLoadTestBlobAsset.Make(Int, PtrInt, String, Strings)
                });
            }
        }

        public struct Component : IComponentData
        {
            public BlobAssetReference<SubSceneLoadTestBlobAsset> BlobAsset;
        }
    }
}
