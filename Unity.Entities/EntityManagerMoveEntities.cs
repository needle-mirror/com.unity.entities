using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Profiling;

namespace Unity.Entities
{
    public sealed unsafe partial class EntityManager
    {
        static readonly ProfilerMarker k_ProfileMoveSharedComponents = new ProfilerMarker("MoveSharedComponents");
        static readonly ProfilerMarker k_ProfileMoveObjectComponents = new ProfilerMarker("MoveObjectComponents");

        // ----------------------------------------------------------------------------------------------------------
        // PUBLIC
        // ----------------------------------------------------------------------------------------------------------


        /// <summary>
        /// Moves all entities managed by the specified EntityManager to the world of this EntityManager.
        /// </summary>
        /// <remarks>
        /// The entities moved are owned by this EntityManager.
        ///
        /// Each <see cref="World"/> has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one World to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        public void MoveEntitiesFrom(EntityManager srcEntities)
        {
            using (var entityRemapping = srcEntities.CreateEntityRemapArray(Allocator.TempJob))
                MoveEntitiesFromInternalAll(srcEntities, entityRemapping);
        }

        
        /// <summary>
        /// Moves all entities managed by the specified EntityManager to the <see cref="World"/> of this EntityManager and fills
        /// an array with their Entity objects.
        /// </summary>
        /// <remarks>
        /// After the move, the entities are managed by this EntityManager. Use the `output` array to make post-move
        /// changes to the transferred entities.
        ///
        /// Each world has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one World to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="output">An array to receive the Entity objects of the transferred entities.</param>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        public void MoveEntitiesFrom(out NativeArray<Entity> output, EntityManager srcEntities)
        {
            using (var entityRemapping = srcEntities.CreateEntityRemapArray(Allocator.TempJob))
            {
                MoveEntitiesFromInternalAll(srcEntities, entityRemapping);
                EntityRemapUtility.GetTargets(out output, entityRemapping);
            }
        }
        
        /// <summary>
        /// Moves all entities managed by the specified EntityManager to the <see cref="World"/> of this EntityManager and fills
        /// an array with their <see cref="Entity"/> objects.
        /// </summary>
        /// <remarks>
        /// After the move, the entities are managed by this EntityManager. Use the `output` array to make post-move
        /// changes to the transferred entities.
        ///
        /// Each world has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one World to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="output">An array to receive the Entity objects of the transferred entities.</param>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        /// <param name="entityRemapping">A set of entity transformations to make during the transfer.</param>
        /// <exception cref="ArgumentException"></exception>
        public void MoveEntitiesFrom(out NativeArray<Entity> output, EntityManager srcEntities, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
            MoveEntitiesFromInternalAll(srcEntities, entityRemapping);
            EntityRemapUtility.GetTargets(out output, entityRemapping);
        }
        
        /// <summary>
        /// Moves all entities managed by the specified EntityManager to the <see cref="World"/> of this EntityManager.
        /// </summary>
        /// <remarks>
        /// After the move, the entities are managed by this EntityManager.
        ///
        /// Each World has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one world to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        /// <param name="entityRemapping">A set of entity transformations to make during the transfer.</param>
        /// <exception cref="ArgumentException">Thrown if you attempt to transfer entities to the EntityManager
        /// that already owns them.</exception>
        public void MoveEntitiesFrom(EntityManager srcEntities, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
            MoveEntitiesFromInternalAll(srcEntities, entityRemapping);
        }

        
        /// <summary>
        /// Moves a selection of the entities managed by the specified EntityManager to the <see cref="World"/> of this EntityManager
        /// and fills an array with their <see cref="Entity"/> objects.
        /// </summary>
        /// <remarks>
        /// After the move, the entities are managed by this EntityManager. Use the `output` array to make post-move
        /// changes to the transferred entities.
        ///
        /// Each world has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one World to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        /// <param name="filter">A EntityQuery that defines the entities to move. Must be part of the source
        /// World.</param>
        /// <exception cref="ArgumentException"></exception>
        public void MoveEntitiesFrom(EntityManager srcEntities, EntityQuery filter)
        {
            using(var entityRemapping = srcEntities.CreateEntityRemapArray(Allocator.TempJob))
                MoveEntitiesFromInternalQuery(srcEntities, filter, entityRemapping);
        }
        
