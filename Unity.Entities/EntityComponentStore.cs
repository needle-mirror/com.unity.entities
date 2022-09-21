using System;
using System.Diagnostics;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Assertions;
using Unity.Mathematics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Unity.Burst.CompilerServices;

// ---------------------------------------------------------------------------------------------------------
// EntityComponentStore
// ---------------------------------------------------------------------------------------------------------
// - Internal interface to Archetype, Entity, and Chunk data.
// - Throwing exceptions in this code not supported. (Can be called from burst delegate from main thread.)
// ---------------------------------------------------------------------------------------------------------

// Notes on upcoming changes to EntityComponentStore:
//
// Checklist @macton Where is entityComponentStore and the EntityBatch interface going?
// [ ] Replace all internal interfaces to entityComponentStore to work with EntityBatch via entityComponentStore
//   [x] Convert AddComponent NativeArray<Entity>
//   [x] Convert AddComponent NativeArray<ArchetypeChunk>
//   [x] Convert AddSharedComponent NativeArray<ArchetypeChunk>
//   [x] Convert AddChunkComponent NativeArray<ArchetypeChunk>
//   [x] Move AddComponents(entity)
//   [ ] Need AddComponents for NativeList<EntityBatch>
//   [ ] Convert DestroyEntities
//   [x] Convert RemoveComponent NativeArray<ArchetypeChunk>
//   [x] Convert RemoveComponent Entity
// [x] EntityDataManager just becomes thin shim on top of EntityComponentStore
// [x] Remove EntityDataManager
// [x] Rework internal storage so that structural changes are blittable (and burst job)
// [ ] Expose EntityBatch interface public via EntityManager
// [ ] Other structural interfaces (e.g. NativeArray<Entity>) are then (optional) utility functions.
//
// 1. Ideally EntityComponentStore is the internal interface that EntityCommandBuffer can use (fast).
// 2. That would be the only access point for SystemBase.
// 3. "Easy Mode" can have (the equivalent) of EntityManager as utility functions on EntityComponentStore.
// 4. EntityDataManager goes away.
//
// Input data protocol to support for structural changes:
//    1. NativeList<EntityBatch>
//    2. NativeArray<ArchetypeChunk>
//    3. Entity
//
// Expected public (internal) API:
//
// ** Add Component **
//
// IComponentData and ISharedComponentData can be added via:
//    AddComponent NativeList<EntityBatch>
//    AddComponent Entity
//    AddComponents NativeList<EntityBatch>
//    AddComponents Entity
//
// Chunk Components can only be added via;
//    AddChunkComponent NativeArray<ArchetypeChunk>
//
// Alternative to add ISharedComponeentData when changing whole chunks.
//    AddSharedComponent NativeArray<ArchetypeChunk>
//
// ** Remove Component **
//
// Any component type can be removed via:
//    RemoveComponent NativeList<EntityBatch>
//    RemoveComponent Entity
//    RemoveComponent NativeArray<ArchetypeChunk>
//    RemoveComponents NativeList<EntityBatch>
//    RemoveComponents Entity
//    RemoveComponents NativeArray<ArchetypeChunk>


namespace Unity.Entities
{
    internal unsafe struct ManagedDeferredCommands : IDisposable
    {
        private EntityComponentStore* ECS;
        public UnsafeAppendBuffer CommandBuffer;
        public bool Empty => CommandBuffer.IsEmpty;

        public enum Command
        {
            IncrementSharedComponentVersion,
            PatchManagedEntities,
            PatchManagedEntitiesForPrefabs,
            AddReference,
            RemoveReference,
            CloneManagedComponents,
            CloneCompanionComponents,
            FreeManagedComponents,
            SetManagedComponentCapacity
        }

        public void Init(EntityComponentStore* ecs)
        {
            ECS = ecs;
            CommandBuffer = new UnsafeAppendBuffer(1024, 16, Allocator.Persistent);
        }

        public void Dispose()
        {
            CommandBuffer.Dispose();
        }

        public void Reset()
        {
            CommandBuffer.Reset();
        }

        public unsafe void IncrementComponentOrderVersion(Archetype* archetype,
            SharedComponentValues sharedComponentValues)
        {
            for (var i = 0; i < archetype->NumSharedComponents; i++)
            {
                var sharedComponentIndex = sharedComponentValues[i];
                if (EntityComponentStore.IsUnmanagedSharedComponentIndex(sharedComponentIndex))
                {
                    ECS->IncrementSharedComponentVersion_Unmanaged(sharedComponentIndex);
                }
                else
                {
                    CommandBuffer.Add<int>((int)Command.IncrementSharedComponentVersion);
                    CommandBuffer.Add<int>(sharedComponentIndex);
                }
            }
        }

        public void PatchEntities(Archetype* archetype, Chunk* chunk, int entityCount,
            NativeArray<EntityRemapUtility.EntityRemapInfo> remapping)
        {
            // In every case this is called ManagedChangesTracker.Playback() is called in the same calling function.
            // There is no question of lifetime. So the pointer is safely deferred.

            CommandBuffer.Add<int>((int)Command.PatchManagedEntities);
            CommandBuffer.Add<IntPtr>((IntPtr)archetype);
            CommandBuffer.Add<IntPtr>((IntPtr)chunk);
            CommandBuffer.Add<int>(entityCount);
            CommandBuffer.Add<IntPtr>((IntPtr)remapping.GetUnsafePtr());
        }

        public void PatchEntitiesForPrefab(Archetype* archetype, Chunk* chunk, int indexInChunk, int allocatedCount,
            Entity* remapSrc, Entity* remapDst, int remappingCount, Allocator allocator)
        {
            // We are deferring the patching so we need a copy of the remapping info since we can't be certain of its lifetime.
            // We will free this ptr in the ManagedComponentStore.PatchEntitiesForPrefab call

            var numManagedComponents = archetype->NumManagedComponents;
            var totalComponentCount = numManagedComponents * allocatedCount;
            var remapSrcSize = UnsafeUtility.SizeOf<Entity>() * remappingCount;
            var remapDstSize = UnsafeUtility.SizeOf<Entity>() * remappingCount * allocatedCount;
            var managedComponentSize = totalComponentCount * sizeof(int);

            var remapSrcCopy = (byte*)Memory.Unmanaged.Allocate(remapSrcSize + remapDstSize + managedComponentSize, 16, Allocator.Temp);
            var remapDstCopy = remapSrcCopy + remapSrcSize;
            var managedComponents = (int*)(remapDstCopy + remapDstSize);

            UnsafeUtility.MemCpy(remapSrcCopy, remapSrc, remapSrcSize);
            UnsafeUtility.MemCpy(remapDstCopy, remapDst, remapDstSize);

            var firstManagedComponent = archetype->FirstManagedComponent;
            for (int i = 0; i < numManagedComponents; ++i)
            {
                int indexInArchetype = i + firstManagedComponent;

                if (archetype->Types[indexInArchetype].HasEntityReferences)
                {
                    var a = (int*)ChunkDataUtility.GetComponentDataRO(chunk, 0, indexInArchetype);
                    for (int ei = 0; ei < allocatedCount; ++ei)
                        managedComponents[ei * numManagedComponents + i] = a[ei + indexInChunk];
                }
                else
                {
                    for (int ei = 0; ei < allocatedCount; ++ei)
                        managedComponents[ei * numManagedComponents + i] = 0; // 0 means do not remap

                }
            }

            CommandBuffer.Add<int>((int)Command.PatchManagedEntitiesForPrefabs);
            CommandBuffer.Add<IntPtr>((IntPtr)remapSrcCopy);
            CommandBuffer.Add<int>(allocatedCount);
            CommandBuffer.Add<int>(remappingCount);
            CommandBuffer.Add<int>(archetype->NumManagedComponents);
            CommandBuffer.Add<int>((int)allocator);
        }

        public void AddReference(int index, int numRefs = 1)
        {
            if (index == 0)
                return;
            CommandBuffer.Add<int>((int)Command.AddReference);
            CommandBuffer.Add<int>(index);
            CommandBuffer.Add<int>(numRefs);
        }

        public void RemoveReference(int index, int numRefs = 1)
        {
            if (index == 0)
                return;
            CommandBuffer.Add<int>((int)Command.RemoveReference);
            CommandBuffer.Add<int>(index);
            CommandBuffer.Add<int>(numRefs);
        }

        public void CloneManagedComponentBegin(int* srcIndices, int componentCount, int instanceCount)
        {
            CommandBuffer.Add<int>((int)Command.CloneManagedComponents);
            CommandBuffer.AddArray<int>(srcIndices, componentCount);
            CommandBuffer.Add<int>(instanceCount);
            CommandBuffer.Add<int>(instanceCount * componentCount);
        }

        public void CloneManagedComponentAddDstIndices(int* dstIndices, int count)
        {
            CommandBuffer.Add(dstIndices, count * sizeof(int));
        }

        public void CloneCompanionComponentBegin(int* srcIndices, int componentCount, Entity* dstEntities, int instanceCount, int* dstCompanionLinkIndices)
        {
            CommandBuffer.Add<int>((int)Command.CloneCompanionComponents);
            CommandBuffer.AddArray<int>(srcIndices, componentCount);
            CommandBuffer.AddArray<Entity>(dstEntities, instanceCount);
            CommandBuffer.AddArray<int>(dstCompanionLinkIndices, dstCompanionLinkIndices == null ? 0 : instanceCount);
            CommandBuffer.Add<int>(instanceCount * componentCount);
        }

        public void CloneCompanionComponentAddDstIndices(int* dstIndices, int count)
        {
            CommandBuffer.Add(dstIndices, count * sizeof(int));
        }

        public int BeginFreeManagedComponentCommand()
        {
            CommandBuffer.Add<int>((int)Command.FreeManagedComponents);

            CommandBuffer.Add<int>(-1); // this will contain the array count
            return CommandBuffer.Length - sizeof(int);
        }

        public void AddToFreeManagedComponentCommand(int managedComponentIndex)
        {
            CommandBuffer.Add<int>(managedComponentIndex);
        }

        public void EndDeallocateManagedComponentCommand(int handle)
        {
            int count = (CommandBuffer.Length - handle) / sizeof(int) - 1;
            if (count == 0)
            {
                CommandBuffer.Length -= sizeof(int) * 2;
            }
            else
            {
                int* countInCommand = (int*)(CommandBuffer.Ptr + handle);
                Assert.AreEqual(-1, *countInCommand);
                *countInCommand = count;
            }
        }

