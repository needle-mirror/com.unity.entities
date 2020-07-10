using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
#if !NET_DOTS
using Unity.Properties;
#endif

namespace Unity.Entities
{
    [DisableAutoCreation]
    unsafe class EntityPatcherBlobAssetSystem : ComponentSystem
    {
        DynamicBlobAssetBatch* m_BlobAssetBatchPtr;

        static EntityQueryDesc s_EntityGuidQueryDesc;

        static EntityQueryDesc EntityGuidQueryDesc
        {
            get
            {
                return s_EntityGuidQueryDesc ?? (s_EntityGuidQueryDesc = new EntityQueryDesc
                {
                    All = new ComponentType[]
                    {
                        typeof(EntityGuid)
                    },
                    Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab
                });
            }
        }

        protected override void OnCreate()
        {
            m_BlobAssetBatchPtr = DynamicBlobAssetBatch.Allocate(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            DynamicBlobAssetBatch.Free(m_BlobAssetBatchPtr);
            m_BlobAssetBatchPtr = null;
        }

        public void SetFramesToRetainBlobAssets(int framesToRetainBlobAssets)
        {
            m_BlobAssetBatchPtr->SetFramesToRetainBlobAssets(framesToRetainBlobAssets);
        }

        public void AllocateBlobAsset(void* ptr, int len, ulong hash)
        {
            m_BlobAssetBatchPtr->AllocateBlobAsset(ptr, len, hash);
        }

        public void ReleaseBlobAsset(EntityManager entityManager, ulong hash)
        {
            m_BlobAssetBatchPtr->ReleaseBlobAsset(entityManager, hash);
        }

        public bool TryGetBlobAsset(ulong hash, out BlobAssetPtr ptr)
        {
            return m_BlobAssetBatchPtr->TryGetBlobAsset(hash, out ptr);
        }

        public void ReleaseUnusedBlobAssets()
        {
            using (var chunks = EntityManager.CreateEntityQuery(EntityGuidQueryDesc).CreateArchetypeChunkArray(Allocator.TempJob))
            using (var blobAssets = EntityDiffer.GetBlobAssetsWithDistinctHash(EntityManager.GetCheckedEntityDataAccess()->ManagedComponentStore, chunks, Allocator.TempJob))
            {
                m_BlobAssetBatchPtr->RemoveUnusedBlobAssets(blobAssets.BlobAssetsMap);
            }
        }

        protected override void OnUpdate() {}
    }

