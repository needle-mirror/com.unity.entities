using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Assertions;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
#if !UNITY_DOTSRUNTIME
using Unity.Properties;
#endif

namespace Unity.Entities
{
    static unsafe partial class EntityDiffer
    {
        // This value has to be power of two.
        public const int ComponentChangesBatchCount = 128;
        public struct DeferredSharedComponentChange
        {
            public EntityGuid EntityGuid;
            public int TypeIndex;
            public int BeforeSharedComponentIndex;
            public int AfterSharedComponentIndex;
        }

        public struct DeferredManagedComponentChange
        {
            public EntityGuid EntityGuid;
            public int TypeIndex;
            public int AfterManagedComponentIndex;
            public int BeforeManagedComponentIndex;
        }

        struct NameChangeSet : IDisposable
        {
            public readonly NativeArray<FixedString64Bytes> Names;
            public readonly NativeArray<EntityGuid> NameChangedEntityGuids;
            public int NameChangedCount;

            public NameChangeSet(int length, Allocator allocator)
            {
                Names = new NativeArray<FixedString64Bytes>(length, allocator);
                NameChangedEntityGuids = new NativeArray<EntityGuid>(length, allocator);
                NameChangedCount = 0;
            }

            public void Dispose()
            {
                Names.Dispose();
                NameChangedEntityGuids.Dispose();
            }
        }
        public struct ComponentChanges : IDisposable
        {
            public readonly PackedEntityGuidsCollection Entities;
            public readonly PackedCollection<ComponentTypeHash> ComponentTypes;
            public readonly NativeList<PackedComponent> AddComponents;
            public readonly NativeList<PackedComponent> RemoveComponents;
            public readonly NativeList<PackedComponentDataChange> SetComponents;
            public readonly NativeList<LinkedEntityGroupChange> LinkedEntityGroupAdditions;
            public readonly NativeList<LinkedEntityGroupChange> LinkedEntityGroupRemovals;
            public readonly NativeList<EntityReferenceChange> EntityReferenceChanges;
            public readonly NativeList<BlobAssetReferenceChange> BlobAssetReferenceChanges;
            public readonly NativeList<byte> ComponentData;
            public readonly NativeList<DeferredSharedComponentChange> SharedComponentChanges;
            public readonly NativeList<DeferredManagedComponentChange> ManagedComponentChanges;

            public readonly bool IsCreated;
            private int ReserveSize;
            public ComponentChanges(int count)
            {
                Entities = new PackedEntityGuidsCollection(count, Allocator.Persistent);
                ComponentTypes = new PackedCollection<ComponentTypeHash>(count, Allocator.Persistent);
                AddComponents = new NativeList<PackedComponent>(count * 16, Allocator.Persistent);
                RemoveComponents = new NativeList<PackedComponent>(count, Allocator.Persistent);
                SetComponents = new NativeList<PackedComponentDataChange>(count * 8, Allocator.Persistent);
                LinkedEntityGroupAdditions = new NativeList<LinkedEntityGroupChange>(count, Allocator.Persistent);
                LinkedEntityGroupRemovals = new NativeList<LinkedEntityGroupChange>(count, Allocator.Persistent);
                EntityReferenceChanges = new NativeList<EntityReferenceChange>(count, Allocator.Persistent);
                BlobAssetReferenceChanges = new NativeList<BlobAssetReferenceChange>(count, Allocator.Persistent);
                ComponentData = new NativeList<byte>(count * 256, Allocator.Persistent);
                SharedComponentChanges = new NativeList<DeferredSharedComponentChange>(count * 16, Allocator.Persistent);
                ManagedComponentChanges = new NativeList<DeferredManagedComponentChange>(count, Allocator.Persistent);
                IsCreated = true;
                ReserveSize = count;
            }

            public void ResizeAndClear(int count)
            {
                if (count > ReserveSize)
                {
                    // Instead of doing resize on the buffers we delete/new them so
                    // we don't do a memcpy of all the data (which won't be used anyway)
                    Dispose();
                    this = new ComponentChanges(count);
                    ReserveSize = count;
                }
                else
                {
                    Clear();
                }
            }

            public void Clear()
            {
                Entities.Clear();
                ComponentTypes.Clear();
                if (AddComponents.IsCreated) AddComponents.Clear();
                if (RemoveComponents.IsCreated) RemoveComponents.Clear();
                if (SetComponents.IsCreated) SetComponents.Clear();
                if (LinkedEntityGroupAdditions.IsCreated) LinkedEntityGroupAdditions.Clear();
                if (LinkedEntityGroupRemovals.IsCreated) LinkedEntityGroupRemovals.Clear();
                if (EntityReferenceChanges.IsCreated) EntityReferenceChanges.Clear();
                if (BlobAssetReferenceChanges.IsCreated) BlobAssetReferenceChanges.Clear();
                if (ComponentData.IsCreated) ComponentData.Clear();
                if (SharedComponentChanges.IsCreated) SharedComponentChanges.Clear();
                if (ManagedComponentChanges.IsCreated) ManagedComponentChanges.Clear();
            }

            public void Dispose()
            {
                Entities.Dispose();
                ComponentTypes.Dispose();

                if (AddComponents.IsCreated) AddComponents.Dispose();
                if (RemoveComponents.IsCreated) RemoveComponents.Dispose();
                if (SetComponents.IsCreated) SetComponents.Dispose();
                if (LinkedEntityGroupAdditions.IsCreated) LinkedEntityGroupAdditions.Dispose();
                if (LinkedEntityGroupRemovals.IsCreated) LinkedEntityGroupRemovals.Dispose();
                if (EntityReferenceChanges.IsCreated) EntityReferenceChanges.Dispose();
                if (BlobAssetReferenceChanges.IsCreated) BlobAssetReferenceChanges.Dispose();
                if (ComponentData.IsCreated) ComponentData.Dispose();
                if (SharedComponentChanges.IsCreated) SharedComponentChanges.Dispose();
                if (ManagedComponentChanges.IsCreated) ManagedComponentChanges.Dispose();
            }
        }


        public struct PackedEntityGuidsCollection : IDisposable
        {
            // This array contains both added and modified entities where AddedCount is the number
            // number of added entities at the start of the list (rest is modified entities)
            [NativeDisableContainerSafetyRestriction]
            public NativeList<EntityGuid> List;

            public NativeReference<int> AddedCount;

            public PackedEntityGuidsCollection(int capacity, Allocator label)
            {
                List = new NativeList<EntityGuid>(capacity, label);
                AddedCount = new NativeReference<int>(label);
            }
            public void ResizeUninitialized(int count)
            {
                List.ResizeUninitialized(count);
            }

            public void Clear()
            {
                if (List.IsCreated) List.Clear();
            }

            public void Dispose()
            {
                if (List.IsCreated) List.Dispose();
                if (AddedCount.IsCreated) AddedCount.Dispose();
            }

            public int BinarySearchRange(EntityGuid key, int startIndex, int endIndex, int entryHint)
            {
                if (startIndex == endIndex)
                    return -1;

                // First we try with the hint index and validate that it's within range
                int index = startIndex + entryHint;

                if (index >= 0 && index < endIndex)
                {
                    if (key == List[index])
                    {
                        return index;
                    }
                }

                int min = startIndex;
                int max = endIndex - 1;

                // If we only have one entry
                if (min == max)
                {
                    if (key == List[0])
                        return 0;
                }

                while (min <= max)
                {
                    int middle = min + (max - min) / 2;
                    EntityGuid guid = List[middle];
                    var r = guid.CompareTo(key);
                    if (r == 0)
                        return middle;

                    if (r > 0)
                        max = middle - 1;
                    else
                        min = middle + 1; 
                }

                return -1;
            }

            public int Get(EntityGuid value, int tableHint, int entryHint)
            {
                int index = 0;
                var addedCount = AddedCount.Value;

                // If tableHint == 0 we search for addedCount range
                if (tableHint == 0)
                {
                    // search added
                    index = BinarySearchRange(value, 0, addedCount, entryHint);

                    if (index == -1)
                    {
                        index = BinarySearchRange(value, addedCount, List.Length, entryHint);
                    }
                }
                else
                {
                    index = BinarySearchRange(value, addedCount, List.Length, entryHint);

                    if (index == -1)
                    {
                        index = BinarySearchRange(value, 0, addedCount, entryHint);
                    }
                }

                if (index == -1)
                {
                    throw new Exception($"Unable to find the correct for {value} - index, ranges: {addedCount} - {List.Length}");
                }

                return index;
            }
        }

        public struct PackedCollection<T> : IDisposable where T : unmanaged, IEquatable<T>
        {
            [NativeDisableContainerSafetyRestriction]
            public NativeList<T> List;

            [NativeDisableContainerSafetyRestriction]
            public NativeParallelHashMap<T, int> Lookup;

            public PackedCollection(int capacity, Allocator label)
            {
                List = new NativeList<T>(capacity, label);
                Lookup = new NativeParallelHashMap<T, int>(capacity, label);
            }

            public void ResizeUninitialized(int count)
            {
                List.ResizeUninitialized(count);
                Lookup.Clear();
            }

            public void Clear()
            {
                if (List.IsCreated) List.Clear();
                if (Lookup.IsCreated) Lookup.Clear();
            }

            public void Dispose()
            {
                if (List.IsCreated) List.Dispose();
                if (Lookup.IsCreated) Lookup.Dispose();
            }

            public int GetOrAdd(T value)
            {
                if (Lookup.TryGetValue(value, out var index))
                {
                    return index;
                }
                index = List.Length;
                List.Add(value);
                Lookup.TryAdd(value, index);
                return index;
            }

