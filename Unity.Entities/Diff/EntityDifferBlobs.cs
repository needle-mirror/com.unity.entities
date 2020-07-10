using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.Entities
{
    static unsafe partial class EntityDiffer
    {
        readonly struct BlobAssetChanges : IDisposable
        {
            public readonly NativeList<BlobAssetChange> CreatedBlobAssets;
            public readonly NativeList<ulong> DestroyedBlobAssets;
            public readonly NativeList<byte> BlobAssetData;
            public readonly bool IsCreated;

            public BlobAssetChanges(Allocator allocator)
            {
                CreatedBlobAssets = new NativeList<BlobAssetChange>(1, allocator);
                DestroyedBlobAssets = new NativeList<ulong>(1, allocator);
                BlobAssetData = new NativeList<byte>(1, allocator);
                IsCreated = true;
            }

            public void Dispose()
            {
                CreatedBlobAssets.Dispose();
                DestroyedBlobAssets.Dispose();
                BlobAssetData.Dispose();
            }
        }

        internal struct BlobAssetsWithDistinctHash : IDisposable
        {
            public NativeList<BlobAssetPtr> BlobAssets;
            public NativeHashMap<ulong, int> BlobAssetsMap;

            public BlobAssetsWithDistinctHash(Allocator allocator)
            {
                BlobAssets = new NativeList<BlobAssetPtr>(1, allocator);
                BlobAssetsMap = new NativeHashMap<ulong, int>(1, allocator);
            }

            public void Dispose()
            {
                BlobAssets.Dispose();
                BlobAssetsMap.Dispose();
            }

            public void TryAdd(BlobAssetPtr blobAssetPtr)
            {
                if (BlobAssetsMap.TryGetValue(blobAssetPtr.Header->Hash, out _))
                    return;

                BlobAssetsMap.TryAdd(blobAssetPtr.Header->Hash, BlobAssets.Length);
                BlobAssets.Add(new BlobAssetPtr(blobAssetPtr.Header));
            }
        }

        [BurstCompile]
        struct SortBlobAssetPtr : IJob
        {
            public NativeArray<BlobAssetPtr> Array;
            public void Execute() => Array.Sort(new BlobAssetPtrHashComparer());
        }

        [BurstCompile]
        struct GatherCreatedAndDestroyedBlobAssets : IJob
        {
            [ReadOnly] public NativeList<BlobAssetPtr> AfterBlobAssets;
            [ReadOnly] public NativeList<BlobAssetPtr> BeforeBlobAssets;
            [WriteOnly] public NativeList<BlobAssetPtr> CreatedBlobAssets;
            [WriteOnly] public NativeList<BlobAssetPtr> DestroyedBlobAssets;

            public void Execute()
            {
                var afterIndex = 0;
                var beforeIndex = 0;

                var comparer = new BlobAssetPtrHashComparer();

                while (afterIndex < AfterBlobAssets.Length && beforeIndex < BeforeBlobAssets.Length)
                {
                    var afterBlobAsset = AfterBlobAssets[afterIndex];
                    var beforeBlobAsset = BeforeBlobAssets[beforeIndex];

                    var compare = comparer.Compare(afterBlobAsset, beforeBlobAsset);

                    if (compare < 0)
                    {
                        CreatedBlobAssets.Add(afterBlobAsset);
                        afterIndex++;
                    }
                    else if (compare == 0)
                    {
                        afterIndex++;
                        beforeIndex++;
                    }
                    else
                    {
                        beforeIndex++;
                        DestroyedBlobAssets.Add(beforeBlobAsset);
                    }
                }

                while (afterIndex < AfterBlobAssets.Length)
                {
                    CreatedBlobAssets.Add(AfterBlobAssets[afterIndex++]);
                }

                while (beforeIndex < BeforeBlobAssets.Length)
                {
                    DestroyedBlobAssets.Add(BeforeBlobAssets[beforeIndex++]);
                }
            }
        }

        [BurstCompile]
        struct GatherBlobAssetChanges : IJob
        {
            [ReadOnly] public NativeList<BlobAssetPtr> CreatedBlobAssets;
            [ReadOnly] public NativeList<BlobAssetPtr> DestroyedBlobAssets;
            [WriteOnly] public NativeList<BlobAssetChange> CreatedBlobAssetChanges;
            [WriteOnly] public NativeList<ulong> DestroyedBlobAssetChanges;
            public NativeList<byte> BlobAssetData;

            public void Execute()
            {
                var totalBlobAssetLength = 0;

                for (var i = 0; i < CreatedBlobAssets.Length; i++)
                {
                    var length = CreatedBlobAssets[i].Header->Length;
                    CreatedBlobAssetChanges.Add(new BlobAssetChange {Length = length, Hash = CreatedBlobAssets[i].Header->Hash});
                    totalBlobAssetLength += length;
                }

                for (var i = 0; i < DestroyedBlobAssets.Length; i++)
                {
                    DestroyedBlobAssetChanges.Add(DestroyedBlobAssets[i].Header->Hash);
                }

                BlobAssetData.Capacity = totalBlobAssetLength;

                for (var i = 0; i < CreatedBlobAssets.Length; i++)
                {
                    BlobAssetData.AddRange(CreatedBlobAssets[i].Data, CreatedBlobAssets[i].Length);
                }
            }
        }

        internal static BlobAssetsWithDistinctHash GetBlobAssetsWithDistinctHash(
            ManagedComponentStore managedComponentStore,
            NativeArray<ArchetypeChunk> chunks,
            Allocator allocator)
        {
            var blobAssetsWithDistinctHash = new BlobAssetsWithDistinctHash(allocator);

            var typeInfoPtr = TypeManager.GetTypeInfoPointer();
            var blobAssetRefOffsetPtr = TypeManager.GetBlobAssetRefOffsetsPointer();

            var managedObjectBlobs = new ManagedObjectBlobs();
            var managedObjectBlobAssets = new NativeList<BlobAssetPtr>(16, Allocator.Temp);
            var managedObjectBlobAssetsMap = new NativeHashMap<BlobAssetPtr, int>(16, Allocator.Temp);

            for (var chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                var chunk = chunks[chunkIndex].m_Chunk;
                var archetype = chunk->Archetype;

                // skip this chunk only if we are _certain_ there are no blob asset refs.
                if (archetype->NumManagedComponents == 0 && archetype->NumSharedComponents == 0 && !archetype->ContainsBlobAssetRefs)
                    continue;

                var typesCount = archetype->TypesCount;
                var entityCount = chunks[chunkIndex].Count;

                for (var unorderedTypeIndexInArchetype = 0; unorderedTypeIndexInArchetype < typesCount; unorderedTypeIndexInArchetype++)
                {
                    var typeIndexInArchetype = archetype->TypeMemoryOrder[unorderedTypeIndexInArchetype];

                    var componentTypeInArchetype = archetype->Types[typeIndexInArchetype];

                    if (componentTypeInArchetype.IsZeroSized)
                        continue;

                    var chunkBuffer = chunk->Buffer;
                    var subArrayOffset = archetype->Offsets[typeIndexInArchetype];
                    var componentArrayStart = chunkBuffer + subArrayOffset;

                    if (componentTypeInArchetype.IsManagedComponent)
                    {
                        var componentSize = archetype->SizeOfs[typeIndexInArchetype];
                        var end = componentArrayStart + componentSize * entityCount;

                        for (var componentData = componentArrayStart; componentData < end; componentData += componentSize)
                        {
                            var managedComponentIndex = *(int*)componentData;
                            var managedComponentValue = managedComponentStore.GetManagedComponent(managedComponentIndex);

                            if (null != managedComponentValue)
                                managedObjectBlobs.GatherBlobAssetReferences(managedComponentValue, managedObjectBlobAssets, managedObjectBlobAssetsMap);
                        }
                    }
                    else
                    {
                        var typeInfo = typeInfoPtr[componentTypeInArchetype.TypeIndex & TypeManager.ClearFlagsMask];
                        var blobAssetRefCount = typeInfo.BlobAssetRefOffsetCount;

                        if (blobAssetRefCount == 0)
                            continue;

                        var blobAssetRefOffsets = blobAssetRefOffsetPtr + typeInfo.BlobAssetRefOffsetStartIndex;

                        if (componentTypeInArchetype.IsBuffer)
                        {
                            var header = (BufferHeader*)componentArrayStart;
                            var strideSize = archetype->SizeOfs[typeIndexInArchetype];
                            var elementSize = typeInfo.ElementSize;

                            for (var entityIndex = 0; entityIndex < entityCount; entityIndex++)
                            {
                                var bufferStart = BufferHeader.GetElementPointer(header);
                                var bufferEnd = bufferStart + header->Length * elementSize;

                                for (var componentData = bufferStart; componentData < bufferEnd; componentData += elementSize)
                                {
                                    AddBlobAssetsWithDistinctHash(componentData, blobAssetRefOffsets, blobAssetRefCount, blobAssetsWithDistinctHash);
                                }

                                header = (BufferHeader*)(((byte*)header) + strideSize);
                            }
                        }
                        else
                        {
                            var componentSize = archetype->SizeOfs[typeIndexInArchetype];
                            var end = componentArrayStart + componentSize * entityCount;

                            for (var componentData = componentArrayStart; componentData < end; componentData += componentSize)
                            {
                                AddBlobAssetsWithDistinctHash(componentData, blobAssetRefOffsets, blobAssetRefCount, blobAssetsWithDistinctHash);
                            }
                        }
                    }
                }
            }

            for (var chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                var chunk = chunks[chunkIndex].m_Chunk;
                var archetype = chunk->Archetype;
                var sharedComponentValues = chunk->SharedComponentValues;

                for (var i = 0; i < archetype->NumSharedComponents; i++)
                {
                    var sharedComponentIndex = sharedComponentValues[i];

                    if (sharedComponentIndex == 0)
                        continue;

                    var sharedComponentValue = managedComponentStore.GetSharedComponentDataNonDefaultBoxed(sharedComponentIndex);
                    managedObjectBlobs.GatherBlobAssetReferences(sharedComponentValue, managedObjectBlobAssets, managedObjectBlobAssetsMap);
                }
            }

            for (var i = 0; i < managedObjectBlobAssets.Length; i++)
            {
                var blobAssetPtr = managedObjectBlobAssets[i];

                void* validationPtr = null;
                try
                {
                    // Try to read ValidationPtr, this might throw if the memory has been unmapped
                    validationPtr = blobAssetPtr.Header->ValidationPtr;
                }
                catch (Exception)
                {
                    // Ignored
                }

                if (validationPtr != blobAssetPtr.Data)
                    continue;

                blobAssetsWithDistinctHash.TryAdd(blobAssetPtr);
            }

            managedObjectBlobAssets.Dispose();
            managedObjectBlobAssetsMap.Dispose();

            new SortBlobAssetPtr
            {
                Array = blobAssetsWithDistinctHash.BlobAssets.AsDeferredJobArray()
            }.Run();

            return blobAssetsWithDistinctHash;
        }

        static void AddBlobAssetsWithDistinctHash(
            byte* componentData,
            TypeManager.EntityOffsetInfo* blobAssetRefOffsets,
            int blobAssetRefCount,
            BlobAssetsWithDistinctHash blobAssets)
        {
            for (var i = 0; i < blobAssetRefCount; ++i)
            {
                var blobAssetRefOffset = blobAssetRefOffsets[i].Offset;
                var blobAssetRefPtr = (BlobAssetReferenceData*)(componentData + blobAssetRefOffset);

                if (blobAssetRefPtr->m_Ptr == null)
                    continue;

                void* validationPtr = null;
                try
                {
                    // Try to read ValidationPtr, this might throw if the memory has been unmapped
                    validationPtr = blobAssetRefPtr->Header->ValidationPtr;
                }
                catch (Exception)
                {
                    // Ignored
                }

                if (validationPtr != blobAssetRefPtr->m_Ptr)
                    continue;

                blobAssets.TryAdd(new BlobAssetPtr(blobAssetRefPtr->Header));
            }
        }

        static BlobAssetChanges GetBlobAssetChanges(
            NativeList<BlobAssetPtr> afterBlobAssets,
            NativeList<BlobAssetPtr> beforeBlobAssets,
            Allocator allocator,
            out JobHandle jobHandle,
            JobHandle dependsOn = default)
        {
            var changes = new BlobAssetChanges(allocator);

            var createdBlobAssets = new NativeList<BlobAssetPtr>(1, Allocator.TempJob);
            var destroyedBlobAssets = new NativeList<BlobAssetPtr>(1, Allocator.TempJob);

            jobHandle = new GatherCreatedAndDestroyedBlobAssets
            {
                AfterBlobAssets = afterBlobAssets,
                BeforeBlobAssets = beforeBlobAssets,
                CreatedBlobAssets = createdBlobAssets,
                DestroyedBlobAssets = destroyedBlobAssets
            }.Schedule(dependsOn);

            jobHandle = new GatherBlobAssetChanges
            {
                CreatedBlobAssets = createdBlobAssets,
                DestroyedBlobAssets = destroyedBlobAssets,
                CreatedBlobAssetChanges = changes.CreatedBlobAssets,
                DestroyedBlobAssetChanges = changes.DestroyedBlobAssets,
                BlobAssetData = changes.BlobAssetData
            }.Schedule(jobHandle);

            jobHandle = JobHandle.CombineDependencies
                (
                    createdBlobAssets.Dispose(jobHandle),
                    destroyedBlobAssets.Dispose(jobHandle)
                );

            return changes;
        }
    }
}
