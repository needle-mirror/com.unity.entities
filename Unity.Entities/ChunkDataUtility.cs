using System;
using System.Diagnostics;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Unity.Entities
{
    // ChunkDataUtility
    //
    // [x] Step 1: Firewall version changes to reduce test vectors
    //     - Everything that could potentially change versions is firewalled in here.
    //     - Anything that takes `ref EntityComponentStore` represents a problem for multi-threading sub-chunk access.
    // [ ] Step 2: Remove EntityComponentStore references
    //

    // Version Change Case 1:
    //   - Component ChangeVersion: All ComponentType(s) in archetype set to GlobalChangeVersion
    //   - Chunk OrderVersion: Destination chunk version set to GlobalChangeVersion.
    //   - Sources:
    //     - AddExistingChunk
    //     - AddEmptyChunk
    //     - Allocate
    //     - AllocateClone
    //     - MoveArchetype
    //     - RemapAllArchetypesJob (direct access GetChangeVersionArrayForType)
    //
    // Version Change Case 2:
    //   - Component ChangeVersion: Only specified ComponentType(s) set to GlobalChangeVersion
    //   - Chunk OrderVersion: Unchanged.
    //   - Sources:
    //     - GetComponentDataWithTypeRW
    //     - GetComponentDataRW
    //     - SwapComponents
    //     - SetSharedComponentDataIndex
    //
    // Version Change Case 3:
    //   - Component ChangeVersion: All ComponentType(s) with EntityReference in archetype set to GlobalChangeVersion
    //   - Chunk OrderVersion: Unchanged.
    //   - Sources:
    //     - ClearMissingReferences
    //
    // Version Change Case 4:
    //   - Component ChangeVersion: ComponentTypes(s) that exist in destination archetype but not source archetype set to GlobalChangeVersion
    //   - Chunk OrderVersion: Unchanged.
    //   - Sources:
    //     - CloneChangeVersions via ChangeArchetypeInPlace
    //     - CloneChangeVersions via PatchAndAddClonedChunks
    //     - CloneChangeVersions via Clone
    //
    // Version Change Case 5:
    //   - Component ChangeVersion: Unchanged.
    //   - Chunk OrderVersion: Destination chunk version set to GlobalChangeVersion.
    //   - Sources:
    //     - Deallocate
    //     - Remove

    [BurstCompile]
    internal static unsafe class ChunkDataUtility
    {
        public static int GetIndexInTypeArray(Archetype* archetype, TypeIndex typeIndex)
        {
            var types = archetype->Types;
            var typeCount = archetype->TypesCount;
            for (var i = 0; i != typeCount; i++)
                if (typeIndex == types[i].TypeIndex)
                    return i;

            return -1;
        }

        // When type arrays are pre-sorted, this can be used to search linearly for a match
        public static int GetNextIndexInTypeArray(Archetype* archetype, TypeIndex typeIndex, int lastTypeIndexInTypeArray)
        {
            Assert.IsTrue(lastTypeIndexInTypeArray >= 0 && lastTypeIndexInTypeArray < archetype->TypesCount);

            var types = archetype->Types;
            var typeCount = archetype->TypesCount;
            for (var i = lastTypeIndexInTypeArray; i != typeCount; i++)
                if (typeIndex == types[i].TypeIndex)
                    return i;

            return -1;
        }

        public static TypeIndex GetTypeIndexFromType(Archetype* archetype, Type componentType)
        {
            var types = archetype->Types;
            var typeCount = archetype->TypesCount;
            for (var i = 0; i != typeCount; i++)
                if (componentType.IsAssignableFrom(TypeManager.GetType(types[i].TypeIndex)))
                    return types[i].TypeIndex;

            return TypeIndex.Null;
        }

        public static void GetIndexInTypeArray(Archetype* archetype, TypeIndex typeIndex, ref short typeLookupCache)
        {
            var types = archetype->Types;
            var typeCount = archetype->TypesCount;

            if (typeLookupCache >= 0 && typeLookupCache < typeCount && types[typeLookupCache].TypeIndex == typeIndex)
                return;

            for (var i = 0; i != typeCount; i++)
            {
                if (typeIndex != types[i].TypeIndex)
                    continue;

                typeLookupCache = (short)i;
                return;
            }

            typeLookupCache = -1;
        }

        public static void SetSharedComponentDataIndex(Entity entity, Archetype* archetype, in SharedComponentValues sharedComponentValues, TypeIndex typeIndex)
        {
            var entityComponentStore = archetype->EntityComponentStore;

            entityComponentStore->Move(entity, archetype, sharedComponentValues);

            var chunk = entityComponentStore->GetChunk(entity);
            Assert.AreEqual((ulong)archetype, (ulong)chunk->Archetype); // chunk should still be in the same archetype
            var indexInTypeArray = GetIndexInTypeArray(archetype, typeIndex);
            var globalSystemVersion = entityComponentStore->GlobalSystemVersion;
            archetype->Chunks.SetChangeVersion(indexInTypeArray, chunk->ListIndex, globalSystemVersion);
        }

        public static void SetSharedComponentDataIndex(Chunk* chunk, Archetype* archetype, in SharedComponentValues sharedComponentValues, TypeIndex typeIndex)
        {
            var entityComponentStore = archetype->EntityComponentStore;
            entityComponentStore->Move(chunk, archetype, sharedComponentValues);

            Assert.AreEqual((ulong)archetype, (ulong)chunk->Archetype); // chunk should still be in the same archetype
            var indexInTypeArray = GetIndexInTypeArray(archetype, typeIndex);
            var globalSystemVersion = entityComponentStore->GlobalSystemVersion;
            archetype->Chunks.SetChangeVersion(indexInTypeArray, chunk->ListIndex, globalSystemVersion);
        }

        public static void SetSharedComponentDataIndex(EntityBatchInChunk batch, Archetype* archetype, in SharedComponentValues sharedComponentValues, TypeIndex typeIndex)
        {
            var entityComponentStore = archetype->EntityComponentStore;
            entityComponentStore->MoveAndSetChangeVersion(batch, archetype, sharedComponentValues, typeIndex);
        }

        // This variant returns an invalid pointer if the component is not present.
        // If you'd like a null pointer in this case instead, use GetOptionalComponentDataWithTypeRO()
        public static byte* GetComponentDataWithTypeRO(Chunk* chunk, Archetype* archetype, int baseEntityIndex, TypeIndex typeIndex, ref LookupCache lookupCache)
        {
            if (Hint.Unlikely(lookupCache.Archetype != archetype))
                lookupCache.Update(archetype, typeIndex);

            return chunk->Buffer + (lookupCache.ComponentOffset + lookupCache.ComponentSizeOf * baseEntityIndex);
        }

        // This variant returns an invalid pointer if the component is not present.
        // If you'd like a null pointer in this case instead, use GetOptionalComponentDataWithTypeRW()
        public static byte* GetComponentDataWithTypeRW(Chunk* chunk, Archetype* archetype, int baseEntityIndex, TypeIndex typeIndex, uint globalSystemVersion, ref LookupCache lookupCache)
        {
            if (Hint.Unlikely(lookupCache.Archetype != archetype))
                lookupCache.Update(archetype, typeIndex);

            // Write Component to Chunk. ChangeVersion:Yes OrderVersion:No
            chunk->SetChangeVersion(lookupCache.IndexInArchetype, globalSystemVersion);
            return chunk->Buffer + (lookupCache.ComponentOffset + lookupCache.ComponentSizeOf * baseEntityIndex);
        }

        // This variant returns null if the component is not present.
        public static byte* GetOptionalComponentDataWithTypeRO(Chunk* chunk, Archetype* archetype, int baseEntityIndex, TypeIndex typeIndex, ref LookupCache lookupCache)
        {
            if (Hint.Unlikely(lookupCache.Archetype != archetype))
                lookupCache.Update(archetype, typeIndex);
            if (Hint.Unlikely(lookupCache.IndexInArchetype == -1))
                return null;

            return chunk->Buffer + (lookupCache.ComponentOffset + lookupCache.ComponentSizeOf * baseEntityIndex);
        }

        // This variant returns null if the component is not present.
        public static byte* GetOptionalComponentDataWithTypeRW(Chunk* chunk, Archetype* archetype, int baseEntityIndex, TypeIndex typeIndex, uint globalSystemVersion, ref LookupCache lookupCache)
        {
            if (Hint.Unlikely(lookupCache.Archetype != archetype))
                lookupCache.Update(archetype, typeIndex);
            if (Hint.Unlikely(lookupCache.IndexInArchetype == -1))
                return null;

            // Write Component to Chunk. ChangeVersion:Yes OrderVersion:No
            chunk->SetChangeVersion(lookupCache.IndexInArchetype, globalSystemVersion);
            return chunk->Buffer + (lookupCache.ComponentOffset + lookupCache.ComponentSizeOf * baseEntityIndex);
        }

        public static byte* GetComponentDataWithTypeRO(Chunk* chunk, int baseEntityIndex, TypeIndex typeIndex)
        {
            var indexInTypeArray = GetIndexInTypeArray(chunk->Archetype, typeIndex);

            var offset = chunk->Archetype->Offsets[indexInTypeArray];
            var sizeOf = chunk->Archetype->SizeOfs[indexInTypeArray];

            return chunk->Buffer + (offset + sizeOf * baseEntityIndex);
        }

        public static byte* GetComponentDataWithTypeRW(Chunk* chunk, int baseEntityIndex, TypeIndex typeIndex, uint globalSystemVersion)
        {
            var indexInTypeArray = GetIndexInTypeArray(chunk->Archetype, typeIndex);

            var offset = chunk->Archetype->Offsets[indexInTypeArray];
            var sizeOf = chunk->Archetype->SizeOfs[indexInTypeArray];

            // Write Component to Chunk. ChangeVersion:Yes OrderVersion:No
            chunk->SetChangeVersion(indexInTypeArray, globalSystemVersion);

            return chunk->Buffer + (offset + sizeOf * baseEntityIndex);
        }

        public static byte* GetComponentDataRO(Chunk* chunk, int baseEntityIndex, int indexInTypeArray)
        {
            var offset = chunk->Archetype->Offsets[indexInTypeArray];
            var sizeOf = chunk->Archetype->SizeOfs[indexInTypeArray];

            return chunk->Buffer + (offset + sizeOf * baseEntityIndex);
        }

        public static byte* GetComponentDataRW(Chunk* chunk, int baseEntityIndex, int indexInTypeArray, uint globalSystemVersion)
        {
            var offset = chunk->Archetype->Offsets[indexInTypeArray];
            var sizeOf = chunk->Archetype->SizeOfs[indexInTypeArray];

            // Write Component to Chunk. ChangeVersion:Yes OrderVersion:No
            chunk->SetChangeVersion(indexInTypeArray, globalSystemVersion);

            return chunk->Buffer + (offset + sizeOf * baseEntityIndex);
        }

        public static UnsafeBitArray GetEnabledRefRO(Chunk* chunk, int indexInTypeArray)
        {
            var archetype = chunk->Archetype;
            var chunks = archetype->Chunks;
            int memoryOrderIndexInArchetype = archetype->TypeIndexInArchetypeToMemoryOrderIndex[indexInTypeArray];
            return chunks.GetEnabledArrayForTypeInChunk(memoryOrderIndexInArchetype, chunk->ListIndex);
        }

        public static UnsafeBitArray GetEnabledRefRW(Chunk* chunk, int indexInTypeArray,
            out int* ptrChunkDisabledCount)
            => GetEnabledRefRW(chunk, indexInTypeArray,
                chunk->Archetype->EntityComponentStore->GlobalSystemVersion, out ptrChunkDisabledCount);

        public static UnsafeBitArray GetEnabledRefRW(Chunk* chunk, int indexInTypeArray, uint globalSystemVersion, out int* ptrChunkDisabledCount)
        {
            chunk->SetChangeVersion(indexInTypeArray, globalSystemVersion);

            var archetype = chunk->Archetype;
            var chunks = archetype->Chunks;
            int memoryOrderIndexInArchetype = archetype->TypeIndexInArchetypeToMemoryOrderIndex[indexInTypeArray];
            ptrChunkDisabledCount = chunks.GetPointerToChunkDisabledCountForType(memoryOrderIndexInArchetype, chunk->ListIndex);
            return chunks.GetEnabledArrayForTypeInChunk(memoryOrderIndexInArchetype, chunk->ListIndex);
        }

        public static void Copy(Chunk* srcChunk, int srcIndex, Chunk* dstChunk, int dstIndex, int count)
        {
            Assert.IsTrue(srcChunk->Archetype == dstChunk->Archetype);

            var arch = srcChunk->Archetype;
            var srcBuffer = srcChunk->Buffer;
            var dstBuffer = dstChunk->Buffer;
            var offsets = arch->Offsets;
            var sizeOfs = arch->SizeOfs;
            var typesCount = arch->TypesCount;

            for (var t = 0; t < typesCount; t++)
            {
                var offset = offsets[t];
                var sizeOf = sizeOfs[t];
                var src = srcBuffer + (offset + sizeOf * srcIndex);
                var dst = dstBuffer + (offset + sizeOf * dstIndex);

                UnsafeUtility.MemCpy(dst, src, sizeOf * count);
            }
        }


        // Assumes that chunk->Count is valid
        public static void UpdateChunkDisabledEntityCounts(Chunk* chunk)
        {
            var arch = chunk->Archetype;
            int chunkIndexInArchetype = chunk->ListIndex;
            int chunkEntityCount = chunk->Count;
            var chunkTypeEnabledMasks = arch->Chunks.GetComponentEnabledMaskArrayForChunk(chunkIndexInArchetype);
            var chunkTypeDisabledCounts = arch->Chunks.GetChunkDisabledCounts(chunkIndexInArchetype);
            for (var t = 0; t < arch->TypesCount; t++)
            {
                int chunkEnabledCount = EnabledBitUtility.countbits(chunkTypeEnabledMasks[t]);
                chunkTypeDisabledCounts[t] = chunkEntityCount - chunkEnabledCount;
            }
        }

        // Use this when cloning entities inside of a chunk to either the same chunk or a different chunk
        public static void CloneEnabledBits(Chunk* srcChunk, int srcEntityIndexInChunk, Chunk* dstChunk, int dstEntityIndexInChunk, int entityCount)
        {
            var srcArch = srcChunk->Archetype;
            var dstArch = dstChunk->Archetype;
            int srcChunkIndexInArchetype = srcChunk->ListIndex;
            int dstChunkIndexInArchetype = dstChunk->ListIndex;

            var srcEnableableTypesCount = srcArch->EnableableTypesCount;
            var dstTypeIndexInArchetype = 0;
            for (var t = 0; t < srcEnableableTypesCount; t++)
            {
                var srcTypeIndexInArchetype = srcArch->EnableableTypeIndexInArchetype[t];

                // Using GetNextIndexInTypeArray() here avoids an N^2 loop while matching src components to dst ones,
                // but relies on the assumption that dstTypeIndex will increase every time through this loop --
                // basically, that the archetype's type arrays are sorted by typeIndex. Otherwise, it will fail to find
                // dstType earlier in the archetype, and the type's bits will quietly not be cloned.
                var srcType = srcArch->Types[srcTypeIndexInArchetype];
                int result = GetNextIndexInTypeArray(dstArch, srcType.TypeIndex, dstTypeIndexInArchetype);
                if (result < 0)
                    continue;
                dstTypeIndexInArchetype = result;

                var srcBits = srcArch->Chunks.GetEnabledArrayForTypeInChunk(srcArch->TypeIndexInArchetypeToMemoryOrderIndex[srcTypeIndexInArchetype], srcChunkIndexInArchetype);
                var dstBits = dstArch->Chunks.GetEnabledArrayForTypeInChunk(dstArch->TypeIndexInArchetypeToMemoryOrderIndex[dstTypeIndexInArchetype], dstChunkIndexInArchetype);
                dstBits.Copy(dstEntityIndexInChunk, ref srcBits, srcEntityIndexInChunk, entityCount);
            }
        }

        // Use this when instantiating a single entity many times
        public static void ReplicateEnabledBits(Chunk* srcChunk, int srcIndex, Chunk* dstChunk, int dstIndex, int count)
        {
            var srcArch = srcChunk->Archetype;
            var dstArch = dstChunk->Archetype;
            var srcEnableableTypesCount = srcArch->EnableableTypesCount;
            int dstIndexInArchetype = 0;

            for (var t = 0; t < srcEnableableTypesCount; t++)
            {
                var srcIndexInArchetype = srcArch->EnableableTypeIndexInArchetype[t];
                int srcMemoryOrderIndexInArchetype = srcArch->TypeIndexInArchetypeToMemoryOrderIndex[srcIndexInArchetype];
                var srcBits = srcArch->Chunks.GetEnabledArrayForTypeInChunk(srcMemoryOrderIndexInArchetype, srcChunk->ListIndex);
                var srcValue = srcBits.IsSet(srcIndex);

                int result = GetNextIndexInTypeArray(dstArch, srcArch->Types[srcIndexInArchetype].TypeIndex, dstIndexInArchetype);
                if (result < 0)
                    continue;
                dstIndexInArchetype = result;

                int dstMemoryOrderIndexInArchetype = dstArch->TypeIndexInArchetypeToMemoryOrderIndex[dstIndexInArchetype];
                var dstBits = dstArch->Chunks.GetEnabledArrayForTypeInChunk(dstMemoryOrderIndexInArchetype, dstChunk->ListIndex);
                dstBits.SetBits(dstIndex, srcValue, count);

                // If the src value is disabled, adjust the hierarchical data
                if (!srcValue)
                {
                    dstArch->Chunks.AdjustChunkDisabledCountForType(dstMemoryOrderIndexInArchetype, dstChunk->ListIndex, count);
                }
            }
        }

        // Use this when moving a chunk between archetypes (or to a new slot within its current archetype)
        // SrcChunk and DstChunk are the same chunk which is currently being moved from srcArchetype to dstArchetype
        // Make sure the chunk is in both Archetypes' chunk lists when called
        // It is okay if srcArchetype and dstArchetype are the same, as long as srcChunkIndex and dstChunkIndex are different
        public static void MoveEnabledBits(Archetype* srcArchetype, int srcChunkIndex, Archetype* dstArchetype, int dstChunkIndex, int srcEntityCount)
        {
            Assert.IsTrue(srcArchetype == dstArchetype || srcArchetype->Chunks[srcChunkIndex] == dstArchetype->Chunks[dstChunkIndex]);
            var dstEnableableTypesCount = dstArchetype->EnableableTypesCount;
            var srcTypeIndexInArchetype = 0;
            for (var t = 0; t < dstEnableableTypesCount; t++)
            {
                var dstTypeIndexInArchetype = dstArchetype->EnableableTypeIndexInArchetype[t];
                int dstMemoryOrderIndexInArchetype = dstArchetype->TypeIndexInArchetypeToMemoryOrderIndex[dstTypeIndexInArchetype];
                var dstBits = dstArchetype->Chunks.GetEnabledArrayForTypeInChunk(dstMemoryOrderIndexInArchetype, dstChunkIndex);
                // Clear destination bits & bit count in preparation for new values.
                // This is technically redundant, as the previous call to InitializeBitsForNewChunk() should already
                // zero-initialize this data.
                dstBits.Clear();
                dstArchetype->Chunks.SetChunkDisabledCountForType(dstMemoryOrderIndexInArchetype, dstChunkIndex, 0);

                int result = GetNextIndexInTypeArray(srcArchetype, dstArchetype->Types[dstTypeIndexInArchetype].TypeIndex, srcTypeIndexInArchetype);
                if (result < 0)
                {
                    // If this type was not in srcArchetype, then it should default to enabled for all entities in the chunk.
                    dstBits.SetBits(0, true, srcEntityCount);
                    // The disabled count is already set to 0 earlier in this function, so no need to set it again.
                }
                else
                {
                    // If the type is in both srcArchetype and dstArchetype, copy its bits & count from srcChunk to dstChunk.
                    srcTypeIndexInArchetype = result;
                    int srcMemoryOrderIndexInArchetype = srcArchetype->TypeIndexInArchetypeToMemoryOrderIndex[srcTypeIndexInArchetype];
                    var srcBits = srcArchetype->Chunks.GetEnabledArrayForTypeInChunk(srcMemoryOrderIndexInArchetype, srcChunkIndex);
                    // Copy src bits to dst bits
                    dstBits.Copy(0, ref srcBits, 0, srcEntityCount);
                    // Copy enabled bit count to destination
                    var srcDisabledCount = srcArchetype->Chunks.GetChunkDisabledCountForType(srcMemoryOrderIndexInArchetype, srcChunkIndex);
                    dstArchetype->Chunks.SetChunkDisabledCountForType(dstMemoryOrderIndexInArchetype, dstChunkIndex, srcDisabledCount);
                }
            }
            // Clear the source bits and counts, because we're doing a full move.
            InitializeBitsForNewChunk(srcArchetype, srcChunkIndex);
        }

        public static void ClearPaddingBits(Chunk* chunk, int startIndex, int count)
        {
            var archetype = chunk->Archetype;
            var enableableTypesCount = archetype->EnableableTypesCount;

            for (var t = 0; t < enableableTypesCount; t++)
            {
                var indexInArchetype = archetype->EnableableTypeIndexInArchetype[t];
                int memoryOrderIndexInArchetype = archetype->TypeIndexInArchetypeToMemoryOrderIndex[indexInArchetype];
                var bits = archetype->Chunks.GetEnabledArrayForTypeInChunk(memoryOrderIndexInArchetype, chunk->ListIndex);
                bits.SetBits(startIndex, false, count);
            }
        }

        public static void InitializeBitsForNewChunk(Archetype* archetype, int chunkIndex)
        {
            // all bits for all components on all entities are set to zero by default on new chunks
            var enabledBits = archetype->Chunks.GetComponentEnabledMaskArrayForChunk(chunkIndex);
            UnsafeUtility.MemClear(enabledBits, (long)archetype->Chunks.ComponentEnabledBitsSizeTotalPerChunk);

            archetype->Chunks.InitializeDisabledCountForChunk(chunkIndex);
        }

        public static void RemoveFromEnabledBitsHierarchicalData(Chunk* chunk, int startIndex, int count)
        {
            var archetype = chunk->Archetype;
            var enableableTypesCount = archetype->EnableableTypesCount;

            for (var t = 0; t < enableableTypesCount; t++)
            {
                var indexInArchetype = archetype->EnableableTypeIndexInArchetype[t];
                int memoryOrderIndexInArchetype = archetype->TypeIndexInArchetypeToMemoryOrderIndex[indexInArchetype];
                var bits = archetype->Chunks.GetEnabledArrayForTypeInChunk(memoryOrderIndexInArchetype, chunk->ListIndex);

                var removedDisabledCount = count - bits.CountBits(startIndex, count);
                archetype->Chunks.AdjustChunkDisabledCountForType(memoryOrderIndexInArchetype, chunk->ListIndex, -removedDisabledCount);
            }
        }

        public static void CopyComponents(Chunk* srcChunk, int srcIndex, Chunk* dstChunk, int dstIndex, int count, uint dstGlobalSystemVersion)
        {
            var srcArch = srcChunk->Archetype;
            var typesCount = srcArch->TypesCount;

#if UNITY_ASSERTIONS
            // This function is used to swap data between different world so assert that the layout is identical if
            // the archetypes dont match
            var dstArch = dstChunk->Archetype;
            if (srcArch != dstArch)
            {
                Assert.AreEqual(typesCount, dstChunk->Archetype->TypesCount);
                for (int i = 0; i < typesCount; ++i)
                {
                    Assert.AreEqual(srcArch->Types[i], dstArch->Types[i]);
                    Assert.AreEqual(srcArch->Offsets[i], dstArch->Offsets[i]);
                    Assert.AreEqual(srcArch->SizeOfs[i], dstArch->SizeOfs[i]);
                }
            }
#endif

            var srcBuffer = srcChunk->Buffer;
            var dstBuffer = dstChunk->Buffer;
            var offsets = srcArch->Offsets;
            var sizeOfs = srcArch->SizeOfs;

            for (var t = 1; t < typesCount; t++) // Only copy component data, not Entity
            {
                var offset = offsets[t];
                var sizeOf = sizeOfs[t];
                var src = srcBuffer + (offset + sizeOf * srcIndex);
                var dst = dstBuffer + (offset + sizeOf * dstIndex);

                dstChunk->SetChangeVersion(t, dstGlobalSystemVersion);

                UnsafeUtility.MemCpy(dst, src, sizeOf * count);
            }
        }

        [BurstCompile]
        public static void SwapComponents(Chunk* srcChunk, int srcIndex, Chunk* dstChunk, int dstIndex, int count, uint srcGlobalSystemVersion, uint dstGlobalSystemVersion)
        {
            var srcArch = srcChunk->Archetype;
            var typesCount = srcArch->TypesCount;


#if UNITY_ASSERTIONS
            // This function is used to swap data between different world so assert that the layout is identical if
            // the archetypes dont match
            var dstArch = dstChunk->Archetype;
            if (srcArch != dstArch)
            {
                Assert.AreEqual(typesCount, dstChunk->Archetype->TypesCount);
                for (int i = 0; i < typesCount; ++i)
                {
                    Assert.AreEqual(srcArch->Types[i], dstArch->Types[i]);
                    Assert.AreEqual(srcArch->Offsets[i], dstArch->Offsets[i]);
                    Assert.AreEqual(srcArch->SizeOfs[i], dstArch->SizeOfs[i]);
                }
            }
#endif

            var srcBuffer = srcChunk->Buffer;
            var dstBuffer = dstChunk->Buffer;
            var offsets = srcArch->Offsets;
            var sizeOfs = srcArch->SizeOfs;

            for (var t = 1; t < typesCount; t++) // Only swap component data, not Entity
            {
                var offset = offsets[t];
                var sizeOf = sizeOfs[t];
                var src = srcBuffer + (offset + sizeOf * srcIndex);
                var dst = dstBuffer + (offset + sizeOf * dstIndex);
                Byte* buffer = stackalloc Byte[sizeOf * count];

                dstChunk->SetChangeVersion(t, dstGlobalSystemVersion);
                srcChunk->SetChangeVersion(t, srcGlobalSystemVersion);

                UnsafeUtility.MemCpy(buffer, src, sizeOf * count);
                UnsafeUtility.MemCpy(src, dst, sizeOf * count);
                UnsafeUtility.MemCpy(dst, buffer, sizeOf * count);
            }
        }

        public static void InitializeComponents(Chunk* dstChunk, int dstIndex, int count)
        {
            var arch = dstChunk->Archetype;

            var offsets = arch->Offsets;
            var sizeOfs = arch->SizeOfs;
            var bufferCapacities = arch->BufferCapacities;
            var dstBuffer = dstChunk->Buffer;
            var typesCount = arch->TypesCount;
            var types = arch->Types;

            for (var t = 1; t != typesCount; t++)
            {
                var offset = offsets[t];
                var sizeOf = sizeOfs[t];
                var dst = dstBuffer + (offset + sizeOf * dstIndex);

                if (types[t].IsBuffer)
                {
                    for (var i = 0; i < count; ++i)
                    {
                        BufferHeader.Initialize((BufferHeader*)dst, bufferCapacities[t]);
                        dst += sizeOf;
                    }
                }
                else
                {
                    UnsafeUtility.MemClear(dst, sizeOf * count);
                }
            }

            // Initialize the enabled bits for all enableable components including Entity
            // We set only the bits for which we have an existing entity
            var enableableTypeCount = arch->EnableableTypesCount;
            for (var t = 0; t != enableableTypeCount; t++)
            {
                var indexInArchetype = arch->EnableableTypeIndexInArchetype[t];
                int memoryOrderIndexInArchetype = arch->TypeIndexInArchetypeToMemoryOrderIndex[indexInArchetype];
                var bits = arch->Chunks.GetEnabledArrayForTypeInChunk(memoryOrderIndexInArchetype, dstChunk->ListIndex);
                bits.SetBits(dstIndex, true, count);
            }

        }

        public static void InitializeBuffersInChunk(byte* p, int count, int stride, int bufferCapacity)
        {
            for (int i = 0; i < count; i++)
            {
                BufferHeader.Initialize((BufferHeader*)p, bufferCapacity);
                p += stride;
            }
        }

        public static void Convert(Chunk* srcChunk, int srcIndex, Chunk* dstChunk, int dstIndex, int count)
        {
            Assert.IsFalse(srcChunk == dstChunk);
            var srcArch = srcChunk->Archetype;
            var dstArch = dstChunk->Archetype;
            var entityComponentStore = dstArch->EntityComponentStore;
            if (srcArch != dstArch)
            {
                Assert.IsFalse(srcArch == null);
            }

            // Process non-zero-sized types
            var srcI = srcArch->NonZeroSizedTypesCount - 1;
            var dstI = dstArch->NonZeroSizedTypesCount - 1;

            var sourceTypesToDealloc = stackalloc int[srcI + 1];
            int sourceTypesToDeallocCount = 0;

            while (dstI >= 0)
            {
                var srcType = srcArch->Types[srcI];
                var dstType = dstArch->Types[dstI];

                if (srcType > dstType)
                {
                    //Type in source is not moved so deallocate it
                    sourceTypesToDealloc[sourceTypesToDeallocCount++] = srcI;
                    --srcI;
                    continue;
                }

                var srcStride = srcArch->SizeOfs[srcI];
                var dstStride = dstArch->SizeOfs[dstI];
                var src = srcChunk->Buffer + srcArch->Offsets[srcI] + srcIndex * srcStride;
                var dst = dstChunk->Buffer + dstArch->Offsets[dstI] + dstIndex * dstStride;

                if (srcType == dstType)
                {
                    // Component exists in both src and dst archetypes; copy current value.
                    UnsafeUtility.MemCpy(dst, src, count * srcStride);
                    --srcI;
                    --dstI;
                }
                else
                {
                    // Component is in dst but not source. Clear values to default.
                    if (dstType.IsBuffer)
                        InitializeBuffersInChunk(dst, count, dstStride, dstArch->BufferCapacities[dstI]);
                    else
                        UnsafeUtility.MemClear(dst, count * dstStride);
                    // Newly-added enableable components should be enabled by default
                    if (dstType.IsEnableable)
                    {
                        int dstMemoryOrderIndexInArchetype = dstArch->TypeIndexInArchetypeToMemoryOrderIndex[dstI];
                        var bits = dstArch->Chunks.GetEnabledArrayForTypeInChunk(dstMemoryOrderIndexInArchetype, dstChunk->ListIndex);
                        bits.SetBits(dstIndex, true, count);
                    }
                    --dstI;
                }
            }

            // Tag components don't need to be copied/deallocated, but any new tag components need to have their
            // enabled bits initialized.
            var srcTagI = srcArch->NumTagComponents - 1;
            var dstTagI = dstArch->NumTagComponents - 1;
            while (dstTagI >= 0)
            {
                srcI = srcArch->FirstTagComponent + srcTagI;
                dstI = dstArch->FirstTagComponent + dstTagI;
                var srcType = srcArch->Types[srcI];
                var dstType = dstArch->Types[dstI];

                if (srcType > dstType)
                {
                    //Type in source is not moved into dst
                    --srcTagI;
                    continue;
                }

                if (srcType == dstType)
                {
                    // Component exists in both src and dst archetypes
                    --srcTagI;
                    --dstTagI;
                }
                else
                {
                    // Component is in dst but not source.
                    // Newly-added enableable components should be enabled by default
                    if (dstType.IsEnableable)
                    {
                        int dstMemoryOrderIndexInArchetype = dstArch->TypeIndexInArchetypeToMemoryOrderIndex[dstI];
                        var bits = dstArch->Chunks.GetEnabledArrayForTypeInChunk(dstMemoryOrderIndexInArchetype, dstChunk->ListIndex);
                        bits.SetBits(dstIndex, true, count);
                    }
                    --dstTagI;
                }
            }

            if (sourceTypesToDeallocCount == 0)
                return;

            sourceTypesToDealloc[sourceTypesToDeallocCount] = 0;

            int iDealloc = 0;
            if (sourceTypesToDealloc[iDealloc] >= srcArch->FirstManagedComponent)
            {
                var freeCommandHandle = entityComponentStore->ManagedChangesTracker.BeginFreeManagedComponentCommand();
                do
                {
                    srcI = sourceTypesToDealloc[iDealloc];
                    var srcStride = srcArch->SizeOfs[srcI];
                    var src = srcChunk->Buffer + srcArch->Offsets[srcI] + srcIndex * srcStride;

                    var a = (int*)src;
                    for (int i = 0; i < count; i++)
                    {
                        var managedComponentIndex = a[i];
                        if (managedComponentIndex == 0)
                            continue;
                        entityComponentStore->FreeManagedComponentIndex(managedComponentIndex);
                        entityComponentStore->ManagedChangesTracker.AddToFreeManagedComponentCommand(managedComponentIndex);
                    }
                }
                while ((sourceTypesToDealloc[++iDealloc] >= srcArch->FirstManagedComponent));
                entityComponentStore->ManagedChangesTracker.EndDeallocateManagedComponentCommand(freeCommandHandle);
            }

            while (sourceTypesToDealloc[iDealloc] >= srcArch->FirstBufferComponent)
            {
                srcI = sourceTypesToDealloc[iDealloc];
                var srcStride = srcArch->SizeOfs[srcI];
                var srcPtr = srcChunk->Buffer + srcArch->Offsets[srcI] + srcIndex * srcStride;
                for (int i = 0; i < count; i++)
                {
                    BufferHeader.Destroy((BufferHeader*)srcPtr);
                    srcPtr += srcStride;
                }
                ++iDealloc;
            }
        }

        public static void MemsetUnusedChunkData(Chunk* chunk, byte value)
        {
            var arch = chunk->Archetype;
            var buffer = chunk->Buffer;
            var count = chunk->Count;

            // Clear unused buffer data
            for (int i = 0; i < arch->TypesCount; ++i)
            {
                var componentDataType = &arch->Types[i];
                var componentSize = arch->SizeOfs[i];

                if (componentDataType->IsBuffer)
                {
                    var elementSize = TypeManager.GetTypeInfo(componentDataType->TypeIndex).ElementSize;
                    var bufferCapacity = arch->BufferCapacities[i];

                    for (int chunkI = 0; chunkI < count; chunkI++)
                    {
                        var bufferHeader = (BufferHeader*)(buffer + arch->Offsets[i] + (chunkI * componentSize));
                        BufferHeader.MemsetUnusedMemory(bufferHeader, bufferCapacity, elementSize, value);
                    }
                }
            }

            // Clear chunk data stream padding
            for (int i = 0; i < arch->TypesCount - 1; ++i)
            {
                var index = arch->TypeMemoryOrderIndexToIndexInArchetype[i];

                var nextIndex = arch->TypeMemoryOrderIndexToIndexInArchetype[i + 1];
                var componentSize = arch->SizeOfs[index];
                var startOffset = arch->Offsets[index] + count * componentSize;
                var endOffset = arch->Offsets[nextIndex];

                Assert.AreNotEqual(-1, startOffset);
                Assert.AreNotEqual(-1, endOffset);
                Assert.IsTrue(componentSize >= 0);

                UnsafeUtility.MemSet(buffer + startOffset, value, endOffset - startOffset);
            }
            var lastIndex = arch->TypeMemoryOrderIndexToIndexInArchetype[arch->TypesCount - 1];
            var lastStartOffset = arch->Offsets[lastIndex] + count * arch->SizeOfs[lastIndex];
            var bufferSize = Chunk.GetChunkBufferSize();
            UnsafeUtility.MemSet(buffer + lastStartOffset, value, bufferSize - lastStartOffset);

            // clear the chunk header padding zone
            UnsafeUtility.MemClear(Chunk.kSerializedHeaderSize + (byte*)chunk, Chunk.kBufferOffset - Chunk.kSerializedHeaderSize);
        }

        public static bool AreLayoutCompatible(Archetype* a, Archetype* b)
        {
            if ((a == null) || (b == null) ||
                (a->ChunkCapacity != b->ChunkCapacity))
                return false;

            var typeCount = a->NonZeroSizedTypesCount;
            if (typeCount != b->NonZeroSizedTypesCount)
                return false;

            for (int i = 0; i < typeCount; ++i)
            {
                if (a->Types[i] != b->Types[i])
                    return false;
            }

            return true;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public static void AssertAreLayoutCompatible(Archetype* a, Archetype* b)
        {
            Assert.IsTrue(AreLayoutCompatible(a, b));

            var typeCount = a->NonZeroSizedTypesCount;

            //If types are identical; SizeOfs, Offsets and BufferCapacities should match
            for (int i = 0; i < typeCount; ++i)
            {
                Assert.AreEqual(a->SizeOfs[i], b->SizeOfs[i]);
                Assert.AreEqual(a->Offsets[i], b->Offsets[i]);
                Assert.AreEqual(a->BufferCapacities[i], b->BufferCapacities[i]);
            }
        }

        public static void DeallocateBuffers(Chunk* chunk)
        {
            var archetype = chunk->Archetype;

            var bufferComponentsEnd = archetype->BufferComponentsEnd;
            for (var ti = archetype->FirstBufferComponent; ti < bufferComponentsEnd; ++ti)
            {
                Assert.IsTrue(archetype->Types[ti].IsBuffer);
                var basePtr = chunk->Buffer + archetype->Offsets[ti];
                var stride = archetype->SizeOfs[ti];

                for (int i = 0; i < chunk->Count; ++i)
                {
                    byte* bufferPtr = basePtr + stride * i;
                    BufferHeader.Destroy((BufferHeader*)bufferPtr);
                }
            }
        }

        static void ReleaseChunk(Chunk* chunk)
        {
            var archetype = chunk->Archetype;
            var entityComponentStore = archetype->EntityComponentStore;

            // Remove references to shared components
            if (chunk->Archetype->NumSharedComponents > 0)
            {
                var sharedComponentValueArray = chunk->SharedComponentValues;

                for (var i = 0; i < archetype->NumSharedComponents; ++i)
                {
                    var sharedComponentIndex = sharedComponentValueArray[i];
                    if (EntityComponentStore.IsUnmanagedSharedComponentIndex(sharedComponentIndex))
                    {
                        entityComponentStore->RemoveSharedComponentReference_Unmanaged(sharedComponentIndex);
                    } else {
                        entityComponentStore->ManagedChangesTracker.RemoveReference(sharedComponentIndex);
                    }
                }
            }

            // this chunk is going away, so it shouldn't be in the empty slot list.
            if (chunk->Count < chunk->Capacity)
                archetype->EmptySlotTrackingRemoveChunk(chunk);

            chunk->Archetype->RemoveFromChunkList(chunk, ref entityComponentStore->m_ChunkListChangesTracker);
            chunk->Archetype = null;

            entityComponentStore->FreeChunk(chunk);
        }

        public static void SetChunkCountKeepMetaChunk(Chunk* chunk, int newCount)
        {
            Assert.AreNotEqual(newCount, chunk->Count);

            // Chunk released to empty chunk pool
            if (newCount == 0)
            {
                ReleaseChunk(chunk);
                return;
            }

            var capacity = chunk->Capacity;

            // Chunk is now full
            if (newCount == capacity)
            {
                // this chunk no longer has empty slots, so it shouldn't be in the empty slot list.
                chunk->Archetype->EmptySlotTrackingRemoveChunk(chunk);
            }
            // Chunk is no longer full
            else if (chunk->Count == capacity)
            {
                Assert.IsTrue(newCount < chunk->Count);
                chunk->Archetype->EmptySlotTrackingAddChunk(chunk);
            }

            chunk->Count = newCount;
            chunk->Archetype->Chunks.SetChunkEntityCount(chunk->ListIndex, newCount);
        }

        public static void SetChunkCount(Chunk* chunk, int newCount)
        {
            var archetype = chunk->Archetype;
            var entityComponentStore = archetype->EntityComponentStore;

            var metaChunkEntity = chunk->metaChunkEntity;
            if (newCount == 0 && metaChunkEntity != Entity.Null)
                entityComponentStore->DestroyMetaChunkEntity(metaChunkEntity);

            SetChunkCountKeepMetaChunk(chunk, newCount);
        }

        // #todo DOTS-1189
        static int AllocateIntoChunk(Chunk* chunk, int count, out int outIndex)
        {
            var allocatedCount = Math.Min(chunk->Capacity - chunk->Count, count);
            outIndex = chunk->Count;
            SetChunkCount(chunk, chunk->Count + allocatedCount);
            chunk->Archetype->EntityCount += allocatedCount;
            return allocatedCount;
        }

        public static void Allocate(Chunk* chunk, int count)
        {
            Allocate(chunk, null, count);
        }

        public static void Allocate(Chunk* chunk, Entity* entities, int count)
        {
            var archetype = chunk->Archetype;
            var entityComponentStore = archetype->EntityComponentStore;
            var globalSystemVersion = entityComponentStore->GlobalSystemVersion;

            int allocatedIndex;
            var allocatedCount = AllocateIntoChunk(chunk, count, out allocatedIndex);
            entityComponentStore->AllocateEntities(archetype, chunk, allocatedIndex, allocatedCount, entities);
            InitializeComponents(chunk, allocatedIndex, allocatedCount);

            // Add Entities in Chunk. ChangeVersion:Yes OrderVersion:Yes
            chunk->SetAllChangeVersions(globalSystemVersion);
            chunk->SetOrderVersion(globalSystemVersion);
            entityComponentStore->IncrementComponentTypeOrderVersion(archetype);
        }

        public static void Remove(in EntityBatchInChunk batchInChunk)
        {
            var chunk = batchInChunk.Chunk;
            var count = batchInChunk.Count;
            var startIndex = batchInChunk.StartIndex;
            var archetype = chunk->Archetype;
            var entityComponentStore = archetype->EntityComponentStore;

            // Fill in moved component data from the end.
            var srcTailIndex = startIndex + count;
            var srcTailCount = chunk->Count - srcTailIndex;
            var fillCount = math.min(count, srcTailCount);

            if (fillCount > 0)
            {
                var fillStartIndex = chunk->Count - fillCount;

                Copy(chunk, fillStartIndex, chunk, startIndex, fillCount);

                RemoveFromEnabledBitsHierarchicalData(chunk, startIndex, count);
                CloneEnabledBits(chunk, fillStartIndex, chunk, startIndex, fillCount);

                var clearStartIndex = chunk->Count - count;
                ClearPaddingBits(chunk, clearStartIndex, count);

                var fillEntities = (Entity*)GetComponentDataRO(chunk, startIndex, 0);
                for (int i = 0; i < fillCount; i++)
                {
                    var entity = fillEntities[i];
                    entityComponentStore->SetEntityInChunk(entity, new EntityInChunk { Chunk = chunk, IndexInChunk = startIndex + i });
                }
            }
            else
            {
                // Need to clear bits for all of the entities we removed
                RemoveFromEnabledBitsHierarchicalData(chunk, startIndex, count);
                ClearPaddingBits(chunk, startIndex, count);
            }

            chunk->SetOrderVersion(entityComponentStore->GlobalSystemVersion);
            entityComponentStore->IncrementComponentTypeOrderVersion(archetype);
            entityComponentStore->ManagedChangesTracker.IncrementComponentOrderVersion(archetype, chunk->SharedComponentValues);

            int newChunkEntityCount = chunk->Count - count;
            SetChunkCount(chunk, newChunkEntityCount);
            if (fillCount > 0 && newChunkEntityCount != 0)
                // can't do this until the chunk count is updated and padding bits are clear
                UpdateChunkDisabledEntityCounts(chunk);
            archetype->EntityCount -= count;
        }

        /// <summary>
        /// Fix-up the chunk to refer to a different (but layout compatible) archetype.
        /// - Should only be called by Move(chunk)
        /// </summary>
        public static void ChangeArchetypeInPlace(Chunk* srcChunk, Archetype* dstArchetype, int* sharedComponentValues)
        {
            var srcArchetype = srcChunk->Archetype;
            var entityComponentStore = dstArchetype->EntityComponentStore;
            AssertAreLayoutCompatible(srcArchetype, dstArchetype);

            var fixupSharedComponentReferences =
                (srcArchetype->NumSharedComponents > 0) || (dstArchetype->NumSharedComponents > 0);
            if (fixupSharedComponentReferences)
            {
                int srcFirstShared = srcArchetype->FirstSharedComponent;
                int dstFirstShared = dstArchetype->FirstSharedComponent;
                int srcCount = srcArchetype->NumSharedComponents;
                int dstCount = dstArchetype->NumSharedComponents;

                int o = 0;
                int n = 0;

                for (; n < dstCount && o < srcCount;)
                {
                    var srcType = srcArchetype->Types[o + srcFirstShared].TypeIndex;
                    var dstType = dstArchetype->Types[n + dstFirstShared].TypeIndex;
                    if (srcType == dstType)
                    {
                        var srcSharedComponentDataIndex = srcChunk->SharedComponentValues[o];
                        var dstSharedComponentDataIndex = sharedComponentValues[n];
                        if (srcSharedComponentDataIndex != dstSharedComponentDataIndex)
                        {
                            if (EntityComponentStore.IsUnmanagedSharedComponentIndex(srcSharedComponentDataIndex))
                            {
                                entityComponentStore->RemoveSharedComponentReference_Unmanaged(srcSharedComponentDataIndex);
                            } else {
                                entityComponentStore->ManagedChangesTracker.RemoveReference(srcSharedComponentDataIndex);
                            }
                            if (EntityComponentStore.IsUnmanagedSharedComponentIndex(dstSharedComponentDataIndex))
                            {
                                entityComponentStore->AddSharedComponentReference_Unmanaged(dstSharedComponentDataIndex);
                            } else {
                                entityComponentStore->ManagedChangesTracker.AddReference(dstSharedComponentDataIndex);
                            }
                        }

                        n++;
                        o++;
                    }
                    else if (dstType > srcType) // removed from dstArchetype
                    {
                        var sharedComponentDataIndex = srcChunk->SharedComponentValues[o];
                        if (EntityComponentStore.IsUnmanagedSharedComponentIndex(sharedComponentDataIndex))
                        {
                            entityComponentStore->RemoveSharedComponentReference_Unmanaged(sharedComponentDataIndex);
                        } else {
                            entityComponentStore->ManagedChangesTracker.RemoveReference(sharedComponentDataIndex);
                        }
                        o++;
                    }
                    else // added to dstArchetype
                    {
                        var sharedComponentDataIndex = sharedComponentValues[n];
                        if (EntityComponentStore.IsUnmanagedSharedComponentIndex(sharedComponentDataIndex))
                        {
                            entityComponentStore->AddSharedComponentReference_Unmanaged(sharedComponentDataIndex);
                        } else {
                            entityComponentStore->ManagedChangesTracker.AddReference(sharedComponentDataIndex);
                        }
                        n++;
                    }
                }

                for (; n < dstCount; n++) // added to dstArchetype
                {
                    var sharedComponentDataIndex = sharedComponentValues[n];
                    if (EntityComponentStore.IsUnmanagedSharedComponentIndex(sharedComponentDataIndex))
                    {
                        entityComponentStore->AddSharedComponentReference_Unmanaged(sharedComponentDataIndex);
                    } else {
                        entityComponentStore->ManagedChangesTracker.AddReference(sharedComponentDataIndex);
                    }
                }

                for (; o < srcCount; o++) // removed from dstArchetype
                {
                    var sharedComponentDataIndex = srcChunk->SharedComponentValues[o];
                    if (EntityComponentStore.IsUnmanagedSharedComponentIndex(sharedComponentDataIndex))
                    {
                        entityComponentStore->RemoveSharedComponentReference_Unmanaged(sharedComponentDataIndex);
                    } else {
                        entityComponentStore->ManagedChangesTracker.RemoveReference(sharedComponentDataIndex);
                    }
                }
            }

            var count = srcChunk->Count;
            bool hasEmptySlots = count < srcChunk->Capacity;

            if (hasEmptySlots)
                srcArchetype->EmptySlotTrackingRemoveChunk(srcChunk);

            int chunkIndexInSrcArchetype = srcChunk->ListIndex;

            if (Hint.Likely(dstArchetype != srcArchetype))
            {
                //Change version is overriden below
                dstArchetype->AddToChunkList(srcChunk, sharedComponentValues, 0,
                    ref entityComponentStore->m_ChunkListChangesTracker);
                int chunkIndexInDstArchetype = srcChunk->ListIndex;

                // For unchanged components: Copy versions from src to dst archetype
                // For different components:
                //   - (srcArchetype->Chunks) Remove Component In-Place. ChangeVersion:No OrderVersion:No
                //   - (dstArchetype->Chunks) Add Component In-Place. ChangeVersion:Yes OrderVersion:No

                CloneChangeVersions(srcArchetype, chunkIndexInSrcArchetype, dstArchetype, chunkIndexInDstArchetype);

                // Since we're not getting a clean chunk, we need to initialize the new bits here
                InitializeBitsForNewChunk(dstArchetype, chunkIndexInDstArchetype);
                MoveEnabledBits(srcArchetype, chunkIndexInSrcArchetype, dstArchetype, chunkIndexInDstArchetype, count);

                srcChunk->ListIndex = chunkIndexInSrcArchetype;
                srcArchetype->RemoveFromChunkList(srcChunk, ref entityComponentStore->m_ChunkListChangesTracker);
                srcChunk->ListIndex = chunkIndexInDstArchetype;

                srcArchetype->EntityCount -= count;
                dstArchetype->EntityCount += count;
                entityComponentStore->SetArchetype(srcChunk, dstArchetype);

                // Also set the order version. Even though the ORDER hasn't changed, the archetype HAS, which must be tracked.
                // Note that srcChunk is now in dstArchetype!
                dstArchetype->Chunks.SetOrderVersion(srcChunk->ListIndex, entityComponentStore->GlobalSystemVersion);
            }
            else
            {
                // This path is used when setting the shared component value for an entire chunk.
                // We don't know which value changed at this point, so just copy them all.
                for (int i = 0, sharedComponentCount = srcArchetype->NumSharedComponents; i < sharedComponentCount; ++i)
                {
                    srcArchetype->Chunks.SetSharedComponentValue(i, chunkIndexInSrcArchetype, sharedComponentValues[i]);
                }
            }

            if (hasEmptySlots)
                dstArchetype->EmptySlotTrackingAddChunk(srcChunk);

            if (srcArchetype->MetaChunkArchetype != dstArchetype->MetaChunkArchetype)
            {
                if (srcArchetype->MetaChunkArchetype == null)
                {
                    entityComponentStore->CreateMetaEntityForChunk(srcChunk);
                }
                else if (dstArchetype->MetaChunkArchetype == null)
                {
                    entityComponentStore->DestroyMetaChunkEntity(srcChunk->metaChunkEntity);
                    srcChunk->metaChunkEntity = Entity.Null;
                }
                else
                {
                    var metaChunk = entityComponentStore->GetChunk(srcChunk->metaChunkEntity);
                    entityComponentStore->Move(srcChunk->metaChunkEntity, dstArchetype->MetaChunkArchetype, metaChunk->SharedComponentValues);
                }
            }
        }

        public static void MoveArchetype(Chunk* chunk, Archetype* dstArchetype, SharedComponentValues sharedComponentValues)
        {
            var srcArchetype = chunk->Archetype;
            var entityComponentStore = dstArchetype->EntityComponentStore;
            var globalSystemVersion = entityComponentStore->GlobalSystemVersion;

            var count = chunk->Count;
            bool hasEmptySlots = count < chunk->Capacity;

            if (hasEmptySlots)
                srcArchetype->EmptySlotTrackingRemoveChunk(chunk);

            int chunkIndexInSrcArchetype = chunk->ListIndex;

            dstArchetype->AddToChunkList(chunk, sharedComponentValues, globalSystemVersion, ref entityComponentStore->m_ChunkListChangesTracker);
            var chunkIndexInDstArchetype = chunk->ListIndex;

            // Since we're not getting a clean chunk, we need to initialize the new bits here
            InitializeBitsForNewChunk(dstArchetype, chunkIndexInDstArchetype);
            MoveEnabledBits(srcArchetype, chunkIndexInSrcArchetype, dstArchetype, chunkIndexInDstArchetype, count);

            chunk->ListIndex = chunkIndexInSrcArchetype;
            srcArchetype->RemoveFromChunkList(chunk, ref entityComponentStore->m_ChunkListChangesTracker);
            chunk->ListIndex = chunkIndexInDstArchetype;

            entityComponentStore->SetArchetype(chunk, dstArchetype);

            if (hasEmptySlots)
                dstArchetype->EmptySlotTrackingAddChunk(chunk);

            srcArchetype->EntityCount -= count;
            dstArchetype->EntityCount += count;

            entityComponentStore->IncrementComponentTypeOrderVersion(dstArchetype);
            chunk->SetOrderVersion(globalSystemVersion);
        }

        public static void CloneChangeVersions(Archetype* srcArchetype, int chunkIndexInSrcArchetype, Archetype* dstArchetype, int chunkIndexInDstArchetype, bool dstValidExistingVersions = false)
        {
            var dstTypes = dstArchetype->Types;
            var srcTypes = srcArchetype->Types;
            var dstGlobalSystemVersion = dstArchetype->EntityComponentStore->GlobalSystemVersion;
            var srcGlobalSystemVersion = srcArchetype->EntityComponentStore->GlobalSystemVersion;

            for (int isrcType = srcArchetype->TypesCount - 1, idstType = dstArchetype->TypesCount - 1;
                 idstType >= 0;
                 --idstType)
            {
                var dstType = dstTypes[idstType];
                while (srcTypes[isrcType] > dstType)
                    --isrcType;

                var version = dstGlobalSystemVersion;

                // select "newer" version relative to dst EntityComponentStore GlobalSystemVersion
                if (srcTypes[isrcType] == dstType)
                {
                    var srcVersion = srcArchetype->Chunks.GetChangeVersion(isrcType, chunkIndexInSrcArchetype);
                    if (dstValidExistingVersions)
                    {
                        var dstVersion = dstArchetype->Chunks.GetChangeVersion(idstType, chunkIndexInDstArchetype);

                        var srcVersionSinceChange = srcGlobalSystemVersion - srcVersion;
                        var dstVersionSinceChange = dstGlobalSystemVersion - dstVersion;

                        if (dstVersionSinceChange < srcVersionSinceChange)
                            version = dstVersion;
                        else
                            version = dstGlobalSystemVersion - srcVersionSinceChange;
                    }
                    else
                    {
                        version = srcVersion;
                    }
                }

                dstArchetype->Chunks.SetChangeVersion(idstType, chunkIndexInDstArchetype, version);
            }
        }

        public static void AllocateClone(Chunk* chunk, Entity* entities, int count, Entity srcEntity)
        {
            var archetype = chunk->Archetype;
            var entityComponentStore = archetype->EntityComponentStore;
            var globalSystemVersion = entityComponentStore->GlobalSystemVersion;
            var src = entityComponentStore->GetEntityInChunk(srcEntity);

            int allocatedIndex;
            var allocatedCount = AllocateIntoChunk(chunk, count, out allocatedIndex);
            entityComponentStore->AllocateEntities(archetype, chunk, allocatedIndex, allocatedCount, entities);
            ReplicateComponents(src.Chunk, src.IndexInChunk, chunk, allocatedIndex, allocatedCount);

            // Add Entities in Chunk. ChangeVersion:Yes OrderVersion:Yes
            chunk->SetAllChangeVersions(globalSystemVersion);
            chunk->SetOrderVersion(globalSystemVersion);

#if !DOTS_DISABLE_DEBUG_NAMES
            for (var i = 0; i < allocatedCount; ++i)
                entityComponentStore->CopyName(entities[i], srcEntity);
#endif

            entityComponentStore->ManagedChangesTracker.IncrementComponentOrderVersion(archetype, chunk->SharedComponentValues);
            entityComponentStore->IncrementComponentTypeOrderVersion(archetype);
        }

        public static void Deallocate(Chunk* chunk)
        {
            Deallocate(new EntityBatchInChunk {Chunk = chunk, StartIndex = 0, Count = chunk->Count});
        }

        public static void Deallocate(in EntityBatchInChunk batch)
        {
            var chunk = batch.Chunk;
            var archetype = chunk->Archetype;
            var entityComponentStore = archetype->EntityComponentStore;
            var globalSystemVersion = entityComponentStore->GlobalSystemVersion;
            var startIndex = batch.StartIndex;
            var count = batch.Count;

            entityComponentStore->DeallocateDataEntitiesInChunk(chunk, startIndex, count);
            entityComponentStore->ManagedChangesTracker.IncrementComponentOrderVersion(archetype, chunk->SharedComponentValues);

            // Remove Entities in Chunk. ChangeVersion:No OrderVersion:Yes
            chunk->SetOrderVersion(globalSystemVersion);
            entityComponentStore->IncrementComponentTypeOrderVersion(archetype);

            chunk->Archetype->EntityCount -= count;
            int newChunkEntityCount = chunk->Count - count;
            SetChunkCount(chunk, newChunkEntityCount);
            // Can't update these counts until the chunk count is correct and the padding bits are clear
            if (newChunkEntityCount != 0)
                UpdateChunkDisabledEntityCounts(chunk);
        }

        public static void Clone(in EntityBatchInChunk srcBatch, Chunk* dstChunk)
        {
            var srcChunk = srcBatch.Chunk;
            var srcChunkIndex = srcBatch.StartIndex;
            var srcCount = srcBatch.Count;
            var dstArchetype = dstChunk->Archetype;
            var srcArchetype = srcChunk->Archetype;
            var entityComponentStore = dstArchetype->EntityComponentStore;
            var globalSystemVersion = entityComponentStore->GlobalSystemVersion;

            // Note (srcArchetype == dstArchetype) is valid
            // Archetypes can the the same, but chunks still differ because filter is different (e.g. shared component)

            int dstChunkIndex;
            var dstValidExistingVersions = dstChunk->Count != 0;
            var dstCount = AllocateIntoChunk(dstChunk, srcCount, out dstChunkIndex);
            Assert.IsTrue(dstCount == srcCount);

            Convert(srcChunk, srcChunkIndex, dstChunk, dstChunkIndex, dstCount);

            var dstEntities = (Entity*)ChunkDataUtility.GetComponentDataRO(dstChunk, dstChunkIndex, 0);
            for (int i = 0; i < dstCount; i++)
            {
                var entity = dstEntities[i];

                entityComponentStore->SetArchetype(entity, dstArchetype);
                entityComponentStore->SetEntityInChunk(entity, new EntityInChunk { Chunk = dstChunk, IndexInChunk = dstChunkIndex + i });
            }

            CloneChangeVersions(srcArchetype, srcChunk->ListIndex, dstArchetype, dstChunk->ListIndex, dstValidExistingVersions);
            CloneEnabledBits(srcChunk, srcChunkIndex, dstChunk, dstChunkIndex, dstCount);
            // Can't update these counts until the chunk count is up to date and padding bits are clear
            UpdateChunkDisabledEntityCounts(dstChunk);

            dstChunk->SetOrderVersion(globalSystemVersion);
            entityComponentStore->IncrementComponentTypeOrderVersion(dstArchetype);
            entityComponentStore->ManagedChangesTracker.IncrementComponentOrderVersion(dstArchetype, dstChunk->SharedComponentValues);

            // Cannot DestroyEntities unless CleanupComplete on the entity chunk.
            if (dstChunk->Archetype->CleanupComplete)
                entityComponentStore->DestroyEntities(dstEntities, dstCount);
        }

        static void ReplicateComponents(Chunk* srcChunk, int srcIndex, Chunk* dstChunk, int dstBaseIndex, int count)
        {
            var srcArchetype        = srcChunk->Archetype;
            var srcBuffer           = srcChunk->Buffer;
            var dstBuffer           = dstChunk->Buffer;
            var dstArchetype        = dstChunk->Archetype;
            var srcOffsets          = srcArchetype->Offsets;
            var srcSizeOfs          = srcArchetype->SizeOfs;
            var srcBufferCapacities = srcArchetype->BufferCapacities;
            var srcTypes            = srcArchetype->Types;
            var dstTypes            = dstArchetype->Types;
            var dstOffsets          = dstArchetype->Offsets;
            var dstTypeIndex        = 1;

            var nativeComponentsEnd = srcArchetype->NativeComponentsEnd;
            for (var srcTypeIndex = 1; srcTypeIndex != nativeComponentsEnd; srcTypeIndex++)
            {
                var srcType = srcTypes[srcTypeIndex];
                var dstType = dstTypes[dstTypeIndex];
                // Type does not exist in destination. Skip it.
                if (srcType.TypeIndex != dstType.TypeIndex)
                    continue;
                var srcSizeOf = srcSizeOfs[srcTypeIndex];
                var src = srcBuffer + (srcOffsets[srcTypeIndex] + srcSizeOf * srcIndex);
                var dst = dstBuffer + (dstOffsets[dstTypeIndex] + srcSizeOf * dstBaseIndex);

                UnsafeUtility.MemCpyReplicate(dst, src, srcSizeOf, count);
                dstTypeIndex++;
            }

            dstTypeIndex = dstArchetype->FirstBufferComponent;
            var bufferComponentsEnd = srcArchetype->BufferComponentsEnd;
            for (var srcTypeIndex = srcArchetype->FirstBufferComponent; srcTypeIndex != bufferComponentsEnd; srcTypeIndex++)
            {
                var srcType = srcTypes[srcTypeIndex];
                var dstType = dstTypes[dstTypeIndex];
                // Type does not exist in destination. Skip it.
                if (srcType.TypeIndex != dstType.TypeIndex)
                    continue;
                var srcSizeOf = srcSizeOfs[srcTypeIndex];
                var src = srcBuffer + (srcOffsets[srcTypeIndex] + srcSizeOf * srcIndex);
                var dst = dstBuffer + (dstOffsets[dstTypeIndex] + srcSizeOf * dstBaseIndex);

                var srcBufferCapacity = srcBufferCapacities[srcTypeIndex];
                var alignment = 8; // TODO: Need a way to compute proper alignment for arbitrary non-generic types in TypeManager
                var elementSize = TypeManager.GetTypeInfo(srcType.TypeIndex).ElementSize;
                for (int i = 0; i < count; ++i)
                {
                    BufferHeader* srcHdr = (BufferHeader*)src;
                    BufferHeader* dstHdr = (BufferHeader*)dst;
                    BufferHeader.Initialize(dstHdr, srcBufferCapacity);
                    BufferHeader.Assign(dstHdr, BufferHeader.GetElementPointer(srcHdr), srcHdr->Length, elementSize, alignment, false, 0);

                    dst += srcSizeOf;
                }

                dstTypeIndex++;
            }

            // Copy enabled bits from source entity to the instantiated entities
            ReplicateEnabledBits(srcChunk, srcIndex, dstChunk, dstBaseIndex, count);

            if (dstArchetype->NumManagedComponents > 0)
            {
                ReplicateManagedComponents(srcChunk, srcIndex, dstChunk, dstBaseIndex, count);
            }
        }

        static void ReplicateManagedComponents(Chunk* srcChunk, int srcIndex, Chunk* dstChunk, int dstBaseIndex, int count)
        {
            var dstArchetype = dstChunk->Archetype;
            var entityComponentStore = dstArchetype->EntityComponentStore;
            var srcArchetype = srcChunk->Archetype;
            var srcTypes = srcArchetype->Types;
            var dstTypes = dstArchetype->Types;
            var srcOffsets          = srcArchetype->Offsets;
            var dstOffsets          = dstArchetype->Offsets;
            int componentCount = dstArchetype->NumManagedComponents;

            int nonNullManagedComponents = 0;
            int nonNullCompanionComponents = 0;
            var componentIndices = stackalloc int[componentCount];
            var componentDstArrayStart = stackalloc IntPtr[componentCount];

            var firstDstManagedComponent = dstArchetype->FirstManagedComponent;
            var dstTypeIndex = firstDstManagedComponent;
            var managedComponentsEnd = srcArchetype->ManagedComponentsEnd;
            var srcBaseAddr = srcChunk->Buffer + sizeof(int) * srcIndex;
            var dstBaseAddr = dstChunk->Buffer + sizeof(int) * dstBaseIndex;

            bool hasCompanionComponents = dstArchetype->HasCompanionComponents;

            for (var srcTypeIndex = srcArchetype->FirstManagedComponent; srcTypeIndex != managedComponentsEnd; srcTypeIndex++)
            {
                var srcType = srcTypes[srcTypeIndex];
                var dstType = dstTypes[dstTypeIndex];
                // Type does not exist in destination. Skip it.
                if (srcType.TypeIndex != dstType.TypeIndex)
                    continue;
                int srcManagedComponentIndex = *(int*)(srcBaseAddr + srcOffsets[srcTypeIndex]);
                var dstArrayStart = dstBaseAddr + dstOffsets[dstTypeIndex];

                if (srcManagedComponentIndex == 0)
                {
                    UnsafeUtility.MemClear(dstArrayStart, sizeof(int) * count);
                }
                else
                {
                    if (hasCompanionComponents && TypeManager.GetTypeInfo(srcType.TypeIndex).Category == TypeManager.TypeCategory.UnityEngineObject)
                    {
                        //Hybrid component, put at end of array
                        var index = componentCount - nonNullCompanionComponents - 1;
                        componentIndices[index] = srcManagedComponentIndex;
                        componentDstArrayStart[index] = (IntPtr)dstArrayStart;
                        ++nonNullCompanionComponents;
                    }
                    else
                    {
                        componentIndices[nonNullManagedComponents] = srcManagedComponentIndex;
                        componentDstArrayStart[nonNullManagedComponents] = (IntPtr)dstArrayStart;
                        ++nonNullManagedComponents;
                    }
                }

                dstTypeIndex++;
            }

            entityComponentStore->ReserveManagedComponentIndices(count * (nonNullManagedComponents + nonNullCompanionComponents));
            entityComponentStore->ManagedChangesTracker.CloneManagedComponentBegin(componentIndices, nonNullManagedComponents, count);
            for (int c = 0; c < nonNullManagedComponents; ++c)
            {
                var dst = (int*)(componentDstArrayStart[c]);
                entityComponentStore->AllocateManagedComponentIndices(dst, count);
                entityComponentStore->ManagedChangesTracker.CloneManagedComponentAddDstIndices(dst, count);
            }

            if (hasCompanionComponents)
            {
                var companionLinkIndexInTypeArray = GetIndexInTypeArray(dstArchetype, ManagedComponentStore.CompanionLinkTypeIndex);
                var companionLinkIndices = (companionLinkIndexInTypeArray == -1) ? null : (int*)(dstBaseAddr + dstOffsets[companionLinkIndexInTypeArray]);

                var dstEntities = (Entity*)dstChunk->Buffer + dstBaseIndex;
                entityComponentStore->ManagedChangesTracker.CloneCompanionComponentBegin(componentIndices + componentCount - nonNullCompanionComponents, nonNullCompanionComponents, dstEntities, count, companionLinkIndices);
                for (int c = componentCount - nonNullCompanionComponents; c < componentCount; ++c)
                {
                    var dst = (int*)(componentDstArrayStart[c]);
                    entityComponentStore->AllocateManagedComponentIndices(dst, count);
                    entityComponentStore->ManagedChangesTracker.CloneCompanionComponentAddDstIndices(dst, count);
                }
            }
        }

        public static byte* GetChunkBuffer(Chunk* chunk)
        {
            return chunk->Buffer;
        }

        public static void ClearMissingReferences(Chunk* chunk)
        {
            var archetype = chunk->Archetype;
            var entityComponentStore = archetype->EntityComponentStore;
            var globalSystemVersion = entityComponentStore->GlobalSystemVersion;
            var typesCount = archetype->TypesCount;
            var entityCount = chunk->Count;

            for (var typeIndexInArchetype = 1; typeIndexInArchetype < typesCount; typeIndexInArchetype++)
            {
                var componentTypeInArchetype = archetype->Types[typeIndexInArchetype];

                if (!componentTypeInArchetype.HasEntityReferences || componentTypeInArchetype.IsSharedComponent ||
                    componentTypeInArchetype.IsZeroSized)
                {
                    continue;
                }

                ref readonly var typeInfo = ref entityComponentStore->GetTypeInfo(componentTypeInArchetype.TypeIndex);
                var typeInChunkPtr = GetChunkBuffer(chunk) + archetype->Offsets[typeIndexInArchetype];
                var typeSizeOf = archetype->SizeOfs[typeIndexInArchetype];

                var changed = false;

                if (componentTypeInArchetype.IsBuffer)
                {
                    for (var entityIndexInChunk = 0; entityIndexInChunk < entityCount; entityIndexInChunk++)
                    {
                        var componentDataPtr = typeInChunkPtr + typeSizeOf * entityIndexInChunk;
                        var bufferHeader = (BufferHeader*)componentDataPtr;
                        var bufferLength = bufferHeader->Length;
                        var bufferPtr = BufferHeader.GetElementPointer(bufferHeader);
                        changed |= ClearEntityReferences(entityComponentStore, typeInfo, bufferPtr, bufferLength);
                    }
                }
                else
                {
                    for (var entityIndexInChunk = 0; entityIndexInChunk < entityCount; entityIndexInChunk++)
                    {
                        var componentDataPtr = typeInChunkPtr + typeSizeOf * entityIndexInChunk;
                        changed |= ClearEntityReferences(entityComponentStore, typeInfo, componentDataPtr, 1);
                    }
                }

                if (changed)
                {
                    chunk->SetChangeVersion(typeIndexInArchetype, globalSystemVersion);
                }
            }
        }

        static bool ClearEntityReferences(EntityComponentStore* entityComponentStore, in TypeManager.TypeInfo typeInfo, byte* address, int elementCount)
        {
            var changed = false;

            var offsets = entityComponentStore->GetEntityOffsets(typeInfo);

            for (var elementIndex = 0; elementIndex < elementCount; elementIndex++)
            {
                var elementPtr = address + typeInfo.ElementSize * elementIndex;

                for (var offsetIndex = 0; offsetIndex < typeInfo.EntityOffsetCount; offsetIndex++)
                {
                    var offset = offsets[offsetIndex].Offset;

                    if (entityComponentStore->Exists(*(Entity*)(elementPtr + offset)))
                        continue;

                    *(Entity*)(elementPtr + offset) = Entity.Null;
                    changed = true;
                }
            }

            return changed;
        }

        public static Entity GetEntityFromEntityInChunk(EntityInChunk entityInChunk)
        {
            var chunk = entityInChunk.Chunk;
            var archetype = chunk->Archetype;
            var buffer = GetChunkBuffer(chunk) + archetype->Offsets[0] + entityInChunk.IndexInChunk * archetype->SizeOfs[0];
            return *(Entity*)buffer;
        }

        public static void AddExistingChunk(Chunk* chunk, int* sharedComponentIndices, byte* enabledBitsValuesForChunk, int* perComponentDisabledBitCount)
        {
            var archetype = chunk->Archetype;
            var entityComponentStore = archetype->EntityComponentStore;
            var globalSystemVersion = entityComponentStore->GlobalSystemVersion;
            archetype->AddToChunkList(chunk, sharedComponentIndices, globalSystemVersion, ref entityComponentStore->m_ChunkListChangesTracker);
            archetype->EntityCount += chunk->Count;

            InitializeBitsForNewChunk(archetype, chunk->ListIndex);
            Assert.IsTrue(chunk->ListIndex >= 0 && chunk->ListIndex < archetype->Chunks.Count);
            archetype->Chunks.SetEnabledBitsAndHierarchicalData(chunk->ListIndex, enabledBitsValuesForChunk, perComponentDisabledBitCount);

            for (var i = 0; i < archetype->NumSharedComponents; ++i)
            {
                var sharedComponentIndex = sharedComponentIndices[i];
                if (EntityComponentStore.IsUnmanagedSharedComponentIndex(sharedComponentIndex))
                {
                    entityComponentStore->AddSharedComponentReference_Unmanaged(sharedComponentIndex);
                } else {
                    entityComponentStore->ManagedChangesTracker.AddReference(sharedComponentIndex);
                }

            }

            if (chunk->Count < chunk->Capacity)
                archetype->EmptySlotTrackingAddChunk(chunk);

            entityComponentStore->AddExistingEntitiesInChunk(chunk);
        }

        public static void AddEmptyChunk(Archetype* archetype, Chunk* chunk, SharedComponentValues sharedComponentValues)
        {
            var entityComponentStore = archetype->EntityComponentStore;
            var globalSystemVersion = entityComponentStore->GlobalSystemVersion;

            chunk->Archetype = archetype;
            chunk->Count = 0;
            chunk->Capacity = archetype->ChunkCapacity;
            chunk->SequenceNumber = entityComponentStore->AllocateSequenceNumber();
            chunk->metaChunkEntity = Entity.Null;

            var numSharedComponents = archetype->NumSharedComponents;

            if (numSharedComponents > 0)
            {
                for (var i = 0; i < archetype->NumSharedComponents; ++i)
                {
                    var sharedComponentIndex = sharedComponentValues[i];
                    if (EntityComponentStore.IsUnmanagedSharedComponentIndex(sharedComponentIndex))
                    {
                        entityComponentStore->AddSharedComponentReference_Unmanaged(sharedComponentIndex);
                    } else {
                        entityComponentStore->ManagedChangesTracker.AddReference(sharedComponentIndex);
                    }
                }
            }

            archetype->AddToChunkList(chunk, sharedComponentValues, globalSystemVersion, ref entityComponentStore->m_ChunkListChangesTracker);

            InitializeBitsForNewChunk(archetype, chunk->ListIndex);

            Assert.IsTrue(archetype->Chunks.Count != 0);

            // Chunk can't be locked at at construction time
            archetype->EmptySlotTrackingAddChunk(chunk);

            if (numSharedComponents == 0)
            {
                Assert.IsTrue(archetype->ChunksWithEmptySlots.Length != 0);
            }
            else
            {
                Assert.IsTrue(archetype->FreeChunksBySharedComponents.TryGet(chunk->SharedComponentValues,
                    archetype->NumSharedComponents) != null);
            }

            chunk->Flags = 0;
        }
    }
}
