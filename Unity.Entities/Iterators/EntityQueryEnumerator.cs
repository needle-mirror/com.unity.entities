using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    /// <summary>
    /// This exists only for use by code-gen. It is the backend implementation used for enumerating all entities matching a query.
    /// Used to code-gen enumerators for aspects.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    [NoAlias]
    public unsafe struct EntityQueryEnumerator : IDisposable
    {
        // hot state

        /// <summary>
        /// The index of the current entity within its chunk
        /// </summary>
        public int         IndexInChunk;
        /// <summary>
        /// The number of entities in the current chunk
        /// </summary>
        public int         EntityCount;

        // enable bit warm state
        int                _ChunkIndex; // NOTE: _ChunkIndex is cold state, but we want v128 to be 16 byte aligned and there is no reason to waste the space.
        byte               _FilteringOrBatching;
        byte               _UseEnableBits;
        v128               _EnableBitMask;
        UnsafeChunkCache      _CacheIfFilteringOrBatching;
        UnsafeCachedChunkList _CachedChunkList;
        private int _CachedChunkListLength;

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
        [NoAlias]
        ComponentDependencyManager* _DependencyManager;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle _EntitySafetyHandle;
        private AtomicSafetyHandle _QuerySafetyHandle;
#endif
#endif

        /// <summary>
        /// Construct an enumerator instance for a query
        /// </summary>
        /// <remarks>
        /// This method includes a sync point on any jobs writing to the query's "filter types"; see
        /// <see cref="EntityQueryImpl.SyncFilterTypes"/> for details.
        /// </remarks>
        /// <param name="query">The query whose matching entities will be enumerated</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public EntityQueryEnumerator(EntityQuery query)
        {
            IndexInChunk = -1;
            EntityCount = -1;

            _UseEnableBits = 0;
            _EnableBitMask = default;

            var impl = query._GetImpl();

            _FilteringOrBatching = impl->_Filter.RequiresMatchesFilter || impl->_QueryData->DoesQueryRequireBatching == 1 ? (byte)1 : (byte)0;
            if (_FilteringOrBatching == 1)
            {
                _CacheIfFilteringOrBatching =
                    new UnsafeChunkCache(impl->_Filter, impl->_QueryData->DoesQueryRequireBatching != 0, impl->_QueryData->GetMatchingChunkCache(), impl->_QueryData->MatchingArchetypes.Ptr);
                _CachedChunkList = default;
                _CachedChunkListLength = -1;

                impl->SyncFilterTypes();
            }
            else
            {
                _CacheIfFilteringOrBatching = default;
                _CachedChunkList = impl->_QueryData->GetMatchingChunkCache();
                _CachedChunkListLength = _CachedChunkList.Length;
            }

            _ChunkIndex = -1;
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            _DependencyManager = impl->_Access->DependencyManager;
            _DependencyManager->ForEachStructuralChange.BeginIsInForEach(impl);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            _EntitySafetyHandle = _DependencyManager->Safety.GetEntityManagerSafetyHandle();
            _QuerySafetyHandle = query.__safety;
#endif
#endif
        }

        /// <summary>
        /// Clean up this enumerator instance
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(_QuerySafetyHandle);
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            _DependencyManager->ForEachStructuralChange.EndIsInForEach();
#endif
        }

        // TODO(DOTS-7398)
        /// <summary>
        /// Attempt to advance to the next entity in the current chunk, updating <see cref="IndexInChunk"/>.
        /// </summary>
        /// <returns>True if another entity in the current chunk was found, or false if the end of the chunk was reached
        /// (in which case, <see cref="MoveNextColdLoop"/> should be called).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNextHotLoop()
        {
            // This is our hot inner loop, we expect that in the case we want to be fastest,
            // we expect that there are a bunch of entities in one batch to be processed.
            IndexInChunk++;

            if (Unity.Burst.CompilerServices.Hint.Likely(IndexInChunk < EntityCount))
                return true;

            // When using enable bits, we try to get another chunk range.
            // This is our warm path, we want it to be reasonably fast,
            // but GetNextRange is marked to not be inlined in order to avoid bloating the code size in foreach loops
            return _UseEnableBits != 0 && EnabledBitUtility.GetNextRange(ref _EnableBitMask, ref IndexInChunk, ref EntityCount);
        }

        /// <summary>
        /// This method retrieves the next range of entities that fulfill the criteria of the `EntityQuery` passed to the constructor.
        /// If the current chunk has been exhausted, the search is continued in the next chunk (if any).
        /// </summary>
        /// <param name="movedToNewChunk">True if the next range of entities are stored in a different chunk than the previous range. In this case, the new chunk is stored in <paramref name="chunk"/>.</param>
        /// <param name="chunk">The next chunk instance if <paramref name="movedToNewChunk"/> is true; `default(ArchetypeChunk)` is returned otherwise.</param>
        /// <param name="entityStartIndex">The index within the chunk of the first entity in the next range of entities.</param>
        /// <param name="entityCount">The number of entities in the next range.</param>
        /// <returns>True if a new range of entities is found; false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNextEntityRange(out bool movedToNewChunk, out ArchetypeChunk chunk, out int entityStartIndex, out int entityCount)
        {
            entityStartIndex = -1;
            entityCount = -1;

            if (_FilteringOrBatching == 0)
            {
#if UNITY_BURST_EXPERIMENTAL_PREFETCH_INTRINSIC
                if (Burst.CompilerServices.Hint.Likely(_ChunkIndex + 1 < _CachedChunkListLength))
                    Common.Prefetch(_CachedChunkList.Ptr[_ChunkIndex + 1], Common.ReadWrite.Read);
#endif

                _ChunkIndex++;

                // If we have iterated through all the chunks
                if (Unity.Burst.CompilerServices.Hint.Unlikely(_ChunkIndex >= _CachedChunkListLength))
                {
                    chunk = default;
                    movedToNewChunk = false;
                    return false;
                }

                chunk = new ArchetypeChunk(_CachedChunkList.Ptr[_ChunkIndex], _CachedChunkList.EntityComponentStore);
                movedToNewChunk = true;
                entityStartIndex = 0;
                entityCount = chunk.Count;
                return true;
            }

            int entityEndIndex = 0;

            // If we are checking for enabledness *and* we haven't finished processing the current chunk,
            // then try to find the next range of contiguous bits in the current chunk
            if (_UseEnableBits != 0 && EnabledBitUtility.GetNextRange(ref _EnableBitMask, ref entityStartIndex, ref entityEndIndex))
            {
                chunk = default;
                movedToNewChunk = false;
                entityCount = entityEndIndex - entityStartIndex;
                return true;
            }

            // We have finished processing the current chunk. Are there more we need to process?
            bool hasChunksLeft = _CacheIfFilteringOrBatching.MoveNextChunk(ref _ChunkIndex, out chunk, out entityCount, out _UseEnableBits, ref _EnableBitMask);

            // If there are no more chunks we need to process
            if (Unity.Burst.CompilerServices.Hint.Unlikely(!hasChunksLeft))
            {
                movedToNewChunk = false;
                return false;
            }

            // If the next chunk does not require us to check for enabledness
            if (Unity.Burst.CompilerServices.Hint.Likely(_UseEnableBits == 0))
            {
                movedToNewChunk = true;
                entityStartIndex = 0;
                return true;
            }

            // If the next chunk requires us to check for enabledness, and we can find a new range of contiguous bits
            movedToNewChunk = Unity.Burst.CompilerServices.Hint.Likely(EnabledBitUtility.GetNextRange(ref _EnableBitMask, ref entityStartIndex, ref entityEndIndex));
            entityCount = entityEndIndex - entityStartIndex;
            return movedToNewChunk;
        }

        // TODO(DOTS-7398)
        /// <summary>
        /// Attempt to advance to the first entity of the next chunk that matches the query, updating <see cref="IndexInChunk"/>
        /// and <see cref="EntityCount"/>.
        /// </summary>
        /// <param name="chunk">If successful, the new chunk's metadata is stored here.</param>
        /// <returns>True if a new non-empty matching chunk was found (in which case iteration can continue immediately),
        /// or false if the end of the matching chunk list was reached (in which case iteration should terminate).</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool MoveNextColdLoop(out ArchetypeChunk chunk)
        {
            CheckDisposed();
            if (_FilteringOrBatching == 0)
            {
                _ChunkIndex++;

                if (Unity.Burst.CompilerServices.Hint.Unlikely(_ChunkIndex >= _CachedChunkList.Length))
                {
                    chunk = default;
                    return false;
                }

                chunk = new ArchetypeChunk(_CachedChunkList.Ptr[_ChunkIndex], _CachedChunkList.EntityComponentStore);
                IndexInChunk = 0;
                EntityCount = chunk.Count;
                return true;
            }

            bool hasChunksLeft = _CacheIfFilteringOrBatching.MoveNextChunk(ref _ChunkIndex, out chunk, out int chunkEntityCount, out _UseEnableBits, ref _EnableBitMask);

            // Should we setup indices so it's clear we are done???
            if (Unity.Burst.CompilerServices.Hint.Unlikely(!hasChunksLeft))
                return false;

            if (Unity.Burst.CompilerServices.Hint.Unlikely(_UseEnableBits != 0))
            {
                EntityCount = IndexInChunk = 0;
                EnabledBitUtility.GetNextRange(ref _EnableBitMask, ref IndexInChunk, ref EntityCount);
            }
            else
            {
                IndexInChunk = 0;
                EntityCount = chunkEntityCount;
            }

            return true;
        }

        /// <summary>
        /// Debug method to ensure that the enumerator is in a valid state
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the enumerator has been disposed, or has outlived its validity.</exception>
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void CheckDisposed()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(_EntitySafetyHandle);
            AtomicSafetyHandle.CheckWriteAndThrow(_QuerySafetyHandle);
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (_DependencyManager->ForEachStructuralChange.Depth == 0)
                throw new ObjectDisposedException("The EntityQueryEnumerator has been disposed or has been incorrectly kept alive across System.Update calls.");
#endif
        }
    }
}