        /// <summary>
        /// Moves a selection of the entities managed by the specified EntityManager to the <see cref="World"/> of this EntityManager
        /// and fills an array with their <see cref="Entity"/> objects.
        /// </summary>
        /// <remarks>
        /// After the move, the entities are managed by this EntityManager. Use the `output` array to make post-move
        /// changes to the transferred entities.
        ///
        /// Each world has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one World to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="output">An array to receive the Entity objects of the transferred entities.</param>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        /// <param name="filter">A EntityQuery that defines the entities to move. Must be part of the source
        /// World.</param>
        /// <param name="entityRemapping">A set of entity transformations to make during the transfer.</param>
        /// <exception cref="ArgumentException"></exception>
        public void MoveEntitiesFrom(out NativeArray<Entity> output, EntityManager srcEntities, EntityQuery filter, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
            MoveEntitiesFromInternalQuery(srcEntities, filter, entityRemapping);
            EntityRemapUtility.GetTargets(out output, entityRemapping);
        }

        /// <summary>
        /// Moves a selection of the entities managed by the specified EntityManager to the <see cref="World"/> of this EntityManager.
        /// </summary>
        /// <remarks>
        /// After the move, the entities are managed by this EntityManager.
        ///
        /// Each world has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one World to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        /// <param name="filter">A EntityQuery that defines the entities to move. Must be part of the source
        /// World.</param>
        /// <param name="entityRemapping">A set of entity transformations to make during the transfer.</param>
        /// <exception cref="ArgumentException">Thrown if the EntityQuery object used as the `filter` comes
        /// from a different world than the `srcEntities` EntityManager.</exception>
        public void MoveEntitiesFrom(EntityManager srcEntities, EntityQuery filter, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
            MoveEntitiesFromInternalQuery(srcEntities, filter, entityRemapping);
        }

        /// <summary>
        /// Moves a selection of the entities managed by the specified EntityManager to the <see cref="World"/> of this EntityManager
        /// and fills an array with their <see cref="Entity"/> objects.
        /// </summary>
        /// <remarks>
        /// After the move, the entities are managed by this EntityManager. Use the `output` array to make post-move
        /// changes to the transferred entities.
        ///
        /// Each world has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one World to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="output">An array to receive the Entity objects of the transferred entities.</param>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        /// <param name="filter">A EntityQuery that defines the entities to move. Must be part of the source
        /// World.</param>
        /// <exception cref="ArgumentException"></exception>
        public void MoveEntitiesFrom(out NativeArray<Entity> output, EntityManager srcEntities, EntityQuery filter)
        {
            using (var entityRemapping = srcEntities.CreateEntityRemapArray(Allocator.TempJob))
            {
                MoveEntitiesFromInternalQuery(srcEntities, filter, entityRemapping);
                EntityRemapUtility.GetTargets(out output, entityRemapping);
            }
        }
        
        /// <summary>
        /// Creates a remapping array with one element for each entity in the <see cref="World"/>.
        /// </summary>
        /// <param name="allocator">The type of memory allocation to use when creating the array.</param>
        /// <returns>An array containing a no-op identity transformation for each entity.</returns>
        public NativeArray<EntityRemapUtility.EntityRemapInfo> CreateEntityRemapArray(Allocator allocator)
        {
            return new NativeArray<EntityRemapUtility.EntityRemapInfo>(m_EntityComponentStore->EntitiesCapacity, allocator);
        }

        // ----------------------------------------------------------------------------------------------------------
        // INTERNAL
        // ----------------------------------------------------------------------------------------------------------

