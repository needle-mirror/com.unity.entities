using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Entities
{
    [BurstCompile]
    unsafe struct GatherChunksJob : IJobParallelForBurstSchedulable
    {
        [NativeDisableUnsafePtrRestriction] public EntityComponentStore* entityComponentStore;
        [NativeDisableUnsafePtrRestriction] public MatchingArchetype** MatchingArchetypes;
        [DeallocateOnJobCompletion]
        [ReadOnly] public NativeArray<int> Offsets;
        [NativeDisableParallelForRestriction] public NativeArray<ArchetypeChunk> Chunks;

        public void Execute(int index)
        {
            var archetype = MatchingArchetypes[index]->Archetype;
            var chunkCount = archetype->Chunks.Count;
            var offset = Offsets[index];
            for (int i = 0; i < chunkCount; i++)
            {
                var srcChunk = archetype->Chunks[i];
                Chunks[offset + i] = new ArchetypeChunk(srcChunk, entityComponentStore);

            }
        }
    }

    [BurstCompile]
    internal unsafe struct GatherChunksAndOffsetsJob : IJobBurstSchedulable
    {
        public UnsafeMatchingArchetypePtrList Archetypes;
        [NativeDisableUnsafePtrRestriction] public EntityComponentStore* entityComponentStore;

        [NativeDisableUnsafePtrRestriction]
        public void* PrefilterData;
        public int   UnfilteredChunkCount;

        public void Execute()
        {
            var chunks = (ArchetypeChunk*)PrefilterData;
            var entityIndices = (int*)(chunks + UnfilteredChunkCount);

            var chunkCounter = 0;
            var entityOffsetPrefixSum = 0;

            for (var m = 0; m < Archetypes.Length; ++m)
            {
                var match = Archetypes.Ptr[m];
                if (match->Archetype->EntityCount <= 0)
                    continue;

                var archetype = match->Archetype;
                int chunkCount = archetype->Chunks.Count;
                var chunkEntityCountArray = archetype->Chunks.GetChunkEntityCountArray();

                for (int chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
                {
                    chunks[chunkCounter] = new ArchetypeChunk(archetype->Chunks[chunkIndex], entityComponentStore);
                    entityIndices[chunkCounter++] = entityOffsetPrefixSum;
                    entityOffsetPrefixSum += chunkEntityCountArray[chunkIndex];
                }
            }

            var outChunkCounter = entityIndices + UnfilteredChunkCount;
            *outChunkCounter = chunkCounter;
        }
    }

    [BurstCompile]
    internal unsafe struct GatherChunksWithFilteringJob : IJobParallelForBurstSchedulable
    {
        [NativeDisableUnsafePtrRestriction] public EntityComponentStore* entityComponentStore;
        [NativeDisableUnsafePtrRestriction] public MatchingArchetype** MatchingArchetypes;
        public EntityQueryFilter Filter;

        [ReadOnly] public NativeArray<int> Offsets;
        public NativeArray<int> FilteredCounts;

        [NativeDisableParallelForRestriction] public NativeArray<ArchetypeChunk> SparseChunks;

        public void Execute(int index)
        {
            int filteredCount = 0;
            var match = MatchingArchetypes[index];
            var archetype = match->Archetype;
            int chunkCount = archetype->Chunks.Count;
            var writeIndex = Offsets[index];
            var archetypeChunks = archetype->Chunks;

            for (var i = 0; i < chunkCount; ++i)
            {
                if (match->ChunkMatchesFilter(i, ref Filter))
                    SparseChunks[writeIndex + filteredCount++] =
                        new ArchetypeChunk(archetypeChunks[i], entityComponentStore);
            }

            FilteredCounts[index] = filteredCount;
        }
    }

    [BurstCompile]
    internal unsafe struct GatherChunksAndOffsetsWithFilteringJob : IJobBurstSchedulable
    {
        public UnsafeMatchingArchetypePtrList Archetypes;
        public EntityQueryFilter Filter;

        [NativeDisableUnsafePtrRestriction]
        public void* PrefilterData;
        public int   UnfilteredChunkCount;

        public void Execute()
        {
            var chunks = (ArchetypeChunk*)PrefilterData;
            var entityIndices = (int*)(chunks + UnfilteredChunkCount);

            var filteredChunkCount = 0;
            var filteredEntityOffset = 0;

            for (var m = 0; m < Archetypes.Length; ++m)
            {
                var match = Archetypes.Ptr[m];
                if (match->Archetype->EntityCount <= 0)
                    continue;

                var archetype = match->Archetype;
                int chunkCount = archetype->Chunks.Count;
                var chunkEntityCountArray = archetype->Chunks.GetChunkEntityCountArray();

                for (var i = 0; i < chunkCount; ++i)
                {
                    if (match->ChunkMatchesFilter(i, ref Filter))
                    {
                        chunks[filteredChunkCount] =
                            new ArchetypeChunk(archetype->Chunks[i], Archetypes.entityComponentStore);
                        entityIndices[filteredChunkCount++] = filteredEntityOffset;
                        filteredEntityOffset += chunkEntityCountArray[i];
                    }
                }
            }

            UnsafeUtility.MemMove(chunks + filteredChunkCount, chunks + UnfilteredChunkCount, filteredChunkCount * sizeof(int));

            var chunkCounter = entityIndices + UnfilteredChunkCount;
            *chunkCounter = filteredChunkCount;
        }
    }

    struct JoinChunksJob : IJobParallelForBurstSchedulable
    {
        [DeallocateOnJobCompletion][ReadOnly] public NativeArray<int> DestinationOffsets;
        [DeallocateOnJobCompletion][ReadOnly] public NativeArray<ArchetypeChunk> SparseChunks;
        [DeallocateOnJobCompletion][ReadOnly] public NativeArray<int> Offsets;
        [NativeDisableParallelForRestriction]  public NativeArray<ArchetypeChunk> JoinedChunks;

        public void Execute(int index)
        {
            int destOffset = DestinationOffsets[index];
            int count = DestinationOffsets[index + 1] - destOffset;
            if (count != 0)
                NativeArray<ArchetypeChunk>.Copy(SparseChunks, Offsets[index], JoinedChunks, destOffset, count);
        }
    }

    [BurstCompile]
    unsafe struct GatherEntitiesJob : IJobEntityBatchWithIndex
    {
        [NativeDisableUnsafePtrRestriction] public byte* Entities;
        [ReadOnly] public EntityTypeHandle EntityTypeHandle;

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var destinationPtr = (Entity*)Entities + firstEntityIndex;
            var sourcePtr = chunk.GetNativeArray(EntityTypeHandle).GetUnsafeReadOnlyPtr();
            var copySizeInBytes = sizeof(Entity) * chunk.Count;

            UnsafeUtility.MemCpy(destinationPtr, sourcePtr, copySizeInBytes);
        }
    }

    [BurstCompile]
    unsafe struct GatherComponentDataJob : IJobEntityBatchWithIndex
    {
        [NativeDisableUnsafePtrRestriction] public byte* ComponentData;
        public int TypeIndex;

        public void Execute(ArchetypeChunk batchInChunk, int batchIndex, int indexOfFirstEntityInQuery)
        {
            var archetype = batchInChunk.Archetype.Archetype;
            var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype, TypeIndex);
            var typeOffset = archetype->Offsets[indexInTypeArray];
            var typeSize = archetype->SizeOfs[indexInTypeArray];

            var src = batchInChunk.m_Chunk->Buffer + typeOffset;
            var dst = ComponentData + (indexOfFirstEntityInQuery * typeSize);
            var copySize = typeSize * batchInChunk.Count;

            UnsafeUtility.MemCpy(dst, src, copySize);
        }
    }

    [BurstCompile]
    unsafe struct CopyComponentArrayToChunksJob : IJobEntityBatchWithIndex
    {
        [NativeDisableUnsafePtrRestriction] public byte* ComponentData;
        public int TypeIndex;

        public void Execute(ArchetypeChunk batchInChunk, int batchIndex, int indexOfFirstEntityInQuery)
        {
            var archetype = batchInChunk.Archetype.Archetype;
            var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype, TypeIndex);
            var typeOffset = archetype->Offsets[indexInTypeArray];
            var typeSize = archetype->SizeOfs[indexInTypeArray];

            var dst = batchInChunk.m_Chunk->Buffer + typeOffset;
            var src = ComponentData + (indexOfFirstEntityInQuery * typeSize);
            var copySize = typeSize * batchInChunk.Count;

            UnsafeUtility.MemCpy(dst, src, copySize);
        }
    }
}
