using System;
using System.Runtime.CompilerServices;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unity.Entities
{
    internal unsafe partial struct EntityComponentStore
    {
        // ----------------------------------------------------------------------------------------------------------
        // PUBLIC
        // ----------------------------------------------------------------------------------------------------------

        public void CreateEntities(Archetype* archetype, Entity* entities, int count)
        {
            var archetypeChunkFilter = new ArchetypeChunkFilter();
            archetypeChunkFilter.Archetype = archetype;

            while (count != 0)
            {
                var chunk = GetChunkWithEmptySlots(ref archetypeChunkFilter);
                var allocateCount = math.min(count, chunk->UnusedCount);

                ChunkDataUtility.Allocate(chunk, entities, allocateCount);

                count -= allocateCount;

                if (entities != null)
                    entities += allocateCount;
            }
        }

        public void DestroyEntities(EntityQueryImpl* queryImpl, ref BufferTypeHandle<LinkedEntityGroup> linkedEntityGroupTypeHandle)
        {
            var chunkCacheIterator = new UnsafeChunkCacheIterator(queryImpl->_Filter,
                queryImpl->_QueryData->HasEnableableComponents != 0,
                queryImpl->GetMatchingChunkCache(), queryImpl->_QueryData->MatchingArchetypes.Ptr);
            int chunkIndex = -1;
            v128 chunkEnabledMask = default;
            int maxChunkCount = chunkCacheIterator.Length;
            using var chunksToProcess = new UnsafeList<ChunkAndEnabledMask>(maxChunkCount, Allocator.TempJob);
            while (chunkCacheIterator.MoveNextChunk(ref chunkIndex, out var archetypeChunk, out int chunkEntityCount,
                       out byte useEnabledMask, ref chunkEnabledMask))
            {
                // Structural changes are not allowed while using an UnsafeChunkCacheIterator, so we just track
                // the chunks & metadata to process outside the loop.
                chunksToProcess.AddNoResize(new ChunkAndEnabledMask
                {
                    Chunk = archetypeChunk.m_Chunk,
                    EnabledMask = chunkEnabledMask,
                    ChunkEntityCount = chunkEntityCount,
                    UseEnabledMask = useEnabledMask,
                });
            }
            // Validate that for all LinkedEntityGroup roots that we're about to destroy, all the entities in their
            // LinkedEntityGroup buffers are also destroyed.
            // Architecturally this validation should happen in EntityDataAccess, but it is more efficient to implement
            // once we've already computed the chunksToProcess array.
            AssertWillDestroyAllInLinkedEntityGroup(chunksToProcess, ref linkedEntityGroupTypeHandle,
                queryImpl->_QueryData->HasEnableableComponents != 0);
            // Apply the structural change to all chunks
            var chunkBatches = stackalloc EntityBatchInChunk[TypeManager.MaximumChunkCapacity];
            for(int iChunk=0,chunkCount=chunksToProcess.Length; iChunk<chunkCount; ++iChunk)
            {
                var chunk = chunksToProcess[iChunk].Chunk;
                if (chunksToProcess[iChunk].UseEnabledMask == 0)
                {
                    // All entities in the chunk are enabled; process it en masse
                    DestroyBatch(new EntityBatchInChunk {Chunk = chunk, StartIndex = 0, Count = chunksToProcess[iChunk].ChunkEntityCount});
                }
                else
                {
                    // Populate a list of entity batches in this chunk to process
                    int batchCount = 0;
                    int batchEndIndex = 0;
                    chunkEnabledMask = chunksToProcess[iChunk].EnabledMask;
                    while (EnabledBitUtility.TryGetNextRange(chunkEnabledMask, batchEndIndex, out int batchStartIndex, out batchEndIndex))
                    {
                        chunkBatches[batchCount++] = new EntityBatchInChunk
                        {
                            Chunk = chunk, Count = batchEndIndex - batchStartIndex, StartIndex = batchStartIndex
                        };
                    }
                    Assert.IsTrue(batchCount <= TypeManager.MaximumChunkCapacity);
                    // Iterate batches backwards, to avoid mutating entities we haven't processed yet
                    for (int i = batchCount - 1; i >= 0; i--)
                    {
                        DestroyBatch(chunkBatches[i]);
                    }
                }
            }
        }

        public void DestroyEntities(Entity* entities, int count)
        {
            var entityIndex = 0;

            var additionalDestroyList = new UnsafeList<Entity>(0, Allocator.Persistent);
            int minDestroyStride = int.MaxValue;
            int maxDestroyStride = 0;

            while (entityIndex != count)
            {
                var entityBatchInChunk =
                    GetFirstEntityBatchInChunk(entities + entityIndex, count - entityIndex);
                var chunk = entityBatchInChunk.Chunk;
                var batchCount = entityBatchInChunk.Count;
                var indexInChunk = entityBatchInChunk.StartIndex;

                if (chunk == null)
                {
                    entityIndex += batchCount;
                    continue;
                }

                AddToDestroyList(chunk, indexInChunk, batchCount, count, ref additionalDestroyList,
                    ref minDestroyStride, ref maxDestroyStride);

                DestroyBatch(new EntityBatchInChunk {Chunk = chunk, StartIndex = indexInChunk, Count = batchCount});

                entityIndex += batchCount;
            }

            // Apply additional destroys from any LinkedEntityGroup
            if (!additionalDestroyList.IsEmpty)
            {
                var additionalDestroyPtr = additionalDestroyList.Ptr;
                // Optimal for destruction speed is if entities with same archetype/chunk are followed one after another.
                // So we lay out the to be destroyed objects assuming that the destroyed entities are "similar":
                // Reorder destruction by index in entityGroupArray...

                //@TODO: This is a very specialized fastpath that is likely only going to give benefits in the stress test.
                ///      Figure out how to make this more general purpose.
                if (minDestroyStride == maxDestroyStride)
                {
                    var reordered = (Entity*)Memory.Unmanaged.Allocate(additionalDestroyList.Length * sizeof(Entity), 16,
                        Allocator.TempJob);
                    int batchCount = additionalDestroyList.Length / minDestroyStride;
                    for (int i = 0; i != batchCount; i++)
                    {
                        for (int j = 0; j != minDestroyStride; j++)
                            reordered[j * batchCount + i] = additionalDestroyPtr[i * minDestroyStride + j];
                    }

                    DestroyEntities(reordered, additionalDestroyList.Length);
                    Memory.Unmanaged.Free(reordered, Allocator.TempJob);
                }
                else
                {
                    DestroyEntities(additionalDestroyPtr, additionalDestroyList.Length);
                }
            }

            additionalDestroyList.Dispose();
        }

        public Chunk* GetCleanChunkNoMetaChunk(Archetype* archetype, SharedComponentValues sharedComponentValues)
        {
            var newChunk = AllocateChunk();
            ChunkDataUtility.AddEmptyChunk(archetype, newChunk, sharedComponentValues);

            return newChunk;
        }

        public Chunk* GetCleanChunk(Archetype* archetype, SharedComponentValues sharedComponentValues)
        {
            var newChunk = AllocateChunk();
            ChunkDataUtility.AddEmptyChunk(archetype, newChunk, sharedComponentValues);

            if (archetype->MetaChunkArchetype != null)
                CreateMetaEntityForChunk(newChunk);

            return newChunk;
        }

        public void InstantiateEntities(Entity srcEntity, Entity* outputEntities, int instanceCount)
        {
            if (HasComponent(srcEntity, m_LinkedGroupType))
            {
                var header = (BufferHeader*)GetComponentDataWithTypeRO(srcEntity, m_LinkedGroupType);
                var entityPtr = (Entity*)BufferHeader.GetElementPointer(header);
                var entityCount = header->Length;

                InstantiateEntitiesGroup(entityPtr, entityCount, outputEntities, true, instanceCount, true);
            }
            else
            {
                InstantiateEntitiesOne(srcEntity, outputEntities, instanceCount, null, 0, true);
            }
        }

        public void InstantiateEntities(Entity* srcEntity, Entity* outputEntities, int entityCount, bool removePrefab)
        {
            InstantiateEntitiesGroup(srcEntity, entityCount, outputEntities, false, 1, removePrefab);
        }

        // ----------------------------------------------------------------------------------------------------------
        // INTERNAL
        // ----------------------------------------------------------------------------------------------------------

        internal void DestroyMetaChunkEntity(Entity entity)
        {
            RemoveComponent(entity, m_ChunkHeaderComponentType);
            DestroyEntities(&entity, 1);
        }

        internal void CreateMetaEntityForChunk(Chunk* chunk)
        {
            fixed(EntityComponentStore* entityComponentStore = &this)
            {
                CreateEntities(chunk->Archetype->MetaChunkArchetype, &chunk->metaChunkEntity, 1);

                var chunkHeader = (ChunkHeader*)GetComponentDataWithTypeRW(chunk->metaChunkEntity, m_ChunkHeaderType, GlobalSystemVersion);

                chunkHeader->ArchetypeChunk = new ArchetypeChunk(chunk, entityComponentStore);
            }
        }

        struct InstantiateRemapChunk
        {
            public Chunk* Chunk;
            public int IndexInChunk;
            public int AllocatedCount;
            public int InstanceBeginIndex;
        }

        void AddToDestroyList(Chunk* chunk, int indexInChunk, int batchCount, int inputDestroyCount,
            ref UnsafeList<Entity> entitiesList, ref int minBufferLength, ref int maxBufferLength)
        {
            int indexInArchetype = ChunkDataUtility.GetIndexInTypeArray(chunk->Archetype, m_LinkedGroupType);
            if (indexInArchetype != -1)
            {
                var baseHeader = ChunkDataUtility.GetComponentDataWithTypeRO(chunk, indexInChunk, m_LinkedGroupType);
                var stride = chunk->Archetype->SizeOfs[indexInArchetype];
                for (int i = 0; i != batchCount; i++)
                {
                    var header = (BufferHeader*)(baseHeader + stride * i);

                    var entityGroupCount = header->Length - 1;
                    if (entityGroupCount <= 0)
                        continue;

                    var entityGroupArray = (Entity*)BufferHeader.GetElementPointer(header) + 1;

                    if (entitiesList.Capacity == 0)
                        entitiesList.SetCapacity(inputDestroyCount * entityGroupCount /*, Allocator.TempJob*/);
                    entitiesList.AddRange(entityGroupArray, entityGroupCount /*, Allocator.TempJob*/);

                    minBufferLength = math.min(minBufferLength, entityGroupCount);
                    maxBufferLength = math.max(maxBufferLength, entityGroupCount);
                }
            }
        }

        void DestroyBatch(in EntityBatchInChunk batch)
        {
            var chunk = batch.Chunk;
            var archetype = chunk->Archetype;

            if (!archetype->CleanupNeeded)
            {
                ChunkDataUtility.Deallocate(batch);
            }
            else
            {
                var startIndex = batch.StartIndex;
                var count = batch.Count;

                var cleanupResidueArchetype = archetype->CleanupResidueArchetype;
                if (archetype == cleanupResidueArchetype)
                    return;

                var dstArchetypeChunkFilter = new ArchetypeChunkFilter();
                dstArchetypeChunkFilter.Archetype = cleanupResidueArchetype;

                if (RequiresBuildingResidueSharedComponentIndices(archetype, dstArchetypeChunkFilter.Archetype))
                {
                    BuildResidueSharedComponentIndices(archetype, dstArchetypeChunkFilter.Archetype, chunk->SharedComponentValues, dstArchetypeChunkFilter.SharedComponentValues);
                }
                else
                {
                    chunk->SharedComponentValues.CopyTo(dstArchetypeChunkFilter.SharedComponentValues, 0, archetype->NumSharedComponents);
                }

                if (count == chunk->Count)
                    Move(chunk, ref dstArchetypeChunkFilter);
                else
                    Move(new EntityBatchInChunk {Chunk = chunk, StartIndex = startIndex, Count = count}, ref dstArchetypeChunkFilter);
            }
        }

        bool RequiresBuildingResidueSharedComponentIndices(Archetype* srcArchetype,
            Archetype* dstArchetype)
        {
            return dstArchetype->NumSharedComponents > 0 &&
                dstArchetype->NumSharedComponents != srcArchetype->NumSharedComponents;
        }

        void BuildResidueSharedComponentIndices(Archetype* srcArchetype, Archetype* dstArchetype,
            SharedComponentValues srcSharedComponentValues, int* dstSharedComponentValues)
        {
            int oldFirstShared = srcArchetype->FirstSharedComponent;
            int newFirstShared = dstArchetype->FirstSharedComponent;
            int newCount = dstArchetype->NumSharedComponents;

            for (int oldIndex = 0, newIndex = 0; newIndex < newCount; ++newIndex, ++oldIndex)
            {
                var t = dstArchetype->Types[newIndex + newFirstShared];
                while (t != srcArchetype->Types[oldIndex + oldFirstShared])
                    ++oldIndex;
                dstSharedComponentValues[newIndex] = srcSharedComponentValues[oldIndex];
            }
        }

        int InstantiateEntitiesOne(Entity srcEntity, Entity* outputEntities, int instanceCount, InstantiateRemapChunk* remapChunks, int remapChunksCount, bool removePrefab)
        {
            var src = GetEntityInChunk(srcEntity);
            var srcArchetype = src.Chunk->Archetype;

            var dstArchetype = removePrefab ? srcArchetype->InstantiateArchetype : srcArchetype->CopyArchetype;

            var archetypeChunkFilter = new ArchetypeChunkFilter();
            archetypeChunkFilter.Archetype = dstArchetype;

            if (RequiresBuildingResidueSharedComponentIndices(srcArchetype, dstArchetype))
            {
                BuildResidueSharedComponentIndices(srcArchetype, dstArchetype, src.Chunk->SharedComponentValues, archetypeChunkFilter.SharedComponentValues);
            }
            else
            {
                // Always copy shared component indices since GetChunkWithEmptySlots might reallocate the storage of SharedComponentValues
                src.Chunk->SharedComponentValues.CopyTo(archetypeChunkFilter.SharedComponentValues, 0, dstArchetype->NumSharedComponents);
            }

            int instanceBeginIndex = 0;
            while (instanceBeginIndex != instanceCount)
            {
                var chunk = GetChunkWithEmptySlots(ref archetypeChunkFilter);
                var indexInChunk = chunk->Count;
                var allocateCount = math.min(instanceCount - instanceBeginIndex, chunk->UnusedCount);

                ChunkDataUtility.AllocateClone(chunk, outputEntities + instanceBeginIndex, allocateCount, srcEntity);

                if (remapChunks != null)
                {
                    remapChunks[remapChunksCount].Chunk = chunk;
                    remapChunks[remapChunksCount].IndexInChunk = indexInChunk;
                    remapChunks[remapChunksCount].AllocatedCount = allocateCount;
                    remapChunks[remapChunksCount].InstanceBeginIndex = instanceBeginIndex;
                    remapChunksCount++;
                }

                instanceBeginIndex += allocateCount;
            }

            return remapChunksCount;
        }

        void InstantiateEntitiesGroup(Entity* srcEntities, int srcEntityCount, Entity* outputRootEntities, bool outputRootEntityOnly, int instanceCount, bool removePrefab)
        {
            int totalCount = srcEntityCount * instanceCount;

            var tempAllocSize = sizeof(Entity) * totalCount +
                sizeof(InstantiateRemapChunk) * totalCount + sizeof(Entity) * instanceCount;
            byte* allocation;
            const int kMaxStackAllocSize = 16 * 1024;

            if (tempAllocSize > kMaxStackAllocSize)
            {
                allocation = (byte*)Memory.Unmanaged.Allocate(tempAllocSize, 16, Allocator.Temp);
            }
            else
            {
                var temp = stackalloc byte[tempAllocSize];

                allocation = temp;
            }

            var entityRemap = (Entity*)allocation;
            var remapChunks = (InstantiateRemapChunk*)(entityRemap + totalCount);
            var outputEntities = (Entity*)(remapChunks + totalCount);

            var remapChunksCount = 0;

            for (int i = 0; i != srcEntityCount; i++)
            {
                var srcEntity = srcEntities[i];

                remapChunksCount = InstantiateEntitiesOne(srcEntity, outputEntities, instanceCount, remapChunks, remapChunksCount, removePrefab);

                for (int r = 0; r != instanceCount; r++)
                {
                    var ptr = entityRemap + (r * srcEntityCount + i);
                    *ptr = outputEntities[r];
                }

                if (outputRootEntityOnly)
                {
                    if (i == 0)
                    {
                        for (int r = 0; r != instanceCount; r++)
                            outputRootEntities[r] = outputEntities[r];
                    }
                }
                else
                {
                    for (int r = 0; r != instanceCount; r++)
                        outputRootEntities[r * srcEntityCount + i] = outputEntities[r];
                }
            }


            for (int i = 0; i != remapChunksCount; i++)
            {
                var chunk = remapChunks[i].Chunk;
                var dstArchetype = chunk->Archetype;
                var allocatedCount = remapChunks[i].AllocatedCount;
                var indexInChunk = remapChunks[i].IndexInChunk;
                var instanceBeginIndex = remapChunks[i].InstanceBeginIndex;

                var localRemap = entityRemap + instanceBeginIndex * srcEntityCount;

                EntityRemapUtility.PatchEntitiesForPrefab(dstArchetype->ScalarEntityPatches + 1, dstArchetype->ScalarEntityPatchCount - 1,
                    dstArchetype->BufferEntityPatches, dstArchetype->BufferEntityPatchCount,
                    chunk->Buffer, indexInChunk, allocatedCount, srcEntities, localRemap, srcEntityCount);

                if (dstArchetype->HasManagedEntityRefs)
                {
                    ManagedChangesTracker.PatchEntitiesForPrefab(dstArchetype, chunk, indexInChunk, allocatedCount, srcEntities, localRemap, srcEntityCount, Allocator.Temp);
                }
            }

            if (tempAllocSize > kMaxStackAllocSize)
                Memory.Unmanaged.Free(allocation, Allocator.Temp);
        }

        EntityBatchInChunk GetFirstEntityBatchInChunk(Entity* entities, int count)
        {
            // This is optimized for the case where the array of entities are allocated contigously in the chunk
            // Thus the compacting of other elements can be batched

            // Calculate baseEntityIndex & chunk
            var baseEntityIndex = entities[0].Index;

            var versions = m_VersionByEntity;
            var chunkData = m_EntityInChunkByEntity;

            var chunk = versions[baseEntityIndex] == entities[0].Version
                ? m_EntityInChunkByEntity[baseEntityIndex].Chunk
                : null;
            var indexInChunk = chunkData[baseEntityIndex].IndexInChunk;
            var batchCount = 0;

            while (batchCount < count)
            {
                var entityIndex = entities[batchCount].Index;
                var curChunk = chunkData[entityIndex].Chunk;
                var curIndexInChunk = chunkData[entityIndex].IndexInChunk;

                if (versions[entityIndex] == entities[batchCount].Version)
                {
                    if (curChunk != chunk || curIndexInChunk != indexInChunk + batchCount)
                        break;
                }
                else
                {
                    if (chunk != null)
                        break;
                }

                batchCount++;
            }

            return new EntityBatchInChunk
            {
                Chunk = chunk,
                Count = batchCount,
                StartIndex = indexInChunk
            };
        }

        public static JobHandle GetCreatedAndDestroyedEntities(EntityComponentStore* store, NativeList<int> state, NativeList<Entity> createdEntities, NativeList<Entity> destroyedEntities, bool async)
        {
            // Early outwhen no entities were created or destroyed compared to the last time this method was called
            if (state.Length != 0 && store->m_EntityCreateDestroyVersion == state[0])
            {
                createdEntities.Clear();
                destroyedEntities.Clear();
                return default;
            }

            var jobData = new GetOrCreateDestroyedEntitiesJob
            {
                State = state,
                CreatedEntities = createdEntities,
                DestroyedEntities = destroyedEntities,
                Store = store
            };

            if (async)
                return jobData.Schedule();
            else
            {
                jobData.Run();
                return default;
            }
        }

        [BurstCompile]
        internal struct GetOrCreateDestroyedEntitiesJob : IJob
        {
            public NativeList<int>    State;
            public NativeList<Entity> CreatedEntities;
            public NativeList<Entity> DestroyedEntities;

            [NativeDisableUnsafePtrRestriction]
            public EntityComponentStore* Store;

            public void Execute()
            {
                var capacity = Store->m_EntitiesCapacity;
                var versionByEntity = Store->m_VersionByEntity;
                var entityInChunkByEntity = Store->m_EntityInChunkByEntity;

                CreatedEntities.Clear();
                DestroyedEntities.Clear();
                State.Resize(capacity + 1, NativeArrayOptions.ClearMemory);

                State[0] = Store->m_EntityCreateDestroyVersion;
                var state = State.AsArray().GetSubArray(1, capacity);

                for (int i = 0; i != capacity; i++)
                {
                    if (state[i] == versionByEntity[i])
                        continue;

                    // Was a valid entity but version was incremented, thus destroyed
                    if (state[i] != 0)
                    {
                        DestroyedEntities.Add(new Entity { Index = i, Version = state[i] });
                        state[i] = 0;
                    }

                    // It is now a valid entity, but version has changed
                    if (entityInChunkByEntity[i].Chunk != null &&
                        !entityInChunkByEntity[i].Chunk->Archetype->HasChunkHeader)
                    {
                        CreatedEntities.Add(new Entity { Index = i, Version = versionByEntity[i] });
                        state[i] = versionByEntity[i];
                    }
                }
            }
        }
    }
}
