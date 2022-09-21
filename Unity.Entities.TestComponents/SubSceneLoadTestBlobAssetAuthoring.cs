using System;
using Unity.Entities;
using UnityEngine;

namespace Unity.Scenes.Editor.Tests
{
    [AddComponentMenu("")]
    public class SubSceneLoadTestBlobAssetAuthoring : MonoBehaviour
    {
        public bool UseNullBlobAsset;
        public int Int;
        public int PtrInt;
        public string String;
        public string[] Strings;
    }

    public struct SubSceneLoadTestBlobAssetComponent : IComponentData
    {
        public BlobAssetReference<SubSceneLoadTestBlobAsset> BlobAsset;
    }

    public class SubsceneLoadTestBlobAssetBaker : Baker<SubSceneLoadTestBlobAssetAuthoring>
    {
        public override void Bake(SubSceneLoadTestBlobAssetAuthoring authoring)
        {
            if (authoring.UseNullBlobAsset)
            {
                AddComponent(GetEntity(authoring), new SubSceneLoadTestBlobAssetComponent
                {
                    BlobAsset = BlobAssetReference<SubSceneLoadTestBlobAsset>.Null
                });
            }
            else
            {
                AddComponent(GetEntity(authoring), new SubSceneLoadTestBlobAssetComponent()
                {
                    BlobAsset = SubSceneLoadTestBlobAsset.Make(authoring.Int, authoring.PtrInt, authoring.String, authoring.Strings)
                });
            }
        }
    }
}
