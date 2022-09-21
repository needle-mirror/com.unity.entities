using System;
using Unity.Collections;
using UnityEngine;

namespace Unity.Entities.Tests
{
    [AddComponentMenu("")]
    public class TestComponentWithBlobAssetAuthoring : MonoBehaviour
    {
        public int Version;
        public struct Component : IComponentData
        {
            public BlobAssetReference<int> BlobAssetRef;
        }

        class Baker : Baker<TestComponentWithBlobAssetAuthoring>
        {
            public override void Bake(TestComponentWithBlobAssetAuthoring authoring)
            {
                var builder = new BlobBuilder(Allocator.Temp);
                builder.ConstructRoot<int>() = authoring.Version;
                var blob = builder.CreateBlobAssetReference<int>(Allocator.Persistent);

                AddBlobAsset(blob, out Hash128 hash);

                AddComponent(new Component
                {
                    BlobAssetRef = blob
                });
                builder.Dispose();
            }
        }
    }
}
