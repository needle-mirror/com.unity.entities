using System;
using System.Runtime.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Mathematics;

namespace Unity.Entities
{
    /// <summary>
    /// Helper utility to quickly identify the next available entity or component within a chunk (e.g. the index of
    /// the <see cref="NativeArray{T}"/> retrieved via <see cref="ArchetypeChunk.GetComponentDataPtrRW{T}"/> or
    /// <see cref="ArchetypeChunk.GetEntityDataPtrRO"/>)
    /// </summary>
    [GenerateTestsForBurstCompatibility]
    public struct ChunkEntityEnumerator
    {
        ulong mask64_0;
        ulong mask64_1;

        /// <summary>
        /// Construct a new instance.
        /// </summary>
        /// <param name="useEnabledMask">If true, <paramref name="chunkEnabledMask"/> contains valid data and should be used
        /// to determine which entities to include in the iteration. If false, the mask is ignored and all entities in the chunk
        /// are included. You can pass this value directly from the argument provided to <see cref="IJobChunk.Execute"/>.</param>
        /// <param name="chunkEnabledMask">A bitmask for all entities in the chunk. If bit N is set, entity N within
        /// this chunk should be included in the iteration. This mask is ignored if <paramref name="useEnabledMask"/>
        /// is false. You can pass this value directly from the argument provided to <see cref="IJobChunk.Execute"/>.</param>
        /// <param name="chunkEntityCount">The number of entities in the chunk.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ChunkEntityEnumerator(bool useEnabledMask, v128 chunkEnabledMask, int chunkEntityCount)
        {
            mask64_0 = useEnabledMask ? chunkEnabledMask.ULong0 : ~0ul;
            mask64_1 = useEnabledMask ? chunkEnabledMask.ULong1 : ~0ul;

            if (chunkEntityCount < 128)
            {
                if (chunkEntityCount < 64)
                {
                    mask64_0 &= ~(~0ul << chunkEntityCount);
                    mask64_1 = 0;
                }
                else
                {
                    mask64_1 &= ~(~0ul << (chunkEntityCount - 64));
                }
            }
        }

        /// <summary>
        /// Iterates through the given <see cref="ArchetypeChunk"/>, retrieving the index of the next available entity
        /// as defined by the mask info provided to the constructor.
        /// </summary>
        /// <param name="nextIndex">The index of the next available entity in the ArchetypeChunk,
        /// or -1 when the function returns false.</param>
        /// <returns>Whether the iteration should continue.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool NextEntityIndex(out int nextIndex)
        {
            if (mask64_0 != 0)
            {
                nextIndex = math.tzcnt(mask64_0);
                mask64_0 &= mask64_0 - 1;
                return true;
            }

            if (mask64_1 != 0)
            {
                nextIndex = math.tzcnt(mask64_1) + 64;
                mask64_1 &= mask64_1 - 1;
                return true;
            }

            nextIndex = -1;
            return false;
        }
    }
}