            public int Get(T value)
            {
                if (Lookup.TryGetValue(value, out var index))
                {
                    return index;
                }
                else
                {
                    return -1;
                }
            }
        }

        static internal void GatherLinkedEntityGroupChanges(
            EntityGuid entityGuid,
            NativeArray<EntityGuid> beforeLinkedEntityGroup,
            NativeArray<EntityGuid> afterLinkedEntityGroup,
            ref NativeList<LinkedEntityGroupChange> additions,
            ref NativeList<LinkedEntityGroupChange> removals)
        {
            beforeLinkedEntityGroup.Sort();
            afterLinkedEntityGroup.Sort();

            var beforeIndex = 0;
            var afterIndex = 0;

            int beforeLength = beforeLinkedEntityGroup.Length;
            int afterLength = afterLinkedEntityGroup.Length;

            while (beforeIndex < beforeLength && afterIndex < afterLength)
            {
                var beforeEntityGuid = beforeLinkedEntityGroup[beforeIndex];
                var afterEntityGuid = afterLinkedEntityGroup[afterIndex];

                var comparison = beforeEntityGuid.CompareTo(afterEntityGuid);

                if (comparison == 0)
                {
                    // If the guids are equal, we know that the entity exists in both states.
                    beforeIndex++;
                    afterIndex++;
                }
                else if (comparison > 0)
                {
                    // If the before guid is greater, then we know that whatever guid we compared to
                    // belongs to an entity that was added. Otherwise, we would already have matched it
                    // in the first case before.
                    additions.Add(new LinkedEntityGroupChange
                        {RootEntityGuid = entityGuid, ChildEntityGuid = afterEntityGuid});
                    afterIndex++;
                }
                else if (comparison < 0)
                {
                    // If the before guid is smaller, then we know that that entity must have been
                    // removed
                    removals.Add(new LinkedEntityGroupChange
                        {RootEntityGuid = entityGuid, ChildEntityGuid = beforeEntityGuid});
                    beforeIndex++;
                }
            }

            while (beforeIndex < beforeLength)
            {
                // If the entity is in "before" but not "after", it's been removed.
                removals.Add(new LinkedEntityGroupChange
                    {RootEntityGuid = entityGuid, ChildEntityGuid = beforeLinkedEntityGroup[beforeIndex++]});
            }

            while (afterIndex < afterLength)
            {
                // If the entity is in "after" but not "before", it's been added.
                additions.Add(new LinkedEntityGroupChange
                    {RootEntityGuid = entityGuid, ChildEntityGuid = afterLinkedEntityGroup[afterIndex++]});
            }
        }

        [BurstCompile]
        struct GatherComponentChangesBuildPacked : IJob
        {
            [ReadOnly] public NativeList<CreatedEntity> CreatedEntities;
            [ReadOnly] public NativeList<ModifiedEntity> ModifiedEntities;
            [ReadOnly] [NativeDisableUnsafePtrRestriction] public TypeManager.TypeInfo* TypeInfo;

            public PackedEntityGuidsCollection Entities;
            public PackedCollection<ComponentTypeHash> ComponentTypes;

            void AddStableTypeHash(int typeIndex)
            {
                var flags = ComponentTypeFlags.None;

                if ((typeIndex & TypeManager.ChunkComponentTypeFlag) != 0)
                    flags |= ComponentTypeFlags.ChunkComponent;

                var stableTypeHash = TypeInfo[typeIndex & TypeManager.ClearFlagsMask].StableTypeHash;

                ComponentTypes.GetOrAdd(new ComponentTypeHash
                {
                    StableTypeHash = stableTypeHash,
                    Flags = flags
                });
            }

            public void Execute()
            {
                var archLookup = new UnsafeParallelHashSet<ulong>(128, Allocator.Temp);

                for (int i = 0; i < CreatedEntities.Length; ++i)
                {
                    var entityGuid = CreatedEntities[i].EntityGuid;
                    var afterEntity = CreatedEntities[i].AfterEntityInChunk;
                    var afterChunk = afterEntity.Chunk;
                    var afterArchetype = afterChunk->Archetype;
                    var afterTypesCount = afterArchetype->TypesCount;

                    Entities.List.Add(entityGuid);

                    if (archLookup.Add((ulong)(IntPtr)afterArchetype))
                    {
                        for (var afterIndexInTypeArray = 1; afterIndexInTypeArray < afterTypesCount; afterIndexInTypeArray++)
                        {
                            var afterTypeInArchetype = afterArchetype->Types[afterIndexInTypeArray];

                            if (afterTypeInArchetype.IsSystemStateComponent)
                                continue;

                            AddStableTypeHash(afterTypeInArchetype.TypeIndex);
                        }

                    }
                }

                Entities.AddedCount.Value = Entities.List.Length;

                for (int i = 0; i < ModifiedEntities.Length; ++i)
                {
                    var modification = ModifiedEntities[i];
                    var entityGuid = modification.EntityGuid;

                    var afterEntity = modification.AfterEntityInChunk;
                    var afterChunk = afterEntity.Chunk;
                    var afterArchetype = afterChunk->Archetype;
                    var afterTypesCount = afterArchetype->TypesCount;

                    var beforeEntity = modification.BeforeEntityInChunk;
                    var beforeChunk = beforeEntity.Chunk;

                    var beforeArchetype = beforeChunk->Archetype;
                    var beforeTypesCount = beforeArchetype->TypesCount;

                    Entities.List.Add(entityGuid);

                    if (archLookup.Add((ulong)(IntPtr)afterArchetype))
                    {
                        for (var afterIndexInTypeArray = 1; afterIndexInTypeArray < afterTypesCount; afterIndexInTypeArray++)
                        {
                            var afterTypeInArchetype = afterArchetype->Types[afterIndexInTypeArray];

                            if (afterTypeInArchetype.IsSystemStateComponent || afterTypeInArchetype.IsChunkComponent)
                                continue;

                            var typeIndex = afterTypeInArchetype.TypeIndex;

                            AddStableTypeHash(afterTypeInArchetype.TypeIndex);
                        }

                    }

                    if (archLookup.Add((ulong)(IntPtr)beforeArchetype))
                    {
                        for (var beforeTypeIndexInArchetype = 1; beforeTypeIndexInArchetype < beforeTypesCount; beforeTypeIndexInArchetype++)
                        {
                            var beforeComponentTypeInArchetype = beforeArchetype->Types[beforeTypeIndexInArchetype];

                            if (beforeComponentTypeInArchetype.IsSystemStateComponent)
                                continue;

                            var beforeTypeIndex = beforeComponentTypeInArchetype.TypeIndex;

                            if (-1 == ChunkDataUtility.GetIndexInTypeArray(afterArchetype, beforeTypeIndex))
                            {
                                AddStableTypeHash(beforeTypeIndex);
                            }
                        }

                    }
                }

                archLookup.Dispose();
            }
        }

        struct GatherComponentChanges
        {
            // Read-only variables set from the outside
            public GatherComponentChangesReadOnlyData ReadData;
            // Used for writing local (on stack/temp) data in a thread manner
            public GatherComponentChangesWriteOnlyData CacheData;
            // Atomic outputs written from the cached data
            public GatherComponentChangesOutput OutputData;

            public void ProcessRange(int index)
            {
                int createdCount = ReadData.CreatedEntities.Length;

                // The way the code works is that index can be both for Creations and Modifications in order to reduce the size of the code
                if (index < createdCount)
                {
                    int maxCreatedCount = math.min(createdCount, index + ComponentChangesBatchCount);

                    for (int i = index; i < maxCreatedCount; ++i)
                    {
                        var entityGuid = ReadData.CreatedEntities[i].EntityGuid;
                        var afterEntity = ReadData.CreatedEntities[i].AfterEntityInChunk;
                        var afterChunk = afterEntity.Chunk;
                        var afterArchetype = afterChunk->Archetype;
                        var afterTypesCount = afterArchetype->TypesCount;

                        for (var afterIndexInTypeArray = 1; afterIndexInTypeArray < afterTypesCount; afterIndexInTypeArray++)
                        {
                            var afterTypeInArchetype = afterArchetype->Types[afterIndexInTypeArray];

                            if (afterTypeInArchetype.IsSystemStateComponent)
                                continue;

                            AddComponentData(
                                afterChunk,
                                afterArchetype,
                                afterTypeInArchetype,
                                afterIndexInTypeArray,
                                afterEntity.IndexInChunk,
                                entityGuid, 0, i);
                        }
                    }

                    // handle the case where we have few entites to process and we need to fall into the other
                    // loop in case we need to

                    if (createdCount >= ComponentChangesBatchCount)
                        return;

                    index = 0; 
                }
                else
                {
                    index -= (createdCount + ComponentChangesBatchCount - 1) & ~(ComponentChangesBatchCount - 1);
                }

                // Offset as we are handling modifications here
                int maxCount = math.min(index + ComponentChangesBatchCount, ReadData.ModifiedEntities.Length);

                for (var i = index; i < maxCount; ++i)
                {
                    var modification = ReadData.ModifiedEntities[i];
                    var entityGuid = modification.EntityGuid;

                    var afterEntity = modification.AfterEntityInChunk;
                    var afterChunk = afterEntity.Chunk;
                    var afterArchetype = afterChunk->Archetype;
                    var afterTypesCount = afterArchetype->TypesCount;

                    var beforeEntity = modification.BeforeEntityInChunk;
                    var beforeChunk = beforeEntity.Chunk;
                    var beforeArchetype = beforeChunk->Archetype;
                    var beforeTypesCount = beforeArchetype->TypesCount;

                    for (var afterIndexInTypeArray = 1; afterIndexInTypeArray < afterTypesCount; afterIndexInTypeArray++)
                    {
                        var afterTypeInArchetype = afterArchetype->Types[afterIndexInTypeArray];

                        if (afterTypeInArchetype.IsSystemStateComponent || afterTypeInArchetype.IsChunkComponent)
                        {
                            continue;
                        }

                        var typeIndex = afterTypeInArchetype.TypeIndex;
                        var beforeIndexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(beforeArchetype, typeIndex);

                        // This type is missing in the before entity.
                        // This means we are dealing with a newly added component.
                        if (-1 == beforeIndexInTypeArray)
                        {
                            // This type does not exist on the before world. This was a newly added component.
                            AddComponentData(
                                afterChunk,
                                afterArchetype,
                                afterTypeInArchetype,
                                afterIndexInTypeArray,

                                afterEntity.IndexInChunk,
                                entityGuid, 1, i
                            );

                            continue;
                        }

                        if (!afterTypeInArchetype.IsManagedComponent && modification.CanCompareChunkVersions && afterChunk->GetChangeVersion(afterIndexInTypeArray) == beforeChunk->GetChangeVersion(beforeIndexInTypeArray))
                        {
                            continue;
                        }

                        SetComponentData(
                            afterChunk,
                            afterArchetype,
                            afterTypeInArchetype,
                            afterIndexInTypeArray,
                            afterEntity.IndexInChunk,
                            beforeChunk,
                            beforeArchetype,
                            beforeIndexInTypeArray,
                            beforeEntity.IndexInChunk,
                            entityGuid,
                            i);
                    }

                   for (var beforeTypeIndexInArchetype = 1; beforeTypeIndexInArchetype < beforeTypesCount; beforeTypeIndexInArchetype++)
                    {
                        var beforeComponentTypeInArchetype = beforeArchetype->Types[beforeTypeIndexInArchetype];

                        if (beforeComponentTypeInArchetype.IsSystemStateComponent)
                        {
                            continue;
                        }

                        var beforeTypeIndex = beforeComponentTypeInArchetype.TypeIndex;

                        if (-1 == ChunkDataUtility.GetIndexInTypeArray(afterArchetype, beforeTypeIndex))
                        {
                            var packedComponent = PackComponent(entityGuid, beforeTypeIndex, 1, i);
                            CacheData.RemoveComponents.Add(packedComponent);
                        }
                    }
                }
            }

