﻿using System;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unity.Entities
{
    [BurstCompile]
    unsafe struct GatherChunks : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public MatchingArchetype** MatchingArchetypes;
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> Offsets;
        [NativeDisableParallelForRestriction] public NativeArray<ArchetypeChunk> Chunks;

        public void Execute(int index)
        {
            var archetype = MatchingArchetypes[index]->Archetype;
            var chunkCount = archetype->Chunks.Count;
            var offset = Offsets[index];
            var dstChunksPtr = (Chunk**) Chunks.GetUnsafePtr();
            UnsafeUtility.MemCpy(dstChunksPtr + offset, archetype->Chunks.p, chunkCount * sizeof(Chunk*));
        }
    }

    [BurstCompile]
    unsafe struct GatherChunksWithFiltering : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public MatchingArchetype** MatchingArchetypes;
        public ComponentGroupFilter Filter;

        [ReadOnly] public NativeArray<int> Offsets;
        public NativeArray<int> FilteredCounts;

        [NativeDisableParallelForRestriction] public NativeArray<ArchetypeChunk> SparseChunks;

        public void Execute(int index)
        {
            var filter = Filter;
            int filteredCount = 0;
            var match = MatchingArchetypes[index];
            var archetype = match->Archetype;
            int chunkCount = archetype->Chunks.Count;
            var writeIndex = Offsets[index];
            var archetypeChunks = archetype->Chunks.p;

            if (filter.Type == FilterType.SharedComponent)
            {
                var indexInComponentGroup1 = filter.Shared.IndexInComponentGroup[0];
                var sharedComponentIndex1 = filter.Shared.SharedComponentIndex[0];
                var componentIndexInChunk1 = match->IndexInArchetype[indexInComponentGroup1] - archetype->FirstSharedComponent;
                var sharedComponents1 = archetype->Chunks.GetSharedComponentValueArrayForType(componentIndexInChunk1);

                if (filter.Shared.Count == 1)
                {
                    for (var i = 0; i < chunkCount; ++i)
                    {
                        if(sharedComponents1[i] == sharedComponentIndex1)
                            SparseChunks[writeIndex + filteredCount++] = new ArchetypeChunk { m_Chunk = archetypeChunks[i] };
                    }
                }
                else
                {
                    var indexInComponentGroup2 = filter.Shared.IndexInComponentGroup[1];
                    var sharedComponentIndex2 = filter.Shared.SharedComponentIndex[1];
                    var componentIndexInChunk2 = match->IndexInArchetype[indexInComponentGroup2] - archetype->FirstSharedComponent;
                    var sharedComponents2 = archetype->Chunks.GetSharedComponentValueArrayForType(componentIndexInChunk2);

                    for (var i = 0; i < chunkCount; ++i)
                    {

                        if(sharedComponents1[i] == sharedComponentIndex1 && sharedComponents2[i] == sharedComponentIndex2)
                            SparseChunks[writeIndex + filteredCount++] = new ArchetypeChunk { m_Chunk = archetypeChunks[i] };
                    }
                }
            }
            else
            {
                var indexInComponentGroup1 = filter.Changed.IndexInComponentGroup[0];
                var componentIndexInChunk1 = match->IndexInArchetype[indexInComponentGroup1];
                var changeVersions1 = archetype->Chunks.GetChangeVersionArrayForType(componentIndexInChunk1);

                var requiredVersion = filter.RequiredChangeVersion;
                if (filter.Changed.Count == 1)
                {
                    for (var i = 0; i < chunkCount; ++i)
                    {
                        if(ChangeVersionUtility.DidChange(changeVersions1[i], requiredVersion))
                            SparseChunks[writeIndex + filteredCount++] = new ArchetypeChunk { m_Chunk = archetypeChunks[i] };
                    }
                }
                else
                {
                    var indexInComponentGroup2 = filter.Shared.IndexInComponentGroup[1];
                    var componentIndexInChunk2 = match->IndexInArchetype[indexInComponentGroup2];
                    var changeVersions2 = archetype->Chunks.GetChangeVersionArrayForType(componentIndexInChunk2);

                    for (var i = 0; i < chunkCount; ++i)
                    {

                        if(ChangeVersionUtility.DidChange(changeVersions1[i], requiredVersion) || ChangeVersionUtility.DidChange(changeVersions2[i], requiredVersion))
                            SparseChunks[writeIndex + filteredCount++] = new ArchetypeChunk { m_Chunk = archetypeChunks[i] };
                    }
                }
            }

            FilteredCounts[index] = filteredCount;
        }
    }

    unsafe struct JoinChunksJob : IJobParallelFor
    {
        [DeallocateOnJobCompletion] [NativeDisableParallelForRestriction] public NativeArray<int> DestinationOffsets;
        [DeallocateOnJobCompletion] [NativeDisableParallelForRestriction] public NativeArray<ArchetypeChunk> SparseChunks;
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> Offsets;
        [NativeDisableParallelForRestriction] public NativeArray<ArchetypeChunk> JoinedChunks;

        public void Execute(int index)
        {
            int destOffset = DestinationOffsets[index];
            int count = DestinationOffsets[index+1]-destOffset;
            if(count != 0)
                NativeArray<ArchetypeChunk>.Copy(SparseChunks, Offsets[index], JoinedChunks, destOffset, count);
        }
    }

    [BurstCompile]
    unsafe struct GatherEntitiesJob : IJobChunk
    {
        public NativeArray<Entity> Entities;
        [ReadOnly]public ArchetypeChunkEntityType EntityType;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int entityOffset)
        {
            var destinationPtr = (Entity*)Entities.GetUnsafePtr() + entityOffset;
            var sourcePtr = chunk.GetNativeArray(EntityType).GetUnsafeReadOnlyPtr();
            var copySizeInBytes = sizeof(Entity) * chunk.Count;

            UnsafeUtility.MemCpy(destinationPtr, sourcePtr, copySizeInBytes);
        }
    }

    [BurstCompile]
    unsafe struct GatherComponentDataJob<T> : IJobChunk
        where T : struct,IComponentData
    {
        public NativeArray<T> ComponentData;
        [ReadOnly]public ArchetypeChunkComponentType<T> ComponentType;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int entityOffset)
        {
            var sourcePtr = chunk.GetNativeArray(ComponentType).GetUnsafeReadOnlyPtr();
            var destinationPtr = (byte*) ComponentData.GetUnsafePtr() + UnsafeUtility.SizeOf<T>() * entityOffset;
            var copySizeInBytes = UnsafeUtility.SizeOf<T>() * chunk.Count;

            UnsafeUtility.MemCpy(destinationPtr, sourcePtr, copySizeInBytes);
        }
    }

    [BurstCompile]
    unsafe struct CopyComponentArrayToChunks<T> : IJobChunk
        where T : struct,IComponentData
    {
        [ReadOnly]
        public NativeArray<T> ComponentData;
        public ArchetypeChunkComponentType<T> ComponentType;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int entityOffset)
        {
            var destinationPtr = chunk.GetNativeArray(ComponentType).GetUnsafePtr();
            var srcPtr = (byte*) ComponentData.GetUnsafeReadOnlyPtr() + UnsafeUtility.SizeOf<T>() * entityOffset;
            var copySizeInBytes = UnsafeUtility.SizeOf<T>() * chunk.Count;

            UnsafeUtility.MemCpy(destinationPtr, srcPtr, copySizeInBytes);
        }
    }

}