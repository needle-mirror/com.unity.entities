using System;
using System.Diagnostics;
using Unity.Collections;

namespace Unity.Entities
{
    //@TODO: Use field offset / union here... There seems to be an issue in mono preventing it...
    unsafe struct EntityQueryFilter
    {
        public struct SharedComponentData
        {
            public const int Capacity = 2;

            public int Count;
            public fixed int IndexInEntityQuery[Capacity];
            public fixed int SharedComponentIndex[Capacity];
        }

        // Saves the index of ComponentTypes in this group that have changed.
        public struct ChangedFilter
        {
            public const int Capacity = 2;

            public int Count;
            public fixed int IndexInEntityQuery[Capacity];
        }

        public uint RequiredChangeVersion;

        public SharedComponentData Shared;
        public ChangedFilter Changed;

        public bool RequiresMatchesFilter
        {
            get { return Shared.Count != 0 || Changed.Count != 0 || _UseOrderFiltering != 0; }
        }

        private uint _UseOrderFiltering;
        public bool UseOrderFiltering
        {
            get { return _UseOrderFiltering != 0; }
            internal set { _UseOrderFiltering = value ? 1u : 0u; }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertValid()
        {
            if (Shared.Count < 0 || Shared.Count > SharedComponentData.Capacity)
                throw new ArgumentOutOfRangeException($"Shared.Count {Shared.Count} is out of range [0..{SharedComponentData.Capacity}]");
            if (Changed.Count < 0 || Changed.Count > ChangedFilter.Capacity)
                throw new ArgumentOutOfRangeException($"Changed.Count {Changed.Count} is out of range [0..{ChangedFilter.Capacity}]");
        }
    }

    /// <summary>
    /// Can be passed into IJobChunk.RunWithoutJobs to iterate over an entity query without running any jobs.
    /// </summary>
    public unsafe struct ArchetypeChunkIterator
    {
        internal readonly UnsafeMatchingArchetypePtrList m_MatchingArchetypeList;
        internal readonly ComponentDependencyManager* m_SafetyManager;
        int m_CurrentArchetypeIndex;
        int m_CurrentChunkInArchetypeIndex;
        int m_CurrentChunkFirstEntityIndexInQuery;
        Chunk* m_PreviousMatchingChunk;
        internal EntityQueryFilter m_Filter;
        internal readonly uint m_GlobalSystemVersion;

        ArchetypeChunk* m_BatchScratchMemory;

        int m_CurrentBatchIndexInChunk;
        int m_CurrentChunkBatchTotal;