            void AddComponentData(
                Chunk* afterChunk,
                Archetype* afterArchetype,
                ComponentTypeInArchetype afterTypeInArchetype,
                int afterIndexInTypeArray,
                int afterEntityIndexInChunk,
                EntityGuid entityGuid,
                int tableHint,
                int entryHint)
            {
                var packedComponent = PackComponent(entityGuid, afterTypeInArchetype.TypeIndex, tableHint, entryHint);

                CacheData.AddComponents.Add(packedComponent);

                if (afterTypeInArchetype.IsSharedComponent)
                {
                    var offset = afterIndexInTypeArray - afterChunk->Archetype->FirstSharedComponent;
                    var sharedComponentIndex = afterChunk->GetSharedComponentValue(offset);

                    // No managed objects in burst land. Do what we can a defer the actual unpacking until later.
                    AddendSharedComponentData(entityGuid, afterTypeInArchetype.TypeIndex, sharedComponentIndex);
                    return;
                }

                if (afterTypeInArchetype.IsManagedComponent)
                {
                    var afterManagedComponentIndex  = ((int*)(ChunkDataUtility.GetChunkBuffer(afterChunk) + afterArchetype->Offsets[afterIndexInTypeArray]))[afterEntityIndexInChunk];
                    AppendManagedComponentData(entityGuid, afterTypeInArchetype.TypeIndex, afterManagedComponentIndex);
                    return;
                }

                // IMPORTANT This means `IsZeroSizedInChunk` which is always true for shared components.
                // Always check shared components first.
                if (afterTypeInArchetype.IsZeroSized)
                {
                    return;
                }

                if (afterTypeInArchetype.IsBuffer)
                {
                    var sizeOf = afterArchetype->SizeOfs[afterIndexInTypeArray];
                    var buffer = (BufferHeader*)(ChunkDataUtility.GetChunkBuffer(afterChunk) + afterArchetype->Offsets[afterIndexInTypeArray] + afterEntityIndexInChunk * sizeOf);
                    var length = buffer->Length;

                    if (length == 0)
                    {
                        return;
                    }

                    var elementPtr = BufferHeader.GetElementPointer(buffer);

                    if (afterTypeInArchetype.TypeIndex == ReadData.LinkedEntityGroupTypeIndex)
                    {
                        // Magic in AddComponent already put a self-reference at the top of the buffer, so there's no need for us to add it.
                        // The rest of the elements should be interpreted as LinkedEntityGroupAdditions.
                        for (var elementIndex = 1; elementIndex < length; elementIndex++)
                        {
                            var childEntity = ((Entity*)elementPtr)[elementIndex];
                            var childEntityGuid = GetEntityGuid(ReadData.AfterEntityComponentStore, ReadData.EntityGuidTypeIndex, childEntity);

                            CacheData.LinkedEntityGroupAdditions.Add(new LinkedEntityGroupChange
                            {
                                RootEntityGuid = entityGuid,
                                ChildEntityGuid = childEntityGuid
                            });
                        }
                    }
                    else
                    {
                        var typeInfo = &ReadData.TypeInfo[afterTypeInArchetype.TypeIndex & TypeManager.ClearFlagsMask];
                        AppendComponentData(packedComponent, elementPtr, typeInfo->ElementSize * length);
                        ExtractPatches(typeInfo, packedComponent, elementPtr, length);
                    }
                }
                else
                {
                    var typeInfo = &ReadData.TypeInfo[afterTypeInArchetype.TypeIndex & TypeManager.ClearFlagsMask];
                    var sizeOf = afterArchetype->SizeOfs[afterIndexInTypeArray];
                    var ptr = ChunkDataUtility.GetChunkBuffer(afterChunk) + afterArchetype->Offsets[afterIndexInTypeArray] + afterEntityIndexInChunk * sizeOf;
                    AppendComponentData(packedComponent, ptr, sizeOf);
                    ExtractPatches(typeInfo, packedComponent, ptr, 1);
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            private static void AssertArchetypeSizeOfsMatch(Archetype* beforeArchetype, int beforeIndexInTypeArray, Archetype* afterArchetype, int afterIndexInTypeArray)
            {
                if (beforeArchetype->SizeOfs[beforeIndexInTypeArray] != afterArchetype->SizeOfs[afterIndexInTypeArray])
                    throw new Exception("Archetype->SizeOfs do not match");
            }

            void SetComponentData(
                Chunk* afterChunk,
                Archetype* afterArchetype,
                ComponentTypeInArchetype afterTypeInArchetype,
                int afterIndexInTypeArray,
                int afterEntityIndexInChunk,
                Chunk* beforeChunk,
                Archetype* beforeArchetype,
                int beforeIndexInTypeArray,
                int beforeEntityIndexInChunk,
                EntityGuid entityGuid,
                int entryHint)
            {
                if (afterTypeInArchetype.IsSharedComponent)
                {
                    var beforeOffset = beforeIndexInTypeArray - beforeChunk->Archetype->FirstSharedComponent;
                    var beforeSharedComponentIndex = beforeChunk->GetSharedComponentValue(beforeOffset);

                    var afterOffset = afterIndexInTypeArray - afterChunk->Archetype->FirstSharedComponent;
                    var afterSharedComponentIndex = afterChunk->GetSharedComponentValue(afterOffset);

                    // No managed objects in burst land. Do what we can and defer the actual unpacking until later.
                    AddendSharedComponentData(entityGuid, afterTypeInArchetype.TypeIndex, afterSharedComponentIndex, beforeSharedComponentIndex);
                    return;
                }

                if (afterTypeInArchetype.IsManagedComponent)
                {
                    var afterManagedComponentIndex  = ((int*)(ChunkDataUtility.GetChunkBuffer(afterChunk) + afterArchetype->Offsets[afterIndexInTypeArray]))[afterEntityIndexInChunk];
                    var beforeManagedComponentIndex  = ((int*)(ChunkDataUtility.GetChunkBuffer(beforeChunk) + beforeArchetype->Offsets[beforeIndexInTypeArray]))[beforeEntityIndexInChunk];

                    AppendManagedComponentData(entityGuid, afterTypeInArchetype.TypeIndex, afterManagedComponentIndex, beforeManagedComponentIndex);
                    return;
                }

                // IMPORTANT This means `IsZeroSizedInChunk` which is always true for shared components.
                // Always check shared components first.
                if (afterTypeInArchetype.IsZeroSized)
                {
                    return;
                }

                if (afterTypeInArchetype.IsBuffer)
                {
                    var beforeBuffer = (BufferHeader*)(ChunkDataUtility.GetChunkBuffer(beforeChunk)
                        + beforeArchetype->Offsets[beforeIndexInTypeArray]
                        + beforeEntityIndexInChunk
                        * beforeArchetype->SizeOfs[beforeIndexInTypeArray]);

                    var beforeElementPtr = BufferHeader.GetElementPointer(beforeBuffer);
                    var beforeLength = beforeBuffer->Length;

                    var afterBuffer = (BufferHeader*)(ChunkDataUtility.GetChunkBuffer(afterChunk)
                        + afterArchetype->Offsets[afterIndexInTypeArray]
                        + afterEntityIndexInChunk
                        * afterArchetype->SizeOfs[afterIndexInTypeArray]);

                    var afterElementPtr = BufferHeader.GetElementPointer(afterBuffer);
                    var afterLength = afterBuffer->Length;

                    if (afterTypeInArchetype.TypeIndex == ReadData.LinkedEntityGroupTypeIndex)
                    {
                        var beforeLinkedEntityGroups = (LinkedEntityGroup*)beforeElementPtr;
                        var afterLinkedEntityGroups = (LinkedEntityGroup*)afterElementPtr;

                        // Using is not supported by burst.
                        var beforeLinkedEntityGroupEntityGuids = new NativeArray<EntityGuid>(beforeLength, Allocator.Temp);
                        var afterLinkedEntityGroupEntityGuids = new NativeArray<EntityGuid>(afterLength, Allocator.Temp);
                        {
                            for (var i = 0; i < beforeLength; i++)
                            {
                                var beforeEntityGuid = GetEntityGuid(ReadData.BeforeEntityComponentStore, ReadData.EntityGuidTypeIndex, beforeLinkedEntityGroups[i].Value);
                                beforeLinkedEntityGroupEntityGuids[i] = beforeEntityGuid;
                            }

                            for (var i = 0; i < afterLength; i++)
                            {
                                var afterEntityGuid = GetEntityGuid(ReadData.AfterEntityComponentStore, ReadData.EntityGuidTypeIndex, afterLinkedEntityGroups[i].Value);
                                afterLinkedEntityGroupEntityGuids[i] = afterEntityGuid;
                            }

                            GatherLinkedEntityGroupChanges(entityGuid,
                                beforeLinkedEntityGroupEntityGuids, afterLinkedEntityGroupEntityGuids,
                                ref CacheData.LinkedEntityGroupAdditions, ref CacheData.LinkedEntityGroupRemovals);
                        }
                    }
                    else
                    {
                        var typeInfo = &ReadData.TypeInfo[afterTypeInArchetype.TypeIndex & TypeManager.ClearFlagsMask];

                        if (afterLength != beforeLength || !AreComponentsEqual(typeInfo, beforeElementPtr, afterElementPtr, afterLength))
                        {
                            var packedComponent = PackComponent(entityGuid, afterTypeInArchetype.TypeIndex, 1, entryHint);
                            AppendComponentData(packedComponent, afterElementPtr, typeInfo->ElementSize * afterLength);
                            ExtractPatches(typeInfo, packedComponent, afterElementPtr, afterLength);
                        }
                    }
                }
                else
                {
                    AssertArchetypeSizeOfsMatch(beforeArchetype, beforeIndexInTypeArray, afterArchetype, afterIndexInTypeArray);

                    var beforeAddress = ChunkDataUtility.GetChunkBuffer(beforeChunk)
                        + beforeArchetype->Offsets[beforeIndexInTypeArray]
                        + beforeArchetype->SizeOfs[beforeIndexInTypeArray]
                        * beforeEntityIndexInChunk;

                    var afterAddress = ChunkDataUtility.GetChunkBuffer(afterChunk)
                        + afterArchetype->Offsets[afterIndexInTypeArray]
                        + afterArchetype->SizeOfs[afterIndexInTypeArray]
                        * afterEntityIndexInChunk;

                    var typeInfo = &ReadData.TypeInfo[afterTypeInArchetype.TypeIndex & TypeManager.ClearFlagsMask];

                    if (!AreComponentsEqual(typeInfo, beforeAddress, afterAddress, 1))
                    {
                        var packedComponent = PackComponent(entityGuid, afterTypeInArchetype.TypeIndex, 1, entryHint);
                        AppendComponentData(packedComponent, afterAddress, beforeArchetype->SizeOfs[beforeIndexInTypeArray]);
                        ExtractPatches(typeInfo, packedComponent, afterAddress, 1);
                    }
                }
            }

            static bool ShouldSkip(int offset, ref int nextOffsetIndex, int offsetCount, TypeManager.EntityOffsetInfo* offsets)
            {
                while (nextOffsetIndex < offsetCount)
                {
                    int cmp = offset - offsets[nextOffsetIndex].Offset;
                    if (cmp < 0)
                        return false;
                    if (cmp == 0)
                        return true;
                    nextOffsetIndex++;
                }

                return false;
            }

            bool AreComponentsEqual(TypeManager.TypeInfo* typeInfo, byte* beforeAddress, byte* afterAddress, int elementCount)
            {
                int elementSize = typeInfo->ElementSize;
                if (!ReadData.UseReferentialEquivalence || typeInfo->EntityOffsetCount == 0 && typeInfo->BlobAssetRefOffsetCount == 0)
                {
                    return UnsafeUtility.MemCmp(beforeAddress, afterAddress, elementCount * elementSize) == 0
                           && !BlobAssetHashesAreDifferent(typeInfo, beforeAddress, afterAddress, elementCount);
                }

                // otherwise do deep comparison
                if (BlobAssetHashesAreDifferent(typeInfo, beforeAddress, afterAddress, elementCount))
                    return false;
                if (EntityGuidsAreDifferent(typeInfo, beforeAddress, afterAddress, elementCount))
                    return false;

                var blobAssetOffsets = ReadData.BlobAssetRefOffsets + typeInfo->BlobAssetRefOffsetStartIndex;
                var entityOffsets = ReadData.EntityOffsets + typeInfo->EntityOffsetStartIndex;
                int blobAssetOffsetCount = typeInfo->BlobAssetRefOffsetCount;
                int entityOffsetCount = typeInfo->EntityOffsetCount;

                byte* mask = stackalloc byte[elementSize];
                {
                    int currentEntityOffset = 0;
                    int currentBlobAssetOffset = 0;
                    for (int offset = 0; offset < elementSize;)
                    {
                        if (ShouldSkip(offset, ref currentEntityOffset, entityOffsetCount, entityOffsets))
                        {
                            UnsafeUtility.MemSet(&mask[offset], 0, sizeof(Entity));
                            offset += sizeof(Entity);
                            continue;
                        }
                        if (ShouldSkip(offset, ref currentBlobAssetOffset, blobAssetOffsetCount, blobAssetOffsets))
                        {
                            UnsafeUtility.MemSet(&mask[offset], 0, sizeof(BlobAssetReferenceData));
                            offset += sizeof(BlobAssetReferenceData);
                            continue;
                        }

                        mask[offset] = 0xFF;
                        offset += 1;
                    }
                }

                for (var elementIndex = 0; elementIndex < elementCount; ++elementIndex)
                {
                    for (int offset = 0; offset < elementSize; )
                    {
                        if (mask[offset] == 0 || beforeAddress[offset] == afterAddress[offset])
                        {
                            offset++;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            /// <summary>
            /// IMPORTANT. This function does *NO* validation. It is assumed to be called after a memcmp == 0
            /// </summary>
            bool BlobAssetHashesAreDifferent(
                TypeManager.TypeInfo* typeInfo,
                byte* beforeAddress,
                byte* afterAddress,
                int elementCount)
            {
                if (typeInfo->BlobAssetRefOffsetCount == 0)
                    return false;

                var offsets = ReadData.BlobAssetRefOffsets + typeInfo->BlobAssetRefOffsetStartIndex;

                var elementOffset = 0;
                int elementSize = typeInfo->ElementSize;
                int blobAssetRefOffsetCount = typeInfo->BlobAssetRefOffsetCount;
                for (var elementIndex = 0; elementIndex < elementCount; ++elementIndex)
                {
                    for (var offsetIndex = 0; offsetIndex < blobAssetRefOffsetCount; ++offsetIndex)
                    {
                        var offset = elementOffset + offsets[offsetIndex].Offset;

                        var beforeBlobAssetReference = (BlobAssetReferenceData*)(beforeAddress + offset);
                        var afterBlobAssetReference = (BlobAssetReferenceData*)(afterAddress + offset);

                        if (GetBlobAssetHash(ReadData.BeforeBlobAssetRemap, beforeBlobAssetReference) != GetBlobAssetHash(ReadData.AfterBlobAssetRemap, afterBlobAssetReference))
                            return true;
                    }

                    elementOffset += elementSize;
                }

                return false;
            }

            bool EntityGuidsAreDifferent(
                TypeManager.TypeInfo* typeInfo,
                byte* beforeAddress,
                byte* afterAddress,
                int elementCount)
            {
                if (typeInfo->EntityOffsetCount == 0)
                    return false;

                var offsets = ReadData.EntityOffsets + typeInfo->EntityOffsetStartIndex;

                var elementOffset = 0;
                int elementSize = typeInfo->ElementSize;
                int entityOffsetCount = typeInfo->EntityOffsetCount;
                for (var elementIndex = 0; elementIndex < elementCount; ++elementIndex)
                {
                    for (var offsetIndex = 0; offsetIndex < entityOffsetCount; ++offsetIndex)
                    {
                        var offset = elementOffset + offsets[offsetIndex].Offset;

                        var afterEntity = *(Entity*) (afterAddress + offset);
                        var beforeEntity = *(Entity*) (beforeAddress + offset);

                        // If the entity has no guid, then guid will be null (desired)
                        ReadData.BeforeEntityComponentStore->TryGetComponent(beforeEntity, ReadData.EntityGuidTypeIndex, out var beforeGuid);
                        ReadData.AfterEntityComponentStore->TryGetComponent(afterEntity, ReadData.EntityGuidTypeIndex, out var afterGuid);
                        if (!beforeGuid.Equals(afterGuid))
                            return true;
                    }

                    elementOffset += elementSize;
                }

                return false;
            }

            void ExtractPatches(
                TypeManager.TypeInfo* typeInfo,
                PackedComponent component,
                byte* afterAddress,
                int elementCount)
            {
                ExtractEntityReferencePatches(typeInfo, component, afterAddress, elementCount);
                ExtractBlobAssetReferencePatches(typeInfo, component, afterAddress, elementCount);
            }

            void ExtractEntityReferencePatches(
                TypeManager.TypeInfo* typeInfo,
                PackedComponent component,
                byte* afterAddress,
                int elementCount)
            {
                if (typeInfo->EntityOffsetCount == 0)
                {
                    return;
                }

                var offsets = ReadData.EntityOffsets + typeInfo->EntityOffsetStartIndex;

                var elementOffset = 0;
                int elementSize = typeInfo->ElementSize;
                int entityOffsetCount = typeInfo->EntityOffsetCount;
                for (var elementIndex = 0; elementIndex < elementCount; ++elementIndex)
                {
                    for (var offsetIndex = 0; offsetIndex < entityOffsetCount; ++offsetIndex)
                    {
                        var offset = elementOffset + offsets[offsetIndex].Offset;
                        var entity = *(Entity*)(afterAddress + offset);

                        // If the entity has no guid, then guid will be null (desired)
                        ReadData.AfterEntityComponentStore->TryGetComponent(entity, ReadData.EntityGuidTypeIndex, out var entityGuid);

                        CacheData.EntityReferencePatches.Add(new EntityReferenceChange
                        {
                            Component = component,
                            Offset = offset,
                            Value = entityGuid
                        });
                    }

                    elementOffset += elementSize;
                }
            }

            void ExtractBlobAssetReferencePatches(
                TypeManager.TypeInfo* typeInfo,
                PackedComponent component,
                byte* afterAddress,
                int elementCount)
            {
                if (typeInfo->BlobAssetRefOffsetCount == 0)
                {
                    return;
                }

                var offsets = ReadData.BlobAssetRefOffsets + typeInfo->BlobAssetRefOffsetStartIndex;

                var elementOffset = 0;
                int elementSize = typeInfo->ElementSize;
                int blobAssetRefOffsetCount = typeInfo->BlobAssetRefOffsetCount;
                for (var elementIndex = 0; elementIndex < elementCount; ++elementIndex)
                {
                    for (var offsetIndex = 0; offsetIndex < blobAssetRefOffsetCount; ++offsetIndex)
                    {
                        var offset = elementOffset + offsets[offsetIndex].Offset;
                        var blobAssetReference = (BlobAssetReferenceData*)(afterAddress + offset);
                        var hash = GetBlobAssetHash(ReadData.AfterBlobAssetRemap, blobAssetReference);

                        CacheData.BlobAssetReferenceChanges.Add(new BlobAssetReferenceChange
                        {
                            Component = component,
                            Offset = offset,
                            Value = hash
                        });
                    }

                    elementOffset += elementSize;
                }
            }

            static ulong GetBlobAssetHash(NativeParallelHashMap<BlobAssetPtr, BlobAssetPtr> remap, BlobAssetReferenceData* blobAssetReferenceData)
            {
                if (blobAssetReferenceData->m_Ptr == null)
                    return 0;

                if (remap.IsCreated && remap.TryGetValue(new BlobAssetPtr(((BlobAssetHeader*)blobAssetReferenceData->m_Ptr) - 1), out var header))
                    return header.Hash;

                return blobAssetReferenceData->Header->Hash;
            }

            void AppendComponentData(PackedComponent component, void* ptr, int sizeOf)
            {
                CacheData.SetComponents.Add(new PackedComponentDataChange
                {
                    Component = component,
                    Offset = 0,
                    Size = sizeOf
                });

                CacheData.ComponentData.AddRange(ptr, sizeOf);
            }

            void AddendSharedComponentData(EntityGuid entityGuid, int typeIndex, int afterSharedComponentIndex, int beforeSharedComponentIndex = -1)
            {
                CacheData.SharedComponentChanges.Add(new DeferredSharedComponentChange
                {
                    EntityGuid = entityGuid,
                    TypeIndex = typeIndex,
                    AfterSharedComponentIndex = afterSharedComponentIndex,
                    BeforeSharedComponentIndex = beforeSharedComponentIndex
                });
            }

            void AppendManagedComponentData(EntityGuid entityGuid, int typeIndex, int afterManagedComponentIndex, int beforeManagedComponentIndex = -1)
            {
                CacheData.ManagedComponentChanges.Add(new DeferredManagedComponentChange
                {
                    EntityGuid = entityGuid,
                    TypeIndex = typeIndex,
                    AfterManagedComponentIndex = afterManagedComponentIndex,
                    BeforeManagedComponentIndex = beforeManagedComponentIndex
                });
            }

            PackedComponent PackComponent(EntityGuid entityGuid, int typeIndex, int tableHint, int entryHint)
            {
                var flags = ComponentTypeFlags.None;

                if ((typeIndex & TypeManager.ChunkComponentTypeFlag) != 0)
                    flags |= ComponentTypeFlags.ChunkComponent;

                ulong stableTypeHash = ReadData.TypeInfo[typeIndex & TypeManager.ClearFlagsMask].StableTypeHash;

                var packedEntityIndex = ReadData.Entities.Get(entityGuid, tableHint, entryHint);
                var packedTypeIndex = ReadData.ComponentTypes.Get(new ComponentTypeHash
                {
                    StableTypeHash = stableTypeHash,
                    Flags = flags
                });

                if (packedTypeIndex == -1)
                {
                    throw new Exception ($"ComponetTypes: Unable to find {stableTypeHash}:{flags}");
                }

                return new PackedComponent
                {
                    PackedEntityIndex = packedEntityIndex,
                    PackedTypeIndex = packedTypeIndex
                };
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            private void ThrowOnEntityGuidMissing()
            {
                throw new Exception("LinkedEntityGroup child is missing an EntityGuid component.");
            }

            EntityGuid GetEntityGuid(EntityComponentStore* entityComponentStore, int entityGuidTypeIndex, Entity entity)
            {
                if (!entityComponentStore->TryGetComponent(entity, entityGuidTypeIndex, out var result))
                {
                    ThrowOnEntityGuidMissing();
                }

                return result;
            }

            private void WriteArray<T>(NativeList<T> dest, ref NativeList<T> src) where T : unmanaged
            {
                if (src.IsEmpty)
                    return;

                dest.AddRange(src);
                src.Clear();
            }

            public void WriteToOutputData()
            {
                WriteArray(OutputData.AddComponents, ref CacheData.AddComponents);
                WriteArray(OutputData.SetComponents, ref CacheData.SetComponents);
                WriteArray(OutputData.RemoveComponents, ref CacheData.RemoveComponents);
                WriteArray(OutputData.EntityReferencePatches, ref CacheData.EntityReferencePatches);
                WriteArray(OutputData.BlobAssetReferenceChanges, ref CacheData.BlobAssetReferenceChanges);
                WriteArray(OutputData.LinkedEntityGroupAdditions, ref CacheData.LinkedEntityGroupAdditions);
                WriteArray(OutputData.LinkedEntityGroupRemovals, ref CacheData.LinkedEntityGroupRemovals);
                WriteArray(OutputData.ComponentData, ref CacheData.ComponentData);
                WriteArray(OutputData.SharedComponentChanges, ref CacheData.SharedComponentChanges);
                WriteArray(OutputData.ManagedComponentChanges, ref CacheData.ManagedComponentChanges);
            }
        }

        struct GatherComponentChangesOutput
        {
            [WriteOnly] public NativeList<PackedComponent> AddComponents;
            [WriteOnly] public NativeList<PackedComponentDataChange> SetComponents;
            [WriteOnly] public NativeList<PackedComponent> RemoveComponents;
            [WriteOnly] public NativeList<EntityReferenceChange> EntityReferencePatches;
            [WriteOnly] public NativeList<BlobAssetReferenceChange> BlobAssetReferenceChanges;
            [WriteOnly] public NativeList<LinkedEntityGroupChange> LinkedEntityGroupAdditions;
            [WriteOnly] public NativeList<LinkedEntityGroupChange> LinkedEntityGroupRemovals;
            [WriteOnly] public NativeList<byte> ComponentData;
            [WriteOnly] public NativeList<DeferredSharedComponentChange> SharedComponentChanges;
            [WriteOnly] public NativeList<DeferredManagedComponentChange> ManagedComponentChanges;
        }

        struct GatherComponentChangesReadOnlyData
        {
            // If these two variables are marked as [ReadOnly] they may endup being set to zero instead of correct value.
            public int EntityGuidTypeIndex;
            public int LinkedEntityGroupTypeIndex;

            [ReadOnly] [NativeDisableUnsafePtrRestriction] public TypeManager.TypeInfo* TypeInfo;
            [ReadOnly] [NativeDisableUnsafePtrRestriction] public TypeManager.EntityOffsetInfo* EntityOffsets;
            [ReadOnly] [NativeDisableUnsafePtrRestriction] public TypeManager.EntityOffsetInfo* BlobAssetRefOffsets;
            [ReadOnly] [NativeDisableUnsafePtrRestriction] public EntityComponentStore* AfterEntityComponentStore;
            [ReadOnly] [NativeDisableUnsafePtrRestriction] public EntityComponentStore* BeforeEntityComponentStore;

            [ReadOnly] public NativeList<CreatedEntity> CreatedEntities;
            [ReadOnly] public NativeList<ModifiedEntity> ModifiedEntities;

            [ReadOnly] public PackedEntityGuidsCollection Entities;
            [ReadOnly] public PackedCollection<ComponentTypeHash> ComponentTypes;

            [ReadOnly] [NativeDisableContainerSafetyRestriction] public NativeParallelHashMap<BlobAssetPtr, BlobAssetPtr> AfterBlobAssetRemap;
            [ReadOnly] [NativeDisableContainerSafetyRestriction] public NativeParallelHashMap<BlobAssetPtr, BlobAssetPtr> BeforeBlobAssetRemap;

            /// <summary>
            /// If set, components are not compared bit-wise. Bit-wise comparison implies that two components that
            /// have references to entities that have the same GUID but different indices/versions are different.
            /// Similarly blob asset references to blob assets that have the same hash but live at different addresses
            /// will be considered different as well. This is often not desirable. For these cases, it is more apt to
            /// check that GUIDs and hashes match.
            /// </summary>
            [ReadOnly] public bool UseReferentialEquivalence;
        }

        struct GatherComponentChangesWriteOnlyData
        {
            [WriteOnly] public NativeList<PackedComponent> AddComponents;
            [WriteOnly] public NativeList<PackedComponentDataChange> SetComponents;
            [WriteOnly] public NativeList<PackedComponent> RemoveComponents;
            [WriteOnly] public NativeList<EntityReferenceChange> EntityReferencePatches;
            [WriteOnly] public NativeList<BlobAssetReferenceChange> BlobAssetReferenceChanges;
            [WriteOnly] public NativeList<LinkedEntityGroupChange> LinkedEntityGroupAdditions;
            [WriteOnly] public NativeList<LinkedEntityGroupChange> LinkedEntityGroupRemovals;
            [WriteOnly] public NativeList<byte> ComponentData;
            [WriteOnly] public NativeList<DeferredSharedComponentChange> SharedComponentChanges;
            [WriteOnly] public NativeList<DeferredManagedComponentChange> ManagedComponentChanges;
        }

        [BurstCompile]
        struct GatherComponentChangesJob : IJob
        {
            public GatherComponentChangesReadOnlyData ReadData;
            public GatherComponentChangesOutput OutputData;

            public void Execute()
            {
                var addComponentsSize = ComponentChangesBatchCount * 16;
                var removeComponentsSize  = ComponentChangesBatchCount;
                var setComponentsSize  = ComponentChangesBatchCount * 8;
                var linkedEntityGroupAdditionsSize = ComponentChangesBatchCount;
                var linkedEntityGroupRemovalsSize = ComponentChangesBatchCount;
                var blobAssetReferenceChangesSize = ComponentChangesBatchCount;
                var entityReferencePatchesSize = ComponentChangesBatchCount;
                var componentDataSize = ComponentChangesBatchCount * 256;
                var sharedComponentChangesSize = ComponentChangesBatchCount * 16;
                var managedComponentChangesSize = ComponentChangesBatchCount;

                var cacheData = new GatherComponentChangesWriteOnlyData
                {
                    AddComponents = new NativeList<PackedComponent>(addComponentsSize, Allocator.Temp),
                    RemoveComponents = new NativeList<PackedComponent>(removeComponentsSize, Allocator.Temp),
                    SetComponents = new NativeList<PackedComponentDataChange>(setComponentsSize, Allocator.Temp),
                    LinkedEntityGroupAdditions = new NativeList<LinkedEntityGroupChange>(linkedEntityGroupAdditionsSize, Allocator.Temp),
                    LinkedEntityGroupRemovals = new NativeList<LinkedEntityGroupChange>(linkedEntityGroupRemovalsSize, Allocator.Temp),
                    BlobAssetReferenceChanges = new NativeList<BlobAssetReferenceChange>(blobAssetReferenceChangesSize, Allocator.Temp),
                    EntityReferencePatches = new NativeList<EntityReferenceChange>(entityReferencePatchesSize, Allocator.Temp),
                    ComponentData = new NativeList<byte>(componentDataSize, Allocator.Temp),
                    SharedComponentChanges = new NativeList<DeferredSharedComponentChange>(sharedComponentChangesSize, Allocator.Temp),
                    ManagedComponentChanges = new NativeList<DeferredManagedComponentChange>(managedComponentChangesSize, Allocator.Temp),
                };

                var data = new GatherComponentChanges
                {
                    ReadData = ReadData,
                    CacheData = cacheData,
                    OutputData = OutputData,
                };

                int count = ReadData.CreatedEntities.Length + ReadData.ModifiedEntities.Length;

                for (int i = 0; i < count; i += ComponentChangesBatchCount)
                {
                    data.ProcessRange(i);
                    data.WriteToOutputData();
                }

                data.CacheData.ManagedComponentChanges.Dispose();
                data.CacheData.SharedComponentChanges.Dispose();
                data.CacheData.ComponentData.Dispose();
                data.CacheData.EntityReferencePatches.Dispose();
                data.CacheData.BlobAssetReferenceChanges.Dispose();
                data.CacheData.LinkedEntityGroupRemovals.Dispose();
                data.CacheData.LinkedEntityGroupAdditions.Dispose();
                data.CacheData.SetComponents.Dispose();
                data.CacheData.RemoveComponents.Dispose();
                data.CacheData.AddComponents.Dispose();
            }
        }

        static readonly PackedSharedComponentDataChange[] s_EmptySetSharedComponentDiff = new PackedSharedComponentDataChange[0];
        static readonly PackedManagedComponentDataChange[] s_EmptySetManagedComponentDiff = new PackedManagedComponentDataChange[0];

        static GatherComponentChangesReadOnlyData InitGatherComponentChangesReadOnlyData(
            EntityInChunkChanges entityChanges,
            ComponentChanges componentChanges,
            bool useReferentialEquivalence,
            NativeParallelHashMap<BlobAssetPtr, BlobAssetPtr> afterBlobAssetRemap,
            NativeParallelHashMap<BlobAssetPtr, BlobAssetPtr> beforeBlobAssetRemap)
        {
            return new GatherComponentChangesReadOnlyData
            {
                EntityGuidTypeIndex = TypeManager.GetTypeIndex<EntityGuid>(),
                LinkedEntityGroupTypeIndex = TypeManager.GetTypeIndex<LinkedEntityGroup>(),
                TypeInfo = TypeManager.GetTypeInfoPointer(),
                EntityOffsets = TypeManager.GetEntityOffsetsPointer(),
                BlobAssetRefOffsets = TypeManager.GetBlobAssetRefOffsetsPointer(),
                AfterEntityComponentStore = entityChanges.AfterEntityManager.GetCheckedEntityDataAccess()->EntityComponentStore,
                BeforeEntityComponentStore = entityChanges.BeforeEntityManager.GetCheckedEntityDataAccess()->EntityComponentStore,
                CreatedEntities = entityChanges.CreatedEntities,
                ModifiedEntities = entityChanges.ModifiedEntities,
                Entities = componentChanges.Entities,
                ComponentTypes = componentChanges.ComponentTypes,

                AfterBlobAssetRemap = afterBlobAssetRemap,
                BeforeBlobAssetRemap = beforeBlobAssetRemap,
                UseReferentialEquivalence = useReferentialEquivalence,
            };
        }

        static GatherComponentChangesOutput InitGatherComponentChangesOutput(ComponentChanges componentChanges)
        {
            return new GatherComponentChangesOutput
            {
                AddComponents = componentChanges.AddComponents,
                RemoveComponents = componentChanges.RemoveComponents,
                SetComponents = componentChanges.SetComponents,
                EntityReferencePatches = componentChanges.EntityReferenceChanges,
                BlobAssetReferenceChanges = componentChanges.BlobAssetReferenceChanges,
                LinkedEntityGroupAdditions = componentChanges.LinkedEntityGroupAdditions,
                LinkedEntityGroupRemovals = componentChanges.LinkedEntityGroupRemovals,
                ComponentData = componentChanges.ComponentData,
                SharedComponentChanges = componentChanges.SharedComponentChanges,
                ManagedComponentChanges = componentChanges.ManagedComponentChanges,
            };
        }

        static ComponentChanges GetComponentChanges(
            ref ComponentChanges componentChanges,
            EntityInChunkChanges entityChanges,
            bool useReferentialEquivalence,
            NativeParallelHashMap<BlobAssetPtr, BlobAssetPtr> afterBlobAssetRemap,
            NativeParallelHashMap<BlobAssetPtr, BlobAssetPtr> beforeBlobAssetRemap,
            Allocator allocator,
            out JobHandle jobHandle,
            JobHandle dependsOn = default)
        {
            dependsOn.Complete();
            dependsOn = default;

            int count = entityChanges.CreatedEntities.Length + entityChanges.ModifiedEntities.Length;

            var readOnlyData = InitGatherComponentChangesReadOnlyData(
                entityChanges,
                componentChanges,
                useReferentialEquivalence,
                afterBlobAssetRemap,
                beforeBlobAssetRemap);

            var outputData = InitGatherComponentChangesOutput(componentChanges);

            // Pre-populate the entities/component types
            new GatherComponentChangesBuildPacked
            {
                CreatedEntities = entityChanges.CreatedEntities,
                ModifiedEntities = entityChanges.ModifiedEntities,
                TypeInfo = TypeManager.GetTypeInfoPointer(),
                Entities = readOnlyData.Entities,
                ComponentTypes = readOnlyData.ComponentTypes,
            }.Run();

            if (count >= 1)
            {
                new GatherComponentChangesJob
                {
                    ReadData = readOnlyData,
                    OutputData = outputData,
                }.Run();
            }

            jobHandle = default;

            return componentChanges;
        }

        static EntityChangeSet CreateEntityChangeSet(
            EntityInChunkChanges entityInChunkChanges,
            ComponentChanges componentChanges,
            BlobAssetChanges blobAssetChanges,
            Allocator allocator)
        {
            if (!entityInChunkChanges.IsCreated || !componentChanges.IsCreated || !blobAssetChanges.IsCreated)
            {
                return default;
            }
            s_CreateEntityChangeSetProfilerMarker.Begin();

            // IMPORTANT. This can add to the packed collections. It must be done before adding destroyed entities.
            var sharedComponentDataChanges = GetChangedSharedComponents(
                componentChanges.Entities,
                componentChanges.ComponentTypes,
                componentChanges.SharedComponentChanges,
                componentChanges.BlobAssetReferenceChanges,
                entityInChunkChanges.AfterEntityManager.GetCheckedEntityDataAccess()->EntityComponentStore,
                entityInChunkChanges.BeforeEntityManager.GetCheckedEntityDataAccess()->ManagedComponentStore,
                entityInChunkChanges.AfterEntityManager.GetCheckedEntityDataAccess()->ManagedComponentStore);

            var managedComponentDataChanges = GetChangedManagedComponents(
                componentChanges.Entities,
                componentChanges.ComponentTypes,
                componentChanges.ManagedComponentChanges,
                componentChanges.EntityReferenceChanges,
                componentChanges.BlobAssetReferenceChanges,
                entityInChunkChanges.AfterEntityManager.GetCheckedEntityDataAccess()->EntityComponentStore,
                entityInChunkChanges.BeforeEntityManager.GetCheckedEntityDataAccess()->ManagedComponentStore,
                entityInChunkChanges.AfterEntityManager.GetCheckedEntityDataAccess()->ManagedComponentStore);

            // Add destroyed entities to componentChanges.Entities
            var entities = componentChanges.Entities.List;
            for (var i = 0; i < entityInChunkChanges.DestroyedEntities.Length; i++)
            {
                entities.Add(entityInChunkChanges.DestroyedEntities[i].EntityGuid);
            }

            var nameChanges = GetEntityNames(
                entityInChunkChanges.CreatedEntities,
                entityInChunkChanges.DestroyedEntities,
                entityInChunkChanges.NameModifiedEntities,
                entityInChunkChanges.AfterEntityManager,
                entityInChunkChanges.BeforeEntityManager,
                allocator);

            // Allocate and copy in to the results buffers.
            var result = new EntityChangeSet
                (
                entityInChunkChanges.CreatedEntities.Length,
                entityInChunkChanges.DestroyedEntities.Length,
                nameChanges.NameChangedCount,
                componentChanges.Entities.List.ToArray(allocator),
                componentChanges.ComponentTypes.List.ToArray(allocator),
                nameChanges.Names,
                nameChanges.NameChangedEntityGuids,
                componentChanges.AddComponents.ToArray(allocator),
                componentChanges.RemoveComponents.ToArray(allocator),
                componentChanges.SetComponents.ToArray(allocator),
                componentChanges.ComponentData.ToArray(allocator),
                componentChanges.EntityReferenceChanges.ToArray(allocator),
                componentChanges.BlobAssetReferenceChanges.ToArray(allocator),
                managedComponentDataChanges,
                sharedComponentDataChanges,
                componentChanges.LinkedEntityGroupAdditions.ToArray(allocator),
                componentChanges.LinkedEntityGroupRemovals.ToArray(allocator),
                blobAssetChanges.CreatedBlobAssets.ToArray(allocator),
                blobAssetChanges.DestroyedBlobAssets.ToArray(allocator),
                blobAssetChanges.BlobAssetData.ToArray(allocator)
                );

            s_CreateEntityChangeSetProfilerMarker.End();
            return result;
        }

        static PackedSharedComponentDataChange[] GetChangedSharedComponents(
            PackedEntityGuidsCollection packedEntityCollection,
            PackedCollection<ComponentTypeHash> packedStableTypeHashCollection,
            NativeList<DeferredSharedComponentChange> changes,
            NativeList<BlobAssetReferenceChange> blobAssetReferencePatches,
            EntityComponentStore* afterEntityComponentStore,
            ManagedComponentStore beforeManagedComponentStore,
            ManagedComponentStore afterManagedComponentStore)
        {
            if (changes.Length == 0)
            {
                return s_EmptySetSharedComponentDiff;
            }

            s_GetChangedSharedComponentsProfilerMarker.Begin();
            var result = new List<PackedSharedComponentDataChange>();

#if !UNITY_DOTSRUNTIME
            var managedObjectPatches = new ManagedObjectPatches(afterEntityComponentStore);
#endif

            for (var i = 0; i < changes.Length; i++)
            {
                var change = changes[i];

                object afterValue = null;

                if (change.AfterSharedComponentIndex == 0 && change.BeforeSharedComponentIndex == 0)
                    continue;

                if (change.AfterSharedComponentIndex != 0)
                {
                    afterValue = afterManagedComponentStore.GetSharedComponentDataBoxed(change.AfterSharedComponentIndex, change.TypeIndex);
                }

                if (change.BeforeSharedComponentIndex > -1 && change.AfterSharedComponentIndex != 0)
                {

                    var beforeValue = beforeManagedComponentStore.GetSharedComponentDataBoxed(change.BeforeSharedComponentIndex, change.TypeIndex);

                    if (TypeManager.Equals(beforeValue, afterValue, change.TypeIndex))
                    {
                        continue;
                    }
                }

                var packedEntityIndex = packedEntityCollection.Get(change.EntityGuid, 0, 0);
                var packedTypeIndex = packedStableTypeHashCollection.GetOrAdd(new ComponentTypeHash
                {
                    StableTypeHash = TypeManager.GetTypeInfo(change.TypeIndex).StableTypeHash
                });

                var packedComponent = new PackedComponent
                {
                    PackedEntityIndex = packedEntityIndex,
                    PackedTypeIndex = packedTypeIndex
                };

                (afterValue as IRefCounted)?.Retain();
#if !UNITY_DOTSRUNTIME
                // NOTE: Extracting entity patches from shared components is intentionally disabled here until a full solution is ready.
                if (null != afterValue)
                    managedObjectPatches.ExtractPatches(ref afterValue, packedComponent, default, blobAssetReferencePatches);
#endif

                result.Add(new PackedSharedComponentDataChange
                {
                    Component = packedComponent,
                    BoxedSharedValue = afterValue
                });
            }
            s_GetChangedSharedComponentsProfilerMarker.End();
            return result.ToArray();
        }

        static PackedManagedComponentDataChange[] GetChangedManagedComponents(
            PackedEntityGuidsCollection packedEntityCollection,
            PackedCollection<ComponentTypeHash> packedStableTypeHashCollection,
            NativeList<DeferredManagedComponentChange> changes,
            NativeList<EntityReferenceChange> entityReferencePatches,
            NativeList<BlobAssetReferenceChange> blobAssetReferencePatches,
            EntityComponentStore* afterEntityComponentStore,
            ManagedComponentStore beforeManagedComponentStore,
            ManagedComponentStore afterManagedComponentStore)
        {
            if (changes.Length == 0)
            {
                return s_EmptySetManagedComponentDiff;
            }
            s_GetChangedManagedComponentsProfilerMarker.Begin();

            var result = new List<PackedManagedComponentDataChange>();

            var managedObjectClone = new ManagedObjectClone();

#if !UNITY_DOTSRUNTIME
            var managedObjectPatches = new ManagedObjectPatches(afterEntityComponentStore);
#endif

            for (var i = 0; i < changes.Length; i++)
            {
                var change = changes[i];
                ref readonly var typeInfo = ref TypeManager.GetTypeInfo(change.TypeIndex);

                if (typeInfo.Category == TypeManager.TypeCategory.UnityEngineObject)
                {
                    // Hybrid Components should be ignored in the diff, the Companion Link will clone the Companion GameObject
                    // and when we apply the diff we'll relink the Hybrid Component to the ones from the Companion GameObject
                    continue;
                }

                var afterValue = afterManagedComponentStore.GetManagedComponent(change.AfterManagedComponentIndex);

                if (change.BeforeManagedComponentIndex > -1)
                {
                    var beforeValue = beforeManagedComponentStore.GetManagedComponent(change.BeforeManagedComponentIndex);

                    if (TypeManager.Equals(beforeValue, afterValue, change.TypeIndex))
                    {
                        continue;
                    }
                }

                var packedEntityIndex = packedEntityCollection.Get(change.EntityGuid, 1, i);
                var packedTypeIndex = packedStableTypeHashCollection.GetOrAdd(new ComponentTypeHash
                {
                    StableTypeHash = typeInfo.StableTypeHash
                });

                var packedComponent = new PackedComponent
                {
                    PackedEntityIndex = packedEntityIndex,
                    PackedTypeIndex = packedTypeIndex
                };

                afterValue = managedObjectClone.Clone(afterValue);

#if !UNITY_DOTSRUNTIME
                if (null != afterValue)
                    managedObjectPatches.ExtractPatches(ref afterValue, packedComponent, entityReferencePatches, blobAssetReferencePatches);
#endif

                result.Add(new PackedManagedComponentDataChange
                {
                    Component = packedComponent,
                    BoxedValue = afterValue
                });
            }
            s_GetChangedManagedComponentsProfilerMarker.End();
            return result.ToArray();
        }

// TODO: remove UNITY_EDITOR conditional once https://unity3d.atlassian.net/browse/DOTS-3862 is fixed
#if UNITY_EDITOR && !DOTS_DISABLE_DEBUG_NAMES
        [BurstCompile]
        struct GetEntityNamesJob
        {
            delegate void BurstExecute(void* @this);
            private static FunctionPointer<BurstExecute> _fPtr;
            private static BurstExecute _cachedFptr;
            [BurstCompile]
            static void ExecuteForBurst(void* @this) => UnsafeUtility.AsRef<GetEntityNamesJob>(@this).Execute();

            public int EntityGuidTypeIndex;
            public NameChangeSet NameChanges;
            public int* NameChangeCount;
            public NativeList<CreatedEntity> CreatedEntities;
            public NativeList<DestroyedEntity> DestroyedEntities;
            public NativeList<NameModifiedEntity> NameModifiedEntities;
            public UnsafeBitArray NameChangeBitsByEntity;
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* AfterEntityComponentStore;
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* BeforeEntityComponentStore;

            public void RunWithoutJobs()
            {
                if (!_fPtr.IsCreated)
                {
                    _fPtr = BurstCompiler.CompileFunctionPointer<BurstExecute>(ExecuteForBurst);
                    _cachedFptr = _fPtr.Invoke;
                }
                var copy = this;
                _cachedFptr(UnsafeUtility.AddressOf(ref copy));
            }

            bool TryGetEntityGuid(EntityComponentStore* entityComponentStoreEntity, Entity entity, out EntityGuid entityGuid)
            {
                entityGuid = default;

                if (entityComponentStoreEntity->Exists(entity) &&
                    entityComponentStoreEntity->TryGetComponent(entity, EntityGuidTypeIndex, out entityGuid))
                {
                    return true;
                }

                return false;
            }

            void Execute()
            {
                var namesPtr = (FixedString64Bytes*)NameChanges.Names.GetUnsafeReadOnlyPtr();
                var entityGuidsPtr = (EntityGuid*)NameChanges.NameChangedEntityGuids.GetUnsafeReadOnlyPtr();

                var length = CreatedEntities.Length + NameModifiedEntities.Length + DestroyedEntities.Length;
                var entitiesLookup = new UnsafeParallelHashSet<EntityGuid>(length, Allocator.TempJob);

                // Created entities will ALWAYS show up in the entityGuid set so we can safely grab the names.
                // They will exist in the after world.
                int nameIndex = 0;
                int guidIndex = 0;
                for (var i = 0; i < CreatedEntities.Length; i++)
                {
                    var afterEntity = ChunkDataUtility.GetEntityFromEntityInChunk(CreatedEntities[i].AfterEntityInChunk);
                    AfterEntityComponentStore->GetName(afterEntity, out namesPtr[nameIndex++]);
                    entitiesLookup.Add(CreatedEntities[i].EntityGuid);

                }

                // Entities with name and component changes
                for (var i = 0; i < NameModifiedEntities.Length; i++)
                {
                    var entity = NameModifiedEntities[i].Entity;
                    AfterEntityComponentStore->GetName(entity, out namesPtr[nameIndex++]);
                    entityGuidsPtr[guidIndex++] = NameModifiedEntities[i].EntityGuid;
                    entitiesLookup.Add(NameModifiedEntities[i].EntityGuid);
                }


                // Only check name change bits when the sequence number of 2 worlds are the same.
                // When the sequence numbers are the same, it is possible that there are entities with only
                // name changes that are not captured in GetEntityInChunkChanges.
                if (AfterEntityComponentStore->NameChangeBitsSequenceNum == BeforeEntityComponentStore->NameChangeBitsSequenceNum)
                {
                    int checkLength = Math.Min(AfterEntityComponentStore->EntitiesCapacity, NameChangeBitsByEntity.Length);
                    // Entities with name changes only
                    for (var i = 0; i < checkLength; i++)
                    {
                        if (NameChangeBitsByEntity.IsSet(i))
                        {
                            var entity = new Entity
                            {
                                Index = i,
                                Version = AfterEntityComponentStore->GetEntityVersionByIndex(i)
                            };

                            if (TryGetEntityGuid(AfterEntityComponentStore, entity, out var entityGuid))
                            {
                                if (!entitiesLookup.Contains(entityGuid))
                                {
                                    AfterEntityComponentStore->GetName(entity, out namesPtr[nameIndex++]);
                                    entityGuidsPtr[guidIndex++] = entityGuid;
                                }
                            }
                        }
                    }
                }

                // Destroyed entities will always show up in the entityGuid set so we can grab the rest of those names.
                // They will not exist in the after world so use the before world.
                for (var i = 0; i < DestroyedEntities.Length; i++)
                {
                    var beforeEntity = ChunkDataUtility.GetEntityFromEntityInChunk(DestroyedEntities[i].BeforeEntityInChunk);
                    BeforeEntityComponentStore->GetName(beforeEntity, out namesPtr[nameIndex++]);
                }

                *NameChangeCount = guidIndex;
                entitiesLookup.Dispose();
            }
        }
#endif

        /// <summary>
        /// This method returns all entity names for the given array of entityGuid components.
        /// </summary>
        /// <remarks>
        /// This method relies on the source buffers the entityGuids was built from. While this could technically be done
        /// while building the entityGuid set, it's a bit more isolated this way so we can remove it easily in the future.
        /// </remarks>
        static NameChangeSet GetEntityNames(
            NativeList<CreatedEntity> createdEntities,
            NativeList<DestroyedEntity> destroyedEntities,
            NativeList<NameModifiedEntity> nameModifiedEntities,
            EntityManager afterEntityManager,
            EntityManager beforeEntityManager,
            Allocator allocator)
        {
            s_GetEntityNamesProfilerMarker.Begin();

            var length = createdEntities.Length + destroyedEntities.Length + nameModifiedEntities.Length;

// TODO: remove UNITY_EDITOR conditional once https://unity3d.atlassian.net/browse/DOTS-3862 is fixed
#if UNITY_EDITOR && !DOTS_DISABLE_DEBUG_NAMES
            var nameChangeBitsByEntity = afterEntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->NameChangeBitsByEntity;
            length += nameChangeBitsByEntity.CountBits(0, nameChangeBitsByEntity.Length);
#endif

            // No entity name changes
            if (length == 0)
            {
                s_GetEntityNamesProfilerMarker.End();
                return new NameChangeSet(0, allocator);
            }

            var namesChanges = new NameChangeSet(length, allocator);
            int nameChangeCount = 0;

// TODO: remove UNITY_EDITOR conditional once https://unity3d.atlassian.net/browse/DOTS-3862 is fixed
#if UNITY_EDITOR && !DOTS_DISABLE_DEBUG_NAMES
            new GetEntityNamesJob
            {
                EntityGuidTypeIndex = TypeManager.GetTypeIndex<EntityGuid>(),
                NameChanges = namesChanges,
                NameChangeCount = &nameChangeCount,
                CreatedEntities = createdEntities,
                DestroyedEntities = destroyedEntities,
                NameModifiedEntities = nameModifiedEntities,
                NameChangeBitsByEntity = nameChangeBitsByEntity,
                AfterEntityComponentStore = afterEntityManager.GetCheckedEntityDataAccess()->EntityComponentStore,
                BeforeEntityComponentStore = beforeEntityManager.GetCheckedEntityDataAccess()->EntityComponentStore
            }.RunWithoutJobs();
#endif
            namesChanges.NameChangedCount = nameChangeCount;

            s_GetEntityNamesProfilerMarker.End();
            return namesChanges;
        }

#if !UNITY_DOTSRUNTIME
    class ManagedObjectPatches :
            PropertyVisitor,
            Properties.Adapters.IVisit<Entity>,
            Properties.Adapters.IVisit<BlobAssetReferenceData>
        {
            readonly EntityComponentStore* m_EntityComponentStore;

            PackedComponent m_Component;
            NativeList<EntityReferenceChange> m_EntityReferencePatches;
            NativeList<BlobAssetReferenceChange> m_BlobAssetReferencePatches;
            int m_EntityReferencePatchId;
            int m_BlobAssetReferencePatchId;

            public ManagedObjectPatches(EntityComponentStore* entityComponentStore)
            {
                m_EntityComponentStore = entityComponentStore;
                AddAdapter(this);
            }

            public void ExtractPatches(
                ref object value,
                PackedComponent component,
                NativeList<EntityReferenceChange> entityReferencePatches,
                NativeList<BlobAssetReferenceChange> blobAssetReferencePatches)
            {
                m_Component = component;
                m_EntityReferencePatches = entityReferencePatches;
                m_BlobAssetReferencePatches = blobAssetReferencePatches;
                m_EntityReferencePatchId = 0;
                m_BlobAssetReferencePatchId = 0;

                PropertyContainer.Visit(ref value, this);
            }

            protected override void VisitProperty<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value)
            {
#if !UNITY_DOTSRUNTIME
                if (typeof(UnityEngine.Object).IsAssignableFrom(typeof(TValue)))
                    return;
#endif
                
                base.VisitProperty(property, ref container, ref value);
            }

            VisitStatus Properties.Adapters.IVisit<Entity>.Visit<TContainer>(Property<TContainer, Entity> property, ref TContainer container, ref Entity value)
            {
                if (!m_EntityReferencePatches.IsCreated) return VisitStatus.Stop;

                var entityGuid = default(EntityGuid);

                if (m_EntityComponentStore->HasComponent(value, TypeManager.GetTypeIndex<EntityGuid>()))
                    entityGuid = *(EntityGuid*)m_EntityComponentStore->GetComponentDataWithTypeRO(value, TypeManager.GetTypeIndex<EntityGuid>());

                value = new Entity { Index = m_EntityReferencePatchId, Version = -1 };

                m_EntityReferencePatches.Add(new EntityReferenceChange
                {
                    Component = m_Component,
                    Offset = m_EntityReferencePatchId++,
                    Value = entityGuid
                });

                return VisitStatus.Stop;
            }

            VisitStatus Properties.Adapters.IVisit<BlobAssetReferenceData>.Visit<TContainer>(Property<TContainer, BlobAssetReferenceData> property, ref TContainer container, ref BlobAssetReferenceData value)
            {
                if (!m_BlobAssetReferencePatches.IsCreated) return VisitStatus.Stop;

                var hash = default(ulong);

                if (value.m_Ptr != null)
                    hash = value.Header->Hash;

                value.m_Align8Union = m_BlobAssetReferencePatchId;

                m_BlobAssetReferencePatches.Add(new BlobAssetReferenceChange
                {
                    Component = m_Component,
                    Offset = m_BlobAssetReferencePatchId++,
                    Value = hash
                });

                return VisitStatus.Stop;
            }
        }
#endif
    }
}
