#if ENABLE_PROFILER
using System;
using Unity.Collections;
using static Unity.Entities.EntitiesProfiler;

namespace Unity.Entities
{
    static partial class MemoryProfiler
    {
        [BurstCompatible(RequiredUnityDefine = "ENABLE_PROFILER")]
        public readonly unsafe struct ArchetypeMemoryData : IEquatable<ArchetypeMemoryData>
        {
            [BurstCompatible(RequiredUnityDefine = "ENABLE_PROFILER")]
            internal readonly struct SharedComponentValuesKey : IEquatable<SharedComponentValuesKey>
            {
                readonly Archetype* m_Archetype;
                readonly int m_ChunkIndex;

                public SharedComponentValuesKey(Archetype* archetype, int chunkIndex)
                {
                    m_Archetype = archetype;
                    m_ChunkIndex = chunkIndex;
                }

                int GetSharedComponentValue(int sharedComponentIndexInTypeArray)
                {
                    return *(m_Archetype->Chunks.GetSharedComponentValues(0).firstIndex + m_ChunkIndex + sharedComponentIndexInTypeArray * m_Archetype->Chunks.Capacity);
                }

                public bool Equals(SharedComponentValuesKey other)
                {
                    for (var i = 0; i < m_Archetype->NumSharedComponents; i++)
                    {
                        if (other.GetSharedComponentValue(i) != GetSharedComponentValue(i))
                            return false;
                    }
                    return true;
                }

                [NotBurstCompatible]
                public override bool Equals(object obj)
                {
                    return obj is SharedComponentValuesKey sharedComponentValuesKey ? Equals(sharedComponentValuesKey) : false;
                }

                public override int GetHashCode()
                {
                    var hash = 23;
                    for (var i = 0; i < m_Archetype->NumSharedComponents; i++)
                        hash = hash * 31 + GetSharedComponentValue(i);

                    return hash;
                }
            }

            public readonly ulong WorldSequenceNumber;
            public readonly ulong StableHash;
            public readonly int EntityCount;
            public readonly int ChunkCount;
            public readonly int SegmentCount;

            public ArchetypeMemoryData(ulong worldSeqNumber, Archetype* archetype)
            {
                WorldSequenceNumber = worldSeqNumber;
                StableHash = archetype->StableHash;
                EntityCount = archetype->EntityCount;
                ChunkCount = archetype->Chunks.Count;
                SegmentCount = CalculateUniqueSharedComponentValuesCount(archetype);
            }

            public ulong CalculateAllocatedBytes()
            {
                return (ulong)ChunkCount * Chunk.kChunkSize;
            }

            public ulong CalculateUnusedBytes(Archetype* archetype)
            {
                return (ulong)CalculateUnusedEntityCount(archetype) * (ulong)archetype->InstanceSize;
            }

            public ulong CalculateUnusedBytes(ArchetypeData archetypeData)
            {
                return (ulong)CalculateUnusedEntityCount(archetypeData) * (ulong)archetypeData.InstanceSize;
            }

            public int CalculateUnusedEntityCount(Archetype* archetype)
            {
                return (ChunkCount * archetype->ChunkCapacity) - EntityCount;
            }

            public int CalculateUnusedEntityCount(ArchetypeData archetypeData)
            {
                return (ChunkCount * archetypeData.ChunkCapacity) - EntityCount;
            }

            static int CalculateUniqueSharedComponentValuesCount(Archetype* archetype)
            {
                if (archetype->NumSharedComponents == 0)
                    return 0;

                var uniqueSharedComponentValueCount = 0;
                using (var map = new NativeHashMap<SharedComponentValuesKey, byte>(16, Allocator.Temp))
                {
                    for (var i = 0; i < archetype->Chunks.Count; i++)
                    {
                        var key = new SharedComponentValuesKey(archetype, i);
                        if (map.TryAdd(key, 1))
                            uniqueSharedComponentValueCount++;
                    }
                }

                return uniqueSharedComponentValueCount;
            }

            public bool Equals(ArchetypeMemoryData other)
            {
                return StableHash == other.StableHash;
            }

            [NotBurstCompatible]
            public override bool Equals(object obj)
            {
                return obj is ArchetypeMemoryData archetypeData ? Equals(archetypeData) : false;
            }

            public override int GetHashCode()
            {
                return StableHash.GetHashCode();
            }

            public static bool operator ==(ArchetypeMemoryData lhs, ArchetypeMemoryData rhs)
            {
                return lhs.Equals(rhs);
            }

            public static bool operator !=(ArchetypeMemoryData lhs, ArchetypeMemoryData rhs)
            {
                return !lhs.Equals(rhs);
            }
        }
    }
}
#endif
