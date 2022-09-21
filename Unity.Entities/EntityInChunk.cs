using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Entities
{
    /// <summary>
    /// Represents a single entity within a chunk. Mainly used internally to sort lists of entities into chunk order.
    /// </summary>
    public unsafe struct EntityInChunk : IComparable<EntityInChunk>, IEquatable<EntityInChunk>
    {
        internal Chunk* Chunk;
        internal int IndexInChunk;

        /// <summary>
        /// Compares two <see cref="EntityInChunk"/> objects to determine their relative ordering
        /// </summary>
        /// <param name="other">The other instance to compare.</param>
        /// <returns>-1 if this entity should be ordered earlier than <paramref name="other"/>. 1 if this entity should
        /// be ordered later than <paramref name="other"/>. 0 if the two entities are equivalent.</returns>
        public int CompareTo(EntityInChunk other)
        {
            ulong lhs = (ulong)Chunk;
            ulong rhs = (ulong)other.Chunk;
            int chunkCompare = lhs < rhs ? -1 : 1;
            int indexCompare = IndexInChunk - other.IndexInChunk;
            return (lhs != rhs) ? chunkCompare : indexCompare;
        }

        /// <summary>
        /// Compares two <see cref="EntityInChunk"/> instances for equality/
        /// </summary>
        /// <param name="other">The other instance to compare.</param>
        /// <returns>True if the two instances refer to the same entity in the same chunk.</returns>
        public bool Equals(EntityInChunk other)
        {
            return CompareTo(other) == 0;
        }
    }
}
