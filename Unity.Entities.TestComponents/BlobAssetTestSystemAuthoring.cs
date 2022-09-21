using Unity.Collections;
using UnityEngine;

namespace Unity.Entities.TestComponents
{
    public class BlobAssetTestSystemAuthoring : MonoBehaviour
    {
        public int blobValue = 0;
    }

    [TemporaryBakingType]
    public struct TempBlobAssetData : IComponentData
    {
        public int blobValue;
        public Hash128 blobHash;
        public int gameObjectId;
    }

    [DisableAutoCreation]
    public class BlobAssetTestSystemBaker : Baker<BlobAssetTestSystemAuthoring>
    {
        public override void Bake(BlobAssetTestSystemAuthoring authoring)
        {
            var customHash = authoring.blobValue.GetHashCode();
            var customCustomHash = new Hash128((uint) customHash, 1, 2, 3);

            AddComponent(new TempBlobAssetData()
            {
                blobValue = authoring.blobValue,
                blobHash = customCustomHash,
                gameObjectId = authoring.gameObject.GetInstanceID()
            });
        }
    }

    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    public partial class BlobAssetStoreRefCountingBakingSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var bakingSystem = World.GetExistingSystemManaged<BakingSystem>();
            var blobAssetStore = bakingSystem.BlobAssetStore;
            using (var context = new BlobAssetComputationContext<int, int>(blobAssetStore, 16, Allocator.Temp))
            {
                Entities.ForEach((in TempBlobAssetData blobData) =>
                {
                    // Create the blob
                    context.AssociateBlobAssetWithUnityObject(blobData.blobHash, blobData.gameObjectId);
                    if (context.NeedToComputeBlobAsset(blobData.blobHash))
                    {
                        context.AddBlobAssetToCompute(blobData.blobHash, 0);

                        // Create the blob reference
                        BlobBuilder builder = new BlobBuilder(Allocator.TempJob);
                        ref var data = ref builder.ConstructRoot<int>();
                        data = blobData.blobValue;
                        var blobAssetReference = builder.CreateBlobAssetReference<int>(Allocator.Persistent);
                        builder.Dispose();

                        context.AddComputedBlobAsset(blobData.blobHash, blobAssetReference);
                    }
                }).Run();

                // Force to update the blob store
                context.UpdateBlobStore();
            }
        }
    }
}
