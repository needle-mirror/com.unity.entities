using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.CompilerServices;
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
        /// The first entity in the chunk which should not be included in the current hot-loop iteration.
        /// If enabled-bits are in use, this one index past the end of the current entity range.
        /// If not, it will happen to match the number of entities in the current chunk, but this is a coincidence.
        /// </summary>
        int         _EndIndexInChunk;

        // enable bit warm state
        int                _ChunkIndex; // NOTE: _ChunkIndex is cold state, but we want v128 to be 16 byte aligned and there is no reason to waste the space.
        byte               _QueryHasFilterOrEnableable;
        byte               _UseEnableBitsForChunk;
        v128               _EnableBitMask;
        UnsafeChunkCache      _CacheIfFilteringOrEnableable;
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
            _EndIndexInChunk = 0;

            _UseEnableBitsForChunk = 0;
            _EnableBitMask = default;

            var impl = query._GetImpl();

            _QueryHasFilterOrEnableable = impl->_Filter.RequiresMatchesFilter || impl->_QueryData->HasEnableableComponents == 1 ? (byte)1 : (byte)0;
            if (_QueryHasFilterOrEnableable == 1)
            {
                _CacheIfFilteringOrEnableable =
                    new UnsafeChunkCache(impl->_Filter, impl->_QueryData->HasEnableableComponents != 0, impl->_QueryData->GetMatchingChunkCache(), impl->_QueryData->MatchingArchetypes.Ptr);
                _CachedChunkList = default;
                _CachedChunkListLength = -1;

                impl->SyncFilterTypes();
            }
            else
            {
                _CacheIfFilteringOrEnableable = default;
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
        /// Attempt to advance to the next entity in the current chunk, updating <see cref="IndexInChunk"/> and <see cref="_EndIndexInChunk"/>.
        /// </summary>
        /// <returns>True if another entity in the current chunk was found, or false if the end of the chunk was reached
        /// (in which case, <see cref="MoveNextColdLoop"/> should be called).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNextHotLoop()
        {
            // This is our hot inner loop, we expect that in the case we want to be fastest,
            // we expect that there are a bunch of entities in one batch to be processed.
            IndexInChunk++;

            if (Unity.Burst.CompilerServices.Hint.Likely(IndexInChunk < _EndIndexInChunk))
                return true;

            // When using enable bits, we try to get another chunk range.
            // This is our warm path, we want it to be reasonably fast,
            // but GetNextRange is marked to not be inlined in order to avoid bloating the code size in foreach loops
            // TODO(DOTS-8131): GetNextRange() has a tricky interface.
            // - beginIndex's input value is ignored; its output on success is the start of the next range.
            // - endIndex's input value is the end of the previous range (or zero on the first call). Its output is the end of the next range.
            return _UseEnableBitsForChunk != 0 && EnabledBitUtility.GetNextRange(ref _EnableBitMask, ref IndexInChunk, ref _EndIndexInChunk);
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
        public bool MoveNextEntityRange(out bool movedToNewChunk, out ArchetypeChunk chunk, out int entityStartIndex, out int entityEndIndex)
        {
            entityStartIndex = -1;

            if (_QueryHasFilterOrEnableable == 0)
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
                    _EndIndexInChunk = 0;
                    entityStartIndex = -1;
                    entityEndIndex = -1;
                    return false;
                }

                chunk = new ArchetypeChunk(_CachedChunkList.Ptr[_ChunkIndex], _CachedChunkList.EntityComponentStore);
                movedToNewChunk = true;
                entityStartIndex = 0;
                entityEndIndex = chunk.Count;
                _EndIndexInChunk = 0;
                return true;
            }

            // If we are checking for enabledness *and* we haven't finished processing the current chunk,
            // then try to find the next range of contiguous bits in the current chunk
            if (_UseEnableBitsForChunk != 0 && EnabledBitUtility.GetNextRange(ref _EnableBitMask, ref entityStartIndex, ref _EndIndexInChunk))
            {
                chunk = default;
                movedToNewChunk = false;
                entityEndIndex = _EndIndexInChunk;
                return true;
            }

            // We have finished processing the current chunk. Are there more we need to process?
            _EndIndexInChunk = 0;
            bool hasChunksLeft = _CacheIfFilteringOrEnableable.MoveNextChunk(ref _ChunkIndex, out chunk, out int chunkEntityCount, out _UseEnableBitsForChunk, ref _EnableBitMask);

            // If there are no more chunks we need to process
            if (Unity.Burst.CompilerServices.Hint.Unlikely(!hasChunksLeft))
            {
                movedToNewChunk = false;
                entityStartIndex = -1;
                entityEndIndex = -1;
                return false;
            }

            // If the next chunk does not require us to check for enabledness
            if (Unity.Burst.CompilerServices.Hint.Likely(_UseEnableBitsForChunk == 0))
            {
                movedToNewChunk = true;
                entityStartIndex = 0;
                entityEndIndex = chunkEntityCount;
                return true;
            }

            // If the next chunk requires us to check for enabledness, and we can find a new range of contiguous bits
            movedToNewChunk = Unity.Burst.CompilerServices.Hint.Likely(EnabledBitUtility.GetNextRange(ref _EnableBitMask, ref entityStartIndex, ref _EndIndexInChunk));
            entityEndIndex = _EndIndexInChunk;
            return movedToNewChunk;
        }

        // TODO(DOTS-7398)
        /// <summary>
        /// Attempt to advance to the first entity of the next chunk that matches the query, updating <see cref="IndexInChunk"/>
        /// and <see cref="_EndIndexInChunk"/>.
        /// </summary>
        /// <param name="chunk">If successful, the new chunk's metadata is stored here.</param>
        /// <returns>True if a new non-empty matching chunk was found (in which case iteration can continue immediately),
        /// or false if the end of the matching chunk list was reached (in which case iteration should terminate).</returns>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public bool MoveNextColdLoop(out ArchetypeChunk chunk)
        {
            CheckDisposed();
            if (_QueryHasFilterOrEnableable == 0)
            {
                _ChunkIndex++;

                if (Unity.Burst.CompilerServices.Hint.Unlikely(_ChunkIndex >= _CachedChunkList.Length))
                {
                    chunk = default;
                    return false;
                }

                chunk = new ArchetypeChunk(_CachedChunkList.Ptr[_ChunkIndex], _CachedChunkList.EntityComponentStore);
                IndexInChunk = 0;
                _EndIndexInChunk = chunk.Count;
                return true;
            }

            bool hasChunksLeft = _CacheIfFilteringOrEnableable.MoveNextChunk(ref _ChunkIndex, out chunk, out int chunkEntityCount, out _UseEnableBitsForChunk, ref _EnableBitMask);

            // Should we setup indices so it's clear we are done???
            if (Unity.Burst.CompilerServices.Hint.Unlikely(!hasChunksLeft))
                return false;

            if (Unity.Burst.CompilerServices.Hint.Unlikely(_UseEnableBitsForChunk != 0))
            {
                _EndIndexInChunk = 0;
                EnabledBitUtility.GetNextRange(ref _EnableBitMask, ref IndexInChunk, ref _EndIndexInChunk);
            }
            else
            {
                IndexInChunk = 0;
                _EndIndexInChunk = chunkEntityCount;
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
