using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Unity.Scenes.Editor.Tests
{
    [AddComponentMenu("")]
    [ConverterVersion("unity", 1)]
    public class SubSceneLoadTestBufferAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public List<int> Ints;
        public List<GameObject> Entities;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            var buf = dstManager.AddBuffer<Component>(entity);
            for (int i = 0; i < Ints.Count; i++)
            {
                buf.Add(new Component
                {
                    Entity = conversionSystem.GetPrimaryEntity(Entities[i]),
                    Int = Ints[i],
                    BlobAsset = SubSceneLoadTestBlobAsset.Make(Ints[i], Ints[i] + 1, gameObject.name + i, SubSceneLoadTestBlobAsset.MakeStrings(i))
                });
            }
        }

        [InternalBufferCapacity(2)]
        public struct Component : IBufferElementData
        {
            public Entity Entity;
            public int Int;
            public BlobAssetReference<SubSceneLoadTestBlobAsset> BlobAsset;
        }
    }
}
