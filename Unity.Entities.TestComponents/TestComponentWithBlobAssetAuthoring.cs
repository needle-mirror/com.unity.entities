using System;
using Unity.Collections;
using UnityEngine;

namespace Unity.Entities.Tests
{
    [ConverterVersion("sschoener", 1)]
    public class TestComponentWithBlobAssetAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public int Version;
        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            var builder = new BlobBuilder(Allocator.Temp);
            builder.ConstructRoot<int>() = Version;
            dstManager.AddComponentData(entity, new Component
            {
                BlobAssetRef = builder.CreateBlobAssetReference<int>(Allocator.Persistent)
            });
        }

        public struct Component : IComponentData
        {
            public BlobAssetReference<int> BlobAssetRef;
        }
    }
}
