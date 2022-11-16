using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
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
        byte               _UseEnableBits;
        v128               _EnableBitMask;

        // chunk level iteration cold state
        UnsafeChunkCache   _Cache;
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
        [NoAlias]
        ComponentDependencyManager* _DependencyManager;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle _EntitySafetyHandle;
        AtomicSafetyHandle          _QuerySafetyHandle;

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

            // Complete any running jobs that would affect which chunks/entities match the query.
            // This sync may not be strictly necessary, if the caller doesn't care about filtering the query results.
            // But if they DO care, and they forget this sync, they'll have an undetected race condition. So, let's play it safe.
            query._GetImpl()->SyncFilterTypes();

            _Cache = query.GetCache(out var access);
            _ChunkIndex = -1;
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            _DependencyManager = access->_Access->DependencyManager;
            _DependencyManager->ForEachStructuralChange.BeginIsInForEach(access);
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
            if (_UseEnableBits != 0)
            {
                if (EnabledBitUtility.GetNextRange(ref _EnableBitMask, ref IndexInChunk, ref EntityCount))
                    return true;
            }

            return false;
        }

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
            var hasChunksLeft = _Cache.MoveNextChunk(ref _ChunkIndex, out chunk, out int chunkEntityCount, out _UseEnableBits, ref _EnableBitMask);

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
