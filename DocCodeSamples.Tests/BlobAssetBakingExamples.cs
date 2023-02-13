using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// The files in this namespace are used to test the code samples in the documentation.
namespace Doc.CodeSamples.Tests
{

    #region BlobAssetBakerSetup
    struct MarketData
    {
        public float PriceOranges;
        public float PriceApples;
    }

    struct MarketDataComponent : IComponentData
    {
        public BlobAssetReference<MarketData> Blob;
    }

    public class MarketDataAuthoring : MonoBehaviour
    {
        public float PriceOranges;
        public float PriceApples;
    }
    #endregion

    #region SimpleBlobAssetBaker
    class MarketDataBaker : Baker<MarketDataAuthoring>
    {
        public override void Bake(MarketDataAuthoring authoring)
        {
            // Create a new builder that will use temporary memory to construct the blob asset
            var builder = new BlobBuilder(Allocator.Temp);

            // Construct the root object for the blob asset. Notice the use of `ref`.
            ref MarketData marketData = ref builder.ConstructRoot<MarketData>();

            // Now fill the constructed root with the data:
            // Apples compare to Oranges in the universally accepted ratio of 2 : 1 .
            marketData.PriceApples = authoring.PriceApples;
            marketData.PriceOranges = authoring.PriceOranges;

            // Now copy the data from the builder into its final place, which will
            // use the persistent allocator
            var blobReference =
                builder.CreateBlobAssetReference<MarketData>(Allocator.Persistent);

            // Make sure to dispose the builder itself so all internal memory is disposed.
            builder.Dispose();

            // Register the Blob Asset to the Baker for de-duplication and reverting.
            AddBlobAsset<MarketData>(ref blobReference, out var hash);
            AddComponent(new MarketDataComponent() {Blob = blobReference});
        }
    }
    #endregion

    #region CustomHashBlobAssetBaker
    class MarketDataCustomHashBaker : Baker<MarketDataAuthoring>
    {
        public override void Bake(MarketDataAuthoring authoring)
        {
            var customHash = new Unity.Entities.Hash128(
                (uint) authoring.PriceOranges.GetHashCode(),
                (uint) authoring.PriceApples.GetHashCode(), 0, 0);

            if (!TryGetBlobAssetReference(customHash,
                    out BlobAssetReference<MarketData> blobReference))
            {
                // Create a new builder that will use temporary memory to construct the blob asset
                var builder = new BlobBuilder(Allocator.Temp);

                // Construct the root object for the blob asset. Notice the use of `ref`.
                ref MarketData marketData = ref builder.ConstructRoot<MarketData>();

                // Now fill the constructed root with the data:
                // Apples compare to Oranges in the universally accepted ratio of 2 : 1 .
                marketData.PriceApples = authoring.PriceApples;
                marketData.PriceOranges = authoring.PriceOranges;

                // Now copy the data from the builder into its final place, which will
                // use the persistent allocator
                blobReference =
                    builder.CreateBlobAssetReference<MarketData>(Allocator.Persistent);

                // Make sure to dispose the builder itself so all internal memory is disposed.
                builder.Dispose();

                // Register the Blob Asset to the Baker for de-duplication and reverting.
                AddBlobAssetWithCustomHash<MarketData>(ref blobReference, customHash);
            }

            AddComponent(new MarketDataComponent() {Blob = blobReference});
        }
    }
    #endregion


    public struct BBBlobAsset
    {
        public float3 MinBoundingBox;
        public float3 MaxBoundingBox;
    }

    public struct MeshComponent : IComponentData
    {
        public float3 MinBoundingBox;
        public float3 MaxBoundingBox;
        public Unity.Entities.Hash128 Hash;
    }

    public struct BoundingBoxComponent : IComponentData
    {
        public BlobAssetReference<BBBlobAsset> BlobData;
    }

    #region BlobAssetBakingSystemSetup
    public struct CleanupComponent : ICleanupComponentData
    {
        public Unity.Entities.Hash128 Hash;
    }
    #endregion

    #region BlobAssetBakingSystem
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    public partial struct ComputeBlobAssetSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            // Get the BlobAssetStore from the BakingSystem
            var blobAssetStore =
                state.World.GetExistingSystemManaged<BakingSystem>().BlobAssetStore;

            // Handles the cleanup of BlobAssets for when BakingSystems are reverted
            HandleCleanup(ref state, blobAssetStore);

            foreach (var (mesh, bb, cleanup) in SystemAPI
                         .Query<RefRO<MeshComponent>, RefRW<BoundingBoxComponent>,
                             RefRW<CleanupComponent>>())
            {
                var hash = mesh.ValueRO.Hash;
                BlobAssetReference<BBBlobAsset> blobAssetReference;

                // If the BlobAsset doesn't exist yet
                if (!blobAssetStore.TryGet<BBBlobAsset>(hash, out blobAssetReference))
                {
                    // Create a new BlobAsset
                    var builder = new BlobBuilder(Allocator.Temp);
                    ref var root = ref builder.ConstructRoot<BBBlobAsset>();

                    root.MinBoundingBox = mesh.ValueRO.MinBoundingBox;
                    root.MaxBoundingBox = mesh.ValueRO.MaxBoundingBox;

                    blobAssetReference =
                        builder.CreateBlobAssetReference<BBBlobAsset>(Allocator.Persistent);

                    // Make sure to dispose the builder itself so all internal memory is disposed.
                    builder.Dispose();
                }

                // Update the Entity and BlobAssetStore with the new BlobAsset if it has changed
                // since last run or is newly created
                if (cleanup.ValueRO.Hash != hash || !bb.ValueRO.BlobData.IsCreated)
                {
                    // Add the new BlobAsset to the component and the BlobAssetStore
                    blobAssetStore.TryAdd(hash, ref blobAssetReference);
                    bb.ValueRW.BlobData = blobAssetReference;

                    // Cleanup of the 'previous' BlobAsset if it existed.
                    blobAssetStore.TryRemove<BBBlobAsset>(cleanup.ValueRO.Hash, true);

                    // Update the cleanup component with the current hash
                    cleanup.ValueRW.Hash = hash;
                }
            }
        }

        public void HandleCleanup(ref SystemState state, BlobAssetStore blobAssetStore)
        {
            // Add the Cleanup Component to the newly created Entities, that do not have it yet
            var addCleanupQuery = SystemAPI.QueryBuilder()
                .WithAll<BoundingBoxComponent>().WithNone<CleanupComponent>().Build();
            state.EntityManager.AddComponent<CleanupComponent>(addCleanupQuery);

            // Cleanup the BlobAssets and Cleanup Components of newly destroyed Entities
            // Cleanup of the BlobAssets through the BlobAssetStore
            foreach (var cleanup in SystemAPI
                         .Query<RefRO<CleanupComponent>>().WithNone<BoundingBoxComponent>())
            {
                blobAssetStore.TryRemove<BBBlobAsset>(cleanup.ValueRO.Hash, true);
            }

            // Remove the Cleanup Component from the destroyed Entities
            var removeCleanupQuery = SystemAPI.QueryBuilder()
                .WithAll<CleanupComponent>().WithNone<BoundingBoxComponent>().Build();
            state.EntityManager.RemoveComponent<CleanupComponent>(removeCleanupQuery);
        }
    }
    #endregion
}
