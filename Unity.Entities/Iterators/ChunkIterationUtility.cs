using System;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace Unity.Entities
{
    /// <summary>
    ///     Enables iteration over chunks belonging to a set of archetypes.
    /// </summary>
    [BurstCompile]
    [BurstCompatible(RequiredUnityDefine = "UNITY_2020_2_OR_NEWER && !NET_DOTS")]
    [GenerateBurstMonoInterop("ChunkIterationUtility")]
    internal unsafe partial struct ChunkIterationUtility
    {
        internal const int kMaxBatchesPerChunk = 1024;

        /// <summary>
        /// Creates a NativeArray with all the chunks in a given archetype filtered by the provided EntityQueryFilter.
        /// This function will not sync the needed types in the EntityQueryFilter so they have to be synced manually before calling this function.
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes.</param>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="jobHandle">Handle to the GatherChunks job used to fill the output array.</param>
        /// <param name="filter">Filter used to filter the resulting chunks</param>
        /// <param name="dependsOn">All jobs spawned will depend on this JobHandle</param>
        /// <returns>NativeArray of all the chunks in the matchingArchetypes list.</returns>
        public static NativeArray<ArchetypeChunk> CreateArchetypeChunkArrayAsync(UnsafeMatchingArchetypePtrList matchingArchetypes,
            Allocator allocator, out JobHandle jobHandle, ref EntityQueryFilter filter,
            JobHandle dependsOn = default(JobHandle))
        {
            var archetypeCount = matchingArchetypes.Length;

            var offsets =
                new NativeArray<int>(archetypeCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var chunkCount = 0;
            {
                var ptrs = matchingArchetypes.Ptr;
                for (int i = 0; i < archetypeCount; ++i)
                {
                    var archetype = ptrs[i]->Archetype;
                    offsets[i] = chunkCount;
                    chunkCount += archetype->Chunks.Count;
                }
            }

            if (!filter.RequiresMatchesFilter)
            {
                var chunks = new NativeArray<ArchetypeChunk>(chunkCount, allocator, NativeArrayOptions.UninitializedMemory);
                var gatherChunksJob = new GatherChunksJob
                {
                    MatchingArchetypes = matchingArchetypes.Ptr,
                    entityComponentStore = matchingArchetypes.entityComponentStore,
                    Offsets = offsets,
                    Chunks = chunks
                };
                jobHandle = gatherChunksJob.Schedule(archetypeCount, 1, dependsOn);

                return chunks;
            }
            else
            {
                var filteredCounts =  new NativeArray<int>(archetypeCount + 1, Allocator.TempJob);
                var sparseChunks = new NativeArray<ArchetypeChunk>(chunkCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var gatherChunksJob = new GatherChunksWithFilteringJob
                {
                    MatchingArchetypes = matchingArchetypes.Ptr,
                    Filter = filter,
                    Offsets = offsets,
                    FilteredCounts = filteredCounts,
                    SparseChunks = sparseChunks,
                    entityComponentStore = matchingArchetypes.entityComponentStore
                };
                gatherChunksJob.Schedule(archetypeCount, 1, dependsOn).Complete();

                // accumulated filtered counts: filteredCounts[i] becomes the destination offset
                int totalChunks = 0;
                for (int i = 0; i < archetypeCount; ++i)
                {
                    int currentCount = filteredCounts[i];
                    filteredCounts[i] = totalChunks;
                    totalChunks += currentCount;
                }
                filteredCounts[archetypeCount] = totalChunks;

                var joinedChunks = new NativeArray<ArchetypeChunk>(totalChunks, allocator, NativeArrayOptions.UninitializedMemory);

                jobHandle = new JoinChunksJob
                {
                    DestinationOffsets = filteredCounts,
                    SparseChunks = sparseChunks,
                    Offsets = offsets,
                    JoinedChunks = joinedChunks
                }.Schedule(archetypeCount, 1);

                return joinedChunks;
            }
        }

        [BurstMonoInteropMethod(MakePublic = false)]
        private static void _GatherChunks(in UnsafeCachedChunkList cache, ArchetypeChunk* chunks)
        {
            EntityComponentStore* entityComponentStore = cache.EntityComponentStore;

            int chunkCount = cache.Length;
            for (int index = 0; index < chunkCount; ++index)
            {
                var srcChunk = cache.Ptr[index];
                chunks[index] = new ArchetypeChunk(srcChunk, entityComponentStore);
            }
        }

        [BurstMonoInteropMethod(MakePublic = false)]
        private static void _GatherChunksWithFilter(in UnsafeCachedChunkList cache,
            in UnsafeMatchingArchetypePtrList matchingArchetypePtrList,
            ref EntityQueryFilter filter,
            ref int filteredChunkCount,
            ArchetypeChunk* filteredChunks)
        {
            EntityComponentStore* entityComponentStore = matchingArchetypePtrList.entityComponentStore;
            MatchingArchetype** matchingArchetypes = matchingArchetypePtrList.Ptr;

            int chunkCount = cache.Length;
            for(int index = 0; index < chunkCount; index++)
            {
                var chunk = cache.Ptr[index];
                var cachePerChunkMatchingArchetypeIndex = *cache.PerChunkMatchingArchetypeIndex;
                var match = matchingArchetypes[cachePerChunkMatchingArchetypeIndex[index]];
                if (match->ChunkMatchesFilter(chunk->ListIndex, ref filter))
                {
                    filteredChunks[filteredChunkCount] = new ArchetypeChunk(chunk, entityComponentStore);
                    filteredChunkCount++;
                }

            }
        }

        [BurstMonoInteropMethod(MakePublic = false)]
        private static void _JoinChunks(int* DestinationOffsets, ArchetypeChunk* SparseChunks,
            int* Offsets,ArchetypeChunk* JoinedChunks, int archetypeCount)
        {
            for(int index = 0; index < archetypeCount; index++)
            {
                int destOffset = DestinationOffsets[index];
                int count = DestinationOffsets[index + 1] - destOffset;
                if (count != 0)
                    UnsafeUtility.MemCpy(JoinedChunks + destOffset,SparseChunks + Offsets[index],count * sizeof(ArchetypeChunk));
            }
        }

        /// <summary>
        /// Creates a NativeArray with all the chunks in a given archetype filtered by the provided EntityQueryFilter.
        /// This function will not sync the needed types in the EntityQueryFilter so they have to be synced manually before calling this function.
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes.</param>
        /// <param name="cache">The cache used to quickly look up archetypes</param>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="filter">Filter used to filter the resulting chunks</param>

        /// <returns>NativeArray of all the chunks in the matchingArchetypes list.</returns>
        public static NativeArray<ArchetypeChunk> CreateArchetypeChunkArray(UnsafeCachedChunkList cache, UnsafeMatchingArchetypePtrList matchingArchetypes,
            Allocator allocator, ref EntityQueryFilter filter)
        {

            if (!filter.RequiresMatchesFilter)
            {
                var chunks = CollectionHelper.CreateNativeArray<ArchetypeChunk>(cache.Length, allocator, NativeArrayOptions.UninitializedMemory);
                GatherChunks(cache,(ArchetypeChunk*)chunks.GetUnsafePtr());
                return chunks;
            }

            //filtered path
            var filteredChunks = CollectionHelper.CreateNativeArray<ArchetypeChunk>(cache.Length, allocator, NativeArrayOptions.UninitializedMemory);

            int filteredLength = 0;
            GatherChunksWithFilter(cache, matchingArchetypes,ref filter,
                ref filteredLength,
                (ArchetypeChunk*) filteredChunks.GetUnsafePtr());

            //compress final return value chunks to return a compact NativeArray
            var outputChunks = CollectionHelper.CreateNativeArray<ArchetypeChunk>(filteredLength, allocator, NativeArrayOptions.UninitializedMemory);
            UnsafeUtility.MemCpy(outputChunks.GetUnsafePtr(),filteredChunks.GetUnsafeReadOnlyPtr(),filteredLength * sizeof(ArchetypeChunk));

            filteredChunks.Dispose();

            return outputChunks;
        }

        /// <summary>
        /// Creates a NativeArray with all the chunks in a given archetype filtered by the provided EntityQueryFilter.
        /// This function will sync the needed types in the EntityQueryFilter.
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes (for filtered cases).</param>
        /// <param name="cache">cache to retrieve chunks.</param>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="filter">Filter used to filter the resulting chunks</param>
        /// <param name="dependencyManager">The ComponentDependencyManager belonging to this world</param>

        /// <returns>NativeArray of all the chunks in the matchingArchetypes list.</returns>
        public static NativeArray<ArchetypeChunk> CreateArchetypeChunkArray(
            UnsafeCachedChunkList cache, UnsafeMatchingArchetypePtrList matchingArchetypes, Allocator allocator,
            ref EntityQueryFilter filter, ComponentDependencyManager* dependencyManager)
        {
            EntityQuery.SyncFilterTypes(ref matchingArchetypes, ref filter, dependencyManager);
            return CreateArchetypeChunkArray(cache, matchingArchetypes, allocator, ref filter);
        }

        [BurstMonoInteropMethod(MakePublic = false)]
        private static void _GatherEntitiesWithFilter(Entity* entities,ref EntityQueryFilter filter,
            ref UnsafeMatchingArchetypePtrList matchingArchetypePtrList,ref EntityTypeHandle entityTypeHandle, in UnsafeCachedChunkList cache)
        {
            MatchingArchetype** matchingArchetypes = matchingArchetypePtrList.Ptr;
            var cachePerChunkMatchingArchetypeIndex = *cache.PerChunkMatchingArchetypeIndex;

            int chunkCount = cache.Length;
            int currentChunkEntityOffset = 0;
            for (int index = 0; index < chunkCount; ++index)
            {
                var srcChunk = cache.Ptr[index];

                var match = matchingArchetypes[cachePerChunkMatchingArchetypeIndex[index]];
                if (match->ChunkMatchesFilter(srcChunk->ListIndex, ref filter))
                {
                    var archetypeChunk = new ArchetypeChunk(srcChunk, cache.EntityComponentStore);

                    var destinationPtr = entities + currentChunkEntityOffset;
                    var sourcePtr = archetypeChunk.GetNativeArray(entityTypeHandle).GetUnsafeReadOnlyPtr();
                    var copySizeInBytes = sizeof(Entity) * archetypeChunk.Count;

                    UnsafeUtility.MemCpy(destinationPtr, sourcePtr, copySizeInBytes);

                    currentChunkEntityOffset += srcChunk->Count;
                }


            }

        }

        [BurstMonoInteropMethod(MakePublic = false)]
        private static void _GatherEntities(Entity* entities, ref EntityTypeHandle entityTypeHandle, in UnsafeCachedChunkList cache)
        {
            int chunkCount = cache.Length;
            int currentChunkEntityOffset = 0;
            for (int index = 0; index < chunkCount; ++index)
            {
                var srcChunk = cache.Ptr[index];
                var archetypeChunk = new ArchetypeChunk(srcChunk, cache.EntityComponentStore);

                var destinationPtr = entities + currentChunkEntityOffset;
                var sourcePtr = archetypeChunk.GetNativeArray(entityTypeHandle).GetUnsafeReadOnlyPtr();
                var copySizeInBytes = sizeof(Entity) * archetypeChunk.Count;

                UnsafeUtility.MemCpy(destinationPtr, sourcePtr, copySizeInBytes);

                currentChunkEntityOffset += srcChunk->Count;
            }

        }

        /// <summary>
        ///     Creates a NativeArray containing the entities in a given EntityQuery and immediately completes the job.
        ///     Meant only for internal use only e.g. when passing temp memory into a job when being able to assure
        ///     that it will complete that frame
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes.</param>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="typeHandle">An atomic safety handle required by GatherEntitiesJob so it can call GetNativeArray() on chunks.</param>
        /// <param name="entityQuery">EntityQuery to gather entities from.</param>
        /// <param name="entityCount">number of entities to reserve for the returned NativeArray.</param>
        /// <param name="filter">EntityQueryFilter for calculating the length of the output array.</param>
        /// <param name="dependsOn">Handle to a job this GatherEntitiesJob must wait on.</param>
        /// <returns>NativeArray of the entities in a given EntityQuery.</returns>
        [NotBurstCompatible]
        public static NativeArray<Entity> CreateEntityArrayAsyncComplete(UnsafeMatchingArchetypePtrList matchingArchetypes,
            Allocator allocator,
            EntityTypeHandle typeHandle,
            EntityQuery entityQuery,
            int entityCount,
            JobHandle dependsOn)
        {
            var entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);

            var job = new GatherEntitiesJob
            {
                EntityTypeHandle = typeHandle,
                Entities = (byte*)entities.GetUnsafePtr()
            };
            var jobHandle = job.ScheduleParallel(entityQuery, dependsOn);
            jobHandle.Complete();

            return entities;
        }

        /// <summary>
        ///     Creates a NativeArray containing the entities in a given EntityQuery.
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes.</param>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="typeHandle">An atomic safety handle required by GatherEntitiesJob so it can call GetNativeArray() on chunks.</param>
        /// <param name="entityQuery">EntityQuery to gather entities from.</param>
        /// <param name="entityCount">number of entities to reserve for the returned NativeArray.</param>
        /// <param name="filter">EntityQueryFilter for calculating the length of the output array.</param>
        /// <returns>NativeArray of the entities in a given EntityQuery.</returns>
        public static NativeArray<Entity> CreateEntityArray(UnsafeMatchingArchetypePtrList matchingArchetypes,
            Allocator allocator,
            EntityTypeHandle typeHandle,
            EntityQuery entityQuery,
            int entityCount)
        {
            var cache = entityQuery.__impl->_QueryData->GetMatchingChunkCache();
            var entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);

            if (!entityQuery.HasFilter())
            {
                GatherEntities((Entity*)entities.GetUnsafePtr(), ref typeHandle, in cache);
            }
            else
            {
                var filter = entityQuery.__impl->_Filter;
                GatherEntitiesWithFilter((Entity*)entities.GetUnsafePtr(), ref filter, ref matchingArchetypes, ref typeHandle, in cache);
            }

            return entities;
        }


        [BurstMonoInteropMethod(MakePublic = true)]
        private static Entity* _CreateEntityArrayFromEntityArray(
            Entity* entities,
            int entityCount,
            Allocator allocator,
            EntityQueryData* queryData,
            EntityComponentStore* ecs,
            ref EntityQueryMask mask,
            ref EntityTypeHandle typeHandle,
            ref EntityQueryFilter filter,
            out int outEntityArrayLength)
        {
            Entity* res = null;
            if (filter.RequiresMatchesFilter)
            {
                var batches = new UnsafeList<ArchetypeChunk>(0, Allocator.TempJob);
                var matchingArchetypeIndices = new UnsafeList<int>(0, Allocator.TempJob);
                FindBatchesForEntityArrayWithQuery(ecs, queryData, true, entities, entityCount, &batches, &matchingArchetypeIndices);

                outEntityArrayLength = 0;
                for (int i = 0; i < batches.Length; ++i)
                {
                    var batch = ((ArchetypeChunk*)batches.Ptr)[i];
                    var match = queryData->MatchingArchetypes.Ptr[matchingArchetypeIndices.Ptr[i]];
                    if (batch.m_Chunk->MatchesFilter(match, ref filter))
                        outEntityArrayLength += batch.Count;
                }

                res = (Entity*)Memory.Unmanaged.Allocate(UnsafeUtility.SizeOf<Entity>() * outEntityArrayLength, UnsafeUtility.AlignOf<Entity>(), allocator);
                var entityCounter = 0;
                for (int i = 0; i < batches.Length; ++i)
                {
                    var batch = ((ArchetypeChunk*)batches.Ptr)[i];
                    var match = queryData->MatchingArchetypes.Ptr[matchingArchetypeIndices.Ptr[i]];
                    if (!batch.m_Chunk->MatchesFilter(match, ref filter))
                        continue;

                    var destinationPtr = res + entityCounter;
                    var sourcePtr = batch.GetNativeArray(typeHandle).GetUnsafeReadOnlyPtr();
                    var copySizeInBytes = sizeof(Entity) * batch.Count;

                    UnsafeUtility.MemCpy(destinationPtr, sourcePtr, copySizeInBytes);

                    entityCounter += batch.Count;
                }

                batches.Dispose();
                matchingArchetypeIndices.Dispose();
            }
            else
            {
                outEntityArrayLength = CalculateEntityCountInEntityArray(entities, entityCount, queryData, ecs, ref mask, ref filter);
                res = (Entity*)Memory.Unmanaged.Allocate(UnsafeUtility.SizeOf<Entity>() * outEntityArrayLength, UnsafeUtility.AlignOf<Entity>(), allocator);

                var entityCounter = 0;
                for (int i = 0; i < entityCount; ++i)
                {
                    var entity = entities[i];
                    if (mask.Matches(entity))
                        res[entityCounter++] = entity;
                }
            }

            return res;
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        private static byte* _CreateComponentDataArrayFromEntityArray(
            Entity* entities,
            int entityCount,
            Allocator allocator,
            EntityQueryData* queryData,
            EntityComponentStore* ecs,
            int typeIndex,
            int typeSizeInChunk,
            int typeAlign,
            ref EntityQueryMask mask,
            ref EntityQueryFilter filter,
            out int outEntityArrayLength)
        {
            byte* res = null;
            if (filter.RequiresMatchesFilter)
            {
                var batches = new UnsafeList<ArchetypeChunk>(0, Allocator.TempJob);
                var matchingArchetypeIndices = new UnsafeList<int>(0, Allocator.TempJob);
                FindBatchesForEntityArrayWithQuery(ecs, queryData, true, entities, entityCount, &batches, &matchingArchetypeIndices);

                outEntityArrayLength = 0;
                for (int i = 0; i < batches.Length; ++i)
                {
                    var batch = ((ArchetypeChunk*)batches.Ptr)[i];
                    var match = queryData->MatchingArchetypes.Ptr[matchingArchetypeIndices.Ptr[i]];
                    if (batch.m_Chunk->MatchesFilter(match, ref filter))
                        outEntityArrayLength += batch.Count;
                }

                res = (byte*)Memory.Unmanaged.Allocate(typeSizeInChunk * outEntityArrayLength, typeAlign, allocator);
                var outDataOffsetInBytes = 0;
                for (int i = 0; i < batches.Length; ++i)
                {
                    var batch = ((ArchetypeChunk*)batches.Ptr)[i];
                    var match = queryData->MatchingArchetypes.Ptr[matchingArchetypeIndices.Ptr[i]];
                    if (!batch.m_Chunk->MatchesFilter(match, ref filter))
                        continue;

                    var archetype = batch.Archetype.Archetype;
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);
                    var typeOffset = archetype->Offsets[indexInTypeArray];

                    var src = batch.m_Chunk->Buffer + typeOffset;
                    var dst = res + (outDataOffsetInBytes * typeSizeInChunk);
                    var copySize = typeSizeInChunk * batch.Count;

                    UnsafeUtility.MemCpy(dst, src, copySize);
                    outDataOffsetInBytes += copySize;
                }

                batches.Dispose();
                matchingArchetypeIndices.Dispose();
            }
            else
            {
                outEntityArrayLength = CalculateEntityCountInEntityArray(entities, entityCount, queryData, ecs, ref mask, ref filter);
                res = (byte*)Memory.Unmanaged.Allocate(typeSizeInChunk * outEntityArrayLength, typeAlign, allocator);

                var outDataOffsetInBytes = 0;
                for (int i = 0; i < entityCount; ++i)
                {
                    var entity = entities[i];
                    if (mask.Matches(entity))
                    {
                        var src = ecs->GetComponentDataWithTypeRO(entity, typeIndex);
                        var dst = res + outDataOffsetInBytes;
                        var copySize = typeSizeInChunk;

                        UnsafeUtility.MemCpy(dst, src, copySize);
                        outDataOffsetInBytes += copySize;
                    }
                }
            }

            return res;
        }

        /// <summary>
        ///     Creates a NativeArray containing the entities in a given EntityQuery.
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes.</param>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="typeHandle">An atomic safety handle required by GatherEntitiesJob so it can call GetNativeArray() on chunks.</param>
        /// <param name="entityQuery">EntityQuery to gather entities from.</param>
        /// <param name="entityCount">number of entities to reserve for the returned NativeArray.</param>
        /// <param name="filter">EntityQueryFilter for calculating the length of the output array.</param>
        /// <param name="jobHandle">Handle to the GatherEntitiesJob job used to fill the output array.</param>
        /// <param name="dependsOn">Handle to a job this GatherEntitiesJob must wait on.</param>
        /// <returns>NativeArray of the entities in a given EntityQuery.</returns>
        [NotBurstCompatible]
        public static NativeArray<Entity> CreateEntityArrayAsync(UnsafeMatchingArchetypePtrList matchingArchetypes,
            Allocator allocator,
            EntityTypeHandle typeHandle,
            EntityQuery entityQuery,
            int entityCount,
            out JobHandle jobHandle,
            JobHandle dependsOn)
        {
            var entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);

            var job = new GatherEntitiesJob
            {
                EntityTypeHandle = typeHandle,
                Entities = (byte*)entities.GetUnsafePtr()
            };
            jobHandle = job.ScheduleParallel(entityQuery, dependsOn);

            return entities;
        }

        [BurstMonoInteropMethod(MakePublic = false)]
        private static void _GatherComponentDataWithFilter(byte* componentData,int typeIndex, in UnsafeCachedChunkList cache,
            in UnsafeMatchingArchetypePtrList matchingArchetypePtrList, ref EntityQueryFilter filter)
        {

            var entityComponentStore = cache.EntityComponentStore;
            int currentChunkComponentOffset = 0;
            var cachePerChunkMatchingArchetypeIndex = *cache.PerChunkMatchingArchetypeIndex;
            MatchingArchetype** matchingArchetypes = matchingArchetypePtrList.Ptr;

            for (int i = 0; i < cache.Length; i++)
            {
                var srcChunk = cache.Ptr[i];
                var matchingArchetype = matchingArchetypes[cachePerChunkMatchingArchetypeIndex[i]];
                if (matchingArchetype->ChunkMatchesFilter(srcChunk->ListIndex, ref filter))
                {
                    var archetypeChunk = new ArchetypeChunk(srcChunk, entityComponentStore);
                    var archetype = archetypeChunk.Archetype.Archetype;
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);
                    var typeOffset = archetype->Offsets[indexInTypeArray];
                    var typeSize = archetype->SizeOfs[indexInTypeArray];

                    var src = archetypeChunk.m_Chunk->Buffer + typeOffset;
                    var dst = componentData + (currentChunkComponentOffset * typeSize);
                    var copySize = typeSize * archetypeChunk.Count;

                    UnsafeUtility.MemCpy(dst, src, copySize);

                    currentChunkComponentOffset += srcChunk->Count;
                }
            }
        }

        [BurstMonoInteropMethod(MakePublic = false)]
        private static void _GatherComponentData(byte* componentData,int typeIndex, in UnsafeCachedChunkList cache)
        {

            var entityComponentStore = cache.EntityComponentStore;
            int currentChunkEntityOffset = 0;
            for (int i = 0; i < cache.Length; i++)
            {
                var srcChunk = cache.Ptr[i];
                ArchetypeChunk archetypeChunk = new ArchetypeChunk(srcChunk, entityComponentStore);


                var archetype = archetypeChunk.Archetype.Archetype;
                var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);
                var typeOffset = archetype->Offsets[indexInTypeArray];
                var typeSize = archetype->SizeOfs[indexInTypeArray];

                var src = archetypeChunk.m_Chunk->Buffer + typeOffset;
                var dst = componentData + (currentChunkEntityOffset * typeSize);
                var copySize = typeSize * archetypeChunk.Count;

                UnsafeUtility.MemCpy(dst, src, copySize);

                currentChunkEntityOffset += srcChunk->Count;
            }
        }

        /// <summary>
        /// Creates a NativeArray with the value of a single component for all entities matching the provided query.
        /// The array will be populated by a job scheduled by this function.
        /// This function will not sync the needed types in the EntityQueryFilter so they have to be synced manually before calling this function.
        /// Meant only for internal use only e.g. when passing temp memory into a job when being able to assure
        /// that it will complete that frame
        /// </summary>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="typeHandle">Type handle for the component whose values should be extracted.</param>
        /// <param name="entityCount">Number of entities that match the query. Used as the output array size.</param>
        /// <param name="entityQuery">Entities that match this query will be included in the output.</param>
        /// <param name="dependsOn">Input job dependencies for the array-populating job.</param>
        /// <returns>NativeArray of all the chunks in the matchingArchetypes list.</returns>
        [NotBurstCompatible]
        public static NativeArray<T> CreateComponentDataArrayAsyncComplete<T>(
            Allocator allocator,
            ComponentTypeHandle<T> typeHandle,
            int entityCount,
            EntityQuery entityQuery,
            JobHandle dependsOn)
            where T : struct, IComponentData
        {
            var componentData = CollectionHelper.CreateNativeArray<T>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);

            var job = new GatherComponentDataJob
            {
                ComponentData = (byte*)componentData.GetUnsafePtr(),
                TypeIndex = typeHandle.m_TypeIndex
            };
            var jobHandle = job.ScheduleParallel(entityQuery, dependsOn);
            jobHandle.Complete();

            return componentData;
        }

        /// <summary>
        /// Creates a NativeArray with the value of a single component for all entities matching the provided query.
        /// The array will be populated by a job scheduled by this function.
        /// This function will not sync the needed types in the EntityQueryFilter so they have to be synced manually before calling this function.
        /// </summary>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="typeHandle">Type handle for the component whose values should be extracted.</param>
        /// <param name="entityCount">Number of entities that match the query. Used as the output array size.</param>
        /// <param name="entityQuery">Entities that match this query will be included in the output.</param>
        /// <param name="jobHandle">Handle to the job that will populate the output array. The caller must complete this job before the output array contents are valid.</param>
        /// <param name="dependsOn">Input job dependencies for the array-populating job.</param>
        /// <returns>NativeArray of all the chunks in the matchingArchetypes list.</returns>
        [NotBurstCompatible]
        public static NativeArray<T> CreateComponentDataArrayAsync<T>(
            Allocator allocator,
            ComponentTypeHandle<T> typeHandle,
            int entityCount,
            EntityQuery entityQuery,
            out JobHandle jobHandle,
            JobHandle dependsOn)
            where T : struct, IComponentData
        {
            var componentData = CollectionHelper.CreateNativeArray<T>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);

            var job = new GatherComponentDataJob
            {
                ComponentData = (byte*)componentData.GetUnsafePtr(),
                TypeIndex = typeHandle.m_TypeIndex
            };
            jobHandle = job.ScheduleParallel(entityQuery, dependsOn);

            return componentData;
        }

        /// <summary>
        /// Creates a NativeArray with the value of a single component for all entities matching the provided query.
        /// This function will not sync the needed types in the EntityQueryFilter so they have to be synced manually before calling this function.
        /// </summary>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="typeHandle">Type handle for the component whose values should be extracted.</param>
        /// <param name="entityCount">Number of entities that match the query. Used as the output array size.</param>
        /// <param name="entityQuery">Entities that match this query will be included in the output.</param>
        /// <returns>NativeArray of all the chunks in the matchingArchetypes list.</returns>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) }, RequiredUnityDefine = "UNITY_2020_2_OR_NEWER && !NET_DOTS")]
        public static NativeArray<T> CreateComponentDataArray<T>(
            Allocator allocator,
            ComponentTypeHandle<T> typeHandle,
            int entityCount,
            EntityQuery entityQuery)
            where T : struct, IComponentData
        {
            var cache = entityQuery.__impl->_QueryData->GetMatchingChunkCache();
            var matchingArchetypes = entityQuery.__impl->_QueryData->MatchingArchetypes;

            var componentData = CollectionHelper.CreateNativeArray<T>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);
            if (!entityQuery.HasFilter())
            {
                GatherComponentData((byte*)componentData.GetUnsafePtr(), typeHandle.m_TypeIndex, in cache);
            }
            else
            {
                var filter = entityQuery.__impl->_Filter;
                GatherComponentDataWithFilter((byte*)componentData.GetUnsafePtr(), typeHandle.m_TypeIndex, in cache, in matchingArchetypes, ref filter);
            }

            return componentData;
        }

        // In order to maximize EntityQuery.ForEach performance we want to avoid data allocation, as ForEach is main thread only we can afford to allocate a big array and use it to store result.
        // Let's not forget that calls to ForEach can be re-entrant, so we need to cover this use case too.
        // The current solution is to allocate an array of a fixed size (16kb) where will will store the result, we will fall back to the jobified implementation if we run out of space in the buffer
        static readonly int k_EntityQueryResultBufferSize = 16384 / sizeof(Entity);
        struct ResultBufferTag { }
        static readonly SharedStatic<IntPtr> s_EntityQueryResultBuffer = SharedStatic<IntPtr>.GetOrCreate<IntPtr, ResultBufferTag>();
        static readonly SharedStatic<int> s_CurrentOffsetInResultBuffer = SharedStatic<int>.GetOrCreate<int, ResultBufferTag>();

        internal static int currentOffsetInResultBuffer
        {
            get { return s_CurrentOffsetInResultBuffer.Data; }
            set { s_CurrentOffsetInResultBuffer.Data = value; }
        }

        public static void GatherEntitiesToArray(EntityQueryData* queryData, ref EntityQueryFilter filter, out EntityQuery.GatherEntitiesResult result)
        {
            if (s_EntityQueryResultBuffer.Data == IntPtr.Zero)
            {
                s_EntityQueryResultBuffer.Data = (IntPtr)Memory.Unmanaged.Allocate(k_EntityQueryResultBufferSize * sizeof(Entity), 64, Allocator.Persistent);
            }

            var buffer = (Entity*) s_EntityQueryResultBuffer.Data;
            var curOffset = currentOffsetInResultBuffer;

            // Main method that copies the entities of each chunk of a matching archetype to the buffer
            bool AddArchetype(MatchingArchetype* matchingArchetype, ref EntityQueryFilter queryFilter)
            {
                var archetype = matchingArchetype->Archetype;
                var entityCountInArchetype = archetype->EntityCount;
                if (entityCountInArchetype == 0)
                {
                    return true;
                }

                var chunkCount = archetype->Chunks.Count;
                var chunks = archetype->Chunks;
                var counts = archetype->Chunks.GetChunkEntityCountArray();

                for (int i = 0; i < chunkCount; ++i)
                {
                    // Ignore the chunk if the query uses filter and the chunk doesn't comply
                    if (queryFilter.RequiresMatchesFilter && (chunks[i]->MatchesFilter(matchingArchetype, ref queryFilter) == false))
                    {
                        continue;
                    }
                    var entityCountInChunk = counts[i];

                    if ((curOffset + entityCountInChunk) > k_EntityQueryResultBufferSize)
                    {
                        return false;
                    }

                    UnsafeUtility.MemCpy(buffer + curOffset, chunks[i]->Buffer, entityCountInChunk * sizeof(Entity));
                    curOffset += entityCountInChunk;
                }

                return true;
            }

            // Parse all the matching archetypes and add the entities that fits the query and its filter
            bool success = true;
            ref var matchingArchetypes = ref queryData->MatchingArchetypes;
            int archetypeCount = matchingArchetypes.Length;
            var ptrs = matchingArchetypes.Ptr;
            for (var m = 0; m < archetypeCount; m++)
            {
                var match = ptrs[m];
                if (!AddArchetype(match, ref filter))
                {
                    success = false;
                    break;
                }
            }

            result = new EntityQuery.GatherEntitiesResult { StartingOffset = currentOffsetInResultBuffer };
            if (success)
            {
                result.EntityCount = curOffset - currentOffsetInResultBuffer;
                result.EntityBuffer = (Entity*)s_EntityQueryResultBuffer.Data + currentOffsetInResultBuffer;
            }

            currentOffsetInResultBuffer = curOffset;
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        private static void _CopyComponentArrayToChunksWithFilter(byte* componentData,int typeIndex,
            ref UnsafeMatchingArchetypePtrList matchingArchetypePtrList, ref EntityQueryFilter filter, in UnsafeCachedChunkList cache,
            uint globalSystemVersion)
        {
            var matchingArchetypes = matchingArchetypePtrList.Ptr;
            var matchingIndices = *cache.PerChunkMatchingArchetypeIndex;

            int entityOffsetInChunk = 0;
            for(int i = 0; i < cache.Length; i++)
            {
                var chunk = cache.Ptr[i];
                var chunkCount = chunk->Count;
                var match = matchingArchetypes[matchingIndices[i]];

                if (match->ChunkMatchesFilter(chunk->ListIndex, ref filter))
                {
                    var archetype = chunk->Archetype;
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);
                    var typeOffset = archetype->Offsets[indexInTypeArray];
                    var typeSize = archetype->SizeOfs[indexInTypeArray];

                    LookupCache typeLookupCache = default;
                    var dst = ChunkDataUtility.GetComponentDataWithTypeRW(chunk, archetype, 0, typeIndex,
                        globalSystemVersion, ref typeLookupCache);
                    var src = componentData + (entityOffsetInChunk * typeSize);

                    var copySize = typeSize * chunkCount;

                    UnsafeUtility.MemCpy(dst, src, copySize);

                    entityOffsetInChunk += chunkCount;
                }
            }
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        private static void _CopyComponentArrayToChunks(byte* componentData,int typeIndex, in UnsafeCachedChunkList cache,
            uint globalSystemVersion)
        {
            int entityOffsetInChunk = 0;
            for(int i = 0; i < cache.Length; i++)
            {
                var chunk = cache.Ptr[i];
                var chunkCount = chunk->Count;

                var archetype = chunk->Archetype;
                var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);
                var typeOffset = archetype->Offsets[indexInTypeArray];
                var typeSize = archetype->SizeOfs[indexInTypeArray];

                LookupCache typeLookupCache = default;
                var dst = ChunkDataUtility.GetComponentDataWithTypeRW(chunk, archetype, 0, typeIndex,
                    globalSystemVersion, ref typeLookupCache);
                var src = componentData + (entityOffsetInChunk * typeSize);

                var copySize = typeSize * chunkCount;

                UnsafeUtility.MemCpy(dst, src, copySize);

                entityOffsetInChunk += chunkCount;
            }
        }
        ///Meant only for internal use only e.g. when passing temp memory into a job when being able to assure
        ///that it will complete that frame
        [NotBurstCompatible]
        public static void CopyFromComponentDataArrayAsyncComplete<T>(UnsafeMatchingArchetypePtrList matchingArchetypes,
            NativeArray<T> componentDataArray,
            ComponentTypeHandle<T> typeHandle,
            EntityQuery entityQuery,
            ref EntityQueryFilter filter,
            JobHandle dependsOn)
            where T : struct, IComponentData
        {
            var job = new CopyComponentArrayToChunksJob
            {
                ComponentData = (byte*)componentDataArray.GetUnsafePtr(),
                TypeIndex = typeHandle.m_TypeIndex,
                GlobalSystemVersion = typeHandle.GlobalSystemVersion,
            };
            var jobHandle = job.ScheduleParallel(entityQuery, dependsOn);
            jobHandle.Complete();
        }


        [NotBurstCompatible]
        public static void CopyFromComponentDataArrayAsync<T>(UnsafeMatchingArchetypePtrList matchingArchetypes,
            NativeArray<T> componentDataArray,
            ComponentTypeHandle<T> typeHandle,
            EntityQuery entityQuery,
            ref EntityQueryFilter filter,
            out JobHandle jobHandle,
            JobHandle dependsOn)
            where T : struct, IComponentData
        {
            var job = new CopyComponentArrayToChunksJob
            {
                ComponentData = (byte*)componentDataArray.GetUnsafePtr(),
                TypeIndex = typeHandle.m_TypeIndex,
                GlobalSystemVersion = typeHandle.GlobalSystemVersion,
            };
            jobHandle = job.ScheduleParallel(entityQuery, dependsOn);
        }

        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) }, RequiredUnityDefine = "UNITY_2020_2_OR_NEWER && !NET_DOTS")]
        public static void CopyFromComponentDataArray<T>(
            NativeArray<T> componentDataArray,
            ComponentTypeHandle<T> typeHandle,
            EntityQuery entityQuery)
            where T : struct, IComponentData
        {
            var matchingArchetypePtrList = entityQuery.__impl->_QueryData->MatchingArchetypes;
            var cache = entityQuery.__impl->_QueryData->GetMatchingChunkCache();

            if (!entityQuery.HasFilter())
            {
                CopyComponentArrayToChunks((byte*)componentDataArray.GetUnsafePtr(), typeHandle.m_TypeIndex,
                    in cache, typeHandle.GlobalSystemVersion);
            }
            else
            {
                var filter = entityQuery.__impl->_Filter;

                CopyComponentArrayToChunksWithFilter((byte*)componentDataArray.GetUnsafePtr(), typeHandle.m_TypeIndex,
                    ref matchingArchetypePtrList, ref filter, in cache, typeHandle.GlobalSystemVersion);
            }
        }

        /// <summary>
        ///     Total number of entities and chunks contained in a given MatchingArchetype list.
        ///
        /// This function will not sync the needed types in the EntityQueryFilter so they have to be synced manually before calling this function.
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes.</param>
        /// <param name="filter">EntityQueryFilter to use when calculating total number of entities.</param>
        /// <param name="chunkCount">The returned number of chunks in a list of archetypes.</param>
        /// <returns>Number of entities</returns>
        [BurstMonoInteropMethod(MakePublic = true)]
        private  static int _CalculateChunkAndEntityCount(ref UnsafeMatchingArchetypePtrList matchingArchetypes, ref EntityQueryFilter filter,
            out int chunkCount)
        {
            var length = 0;
            chunkCount = 0;
            int archetypeCount = matchingArchetypes.Length;
            var ptrs = matchingArchetypes.Ptr;
            if (!filter.RequiresMatchesFilter)
            {
                for (var m = 0; m < archetypeCount; ++m)
                {
                    var match = ptrs[m];
                    length += match->Archetype->EntityCount;
                    chunkCount += match->Archetype->Chunks.Count;
                }
            }
            else
            {
                for (var m = 0; m < archetypeCount; ++m)
                {
                    var match = ptrs[m];
                    if (match->Archetype->EntityCount <= 0)
                        continue;

                    int filteredCount = 0;
                    var archetype = match->Archetype;
                    int chunksWithArchetype = archetype->Chunks.Count;
                    var chunkEntityCountArray = archetype->Chunks.GetChunkEntityCountArray();

                    for (var i = 0; i < chunksWithArchetype; ++i)
                    {
                        if (match->ChunkMatchesFilter(i, ref filter))
                        {
                            filteredCount += chunkEntityCountArray[i];
                            chunkCount++;
                        }
                    }

                    length += filteredCount;
                }
            }

            return length;
        }

        /// <summary>
        ///     Total number of chunks in a given MatchingArchetype list.
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes.</param>
        /// <returns>Number of chunks in a list of archetypes.</returns>
        [BurstMonoInteropMethod(MakePublic = true)]
        static int _CalculateChunkCount(ref UnsafeMatchingArchetypePtrList matchingArchetypes, ref EntityQueryFilter filter)
        {
            var totalChunkCount = 0;
            int archetypeCount = matchingArchetypes.Length;
            var ptrs = matchingArchetypes.Ptr;

            // If no filter, then fast path it
            if (!filter.RequiresMatchesFilter)
            {
                for (var m = 0; m < archetypeCount; ++m)
                {
                    var match = ptrs[m];
                    totalChunkCount += match->Archetype->Chunks.Count;
                }

                return totalChunkCount;
            }

            // Otherwise do filtering
            for (var m = 0; m < archetypeCount; ++m)
            {
                var match = ptrs[m];
                var archetype = match->Archetype;
                int chunkCount = archetype->Chunks.Count;

                for (var i = 0; i < chunkCount; ++i)
                {
                    if (match->ChunkMatchesFilter(i, ref filter))
                        totalChunkCount++;
                }
            }

            return totalChunkCount;
        }

        /// <summary>
        ///     Total number of entities contained in a given MatchingArchetype list.
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes.</param>
        /// <param name="filter">EntityQueryFilter to use when calculating total number of entities.</param>
        /// <returns>Number of entities</returns>
        [BurstMonoInteropMethod(MakePublic = true)]
        private static int _CalculateEntityCount(ref UnsafeMatchingArchetypePtrList matchingArchetypes, ref EntityQueryFilter filter, int doesRequireBatching)
        {
            var length = 0;
            int archetypeCount = matchingArchetypes.Length;
            var ptrs = matchingArchetypes.Ptr;
            var requiresFilter = filter.RequiresMatchesFilter;
            var requiresBatching = doesRequireBatching == 1;
            if (!requiresFilter && !requiresBatching)
            {
                for (var m = 0; m < archetypeCount; ++m)
                {
                    var match = ptrs[m];
                    length += match->Archetype->EntityCount;
                }
            }
            else if (requiresBatching)
            {
                var batches = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];

                for (var m = 0; m < archetypeCount; ++m)
                {
                    var match = ptrs[m];
                    if (match->Archetype->EntityCount <= 0)
                        continue;

                    int filteredCount = 0;
                    var archetype = match->Archetype;
                    int chunkCount = archetype->Chunks.Count;
                    var chunkEntityCountArray = archetype->Chunks.GetChunkEntityCountArray();

                    for (var chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
                    {
                        var chunk = archetype->Chunks[chunkIndex];
                        if (requiresFilter && !match->ChunkMatchesFilter(chunkIndex, ref filter))
                            continue;

                        var chunkRequiresBatching = ChunkIterationUtility.DoesChunkRequireBatching(chunk, match, out var skipChunk);
                        if (skipChunk)
                            continue;

                        if (chunkRequiresBatching)
                        {
                            ChunkIterationUtility.FindBatchesForChunk(chunk, match, matchingArchetypes.entityComponentStore, batches, out var batchCount);
                            for (int batchIndex = 0; batchIndex < batchCount; ++batchIndex)
                            {
                                filteredCount += batches[batchIndex].Count;
                            }
                        }
                        else
                        {
                            filteredCount += chunkEntityCountArray[chunkIndex];
                        }
                    }

                    length += filteredCount;
                }

            }
            else
            {
                for (var m = 0; m < archetypeCount; ++m)
                {
                    var match = ptrs[m];
                    if (match->Archetype->EntityCount <= 0)
                        continue;

                    int filteredCount = 0;
                    var archetype = match->Archetype;
                    int chunkCount = archetype->Chunks.Count;
                    var chunkEntityCountArray = archetype->Chunks.GetChunkEntityCountArray();

                    for (var chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
                    {
                        if (match->ChunkMatchesFilter(chunkIndex, ref filter))
                            filteredCount += chunkEntityCountArray[chunkIndex];
                    }

                    length += filteredCount;
                }
            }

            return length;
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        private static int _CalculateEntityCountInEntityArray(
            Entity* entities,
            int entityCount,
            EntityQueryData* queryData,
            EntityComponentStore* ecs,
            ref EntityQueryMask mask,
            ref EntityQueryFilter filter)
        {
            var length = 0;
            if (filter.RequiresMatchesFilter)
            {
                var batches = new UnsafeList<ArchetypeChunk>(0,Allocator.TempJob);
                var matchingArchetypeIndices = new UnsafeList<int>(0, Allocator.TempJob);
                FindBatchesForEntityArrayWithQuery(ecs, queryData, true, entities, entityCount, &batches, &matchingArchetypeIndices);

                for (int i = 0; i < batches.Length; ++i)
                {
                    var batch = ((ArchetypeChunk*)batches.Ptr)[i];
                    var match = queryData->MatchingArchetypes.Ptr[matchingArchetypeIndices.Ptr[i]];
                    if (batch.m_Chunk->MatchesFilter(match, ref filter))
                        length += batch.Count;
                }

                batches.Dispose();
                matchingArchetypeIndices.Dispose();
            }
            else
            {
                for (int i = 0; i < entityCount; ++i)
                {
                    if (mask.Matches(entities[i]))
                        length++;
                }
            }

            return length;
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        private static bool _MatchesAnyInEntityArray(
            Entity* entities,
            int entityCount,
            EntityQueryData* queryData,
            EntityComponentStore* ecs,
            ref EntityQueryMask mask,
            ref EntityQueryFilter filter)
        {
            if (filter.RequiresMatchesFilter)
            {
                var batches = new UnsafeList<ArchetypeChunk>(0, Allocator.TempJob);
                var matchingArchetypeIndices = new UnsafeList<int>(0, Allocator.TempJob);
                FindBatchesForEntityArrayWithQuery(ecs, queryData, true, entities, entityCount, &batches, &matchingArchetypeIndices);

                var ret = false;
                for (int i = 0; i < batches.Length; ++i)
                {
                    var batch = ((ArchetypeChunk*)batches.Ptr)[i];
                    var match = queryData->MatchingArchetypes.Ptr[matchingArchetypeIndices.Ptr[i]];
                    if (batch.m_Chunk->MatchesFilter(match, ref filter))
                    {
                        ret = true;
                        break;
                    }
                }

                batches.Dispose();
                matchingArchetypeIndices.Dispose();
                return ret;
            }
            else
            {
                for (int i = 0; i < entityCount; ++i)
                {
                    if (mask.Matches(entities[i]))
                        return true;
                }
            }

            return false;
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _RebuildChunkListCache(EntityQueryData* queryData)
        {
            ref var cache = ref queryData->MatchingChunkCache;

            cache.MatchingChunks->Clear();
            cache.PerChunkMatchingArchetypeIndex->Clear();

            int archetypeCount = queryData->MatchingArchetypes.Length;
            var ptrs = queryData->MatchingArchetypes.Ptr;
            for (int matchingArchetypeIndex = 0; matchingArchetypeIndex < archetypeCount; ++matchingArchetypeIndex)
            {
                var archetype = ptrs[matchingArchetypeIndex]->Archetype;
                if (archetype->EntityCount > 0)
                    archetype->Chunks.AddToCachedChunkList(ref cache, matchingArchetypeIndex);
            }

            cache.CacheValid = 1;
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleBufferElement) }, RequiredUnityDefine = "ENABLE_UNITY_COLLECTIONS_CHECKS", CompileTarget = BurstCompatibleAttribute.BurstCompatibleCompileTarget.Editor)]
        internal static BufferAccessor<T> GetChunkBufferAccessor<T>(Chunk* chunk, bool isWriting, int typeIndexInArchetype, uint systemVersion, AtomicSafetyHandle safety0, AtomicSafetyHandle safety1)
#else
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleBufferElement) })]
        internal static BufferAccessor<T> GetChunkBufferAccessor<T>(Chunk * chunk, bool isWriting, int typeIndexInArchetype, uint systemVersion)
