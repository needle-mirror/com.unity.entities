using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace Unity.Entities
{
    /// <summary>
    /// Purpose of this class is to provide a consistent cache of BlobAsset object in order to avoid rebuilding them when it is not necessary
    /// </summary>
    /// <remarks>
    /// Right now the lifetime scope of this cache is bound to the LiveConversionDiffGenerator's one and it is scoped by SubScene.
    /// In other words the cache is created when we enter edit mode for a given SubScene and it is released when we close edit mode.
    /// And instance of this cache is exposed in `Unity.Entities.GameObjectConversionSettings` to allow users to query and avoid rebuilding assets.
    /// During conversion process the user must rely on the <see cref="BlobAssetComputationContext{TS,TB}"/> to associate the BlobAsset with their corresponding Authoring UnityObject and to determine which ones are to compute.
    /// Thread-safety: nothing is thread-safe, we assume this class is consumed through the main-thread only.
    /// Calling Dispose on an instance will reset the content and dispose all BlobAssetReference object stored.
    /// </remarks>
    public struct BlobAssetStore : IDisposable
    {
        NativeParallelHashMap<Hash128, BlobAssetReferenceData> m_BlobAssets;
        NativeParallelHashMap<Hash128, int>                    m_RefCounterPerBlobHash;
        NativeMultiHashMap<int, Hash128>                       m_HashByOwner;  //Context
        NativeList<int>                                        m_CacheStats;

        Allocator                                              m_Allocator;

        enum CacheStats
        {
            Hit = 0,
            Miss
        }

        /// <summary>
        /// Initializes and returns an instance of BlobAssetStore.
        /// </summary>
        /// <param name="capacity">The initial capacity of the internal native containers.</param>
        public BlobAssetStore(int capacity)
        {
            m_Allocator = Allocator.Persistent;
            m_BlobAssets = new NativeParallelHashMap<Hash128, BlobAssetReferenceData>(capacity, m_Allocator);
            m_RefCounterPerBlobHash = new NativeParallelHashMap<Hash128, int>(capacity, m_Allocator);
            m_HashByOwner = new NativeMultiHashMap<int, Hash128>(capacity, m_Allocator);
            m_CacheStats = new NativeList<int>(2, m_Allocator);
            m_CacheStats.Add(0);
            m_CacheStats.Add(0);
        }

        /// <summary>
        /// Checks if the BlobAssetStoreInternal has been created
        /// </summary>
        /// <returns>True if the BlobAssetStoreInternal has been created</returns>
        public bool IsCreated
        {
            get { return m_BlobAssets.IsCreated; }
        }

        /// <summary>
        /// Call this method to clear the whole content of the Cache
        /// </summary>
        /// <param name="disposeAllBlobAssetReference">If true all BlobAssetReference present in the cache will be dispose. If false they will remain present in memory</param>
        public void ResetCache(bool disposeAllBlobAssetReference)
        {
            if (disposeAllBlobAssetReference)
            {
                using (var blobDataArray = m_BlobAssets.GetValueArray(Allocator.Temp))
                {
                    for (int i = 0; i < blobDataArray.Length; i++)
                    {
                        blobDataArray[i].Dispose();
                    }
                }
            }

            m_BlobAssets.Clear();
            m_HashByOwner.Clear();
            m_RefCounterPerBlobHash.Clear();
        }

        /// <summary>
        /// Try to access to a BlobAssetReference from its key
        /// </summary>
        /// <param name="hash">The key associated with the BlobAssetReference when it was added to the cache</param>
        /// <param name="blobAssetReference">The corresponding BlobAssetReference or default if none was found</param>
        /// <typeparam name="T">The type of BlobAsset</typeparam>
        /// <returns>True if the BlobAsset was found and returned, false if it wasn't</returns>
        public bool TryGet<T>(Hash128 hash, out BlobAssetReference<T> blobAssetReference) where T : unmanaged
        {
            return TryGet(hash, out blobAssetReference, true);
        }

        /// <summary>
        /// Try to access to a BlobAssetReference from its key
        /// </summary>
        /// <param name="fullHash">The full key (object hash + type hash) associated with the BlobAssetReference when it was added to the cache</param>
        /// <param name="blobAssetReference">The corresponding BlobAssetReference or default if none was found</param>
        /// <typeparam name="T">The type of BlobAsset</typeparam>
        /// <returns>True if the BlobAsset was found and returned, false if it wasn't</returns>
        internal bool TryGetWithFullHash<T>(Hash128 fullHash, out BlobAssetReference<T> blobAssetReference) where T : unmanaged
        {
            return TryGetWithFullHash(fullHash, out blobAssetReference, true);
        }

        /// <summary>
        /// Try to access to a BlobAssetReference from its key
        /// </summary>
        /// <param name="hash">The key associated with the BlobAssetReference when it was added to the cache</param>
        /// <param name="blobAssetReference">The corresponding BlobAssetReference or default if none was found</param>
        /// <param name="updateRefCount">If the refCounter of this BlobAsset needs to be increased based on the result (temporary until refactor)</param>
        /// <typeparam name="T">The type of BlobAsset</typeparam>
        /// <returns>True if the BlobAsset was found and returned, false if it wasn't</returns>
        internal bool TryGet<T>(Hash128 hash, out BlobAssetReference<T> blobAssetReference, bool updateRefCount = true) where T : unmanaged
        {
            var typedHash = ComputeKeyAndTypeHash(hash, typeof(T));

            return TryGetWithFullHash(typedHash, out blobAssetReference, updateRefCount);
        }

        /// <summary>
        /// Try to access to a BlobAssetReference from its key
        /// </summary>
        /// <param name="hash">The key associated with the BlobAssetReference when it was added to the cache</param>
        /// <param name="blobAssetReference">The corresponding BlobAssetReference or default if none was found</param>
        /// <param name="typeHash">Hash calculated with ComputeTypeHash for the type of BlobAsset</param>
        /// <param name="updateRefCount">If the refCounter of this BlobAsset needs to be increased based on the result (temporary until refactor)</param>
        /// <typeparam name="T">The type of BlobAsset</typeparam>
        /// <returns>True if the BlobAsset was found and returned, false if it wasn't</returns>
        internal bool TryGet<T>(Hash128 hash, uint typeHash, out BlobAssetReference<T> blobAssetReference, bool updateRefCount = true) where T : unmanaged
        {
            var typedHash = ComputeKeyAndTypeHash(hash, typeHash);

            return TryGetWithFullHash(typedHash, out blobAssetReference, updateRefCount);
        }

        /// <summary>
        /// Try to access to a BlobAssetReference from its key
        /// </summary>
        /// <param name="hash">The key associated with the BlobAssetReference when it was added to the cache</param>
        /// <param name="blobAssetReference">The corresponding BlobAssetReference or default if none was found</param>
        /// <param name="updateRefCount">If the refCounter of this BlobAsset needs to be increased based on the result (temporary until refactor)</param>
        /// <typeparam name="T">The type of BlobAsset</typeparam>
        /// <returns>True if the BlobAsset was found and returned, false if it wasn't</returns>
        internal bool TryGetTest<T>(Hash128 hash, out BlobAssetReference<T> blobAssetReference, bool updateRefCount = true) where T : unmanaged
        {
            var result =  TryGet(hash, out blobAssetReference, updateRefCount);

            if (result)
            {
                ++m_CacheStats[(int)CacheStats.Hit];
            } else
            {
                ++m_CacheStats[(int)CacheStats.Miss];
            }

            return result;
        }

        /// <summary>
        /// Try to access to a BlobAssetReference from its key
        /// </summary>
        /// <param name="fullHash">The full key (object hash + type hash) associated with the BlobAssetReference when it was added to the cache</param>
        /// <param name="blobAssetReference">The corresponding BlobAssetReference or default if none was found</param>
        /// <param name="updateRefCount">If the refCounter of this BlobAsset needs to be increased based on the result (temporary until refactor)</param>
        /// <typeparam name="T">The type of BlobAsset</typeparam>
        /// <returns>True if the BlobAsset was found and returned, false if it wasn't</returns>
        internal bool TryGetWithFullHash<T>(Hash128 fullHash, out BlobAssetReference<T> blobAssetReference, bool updateRefCount = true) where T : unmanaged
        {
            if (m_BlobAssets.TryGetValue(fullHash, out var blobData))
            {
                blobAssetReference = BlobAssetReference<T>.Create(blobData);
                if(updateRefCount)
                    m_RefCounterPerBlobHash[fullHash]++;
                return true;
            }

            blobAssetReference = default;
            return false;
        }

        /// <summary>
        /// Number of times the cache was successfully accessed
        /// </summary>
        /// <remarks>
        /// Each TryGet returning a valid content will increment this counter
        /// </remarks>
        internal int CacheHit => m_CacheStats[(int)CacheStats.Hit];

        /// <summary>
        /// Number of times the cache failed to return a BlobAssetReference for the given key
        /// </summary>
        /// <remarks>
        /// Each TryGet returning false will increment this counter
        /// </remarks>
        internal int CacheMiss => m_CacheStats[(int)CacheStats.Miss];

        /// <summary>
        /// Check if the Store contains a BlobAsset of a given type and hash
        /// </summary>
        /// <param name="key">The hash associated with the BlobAsset</param>
        /// <typeparam name="T">The type of the BlobAsset</typeparam>
        /// <returns>True if the Store contains the BlobAsset or false if it doesn't</returns>
        public bool Contains<T>(Hash128 key)
        {
            var typedHash = ComputeKeyAndTypeHash(key, typeof(T));
            return m_BlobAssets.ContainsKey(typedHash);
        }

        /// <summary>
        /// Check if the Store contains a BlobAsset of a given type and hash
        /// </summary>
        /// <param name="key">The hash associated with the BlobAsset</param>
        /// <param name="typeHash">Hash calculated with ComputeTypeHash for the type of BlobAsset</param>
        /// <returns>True if the Store contains the BlobAsset or false if it doesn't</returns>
        internal bool Contains(Hash128 key, uint typeHash)
        {
            var typedHash = ComputeKeyAndTypeHash(key, typeHash);
            return m_BlobAssets.ContainsKey(typedHash);
        }

        /// <summary>
        /// Add a BlobAssetReference with a custom hash key
        /// </summary>
        /// <param name="blobAsset">The BlobAssetReference if found or default</param>
        /// <param name="customHash">The key to be associated with the BlobAssetReference</param>
        /// <typeparam name="T">The type of BlobAsset</typeparam>
        /// <returns>True if the BlobAssetReference was found, false if not found</returns>
        public bool TryAdd<T>(Hash128 customHash, ref BlobAssetReference<T> blobAsset) where T : unmanaged
        {
            return TryAdd<T>(customHash, ref blobAsset, true);
        }

        /// <summary>
        /// Add a BlobAssetReference with the default hash key based on the BlobAsset contents itself. If the contents of the generated blob asset is the same as a previously inserted blob asset,
        /// then the passed blobAsset will be disposed and the reference to the blob asset will be replaced with the previously added blob asset
        /// </summary>
        /// <param name="blobAsset">The blob asset that will be inserted or replaced</param>
        /// <param name="objectHash">The hash that is based on the content of the BlobAsset</param>
        /// <typeparam name="T">The type of BlobAsset.</typeparam>
        /// <returns>Returns true if the blob asset was added, returns false if the blob asset was disposed and replaced with the previous blob.</returns>
        unsafe public bool TryAdd<T>(ref BlobAssetReference<T> blobAsset, out Hash128 objectHash) where T : unmanaged
        {
            return TryAdd<T>(ref blobAsset, out objectHash, true);
        }

        /// <summary>
        /// Add a BlobAssetReference with the default hash key based on the BlobAsset contents itself. If the contents of the generated blob asset is the same as a previously inserted blob asset,
        /// then the passed blobAsset will be disposed and the reference to the blob asset will be replaced with the previously added blob asset
        /// </summary>
        /// <param name="blobAsset">The blob asset that will be inserted or replaced</param>
        /// <typeparam name="T">The type of BlobAsset.</typeparam>
        /// <returns>Returns true if the blob asset was added, returns false if the blob asset was disposed and replaced with the previous blob.</returns>
        unsafe public bool TryAdd<T>(ref BlobAssetReference<T> blobAsset) where T : unmanaged
        {
            return TryAdd<T>(ref blobAsset, out _, true);
        }

        /// <summary>
        /// Add a BlobAssetReference with a custom hash key
        /// </summary>
        /// <param name="blobAsset">The BlobAssetReference if found or default</param>
        /// <param name="customHash">The key to be associated with the BlobAssetReference</param>
        /// <param name="updateRefCount">If the refCounter of this BlobAsset needs to be increased based on the result (temporary until refactor)</param>
        /// <typeparam name="T">The type of BlobAsset</typeparam>
        /// <returns>true if the BlobAssetReference was found, false if not found</returns>
        internal bool TryAdd<T>(Hash128 customHash, ref BlobAssetReference<T> blobAsset, bool updateRefCount = true) where T : unmanaged
        {
            ValidateBlob(blobAsset.m_data);

            var fullHash = ComputeKeyAndTypeHash(customHash, typeof(T));

            return TryAddWithFullHash<T>(ref blobAsset, fullHash, updateRefCount);
        }

        /// <summary>
        /// Add a BlobAssetReference with a custom hash key
        /// </summary>
        /// <param name="blobAsset">The BlobAssetReference if found or default</param>
        /// <param name="customHash">The key to be associated with the BlobAssetReference</param>
        /// <param name="typeHash">Hash calculated with ComputeTypeHash for the type of BlobAsset</param>
        /// <param name="updateRefCount">If the refCounter of this BlobAsset needs to be increased based on the result (temporary until refactor)</param>
        /// <typeparam name="T">The type of BlobAsset</typeparam>
        /// <returns>true if the BlobAssetReference was found, false if not found</returns>
        internal bool TryAdd<T>(Hash128 customHash, uint typeHash, ref BlobAssetReference<T> blobAsset, bool updateRefCount = true) where T : unmanaged
        {
            ValidateBlob(blobAsset.m_data);

            var fullHash = ComputeKeyAndTypeHash(customHash, typeHash);

            return TryAddWithFullHash<T>(ref blobAsset, fullHash, updateRefCount);
        }

        /// <summary>
        /// Add a BlobAssetReference with the default hash key based on the BlobAsset contents itself. If the contents of the generated blob asset is the same as a previously inserted blob asset,
        /// then the passed blobAsset will be disposed and the reference to the blob asset will be replaced with the previously added blob asset
        /// </summary>
        /// <param name="blobAsset">The blob asset that will be inserted or replaced</param>
        /// <param name="objectHash">The hash that is based on the content of the BlobAsset</param>
        /// <param name="updateRefCount">If the refCounter of this BlobAsset needs to be increased based on the result (temporary until refactor)</param>
        /// <typeparam name="T">The type of BlobAsset.</typeparam>
        /// <returns>Returns true if the blob asset was added, returns false if the blob asset was disposed and replaced with the previous blob.</returns>
        unsafe internal bool TryAdd<T>(ref BlobAssetReference<T> blobAsset, out Hash128 objectHash, bool updateRefCount = true) where T : unmanaged
        {
            ValidateBlob(blobAsset.m_data);

            // This used to be a different method. This was changed to the same method as for the custom hash to avoid clashes in the genereated hashes
            Hash128 hash = default;
            UnsafeUtility.MemCpy(&hash, &blobAsset.m_data.Header->Hash, sizeof(ulong));
            objectHash = hash;
            var fullHash = ComputeKeyAndTypeHash(objectHash, typeof(T));

            return TryAddWithFullHash<T>(ref blobAsset, fullHash, updateRefCount);
        }

        /// <summary>
        /// Add a BlobAssetReference with the default hash key based on the BlobAsset contents itself. If the contents of the generated blob asset is the same as a previously inserted blob asset,
        /// then the passed blobAsset will be disposed and the reference to the blob asset will be replaced with the previously added blob asset
        /// </summary>
        /// <param name="blobAsset">The blob asset that will be inserted or replaced</param>
        /// <param name="typeHash">Hash calculated with ComputeTypeHash for the type of BlobAsset</param>
        /// <param name="updateRefCount">If the refCounter of this BlobAsset needs to be increased based on the result (temporary until refactor)</param>
        /// <typeparam name="T">The type of BlobAsset.</typeparam>
        /// <returns>Returns true if the blob asset was added, returns false if the blob asset was disposed and replaced with the previous blob.</returns>
        unsafe internal bool TryAdd<T>(ref BlobAssetReference<T> blobAsset, uint typeHash, bool updateRefCount = true) where T : unmanaged
        {
            ValidateBlob(blobAsset.m_data);

            Hash128 hash = default;
            UnsafeUtility.MemCpy(&hash, &blobAsset.m_data.Header->Hash, sizeof(ulong));
            hash.Value.w = typeHash;

            return TryAddWithFullHash<T>(ref blobAsset, hash, updateRefCount);
        }

        /// <summary>
        /// Add a BlobAssetReference with the default hash key based on the BlobAsset contents itself. If the contents of the generated blob asset is the same as a previously inserted blob asset,
        /// then the passed blobAsset will be disposed and the reference to the blob asset will be replaced with the previously added blob asset
        /// </summary>
        /// <param name="blobAssetReference">The BlobAssetReference if found or default</param>
        /// <param name="fullHash">The full key (object hash + type hash) associated with the BlobAssetReference when it was added to the cache</param>
        /// <param name="updateRefCount">If the refCounter of this BlobAsset needs to be increased based on the result (temporary until refactor)</param>
        /// <typeparam name="T">The type of BlobAsset.</typeparam>
        /// <returns>Returns true if the blob asset was added, returns false if the blob asset was disposed and replaced with the previous blob.</returns>
        private bool TryAddWithFullHash<T>(ref BlobAssetReference<T> blobAssetReference, Hash128 fullHash, bool updateRefCount = true) where T : unmanaged
        {
            if (m_BlobAssets.TryGetValue(fullHash, out var existingBlob))
            {
                var existingBlobReference = BlobAssetReference<T>.Create(existingBlob);
                if(existingBlobReference != blobAssetReference)
                    blobAssetReference.Dispose();
                blobAssetReference = existingBlobReference;
                if(updateRefCount)
                    m_RefCounterPerBlobHash[fullHash]++;
                return false;
            }
            m_BlobAssets.Add(fullHash, blobAssetReference.m_data);
            if(updateRefCount)
                m_RefCounterPerBlobHash.Add(fullHash, 1);
            return true;
        }


        /// <summary>
        /// Validate that the BlobAsset has not been disposed
        /// </summary>
        /// <param name="referenceData">The BlobAssetReferenceData to validate</param>
        /// <exception cref="ArgumentException"></exception>
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        unsafe void ValidateBlob(BlobAssetReferenceData referenceData)
        {
            referenceData.ValidateNotNull();
            if (referenceData.Header->Allocator != m_Allocator)
                throw new ArgumentException($"The Allocator for the blob asset must be {m_Allocator} but was {referenceData.Header->Allocator}");

        }


        /// <summary>
        /// Remove a BlobAssetReference from the store
        /// </summary>
        /// <param name="hash">The key associated with the BlobAssetReference</param>
        /// <param name="releaseBlobAsset">If true the BlobAsset data will be released</param>
        /// <typeparam name="T">The type of the BlobAsset</typeparam>
        /// <returns>True if the BLobAsset was removed from the store, false if it wasn't found</returns>
        public bool Remove<T>(Hash128 hash, bool releaseBlobAsset)
        {
            var fullHash = ComputeKeyAndTypeHash(hash, typeof(T));

            return RemoveWithFullHash(fullHash, releaseBlobAsset);
        }

        /// <summary>
        /// Remove a BlobAssetReference from the store
        /// </summary>
        /// <param name="hash">The key associated with the BlobAssetReference</param>
        /// <param name="typeHash">Hash calculated with ComputeTypeHash for the type of BlobAsset</param>
        /// <param name="releaseBlobAsset">If true the BlobAsset data will be released</param>
        /// <returns>True if the BLobAsset was removed from the store, false if it wasn't found</returns>
        internal bool Remove(Hash128 hash, uint typeHash, bool releaseBlobAsset)
        {
            var fullHash = ComputeKeyAndTypeHash(hash, typeHash);

            return RemoveWithFullHash(fullHash, releaseBlobAsset);
        }

        /// <summary>
        /// Remove a BlobAssetReference from the store
        /// </summary>
        /// <param name="fullHash">The full key (object hash + type hash) associated with the BlobAssetReference when it was added to the cache</param>
        /// <param name="releaseBlobAsset">If true the BlobAsset data will be released</param>
        /// <returns>True if the BLobAsset was removed from the store, false if it wasn't found</returns>
        internal bool RemoveWithFullHash(Hash128 fullHash, bool releaseBlobAsset)
        {
            if (!m_BlobAssets.TryGetValue(fullHash, out var blobData))
            {
                return false;
            }

            var newRefCount = --m_RefCounterPerBlobHash[fullHash];

            if (newRefCount != 0)
            {
                return false;
            }

            var res = m_BlobAssets.Remove(fullHash);

            if (releaseBlobAsset)
            {
                blobData.Dispose();
                res &= m_RefCounterPerBlobHash.Remove(fullHash);
            }

            return res;
        }


        /// <summary>
        /// Get the Reference Counter value of a given BlogAsset
        /// </summary>
        /// <param name="hash">The hash associated with the BLobAsset</param>
        /// <returns>The value of the reference counter, 0 if there is no BlobAsset for the given hash</returns>
        internal int GetBlobAssetRefCounter<T>(Hash128 hash)
        {
            return m_RefCounterPerBlobHash.TryGetValue(ComputeKeyAndTypeHash(hash, typeof(T)), out var counter) ? counter : 0;
        }

        /// <summary>
        /// Calling dispose will reset the cache content and release all the BlobAssetReference that were stored
        /// </summary>
        public void Dispose()
        {
            ResetCache(true);
            m_BlobAssets.Dispose();
            m_HashByOwner.Dispose();
            m_CacheStats.Dispose();
            m_RefCounterPerBlobHash.Dispose();
        }

        /// <summary>
        /// Function to calculate a hash value associated with a Type
        /// </summary>
        /// <param name="type">The BlobAsset type from where the hash is calculated.</param>
        /// <returns>Calculated hash value</returns>
        internal static uint ComputeTypeHash(Type type)
        {
            return (uint)type.GetHashCode();
        }

        /// <summary>
        /// Function to calculate a full hash value based on a BlobAsset hash and the BlobAsset type.
        /// </summary>
        /// <param name="key">The hash associated with the BlobAsset.</param>
        /// <param name="type">The BlobAsset type from where the hash is calculated.</param>
        /// <returns>Calculated hash value</returns>
        internal static Hash128 ComputeKeyAndTypeHash(Hash128 key, Type type)
        {
            return new Hash128(math.hashwide(new uint4x2 { c0 = key.Value, c1 = new uint4(ComputeTypeHash(type))}));
        }

        /// <summary>
        /// Calculates a full hash value based on a hash key and a hash associated with a Type.
        /// </summary>
        /// <param name="key">The hash associated with the BlobAsset.</param>
        /// <param name="typeHash">The hash associated with the BlobAsset type.</param>
        /// <returns>Returns the calculated hash value as a 128-bit hash value.</returns>
        static Hash128 ComputeKeyAndTypeHash(Hash128 key, uint typeHash)
        {
            return new Hash128(math.hashwide(new uint4x2 { c0 = key.Value, c1 = new uint4(typeHash)}));
        }

        /// <summary>
        /// Update the refcounting and the HashByOwner for the BlobAssets based on the <see cref="UnityEngine.Object"/>
        /// </summary>
        /// <param name="ownerId">The Unity ID of the <see cref="UnityEngine.Object"/> to update the BlobAssets for</param>
        /// <param name="newBlobHashes">The hashes of the current BlobAssets</param>
        /// <typeparam name="TB">The type of BlobAsset.</typeparam>
        internal void UpdateBlobAssetForUnityObject<TB>(int ownerId, NativeArray<Hash128> newBlobHashes) where TB : struct
        {
            var leftLength = newBlobHashes.Length;
            var toInc = new NativeArray<Hash128>(leftLength, Allocator.Temp);
            var toDec = new NativeArray<Hash128>(m_HashByOwner.CountValuesForKey(ownerId), Allocator.Temp);

            var curLeftIndex = 0;
            var curIncIndex = 0;
            var curDecIndex = 0;

            var leftRes = curLeftIndex < leftLength;
            var rightRes = m_HashByOwner.TryGetFirstValue(ownerId, out var rightHash, out var it);

            var maxHash = new Hash128(UInt32.MaxValue, UInt32.MaxValue, UInt32.MaxValue, UInt32.MaxValue);

            // We will parse newBlobHashes, considered the left part and the store hashes for this ownerId, considered the right part
            //  in order to build a list of BlobAssets to increment (the ones only present in left part) and the ones to decrement
            //  (only present in the right part). If a hash is present on both side, we do not change its RefCounter
            do
            {
                var leftHash = leftRes ? newBlobHashes[curLeftIndex] : maxHash;
                rightHash = rightRes ? rightHash : maxHash;

                // Both side equal? We are synchronized, step next for both sides
                if (rightHash == leftHash)
                {
                    leftRes = ++curLeftIndex < leftLength;
                    rightRes = m_HashByOwner.TryGetNextValue(out rightHash, ref it);
                    continue;
                }

                // More items on the left, add them to the "toAdd" list
                if (leftHash < rightHash)
                {
                    do
                    {
                        // Get left hash
                        leftHash = newBlobHashes[curLeftIndex++];

                        // Add to "toInc"
                        toInc[curIncIndex++] = leftHash;

                        // Check if there's more left item
                        leftRes = curLeftIndex < leftLength;
                    }
                    while (leftRes && (leftHash < rightHash));
                }
                // More items on the right, add them to the "toRemove" list
                else
                {
                    do
                    {
                        // Add to "toDec"
                        toDec[curDecIndex++] = rightHash;

                        // Get next right item
                        rightRes = m_HashByOwner.TryGetNextValue(out rightHash, ref it);
                    }
                    while (rightRes && leftHash > rightHash);
                }
            }
            while (leftRes || rightRes);

            // Increment each hash in "toInc" if they exist, add them to the RefCounter hash if they are new
            for (int i = 0; i < curIncIndex; i++)
            {
                var hash = ComputeKeyAndTypeHash(toInc[i], typeof(TB));
                if (m_RefCounterPerBlobHash.TryGetValue(hash, out var counter))
                {
                    m_RefCounterPerBlobHash[hash] = counter + 1;
                }
                else
                {
                    m_RefCounterPerBlobHash.Add(hash, 1);
                }
            }

            // Decrement each hash in "toDec", remove the BlobAsset if it reaches 0
            for (int i = 0; i < curDecIndex; i++)
            {
                // Decrement the hash of the previously assigned Blob Asset
                Remove<TB>(toDec[i], true);
            }

            // Clear the former list of BlobAsset hashes and replace by the new one
            m_HashByOwner.Remove(ownerId);

            for (int i = 0; i < leftLength; i++)
            {
                m_HashByOwner.Add(ownerId, newBlobHashes[i]);
            }
        }

        /// <summary>
        /// Gets the BlobAssets associated with a <see cref="UnityEngine.Object"/>.
        /// </summary>
        /// <param name="gameObject">The <see cref="UnityEngine.Object"/> to get the BlobAssets for</param>
        /// <param name="allocator">The allocator used for the NativeArray result</param>
        /// <param name="result">The BlobAsset hashes associated with this <see cref="UnityEngine.Object"/></param>
        /// <returns>True if the BlobAssets were found and returned, false if the <see cref="UnityEngine.Object"/> was not found</returns>
        internal bool GetBlobAssetsOfGameObject(GameObject gameObject, Allocator allocator, out NativeArray<Hash128> result)
        {
            return GetBlobAssetsOfUnityObject(gameObject, allocator, out result);
        }

        /// <summary>
        /// Gets the BlobAssets associated with a <see cref="UnityEngine.Object"/>.
        /// </summary>
        /// <param name="unityObject">The Unity ID of the <see cref="UnityEngine.Object"/> to get the BlobAssets for</param>
        /// <param name="allocator">The allocator used for the NativeArray result</param>
        /// <param name="result">The BlobAsset hashes associated with this <see cref="UnityEngine.Object"/></param>
        /// <returns>True if the BlobAssets were found and returned, false if the <see cref="UnityEngine.Object"/> was not found</returns>
        internal bool GetBlobAssetsOfUnityObject(UnityObject unityObject, Allocator allocator, out NativeArray<Hash128> result)
        {
            var key = unityObject.GetInstanceID();
            if (!m_HashByOwner.ContainsKey(key))
            {
                result = default;
                return false;
            }

            var count = m_HashByOwner.CountValuesForKey(key);
            result = new NativeArray<Hash128>(count, allocator);

            var index = 0;
            if (m_HashByOwner.TryGetFirstValue(key, out var hash, out var it))
            {
                result[index++] = hash;

                while (m_HashByOwner.TryGetNextValue(out hash, ref it))
                {
                    result[index++] = hash;
                }
            }

            return true;
        }
    }
}