        internal MatchingArchetype* CurrentMatchingArchetype
        {
            get
            {
                CheckOutOfBoundsCurrentMatchingArchetype();
                return m_MatchingArchetypeList.Ptr[m_CurrentArchetypeIndex];
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private void CheckOutOfBoundsCurrentMatchingArchetype()
        {
            if (m_CurrentArchetypeIndex < 0 || m_CurrentArchetypeIndex >= m_MatchingArchetypeList.Length)
                throw new InvalidOperationException("Tried to get an out of bounds Current matching archetype of an ArchetypeChunkIterator. Try calling Reset() and MoveNext().");
        }

        internal Archetype* CurrentArchetype
        {
            get
            {
                CheckOutOfBoundsCurrentMatchingArchetype();
                return CurrentMatchingArchetype->Archetype;
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private void CheckOutOfBoundsCurrentChunk()
        {
            if (m_CurrentChunkInArchetypeIndex >= CurrentArchetype->Chunks.Count)
                throw new InvalidOperationException("Tried to get an out of bounds Current chunk of an ArchetypeChunkIterator. Try calling Reset() and MoveNext().");
        }

        internal Chunk* CurrentChunk
        {
            get
            {
                CheckOutOfBoundsCurrentChunk();
                return CurrentArchetype->Chunks[m_CurrentChunkInArchetypeIndex];
            }
        }

        internal ArchetypeChunk CurrentArchetypeChunk
        {
            get
            {
                if (m_CurrentBatchIndexInChunk >= 0)
                {
                    return m_BatchScratchMemory[m_CurrentBatchIndexInChunk];
                }

                return new ArchetypeChunk
                {
                    m_Chunk = CurrentChunk,
                    m_EntityComponentStore = m_MatchingArchetypeList.entityComponentStore
                };
            }
        }

        internal ArchetypeChunkIterator(
            UnsafeMatchingArchetypePtrList match,
            ComponentDependencyManager* safetyManager,
            uint globalSystemVersion,
            ref EntityQueryFilter filter,
            Allocator batchScratchMemoryAllocator)
        {
            m_MatchingArchetypeList = match;
            m_CurrentArchetypeIndex = 0;
            m_CurrentChunkInArchetypeIndex = -1;
            m_CurrentChunkFirstEntityIndexInQuery = 0;
            m_PreviousMatchingChunk = null;
            m_Filter = filter;
            m_GlobalSystemVersion = globalSystemVersion;
            m_SafetyManager = safetyManager;

            m_BatchScratchMemory = (ArchetypeChunk*)Memory.Unmanaged.Allocate(sizeof(ArchetypeChunk) * 1024, 8, batchScratchMemoryAllocator);;
            m_CurrentBatchIndexInChunk = -1;
            m_CurrentChunkBatchTotal = 0;
        }

        /// <summary>
        /// Moves the iterator to the next chunk, returning false if at the end of the ArchetypeChunk list.
        /// </summary>
        /// <remarks>The initial call to MoveNext sets the iterator to the first chunk.</remarks>
        internal bool MoveNext()
        {
            while (true)
            {
                // if we've reached the end of the archetype list we're done
                if (m_CurrentArchetypeIndex >= m_MatchingArchetypeList.Length)
                    return false;

                // if we are dealing with a batched chunk, simply move to the next batch
                if (m_CurrentBatchIndexInChunk >= 0 && ++m_CurrentBatchIndexInChunk < m_CurrentChunkBatchTotal)
                    return true;

                // increment chunk index
                m_CurrentChunkInArchetypeIndex++;
                m_CurrentBatchIndexInChunk = -1;
                m_CurrentChunkBatchTotal = 0;

                // if we've reached the end of the chunk list for this archetype...
                if (m_CurrentChunkInArchetypeIndex >= CurrentArchetype->Chunks.Count)
                {
                    // move to the next archetype
                    m_CurrentArchetypeIndex++;
                    m_CurrentChunkInArchetypeIndex = -1;
                    continue;
                }

                // if the current chunk does not match the filter, move on to the next chunk
                if (!CurrentMatchingArchetype->ChunkMatchesFilter(m_CurrentChunkInArchetypeIndex, ref m_Filter))
                    continue;

                var chunkRequiresBatching = ChunkIterationUtility.DoesChunkRequireBatching(
                        CurrentChunk,
                        CurrentMatchingArchetype,
                        out var skipChunk);

                // if enabled bits tell us to skip this chunk, recurse
                if (skipChunk)
                    continue;

                // if we need to batch the current chunk, perform the batching now and cache the result
                if (chunkRequiresBatching)
                {
                    ChunkIterationUtility.FindBatchesForChunk(
                        CurrentChunk,
                        CurrentMatchingArchetype,
                        m_MatchingArchetypeList.entityComponentStore,
                        m_BatchScratchMemory,
                        out m_CurrentChunkBatchTotal);
                    if (m_CurrentChunkBatchTotal == 0)
                        continue; // no batches in the current chunk; move on to the next one
                    m_CurrentBatchIndexInChunk = 0;
                }

                // Aggregate the entity index
                if (m_PreviousMatchingChunk != null)
                    m_CurrentChunkFirstEntityIndexInQuery += m_PreviousMatchingChunk->Count;

                // note: Previous matching chunk is set while its actually the current chunk to support entity index aggregation
                m_PreviousMatchingChunk = CurrentChunk;

                return true;
            }
        }

        /// <summary>
        /// Sets the iterator to its initial position, which is before the first element in the collection.
        /// </summary>
        internal void Reset()
        {
            m_CurrentArchetypeIndex = 0;
            m_CurrentChunkInArchetypeIndex = -1;
            m_CurrentChunkFirstEntityIndexInQuery = 0;
            m_PreviousMatchingChunk = null;
        }

        /// <summary>
        /// The index of the first entity of the current chunk, as if all entities accessed by this iterator were
        /// in a singular array.
        /// </summary>
        internal int CurrentChunkFirstEntityIndex
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                var dummy = CurrentChunk; // this line throws an exception if we're outside the valid range of chunks in the iterator
#endif
                return m_CurrentChunkFirstEntityIndexInQuery;
            }
        }

        internal void* GetCurrentChunkComponentDataPtr(bool isWriting, int indexInEntityQuery)
        {
            int indexInArchetype = CurrentMatchingArchetype->IndexInArchetype[indexInEntityQuery];
            return ChunkIterationUtility.GetChunkComponentDataPtr(CurrentChunk, isWriting, indexInArchetype, m_GlobalSystemVersion);
        }

        internal object GetManagedObject(ManagedComponentStore managedComponentStore, int typeIndexInQuery, int entityInChunkIndex)
        {
            var indexInArchetype = CurrentMatchingArchetype->IndexInArchetype[typeIndexInQuery];
            var managedComponentArray = (int*)ChunkDataUtility.GetComponentDataRW(CurrentChunk, 0, indexInArchetype, CurrentArchetype->EntityComponentStore->GlobalSystemVersion);
            return managedComponentStore.GetManagedComponent(managedComponentArray[entityInChunkIndex]);
        }
    }
}