        public void SetManagedComponentCapacity(int capacity)
        {
            CommandBuffer.Add<int>((int)Command.SetManagedComponentCapacity);
            CommandBuffer.Add<int>(capacity);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ComponentTypeList
    {
        internal void* Ptr;
        internal int Length;
        internal int Capacity;
        internal AllocatorManager.AllocatorHandle Allocator;

        internal ComponentTypeList(int sizeOf, int alignOf, int initialCapacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory) : this()
        {
            Allocator = allocator;
            Ptr = null;
            Length = 0;
            Capacity = 0;

            if (initialCapacity != 0)
            {
                SetCapacity(ref Allocator, sizeOf, alignOf, initialCapacity);
            }

            if (options == NativeArrayOptions.ClearMemory
                && Ptr != null)
            {
                UnsafeUtility.MemClear(Ptr, Capacity * sizeOf);
            }
        }

        internal bool IsEmpty => !IsCreated || Length == 0;

        internal bool IsCreated => Ptr != null;

        internal void Dispose()
        {
            if (CollectionHelper.ShouldDeallocate(Allocator))
            {
                AllocatorManager.Free(Allocator, Ptr);
                Allocator = AllocatorManager.Invalid;
            }

            Ptr = null;
            Length = 0;
            Capacity = 0;
        }

        internal void Clear()
        {
            Length = 0;
        }

        internal void Resize(int sizeOf, int alignOf, int length, NativeArrayOptions options = NativeArrayOptions.UninitializedMemory)
        {
            var oldLength = Length;

            if (length > Capacity)
            {
                SetCapacity(ref Allocator, sizeOf, alignOf, length);
            }

            Length = length;

            if (options == NativeArrayOptions.ClearMemory
                && oldLength < length)
            {
                var num = length - oldLength;
                byte* ptr = (byte*)Ptr;
                UnsafeUtility.MemClear(ptr + oldLength * sizeOf, num * sizeOf);
            }
        }

        void Realloc<U>(ref U allocator, int sizeOf, int alignOf, int capacity) where U : unmanaged, AllocatorManager.IAllocator
        {
            void* newPointer = null;

            if (capacity > 0)
            {
                newPointer = allocator.Allocate(sizeOf, alignOf, capacity);

                if (Capacity > 0)
                {
                    var itemsToCopy = math.min(capacity, Capacity);
                    var bytesToCopy = itemsToCopy * sizeOf;
                    UnsafeUtility.MemCpy(newPointer, Ptr, bytesToCopy);
                }
            }

            allocator.Free(Ptr, sizeOf, alignOf, Capacity);

            Ptr = newPointer;
            Capacity = capacity;
            Length = math.min(Length, capacity);
        }

        void SetCapacity<U>(ref U allocator, int sizeOf, int alignOf, int capacity) where U : unmanaged, AllocatorManager.IAllocator
        {
            var newCapacity = math.max(capacity, 64 / sizeOf);
            newCapacity = math.ceilpow2(newCapacity);

            if (newCapacity == Capacity)
            {
                return;
            }

            Realloc(ref allocator, sizeOf, alignOf, newCapacity);
        }
    }

    [BurstCompile]
    internal unsafe partial struct EntityComponentStore
    {
        private const int kUnmanagedSharedComponentIndexFlag = 1 << 31;

        [NativeDisableUnsafePtrRestriction]
        int* m_VersionByEntity;

        [NativeDisableUnsafePtrRestriction]
        Archetype** m_ArchetypeByEntity;

        [NativeDisableUnsafePtrRestriction]
        EntityInChunk* m_EntityInChunkByEntity;

        [NativeDisableUnsafePtrRestriction]
        int* m_ComponentTypeOrderVersion;

        BlockAllocator m_ArchetypeChunkAllocator;

        internal UnsafePtrList<Archetype> m_Archetypes;

        ArchetypeListMap m_TypeLookup;

        internal int m_ManagedComponentIndex;
        internal int m_ManagedComponentIndexCapacity;
        internal UnsafeAppendBuffer m_ManagedComponentFreeIndex;

        internal ManagedDeferredCommands ManagedChangesTracker;

        internal int m_SharedComponentVersion;
        internal int m_SharedComponentGlobalVersion;

        private int m_UnmanagedSharedComponentCount;
        internal UnsafeList<ComponentTypeList> m_UnmanagedSharedComponentsByType;

        struct SharedComponentInfo
        {
            public int RefCount;
            public int ComponentType;
            public int Version;
            public int HashCode;
        }

        private UnsafeList<TypeIndex> m_UnmanagedSharedComponentTypes;
        private UnsafeList<UnsafeList<SharedComponentInfo>> m_UnmanagedSharedComponentInfo;
        private UnsafeMultiHashMap<int, int> m_HashLookup;

        internal ChunkListChanges m_ChunkListChangesTracker;

        ulong m_WorldSequenceNumber;
        ulong m_NextChunkSequenceNumber;

        // Free list index for entity id allocation
        int  m_NextFreeEntityIndex;
        // Any entity creation / destruction, bumps this version number
        // Generally any write to m_NextFreeEntityIndex must also increment m_EntityCreateDestroyVersion
        int  m_EntityCreateDestroyVersion;

        uint m_GlobalSystemVersion;
        int  m_EntitiesCapacity;
        int  m_IntentionallyInconsistent;
        uint m_ArchetypeTrackingVersion;

        TypeIndex m_LinkedGroupType;
        TypeIndex m_ChunkHeaderType;
        TypeIndex m_PrefabType;
        TypeIndex m_CleanupEntityType;
        TypeIndex m_DisabledType;
        TypeIndex m_EntityType;
        TypeIndex m_SystemInstanceType;

        ComponentType m_ChunkHeaderComponentType;
        ComponentType m_EntityComponentType;
        ComponentType m_SimulateComponentType;

        TypeManager.TypeInfo* m_TypeInfos;
        TypeManager.EntityOffsetInfo* m_EntityOffsetInfos;

        internal int m_DebugOnlyManagedAccess;

        internal byte memoryInitPattern;
        internal byte useMemoryInitPattern;        // should be bool, but it doesn't get along nice with burst so far, so we use a byte instead
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
        internal byte m_RecordToJournal;
#endif

        const int kMaximumEmptyChunksInPool = 16; // can't alloc forever
        const int kDefaultCapacity = 1024;
        internal const int kMaxSharedComponentCount = 16;

        public ulong WorldSequenceNumber => m_WorldSequenceNumber;

        struct AddressSpaceTagType { }
        static readonly SharedStatic<ulong> s_TotalChunkAddressSpaceInBytes = SharedStatic<ulong>.GetOrCreate<AddressSpaceTagType>();

        static readonly ulong DefaultChunkAddressSpaceInBytes = 1024UL * 1024UL * 1024UL;

        public static ulong TotalChunkAddressSpaceInBytes
        {
            get => s_TotalChunkAddressSpaceInBytes.Data > 0 ? s_TotalChunkAddressSpaceInBytes.Data - 1 : DefaultChunkAddressSpaceInBytes;
            set => s_TotalChunkAddressSpaceInBytes.Data = value + 1;
        }

#if !DOTS_DISABLE_DEBUG_NAMES
        [NativeDisableUnsafePtrRestriction]
        EntityName* m_NameByEntity;
        internal EntityName* NameByEntity => m_NameByEntity;

        internal const ulong InitialNameChangeBitsSequenceNum = 1;
        ulong m_NameChangeBitsSequenceNum;
        UnsafeBitArray m_NameChangeBitsByEntity;
        public UnsafeBitArray NameChangeBitsByEntity => m_NameChangeBitsByEntity;
        public ulong NameChangeBitsSequenceNum => m_NameChangeBitsSequenceNum;
        public void IncNameChangeBitsVersion()
        {
            m_NameChangeBitsSequenceNum++;
        }

        public void SetNameChangeBitsVersion(ulong nameChangeBitsVersion)
        {
            m_NameChangeBitsSequenceNum = nameChangeBitsVersion;
        }

        public EntityName GetEntityNameByEntityIndex(int index)
        {
            if(index >= 0 && index < m_EntitiesCapacity)
            {
                return m_NameByEntity[index];
            }

            return new EntityName();
        }

        public void CopyAndUpdateNameByEntity(EntityComponentStore *fromEntityComponentStore)
        {
            Assert.IsTrue(m_EntitiesCapacity >= fromEntityComponentStore ->EntitiesCapacity,
                $"Destination entity capacity should be equal or larger than source, m_EntitiesCapacity = { m_EntitiesCapacity }, fromEntityComponentStore ->EntitiesCapacity = { fromEntityComponentStore->EntitiesCapacity }");

            long length = fromEntityComponentStore->EntitiesCapacity * sizeof(EntityName);
            UnsafeUtility.MemCpy(m_NameByEntity, fromEntityComponentStore->NameByEntity, length);

            m_NameChangeBitsSequenceNum++;
            fromEntityComponentStore->IncNameChangeBitsVersion();

            // Now the names of the entities in 2 worlds are the same.
            // Set name change sequence number to the latest one and clear name change bitmap
            var newerNameChangeBitsSequenceNum = math.max(m_NameChangeBitsSequenceNum, fromEntityComponentStore->NameChangeBitsSequenceNum);
            m_NameChangeBitsSequenceNum = newerNameChangeBitsSequenceNum;
            fromEntityComponentStore->SetNameChangeBitsVersion(newerNameChangeBitsSequenceNum);
            fromEntityComponentStore->m_NameChangeBitsByEntity.Clear();
        }
#endif // !DOTS_DISABLE_DEBUG_NAMES

        public int EntityOrderVersion => GetComponentTypeOrderVersion(m_EntityType);
        public int EntitiesCapacity => m_EntitiesCapacity;
        public uint GlobalSystemVersion => m_GlobalSystemVersion;

        public void IncrementGlobalSystemVersion(in SystemHandle handle = default)
        {
            ChangeVersionUtility.IncrementGlobalSystemVersion(ref m_GlobalSystemVersion);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            if (Hint.Unlikely(m_RecordToJournal != 0))
            {
                fixed (EntityComponentStore* store = &this)
                    EntitiesJournaling.AddSystemVersionHandle(store, m_GlobalSystemVersion, in handle);
            }
#endif
        }

        public void SetGlobalSystemVersion(uint value, in SystemHandle handle = default)
        {
            m_GlobalSystemVersion = value;

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            if (Hint.Unlikely(m_RecordToJournal != 0))
            {
                fixed (EntityComponentStore* store = &this)
                    EntitiesJournaling.AddSystemVersionHandle(store, m_GlobalSystemVersion, in handle);
            }
#endif
        }

        void IncreaseCapacity()
        {
            EnsureCapacity(m_EntitiesCapacity * 2);
        }

        internal bool IsIntentionallyInconsistent => m_IntentionallyInconsistent == 1;
        internal const long k_MaximumEntitiesPerWorld = 128L * 1024L * 1024L; // roughly 128 million Entities per World, maximum
        internal const int kUnmanagedSharedElementIndexMask = 0xFFFF;
        internal const int kUnmanagedSharedTypeIndexBitOffset = 16;

        void ResizeUnmanagedArrays(long oldValue, long newValue)
        {
            m_VersionByEntity = Memory.Unmanaged.Array.Resize(m_VersionByEntity, oldValue, newValue, Allocator.Persistent);
            IntPtr* temp = (IntPtr*) m_ArchetypeByEntity;
            m_ArchetypeByEntity = (Archetype**) Memory.Unmanaged.Array.Resize(temp, oldValue, newValue, Allocator.Persistent);
            m_EntityInChunkByEntity = Memory.Unmanaged.Array.Resize(m_EntityInChunkByEntity, oldValue, newValue, Allocator.Persistent);
#if !DOTS_DISABLE_DEBUG_NAMES
            m_NameByEntity = Memory.Unmanaged.Array.Resize(m_NameByEntity, oldValue, newValue, Allocator.Persistent);
            long nameChangeBitsArrayLength = (newValue + 7) & ~7;
            if (nameChangeBitsArrayLength > m_NameChangeBitsByEntity.Length)
            {
                var oldNameChangeBitsByEntity = m_NameChangeBitsByEntity;
                m_NameChangeBitsByEntity = new UnsafeBitArray((int)nameChangeBitsArrayLength, Allocator.Persistent);
                m_NameChangeBitsByEntity.Copy(0, ref oldNameChangeBitsByEntity, 0, oldNameChangeBitsByEntity.Length);
                oldNameChangeBitsByEntity.Dispose();
            }
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        void ThrowIfEntitiesPerWorldIsTooHigh(long newValue)
        {
            if (newValue > math.ceilpow2(k_MaximumEntitiesPerWorld))
            {
                m_IntentionallyInconsistent = 1;
                throw new InvalidOperationException(
                    $"Maximum Entities in World is {k_MaximumEntitiesPerWorld}. Attempted to allocate {newValue}.");
            }
        }
        internal void EnsureCapacity(int value)
        {
            long oldValue = m_EntitiesCapacity;
            long newValue = value;
            // Capacity can never be decreased since entity lookups would start failing as a result
            if (newValue <= oldValue)
                return;
            ThrowIfEntitiesPerWorldIsTooHigh(newValue);
            ResizeUnmanagedArrays(oldValue, newValue);
            var startNdx = 0;
            if(m_EntitiesCapacity > 0)
                startNdx = m_EntitiesCapacity - 1;
            m_EntitiesCapacity = (int)newValue;
            InitializeAdditionalCapacity(startNdx);
        }

        public void CopyNextFreeEntityIndex(EntityComponentStore* src)
        {
            m_NextFreeEntityIndex = src->m_NextFreeEntityIndex;
            m_EntityCreateDestroyVersion++;
        }

        public Entity GetEntityByEntityIndex(int index)
        {
            if (index >= 0 && index < m_EntitiesCapacity)
            {
                return new Entity { Version = m_VersionByEntity[index], Index = index };
            }

            return new Entity();
        }

        private void InitializeAdditionalCapacity(int start)
        {
            for (var i = start; i != EntitiesCapacity; i++)
            {
                m_EntityInChunkByEntity[i].IndexInChunk = i + 1;
                m_VersionByEntity[i] = 1;
                m_EntityInChunkByEntity[i].Chunk = null;
#if !DOTS_DISABLE_DEBUG_NAMES
                m_NameByEntity[i] = new EntityName();
#endif
            }

            // Last entity indexInChunk identifies that we ran out of space...
            m_EntityInChunkByEntity[EntitiesCapacity - 1].IndexInChunk = -1;

#if !DOTS_DISABLE_DEBUG_NAMES
            int numBits = m_NameChangeBitsByEntity.Length - start;
            m_NameChangeBitsByEntity.SetBits(start, false, numBits);
#endif
        }

        public static void Create(EntityComponentStore* entities, ulong worldSequenceNumber, int newCapacity = kDefaultCapacity)
        {
            UnsafeUtility.MemClear(entities, sizeof(EntityComponentStore));

#if !DOTS_DISABLE_DEBUG_NAMES
            entities->m_NameChangeBitsSequenceNum = InitialNameChangeBitsSequenceNum;
#endif

            entities->EnsureCapacity(newCapacity);
            entities->m_GlobalSystemVersion = ChangeVersionUtility.InitialGlobalSystemVersion;

            entities->m_ComponentTypeOrderVersion = Memory.Unmanaged.Array.Allocate<int>(TypeManager.MaximumTypesCount, Allocator.Persistent);
            Memory.Array.Clear(entities->m_ComponentTypeOrderVersion, TypeManager.MaximumTypesCount);

            entities->m_ArchetypeChunkAllocator = new BlockAllocator(AllocatorManager.Persistent, 16 * 1024 * 1024); // 16MB should be enough
            entities->m_TypeLookup = new ArchetypeListMap();
            entities->m_TypeLookup.Init(16);
            entities->m_WorldSequenceNumber = worldSequenceNumber;
            entities->m_NextChunkSequenceNumber = worldSequenceNumber << 32;
            entities->m_Archetypes = new UnsafePtrList<Archetype>(0, Allocator.Persistent);
            entities->ManagedChangesTracker = new ManagedDeferredCommands();
            entities->ManagedChangesTracker.Init(entities);
            entities->m_SharedComponentVersion = 0;
            entities->m_ManagedComponentIndex = 1;
            entities->m_ManagedComponentIndexCapacity = 64;
            entities->m_ManagedComponentFreeIndex = new UnsafeAppendBuffer(1024, 16, Allocator.Persistent);
            entities->m_SharedComponentGlobalVersion = 1;
            entities->m_UnmanagedSharedComponentCount = 0;
            entities->m_UnmanagedSharedComponentTypes = new UnsafeList<TypeIndex>(64, Allocator.Persistent);
            entities->m_UnmanagedSharedComponentsByType = new UnsafeList<ComponentTypeList>(TypeManager.GetTypeCount() + 1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            entities->m_UnmanagedSharedComponentInfo = new UnsafeList<UnsafeList<SharedComponentInfo>>(TypeManager.GetTypeCount(), Allocator.Persistent, NativeArrayOptions.ClearMemory);
            entities->m_HashLookup = new UnsafeMultiHashMap<int, int>(128, Allocator.Persistent);
            entities->m_LinkedGroupType = TypeManager.GetTypeIndex<LinkedEntityGroup>();
            entities->m_ChunkHeaderType = TypeManager.GetTypeIndex<ChunkHeader>();
            entities->m_PrefabType = TypeManager.GetTypeIndex<Prefab>();
            entities->m_CleanupEntityType = TypeManager.GetTypeIndex<CleanupEntity>();
            entities->m_DisabledType = TypeManager.GetTypeIndex<Disabled>();
            entities->m_EntityType = TypeManager.GetTypeIndex<Entity>();
            entities->m_SystemInstanceType = TypeManager.GetTypeIndex<SystemInstance>();

            entities->m_ChunkHeaderComponentType = ComponentType.ReadWrite<ChunkHeader>();
            entities->m_EntityComponentType = ComponentType.ReadWrite<Entity>();
            entities->m_SimulateComponentType = ComponentType.ReadWrite<Simulate>();
            entities->InitializeTypeManagerPointers();

            entities->m_ChunkListChangesTracker = new ChunkListChanges();
            entities->m_ChunkListChangesTracker.Init();

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            entities->m_RecordToJournal = (byte)(EntitiesJournaling.Enabled ? 1 : 0);
#endif

            // Sanity check a few alignments
#if UNITY_ASSERTIONS
            // Buffer should be 16 byte aligned to ensure component data layout itself can guarantee being aligned
            var offset = UnsafeUtility.GetFieldOffset(typeof(Chunk).GetField("Buffer"));
            Assert.IsTrue(offset % TypeManager.MaximumSupportedAlignment == 0, $"Chunk buffer must be {TypeManager.MaximumSupportedAlignment} byte aligned (buffer offset at {offset})");
            Assert.IsTrue(sizeof(Entity) == 8, $"Unity.Entities.Entity is expected to be 8 bytes in size (is {sizeof(Entity)}); if this changes, update Chunk explicit layout");
#endif
            var bufHeaderSize = UnsafeUtility.SizeOf<BufferHeader>();
            Assert.IsTrue(bufHeaderSize % TypeManager.MaximumSupportedAlignment == 0,
                $"BufferHeader total struct size must be a multiple of the max supported alignment ({TypeManager.MaximumSupportedAlignment})");
        }

        internal void InitializeTypeManagerPointers()
        {
            m_TypeInfos = TypeManager.GetTypeInfoPointer();
            m_EntityOffsetInfos = TypeManager.GetEntityOffsetsPointer();
        }

        public ref readonly TypeManager.TypeInfo GetTypeInfo(TypeIndex typeIndex)
        {
            return ref m_TypeInfos[typeIndex.Index];
        }

        public TypeManager.EntityOffsetInfo* GetEntityOffsets(in TypeManager.TypeInfo typeInfo)
        {
            return m_EntityOffsetInfos + typeInfo.EntityOffsetStartIndex;
        }

        public TypeIndex ChunkComponentToNormalTypeIndex(TypeIndex typeIndex) => m_TypeInfos[typeIndex.Index].TypeIndex;

        public static void Destroy(EntityComponentStore* entityComponentStore)
        {
            entityComponentStore->Dispose();
        }

        void Dispose()
        {
            if (m_EntitiesCapacity > 0)
            {
                ResizeUnmanagedArrays(m_EntitiesCapacity, 0);

                m_VersionByEntity = null;
                m_ArchetypeByEntity = null;
                m_EntityInChunkByEntity = null;
#if !DOTS_DISABLE_DEBUG_NAMES
                m_NameByEntity = null;
#endif

                m_EntitiesCapacity = 0;
            }

            if (m_ComponentTypeOrderVersion != null)
            {
                Memory.Unmanaged.Free(m_ComponentTypeOrderVersion, Allocator.Persistent);
                m_ComponentTypeOrderVersion = null;
            }

            // Move all chunks to become pooled chunks
            for (var i = 0; i < m_Archetypes.Length; i++)
            {
                var archetype = m_Archetypes.Ptr[i];

                for (int c = 0; c != archetype->Chunks.Count; c++)
                {
                    var chunk = archetype->Chunks[c];

                    ChunkDataUtility.DeallocateBuffers(chunk);
                    s_chunkStore.Data.FreeContiguousChunks(archetype->Chunks[c], 1);
                }

                archetype->Chunks.Dispose();
                archetype->ChunksWithEmptySlots.Dispose();
                archetype->FreeChunksBySharedComponents.Dispose();
                archetype->MatchingQueryData.Dispose();
            }

            m_Archetypes.Dispose();

            m_TypeLookup.Dispose();
            m_ArchetypeChunkAllocator.Dispose();
            ManagedChangesTracker.Dispose();
            m_ManagedComponentFreeIndex.Dispose();
#if !UNITY_DOTSRUNTIME
            AspectTypeInfoManager.Dispose();
#endif

            for (int i = 0; i < m_UnmanagedSharedComponentsByType.Length; i++)
            {
                if (m_UnmanagedSharedComponentsByType[i].IsCreated)
                {
                    var typeInfo = TypeManager.GetTypeInfoPointer()[i];
                    var typeIndexWithFlags = typeInfo.TypeIndex;
                    /*
                     * if it implements irefcounted, we should go through every live instance of this type that we have
                     * and call Release() on it
                     */
                    if (typeIndexWithFlags.IsRefCounted)
                    {
                        var size = typeInfo.TypeSize;
                        var infos = m_UnmanagedSharedComponentInfo.Ptr[i];

                        /*
                         * the info array for this type may have been cleared by ResetSharedComponentData, which means
                         * this entitycomponentstore no longer owns any refcounts for that typeindex. so we only try to
                         * release stuff that we have nonzero refcounts for.
                         */
                        if (infos.IsCreated)
                        {
                            for (int j = 1;
                                j < math.min(m_UnmanagedSharedComponentsByType[i].Length, infos.Length);
                                j++)
                            {
                                if (infos[j].RefCount > 0)
                                    TypeManager.CallIRefCounted_Release(typeIndexWithFlags, (IntPtr) m_UnmanagedSharedComponentsByType[i].Ptr + j * size);
                            }
                        }
                    }
                    m_UnmanagedSharedComponentsByType[i].Dispose();
                }
            }
            m_UnmanagedSharedComponentsByType.Dispose();

            for (int i = 0; i < m_UnmanagedSharedComponentInfo.Capacity; i++)
            {
                if (m_UnmanagedSharedComponentInfo.Ptr[i].IsCreated)
                {
                    m_UnmanagedSharedComponentInfo.Ptr[i].Dispose();
                }
            }
            m_UnmanagedSharedComponentInfo.Dispose();
            m_UnmanagedSharedComponentTypes.Dispose();
            m_HashLookup.Dispose();
#if !DOTS_DISABLE_DEBUG_NAMES
            m_NameChangeBitsByEntity.Dispose();
#endif
        }

        private void ResetSharedComponentData()
        {
            /*
             * we reset shared component data when we have moved all our shared components from this world to another
             * world. when that happens, we expect each shared component type index to return to having exactly one
             * entry representing the default value of that shared component. so we clear the lists for each typeindex,
             * and reset it to that state.
             */

            for (int i = 0; i < m_UnmanagedSharedComponentsByType.Length; i++)
            {
                if (m_UnmanagedSharedComponentsByType[i].IsCreated)
                {
                    m_UnmanagedSharedComponentsByType[i].Clear();
                    m_UnmanagedSharedComponentsByType.Ptr[i].Length = 1;
                }
            }

            for (int i = 0; i < m_UnmanagedSharedComponentInfo.Capacity; i++)
            {
                //have to use .Ptr and .Capacity because length of this array is always 0
                if (m_UnmanagedSharedComponentInfo.Ptr[i].IsCreated)
                {
                    m_UnmanagedSharedComponentInfo.Ptr[i].Clear();
                    m_UnmanagedSharedComponentInfo.Ptr[i].Add(new SharedComponentInfo
                        {RefCount = 1, ComponentType = -1, Version = 1, HashCode = 0});
                }
            }
            m_HashLookup.Clear();
        }

        public void FreeAllEntities(bool resetVersion)
        {
            for (var i = 0; i != EntitiesCapacity; i++)
            {
                m_EntityInChunkByEntity[i].IndexInChunk = i + 1;
                m_EntityInChunkByEntity[i].Chunk = null;
#if !DOTS_DISABLE_DEBUG_NAMES
                m_NameByEntity[i] = new EntityName();
                m_NameChangeBitsByEntity.Set(i, false);
#endif
            }

            if (resetVersion)
            {
                for (var i = 0; i != EntitiesCapacity; i++)
                    m_VersionByEntity[i] = 1;
            }
            else
            {
                for (var i = 0; i != EntitiesCapacity; i++)
                    m_VersionByEntity[i] += 1;
            }



            // Last entity indexInChunk identifies that we ran out of space...
            m_EntityInChunkByEntity[EntitiesCapacity - 1].IndexInChunk = -1;
            m_NextFreeEntityIndex = 0;
            m_EntityCreateDestroyVersion++;
        }

        public void FreeEntities(Chunk* chunk)
        {
            var count = chunk->Count;
            var entities = (Entity*)chunk->Buffer;
            int freeIndex = m_NextFreeEntityIndex;

            for (var i = 0; i != count; i++)
            {
                int index = entities[i].Index;
                m_VersionByEntity[index] += 1;
                m_EntityInChunkByEntity[index].Chunk = null;
                m_EntityInChunkByEntity[index].IndexInChunk = freeIndex;
#if !DOTS_DISABLE_DEBUG_NAMES
                m_NameByEntity[index] = new EntityName();
                m_NameChangeBitsByEntity.Set(i, false);
#endif
                freeIndex = index;
            }

            m_NextFreeEntityIndex = freeIndex;
            m_EntityCreateDestroyVersion++;
        }

        public string GetName(Entity entity)
        {
#if !DOTS_DISABLE_DEBUG_NAMES
            if (!Exists(entity))
                return "ENTITY_NOT_FOUND";
            return m_NameByEntity[entity.Index].ToString();
#else
            return "";
#endif
        }

        [GenerateTestsForBurstCompatibility]
        public void GetName(Entity entity, out FixedString64Bytes name)
        {
#if !DOTS_DISABLE_DEBUG_NAMES
            if (!Exists(entity))
            {
                name = "ENTITY_NOT_FOUND";
                return;
            }
            name = default;
            m_NameByEntity[entity.Index].ToFixedString(ref name);
#else
            name = default;
#endif
        }

        public static string Debugger_GetName(EntityComponentStore* store, Entity entity)
        {
#if !DOTS_DISABLE_DEBUG_NAMES
            if (store != null && store->m_NameByEntity != null && !Debugger_Exists(store, entity))
                return "ENTITY_NOT_FOUND";
            return store->m_NameByEntity[entity.Index].ToString();
#else
            return "";
#endif
        }

        [GenerateTestsForBurstCompatibility]
        public void SetName(Entity entity, in FixedString64Bytes name)
        {
#if !DOTS_DISABLE_DEBUG_NAMES
            if (!Exists(entity))
                return;
            m_NameByEntity[entity.Index].SetFixedString(in name);
            m_NameChangeBitsByEntity.Set(entity.Index, true);
#endif
        }

        public void CopyName(Entity dstEntity, Entity srcEntity)
        {
#if !DOTS_DISABLE_DEBUG_NAMES
            m_NameByEntity[dstEntity.Index] = m_NameByEntity[srcEntity.Index];
#endif
        }

        public Archetype* GetArchetype(Entity entity)
        {
            return m_ArchetypeByEntity[entity.Index];
        }

        public void SetArchetype(Entity entity, Archetype* archetype)
        {
            m_ArchetypeByEntity[entity.Index] = archetype;
        }

        public void SetArchetype(Chunk* srcChunk, Archetype* dstArchetype)
        {
            var entities = (Entity*)srcChunk->Buffer;
            var count = srcChunk->Count;
            for (int i = 0; i < count; ++i)
            {
                m_ArchetypeByEntity[entities[i].Index] = dstArchetype;
            }

            srcChunk->Archetype = dstArchetype;
        }

        public Chunk* GetChunk(Entity entity)
        {
            var entityChunk = m_EntityInChunkByEntity[entity.Index].Chunk;

            return entityChunk;
        }

        public void SetEntityInChunk(Entity entity, EntityInChunk entityInChunk)
        {
            m_EntityInChunkByEntity[entity.Index] = entityInChunk;
        }

        public EntityInChunk GetEntityInChunk(Entity entity)
        {
            return m_EntityInChunkByEntity[entity.Index];
        }

        public int GetEntityVersionByIndex(int index)
        {
            return m_VersionByEntity[index];
        }

        public void IncrementComponentTypeOrderVersion(Archetype* archetype)
        {
            // Increment type component version
            for (var t = 0; t < archetype->TypesCount; ++t)
            {
                var typeIndex = archetype->Types[t].TypeIndex;
                m_ComponentTypeOrderVersion[typeIndex.Index]++;
            }
        }

        public bool TryGetComponent(Entity entity, TypeIndex typeIndex, out EntityGuid entityGuid)
        {
            entityGuid = default;

            if (!HasComponent(entity, typeIndex))
            {
                return false;
            }

            entityGuid = *(EntityGuid*)GetComponentDataWithTypeRO(entity, typeIndex);
            return true;
        }

        public bool Exists(Entity entity)
        {
            int index = entity.Index;

            ValidateEntity(entity);

            var versionMatches = m_VersionByEntity[index] == entity.Version;
            var hasChunk = m_EntityInChunkByEntity[index].Chunk != null;

            return versionMatches && hasChunk;
        }

        public static bool Debugger_Exists(EntityComponentStore* store, Entity entity)
        {
            int index = entity.Index;

            if (store == null || index < 0 || index >= store->EntitiesCapacity || store->m_VersionByEntity == null || store->m_EntityInChunkByEntity == null || store->m_ArchetypeByEntity == null)
                return false;

            var versionMatches = store->m_VersionByEntity[index] == entity.Version;
            var hasChunk = store->m_EntityInChunkByEntity[index].Chunk != null;

            return versionMatches && hasChunk;
        }


        public int GetComponentTypeOrderVersion(TypeIndex typeIndex)
        {
            return m_ComponentTypeOrderVersion[typeIndex.Index];
        }

        public bool HasComponent(Entity entity, TypeIndex type)
        {
            if (!Exists(entity))
                return false;

            var archetype = m_ArchetypeByEntity[entity.Index];
            return ChunkDataUtility.GetIndexInTypeArray(archetype, type) != -1;
        }

        internal bool HasComponent(Entity entity, TypeIndex type, ref LookupCache cache)
        {
            if (!Exists(entity))
                return false;

            var archetype = m_ArchetypeByEntity[entity.Index];
            if (Hint.Unlikely(archetype != cache.Archetype))
                cache.Update(archetype, type);
            return cache.IndexInArchetype != -1;
        }

        public bool HasComponent(Entity entity, ComponentType type)
        {
            if (!Exists(entity))
                return false;

            var archetype = m_ArchetypeByEntity[entity.Index];
            return ChunkDataUtility.GetIndexInTypeArray(archetype, type.TypeIndex) != -1;
        }

        public void SetChunkComponent<T>(NativeArray<ArchetypeChunk> chunks, T componentData)
            where T : unmanaged, IComponentData
        {
            var type = ComponentType.ReadWrite<T>();
            var chunkTypeIndex = TypeManager.MakeChunkComponentTypeIndex(type.TypeIndex);
            ArchetypeChunk* chunkPtr = (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks);

            SetChunkComponent(chunkPtr, chunks.Length, &componentData, chunkTypeIndex);
        }

        public void SetChunkComponent(ArchetypeChunk* chunks, int chunkCount, void* componentData, TypeIndex componentTypeIndex)
        {
            var type = ComponentType.FromTypeIndex(componentTypeIndex);
            if (type.IsZeroSized)
                return;

            for (int i = 0; i < chunkCount; i++)
            {
                var srcChunk = chunks[i].m_Chunk;
                var ptr = GetComponentDataWithTypeRW(srcChunk->metaChunkEntity, componentTypeIndex, m_GlobalSystemVersion);
                var sizeInChunk = GetTypeInfo(componentTypeIndex).SizeInChunk;
                UnsafeUtility.MemCpy(ptr, componentData, sizeInChunk);
            }
        }

        public void GetChunk(Entity entity, out Chunk* chunk, out int chunkIndex)
        {
            var entityChunk = m_EntityInChunkByEntity[entity.Index].Chunk;
            var entityIndexInChunk = m_EntityInChunkByEntity[entity.Index].IndexInChunk;

            chunk = entityChunk;
            chunkIndex = entityIndexInChunk;
        }

        public byte* GetComponentDataWithTypeRO(Entity entity, TypeIndex typeIndex)
        {
            var entityChunk = m_EntityInChunkByEntity[entity.Index].Chunk;
            var entityIndexInChunk = m_EntityInChunkByEntity[entity.Index].IndexInChunk;

            return ChunkDataUtility.GetComponentDataWithTypeRO(entityChunk, entityIndexInChunk, typeIndex);
        }

        public static byte* Debugger_GetComponentDataWithTypeRO(EntityComponentStore* store, Entity entity, TypeIndex typeIndex)
        {
            if (!Debugger_Exists(store, entity))
                return null;

            var entityChunk = store->m_EntityInChunkByEntity[entity.Index].Chunk;
            var entityIndexInChunk = store->m_EntityInChunkByEntity[entity.Index].IndexInChunk;
            if (entityChunk == null && entityIndexInChunk < 0 || entityIndexInChunk > entityChunk->Count || entityChunk->Archetype == null)
                return null;
            var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(entityChunk->Archetype, typeIndex);
            if (indexInTypeArray == -1)
                return null;

            return ChunkDataUtility.GetComponentDataRO(entityChunk, entityIndexInChunk, indexInTypeArray);
        }

        public byte* GetComponentDataWithTypeRW(Entity entity, TypeIndex typeIndex, uint globalVersion)
        {
            var entityChunk = m_EntityInChunkByEntity[entity.Index].Chunk;
            var entityIndexInChunk = m_EntityInChunkByEntity[entity.Index].IndexInChunk;

            var data = ChunkDataUtility.GetComponentDataWithTypeRW(entityChunk, entityIndexInChunk, typeIndex,
                globalVersion);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            if (Burst.CompilerServices.Hint.Unlikely(m_RecordToJournal != 0))
                JournalAddRecordGetRW(entity, typeIndex, globalVersion, data);
#endif

            return data;
        }

        // This method will return an invalid pointer if the entity does not have the provided type. It is the caller's
        // responsibility to ensure that the type exists on the entity.
        public byte* GetComponentDataWithTypeRO(Entity entity, TypeIndex typeIndex, ref LookupCache cache)
        {
            return ChunkDataUtility.GetComponentDataWithTypeRO(m_EntityInChunkByEntity[entity.Index].Chunk, m_ArchetypeByEntity[entity.Index], m_EntityInChunkByEntity[entity.Index].IndexInChunk, typeIndex, ref cache);
        }

        // This method will return a null pointer if the entity does not have the provided type.
        public byte* GetOptionalComponentDataWithTypeRO(Entity entity, TypeIndex typeIndex, ref LookupCache cache)
        {
            return ChunkDataUtility.GetOptionalComponentDataWithTypeRO(m_EntityInChunkByEntity[entity.Index].Chunk, m_ArchetypeByEntity[entity.Index], m_EntityInChunkByEntity[entity.Index].IndexInChunk, typeIndex, ref cache);
        }

        // This method will return an invalid pointer if the entity does not have the provided type. It is the caller's
        // responsibility to ensure that the type exists on the entity.
        public byte* GetComponentDataWithTypeRW(Entity entity, TypeIndex typeIndex, uint globalVersion, ref LookupCache cache)
        {
            var data = ChunkDataUtility.GetComponentDataWithTypeRW(m_EntityInChunkByEntity[entity.Index].Chunk, m_ArchetypeByEntity[entity.Index], m_EntityInChunkByEntity[entity.Index].IndexInChunk, typeIndex, globalVersion, ref cache);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            if (Burst.CompilerServices.Hint.Unlikely(m_RecordToJournal != 0))
                JournalAddRecordGetRW(entity, typeIndex, globalVersion, data);
#endif

            return data;
        }

        // This method will return a null pointer if the entity does not have the provided type.
        public byte* GetOptionalComponentDataWithTypeRW(Entity entity, TypeIndex typeIndex, uint globalVersion, ref LookupCache cache)
        {
            var data = ChunkDataUtility.GetOptionalComponentDataWithTypeRW(m_EntityInChunkByEntity[entity.Index].Chunk, m_ArchetypeByEntity[entity.Index], m_EntityInChunkByEntity[entity.Index].IndexInChunk, typeIndex, globalVersion, ref cache);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            if (data != null && Burst.CompilerServices.Hint.Unlikely(m_RecordToJournal != 0))
            {
                JournalAddRecordGetRW(entity, typeIndex, globalVersion, data);
            }
#endif

            return data;
        }

        public void* GetComponentDataRawRW(Entity entity, TypeIndex typeIndex)
        {
            AssertEntityHasComponent(entity, typeIndex);
            return GetComponentDataRawRWEntityHasComponent(entity, typeIndex);
        }

        internal void* GetComponentDataRawRWEntityHasComponent(Entity entity, TypeIndex typeIndex)
        {
            AssertNotZeroSizedComponent(typeIndex);
            return GetComponentDataWithTypeRW(entity, typeIndex, GlobalSystemVersion);
        }

        public void SetComponentDataRawEntityHasComponent(Entity entity, TypeIndex typeIndex, void* data, int size)
        {
            AssertComponentSizeMatches(typeIndex, size);
            var ptr = GetComponentDataWithTypeRW(entity, typeIndex,
                GlobalSystemVersion);
            UnsafeUtility.MemCpy(ptr, data, size);
        }

        public void SetBufferRaw(Entity entity, TypeIndex componentTypeIndex, BufferHeader* tempBuffer, int sizeInChunk)
        {
            var ptr = GetComponentDataWithTypeRW(entity, componentTypeIndex,
                GlobalSystemVersion);

            BufferHeader.Destroy((BufferHeader*)ptr);

            UnsafeUtility.MemCpy(ptr, tempBuffer, sizeInChunk);
        }

        [GenerateTestsForBurstCompatibility]
        public int GetSharedComponentDataIndex(Entity entity, TypeIndex typeIndex)
        {
            var archetype = m_ArchetypeByEntity[entity.Index];
            var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);
            var chunk = m_EntityInChunkByEntity[entity.Index].Chunk;
            var sharedComponentValueArray = chunk->SharedComponentValues;
            var sharedComponentOffset = indexInTypeArray - archetype->FirstSharedComponent;
            return sharedComponentValueArray[sharedComponentOffset];
        }

        public int Debugger_GetSharedComponentDataIndex(Entity entity, TypeIndex typeIndex)
        {
            var archetype = m_ArchetypeByEntity[entity.Index];
            if (archetype == null)
                return -1;
            var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);
            if (indexInTypeArray == -1)
                return -1;

            var chunk = m_EntityInChunkByEntity[entity.Index].Chunk;
            var sharedComponentValueArray = chunk->SharedComponentValues;
            var sharedComponentOffset = indexInTypeArray - archetype->FirstSharedComponent;
            return sharedComponentValueArray[sharedComponentOffset];
        }

        public void AllocateConsecutiveEntitiesForLoading(int count)
        {
            int newCapacity = count + 1; // make room for Entity.Null
            EnsureCapacity(newCapacity + 1); // the last entity is used to indicate we ran out of space
            m_NextFreeEntityIndex = newCapacity;
            m_EntityCreateDestroyVersion++;

            for (int i = 1; i < newCapacity; ++i)
            {
                Assert.IsTrue(m_EntityInChunkByEntity[i].Chunk == null); //  Loading into non-empty entity manager is not supported.

                m_EntityInChunkByEntity[i].IndexInChunk = 0;
                m_VersionByEntity[i] = 0;
#if !DOTS_DISABLE_DEBUG_NAMES
                m_NameByEntity[i] = new EntityName();
#endif
            }
        }

        public void AddExistingEntitiesInChunk(Chunk* chunk)
        {
            for (int iEntity = 0; iEntity < chunk->Count; ++iEntity)
            {
                var entity = (Entity*)ChunkDataUtility.GetComponentDataRO(chunk, iEntity, 0);

                m_EntityInChunkByEntity[entity->Index].Chunk = chunk;
                m_EntityInChunkByEntity[entity->Index].IndexInChunk = iEntity;
                m_ArchetypeByEntity[entity->Index] = chunk->Archetype;
                m_VersionByEntity[entity->Index] = entity->Version;
            }
        }

        public void AllocateEntitiesForRemapping(EntityComponentStore* srcEntityComponentStore, ref NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
            var count = srcEntityComponentStore->EntitiesCapacity;

            for (var i = 0; i != count; i++)
            {
                if (srcEntityComponentStore->m_EntityInChunkByEntity[i].Chunk != null)
                {
                    var entityIndexInChunk = m_EntityInChunkByEntity[m_NextFreeEntityIndex].IndexInChunk;
                    if (entityIndexInChunk == -1)
                    {
                        IncreaseCapacity();
                        entityIndexInChunk = m_EntityInChunkByEntity[m_NextFreeEntityIndex].IndexInChunk;
                    }

                    var entityVersion = m_VersionByEntity[m_NextFreeEntityIndex];
#if !DOTS_DISABLE_DEBUG_NAMES
                    m_NameByEntity[m_NextFreeEntityIndex] = srcEntityComponentStore->m_NameByEntity[i];
#endif

                    EntityRemapUtility.AddEntityRemapping(ref entityRemapping,
                        new Entity {Version = srcEntityComponentStore->m_VersionByEntity[i], Index = i},
                        new Entity {Version = entityVersion, Index = m_NextFreeEntityIndex});

                    m_NextFreeEntityIndex = entityIndexInChunk;
                    m_EntityCreateDestroyVersion++;

                }
            }
        }

        public void AllocateEntitiesForRemapping(Chunk* chunk, EntityComponentStore* srcComponentStore, ref NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
            var count = chunk->Count;
            var entities = (Entity*)chunk->Buffer;

            for (var i = 0; i != count; i++)
            {
                var entityIndexInChunk = m_EntityInChunkByEntity[m_NextFreeEntityIndex].IndexInChunk;
                if (entityIndexInChunk == -1)
                {
                    IncreaseCapacity();
                    entityIndexInChunk = m_EntityInChunkByEntity[m_NextFreeEntityIndex].IndexInChunk;
                }

                var entityVersion = m_VersionByEntity[m_NextFreeEntityIndex];
#if !DOTS_DISABLE_DEBUG_NAMES
                m_NameByEntity[m_NextFreeEntityIndex] = srcComponentStore->m_NameByEntity[entities[i].Index];
#endif
                EntityRemapUtility.AddEntityRemapping(ref entityRemapping,
                    new Entity {Version = entities[i].Version, Index = entities[i].Index},
                    new Entity {Version = entityVersion, Index = m_NextFreeEntityIndex});

                m_NextFreeEntityIndex = entityIndexInChunk;
                m_EntityCreateDestroyVersion++;

            }
        }

        public void RemapChunk(Archetype* arch, Chunk* chunk, int baseIndex, int count, ref NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
            Assert.AreEqual(chunk->Archetype->Offsets[0], 0);
            Assert.AreEqual(chunk->Archetype->SizeOfs[0], sizeof(Entity));

            var entityInChunkStart = (Entity*)(chunk->Buffer) + baseIndex;

            for (var i = 0; i != count; i++)
            {
                var entityInChunk = entityInChunkStart + i;
                var target = EntityRemapUtility.RemapEntity(ref entityRemapping, *entityInChunk);
                var entityVersion = m_VersionByEntity[target.Index];

                Assert.AreEqual(entityVersion, target.Version);

                entityInChunk->Index = target.Index;
                entityInChunk->Version = entityVersion;
                m_EntityInChunkByEntity[target.Index].IndexInChunk = baseIndex + i;
                m_ArchetypeByEntity[target.Index] = arch;
                m_EntityInChunkByEntity[target.Index].Chunk = chunk;
            }

            if (chunk->metaChunkEntity != Entity.Null)
            {
                chunk->metaChunkEntity = EntityRemapUtility.RemapEntity(ref entityRemapping, chunk->metaChunkEntity);
            }
        }

        // There is a special case where the index is 0, we interpret it as an unmanaged value even though it may not, but it's ok because 0 is always interpreted as "don't do anything" and
        //  it's better to favor branching in the unmanaged path rather than the managed one.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsUnmanagedSharedComponentIndex(int sharedComponentIndex) =>
            (sharedComponentIndex == 0) || (sharedComponentIndex & kUnmanagedSharedComponentIndexFlag) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TypeIndex GetComponentTypeFromSharedComponentIndex(int sharedComponentIndex) =>
            TypeManager.GetTypeInfoPointer()[
                    ((sharedComponentIndex & ~kUnmanagedSharedComponentIndexFlag) >> kUnmanagedSharedTypeIndexBitOffset)]
                .TypeIndex;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetElementIndexFromSharedComponentIndex(int sharedComponentIndex) =>
            sharedComponentIndex & kUnmanagedSharedElementIndexMask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int BuildUnmanagedSharedComponentDataIndex(int elementIndex, TypeIndex typeIndex) =>
            kUnmanagedSharedComponentIndexFlag | (typeIndex.Value << kUnmanagedSharedTypeIndexBitOffset) | elementIndex;

#pragma warning disable 0618 //Untyped UnsafeList is obsolete.
        private ComponentTypeList* CheckGetSharedComponentList(TypeIndex typeIndex)
        {
            var typeIndexNoFlags = typeIndex.Index;
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (typeIndexNoFlags >= m_UnmanagedSharedComponentsByType.Capacity)
            {
                throw new ArgumentException($"Invalid type index {typeIndex}", nameof(typeIndex));
            }
#endif
            var components = &m_UnmanagedSharedComponentsByType.Ptr[typeIndexNoFlags];
            if (components->IsCreated == false)
            {
                var typeSize = TypeManager.GetTypeInfo(typeIndex).TypeSize;
                *components = new ComponentTypeList(typeSize, 16, 16 * typeSize, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                components->Length = 1;
                m_UnmanagedSharedComponentsByType.Length = Math.Max(m_UnmanagedSharedComponentsByType.Length, typeIndexNoFlags + 1);
            }
            return components;
        }
#pragma warning restore 0618

        private UnsafeList<SharedComponentInfo>* CheckGetSharedComponentInfo(TypeIndex typeIndex)
        {
            var typeIndexNoFlags = typeIndex.Index;
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (typeIndexNoFlags >= m_UnmanagedSharedComponentInfo.Capacity)
            {
                throw new ArgumentException($"Invalid type index {typeIndex}", nameof(typeIndex));
            }
#endif
            // Store the list at the offset of the typeIndex for direct lookup
            var components = &m_UnmanagedSharedComponentInfo.Ptr[typeIndexNoFlags];
            if (components->IsCreated == false)
            {
                *components = new UnsafeList<SharedComponentInfo>(16, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                // First entry is reserved for default value, ComponentType is used as the entry point for the Free linked list
                components->Add(new SharedComponentInfo {RefCount = 1, HashCode = 0, Version = 1, ComponentType = -1});
                m_UnmanagedSharedComponentTypes.Add(typeIndex);
            }

            return components;
        }

        private int FindSharedComponentIndex(
            TypeIndex typeIndex,
            int hashCode,
            void* newData,
            void* defaultValue,
            out UnsafeList<SharedComponentInfo>* infos,
#pragma warning disable 0618 //Untyped UnsafeList is obsolete.
            out ComponentTypeList* components)
#pragma warning restore 0618
        {
            components = CheckGetSharedComponentList(typeIndex);
            infos = CheckGetSharedComponentInfo(typeIndex);

            // If newData is null, we assume we want to find the default value, so return 0
            if (newData == null)
            {
                return 0;
            }

            // If defaultValue is null, we assume we are looking for a non-default SharedComponent
            if (defaultValue != null && TypeManager.EqualsWithBurst(newData, defaultValue, typeIndex))
            {
                return 0;
            }

            return FindNonDefaultSharedComponentIndex(typeIndex, hashCode, newData, out infos, out components);
        }

        private int FindNonDefaultSharedComponentIndex(TypeIndex typeIndex, int hashCode, void* newData, out UnsafeList<SharedComponentInfo>* infos, out ComponentTypeList* components)
        {
            // It's most likely a hash computation produces 0, and if it's the case, we will end up...computing it again
            if (hashCode == 0)
            {
                hashCode = TypeManager.GetHashCodeWithBurst(newData, typeIndex);
            }

            int itemIndex;
            NativeMultiHashMapIterator<int> iter;

            if (!m_HashLookup.TryGetFirstValue(hashCode, out itemIndex, out iter))
            {
                infos = null;
                components = null;
                return -1;
            }

            var typeSize = TypeManager.GetTypeInfo(typeIndex).TypeSize;
            components = CheckGetSharedComponentList(typeIndex);
            infos = CheckGetSharedComponentInfo(typeIndex);
            do
            {
                if (TypeManager.EqualsWithBurst((byte*) components->Ptr + (itemIndex * typeSize), newData, typeIndex))
                {
                    return itemIndex;
                }
            }
            while (m_HashLookup.TryGetNextValue(out itemIndex, ref iter));

            return -1;
        }

        internal int CloneSharedComponentNonDefault(EntityComponentStore* srcComponentStore, int sharedComponentIndex)
        {
            var typeIndex = GetComponentTypeFromSharedComponentIndex(sharedComponentIndex);
            var componentDataAddr = srcComponentStore->GetSharedComponentDataAddr_Unmanaged(sharedComponentIndex, typeIndex);
            var elementIndex = GetElementIndexFromSharedComponentIndex(sharedComponentIndex);
            var infos = srcComponentStore->CheckGetSharedComponentInfo(typeIndex);

            return InsertSharedComponent_Unmanaged(typeIndex, infos->Ptr[elementIndex].HashCode, componentDataAddr, null);
        }

        // If defaultValue is null we assume we are inserting a non-default value
        internal int InsertSharedComponent_Unmanaged(TypeIndex typeIndex, int hashCode, void* data, void* defaultValue)
        {
            // It's most likely a hash computation produces 0, and if it's the case, we will end up...computing it again
            if (hashCode == 0)
            {
                // No data, means we're inserting the default value, we will exit before we need a hash
                hashCode = data!=null ? TypeManager.GetHashCodeWithBurst(data, typeIndex) : 0;
            }

            var elementIndex = FindSharedComponentIndex(
                typeIndex,
                hashCode,
                data,
                defaultValue,
                out var infos,
                out var components);

            // We return 0 as the sharedComponentIndex for the default value, 0 is interpreted by the code paths as "don't do anything",
            //  which is the case for the default value, whether it's managed or unmanaged.
            if (elementIndex == 0)
                return 0;

            if (elementIndex != -1)
            {
                infos->Ptr[elementIndex].RefCount++;
                return BuildUnmanagedSharedComponentDataIndex(elementIndex, typeIndex);
            }

            if (typeIndex.IsRefCounted)
            {
                TypeManager.CallIRefCounted_Retain(typeIndex, (IntPtr)data);
            }

            var typeSize = TypeManager.GetTypeInfo(typeIndex).TypeSize;

            infos = CheckGetSharedComponentInfo(typeIndex);
            components = CheckGetSharedComponentList(typeIndex);
            Assert.AreEqual(components->Length, infos->Length);

            var info = new SharedComponentInfo { RefCount = 1, HashCode = hashCode, ComponentType = typeIndex.Value, Version = m_SharedComponentVersion++ };

            // Check if we can use an entry that was previously freed
            elementIndex = infos->Ptr[0].ComponentType;
            if (elementIndex != -1)
            {
                infos->Ptr[0].ComponentType = infos->Ptr[elementIndex].ComponentType;
                UnsafeUtility.MemCpy((byte*)components->Ptr + elementIndex*typeSize, data, typeSize);
                UnsafeUtility.WriteArrayElement(infos->Ptr, elementIndex, info);
            }

            // Allocate a new entry
            else
            {
                elementIndex = components->Length;
                components->Resize(typeSize, 16, components->Length + 1);
                UnsafeUtility.MemCpy((byte*)components->Ptr + elementIndex*typeSize, data, typeSize);

                infos->Add(info);
            }

            m_HashLookup.Add(hashCode, elementIndex);
            m_UnmanagedSharedComponentCount++;

            return BuildUnmanagedSharedComponentDataIndex(elementIndex, typeIndex);
        }

        [GenerateTestsForBurstCompatibility]
        internal int GetUnmanagedSharedComponentCount()
        {
            return m_UnmanagedSharedComponentCount;
        }

        [GenerateTestsForBurstCompatibility]
        internal void AddSharedComponentReference_Unmanaged(int sharedComponentIndex, int numRefs = 1)
        {
            var elementIndex = GetElementIndexFromSharedComponentIndex(sharedComponentIndex);
            if (elementIndex == 0) return;

            var componentTypeIndex = GetComponentTypeFromSharedComponentIndex(sharedComponentIndex);
            var infos = CheckGetSharedComponentInfo(componentTypeIndex);

            infos->Ptr[elementIndex].RefCount += numRefs;
        }

        internal void GetSharedComponentData_Unmanaged(int sharedComponentIndex, TypeIndex typeIndex, void* destinationAddress)
        {
            var typeSize = TypeManager.GetTypeInfo(typeIndex).TypeSize;
            var sourceAddress = GetSharedComponentDataAddr_Unmanaged(sharedComponentIndex, typeIndex);
            UnsafeUtility.MemCpy(destinationAddress, sourceAddress, typeSize);
        }

        // sharedComponentIndex can be 0, in which case we return the first entry of the type which corresponds to the default value
        internal void* GetSharedComponentDataAddr_Unmanaged(int sharedComponentIndex, TypeIndex typeIndex)
        {
            // if typeindex is Null, don't bother with all this checking, because nothing matters and everything
            // will definitely be null. we should clean this up so we don't make this call or many of its parents
            // when typeindex is Null.
            if (typeIndex == TypeIndex.Null)
                return null;

            var elementIndex = GetElementIndexFromSharedComponentIndex(sharedComponentIndex);
            var typeIndexNoFlags = typeIndex.Index;

            // Make sure the component list is fetch if we access the default value (because we allow getting default without first setting it)
            if (sharedComponentIndex == 0)
            {
                CheckGetSharedComponentList(typeIndex);
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if ((typeIndexNoFlags >= m_UnmanagedSharedComponentsByType.Length) || (m_UnmanagedSharedComponentsByType.Ptr[typeIndexNoFlags].IsCreated==false))
            {
                Assert.IsTrue(false, $"componentTypeIndex {typeIndexNoFlags} is out of range {m_UnmanagedSharedComponentsByType.Length}");
            }
#endif
            var components = &m_UnmanagedSharedComponentsByType.Ptr[typeIndexNoFlags];
            UnityEngine.Assertions.Assert.IsTrue(components->IsCreated && components->Length > 0);

            var typeSize = TypeManager.GetTypeInfo(typeIndex).TypeSize;
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (elementIndex >= components->Length)
            {
                Assert.IsTrue(false, $"elementIndex {elementIndex} is out of range {components->Length}");
            }
#endif
            return (byte*)components->Ptr + elementIndex*typeSize;
        }

        internal object GetSharedComponentDataObject_Unmanaged(int sharedComponentIndex, TypeIndex typeIndex)
        {
            var componentAddr = GetSharedComponentDataAddr_Unmanaged(sharedComponentIndex, typeIndex);
            return TypeManager.ConstructComponentFromBuffer(typeIndex, componentAddr);
        }

        [GenerateTestsForBurstCompatibility]
        internal void RemoveSharedComponentReference_Unmanaged(int sharedComponentIndex, int numRefs = 1)
        {
            var componentTypeIndex = GetComponentTypeFromSharedComponentIndex(sharedComponentIndex);
            var elementIndex = GetElementIndexFromSharedComponentIndex(sharedComponentIndex);
            if (elementIndex == 0)
                return;

            var infos = CheckGetSharedComponentInfo(componentTypeIndex);

            var newCount = infos->Ptr[elementIndex].RefCount -= numRefs;
            Assert.IsTrue(newCount >= 0);

            if (newCount != 0)
                return;
            /*
             * we only call release() for IRefCounted things when our own separate refcount goes to 0.
             */
            if (componentTypeIndex.IsRefCounted)
            {
                var data = GetSharedComponentDataAddr_Unmanaged(sharedComponentIndex, componentTypeIndex);
                TypeManager.CallIRefCounted_Release(componentTypeIndex, (IntPtr)data);
            }
            // Bump default version when a shared component is removed completely.
            // This ensures that when asking for a shared component that previously existed and no longer exists
            // It will always return a change value.
            IncrementSharedComponentVersion_Unmanaged(0);

            var hashCode = infos->Ptr[elementIndex].HashCode;

            // Update the free linked list by adding the entry we just freed
            infos->Ptr[elementIndex].ComponentType = infos->Ptr[0].ComponentType;
            infos->Ptr[0].ComponentType = elementIndex;

            int itemIndex;
            NativeMultiHashMapIterator<int> iter;
            if (m_HashLookup.TryGetFirstValue(hashCode, out itemIndex, out iter))
            {
                do
                {
                    if (itemIndex == elementIndex)
                    {
                        m_HashLookup.Remove(iter);
                        m_UnmanagedSharedComponentCount--;
                        return;
                    }
                }
                while (m_HashLookup.TryGetNextValue(out itemIndex, ref iter));
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            throw new System.InvalidOperationException("shared component couldn't be removed due to internal state corruption");
#endif
        }

        [GenerateTestsForBurstCompatibility]
        internal void IncrementSharedComponentVersion_Unmanaged(int sharedComponentIndex)
        {
            var version = ++m_SharedComponentVersion;
            if (sharedComponentIndex == 0)
            {
                m_SharedComponentGlobalVersion = version;
            }
            else
            {
                var componentType = GetComponentTypeFromSharedComponentIndex(sharedComponentIndex);
                var elementIndex = GetElementIndexFromSharedComponentIndex(sharedComponentIndex);
                var info = CheckGetSharedComponentInfo(componentType);

                info->Ptr[elementIndex].Version = version;
            }
        }

        internal int GetSharedComponentVersion_Unmanaged(TypeIndex typeIndex, void* sharedData, void* defaultData)
        {
            var elementIndex = FindSharedComponentIndex(typeIndex, 0, sharedData, defaultData, out var infos, out _);
            if (elementIndex <= 0)
            {
                return m_SharedComponentGlobalVersion;
            }
            return infos->Ptr[elementIndex].Version;
        }

        [GenerateTestsForBurstCompatibility(GenericTypeArguments = new[] {typeof(BurstCompatibleSharedComponentData)})]
        internal void GetAllUniqueSharedComponents_Unmanaged<T>(out UnsafeList<T> sharedComponentValues, Allocator allocator) where T : unmanaged, ISharedComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var defaultValue = default(T);
            var components = CheckGetSharedComponentList(typeIndex);
            var infos = CheckGetSharedComponentInfo(typeIndex);
            var typeSize = TypeManager.GetTypeInfo<T>().TypeSize;

            sharedComponentValues = new UnsafeList<T>(components->Length, allocator);
            sharedComponentValues.Add(defaultValue);

            //0 is always default
            for (int i = 1, j = 1; i < infos->Length; i++)
            {
                if (infos->Ptr[i].RefCount > 0)
                {
                    UnsafeUtility.MemCpy((byte*)sharedComponentValues.Ptr + j*typeSize, (byte*)components->Ptr + i*typeSize, typeSize);
                    sharedComponentValues.Length++;
                    j++;
                }
            }
        }

#pragma warning disable 0618 //Untyped UnsafeList is obsolete.
        [GenerateTestsForBurstCompatibility]
        internal void GetAllUniqueSharedComponents_Unmanaged(
            TypeIndex typeIndex,
            void* defaultValue,
            out ComponentTypeList sharedComponentValues,
            out UnsafeList<int> sharedComponentIndices,
            Allocator allocator)
        {
            var components = CheckGetSharedComponentList(typeIndex);
            var infos = CheckGetSharedComponentInfo(typeIndex);
            var typeSize = TypeManager.GetTypeInfo(typeIndex).TypeSize;

            sharedComponentValues = new ComponentTypeList(typeSize, 16, components->Length * typeSize, allocator);
            sharedComponentIndices = new UnsafeList<int>(components->Length, allocator);
            UnsafeUtility.MemCpy(sharedComponentValues.Ptr, defaultValue, typeSize);
            sharedComponentValues.Length++;
            sharedComponentIndices.Add(0);

            for (int elementIndexInComponentStore = 1, indexInOutputList = 1;
                 elementIndexInComponentStore < infos->Length;
                 elementIndexInComponentStore++)
            {
                if (infos->Ptr[elementIndexInComponentStore].RefCount > 0)
                {
                    UnsafeUtility.MemCpy((byte*)sharedComponentValues.Ptr + indexInOutputList * typeSize,
                        (byte*)components->Ptr + elementIndexInComponentStore * typeSize,
                        typeSize);
                    sharedComponentValues.Length++;
                    sharedComponentIndices.Add(
                        BuildUnmanagedSharedComponentDataIndex(elementIndexInComponentStore, typeIndex));
                    indexInOutputList++;
                }
            }
        }
#pragma warning restore 0618

        internal void MoveAllSharedComponents_Unmanaged(EntityComponentStore* srcEntityComponentStore, ref NativeParallelHashMap<int, int> result)
        {
            var sharedComponentTypeCount = srcEntityComponentStore->m_UnmanagedSharedComponentTypes.Length;
            for (int i = 0; i < sharedComponentTypeCount; ++i)
            {
                var typeIndex = srcEntityComponentStore->m_UnmanagedSharedComponentTypes[i];
                var typeSize = TypeManager.GetTypeInfo(typeIndex).TypeSize;

                var srcComponents = srcEntityComponentStore->CheckGetSharedComponentList(typeIndex);
                var srcInfos = srcEntityComponentStore->CheckGetSharedComponentInfo(typeIndex);

                for (int j = 1; j < srcComponents->Length; j++)
                {
                    // TODO: Strip all the flags from the typeindex as we don't want to do anything special based on the
                    // typeindex  such as Retain()/Release() an additional time since we are specifically in the
                    // MoveEntities codepath were we want components// to be identical in the dst world as the src world
                    // DOTS-6895
                    var dstIndex = InsertSharedComponent_Unmanaged(new TypeIndex() { Value = typeIndex.Index }, srcInfos->Ptr[j].HashCode, (byte*) srcComponents->Ptr + j * typeSize, null);
                    AddSharedComponentReference_Unmanaged(dstIndex, srcInfos->Ptr[j].RefCount - 1);
                    IncrementSharedComponentVersion_Unmanaged(dstIndex);

                    result.Add(BuildUnmanagedSharedComponentDataIndex(j, typeIndex), dstIndex);
                }
            }

            srcEntityComponentStore->ResetSharedComponentData();
        }

        public bool AllSharedComponentReferencesAreFromChunks(UnsafeParallelHashMap<int, int> refCountMap)
        {
            var sharedComponentTypeCount = m_UnmanagedSharedComponentTypes.Length;
            for (int i = 0; i < sharedComponentTypeCount; ++i)
            {
                var typeIndex = m_UnmanagedSharedComponentTypes[i];
                var infos = CheckGetSharedComponentInfo(typeIndex);

                for (int j = 1; j < infos->Length; j++)
                {
                    var sharedComponentIndex = BuildUnmanagedSharedComponentDataIndex(j, typeIndex);
                    if (refCountMap.TryGetValue(sharedComponentIndex, out var recordedRefCount))
                    {
                        var infoRefCount = infos->Ptr[j].RefCount;
                        if (infoRefCount != recordedRefCount)
                            return false;
                    }
                }
            }

            return true;
        }

        [BurstCompile]
        private static void EntityBatchFromEntityChunkDataShared(in EntityInChunk* chunkData,
            int chunkCount,
            EntityBatchInChunk* entityBatchList, int* currentbatchIndex,
            int nSharedComponentsToAdd, int* foundError)
        {

            *foundError = 0;

            var entityIndex = 0;
            var entityBatch = new EntityBatchInChunk
            {
                Chunk = chunkData[entityIndex].Chunk,
                StartIndex = chunkData[entityIndex].IndexInChunk,
                Count = 1
            };
            entityIndex++;
            while (entityIndex < chunkCount)
            {
                // Skip this entity if it's a duplicate.  Checking previous entityIndex is sufficient
                // since arrays are sorted.
                if (chunkData[entityIndex].Equals(chunkData[entityIndex - 1]))
                {
                    entityIndex++;
                    continue;
                }

                var chunk = chunkData[entityIndex].Chunk;
                var indexInChunk = chunkData[entityIndex].IndexInChunk;
                var chunkBreak = (chunk != entityBatch.Chunk);
                var indexBreak = (indexInChunk != (entityBatch.StartIndex + entityBatch.Count));
                var runBreak = chunkBreak || indexBreak;
                if (runBreak && entityBatch.Chunk != null)
                {

                    if (nSharedComponentsToAdd + entityBatch.Chunk->Archetype->NumSharedComponents > kMaxSharedComponentCount)
                    {
                        *foundError = 1;
                        return;
                    }

                    entityBatchList[*currentbatchIndex] = entityBatch;
                    *currentbatchIndex = *currentbatchIndex + 1;
                    //just to make sure we do not overflow our entityBatchList buffer
                    if (*currentbatchIndex > chunkCount)
                    {
                        *foundError = 1;
                        return;
                    }

                    entityBatch = new EntityBatchInChunk
                    {
                        Chunk = chunk,
                        StartIndex = indexInChunk,
                        Count = 1
                    };
                }
                else
                {
                    entityBatch = new EntityBatchInChunk
                    {
                        Chunk = entityBatch.Chunk,
                        StartIndex = entityBatch.StartIndex,
                        Count = entityBatch.Count + 1
                    };
                }

                entityIndex++;
            }

            if (entityBatch.Chunk == null)
                return;

            // Deleted Entity (not an error)
            if (nSharedComponentsToAdd + entityBatch.Chunk->Archetype->NumSharedComponents > kMaxSharedComponentCount)
            {
                *foundError = 1;
                return;
            }

            entityBatchList[*currentbatchIndex] = entityBatch;
            *currentbatchIndex = *currentbatchIndex + 1;
            //just to make sure we do not overflow our entityBatchList buffer
            if (*currentbatchIndex > chunkCount)
            {
                *foundError = 1;
            }
        }

        [BurstCompile]
        private static void SortEntityInChunk(EntityInChunk* entityInChunks, int count)
        {
            NativeSortExtension.Sort(entityInChunks,count);
        }

        [BurstCompile]
        private static void GatherEntityInChunkForEntities(Entity* Entities,
            EntityInChunk* globalEntityInChunk,
            EntityInChunk* EntityChunkData, int numEntities)
        {
            for (int index = 0; index < numEntities; ++index)
            {
                var entity = Entities[index];
                EntityChunkData[index] = new EntityInChunk
                {
                    Chunk = globalEntityInChunk[entity.Index].Chunk,
                    IndexInChunk = globalEntityInChunk[entity.Index].IndexInChunk
                };
            }
        }

        internal bool CreateEntityBatchList(NativeArray<Entity> entities, int nSharedComponentsToAdd,
            Allocator allocator, out NativeList<EntityBatchInChunk> entityBatchList)
        {
            if (entities.Length == 0)
            {
                entityBatchList = default;
                return false;
            }

            var entityChunkData = new NativeArray<EntityInChunk>(entities.Length, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);

            GatherEntityInChunkForEntities((Entity*) entities.GetUnsafeReadOnlyPtr(),
                m_EntityInChunkByEntity, (EntityInChunk*) entityChunkData.GetUnsafePtr(),entities.Length);

            SortEntityInChunk((EntityInChunk*)entityChunkData.GetUnsafePtr(), entityChunkData.Length);

            entityBatchList = new NativeList<EntityBatchInChunk>(entityChunkData.Length, allocator);
            entityBatchList.Length = entityChunkData.Length;

            int foundError = 0;
            int finalBatchSize = 0;

            EntityBatchFromEntityChunkDataShared((EntityInChunk*)entityChunkData.GetUnsafePtr(),
                entityChunkData.Length, (EntityBatchInChunk*)entityBatchList.GetUnsafePtr(),&finalBatchSize,
                nSharedComponentsToAdd,&foundError);

            entityBatchList.Length = finalBatchSize;

            entityChunkData.Dispose();
            if (foundError != 0)
            {
                entityBatchList.Dispose();
                entityBatchList = default;
                return false;
            }

            return true;
        }


        public ulong AllocateSequenceNumber()
        {
            // EntityComponentStore.CheckInternalConsistency checks for a chunk's SequenceNumber to be != 0
            // So we want to be sure that we increment the global sequence number before returning it, so
            // it will never be zero.
            // The following two lines used to be inverted, and we hit an instability that was happening
            // when running a single test that creates a single entity, inside an EcsTestFixture.
            m_NextChunkSequenceNumber++;
            var sequenceNumber = m_NextChunkSequenceNumber;
            return sequenceNumber;
        }

#pragma warning disable 169
        struct Ulong16
        {
            private ulong p00;
            private ulong p01;
            private ulong p02;
            private ulong p03;
            private ulong p04;
            private ulong p05;
            private ulong p06;
            private ulong p07;
            private ulong p08;
            private ulong p09;
            private ulong p10;
            private ulong p11;
            private ulong p12;
            private ulong p13;
            private ulong p14;
            private ulong p15;
        }

        struct Ulong256
        {
            private Ulong16 p00;
            private Ulong16 p01;
            private Ulong16 p02;
            private Ulong16 p03;
            private Ulong16 p04;
            private Ulong16 p05;
            private Ulong16 p06;
            private Ulong16 p07;
            private Ulong16 p08;
            private Ulong16 p09;
            private Ulong16 p10;
            private Ulong16 p11;
            private Ulong16 p12;
            private Ulong16 p13;
            private Ulong16 p14;
            private Ulong16 p15;
            public int Length { get { return 256; } set {} }
            public ref long ElementAt(int index)
            {
                fixed(Ulong16* p = &p00) { return ref UnsafeUtility.AsRef<long>((long*)p + index); }
            }
        }

        struct Ulong4096
        {
            private Ulong256 p00;
            private Ulong256 p01;
            private Ulong256 p02;
            private Ulong256 p03;
            private Ulong256 p04;
            private Ulong256 p05;
            private Ulong256 p06;
            private Ulong256 p07;
            private Ulong256 p08;
            private Ulong256 p09;
            private Ulong256 p10;
            private Ulong256 p11;
            private Ulong256 p12;
            private Ulong256 p13;
            private Ulong256 p14;
            private Ulong256 p15;
        }

       struct Ulong16384 : IIndexable<long>
        {
            private Ulong4096 p00;
            private Ulong4096 p01;
            private Ulong4096 p02;
            private Ulong4096 p03;
            public int Length { get { return 16384; } set {} }
            public ref long ElementAt(int index)
            {
                fixed(Ulong4096* p = &p00) { return ref UnsafeUtility.AsRef<long>((long*)p + index); }
            }
        }
#pragma warning restore 169

        internal struct ChunkStore : IDisposable
        {
            Ulong16384 m_megachunk;
            Ulong16384 m_chunkInUse;
            Ulong256   m_megachunkIsFull;
            static readonly int log2ChunksPerMegachunk = 6;
            static readonly int chunksPerMegachunk = 1 << log2ChunksPerMegachunk;
            static readonly int log2MegachunksInUniverse = 14;
            static readonly int megachunksInUniverse = 1 << log2MegachunksInUniverse;

            public static readonly int kErrorNone = 0;
            public static readonly int kErrorAllocationFailed = -1;
            public static readonly int kErrorChunkAlreadyFreed = -2;
            public static readonly int kErrorChunkAlreadyMarkedFree = -3;
            public static readonly int kErrorChunkNotFound = -4;
            public static readonly int kErrorNoChunksAvailable = -5;

            static readonly int ChunkSizeInBytesRoundedUpToPow2 = math.ceilpow2(Chunk.kChunkSize);
            static readonly int Log2ChunkSizeInBytesRoundedUpToPow2 = math.tzcnt(ChunkSizeInBytesRoundedUpToPow2);
            static readonly int MegachunkSizeInBytes = 1 << (Log2ChunkSizeInBytesRoundedUpToPow2 + 6);

//            int m_refCount;

            int AllocationFailed(int offset, int count)
            {
                int error = ConcurrentMask.TryFree(ref m_chunkInUse, offset, count);
                if(error == ConcurrentMask.ErrorFailedToFree)
                    return kErrorChunkAlreadyFreed;
                return kErrorAllocationFailed;
            }

            static int AtomicRead(ref int value)
            {
                return Interlocked.Add(ref value, 0);
            }

            static long AtomicRead(ref long value)
            {
                return Interlocked.Add(ref value, 0L);
            }

            static bool IsZero(ref long value)
            {
                return Interlocked.Add(ref value, 0L) == 0L;
            }

            Chunk* GetChunkPointer(int bitOffset)
            {
                var megachunkIndex = bitOffset >> log2ChunksPerMegachunk;
                var chunkInMegachunk = bitOffset & (chunksPerMegachunk-1);
                var megachunk = (byte*)m_megachunk.ElementAt(megachunkIndex);
                var chunk = megachunk + (chunkInMegachunk << Log2ChunkSizeInBytesRoundedUpToPow2);
                return (Chunk*)chunk;
            }

            int Allocate(out Chunk* value, int bitOffset, int actualCount)
            {
                value = null;
                var megachunkIndex = bitOffset >> log2ChunksPerMegachunk;
                var chunkInMegachunk = bitOffset & (chunksPerMegachunk-1);
                if(0 == Interlocked.Add(ref m_megachunk.ElementAt(megachunkIndex), 0))
                {
                    long pointer = (long) Memory.Unmanaged.Allocate(MegachunkSizeInBytes, CollectionHelper.CacheLineSize, Allocator.Persistent);
                    if (pointer == 0) // if the allocation failed...
                        return AllocationFailed(bitOffset, actualCount);
                    if(0 != Interlocked.CompareExchange(ref m_megachunk.ElementAt(megachunkIndex), pointer, 0)) // store the new pointer
                        Memory.Unmanaged.Free((void*)pointer, Allocator.Persistent);
                }
                value = GetChunkPointer(bitOffset);
                for(var chunkInAllocation = 0; chunkInAllocation < actualCount; ++chunkInAllocation)
                {
                    var bitOffsetOfChunkInAllocation = bitOffset + chunkInAllocation;
                    Chunk* chunk = GetChunkPointer(bitOffsetOfChunkInAllocation);
                    chunk->ChunkstoreIndex = megachunkIndex;
                }
                return kErrorNone;
            }

            public int AllocateContiguousChunks(out Chunk* value, int requestedCount, out int actualCount)
            {
                int gigachunkIndex = 0;
                for(; gigachunkIndex < m_megachunkIsFull.Length; ++gigachunkIndex)
                    if(m_megachunkIsFull.ElementAt(gigachunkIndex) != ~0L)
                        break;
                int firstMegachunk = gigachunkIndex << 6;
                actualCount = math.min(chunksPerMegachunk, requestedCount); // literally can't service requests for more
                value = null;
                while (actualCount > 0)
                {
                    var error = ConcurrentMask.TryAllocate(ref m_chunkInUse, out int bitOffset, firstMegachunk, actualCount);
                    if(ConcurrentMask.Succeeded(error))
                    {
                        if(error + actualCount == chunksPerMegachunk)
                        {
                            int megachunkIndex = bitOffset >> log2ChunksPerMegachunk;
                            ConcurrentMask.AtomicOr(ref m_megachunkIsFull.ElementAt(megachunkIndex>>6), 1L << (megachunkIndex & 63));
                        }
                        return Allocate(out value, bitOffset, actualCount);
                    }
                    actualCount >>= 1; // we tried to get what was asked for, but couldn't find space. halve request and try again.
                }
                return kErrorNoChunksAvailable;
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            private static void ThrowMegachunkIndexIsInvalid(int megachunkIndex)
            {
                throw new ArgumentException($"Megachunk index {megachunkIndex} is not beween 0 and {megachunksInUniverse-1}, inclusive");
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            private void ThrowChunkPointerIsNotInMegachunk(Chunk* chunk, int megachunkIndex)
            {
                throw new ArgumentException($"Chunk pointer {(IntPtr)chunk} is not in megachunk {megachunkIndex}");
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            private void ThrowChunkAlreadyMarkedAsFree(Chunk* chunk)
            {
                throw new ArgumentException($"Chunk pointer {(IntPtr)chunk} already marked as free");
            }

            public int FreeContiguousChunks(Chunk* value, int count)
            {
                var megachunkIndex = value->ChunkstoreIndex;
                megachunkIndex &= megachunksInUniverse - 1;
                byte* begin = (byte*)m_megachunk.ElementAt(megachunkIndex);
                byte* end = begin + MegachunkSizeInBytes;
                if (!(value >= begin && value < end))
                {
                    for(megachunkIndex = 0; megachunkIndex < megachunksInUniverse; ++megachunkIndex)
                    {
                        begin = (byte*)m_megachunk.ElementAt(megachunkIndex);
                        end = begin + MegachunkSizeInBytes;
                        if(value >= begin && value < end)
                            break;
                    }
                    if(megachunkIndex == megachunksInUniverse)
                    {
                        return kErrorChunkNotFound;
                    }
                }
                int megachunkInChunk = (int)((byte*)value - begin) >> Log2ChunkSizeInBytesRoundedUpToPow2;
                int error = ConcurrentMask.TryFree(ref m_chunkInUse.ElementAt(megachunkIndex), megachunkInChunk, count);
                if(error == ConcurrentMask.ErrorFailedToFree)
                {
                    ThrowChunkAlreadyMarkedAsFree(value);
                    return kErrorChunkAlreadyMarkedFree; // yeah, it's zero. somebody already freed it. shouldn'tve happened
                }
                ConcurrentMask.AtomicAnd(ref m_megachunkIsFull.ElementAt(megachunkIndex>>6), ~(1L << (megachunkIndex & 63)));
                if(error == ConcurrentMask.EmptyAfterFree) // if the mask is all 0 after we mask ourselves out,
                {
                    var oldValue = (long)begin;
                    if(oldValue == Interlocked.CompareExchange(ref m_megachunk.ElementAt(megachunkIndex), 0L, oldValue)) // store a 0
                        Memory.Unmanaged.Free((void*)oldValue, Allocator.Persistent); // free it too.
                }
                return kErrorNone;
            }

            public void Dispose()
            {
                for(var megachunkIndex = 0; megachunkIndex < megachunksInUniverse; ++megachunkIndex)
                {
                    void* megachunk = (void*)m_megachunk.ElementAt(megachunkIndex);
                    if(megachunk != null)
                        Memory.Unmanaged.Free(megachunk, Allocator.Persistent);
                }
                this = default;
            }
        }

        internal static readonly SharedStatic<ChunkStore> s_chunkStore = SharedStatic<ChunkStore>.GetOrCreate<EntityComponentStore>();

        public static int AllocateContiguousChunk(int requestedCount, out Chunk* chunk, out int actualCount)
        {
            return s_chunkStore.Data.AllocateContiguousChunks(out chunk, requestedCount, out  actualCount);
        }

        public static int FreeContiguousChunks(Chunk* chunks, int count)
        {
            return s_chunkStore.Data.FreeContiguousChunks(chunks, count);
        }

        public static void GetChunkMemoryStats(out long reservedPages, out long committedPages, out ulong reservedBytes, out ulong committedBytes, out long pageSizeInBytes)
        {
            reservedPages = 0;
            reservedBytes = 0;
            committedPages = 0;
            committedBytes = 0;
            pageSizeInBytes = 0;
        }

        public Chunk* AllocateChunk()
        {
            Chunk* newChunk;
            // Allocate new chunk
            var success = s_chunkStore.Data.AllocateContiguousChunks(out newChunk, 1, out _);
            Assert.IsTrue(success == 0);
            Assert.IsTrue(newChunk != null);

            if (useMemoryInitPattern != 0)
            {
                var temp = newChunk->ChunkstoreIndex;
                UnsafeUtility.MemSet(newChunk, memoryInitPattern, Chunk.kChunkSize);
                newChunk->ChunkstoreIndex = temp;
            }
            return newChunk;
        }

        public void FreeChunk(Chunk* chunk)
        {
            var success = s_chunkStore.Data.FreeContiguousChunks(chunk, 1);
            Assert.IsTrue(success == 0);
        }

        public Archetype* GetExistingArchetype(ComponentTypeInArchetype* typesSorted, int count)
        {
            return m_TypeLookup.TryGet(typesSorted, count);
        }

        void ChunkAllocate<T>(void* pointer, int count = 1) where T : struct
        {
            void** pointerToPointer = (void**)pointer;
            *pointerToPointer =
                m_ArchetypeChunkAllocator.Allocate(UnsafeUtility.SizeOf<T>() * count, UnsafeUtility.AlignOf<T>());
        }

        internal static int GetComponentArraySize(int componentSize, int entityCount) => CollectionHelper.Align(componentSize * entityCount, CollectionHelper.CacheLineSize);

        static int CalculateSpaceRequirement(ushort* componentSizes, int componentCount, int entityCount)
        {
            int size = 0;
            for (int i = 0; i < componentCount; ++i)
                size += GetComponentArraySize(componentSizes[i], entityCount);
            return size;
        }

        static int CalculateChunkCapacity(int bufferSize, ushort* componentSizes, int count)
        {
            int totalSize = 0;
            for (int i = 0; i < count; ++i)
                totalSize += componentSizes[i];

            int capacity = bufferSize / totalSize;
            while (CalculateSpaceRequirement(componentSizes, count, capacity) > bufferSize)
                --capacity;
            return capacity;
        }

        static ulong CalculateStableHash(ComponentTypeInArchetype* types, int* typeMemoryOrder, int count)
        {
            ulong hash = 1;
            for (var i = 0; i < count; ++i)
            {
                var typeIndex = types[typeMemoryOrder[i]].TypeIndex;
                var stableTypeIndex = TypeManager.GetTypeInfo(typeIndex).StableTypeHash;
                hash = TypeHash.CombineFNV1A64(hash, stableTypeIndex);
            }
            return hash;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private static void ThrowIfComponentDataSizeIsLargerThanShortMaxValue(int sizeInChunk)
        {
            if (sizeInChunk > short.MaxValue)
                throw new ArgumentException($"Component Data sizes may not be larger than {short.MaxValue}");
        }

        internal Archetype* CreateArchetype(ComponentTypeInArchetype* types, int count)
        {
            AssertArchetypeComponents(types, count);

            // Compute how many IComponentData types store Entities and need to be patched.
            // Types can have more than one entity, which means that this count is not necessarily
            // the same as the type count.
            var scalarEntityPatchCount = 0;
            var bufferEntityPatchCount = 0;
            var numManagedArrays = 0;
            var numSharedComponents = 0;
            var enableableComponentIndexInArchetype = stackalloc int[count];
            var numEnableableComponents = 0;

            for (var i = 0; i < count; ++i)
            {
                ref readonly var ct = ref GetTypeInfo(types[i].TypeIndex);

                if (ct.Category == TypeManager.TypeCategory.ISharedComponentData)
                {
                    ++numSharedComponents;
                }
                else if (TypeManager.IsManagedComponent(types[i].TypeIndex))
                {
                    ++numManagedArrays;
                }
                else
                {
                    if (ct.BufferCapacity >= 0)
                        bufferEntityPatchCount += ct.EntityOffsetCount;
                    else if (ct.SizeInChunk > 0)
                        scalarEntityPatchCount += ct.EntityOffsetCount;
                }

                if (types[i].IsEnableable)
                    enableableComponentIndexInArchetype[numEnableableComponents++] = i;
            }

            Archetype* dstArchetype = null;
            ChunkAllocate<Archetype>(&dstArchetype);
            ChunkAllocate<ComponentTypeInArchetype>(&dstArchetype->Types, count);
            ChunkAllocate<ComponentTypeInArchetype>(&dstArchetype->EnableableTypeIndexInArchetype, numEnableableComponents);
            ChunkAllocate<int>(&dstArchetype->Offsets, count);
            ChunkAllocate<int>(&dstArchetype->SizeOfs, count);
            ChunkAllocate<int>(&dstArchetype->BufferCapacities, count);
            ChunkAllocate<int>(&dstArchetype->TypeMemoryOrder, count);
            ChunkAllocate<EntityRemapUtility.EntityPatchInfo>(&dstArchetype->ScalarEntityPatches, scalarEntityPatchCount);
            ChunkAllocate<EntityRemapUtility.BufferEntityPatchInfo>(&dstArchetype->BufferEntityPatches, bufferEntityPatchCount);

            dstArchetype->TypesCount = count;
            Memory.Array.Copy(dstArchetype->Types, types, count);
            dstArchetype->EnableableTypesCount = numEnableableComponents;
            Memory.Array.Copy(dstArchetype->EnableableTypeIndexInArchetype, (int*)enableableComponentIndexInArchetype, numEnableableComponents);
            dstArchetype->EntityCount = 0;
            dstArchetype->ChunksWithEmptySlots = new UnsafePtrList<Chunk>(0, Allocator.Persistent);
            dstArchetype->MatchingQueryData = new UnsafeList<IntPtr>(0, Allocator.Persistent);
            dstArchetype->NextChangedArchetype = null;
            dstArchetype->InstantiateArchetype = null;
            dstArchetype->CopyArchetype = null;
            dstArchetype->MetaChunkArchetype = null;
            dstArchetype->CleanupResidueArchetype = null;

            dstArchetype->Flags = 0;

            {
                short i = (short)count;
                do dstArchetype->FirstChunkComponent = i;
                while (types[--i].IsChunkComponent);
                i++;
                do dstArchetype->FirstSharedComponent = i;
                while (types[--i].IsSharedComponent);
                i++;
                do dstArchetype->FirstTagComponent = i;
                while (types[--i].IsZeroSized);
                i++;
                do dstArchetype->FirstManagedComponent = i;
                while (types[--i].IsManagedComponent);
                i++;
                do dstArchetype->FirstBufferComponent = i;
                while (types[--i].IsBuffer);
            }

            for (var i = 0; i < count; ++i)
            {
                var typeIndex = types[i].TypeIndex;
                ref readonly var typeInfo = ref GetTypeInfo(typeIndex);
                if (typeIndex == m_DisabledType)
                    dstArchetype->Flags |= ArchetypeFlags.Disabled;
                if (typeIndex == m_SystemInstanceType)
                    dstArchetype->Flags |= ArchetypeFlags.HasSystemInstanceComponents;
                if (typeIndex == m_PrefabType)
                    dstArchetype->Flags |= ArchetypeFlags.Prefab;
                if (typeIndex == m_ChunkHeaderType)
                    dstArchetype->Flags |= ArchetypeFlags.HasChunkHeader;
                if (typeInfo.HasBlobAssetRefs)
                    dstArchetype->Flags |= ArchetypeFlags.HasBlobAssetRefs;
                if (!types[i].IsChunkComponent && types[i].IsManagedComponent && typeInfo.Category == TypeManager.TypeCategory.UnityEngineObject)
                    dstArchetype->Flags |= ArchetypeFlags.HasCompanionComponents;
                if (types[i].IsManagedComponent && TypeManager.HasEntityReferences(typeIndex))
                    dstArchetype->Flags |= ArchetypeFlags.HasManagedEntityRefs;
                if (typeInfo.HasWeakAssetRefs)
                    dstArchetype->Flags |= ArchetypeFlags.HasWeakAssetRefs;
            }

            if (dstArchetype->NumManagedComponents > 0)
                dstArchetype->Flags |= ArchetypeFlags.HasManagedComponents;

            if (dstArchetype->NumBufferComponents > 0)
                dstArchetype->Flags |= ArchetypeFlags.HasBufferComponents;


            var chunkDataSize = Chunk.GetChunkBufferSize();

            dstArchetype->ScalarEntityPatchCount = scalarEntityPatchCount;
            dstArchetype->BufferEntityPatchCount = bufferEntityPatchCount;

            int maxCapacity = TypeManager.MaximumChunkCapacity;
            for (var i = 0; i < count; ++i)
            {
                ref readonly var cType = ref GetTypeInfo(types[i].TypeIndex);
                if (i < dstArchetype->NonZeroSizedTypesCount)
                {
                    ThrowIfComponentDataSizeIsLargerThanShortMaxValue(cType.SizeInChunk);
                    dstArchetype->SizeOfs[i] = (ushort)cType.SizeInChunk;
                    dstArchetype->BufferCapacities[i] = cType.BufferCapacity;
                }
                else
                {
                    dstArchetype->SizeOfs[i] = 0;
                    dstArchetype->BufferCapacities[i] = 0;
                }
                maxCapacity = math.min(maxCapacity, cType.MaximumChunkCapacity);
            }

            dstArchetype->ChunkCapacity = math.min(maxCapacity, CalculateChunkCapacity(chunkDataSize, dstArchetype->SizeOfs, dstArchetype->NonZeroSizedTypesCount));
            Assert.IsTrue(dstArchetype->ChunkCapacity > 0);
            Assert.IsTrue(Chunk.kMaximumEntitiesPerChunk >= dstArchetype->ChunkCapacity);
            dstArchetype->Chunks = new ArchetypeChunkData(count, numSharedComponents);

            dstArchetype->InstanceSize = 0;
            dstArchetype->InstanceSizeWithOverhead = 0;
            for (var i = 0; i < dstArchetype->NonZeroSizedTypesCount; ++i)
            {
                dstArchetype->InstanceSize += dstArchetype->SizeOfs[i];
                dstArchetype->InstanceSizeWithOverhead += GetComponentArraySize(dstArchetype->SizeOfs[i], 1);
            }

            // For serialization a stable ordering of the components in the
            // chunk is desired. The type index is not stable, since it depends
            // on the order in which types are added to the TypeManager.
            // A permutation of the types ordered by a TypeManager-generated
            // memory ordering is used instead.
            var memoryOrderings = stackalloc UInt64[count];
            var typeFlags = stackalloc int[count];

            for (int i = 0; i < count; ++i)
            {
                var typeIndex = types[i].TypeIndex;
                memoryOrderings[i] = GetTypeInfo(typeIndex).MemoryOrdering;
                typeFlags[i] = typeIndex.Flags;
            }

            // Having memory order depend on type flags has the advantage that
            // TypeMemoryOrder is stable within component types
            // i.e. if Types[X] is a buffer component then Types[TypeMemoryOrder[X]] is also a buffer component
            // this simplifies iterating types in memory order (mainly serialization code)
            bool MemoryOrderCompare(int lhs, int rhs)
            {
                if (typeFlags[lhs] == typeFlags[rhs])
                    return memoryOrderings[lhs] < memoryOrderings[rhs];
                return typeFlags[lhs] < typeFlags[rhs];
            }

            for (int i = 0; i < count; ++i)
            {
                int index = i;
                while (index > 1 && MemoryOrderCompare(i, dstArchetype->TypeMemoryOrder[index - 1]))
                {
                    dstArchetype->TypeMemoryOrder[index] = dstArchetype->TypeMemoryOrder[index - 1];
                    --index;
                }
                dstArchetype->TypeMemoryOrder[index] = i;
            }

            var usedBytes = 0;
            for (var i = 0; i < count; ++i)
            {
                var index = dstArchetype->TypeMemoryOrder[i];
                var sizeOf = dstArchetype->SizeOfs[index];

                // align usedBytes upwards (eating into alignExtraSpace) so that
                // this component actually starts at its required alignment.
                // Assumption is that the start of the entire data segment is at the
                // maximum possible alignment.
                dstArchetype->Offsets[index] = usedBytes;
                usedBytes += GetComponentArraySize(sizeOf, dstArchetype->ChunkCapacity);
            }

            // Fill in arrays of scalar, buffer and managed entity patches
            var scalarPatchInfo = dstArchetype->ScalarEntityPatches;
            var bufferPatchInfo = dstArchetype->BufferEntityPatches;
            for (var i = 0; i != count; i++)
            {
                ref readonly var ct = ref GetTypeInfo(types[i].TypeIndex);
                var offsets = GetEntityOffsets(ct);
                var offsetCount = ct.EntityOffsetCount;

                if (ct.BufferCapacity >= 0)
                {
                    bufferPatchInfo = EntityRemapUtility.AppendBufferEntityPatches(bufferPatchInfo, offsets, offsetCount, dstArchetype->Offsets[i], dstArchetype->SizeOfs[i], ct.ElementSize);
                }
                else if (TypeManager.IsManagedComponent(ct.TypeIndex))
                {
                }
                else if (ct.SizeInChunk > 0)
                {
                    scalarPatchInfo = EntityRemapUtility.AppendEntityPatches(scalarPatchInfo, offsets, offsetCount, dstArchetype->Offsets[i], dstArchetype->SizeOfs[i]);
                }
            }
            Assert.AreEqual(scalarPatchInfo - dstArchetype->ScalarEntityPatches, scalarEntityPatchCount);

            dstArchetype->ScalarEntityPatchCount = scalarEntityPatchCount;
            dstArchetype->BufferEntityPatchCount = bufferEntityPatchCount;
            UnsafeUtility.MemClear(dstArchetype->QueryMaskArray, sizeof(byte) * 128);

            // Update the list of all created archetypes
            m_Archetypes.Add(dstArchetype);

            dstArchetype->FreeChunksBySharedComponents = new ChunkListMap();
            dstArchetype->FreeChunksBySharedComponents.Init(16);

            m_TypeLookup.Add(dstArchetype);

            if (ArchetypeCleanupComplete(dstArchetype))
                dstArchetype->Flags |= ArchetypeFlags.CleanupComplete;
            if (ArchetypeCleanupNeeded(dstArchetype))
                dstArchetype->Flags |= ArchetypeFlags.CleanupNeeded;

            fixed(EntityComponentStore* entityComponentStore = &this)
            {
                dstArchetype->EntityComponentStore = entityComponentStore;
            }

            dstArchetype->StableHash = CalculateStableHash(types, dstArchetype->TypeMemoryOrder, count);

#if ENABLE_PROFILER
            EntitiesProfiler.ArchetypeAdded(dstArchetype);
#endif

            return dstArchetype;
        }

        private bool ArchetypeCleanupComplete(Archetype* archetype)
        {
            return archetype->TypesCount == 2 && archetype->Types[1].TypeIndex == m_CleanupEntityType;
        }

        private bool ArchetypeCleanupNeeded(Archetype* archetype)
        {
            for (var t = 1; t < archetype->TypesCount; ++t)
            {
                var type = archetype->Types[t];
                if (type.IsCleanupComponent)
                {
                    return true;
                }
            }

            return false;
        }

        public int CountEntities()
        {
            int entityCount = 0;
            for (var i = 0; i < m_Archetypes.Length; ++i)
            {
                var archetype = m_Archetypes.Ptr[i];
                entityCount += archetype->EntityCount;
            }

            return entityCount;
        }

        public struct ArchetypeChanges
        {
            public int StartIndex;
            public uint ArchetypeTrackingVersion;
        }

        public ArchetypeChanges BeginArchetypeChangeTracking()
        {
            m_ArchetypeTrackingVersion++;
            return new ArchetypeChanges
            {
                StartIndex = m_Archetypes.Length,
                ArchetypeTrackingVersion = m_ArchetypeTrackingVersion
            };
        }

        public void EndArchetypeChangeTracking(ArchetypeChanges changes, EntityQueryManager* queries)
        {
            if (m_Archetypes.Length - changes.StartIndex == 0)
                return;

            Assert.AreEqual(m_ArchetypeTrackingVersion, changes.ArchetypeTrackingVersion);
            var changeList = new UnsafePtrList<Archetype>(m_Archetypes.Ptr + changes.StartIndex, m_Archetypes.Length - changes.StartIndex);
            queries->AddAdditionalArchetypes(changeList);
        }


        internal struct ChunkListChanges
        {
            public Archetype* ArchetypeTrackingHead;

            public void Init()
            {
                ArchetypeTrackingHead = null;
            }

            public void TrackArchetype(Archetype* archetype)
            {
                if (archetype->NextChangedArchetype == null)
                {
                    archetype->NextChangedArchetype = ArchetypeTrackingHead;
                    ArchetypeTrackingHead = archetype;
                }
            }
        }

        public void InvalidateChunkListCacheForChangedArchetypes()
        {
            var archetype = m_ChunkListChangesTracker.ArchetypeTrackingHead;
            while(archetype != null)
            {
                var matchingQueryCount = archetype->MatchingQueryData.Length;
                for (int queryIndex = 0; queryIndex < matchingQueryCount; ++queryIndex)
                {
                    var queryData = (EntityQueryData*) archetype->MatchingQueryData.Ptr[queryIndex];
                    queryData->MatchingChunkCache.Invalidate();
                }

                var nextArchetype = archetype->NextChangedArchetype;
                archetype->NextChangedArchetype = null;
                archetype = nextArchetype;
            }

            m_ChunkListChangesTracker.ArchetypeTrackingHead = null;
        }

        public int ManagedComponentIndexUsedCount => m_ManagedComponentIndex - 1 - m_ManagedComponentFreeIndex.Length / 4;
        public int ManagedComponentFreeCount => m_ManagedComponentIndexCapacity - m_ManagedComponentIndex + m_ManagedComponentFreeIndex.Length / 4;

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertNoQueuedManagedDeferredCommands()
        {
            var isEmpty = ManagedChangesTracker.Empty;
            ManagedChangesTracker.Reset();
            Assert.IsTrue(isEmpty);
        }

        public void DeallocateManagedComponents(Chunk* chunk, int indexInChunk, int batchCount)
        {
            var archetype = chunk->Archetype;
            if (archetype->NumManagedComponents == 0)
                return;

            var firstManagedComponent = archetype->FirstManagedComponent;
            var numManagedComponents = archetype->NumManagedComponents;
            var freeCommandHandle = ManagedChangesTracker.BeginFreeManagedComponentCommand();
            for (int i = 0; i < numManagedComponents; ++i)
            {
                int type = i + firstManagedComponent;
                var a = (int*)ChunkDataUtility.GetComponentDataRO(chunk, 0, type);
                for (int ei = 0; ei < batchCount; ++ei)
                {
                    var managedComponentIndex = a[ei + indexInChunk];
                    if (managedComponentIndex == 0)
                        continue;

                    FreeManagedComponentIndex(managedComponentIndex);
                    ManagedChangesTracker.AddToFreeManagedComponentCommand(managedComponentIndex);
                }
            }
            ManagedChangesTracker.EndDeallocateManagedComponentCommand(freeCommandHandle);
        }

        public int GrowManagedComponentCapacity(int count)
        {
            return m_ManagedComponentIndexCapacity += math.max(m_ManagedComponentIndexCapacity / 2, count);
        }

        public void ReserveManagedComponentIndices(int count)
        {
            int freeCount = ManagedComponentFreeCount;
            if (freeCount >= count)
                return;
            int newCapacity = GrowManagedComponentCapacity(count - freeCount);
            ManagedChangesTracker.SetManagedComponentCapacity(newCapacity);
        }

        public int AllocateManagedComponentIndex()
        {
            if (!m_ManagedComponentFreeIndex.IsEmpty)
                return m_ManagedComponentFreeIndex.Pop<int>();

            if (m_ManagedComponentIndex == m_ManagedComponentIndexCapacity)
            {
                m_ManagedComponentIndexCapacity += m_ManagedComponentIndexCapacity / 2;
                ManagedChangesTracker.SetManagedComponentCapacity(m_ManagedComponentIndexCapacity);
            }
            return m_ManagedComponentIndex++;
        }

        public void AllocateManagedComponentIndices(int* dst, int count)
        {
            int freeCount = m_ManagedComponentFreeIndex.Length / sizeof(int);
            if (freeCount >= count)
            {
                var newFreeCount = freeCount - count;
                UnsafeUtility.MemCpy(dst, (int*)m_ManagedComponentFreeIndex.Ptr + newFreeCount, count * sizeof(int));
                m_ManagedComponentFreeIndex.Length = newFreeCount * sizeof(int);
            }
            else
            {
                UnsafeUtility.MemCpy(dst, (int*)m_ManagedComponentFreeIndex.Ptr, freeCount * sizeof(int));
                m_ManagedComponentFreeIndex.Length = 0;
                ReserveManagedComponentIndices(count - freeCount);
                for (int i = freeCount; i < count; ++i)
                    dst[i] = m_ManagedComponentIndex++;
            }
        }

        public void FreeManagedComponentIndex(int index)
        {
            Assert.AreNotEqual(0, index);
            m_ManagedComponentFreeIndex.Add(index);
        }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
        [MethodImpl(MethodImplOptions.NoInlining)]
        void JournalAddRecordGetRW(Entity entity, TypeIndex typeIndex, uint version, void* data)
        {
            EntitiesJournaling.RecordType recordType;
            void* recordData = null;
            int recordDataLength = 0;
            if (TypeManager.IsSharedComponentType(typeIndex))
            {
                // Getting RW data pointer on shared components should not be allowed
                return;
            }
            else if (TypeManager.IsManagedComponent(typeIndex))
            {
                recordType = EntitiesJournaling.RecordType.GetComponentObjectRW;
            }
            else if (TypeManager.IsBuffer(typeIndex))
            {
                recordType = EntitiesJournaling.RecordType.GetBufferRW;
            }
            else
            {
                recordType = EntitiesJournaling.RecordType.GetComponentDataRW;
                recordData = data;
                recordDataLength = TypeManager.GetTypeInfo(typeIndex).TypeSize;
            }

            fixed (EntityComponentStore* store = &this)
            {
                EntitiesJournaling.AddRecord(
                    recordType: recordType,
                    entityComponentStore: store,
                    globalSystemVersion: version,
                    entities: &entity,
                    entityCount: 1,
                    types: &typeIndex,
                    typeCount: 1,
                    data: recordData,
                    dataLength: recordDataLength);
            }
        }
#endif
    }

    unsafe struct LookupCache
    {
        // This struct caches information about single component type, which is expected to be accessed in several
        // chunks within a given archetype. The type itself is not stored.
        // If the Archetype pointer matches the target archetype, assume the cache is valid. and all fields can be
        // read safely. However, a valid cache may still have IndexInArchetype=-1 (if the archetype does not have the
        // component type). In this case, the remaining fields are undefined and should not be used.
        // If the Archetype pointer does not match the target archetype, the cache is stale and must be updated before
        // any field can be used.
        // No fields should be modified outside of the Update() method.
        // A cache instance should only ever be used within the context of a single component type. Updating the cache
        // to store a different type in the same archetype is not supported.
        [NativeDisableUnsafePtrRestriction]
        public Archetype* Archetype;
        public int        ComponentOffset;
        public ushort     ComponentSizeOf;
        public short      IndexInArchetype;

        // This method will *always* update the cache.
        // It should only be called if it's already been determined that the cache is stale.
        // It is safe to call if the archetype does not contain the type.
        public void Update(Archetype *archetype, TypeIndex typeIndex)
        {
            ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex, ref IndexInArchetype);
            ComponentOffset = IndexInArchetype == -1 ? 0 : archetype->Offsets[IndexInArchetype];
            ComponentSizeOf = IndexInArchetype == -1 ? (ushort)0 : archetype->SizeOfs[IndexInArchetype];
            Archetype = archetype;
        }
    }
}
