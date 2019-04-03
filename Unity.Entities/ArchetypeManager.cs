using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    internal struct ComponentTypeInArchetype
    {
        public readonly int TypeIndex;
        public readonly int FixedArrayLength;

        public bool IsFixedArray => FixedArrayLength != -1;
        public int FixedArrayLengthMultiplier => FixedArrayLength != -1 ? FixedArrayLength : 1;

        public ComponentTypeInArchetype(ComponentType type)
        {
            TypeIndex = type.TypeIndex;
            FixedArrayLength = type.FixedArrayLength;
        }

        public static bool operator ==(ComponentTypeInArchetype lhs, ComponentTypeInArchetype rhs)
        {
            return lhs.TypeIndex == rhs.TypeIndex && lhs.FixedArrayLength == rhs.FixedArrayLength;
        }

        public static bool operator !=(ComponentTypeInArchetype lhs, ComponentTypeInArchetype rhs)
        {
            return lhs.TypeIndex != rhs.TypeIndex || lhs.FixedArrayLength != rhs.FixedArrayLength;
        }

        public static bool operator <(ComponentTypeInArchetype lhs, ComponentTypeInArchetype rhs)
        {
            return lhs.TypeIndex != rhs.TypeIndex
                ? lhs.TypeIndex < rhs.TypeIndex
                : lhs.FixedArrayLength < rhs.FixedArrayLength;
        }

        public static bool operator >(ComponentTypeInArchetype lhs, ComponentTypeInArchetype rhs)
        {
            return lhs.TypeIndex != rhs.TypeIndex
                ? lhs.TypeIndex > rhs.TypeIndex
                : lhs.FixedArrayLength > rhs.FixedArrayLength;
        }

        public static unsafe bool CompareArray(ComponentTypeInArchetype* type1, int typeCount1,
            ComponentTypeInArchetype* type2, int typeCount2)
        {
            if (typeCount1 != typeCount2)
                return false;
            for (var i = 0; i < typeCount1; ++i)
                if (type1[i] != type2[i])
                    return false;
            return true;
        }

        public ComponentType ToComponentType()
        {
            ComponentType type;
            type.FixedArrayLength = FixedArrayLength;
            type.TypeIndex = TypeIndex;
            type.AccessModeType = ComponentType.AccessMode.ReadWrite;
            return type;
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public override string ToString()
        {
            return ToComponentType().ToString();
        }
#endif
        public override bool Equals(object obj)
        {
            if (obj is ComponentTypeInArchetype) return (ComponentTypeInArchetype) obj == this;

            return false;
        }

        public override int GetHashCode()
        {
            return (TypeIndex * 5819) ^ FixedArrayLength;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct Chunk
    {
        // NOTE: Order of the UnsafeLinkedListNode is required to be this in order
        //       to allow for casting & grabbing Chunk* from nodes...
        public UnsafeLinkedListNode ChunkListNode; // 16 | 8
        public UnsafeLinkedListNode ChunkListWithEmptySlotsNode; // 32 | 16

        public Archetype* Archetype; // 40 | 20
        public int* SharedComponentValueArray; // 48 | 24

        // This is meant as read-only.
        // ArchetypeManager.SetChunkCount should be used to change the count.
        public int Count; // 52 | 28
        public int Capacity; // 56 | 32

        public int ManagedArrayIndex; // 60 | 36

        public int Padding0; // 64 | 40
        public uint* ChangeVersion; // 72 | 44
        public void* Padding2; // 80 | 48


        // Component data buffer
        public fixed byte Buffer[4];


        public const int kChunkSize = 16 * 1024;
        public const int kMaximumEntitiesPerChunk = kChunkSize / 8;

        public static int GetChunkBufferSize(int numComponents, int numSharedComponents)
        {
            var bufferSize = kChunkSize -
                             (sizeof(Chunk) - 4 + numSharedComponents * sizeof(int) + numComponents * sizeof(uint));
            return bufferSize;
        }

        public static int GetSharedComponentOffset(int numSharedComponents)
        {
            return kChunkSize - numSharedComponents * sizeof(int);
        }

        public static int GetChangedComponentOffset(int numComponents, int numSharedComponents)
        {
            return GetSharedComponentOffset(numSharedComponents) - numComponents * sizeof(uint);
        }

        public bool MatchesFilter(MatchingArchetypes* match, ref ComponentGroupFilter filter)
        {
            if (filter.Type == FilterType.SharedComponent)
            {
                var sharedComponentsInChunk = SharedComponentValueArray;
                var filteredCount = filter.Shared.Count;

                fixed (int* indexInComponentGroupPtr = filter.Shared.IndexInComponentGroup, sharedComponentIndexPtr =
                    filter.Shared.SharedComponentIndex)
                {
                    for (var i = 0; i < filteredCount; ++i)
                    {
                        var indexInComponentGroup = indexInComponentGroupPtr[i];
                        var sharedComponentIndex = sharedComponentIndexPtr[i];
                        var componentIndexInArcheType = match->TypeIndexInArchetypeArray[indexInComponentGroup];
                        var componentIndexInChunk = match->Archetype->SharedComponentOffset[componentIndexInArcheType];
                        if (sharedComponentsInChunk[componentIndexInChunk] != sharedComponentIndex)
                            return false;
                    }
                }

                return true;
            }

            if (filter.Type == FilterType.Changed)
            {
                var changedCount = filter.Changed.Count;

                var requiredVersion = filter.RequiredChangeVersion;
                fixed (int* indexInComponentGroupPtr = filter.Changed.IndexInComponentGroup)
                {
                    for (var i = 0; i < changedCount; ++i)
                    {
                        var indexInArchetype = match->TypeIndexInArchetypeArray[indexInComponentGroupPtr[i]];

                        var changeVersion = ChangeVersion[indexInArchetype];
                        if (ChangeVersionUtility.DidChange(changeVersion, requiredVersion))
                            return true;
                    }
                }

                return false;
            }

            return true;
        }

        public int GetSharedComponentIndex(MatchingArchetypes* match, int indexInComponentGroup)
        {
            var sharedComponentsInChunk = SharedComponentValueArray;

            var componentIndexInArcheType = match->TypeIndexInArchetypeArray[indexInComponentGroup];
            var componentIndexInChunk = match->Archetype->SharedComponentOffset[componentIndexInArcheType];
            return sharedComponentsInChunk[componentIndexInChunk];
        }
    }

    internal unsafe struct Archetype
    {
        public UnsafeLinkedListNode ChunkList;
        public UnsafeLinkedListNode ChunkListWithEmptySlots;

        public int EntityCount;
        public int ChunkCapacity;
        public int ChunkCount;

        public ComponentTypeInArchetype* Types;
        public int TypesCount;

        // Index matches archetype types
        public int* Offsets;
        public int* SizeOfs;

        // TypesCount indices into Types/Offsets/SizeOfs in the order that the
        // components are laid out in memory.
        public int* TypeMemoryOrder;

        public int* ManagedArrayOffset;
        public int NumManagedArrays;

        public int* SharedComponentOffset;
        public int NumSharedComponents;

        public Archetype* PrevArchetype;
        
        public bool SystemStateCleanupComplete;
        public bool SystemStateCleanupNeeded;
    }

    internal unsafe class ArchetypeManager : IDisposable
    {
        private readonly UnsafeLinkedListNode* m_EmptyChunkPool;

        private readonly SharedComponentDataManager m_SharedComponentManager;
        private ChunkAllocator m_ArchetypeChunkAllocator;

        internal Archetype* m_LastArchetype;
        private ManagedArrayStorage[] m_ManagedArrays = new ManagedArrayStorage[1];
        private NativeMultiHashMap<uint, IntPtr> m_TypeLookup;

        public ArchetypeManager(SharedComponentDataManager sharedComponentManager)
        {
            m_SharedComponentManager = sharedComponentManager;
            m_TypeLookup = new NativeMultiHashMap<uint, IntPtr>(256, Allocator.Persistent);

            m_EmptyChunkPool = (UnsafeLinkedListNode*) m_ArchetypeChunkAllocator.Allocate(sizeof(UnsafeLinkedListNode),
                UnsafeUtility.AlignOf<UnsafeLinkedListNode>());
            UnsafeLinkedListNode.InitializeList(m_EmptyChunkPool);

#if UNITY_ASSERTIONS
            // Buffer should be 16 byte aligned to ensure component data layout itself can gurantee being aligned
            var offset = UnsafeUtility.GetFieldOffset(typeof(Chunk).GetField("Buffer"));
            Assert.IsTrue(offset % 16 == 0, "Chunk buffer must be 16 byte aligned");
#endif
        }

        public void Dispose()
        {
            // Move all chunks to become pooled chunks
            while (m_LastArchetype != null)
            {
                while (!m_LastArchetype->ChunkList.IsEmpty)
                {
                    var chunk = (Chunk*)m_LastArchetype->ChunkList.Begin;
                    SetChunkCount(chunk, 0);
                }

                m_LastArchetype = m_LastArchetype->PrevArchetype;
            }

            // And all pooled chunks
            while (!m_EmptyChunkPool->IsEmpty)
            {
                var chunk = m_EmptyChunkPool->Begin;
                chunk->Remove();
                UnsafeUtility.Free(chunk, Allocator.Persistent);
            }

            m_ManagedArrays = null;
            m_TypeLookup.Dispose();
            m_ArchetypeChunkAllocator.Dispose();
        }

        private void DeallocateManagedArrayStorage(int index)
        {
            Assert.IsTrue(m_ManagedArrays[index].ManagedArray != null);
            m_ManagedArrays[index].ManagedArray = null;
        }

        private int AllocateManagedArrayStorage(int length)
        {
            for (var i = 0; i < m_ManagedArrays.Length; i++)
                if (m_ManagedArrays[i].ManagedArray == null)
                {
                    m_ManagedArrays[i].ManagedArray = new object[length];
                    return i;
                }

            var oldLength = m_ManagedArrays.Length;
            Array.Resize(ref m_ManagedArrays, m_ManagedArrays.Length * 2);

            m_ManagedArrays[oldLength].ManagedArray = new object[length];

            return oldLength;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void AssertArchetypeComponents(ComponentTypeInArchetype* types, int count)
        {
            if (count < 1)
                throw new ArgumentException($"Invalid component count");
            if (types[0].TypeIndex == 0)
                throw new ArgumentException($"Component type may not be null");
            if (types[0].TypeIndex != TypeManager.GetTypeIndex<Entity>())
                throw new ArgumentException($"The Entity ID must always be the first component");

            for (var i = 1; i < count; i++)
            {
                if (!TypeManager.IsValidComponentTypeForArchetype(types[i].TypeIndex, types[i].IsFixedArray))
                    throw new ArgumentException($"{types[i]} is not a valid component type.");
                if (types[i - 1].TypeIndex == types[i].TypeIndex)
                    throw new ArgumentException(
                        $"It is not allowed to have two components of the same type on the same entity. ({types[i - 1]} and {types[i]})");
            }
        }

        public Archetype* GetExistingArchetype(ComponentTypeInArchetype* types, int count)
        {
            IntPtr typePtr;
            NativeMultiHashMapIterator<uint> it;

            if (!m_TypeLookup.TryGetFirstValue(GetHash(types, count), out typePtr, out it))
                return null;

            do
            {
                var type = (Archetype*) typePtr;
                if (ComponentTypeInArchetype.CompareArray(type->Types, type->TypesCount, types, count))
                    return type;
            } while (m_TypeLookup.TryGetNextValue(out typePtr, ref it));

            return null;
        }

        private static uint GetHash(ComponentTypeInArchetype* types, int count)
        {
            var hash = HashUtility.Fletcher32((ushort*) types,
                count * sizeof(ComponentTypeInArchetype) / sizeof(ushort));
            return hash;
        }

        public Archetype* GetOrCreateArchetype(ComponentTypeInArchetype* types, int count,
            EntityGroupManager groupManager)
        {
            var type = GetExistingArchetype(types, count);
            if (type != null)
                return type;

            AssertArchetypeComponents(types, count);

            // This is a new archetype, allocate it and add it to the hash map
            type = (Archetype*) m_ArchetypeChunkAllocator.Allocate(sizeof(Archetype), 8);
            type->TypesCount = count;
            type->Types =
                (ComponentTypeInArchetype*) m_ArchetypeChunkAllocator.Construct(
                    sizeof(ComponentTypeInArchetype) * count, 4, types);
            type->EntityCount = 0;
            type->ChunkCount = 0;

            type->NumSharedComponents = 0;
            type->SharedComponentOffset = null;

            for (var i = 0; i < count; ++i)
                if (TypeManager.GetComponentType(types[i].TypeIndex).Category ==
                    TypeManager.TypeCategory.ISharedComponentData)
                    ++type->NumSharedComponents;

            var chunkDataSize = Chunk.GetChunkBufferSize(type->TypesCount, type->NumSharedComponents);

            // FIXME: proper alignment
            type->Offsets = (int*) m_ArchetypeChunkAllocator.Allocate(sizeof(int) * count, 4);
            type->SizeOfs = (int*) m_ArchetypeChunkAllocator.Allocate(sizeof(int) * count, 4);
            type->TypeMemoryOrder = (int*) m_ArchetypeChunkAllocator.Allocate(sizeof(int) * count, 4);

            var bytesPerInstance = 0;

            for (var i = 0; i < count; ++i)
            {
                var cType = TypeManager.GetComponentType(types[i].TypeIndex);
                var sizeOf = cType.SizeInChunk * types[i].FixedArrayLengthMultiplier;
                type->SizeOfs[i] = sizeOf;

                bytesPerInstance += sizeOf;
            }

            type->ChunkCapacity = chunkDataSize / bytesPerInstance;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (bytesPerInstance > chunkDataSize)
                throw new ArgumentException(
                    $"Entity archetype component data is too large. The maximum component data is {chunkDataSize} but the component data is {bytesPerInstance}");

            Assert.IsTrue(Chunk.kMaximumEntitiesPerChunk >= type->ChunkCapacity);
#endif

            // For serialization a stable ordering of the components in the
            // chunk is desired. The type index is not stable, since it depends
            // on the order in which types are added to the TypeManager.
            // A permutation of the types ordered by a TypeManager-generated
            // memory ordering is used instead.
            var memoryOrderings = new NativeArray<UInt64>(count, Allocator.Temp);
            for (int i = 0; i < count; ++i)
                memoryOrderings[i] = TypeManager.GetComponentType(types[i].TypeIndex).MemoryOrdering;
            for (int i = 0; i < count; ++i)
            {
                int index = i;
                while (index > 1 && memoryOrderings[i] < memoryOrderings[type->TypeMemoryOrder[index - 1]])
                {
                    type->TypeMemoryOrder[index] = type->TypeMemoryOrder[index - 1];
                    --index;
                }
                type->TypeMemoryOrder[index] = i;
            }
            memoryOrderings.Dispose();

            var usedBytes = 0;
            for (var i = 0; i < count; ++i)
            {
                var index = type->TypeMemoryOrder[i];
                var sizeOf = type->SizeOfs[index];

                type->Offsets[index] = usedBytes;

                usedBytes += sizeOf * type->ChunkCapacity;
            }

            type->NumManagedArrays = 0;
            type->ManagedArrayOffset = null;

            for (var i = 0; i < count; ++i)
                if (TypeManager.GetComponentType(types[i].TypeIndex).Category == TypeManager.TypeCategory.Class)
                    ++type->NumManagedArrays;

            if (type->NumManagedArrays > 0)
            {
                type->ManagedArrayOffset = (int*) m_ArchetypeChunkAllocator.Allocate(sizeof(int) * count, 4);
                var mi = 0;
                for (var i = 0; i < count; ++i)
                {
                    var cType = TypeManager.GetComponentType(types[i].TypeIndex);
                    if (cType.Category == TypeManager.TypeCategory.Class)
                        type->ManagedArrayOffset[i] = mi++;
                    else
                        type->ManagedArrayOffset[i] = -1;
                }
            }

            if (type->NumSharedComponents > 0)
            {
                type->SharedComponentOffset = (int*) m_ArchetypeChunkAllocator.Allocate(sizeof(int) * count, 4);
                var mi = 0;
                for (var i = 0; i < count; ++i)
                {
                    var cType = TypeManager.GetComponentType(types[i].TypeIndex);
                    if (cType.Category == TypeManager.TypeCategory.ISharedComponentData)
                        type->SharedComponentOffset[i] = mi++;
                    else
                        type->SharedComponentOffset[i] = -1;
                }
            }

            // Update the list of all created archetypes
            type->PrevArchetype = m_LastArchetype;
            m_LastArchetype = type;
            
            UnsafeLinkedListNode.InitializeList(&type->ChunkList);
            UnsafeLinkedListNode.InitializeList(&type->ChunkListWithEmptySlots);

            m_TypeLookup.Add(GetHash(types, count), (IntPtr) type);

            type->SystemStateCleanupComplete = ArchetypeSystemStateCleanupComplete(type);
            type->SystemStateCleanupNeeded = ArchetypeSystemStateCleanupNeeded(type);

            groupManager.OnArchetypeAdded(type);

            return type;
        }
        
        private bool ArchetypeSystemStateCleanupComplete(Archetype* archetype)
        {
            if (archetype->TypesCount == 2 && archetype->Types[1].TypeIndex == TypeManager.GetTypeIndex<CleanupEntity>()) return true;
            return false;
        }

        private bool ArchetypeSystemStateCleanupNeeded(Archetype* archetype)
        {
            for (var t = 1; t < archetype->TypesCount; ++t)
            {
                var typeIndex = archetype->Types[t].TypeIndex;
                var systemStateType =
                    typeof(ISystemStateComponentData).IsAssignableFrom(TypeManager.GetType(typeIndex));
                var systemStateSharedType =
                    typeof(ISystemStateSharedComponentData).IsAssignableFrom(TypeManager.GetType(typeIndex));

                if (systemStateType || systemStateSharedType)
                {
                    return true;
                }
            }

            return false;
        }

        public static Chunk* GetChunkFromEmptySlotNode(UnsafeLinkedListNode* node)
        {
            return (Chunk*) (node - 1);
        }

        public Chunk* AllocateChunk(Archetype* archetype, int* sharedComponentDataIndices)
        {
            var buffer = (byte*) UnsafeUtility.Malloc(Chunk.kChunkSize, 64, Allocator.Persistent);
            var chunk = (Chunk*) buffer;
            ConstructChunk(archetype, chunk, sharedComponentDataIndices);
            return chunk;
        }

        public static void CopySharedComponentDataIndexArray(int* dest, int* src, int count)
        {
            if (src == null)
                for (var i = 0; i < count; ++i)
                    dest[i] = 0;
            else
                for (var i = 0; i < count; ++i)
                    dest[i] = src[i];
        }

        public void AddExistingChunk(Chunk* chunk)
        {
            var archetype = chunk->Archetype;
            archetype->ChunkList.Add(&chunk->ChunkListNode);
            archetype->EntityCount += chunk->Count;
            for (var i = 0; i < archetype->NumSharedComponents; ++i)
                m_SharedComponentManager.AddReference(chunk->SharedComponentValueArray[i]);
        }

        public void ConstructChunk(Archetype* archetype, Chunk* chunk, int* sharedComponentDataIndices)
        {
            chunk->Archetype = archetype;

            chunk->Count = 0;
            chunk->Capacity = archetype->ChunkCapacity;
            chunk->ChunkListNode = new UnsafeLinkedListNode();
            chunk->ChunkListWithEmptySlotsNode = new UnsafeLinkedListNode();
            chunk->SharedComponentValueArray =
                (int*) ((byte*) chunk + Chunk.GetSharedComponentOffset(archetype->NumSharedComponents));
            chunk->ChangeVersion = (uint*) ((byte*) chunk +
                                            Chunk.GetChangedComponentOffset(archetype->TypesCount,
                                                archetype->NumSharedComponents));

            archetype->ChunkList.Add(&chunk->ChunkListNode);
            archetype->ChunkCount += 1;
            archetype->ChunkListWithEmptySlots.Add(&chunk->ChunkListWithEmptySlotsNode);

            Assert.IsTrue(!archetype->ChunkList.IsEmpty);
            Assert.IsTrue(!archetype->ChunkListWithEmptySlots.IsEmpty);

            Assert.IsTrue(chunk == (Chunk*) archetype->ChunkList.Back);
            Assert.IsTrue(chunk == GetChunkFromEmptySlotNode(archetype->ChunkListWithEmptySlots.Back));

            if (archetype->NumManagedArrays > 0)
                chunk->ManagedArrayIndex = AllocateManagedArrayStorage(archetype->NumManagedArrays * chunk->Capacity);
            else
                chunk->ManagedArrayIndex = -1;

            for (var i = 0; i < archetype->TypesCount; i++)
                chunk->ChangeVersion[i] = 0;

            if (archetype->NumSharedComponents <= 0)
                return;

            var sharedComponentValueArray = chunk->SharedComponentValueArray;
            CopySharedComponentDataIndexArray(sharedComponentValueArray, sharedComponentDataIndices,
                chunk->Archetype->NumSharedComponents);

            if (sharedComponentDataIndices == null)
                return;

            for (var i = 0; i < archetype->NumSharedComponents; ++i)
                m_SharedComponentManager.AddReference(sharedComponentValueArray[i]);
        }

        private static bool ChunkHasSharedComponents(Chunk* chunk, int* sharedComponentDataIndices)
        {
            var sharedComponentValueArray = chunk->SharedComponentValueArray;
            var numSharedComponents = chunk->Archetype->NumSharedComponents;
            if (sharedComponentDataIndices == null)
            {
                for (var i = 0; i < numSharedComponents; ++i)
                    if (sharedComponentValueArray[i] != 0)
                        return false;
            }
            else
            {
                for (var i = 0; i < numSharedComponents; ++i)
                    if (sharedComponentValueArray[i] != sharedComponentDataIndices[i])
                        return false;
            }
            return true;
        }

        public Chunk* GetChunkWithEmptySlots(Archetype* archetype, int* sharedComponentDataIndices)
        {
            // Try existing archetype chunks
            if (!archetype->ChunkListWithEmptySlots.IsEmpty)
            {
                if (archetype->NumSharedComponents == 0)
                {
                    var chunk = GetChunkFromEmptySlotNode(archetype->ChunkListWithEmptySlots.Begin);
                    Assert.AreNotEqual(chunk->Count, chunk->Capacity);
                    return chunk;
                }

                var end = archetype->ChunkListWithEmptySlots.End;
                for (var it = archetype->ChunkListWithEmptySlots.Begin; it != end; it = it->Next)
                {
                    var chunk = GetChunkFromEmptySlotNode(it);
                    Assert.AreNotEqual(chunk->Count, chunk->Capacity);
                    if (ChunkHasSharedComponents(chunk, sharedComponentDataIndices)) return chunk;
                }
            }

            // Try empty chunk pool
            if (m_EmptyChunkPool->IsEmpty)
                return AllocateChunk(archetype, sharedComponentDataIndices);

            var pooledChunk = (Chunk*) m_EmptyChunkPool->Begin;
            pooledChunk->ChunkListNode.Remove();

            ConstructChunk(archetype, pooledChunk, sharedComponentDataIndices);
            return pooledChunk;

            // Allocate new chunk
        }

        public int AllocateIntoChunk(Chunk* chunk)
        {
            int outIndex;
            var res = AllocateIntoChunk(chunk, 1, out outIndex);
            Assert.AreEqual(1, res);
            return outIndex;
        }

        public int AllocateIntoChunk(Chunk* chunk, int count, out int outIndex)
        {
            var allocatedCount = Math.Min(chunk->Capacity - chunk->Count, count);
            outIndex = chunk->Count;
            SetChunkCount(chunk, chunk->Count + allocatedCount);
            chunk->Archetype->EntityCount += allocatedCount;
            return allocatedCount;
        }

        public void SetChunkCount(Chunk* chunk, int newCount)
        {
            Assert.AreNotEqual(newCount, chunk->Count);

            var capacity = chunk->Capacity;

            // Chunk released to empty chunk pool
            if (newCount == 0)
            {
                // Remove references to shared components
                if (chunk->Archetype->NumSharedComponents > 0)
                {
                    var sharedComponentValueArray = chunk->SharedComponentValueArray;

                    for (var i = 0; i < chunk->Archetype->NumSharedComponents; ++i)
                        m_SharedComponentManager.RemoveReference(sharedComponentValueArray[i]);
                }

                if (chunk->ManagedArrayIndex != -1)
                {
                    DeallocateManagedArrayStorage(chunk->ManagedArrayIndex);
                    chunk->ManagedArrayIndex = -1;
                }

                chunk->Archetype = null;
                chunk->ChunkListNode.Remove();
                chunk->ChunkListWithEmptySlotsNode.Remove();

                m_EmptyChunkPool->Add(&chunk->ChunkListNode);
            }
            // Chunk is now full
            else if (newCount == capacity)
            {
                chunk->ChunkListWithEmptySlotsNode.Remove();
            }
            // Chunk is no longer full
            else if (chunk->Count == capacity)
            {
                Assert.IsTrue(newCount < chunk->Count);

                chunk->Archetype->ChunkListWithEmptySlots.Add(&chunk->ChunkListWithEmptySlotsNode);
            }

            chunk->Count = newCount;
        }

        public object GetManagedObject(Chunk* chunk, ComponentType type, int index)
        {
            var typeOfs = ChunkDataUtility.GetIndexInTypeArray(chunk->Archetype, type.TypeIndex);
            if (typeOfs < 0 || chunk->Archetype->ManagedArrayOffset[typeOfs] < 0)
                throw new InvalidOperationException("Trying to get managed object for non existing component");
            return GetManagedObject(chunk, typeOfs, index);
        }

        internal object GetManagedObject(Chunk* chunk, int type, int index)
        {
            var managedStart = chunk->Archetype->ManagedArrayOffset[type] * chunk->Capacity;
            return m_ManagedArrays[chunk->ManagedArrayIndex].ManagedArray[index + managedStart];
        }

        public object[] GetManagedObjectRange(Chunk* chunk, int type, out int rangeStart, out int rangeLength)
        {
            rangeStart = chunk->Archetype->ManagedArrayOffset[type] * chunk->Capacity;
            rangeLength = chunk->Count;
            return m_ManagedArrays[chunk->ManagedArrayIndex].ManagedArray;
        }

        public void SetManagedObject(Chunk* chunk, int type, int index, object val)
        {
            var managedStart = chunk->Archetype->ManagedArrayOffset[type] * chunk->Capacity;
            m_ManagedArrays[chunk->ManagedArrayIndex].ManagedArray[index + managedStart] = val;
        }

        public void SetManagedObject(Chunk* chunk, ComponentType type, int index, object val)
        {
            var typeOfs = ChunkDataUtility.GetIndexInTypeArray(chunk->Archetype, type.TypeIndex);
            if (typeOfs < 0 || chunk->Archetype->ManagedArrayOffset[typeOfs] < 0)
                throw new InvalidOperationException("Trying to set managed object for non existing component");
            SetManagedObject(chunk, typeOfs, index, val);
        }

        public static void MoveChunks(ArchetypeManager srcArchetypeManager, EntityDataManager* srcEntityDataManager,
            SharedComponentDataManager srcSharedComponents, ArchetypeManager dstArchetypeManager,
            EntityGroupManager dstGroupManager, SharedComponentDataManager dstSharedComponentDataManager,
            EntityDataManager* dstEntityDataManager, SharedComponentDataManager dstSharedComponents)
        {
            var entityRemapping = new NativeArray<EntityRemapUtility.EntityRemapInfo>(srcEntityDataManager->Capacity, Allocator.Temp);
            var entityPatches = new NativeList<EntityRemapUtility.EntityPatchInfo>(128, Allocator.Temp);

            dstEntityDataManager->AllocateEntitiesForRemapping(srcEntityDataManager, ref entityRemapping);

            var srcArchetype = srcArchetypeManager.m_LastArchetype;
            while (srcArchetype != null)
            {
                if (srcArchetype->EntityCount != 0)
                {
                    if (srcArchetype->NumManagedArrays != 0)
                        throw new ArgumentException("MoveEntitiesFrom is not supported with managed arrays");
                    var dstArchetype = dstArchetypeManager.GetOrCreateArchetype(srcArchetype->Types,
					    srcArchetype->TypesCount, dstGroupManager);

                    entityPatches.Clear();
                    for (var i = 1; i != dstArchetype->TypesCount; i++)
                        EntityRemapUtility.AppendEntityPatches(ref entityPatches,
						TypeManager.GetComponentType(dstArchetype->Types[i].TypeIndex).EntityOffsets,
						dstArchetype->Offsets[i], dstArchetype->SizeOfs[i]);

                    for (var c = srcArchetype->ChunkList.Begin;c != srcArchetype->ChunkList.End;c = c->Next)
                    {
                        var chunk = (Chunk*) c;

                        dstEntityDataManager->RemapChunk(dstArchetype, chunk, 0, chunk->Count, ref entityRemapping);
                        EntityRemapUtility.PatchEntities(ref entityPatches, chunk->Buffer, chunk->Count, ref entityRemapping);

                        chunk->Archetype = dstArchetype;

                        if (dstArchetype->NumSharedComponents > 0)
                            dstSharedComponents.MoveSharedComponents(srcSharedComponents,
                                chunk->SharedComponentValueArray, dstArchetype->NumSharedComponents);
                    }

                    UnsafeLinkedListNode.InsertListBefore(dstArchetype->ChunkList.End, &srcArchetype->ChunkList);
                    if (!srcArchetype->ChunkListWithEmptySlots.IsEmpty)
                        UnsafeLinkedListNode.InsertListBefore(dstArchetype->ChunkListWithEmptySlots.End,
						    &srcArchetype->ChunkListWithEmptySlots);

                    dstArchetype->EntityCount += srcArchetype->EntityCount;
                    dstArchetype->ChunkCount += srcArchetype->ChunkCount;
                    srcArchetype->EntityCount = 0;
                    srcArchetype->ChunkCount = 0;
                }

                srcArchetype = srcArchetype->PrevArchetype;
            }

            srcEntityDataManager->FreeAllEntities();

            entityRemapping.Dispose();
            entityPatches.Dispose();
        }

        public int CheckInternalConsistency()
        {
            var archetype = m_LastArchetype;
            var totalCount = 0;
            while (archetype != null)
            {
                var countInArchetype = 0;
                var chunkCount = 0;
                for (var c = archetype->ChunkList.Begin; c != archetype->ChunkList.End; c = c->Next)
                {
                    var chunk = (Chunk*) c;
                    Assert.IsTrue(chunk->Archetype == archetype);
                    Assert.IsTrue(chunk->Capacity >= chunk->Count);
                    Assert.AreEqual(chunk->ChunkListWithEmptySlotsNode.IsInList, chunk->Capacity != chunk->Count);

                    countInArchetype += chunk->Count;
                    chunkCount++;
                }

                Assert.AreEqual(countInArchetype, archetype->EntityCount);
                Assert.AreEqual(chunkCount, archetype->ChunkCount);

                totalCount += countInArchetype;
                archetype = archetype->PrevArchetype;
            }

            return totalCount;
        }

        internal SharedComponentDataManager GetSharedComponentDataManager()
        {
            return m_SharedComponentManager;
        }

        private struct ManagedArrayStorage
        {
            public object[] ManagedArray;
        }
    }
}