#endif
            where T : struct, IBufferElementData
        {
            var archetype = chunk->Archetype;
            int internalCapacity = archetype->BufferCapacities[typeIndexInArchetype];

            byte* ptr = (!isWriting)
                ? ChunkDataUtility.GetComponentDataRO(chunk, 0, typeIndexInArchetype)
                : ChunkDataUtility.GetComponentDataRW(chunk, 0, typeIndexInArchetype, systemVersion);

            var length = chunk->Count;
            int stride = archetype->SizeOfs[typeIndexInArchetype];
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            return new BufferAccessor<T>(ptr, length, stride, !isWriting, safety0, safety1, internalCapacity);
#else
            return new BufferAccessor<T>(ptr, length, stride, internalCapacity);
#endif
        }

        internal static void* GetChunkComponentDataPtr(Chunk* chunk, bool isWriting, int indexInArchetype, uint systemVersion)
        {
            byte* ptr = (!isWriting)
                ? ChunkDataUtility.GetComponentDataRO(chunk, 0, indexInArchetype)
                : ChunkDataUtility.GetComponentDataRW(chunk, 0, indexInArchetype, systemVersion);
            return ptr;
        }

        internal static void* GetChunkComponentDataROPtr(Chunk* chunk, int indexInArchetype)
        {
            var archetype = chunk->Archetype;
            return chunk->Buffer + archetype->Offsets[indexInArchetype];
        }

        internal static JobHandle PreparePrefilteredChunkListsAsync(int unfilteredChunkCount, UnsafeMatchingArchetypePtrList archetypes, EntityQueryFilter filter, JobHandle dependsOn, ScheduleMode mode, out NativeArray<byte> prefilterDataArray, out void* deferredCountData, out bool useFiltering)
        {
            // Allocate one buffer for all prefilter data and distribute it
            // We keep the full buffer as a "dummy array" so we can deallocate it later with [DeallocateOnJobCompletion]
            var sizeofChunkArray = sizeof(ArchetypeChunk) * unfilteredChunkCount;
            var sizeofIndexArray = sizeof(int) * unfilteredChunkCount;
            var prefilterDataSize = sizeofChunkArray + sizeofIndexArray + sizeof(int);

            var prefilterData = (byte*)Memory.Unmanaged.Allocate(prefilterDataSize, 64, Allocator.TempJob);
            prefilterDataArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(prefilterData, prefilterDataSize, Allocator.TempJob);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref prefilterDataArray, AtomicSafetyHandle.Create());