        void MoveEntitiesFromInternalQuery(EntityManager srcEntities, EntityQuery filter, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (filter._EntityComponentStore != srcEntities.EntityComponentStore)
                throw new ArgumentException(
                    "EntityManager.MoveEntitiesFrom failed - srcEntities and filter must belong to the same World)");

            if (srcEntities == this)
                throw new ArgumentException("srcEntities must not be the same as this EntityManager.");
#endif
            BeforeStructuralChange();
            srcEntities.BeforeStructuralChange();

            using (var chunks = filter.CreateArchetypeChunkArray(Allocator.TempJob))
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                for (int i = 0; i < chunks.Length; ++i)
                    if (chunks[i].m_Chunk->Archetype->HasChunkHeader)
                        throw new ArgumentException("MoveEntitiesFrom can not move chunks that contain ChunkHeader components.");
#endif
                
                var archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

                MoveChunksFromFiltered(chunks, entityRemapping, srcEntities.EntityComponentStore, srcEntities.ManagedComponentStore);

                var changedArchetypes = EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges);
                EntityQueryManager.AddAdditionalArchetypes(changedArchetypes);
            }
        }
       
        public void MoveEntitiesFromInternalAll(EntityManager srcEntities, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (srcEntities == this)
                throw new ArgumentException("srcEntities must not be the same as this EntityManager.");

            if (entityRemapping.Length < srcEntities.m_EntityComponentStore->EntitiesCapacity)
                throw new ArgumentException("entityRemapping.Length isn't large enough, use srcEntities.CreateEntityRemapArray");
            
            if (!srcEntities.m_ManagedComponentStore.AllSharedComponentReferencesAreFromChunks(srcEntities
                .EntityComponentStore))
                throw new ArgumentException(
                    "EntityManager.MoveEntitiesFrom failed - All ISharedComponentData references must be from EntityManager. (For example EntityQuery.SetFilter with a shared component type is not allowed during EntityManager.MoveEntitiesFrom)");
#endif

            BeforeStructuralChange();
            srcEntities.BeforeStructuralChange();
            var archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

            MoveChunksFromAll(entityRemapping, srcEntities.EntityComponentStore, srcEntities.ManagedComponentStore);
            
            var changedArchetypes = EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges);
            EntityQueryManager.AddAdditionalArchetypes(changedArchetypes);
        }
        
        

        internal void MoveChunksFromFiltered(
            NativeArray<ArchetypeChunk> chunks,
            NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping,
            EntityComponentStore* srcEntityComponentStore,
            ManagedComponentStore srcManagedComponentStore)
        {
            new MoveChunksJob
            {
                srcEntityComponentStore = srcEntityComponentStore,
                dstEntityComponentStore = EntityComponentStore,
                entityRemapping = entityRemapping,
                chunks = chunks
            }.Run();

            var managedArrayChunks = new NativeList<IntPtr>(Allocator.Temp);

            int chunkCount = chunks.Length;
            var remapChunks = new NativeArray<RemapChunk>(chunkCount, Allocator.TempJob);

            Archetype* previousSrcArchetypee = null;
            Archetype* dstArchetype = null;
            
            for (int i = 0; i < chunkCount; ++i)
            {
                var chunk = chunks[i].m_Chunk;
                var archetype = chunk->Archetype;

                if (previousSrcArchetypee != archetype)
                {
                    dstArchetype = EntityComponentStore->GetOrCreateArchetype(archetype->Types, archetype->TypesCount);
                    EntityComponentStore->IncrementComponentTypeOrderVersion(dstArchetype);
                }

                remapChunks[i] = new RemapChunk {chunk = chunk, dstArchetype = dstArchetype};

                if (archetype->NumManagedArrays > 0)
                {
                    managedArrayChunks.Add((IntPtr)chunk);
                }

                if (archetype->MetaChunkArchetype != null)
                {
                    Entity srcEntity = chunk->metaChunkEntity;
                    Entity dstEntity;

                    EntityComponentStore->CreateEntities(dstArchetype->MetaChunkArchetype, &dstEntity, 1);

                    var srcEntityInChunk = srcEntityComponentStore->GetEntityInChunk(srcEntity);
                    var dstEntityInChunk = EntityComponentStore->GetEntityInChunk(dstEntity);

                    ChunkDataUtility.SwapComponents(srcEntityInChunk.Chunk, srcEntityInChunk.IndexInChunk, dstEntityInChunk.Chunk, dstEntityInChunk.IndexInChunk, 1,
                        srcEntityComponentStore->GlobalSystemVersion, EntityComponentStore->GlobalSystemVersion);
                    EntityRemapUtility.AddEntityRemapping(ref entityRemapping, srcEntity, dstEntity);

                    srcEntityComponentStore->DestroyEntities(&srcEntity, 1);
                }
            }

            var managedArrayDstIndices = new NativeArray<int>(managedArrayChunks.Length, Allocator.TempJob);
            var managedArraySrcIndices = new NativeArray<int>(managedArrayChunks.Length, Allocator.TempJob);
            EntityComponentStore->ReserveManagedObjectArrays(managedArrayDstIndices);

            var remapManaged = new RemapManagedArraysJob
            {
                chunks = managedArrayChunks,
                dstIndices = managedArrayDstIndices,
                srcIndices = managedArraySrcIndices,
            }.Schedule(managedArrayChunks.Length, 64);

            ManagedComponentStore.Playback(ref EntityComponentStore->ManagedChangesTracker);
            srcManagedComponentStore.Playback(ref srcEntityComponentStore->ManagedChangesTracker);

            k_ProfileMoveObjectComponents.Begin();
            remapManaged.Complete();
            m_ManagedComponentStore.MoveManagedObjectArrays(managedArraySrcIndices, managedArrayDstIndices, srcManagedComponentStore);
            k_ProfileMoveObjectComponents.End();

            managedArrayDstIndices.Dispose();
            managedArraySrcIndices.Dispose();
            managedArrayChunks.Dispose();

            k_ProfileMoveSharedComponents.Begin();
            var remapShared =
                ManagedComponentStore.MoveSharedComponents(srcManagedComponentStore, chunks, Allocator.TempJob);
            k_ProfileMoveSharedComponents.End();

            new ChunkPatchEntities
            {
                RemapChunks = remapChunks,
                EntityRemapping = entityRemapping,
                EntityComponentStore = EntityComponentStore
            }.Run();

            var remapChunksJob = new RemapChunksFilteredJob
            {
                dstEntityComponentStore = EntityComponentStore,
                remapChunks = remapChunks,
                entityRemapping = entityRemapping
            }.Schedule(remapChunks.Length, 1);

            var moveChunksBetweenArchetypeJob = new MoveFilteredChunksBetweenArchetypexJob
            {
                remapChunks = remapChunks,
                remapShared = remapShared,
                globalSystemVersion = EntityComponentStore->GlobalSystemVersion
            }.Schedule(remapChunksJob);

            moveChunksBetweenArchetypeJob.Complete();
            
            ManagedComponentStore.Playback(ref EntityComponentStore->ManagedChangesTracker);
            
            remapShared.Dispose();
            remapChunks.Dispose();
        }

        internal void MoveChunksFromAll(
            NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping,
            EntityComponentStore* srcEntityComponentStore,
            ManagedComponentStore srcManagedComponentStore)
        {
            var moveChunksJob = new MoveAllChunksJob
            {
                srcEntityComponentStore = srcEntityComponentStore,
                dstEntityComponentStore = EntityComponentStore,
                entityRemapping = entityRemapping
            }.Schedule();

            JobHandle.ScheduleBatchedJobs();

            int chunkCount = 0;
            for (var i = 0; i < srcEntityComponentStore->m_Archetypes.Length; ++i)
            {
                var srcArchetype = srcEntityComponentStore->m_Archetypes.Ptr[i];
                chunkCount += srcArchetype->Chunks.Count;
            }

            var remapChunks = new NativeArray<RemapChunk>(chunkCount, Allocator.TempJob);
            var remapArchetypes = new NativeArray<RemapArchetype>(srcEntityComponentStore->m_Archetypes.Length, Allocator.TempJob);
            var managedArrayChunks = new NativeList<IntPtr>(Allocator.Temp);

            int chunkIndex = 0;
            int archetypeIndex = 0;
            for (var i = 0; i < srcEntityComponentStore->m_Archetypes.Length; ++i)
            {
                var srcArchetype = srcEntityComponentStore->m_Archetypes.Ptr[i];
                if (srcArchetype->Chunks.Count != 0)
                {
                    var dstArchetype = EntityComponentStore->GetOrCreateArchetype(srcArchetype->Types,
                        srcArchetype->TypesCount);

                    remapArchetypes[archetypeIndex] = new RemapArchetype
                    {srcArchetype = srcArchetype, dstArchetype = dstArchetype};

                    for (var j = 0; j < srcArchetype->Chunks.Count; ++j)
                    {
                        var srcChunk = srcArchetype->Chunks.p[j];
                        remapChunks[chunkIndex] = new RemapChunk {chunk = srcChunk, dstArchetype = dstArchetype};
                        chunkIndex++;
                    }

                    if (srcArchetype->NumManagedArrays > 0)
                    {
                        for (var j = 0; j < srcArchetype->Chunks.Count; ++j)
                        {
                            var chunk = srcArchetype->Chunks.p[j];
                            managedArrayChunks.Add((IntPtr)chunk);
                        }
                    }

                    archetypeIndex++;

                    EntityComponentStore->IncrementComponentTypeOrderVersion(dstArchetype);
                }
            }

            moveChunksJob.Complete();

            var managedArrayDstIndices = new NativeArray<int>(managedArrayChunks.Length, Allocator.TempJob);
            var managedArraySrcIndices = new NativeArray<int>(managedArrayChunks.Length, Allocator.TempJob);
            EntityComponentStore->ReserveManagedObjectArrays(managedArrayDstIndices);

            var remapManaged = new RemapManagedArraysJob
            {
                chunks = managedArrayChunks,
                dstIndices = managedArrayDstIndices,
                srcIndices = managedArraySrcIndices,
            }.Schedule(managedArrayChunks.Length, 64);

            ManagedComponentStore.Playback(ref EntityComponentStore->ManagedChangesTracker);
            srcManagedComponentStore.Playback(ref srcEntityComponentStore->ManagedChangesTracker);

            k_ProfileMoveObjectComponents.Begin();
            remapManaged.Complete();
            m_ManagedComponentStore.MoveManagedObjectArrays(managedArraySrcIndices, managedArrayDstIndices, srcManagedComponentStore);
            k_ProfileMoveObjectComponents.End();

            managedArrayDstIndices.Dispose();
            managedArraySrcIndices.Dispose();
            managedArrayChunks.Dispose();

            k_ProfileMoveSharedComponents.Begin();
            var remapShared =
                ManagedComponentStore.MoveAllSharedComponents(srcManagedComponentStore, Allocator.TempJob);
            k_ProfileMoveSharedComponents.End();

            new ChunkPatchEntities
            {
                RemapChunks = remapChunks,
                EntityRemapping = entityRemapping,
                EntityComponentStore = EntityComponentStore
            }.Run();
            
            var remapAllChunksJob = new RemapAllChunksJob
            {
                dstEntityComponentStore = EntityComponentStore,
                remapChunks = remapChunks,
                entityRemapping = entityRemapping
            }.Schedule(remapChunks.Length, 1);

            var remapArchetypesJob = new RemapAllArchetypesJob
            {
                remapArchetypes = remapArchetypes,
                remapShared = remapShared,
                dstEntityComponentStore = EntityComponentStore,
                chunkHeaderType = TypeManager.GetTypeIndex<ChunkHeader>()
            }.Schedule(archetypeIndex, 1, remapAllChunksJob);

            ManagedComponentStore.Playback(ref EntityComponentStore->ManagedChangesTracker);
            
            remapArchetypesJob.Complete();
            remapShared.Dispose();
            remapChunks.Dispose();
        }

        struct RemapChunk
        {
            public Chunk* chunk;
            public Archetype* dstArchetype;
        }

        struct RemapArchetype
        {
            public Archetype* srcArchetype;
            public Archetype* dstArchetype;
        }

        [BurstCompile]
        struct ChunkPatchEntities : IJob
        {
            public NativeArray<RemapChunk> RemapChunks;
            public NativeArray<EntityRemapUtility.EntityRemapInfo> EntityRemapping;
            [NativeDisableUnsafePtrRestriction]
            public EntityComponentStore* EntityComponentStore;

            public void Execute()
            {
                for (int i = 0; i < RemapChunks.Length; i++)
                {
                    var remapChunk = RemapChunks[i];
                    Chunk* chunk = remapChunk.chunk;
                    Archetype* dstArchetype = remapChunk.dstArchetype;
                    EntityComponentStore->ManagedChangesTracker.PatchEntities(dstArchetype, chunk, chunk->Count, EntityRemapping);
                }
            }
        }

        [BurstCompile]
        struct MoveChunksJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* srcEntityComponentStore;
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* dstEntityComponentStore;
            public NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping;
            [ReadOnly] public NativeArray<ArchetypeChunk> chunks;

            public void Execute()
            {
                int chunkCount = chunks.Length;
                for (int i = 0; i < chunkCount; ++i)
                {
                    var chunk = chunks[i].m_Chunk;
                    dstEntityComponentStore->AllocateEntitiesForRemapping(chunk, ref entityRemapping);
                    srcEntityComponentStore->FreeEntities(chunk);
                }
            }
        }

        [BurstCompile]
        struct RemapManagedArraysJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<IntPtr> chunks;
            [ReadOnly] public NativeArray<int> dstIndices;
            public NativeArray<int> srcIndices;

            public void Execute(int index)
            {
                var chunk = (Chunk*)chunks[index];
                srcIndices[index] = chunk->ManagedArrayIndex;
                chunk->ManagedArrayIndex = dstIndices[index];
            }
        }

        [BurstCompile]
        struct RemapChunksFilteredJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping;
            [ReadOnly] public NativeArray<RemapChunk> remapChunks;

            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* dstEntityComponentStore;

            public void Execute(int index)
            {
                Chunk* chunk = remapChunks[index].chunk;
                Archetype* dstArchetype = remapChunks[index].dstArchetype;

                dstEntityComponentStore->RemapChunk(dstArchetype, chunk, 0, chunk->Count, ref entityRemapping);
                EntityRemapUtility.PatchEntities(dstArchetype->ScalarEntityPatches + 1,
                    dstArchetype->ScalarEntityPatchCount - 1, dstArchetype->BufferEntityPatches,
                    dstArchetype->BufferEntityPatchCount, chunk->Buffer, chunk->Count, ref entityRemapping);
            }
        }

        [BurstCompile]
        struct MoveFilteredChunksBetweenArchetypexJob : IJob
        {
            [ReadOnly] public NativeArray<RemapChunk> remapChunks;
            [ReadOnly] public NativeArray<int> remapShared;
            public uint globalSystemVersion;

            public void Execute()
            {
                int chunkCount = remapChunks.Length;
                for (int iChunk = 0; iChunk < chunkCount; ++iChunk)
                {
                    var chunk = remapChunks[iChunk].chunk;
                    var dstArchetype = remapChunks[iChunk].dstArchetype;
                    var srcArchetype = chunk->Archetype;

                    int numSharedComponents = dstArchetype->NumSharedComponents;

                    var sharedComponentValues = chunk->SharedComponentValues;

                    if (numSharedComponents != 0)
                    {
                        var alloc = stackalloc int[numSharedComponents];

                        for (int i = 0; i < numSharedComponents; ++i)
                            alloc[i] = remapShared[sharedComponentValues[i]];
                        sharedComponentValues = alloc;
                    }

                    if (chunk->Count < chunk->Capacity)
                        srcArchetype->EmptySlotTrackingRemoveChunk(chunk);
                    srcArchetype->RemoveFromChunkList(chunk);
                    srcArchetype->EntityCount -= chunk->Count;

                    chunk->Archetype = dstArchetype;

                    dstArchetype->EntityCount += chunk->Count;
                    dstArchetype->AddToChunkList(chunk, sharedComponentValues, globalSystemVersion);
                    if (chunk->Count < chunk->Capacity)
                        dstArchetype->EmptySlotTrackingAddChunk(chunk);
                }
            }
        }

        [BurstCompile]
        struct RemapAllChunksJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping;
            [ReadOnly] public NativeArray<RemapChunk> remapChunks;

            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* dstEntityComponentStore;

            public void Execute(int index)
            {
                Chunk* chunk = remapChunks[index].chunk;
                Archetype* dstArchetype = remapChunks[index].dstArchetype;

                dstEntityComponentStore->RemapChunk(dstArchetype, chunk, 0, chunk->Count, ref entityRemapping);
                EntityRemapUtility.PatchEntities(dstArchetype->ScalarEntityPatches + 1,
                    dstArchetype->ScalarEntityPatchCount - 1, dstArchetype->BufferEntityPatches,
                    dstArchetype->BufferEntityPatchCount, chunk->Buffer, chunk->Count, ref entityRemapping);

                chunk->Archetype = dstArchetype;
                chunk->ListIndex += dstArchetype->Chunks.Count;
                chunk->ListWithEmptySlotsIndex += dstArchetype->ChunksWithEmptySlots.Length;
            }
        }

        [BurstCompile]
        struct RemapAllArchetypesJob : IJobParallelFor
        {
            [DeallocateOnJobCompletion][ReadOnly] public NativeArray<RemapArchetype> remapArchetypes;

            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* dstEntityComponentStore;

            [ReadOnly] public NativeArray<int> remapShared;

            public int chunkHeaderType;

            // This must be run after chunks have been remapped since FreeChunksBySharedComponents needs the shared component
            // indices in the chunks to be remapped
            public void Execute(int index)
            {
                var srcArchetype = remapArchetypes[index].srcArchetype;
                int srcChunkCount = srcArchetype->Chunks.Count;

                var dstArchetype = remapArchetypes[index].dstArchetype;
                int dstChunkCount = dstArchetype->Chunks.Count;

                if (dstArchetype->Chunks.Capacity < srcChunkCount + dstChunkCount)
                    dstArchetype->Chunks.Grow(srcChunkCount + dstChunkCount);

                UnsafeUtility.MemCpy(dstArchetype->Chunks.p + dstChunkCount, srcArchetype->Chunks.p,
                    sizeof(Chunk*) * srcChunkCount);

                if (srcArchetype->NumSharedComponents == 0)
                {
                    if (srcArchetype->ChunksWithEmptySlots.Length != 0)
                    {
                        dstArchetype->ChunksWithEmptySlots.SetCapacity(
                            srcArchetype->ChunksWithEmptySlots.Length + dstArchetype->ChunksWithEmptySlots.Length);
                        dstArchetype->ChunksWithEmptySlots.AddRange(srcArchetype->ChunksWithEmptySlots);
                        srcArchetype->ChunksWithEmptySlots.Resize(0);
                    }
                }
                else
                {
                    for (int i = 0; i < dstArchetype->NumSharedComponents; ++i)
                    {
                        var srcArray = srcArchetype->Chunks.GetSharedComponentValueArrayForType(i);
                        var dstArray = dstArchetype->Chunks.GetSharedComponentValueArrayForType(i) + dstChunkCount;
                        for (int j = 0; j < srcChunkCount; ++j)
                        {
                            int srcIndex = srcArray[j];
                            int remapped = remapShared[srcIndex];
                            dstArray[j] = remapped;
                        }
                    }

                    for (int i = 0; i < srcChunkCount; ++i)
                    {
                        var chunk = dstArchetype->Chunks.p[i + dstChunkCount];
                        if (chunk->Count < chunk->Capacity)
                            dstArchetype->FreeChunksBySharedComponents.Add(dstArchetype->Chunks.p[i + dstChunkCount]);
                    }

                    srcArchetype->FreeChunksBySharedComponents.Init(16);
                }

                var globalSystemVersion = dstEntityComponentStore->GlobalSystemVersion;
                // Set change versions to GlobalSystemVersion
                for (int iType = 0; iType < dstArchetype->TypesCount; ++iType)
                {
                    var dstArray = dstArchetype->Chunks.GetChangeVersionArrayForType(iType) + dstChunkCount;
                    for (int i = 0; i < srcChunkCount; ++i)
                    {
                        dstArray[i] = globalSystemVersion;
                    }
                }

                // Copy chunk count array
                var dstCountArray = dstArchetype->Chunks.GetChunkEntityCountArray() + dstChunkCount;
                UnsafeUtility.MemCpy(dstCountArray, srcArchetype->Chunks.GetChunkEntityCountArray(),
                    sizeof(int) * srcChunkCount);

                // Fix up chunk pointers in ChunkHeaders
                if (dstArchetype->HasChunkComponents)
                {
                    var metaArchetype = dstArchetype->MetaChunkArchetype;
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(metaArchetype, chunkHeaderType);
                    var offset = metaArchetype->Offsets[indexInTypeArray];
                    var sizeOf = metaArchetype->SizeOfs[indexInTypeArray];

                    for (int i = 0; i < srcChunkCount; ++i)
                    {
                        // Set chunk header without bumping change versions since they are zeroed when processing meta chunk
                        // modifying them here would be a race condition
                        var chunk = dstArchetype->Chunks.p[i + dstChunkCount];
                        var metaChunkEntity = chunk->metaChunkEntity;
                        var metaEntityInChunk = dstEntityComponentStore->GetEntityInChunk(metaChunkEntity);
                        var chunkHeader = (ChunkHeader*)(metaEntityInChunk.Chunk->Buffer + (offset + sizeOf * metaEntityInChunk.IndexInChunk));
                        chunkHeader->ArchetypeChunk = new ArchetypeChunk(chunk, dstEntityComponentStore);
                    }
                }

                dstArchetype->EntityCount += srcArchetype->EntityCount;
                dstArchetype->Chunks.Count += srcChunkCount;
                srcArchetype->Chunks.Dispose();
                srcArchetype->EntityCount = 0;
            }
        }

        [BurstCompile]
        struct MoveAllChunksJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* srcEntityComponentStore;
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* dstEntityComponentStore;
            public NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping;

            public void Execute()
            {
                dstEntityComponentStore->AllocateEntitiesForRemapping(srcEntityComponentStore, ref entityRemapping);
                srcEntityComponentStore->FreeAllEntities();
            }
        }
    }
}
