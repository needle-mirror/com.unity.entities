using Unity.Burst;
using Unity.Collections;

namespace Unity.Entities
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    partial class RetainBlobAssetSystem : SystemBase
    {
        protected unsafe override void OnUpdate()
        {
            foreach (var (blobOwner, entity) in
                     SystemAPI.Query<BlobAssetOwner>()
                         .WithAll<RetainBlobAssets>()
                         .WithNone<RetainBlobAssetBatchPtr>().WithEntityAccess())
            {
                BlobAssetBatch.Retain(blobOwner.BlobAssetBatchPtr);
                EntityManager.AddComponentData(entity, new RetainBlobAssetBatchPtr { BlobAssetBatchPtr = blobOwner.BlobAssetBatchPtr});
            }

            var retainBlobAssetBatchQuery = SystemAPI.QueryBuilder().WithNone<BlobAssetOwner>().WithAll<RetainBlobAssets>()
                .WithAll<RetainBlobAssetBatchPtr>().Build();
            var retainBlobAssetBatchEntities = retainBlobAssetBatchQuery.ToEntityArray(Allocator.Temp);
            foreach (var entity in retainBlobAssetBatchEntities)
            {
                var retainBlobAssets = EntityManager.GetComponentData<RetainBlobAssets>(entity);
                if (retainBlobAssets.FramesToRetainBlobAssets-- <= 0)
                {
                    EntityManager.RemoveComponent<RetainBlobAssets>(entity);
                    EntityManager.RemoveComponent<RetainBlobAssetBatchPtr>(entity);
                }
                else
                    EntityManager.SetComponentData(entity, retainBlobAssets);
            }

            var retainBlobAssetsQuery = SystemAPI.QueryBuilder().WithNone<BlobAssetOwner>().WithAll<RetainBlobAssets>()
                .WithAll<RetainBlobAssetPtr>().Build();
            var retainBlobAssetsEntities = retainBlobAssetsQuery.ToEntityArray(Allocator.Temp);
            foreach (var entity in retainBlobAssetsEntities)
            {
                var retainBlobAssets = EntityManager.GetComponentData<RetainBlobAssets>(entity);
                var retainBlobAssetPtr = EntityManager.GetComponentData<RetainBlobAssetPtr>(entity);
                if (retainBlobAssets.FramesToRetainBlobAssets-- <= 0)
                {
                    retainBlobAssetPtr.BlobAsset->Invalidate();
                    Memory.Unmanaged.Free(retainBlobAssetPtr.BlobAsset, Allocator.Persistent);
                    EntityManager.RemoveComponent<RetainBlobAssets>(entity);
                    EntityManager.RemoveComponent<RetainBlobAssetPtr>(entity);
                }
                else
                    EntityManager.SetComponentData(entity, retainBlobAssets);
            }
        }

        protected override unsafe void OnDestroy()
        {
            foreach (var retainPtr in SystemAPI.Query<RefRO<RetainBlobAssetBatchPtr>>()
                         .WithAll<RetainBlobAssets>())
            {
                BlobAssetBatch.Release(retainPtr.ValueRO.BlobAssetBatchPtr);
            }

            foreach (var retainPtr in SystemAPI.Query<RefRO<RetainBlobAssetPtr>>()
                         .WithAll<RetainBlobAssets>())
            {
                retainPtr.ValueRO.BlobAsset->Invalidate();
                Memory.Unmanaged.Free(retainPtr.ValueRO.BlobAsset, Allocator.Persistent);
            }
        }
    }
}
