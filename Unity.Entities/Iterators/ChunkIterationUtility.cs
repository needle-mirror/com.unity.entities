using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
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
                for (int i = 0; i < matchingArchetypes.Length; ++i)
                {
                    var archetype = matchingArchetypes.Ptr[i]->Archetype;
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
        private static void _GatherChunks(in UnsafeMatchingArchetypePtrList matchingArchetypesList,
            int* offsets, ArchetypeChunk* chunks)
        {
            MatchingArchetype** matchingArchetypes = matchingArchetypesList.Ptr;
            EntityComponentStore* entityComponentStore = matchingArchetypesList.entityComponentStore;

            for (int index = 0; index < matchingArchetypesList.Length; ++index)
            {
                var archetype = matchingArchetypes[index]->Archetype;
                var chunkCount = archetype->Chunks.Count;
                var offset = offsets[index];
                for (int i = 0; i < chunkCount; i++)
                {
                    var srcChunk = archetype->Chunks[i];
                    chunks[offset + i] = new ArchetypeChunk(srcChunk, entityComponentStore);
                }
            }
        }

        [BurstMonoInteropMethod(MakePublic = false)]
        private static void _GatherChunksWithFilter(in UnsafeMatchingArchetypePtrList matchingArchetypePtrList,
            ref EntityQueryFilter filter,
            int* offsets,
            int* filteredCounts,
            ArchetypeChunk* sparseChunks)
        {
            EntityComponentStore* entityComponentStore = matchingArchetypePtrList.entityComponentStore;
            MatchingArchetype** matchingArchetypes = matchingArchetypePtrList.Ptr;

            for(int index = 0; index < matchingArchetypePtrList.Length; index++)
            {
                int filteredCount = 0;
                var match = matchingArchetypes[index];
                var archetype = match->Archetype;
                int chunkCount = archetype->Chunks.Count;
                var writeIndex = offsets[index];
                var archetypeChunks = archetype->Chunks;

                for (var i = 0; i < chunkCount; ++i)
                {
                    if (match->ChunkMatchesFilter(i, ref filter))
                        sparseChunks[writeIndex + filteredCount++] =
                            new ArchetypeChunk(archetypeChunks[i], entityComponentStore);
                }

                filteredCounts[index] = filteredCount;
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
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="filter">Filter used to filter the resulting chunks</param>

        /// <returns>NativeArray of all the chunks in the matchingArchetypes list.</returns>
        public static NativeArray<ArchetypeChunk> CreateArchetypeChunkArray(UnsafeMatchingArchetypePtrList matchingArchetypes,
            Allocator allocator, ref EntityQueryFilter filter)
        {
            var archetypeCount = matchingArchetypes.Length;

            var offsets = new NativeArray<int>(archetypeCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var chunkCount = 0;
            {
                for (int i = 0; i < matchingArchetypes.Length; ++i)
                {
                    var archetype = matchingArchetypes.Ptr[i]->Archetype;
                    offsets[i] = chunkCount;
                    chunkCount += archetype->Chunks.Count;
                }
            }

            if (!filter.RequiresMatchesFilter)
            {
                var chunks = new NativeArray<ArchetypeChunk>(chunkCount, allocator, NativeArrayOptions.UninitializedMemory);
                GatherChunks(matchingArchetypes,(int *)offsets.GetUnsafeReadOnlyPtr(),(ArchetypeChunk*)chunks.GetUnsafePtr());

                offsets.Dispose();

                return chunks;
            }
            else
            {
                var filteredCounts =  new NativeArray<int>(archetypeCount + 1, Allocator.TempJob);
                var sparseChunks = new NativeArray<ArchetypeChunk>(chunkCount, Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory);

                GatherChunksWithFilter(matchingArchetypes,ref filter,(int *)offsets.GetUnsafeReadOnlyPtr(),
                    (int *)filteredCounts.GetUnsafePtr(),(ArchetypeChunk*) sparseChunks.GetUnsafePtr());


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

                JoinChunks((int*)filteredCounts.GetUnsafeReadOnlyPtr(),
                    (ArchetypeChunk*)sparseChunks.GetUnsafeReadOnlyPtr(), (int *) offsets.GetUnsafeReadOnlyPtr(),
                    (ArchetypeChunk *) joinedChunks.GetUnsafePtr(), archetypeCount);

                filteredCounts.Dispose();
                sparseChunks.Dispose();
                offsets.Dispose();

                return joinedChunks;
            }
        }

        /// <summary>
        /// Creates a NativeArray with all the chunks in a given archetype filtered by the provided EntityQueryFilter.
        /// This function will sync the needed types in the EntityQueryFilter.
        /// </summary>
        /// <param name="matchingArchetypes">List of matching archetypes.</param>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="filter">Filter used to filter the resulting chunks</param>
        /// <param name="dependencyManager">The ComponentDependencyManager belonging to this world</param>

        /// <returns>NativeArray of all the chunks in the matchingArchetypes list.</returns>
        public static NativeArray<ArchetypeChunk> CreateArchetypeChunkArray(
            UnsafeMatchingArchetypePtrList matchingArchetypes, Allocator allocator,
            ref EntityQueryFilter filter, ComponentDependencyManager* dependencyManager)
        {
            EntityQuery.SyncFilterTypes(ref matchingArchetypes, ref filter, dependencyManager);
            return CreateArchetypeChunkArray(matchingArchetypes, allocator, ref filter);
        }

        [BurstMonoInteropMethod(MakePublic = false)]
        private static void _GatherEntities(Entity* entities,ref EntityQuery entityQuery, ref EntityTypeHandle entityTypeHandle)
        {
            var chunkIterator = entityQuery.GetArchetypeChunkIterator();
            while (chunkIterator.MoveNext())
            {
                var archetypeChunk = chunkIterator.CurrentArchetypeChunk;

                var destinationPtr = entities + chunkIterator.CurrentChunkFirstEntityIndex;
                var sourcePtr = archetypeChunk.GetNativeArray(entityTypeHandle).GetUnsafeReadOnlyPtr();
                var copySizeInBytes = sizeof(Entity) * archetypeChunk.Count;

                UnsafeUtility.MemCpy(destinationPtr, sourcePtr, copySizeInBytes);
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
        public static NativeArray<Entity> CreateEntityArrayAsyncComplete(UnsafeMatchingArchetypePtrList matchingArchetypes,
            Allocator allocator,
            EntityTypeHandle typeHandle,
            EntityQuery entityQuery,
            int entityCount,
            JobHandle dependsOn)
        {
            var entities = new NativeArray<Entity>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);
            var job = new GatherEntitiesJob
            {
                EntityTypeHandle = typeHandle,
                Entities = (byte*)entities.GetUnsafePtr()
            };
            var jobHandle = job.Schedule(entityQuery, dependsOn);
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

            var entities = new NativeArray<Entity>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);

            GatherEntities((Entity*)entities.GetUnsafePtr(),ref entityQuery, ref typeHandle);

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
                var batches = new UnsafeList(Allocator.TempJob);
                var matchingArchetypeIndices = new UnsafeIntList(0, Allocator.TempJob);
                FindBatchesForEntityArrayWithQuery(ecs, queryData, ref filter, entities, entityCount,ref batches, ref matchingArchetypeIndices);

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
                var batches = new UnsafeList(Allocator.TempJob);
                var matchingArchetypeIndices = new UnsafeIntList(0, Allocator.TempJob);
                FindBatchesForEntityArrayWithQuery(ecs, queryData, ref filter, entities, entityCount,ref batches, ref matchingArchetypeIndices);

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
        public static NativeArray<Entity> CreateEntityArrayAsync(UnsafeMatchingArchetypePtrList matchingArchetypes,
            Allocator allocator,
            EntityTypeHandle typeHandle,
            EntityQuery entityQuery,
            int entityCount,
            out JobHandle jobHandle,
            JobHandle dependsOn)
        {
            var entities = new NativeArray<Entity>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);
            var job = new GatherEntitiesJob
            {
                EntityTypeHandle = typeHandle,
                Entities = (byte*)entities.GetUnsafePtr()
            };
            jobHandle = job.ScheduleParallel(entityQuery, 1, dependsOn);

            return entities;
        }

        [BurstMonoInteropMethod(MakePublic = false)]
        private static void _GatherComponentData(byte* componentData,int typeIndex, ref ArchetypeChunkIterator chunkIter)
        {
            while (chunkIter.MoveNext())
            {
                ArchetypeChunk chunk = chunkIter.CurrentArchetypeChunk;
                int entityOffset = chunkIter.CurrentChunkFirstEntityIndex;

                var archetype = chunk.Archetype.Archetype;
                var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);
                var typeOffset = archetype->Offsets[indexInTypeArray];
                var typeSize = archetype->SizeOfs[indexInTypeArray];

                var src = chunk.m_Chunk->Buffer + typeOffset;
                var dst = componentData + (entityOffset * typeSize);
                var copySize = typeSize * chunk.Count;

                UnsafeUtility.MemCpy(dst, src, copySize);
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
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) }, RequiredUnityDefine = "UNITY_2020_2_OR_NEWER && !NET_DOTS")]
        public static NativeArray<T> CreateComponentDataArrayAsyncComplete<T>(
            Allocator allocator,
            ComponentTypeHandle<T> typeHandle,
            int entityCount,
            EntityQuery entityQuery,
            JobHandle dependsOn)
            where T : struct, IComponentData
        {
            var componentData = new NativeArray<T>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);

            var job = new GatherComponentDataJob
            {
                ComponentData = (byte*)componentData.GetUnsafePtr(),
                TypeIndex = typeHandle.m_TypeIndex
            };
            var jobHandle = job.Schedule(entityQuery, dependsOn);
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
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) }, RequiredUnityDefine = "UNITY_2020_2_OR_NEWER && !NET_DOTS")]
        public static NativeArray<T> CreateComponentDataArrayAsync<T>(
            Allocator allocator,
            ComponentTypeHandle<T> typeHandle,
            int entityCount,
            EntityQuery entityQuery,
            out JobHandle jobHandle,
            JobHandle dependsOn)
            where T : struct, IComponentData
        {
            var componentData = new NativeArray<T>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);

            var job = new GatherComponentDataJob
            {
                ComponentData = (byte*)componentData.GetUnsafePtr(),
                TypeIndex = typeHandle.m_TypeIndex
            };
            jobHandle = job.ScheduleParallel(entityQuery, 1, dependsOn);

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
            var componentData = new NativeArray<T>(entityCount, allocator, NativeArrayOptions.UninitializedMemory);

            var archetypeChunkIterator = entityQuery.GetArchetypeChunkIterator();
            GatherComponentData((byte*)componentData.GetUnsafePtr(),typeHandle.m_TypeIndex,ref archetypeChunkIterator);

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
            for (var m = 0; m < matchingArchetypes.Length; m++)
            {
                var match = matchingArchetypes.Ptr[m];
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
        private static void _CopyComponentArrayToChunks(byte* componentData,int typeIndex, ref ArchetypeChunkIterator chunkIter)
        {
            while (chunkIter.MoveNext())
            {
                ArchetypeChunk chunk = chunkIter.CurrentArchetypeChunk;
                int entityOffset = chunkIter.CurrentChunkFirstEntityIndex;

                var archetype = chunk.Archetype.Archetype;
                var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);
                var typeOffset = archetype->Offsets[indexInTypeArray];
                var typeSize = archetype->SizeOfs[indexInTypeArray];

                var dst = chunk.m_Chunk->Buffer + typeOffset;
                var src = componentData + (entityOffset * typeSize);
                var copySize = typeSize * chunk.Count;

                UnsafeUtility.MemCpy(dst, src, copySize);
            }
        }
        ///Meant only for internal use only e.g. when passing temp memory into a job when being able to assure
        ///that it will complete that frame
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) }, RequiredUnityDefine = "UNITY_2020_2_OR_NEWER && !NET_DOTS")]
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
                TypeIndex = typeHandle.m_TypeIndex
            };
            var jobHandle = job.Schedule(entityQuery, dependsOn);
            jobHandle.Complete();
        }


        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) }, RequiredUnityDefine = "UNITY_2020_2_OR_NEWER && !NET_DOTS")]
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
                TypeIndex = typeHandle.m_TypeIndex
            };
            jobHandle = job.Schedule(entityQuery, dependsOn);
        }

        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) }, RequiredUnityDefine = "UNITY_2020_2_OR_NEWER && !NET_DOTS")]
        public static void CopyFromComponentDataArray<T>(
            NativeArray<T> componentDataArray,
            ComponentTypeHandle<T> typeHandle,
            EntityQuery entityQuery)
            where T : struct, IComponentData
        {
            var archetypeChunkIterator = entityQuery.GetArchetypeChunkIterator();
            CopyComponentArrayToChunks((byte*)componentDataArray.GetUnsafePtr(),typeHandle.m_TypeIndex,ref archetypeChunkIterator);
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
            if (!filter.RequiresMatchesFilter)
            {
                for (var m = 0; m < matchingArchetypes.Length; ++m)
                {
                    var match = matchingArchetypes.Ptr[m];
                    length += match->Archetype->EntityCount;
                    chunkCount += match->Archetype->Chunks.Count;
                }
            }
            else
            {
                for (var m = 0; m < matchingArchetypes.Length; ++m)
                {
                    var match = matchingArchetypes.Ptr[m];
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

            // If no filter, then fast path it
            if (!filter.RequiresMatchesFilter)
            {
                for (var m = 0; m < matchingArchetypes.Length; ++m)
                {
                    var match = matchingArchetypes.Ptr[m];
                    totalChunkCount += match->Archetype->Chunks.Count;
                }

                return totalChunkCount;
            }

            // Otherwise do filtering
            for (var m = 0; m < matchingArchetypes.Length; ++m)
            {
                var match = matchingArchetypes.Ptr[m];
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
        private static int _CalculateEntityCount(ref UnsafeMatchingArchetypePtrList matchingArchetypes, ref EntityQueryFilter filter)
        {
            var length = 0;
            if (!filter.RequiresMatchesFilter)
            {
                for (var m = 0; m < matchingArchetypes.Length; ++m)
                {
                    var match = matchingArchetypes.Ptr[m];
                    length += match->Archetype->EntityCount;
                }
            }
            else
            {
                for (var m = 0; m < matchingArchetypes.Length; ++m)
                {
                    var match = matchingArchetypes.Ptr[m];
                    if (match->Archetype->EntityCount <= 0)
                        continue;

                    int filteredCount = 0;
                    var archetype = match->Archetype;
                    int chunkCount = archetype->Chunks.Count;
                    var chunkEntityCountArray = archetype->Chunks.GetChunkEntityCountArray();

                    for (var i = 0; i < chunkCount; ++i)
                    {
                        if (match->ChunkMatchesFilter(i, ref filter))
                            filteredCount += chunkEntityCountArray[i];
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
                var batches = new UnsafeList(Allocator.TempJob);
                var matchingArchetypeIndices = new UnsafeIntList(0, Allocator.TempJob);
                FindBatchesForEntityArrayWithQuery(ecs, queryData, ref filter, entities, entityCount,ref batches, ref matchingArchetypeIndices);

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
                var batches = new UnsafeList(Allocator.TempJob);
                var matchingArchetypeIndices = new UnsafeIntList(0, Allocator.TempJob);
                FindBatchesForEntityArrayWithQuery(ecs, queryData, ref filter, entities, entityCount,ref batches, ref matchingArchetypeIndices);

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

            cache.MatchingChunks.Clear();
            cache.PerChunkMatchingArchetypeIndex.Clear();

            var archetypes = queryData->MatchingArchetypes;
            for (int matchingArchetypeIndex = 0; matchingArchetypeIndex < archetypes.Length; ++matchingArchetypeIndex)
            {
                var archetype = archetypes.Ptr[matchingArchetypeIndex]->Archetype;
                archetype->Chunks.AddToCachedChunkList(ref cache, matchingArchetypeIndex);
            }

            cache.CacheValid = true;
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleBufferElement) })]
        internal static BufferAccessor<T> GetChunkBufferAccessor<T>(Chunk* chunk, bool isWriting, int typeIndexInArchetype, uint systemVersion, AtomicSafetyHandle safety0, AtomicSafetyHandle safety1)
#else
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
            ref UnsafeList batches)
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
                    if (currentBatchChunk->MatchesFilter(data->MatchingArchetypes.Ptr[matchingArchetypeIndex], ref filter))
                        continue;
                }

                // Finish the batch
                batches.Add(new ArchetypeChunk
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
            ref EntityQueryFilter filter,
            Entity* entities,
            int entityCount,
            ref UnsafeList batches,
            ref UnsafeIntList perBatchMatchingArchetypeIndex)
        {
            var isFiltering = filter.RequiresMatchesFilter;

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
                batches.Add(new ArchetypeChunk
                {
                    m_Chunk = currentBatchChunk,
                    m_EntityComponentStore = ecs,
                    m_BatchStartEntityIndex = currentBatchStartIndex,
                    m_BatchEntityCount = currentBatchCounter
                });

                if(isFiltering)
                    perBatchMatchingArchetypeIndex.Add(EntityQueryManager.FindMatchingArchetypeIndexForArchetype(ref data->MatchingArchetypes, currentBatchChunk->Archetype));
            }
        }
    }
}
