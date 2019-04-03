#if !UNITY_CSHARP_TINY
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Entities.Serialization
{
    public static class SerializeUtility
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct BufferPatchRecord
        {
            public int ChunkOffset;
            public int AllocSizeBytes;
        }

        public static int CurrentFileFormatVersion = 15;

        public static unsafe void DeserializeWorld(ExclusiveEntityTransaction manager, BinaryReader reader, int numSharedComponents)
        {
            if (manager.ArchetypeManager.CountEntities() != 0)
            {
                throw new ArgumentException(
                    $"DeserializeWorld can only be used on completely empty EntityManager. Please create a new empty World and use EntityManager.MoveEntitiesFrom to move the loaded entities into the destination world instead.");
            }
            int storedVersion = reader.ReadInt();
            if (storedVersion != CurrentFileFormatVersion)
            {
                throw new ArgumentException(
                    $"Attempting to read a entity scene stored in an old file format version (stored version : {storedVersion}, current version : {CurrentFileFormatVersion})");
            }

            var types = ReadTypeArray(reader);
            int totalEntityCount;
            var typeCount = new NativeArray<int>(types.Length, Allocator.Temp);
            var archetypes = ReadArchetypes(reader, types, manager, out totalEntityCount, typeCount);

            int sharedComponentArraysLength = reader.ReadInt();
            var sharedComponentArrays = new NativeArray<int>(sharedComponentArraysLength, Allocator.Temp);
            reader.ReadArray(sharedComponentArrays, sharedComponentArraysLength);

            manager.AllocateConsecutiveEntitiesForLoading(totalEntityCount);

            int totalChunkCount = reader.ReadInt();
            var chunksWithMetaChunkEntities = new NativeList<ArchetypeChunk>(totalChunkCount, Allocator.Temp);

            int sharedComponentArraysIndex = 0;
            for (int i = 0; i < totalChunkCount; ++i)
            {
                var chunk = (Chunk*) UnsafeUtility.Malloc(Chunk.kChunkSize, 64, Allocator.Persistent);
                reader.ReadBytes(chunk, Chunk.kChunkSize);

                chunk->Archetype = archetypes[(int)chunk->Archetype].Archetype;
                var numSharedComponentsInArchetype = chunk->Archetype->NumSharedComponents;
                int* sharedComponentValueArray = (int*)sharedComponentArrays.GetUnsafePtr() + sharedComponentArraysIndex;

                for (int j = 0; j < numSharedComponentsInArchetype; ++j)
                {
                    // The shared component 0 is not part of the array, so an index equal to the array size is valid.
                    if (sharedComponentValueArray[j] > numSharedComponents)
                    {
                        throw new ArgumentException(
                            $"Archetype uses shared component at index {sharedComponentValueArray[j]} but only {numSharedComponents} are available, check if the shared scene has been properly loaded.");
                    }
                }

                var remapedSharedComponentValues = stackalloc int[chunk->Archetype->NumSharedComponents];
                RemapSharedComponentIndices(remapedSharedComponentValues, chunk->Archetype, sharedComponentValueArray);

                sharedComponentArraysIndex += numSharedComponentsInArchetype;

                // Allocate additional heap memory for buffers that have overflown into the heap, and read their data.
                int bufferAllocationCount = reader.ReadInt();
                if (bufferAllocationCount > 0)
                {
                    var bufferPatches = new NativeArray<BufferPatchRecord>(bufferAllocationCount, Allocator.Temp);
                    reader.ReadArray(bufferPatches, bufferPatches.Length);

                    // TODO: PERF: Batch malloc interface.
                    for (int pi = 0; pi < bufferAllocationCount; ++pi)
                    {
                        var target = (BufferHeader*)OffsetFromPointer(chunk->Buffer, bufferPatches[pi].ChunkOffset);

                        // TODO: Alignment
                        target->Pointer = (byte*) UnsafeUtility.Malloc(bufferPatches[pi].AllocSizeBytes, 8, Allocator.Persistent);

                        reader.ReadBytes(target->Pointer, bufferPatches[pi].AllocSizeBytes);
                    }

                    bufferPatches.Dispose();
                }

                manager.AddExistingChunk(chunk, remapedSharedComponentValues);

                if (chunk->metaChunkEntity != Entity.Null)
                {
                    chunksWithMetaChunkEntities.Add(new ArchetypeChunk{ m_Chunk = chunk});
                }
            }

            for (int i = 0; i < chunksWithMetaChunkEntities.Length; ++i)
            {
                var chunk = chunksWithMetaChunkEntities[i].m_Chunk;
                var archetype = chunk->Archetype;
                manager.SetComponentData(chunk->metaChunkEntity, new ChunkHeader{chunk = chunk});
            }

            chunksWithMetaChunkEntities.Dispose();
            sharedComponentArrays.Dispose();
            archetypes.Dispose();
            types.Dispose();
            typeCount.Dispose();
        }

        private static unsafe NativeArray<EntityArchetype> ReadArchetypes(BinaryReader reader, NativeArray<int> types, ExclusiveEntityTransaction entityManager,
            out int totalEntityCount, NativeArray<int> typeCount)
        {
            int archetypeCount = reader.ReadInt();
            var archetypes = new NativeArray<EntityArchetype>(archetypeCount, Allocator.Temp);
            totalEntityCount = 0;
            var tempComponentTypes = new NativeList<ComponentType>(Allocator.Temp);
            for (int i = 0; i < archetypeCount; ++i)
            {
                var archetypeEntityCount = reader.ReadInt();
                totalEntityCount += archetypeEntityCount;
                int archetypeComponentTypeCount = reader.ReadInt();
                tempComponentTypes.Clear();
                for (int iType = 0; iType < archetypeComponentTypeCount; ++iType)
                {
                    int typeIndexInFile = reader.ReadInt();
                    int typeIndexInFileWithoutFlags = typeIndexInFile & TypeManager.ClearFlagsMask;
                    int typeIndex = types[typeIndexInFileWithoutFlags];
                    if (TypeManager.IsChunkComponent(typeIndexInFile))
                        typeIndex = TypeManager.MakeChunkComponentTypeIndex(typeIndex);

                    typeCount[typeIndexInFileWithoutFlags] += archetypeEntityCount;
                    tempComponentTypes.Add(ComponentType.FromTypeIndex(typeIndex));
                }

                archetypes[i] = entityManager.CreateArchetype((ComponentType*) tempComponentTypes.GetUnsafePtr(),
                    tempComponentTypes.Length);
            }

            tempComponentTypes.Dispose();
            return archetypes;
        }

        private static NativeArray<int> ReadTypeArray(BinaryReader reader)
        {
            int typeCount = reader.ReadInt();
            var typeHashBuffer = new NativeArray<ulong>(typeCount, Allocator.Temp);

            reader.ReadArray(typeHashBuffer, typeCount);

            int nameBufferSize = reader.ReadInt();
            var nameBuffer = new NativeArray<byte>(nameBufferSize, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            reader.ReadBytes(nameBuffer, nameBufferSize);
            var types = new NativeArray<int>(typeCount, Allocator.Temp);
            int offset = 0;
            for (int i = 0; i < typeCount; ++i)
            {
                string typeName = StringFromNativeBytes(nameBuffer, offset);
                var type = Type.GetType(typeName);

                if (type == null)
                    throw new ArgumentException($"Type no longer exists: '{typeName}'");

                types[i] = TypeManager.GetTypeIndex(type);
                if (types[i] == 0)
                    throw new ArgumentException("Unknown type '" + typeName + "'");

                if (typeHashBuffer[i] != TypeManager.GetTypeInfo(types[i]).StableTypeHash)
                    throw new ArgumentException($"Type layout has changed: '{type.Name}'");

                offset += typeName.Length + 1;
            }

            nameBuffer.Dispose();
            typeHashBuffer.Dispose();
            return types;
        }

        internal static unsafe void GetAllArchetypes(ArchetypeManager archetypeManager, out Dictionary<EntityArchetype, int> archetypeToIndex, out EntityArchetype[] archetypeArray)
        {
            var archetypeList = new List<EntityArchetype>();
            for (var i = archetypeManager.m_Archetypes.Count - 1; i >= 0; --i)
            {
                var archetype = archetypeManager.m_Archetypes.p[i];
                if (archetype->EntityCount >= 0)
                    archetypeList.Add(new EntityArchetype{Archetype = archetype});
            }
            //todo: sort archetypes to get deterministic indices
            archetypeToIndex = new Dictionary<EntityArchetype, int>();
            for (int i = 0; i < archetypeList.Count; ++i)
            {
                archetypeToIndex.Add(archetypeList[i],i);
            }

            archetypeArray = archetypeList.ToArray();
        }

        public static unsafe void SerializeWorld(EntityManager entityManager, BinaryWriter writer, out int[] sharedComponentsToSerialize)
        {
            var entityRemapInfos = new NativeArray<EntityRemapUtility.EntityRemapInfo>(entityManager.EntityCapacity, Allocator.Temp);
            SerializeWorld(entityManager, writer, out sharedComponentsToSerialize, entityRemapInfos);
            entityRemapInfos.Dispose();
        }

        public static unsafe void SerializeWorld(EntityManager entityManager, BinaryWriter writer, out int[] sharedComponentsToSerialize, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapInfos)
        {
            writer.Write(CurrentFileFormatVersion);
            var archetypeManager = entityManager.ArchetypeManager;

            Dictionary<EntityArchetype, int> archetypeToIndex;
            EntityArchetype[] archetypeArray;
            GetAllArchetypes(archetypeManager, out archetypeToIndex, out archetypeArray);

            var typeindices = new HashSet<int>();
            foreach (var archetype in archetypeArray)
            {
                for (int iType = 0; iType < archetype.Archetype->TypesCount; ++iType)
                {
                    typeindices.Add(archetype.Archetype->Types[iType].TypeIndex & TypeManager.ClearFlagsMask);
                }
            }

            var typeArray = typeindices.Select(index =>
            {
                var type = TypeManager.GetType(index);
                var name = TypeManager.GetType(index).AssemblyQualifiedName;
                var hash = TypeManager.GetTypeInfo(index).StableTypeHash;
                return new
                {
                    index,
                    type,
                    name,
                    hash,
                    utf8Name = Encoding.UTF8.GetBytes(name)
                };
            }).OrderBy(t => t.name).ToArray();

            int typeNameBufferSize = typeArray.Sum(t => t.utf8Name.Length + 1);
            writer.Write(typeArray.Length);
            foreach (var n in typeArray)
            {
                writer.Write(n.hash);
            }

            writer.Write(typeNameBufferSize);
            foreach(var n in typeArray)
            {
                writer.Write(n.utf8Name);
                writer.Write((byte)0);
            }

            var typeIndexMap = new Dictionary<int, int>();
            for (int i = 0; i < typeArray.Length; ++i)
            {
                typeIndexMap[typeArray[i].index] = i;
            }

            WriteArchetypes(writer, archetypeArray, typeIndexMap);
            var sharedComponentMapping = GatherSharedComponents(archetypeArray, out var sharedComponentArraysTotalCount);
            var sharedComponentArrays = new NativeArray<int>(sharedComponentArraysTotalCount, Allocator.Temp);
            FillSharedComponentArrays(sharedComponentArrays, archetypeArray, sharedComponentMapping);
            writer.Write(sharedComponentArrays.Length);
            writer.WriteArray(sharedComponentArrays);
            sharedComponentArrays.Dispose();

            //TODO: ensure chunks are defragged?

            var bufferPatches = new NativeList<BufferPatchRecord>(128, Allocator.Temp);
            var totalChunkCount = GenerateRemapInfo(entityManager, archetypeArray, entityRemapInfos);

            writer.Write(totalChunkCount);

            var tempChunk = (Chunk*)UnsafeUtility.Malloc(Chunk.kChunkSize, 16, Allocator.Temp);

            for(int archetypeIndex = 0; archetypeIndex < archetypeArray.Length; ++archetypeIndex)
            {
                var archetype = archetypeArray[archetypeIndex].Archetype;
                for (var ci = 0; ci < archetype->Chunks.Count; ++ci)
                {
                    var chunk = archetype->Chunks.p[ci];
                    bufferPatches.Clear();

                    UnsafeUtility.MemCpy(tempChunk, chunk, Chunk.kChunkSize);
                    tempChunk->metaChunkEntity = EntityRemapUtility.RemapEntity(ref entityRemapInfos, tempChunk->metaChunkEntity);

                    // Prevent patching from touching buffers allocated memory
                    BufferHeader.PatchAfterCloningChunk(tempChunk);

                    byte* tempChunkBuffer = tempChunk->Buffer;
                    EntityRemapUtility.PatchEntities(archetype->ScalarEntityPatches, archetype->ScalarEntityPatchCount, archetype->BufferEntityPatches, archetype->BufferEntityPatchCount, tempChunkBuffer, tempChunk->Count, ref entityRemapInfos);

                    // Find all buffer pointer locations and work out how much memory the deserializer must allocate on load.
                    for (int ti = 0; ti < archetype->TypesCount; ++ti)
                    {
                        int index = archetype->TypeMemoryOrder[ti];

                        if (!archetype->Types[index].IsBuffer)
                            continue;

                        int subArrayOffset = archetype->Offsets[index];
                        BufferHeader* header = (BufferHeader*) OffsetFromPointer(tempChunkBuffer, subArrayOffset);
                        int stride = archetype->SizeOfs[index];
                        int count = chunk->Count;
                        var ct = TypeManager.GetTypeInfo(archetype->Types[index].TypeIndex);

                        for (int bi = 0; bi < count; ++bi)
                        {
                            if (header->Pointer != null)
                            {
                                // TODO: Find a way to null header->Pointer so it doesn't get written
                                bufferPatches.Add(new BufferPatchRecord
                                {
                                    ChunkOffset = (int)(((byte*)header) - (byte*)tempChunkBuffer),
                                    AllocSizeBytes = ct.ElementSize * header->Capacity,
                                });
                            }
                            header = (BufferHeader*)OffsetFromPointer(header, stride);
                        }
                    }

                    ClearChunkHeaderComponents(tempChunk);
                    ChunkDataUtility.MemsetUnusedChunkData(tempChunk, 0);
                    tempChunk->Archetype = (Archetype*) archetypeIndex;

                    if (archetype->NumManagedArrays != 0)
                    {
                        throw new ArgumentException("Serialization of GameObject components is not supported for pure entity scenes");
                    }

                    writer.WriteBytes(tempChunk, Chunk.kChunkSize);

                    writer.Write(bufferPatches.Length);

                    if (bufferPatches.Length > 0)
                    {
                        writer.WriteList(bufferPatches);

                        // Write heap backed data for each required patch.
                        // TODO: PERF: Investigate static-only deserialization could manage one block and mark in pointers somehow that they are not indiviual
                        for (int i = 0; i < bufferPatches.Length; ++i)
                        {
                            var patch = bufferPatches[i];
                            var header = (BufferHeader*)OffsetFromPointer(tempChunk->Buffer, patch.ChunkOffset);
                            writer.WriteBytes(header->Pointer, patch.AllocSizeBytes);
                            BufferHeader.Destroy(header);
                        }
                    }
                }
            }

            bufferPatches.Dispose();
            UnsafeUtility.Free(tempChunk, Allocator.Temp);

            sharedComponentsToSerialize = new int[sharedComponentMapping.Count-1];

            foreach (var i in sharedComponentMapping)
                if(i.Key != 0)
                    sharedComponentsToSerialize[i.Value - 1] = i.Key;
        }

        static unsafe void FillSharedComponentIndexRemap(int* remapArray, Archetype* archetype)
        {
            int i = 0;
            for (int iType = 1; iType < archetype->TypesCount; ++iType)
            {
                int orderedIndex = archetype->TypeMemoryOrder[iType] - archetype->FirstSharedComponent;
                if (0 <= orderedIndex && orderedIndex < archetype->NumSharedComponents)
                    remapArray[i++] = orderedIndex;
            }
        }

        static unsafe void RemapSharedComponentIndices(int* destValues, Archetype* archetype, int* sourceValues)
        {
            int i = 0;
            for (int iType = 1; iType < archetype->TypesCount; ++iType)
            {
                int orderedIndex = archetype->TypeMemoryOrder[iType] - archetype->FirstSharedComponent;
                if (0 <= orderedIndex && orderedIndex < archetype->NumSharedComponents)
                    destValues[orderedIndex] = sourceValues[i++];
            }
        }

        private static unsafe void FillSharedComponentArrays(NativeArray<int> sharedComponentArrays, EntityArchetype[] archetypeArray, Dictionary<int, int> sharedComponentMapping)
        {
            int index = 0;
            for (int iArchetype = 0; iArchetype < archetypeArray.Length; ++iArchetype)
            {
                var archetype = archetypeArray[iArchetype].Archetype;
                int numSharedComponents = archetype->NumSharedComponents;
                if(numSharedComponents==0)
                    continue;
                var sharedComponentIndexRemap = stackalloc int[numSharedComponents];
                FillSharedComponentIndexRemap(sharedComponentIndexRemap, archetype);
                for (int iChunk = 0; iChunk < archetype->Chunks.Count; ++iChunk)
                {
                    var sharedComponents = archetype->Chunks.p[iChunk]->SharedComponentValues;
                    for (int iType = 0; iType < numSharedComponents; iType++)
                    {
                        int remappedIndex = sharedComponentIndexRemap[iType];
                        sharedComponentArrays[index++] = sharedComponentMapping[sharedComponents[remappedIndex]];
                    }
                }
            }
            Assert.AreEqual(sharedComponentArrays.Length,index);
        }

        private static unsafe Dictionary<int, int> GatherSharedComponents(EntityArchetype[] archetypeArray, out int sharedComponentArraysTotalCount)
        {
            sharedComponentArraysTotalCount = 0;
            var sharedIndexToSerialize = new Dictionary<int, int>();
            sharedIndexToSerialize[0] = 0; // All default values map to 0
            int nextIndex = 1;
            for (int iArchetype = 0; iArchetype < archetypeArray.Length; ++iArchetype)
            {
                var archetype = archetypeArray[iArchetype].Archetype;
                sharedComponentArraysTotalCount += archetype->Chunks.Count * archetype->NumSharedComponents;

                int numSharedComponents = archetype->NumSharedComponents;
                for (int iType = 0; iType < numSharedComponents; iType++)
                {
                    var sharedComponents = archetype->Chunks.GetSharedComponentValueArrayForType(iType);
                    for (int iChunk = 0; iChunk < archetype->Chunks.Count; ++iChunk)
                    {
                        int sharedComponentIndex = sharedComponents[iChunk];
                        if (!sharedIndexToSerialize.ContainsKey(sharedComponentIndex))
                        {
                            sharedIndexToSerialize[sharedComponentIndex] = nextIndex++;
                        }
                    }
                }
            }

            return sharedIndexToSerialize;
        }

        private static unsafe void ClearChunkHeaderComponents(Chunk* chunk)
        {
            int chunkHeaderTypeIndex = TypeManager.GetTypeIndex<ChunkHeader>();
            var archetype = chunk->Archetype;
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(chunk->Archetype, chunkHeaderTypeIndex);
            if (typeIndexInArchetype == -1)
                return;

            var buffer = chunk->Buffer;
            var length = chunk->Count;
            var startOffset = archetype->Offsets[typeIndexInArchetype];
            var chunkHeaders = (ChunkHeader*)(buffer + startOffset);
            for (int i = 0; i < length; ++i)
            {
                chunkHeaders[i].chunk = null;
            }
        }

        static unsafe byte* OffsetFromPointer(void* ptr, int offset)
        {
            return ((byte*)ptr) + offset;
        }

        static unsafe void WriteArchetypes(BinaryWriter writer, EntityArchetype[] archetypeArray, Dictionary<int, int> typeIndexMap)
        {
            writer.Write(archetypeArray.Length);

            foreach (var archetype in archetypeArray)
            {
                writer.Write(archetype.Archetype->EntityCount);
                writer.Write(archetype.Archetype->TypesCount - 1);
                for (int i = 1; i < archetype.Archetype->TypesCount; ++i)
                {
                    var componentType = archetype.Archetype->Types[i];
                    int flag = componentType.IsChunkComponent ? TypeManager.ChunkComponentTypeFlag : 0;
                    writer.Write(typeIndexMap[componentType.TypeIndex & TypeManager.ClearFlagsMask] | flag);
                }
            }
        }

        static unsafe int GenerateRemapInfo(EntityManager entityManager, EntityArchetype[] archetypeArray, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapInfos)
        {
            int nextEntityId = 1; //0 is reserved for Entity.Null;

            int totalChunkCount = 0;
            for (int archetypeIndex = 0; archetypeIndex < archetypeArray.Length; ++archetypeIndex)
            {
                var archetype = archetypeArray[archetypeIndex].Archetype;
                for (int i = 0; i < archetype->Chunks.Count; ++i)
                {
                    var chunk = archetype->Chunks.p[i];
                    for (int iEntity = 0; iEntity < chunk->Count; ++iEntity)
                    {
                        var entity = *(Entity*)ChunkDataUtility.GetComponentDataRO(chunk, iEntity, 0);
                        EntityRemapUtility.AddEntityRemapping(ref entityRemapInfos, entity, new Entity { Version = 0, Index = nextEntityId });
                        ++nextEntityId;
                    }

                    totalChunkCount += 1;
                }
            }

            return totalChunkCount;
        }

        static unsafe string StringFromNativeBytes(NativeArray<byte> bytes, int offset = 0)
        {
            int len = 0;
            while (bytes[offset + len] != 0)
                ++len;
            return System.Text.Encoding.UTF8.GetString((Byte*) bytes.GetUnsafePtr() + offset, len);
        }
    }


}
#endif