    public static unsafe partial class EntityPatcher
    {
        static void ApplyBlobAssetChanges(
            EntityManager entityManager,
            NativeArray<EntityGuid> packedEntityGuids,
            NativeMultiHashMap<int, Entity> packedEntities,
            NativeArray<ComponentType> packedTypes,
            NativeArray<BlobAssetChange> createdBlobAssets,
            NativeArray<byte> createdBlobAssetData,
            NativeArray<ulong> destroyedBlobAssets,
            NativeArray<BlobAssetReferenceChange> blobAssetReferenceChanges)
        {
            if (createdBlobAssets.Length == 0 && blobAssetReferenceChanges.Length == 0)
                return;

            s_ApplyBlobAssetChangesProfilerMarker.Begin();

            var managedObjectBlobAssetReferencePatches = new NativeMultiHashMap<EntityComponentPair, ManagedObjectBlobAssetReferencePatch>(blobAssetReferenceChanges.Length, Allocator.Temp);

            var patcherBlobAssetSystem = entityManager.World.GetOrCreateSystem<EntityPatcherBlobAssetSystem>();

            var blobAssetDataPtr = (byte*)createdBlobAssetData.GetUnsafePtr();

            for (var i = 0; i < createdBlobAssets.Length; i++)
            {
                if (!patcherBlobAssetSystem.TryGetBlobAsset(createdBlobAssets[i].Hash, out _))
                {
                    patcherBlobAssetSystem.AllocateBlobAsset(blobAssetDataPtr, createdBlobAssets[i].Length, createdBlobAssets[i].Hash);
                }

                blobAssetDataPtr += createdBlobAssets[i].Length;
            }

            for (var i = 0; i < destroyedBlobAssets.Length; i++)
            {
                patcherBlobAssetSystem.ReleaseBlobAsset(entityManager, destroyedBlobAssets[i]);
            }

            for (var i = 0; i < blobAssetReferenceChanges.Length; i++)
            {
                var packedComponent = blobAssetReferenceChanges[i].Component;
                var component = packedTypes[packedComponent.PackedTypeIndex];
                var targetOffset = blobAssetReferenceChanges[i].Offset;

                BlobAssetReferenceData targetBlobAssetReferenceData;
                if (patcherBlobAssetSystem.TryGetBlobAsset(blobAssetReferenceChanges[i].Value, out var blobAssetPtr))
                {
                    targetBlobAssetReferenceData = new BlobAssetReferenceData {m_Ptr = (byte*)blobAssetPtr.Data};
                }

                if (packedEntities.TryGetFirstValue(packedComponent.PackedEntityIndex, out var entity, out var iterator))
                {
                    do
                    {
                        if (!entityManager.Exists(entity))
                        {
                            Debug.LogWarning($"ApplyBlobAssetReferencePatches<{component}>({packedEntityGuids[packedComponent.PackedEntityIndex]}) but entity to patch does not exist.");
                        }
                        else if (!entityManager.HasComponent(entity, component))
                        {
                            Debug.LogWarning($"ApplyBlobAssetReferencePatches<{component}>({packedEntityGuids[packedComponent.PackedEntityIndex]}) but component in entity to patch does not exist.");
                        }
                        else
                        {
                            if (component.IsBuffer)
                            {
                                var pointer = (byte*)entityManager.GetBufferRawRW(entity, component.TypeIndex);
                                UnsafeUtility.MemCpy(pointer + targetOffset, &targetBlobAssetReferenceData, sizeof(BlobAssetReferenceData));
                            }
                            else if (component.IsManagedComponent || component.IsSharedComponent)
                            {
                                managedObjectBlobAssetReferencePatches.Add(
                                    new EntityComponentPair { Entity = entity, Component = component },
                                    new ManagedObjectBlobAssetReferencePatch { Id = targetOffset, Target = blobAssetReferenceChanges[i].Value });
                            }
                            else
                            {
                                var pointer = (byte*)entityManager.GetComponentDataRawRW(entity, component.TypeIndex);
                                UnsafeUtility.MemCpy(pointer + targetOffset, &targetBlobAssetReferenceData, sizeof(BlobAssetReferenceData));
                            }
                        }
                    }
                    while (packedEntities.TryGetNextValue(out entity, ref iterator));
                }
            }
            s_ApplyBlobAssetChangesProfilerMarker.End();

#if !UNITY_DOTSRUNTIME
            var managedObjectPatcher = new ManagedObjectEntityBlobAssetReferencePatcher();

            // Apply all managed entity patches
            using (var keys = managedObjectBlobAssetReferencePatches.GetKeyArray(Allocator.Temp))
            {
                foreach (var pair in keys)
                {
                    var patches = managedObjectBlobAssetReferencePatches.GetValuesForKey(pair);

                    if (pair.Component.IsManagedComponent)
                    {
                        var obj = entityManager.GetComponentObject<object>(pair.Entity, pair.Component);
                        managedObjectPatcher.ApplyPatches(ref obj, patches);
                    }
                    else if (pair.Component.IsSharedComponent)
                    {
                        var obj = entityManager.GetSharedComponentData(pair.Entity, pair.Component.TypeIndex);
                        managedObjectPatcher.ApplyPatches(ref obj, patches);
                        entityManager.SetSharedComponentDataBoxedDefaultMustBeNull(pair.Entity, pair.Component.TypeIndex, obj);
                    }

                    patches.Dispose();
                }
            }
#endif

            managedObjectBlobAssetReferencePatches.Dispose();

            // Workaround to catch some special cases where the memory is never released. (e.g. reloading a scene, toggling live-link on/off).
            patcherBlobAssetSystem.ReleaseUnusedBlobAssets();
        }

#if !UNITY_DOTSRUNTIME
        class ManagedObjectEntityBlobAssetReferencePatcher : PropertyVisitor, Properties.Adapters.IVisit<BlobAssetReferenceData>
        {
            NativeMultiHashMap<EntityComponentPair, ManagedObjectBlobAssetReferencePatch>.Enumerator Patches;

            public ManagedObjectEntityBlobAssetReferencePatcher()
            {
                AddAdapter(this);
            }

            public void ApplyPatches(ref object obj, NativeMultiHashMap<EntityComponentPair, ManagedObjectBlobAssetReferencePatch>.Enumerator patches)
            {
                Patches = patches;
                PropertyContainer.Visit(obj, this);
            }

            VisitStatus Properties.Adapters.IVisit<BlobAssetReferenceData>.Visit<TContainer>(Property<TContainer, BlobAssetReferenceData> property, ref TContainer container, ref BlobAssetReferenceData value)
            {
                return VisitStatus.Stop;
            }
        }
#endif
    }
}