#endif

            JobHandle prefilterHandle = default(JobHandle);

            if (filter.RequiresMatchesFilter)
            {
                var prefilteringJob = new GatherChunksAndOffsetsWithFilteringJob
                {
                    Archetypes = archetypes,
                    Filter = filter,
                    PrefilterData = prefilterData,
                    UnfilteredChunkCount = unfilteredChunkCount
                };
                if (mode != ScheduleMode.Run)
                    prefilterHandle = prefilteringJob.Schedule(dependsOn);
                else
                    prefilteringJob.Run();
                useFiltering = true;
            }
            else
            {
                var gatherJob = new GatherChunksAndOffsetsJob
                {
                    Archetypes = archetypes,
                    PrefilterData = prefilterData,
                    UnfilteredChunkCount = unfilteredChunkCount,
                    entityComponentStore = archetypes.entityComponentStore
                };
                if (mode != ScheduleMode.Run)
                    prefilterHandle = gatherJob.Schedule(dependsOn);
                else
                    gatherJob.Run();
                useFiltering = false;
            }

            // ScheduleParallelForDeferArraySize expects a ptr to a structure with a void* and a count.
            // It only uses the count, so this is safe to fudge
            deferredCountData = prefilterData + sizeofChunkArray + sizeofIndexArray;
            deferredCountData = (byte*)deferredCountData - sizeof(void*);

            return prefilterHandle;
        }

        internal static void UnpackPrefilterData(NativeArray<byte> prefilterData, out ArchetypeChunk* chunks, out int* entityOffsets, out int filteredChunkCount)
        {
            chunks = (ArchetypeChunk*)prefilterData.GetUnsafePtr();
            filteredChunkCount = *(int*)((byte*)prefilterData.GetUnsafePtr() + prefilterData.Length - sizeof(int));

            Assert.IsTrue(filteredChunkCount >= 0);
            entityOffsets = (int*)(chunks + filteredChunkCount);
        }

        private static bool FindNextMatchingBatchStart(EntityQueryMask mask, Entity* entities, int totalEntityCount,  ref int currentIndexInEntityArray)
        {
            for (; currentIndexInEntityArray < totalEntityCount; ++currentIndexInEntityArray)
            {
                var currentEntity = entities[currentIndexInEntityArray];

                if (mask.Matches(currentEntity))
                    return true;
            }

            return false;
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _FindFilteredBatchesForEntityArrayWithQuery(
            EntityQueryImpl* query,
            Entity* entities, int entityCount,
            UnsafeList<ArchetypeChunk>* batches)
        {
            var queryImpl = query->_Access;

            var data = query->_QueryData;
            var ecs = query->_Access->EntityComponentStore;
            var queryMask = queryImpl->EntityQueryManager->GetEntityQueryMask(query->_QueryData, ecs);

            ref var filter = ref query->_Filter;
            var isFiltering = filter.RequiresMatchesFilter;

            // Start first batch
            var currentIndexInEntityArray = 0;
            while (FindNextMatchingBatchStart(queryMask, entities, entityCount, ref currentIndexInEntityArray))
            {
                var batchStartEntityInChunk = ecs->GetEntityInChunk(entities[currentIndexInEntityArray++]);

                var currentBatchChunk = batchStartEntityInChunk.Chunk;
                var currentBatchStartIndex = batchStartEntityInChunk.IndexInChunk;
                var currentBatchCounter = 1;

                for (; currentIndexInEntityArray < entityCount; ++currentIndexInEntityArray, currentBatchCounter++)
                {
                    var currentEntityInChunk = ecs->GetEntityInChunk(entities[currentIndexInEntityArray]);

                    // Check if we're looking at the next entity in the same chunk
                    if (currentEntityInChunk.Chunk != currentBatchChunk || currentEntityInChunk.IndexInChunk != currentBatchStartIndex + currentBatchCounter)
                        break;
                }

                if (isFiltering)
                {
                    var matchingArchetypeIndex = EntityQueryManager.FindMatchingArchetypeIndexForArchetype(ref data->MatchingArchetypes, currentBatchChunk->Archetype);
                    if (!currentBatchChunk->MatchesFilter(data->MatchingArchetypes.Ptr[matchingArchetypeIndex], ref filter))
                        continue;
                }

                // Finish the batch
                batches->Add(new ArchetypeChunk
                {
                    m_Chunk = currentBatchChunk,
                    m_EntityComponentStore = ecs,
                    m_BatchStartEntityIndex = currentBatchStartIndex,
                    m_BatchEntityCount = currentBatchCounter
                });
            }
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _FindBatchesForEntityArrayWithQuery(
            EntityComponentStore* ecs,
            EntityQueryData* data,
            bool requiresFilteringOrBatching,
            Entity* entities,
            int entityCount,
            UnsafeList<ArchetypeChunk>* batches,
            UnsafeList<int>* perBatchMatchingArchetypeIndex)
        {
            // Start first batch
            var currentIndexInEntityArray = 0;
            while (FindNextMatchingBatchStart(data->EntityQueryMask, entities, entityCount, ref currentIndexInEntityArray))
            {
                var batchStartEntityInChunk = ecs->GetEntityInChunk(entities[currentIndexInEntityArray++]);

                var currentBatchChunk = batchStartEntityInChunk.Chunk;
                var currentBatchStartIndex = batchStartEntityInChunk.IndexInChunk;
                var currentBatchCounter = 1;

                for (; currentIndexInEntityArray < entityCount; ++currentIndexInEntityArray, currentBatchCounter++)
                {
                    var currentEntityInChunk = ecs->GetEntityInChunk(entities[currentIndexInEntityArray]);

                    // Check if we're looking at the next entity in the same chunk
                    if (currentEntityInChunk.Chunk != currentBatchChunk || currentEntityInChunk.IndexInChunk != currentBatchStartIndex + currentBatchCounter)
                        break;
                }

                // Finish the batch
                batches->Add(new ArchetypeChunk
                {
                    m_Chunk = currentBatchChunk,
                    m_EntityComponentStore = ecs,
                    m_BatchStartEntityIndex = currentBatchStartIndex,
                    m_BatchEntityCount = currentBatchCounter
                });

                if(requiresFilteringOrBatching)
                    perBatchMatchingArchetypeIndex->Add(EntityQueryManager.FindMatchingArchetypeIndexForArchetype(ref data->MatchingArchetypes, currentBatchChunk->Archetype));
            }
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _FindBatchesForChunk(Chunk* chunk, MatchingArchetype* matchingArchetype, EntityComponentStore* ecs, ArchetypeChunk* outBatches, out int outBatchCount)
        {
            var chunkData = chunk->Archetype->Chunks;

            var currentBatchStartEntityIndex = 0;
            var currentBatchEntityCount = 0;
            var nextBatchStartEntityIndex = 0;

            var batchCounter = 0;

            // Iterate through the chunk, looking for gaps
            while (nextBatchStartEntityIndex < chunk->Count)
            {
                currentBatchStartEntityIndex = nextBatchStartEntityIndex;

                var matchingEntities = 0;
                var searchingForNextIndex = false;
                var advanceToNextBatchCount = 0;

                var allComponentCount = matchingArchetype->EnableableComponentsCount_All;
                var noneComponentCount = matchingArchetype->EnableableComponentsCount_None;
                var chunkEntityCount = chunk->Count;

                // 64 entities at a time...
                for (int entityIndex = currentBatchStartEntityIndex; entityIndex < chunkEntityCount; entityIndex += 64)
                {
                    var matchesThisStride = ulong.MaxValue;

                    // Find the "matches" bitmask for "All" types in query for current stride
                    for (int typeIndex = 0; typeIndex < allComponentCount; ++typeIndex)
                    {
                        var typeIndexInArchetype = matchingArchetype->EnableableIndexInArchetype_All[typeIndex];
                        var mask = ~0ul;

                        var bits = chunkData.GetComponentEnabledArrayForTypeInChunk(typeIndexInArchetype, chunk->ListIndex);
                        var bitsAsLong = bits.GetBits(entityIndex, math.min(64, chunkEntityCount - entityIndex));

                        matchesThisStride &= (bitsAsLong & mask);
                    }

                    // AND with the "matches" bitmask for "None" types in query for current stride
                    for (int typeIndex = 0; typeIndex < noneComponentCount; ++typeIndex)
                    {
                        var typeIndexInArchetype = matchingArchetype->EnableableIndexInArchetype_None[typeIndex];
                        var entitiesToTest = math.min(64, chunkEntityCount - entityIndex);

                        var bits = chunkData.GetComponentEnabledArrayForTypeInChunk(typeIndexInArchetype, chunk->ListIndex);
                        var bitsAsLong = bits.GetBits(entityIndex, entitiesToTest);

                        var maskA = ~0ul;
                        var maskB = entitiesToTest == 64 ? maskA : (1ul << entitiesToTest) - 1;
                        matchesThisStride &= ((~bitsAsLong & maskB) & maskA);
                    }

                    var trailingZeroes = math.tzcnt(~matchesThisStride);
                    var tzMask = 0ul;
                    if (!searchingForNextIndex)
                    {
                        matchingEntities += trailingZeroes;
                        tzMask = trailingZeroes == 64 ? 0ul : (1ul << trailingZeroes) - 1ul;
                        searchingForNextIndex = trailingZeroes < 64;
                    }

                    // If we've found a gap, find the next start index
                    if (searchingForNextIndex)
                    {
                        var v2 = matchesThisStride & tzMask;
                        var v3 = matchesThisStride & ~v2;
                        var tz = math.tzcnt(v3);
                        advanceToNextBatchCount += tz;
                        nextBatchStartEntityIndex = currentBatchStartEntityIndex + advanceToNextBatchCount;

                        if (tz < 64)
                            break;
                    }
                    else
                    {
                        advanceToNextBatchCount += trailingZeroes;
                    }
                }

                if (matchingEntities > 0)
                {
                    currentBatchEntityCount = matchingEntities;
                    outBatches[batchCounter++] = new ArchetypeChunk
                    {
                        m_Chunk = chunk,
                        m_EntityComponentStore = ecs,
                        m_BatchEntityCount = currentBatchEntityCount,
                        m_BatchStartEntityIndex = currentBatchStartEntityIndex
                    };
                }
            }

            outBatchCount = batchCounter;
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static bool _DoesChunkRequireBatching(Chunk* chunk, MatchingArchetype* match, out bool skipChunk)
        {
            var archetype = chunk->Archetype;
            skipChunk = false;

            // If any of the expected components have any disabled entities, we need to perform batching
            for (int t = 0; t < match->EnableableComponentsCount_All; ++t)
            {
                var indexInArchetype = match->EnableableIndexInArchetype_All[t];
                if (archetype->Chunks.GetChunkDisabledCountForType(indexInArchetype, chunk->ListIndex) != 0)
                    return true;
            }

            // If any of the None components are disabled in the chunk, we need to perform batching
            for (int t = 0; t < match->EnableableComponentsCount_None; ++t)
            {
                var indexInArchetype = match->EnableableIndexInArchetype_None[t];
                if (archetype->Chunks.GetChunkDisabledCountForType(indexInArchetype, chunk->ListIndex) != 0)
                    return true;
            }

            // If none of the None components are disabled in the chunk, we can skip the chunk
            skipChunk = match->EnableableComponentsCount_None != 0;
            return false;
        }
    }
}
