using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Unity.Scenes.Editor.Tests
{
    [AddComponentMenu("")]
    public class SubSceneLoadTestBufferAuthoring : MonoBehaviour
    {
        public List<int> Ints;
        public List<GameObject> Entities;

    }

    [InternalBufferCapacity(2)]
    public struct SubSceneLoadTestBufferComponent : IBufferElementData
    {
        public Entity Entity;
        public int Int;
        public BlobAssetReference<SubSceneLoadTestBlobAsset> BlobAsset;
    }

    public class SubSceneLoadTestBufferBaker : Baker<SubSceneLoadTestBufferAuthoring>
    {
        public override void Bake(SubSceneLoadTestBufferAuthoring authoring)
        {
            var buf = AddBuffer<SubSceneLoadTestBufferComponent>(GetEntity(authoring));
            for (int i = 0; i < authoring.Ints.Count; i++)
            {
                var entity = GetEntity(authoring.Entities[i]);
                buf.Add(new SubSceneLoadTestBufferComponent
                {
                    Entity = entity,
                    Int = authoring.Ints[i],
                    BlobAsset = SubSceneLoadTestBlobAsset.Make(authoring.Ints[i], authoring.Ints[i] + 1, authoring.gameObject.name + i, SubSceneLoadTestBlobAsset.MakeStrings(i))
                });
            }
        }
    }
}
