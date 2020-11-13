using System;
using System.Diagnostics;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Assertions;
using Unity.Mathematics;

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
// 2. That would be the only access point for JobComponentSystem.
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
            CloneHybridComponents,
            FreeManagedComponents,
            SetManagedComponentCapacity
        }

        public void Init()
        {
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
                CommandBuffer.Add<int>((int)Command.IncrementSharedComponentVersion);
                CommandBuffer.Add<int>(sharedComponentValues[i]);
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

        public void CloneHybridComponentBegin(int* srcIndices, int componentCount, Entity* dstEntities, int instanceCount, int* dstCompanionLinkIndices)
        {
            CommandBuffer.Add<int>((int)Command.CloneHybridComponents);
            CommandBuffer.AddArray<int>(srcIndices, componentCount);
            CommandBuffer.AddArray<Entity>(dstEntities, instanceCount);
            CommandBuffer.AddArray<int>(dstCompanionLinkIndices, dstCompanionLinkIndices == null ? 0 : instanceCount);
            CommandBuffer.Add<int>(instanceCount * componentCount);
        }

        public void CloneHybridComponentAddDstIndices(int* dstIndices, int count)
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

    [BurstCompile]
    [GenerateBurstMonoInterop("EntityComponentStore")]
    internal unsafe partial struct EntityComponentStore
    {
        [NativeDisableUnsafePtrRestriction]
        int* m_VersionByEntity;

        [NativeDisableUnsafePtrRestriction]
        Archetype** m_ArchetypeByEntity;

        [NativeDisableUnsafePtrRestriction]
        EntityInChunk* m_EntityInChunkByEntity;

        [NativeDisableUnsafePtrRestriction]
        int* m_ComponentTypeOrderVersion;

        BlockAllocator m_ArchetypeChunkAllocator;

        internal UnsafeArchetypePtrList m_Archetypes;

        ArchetypeListMap m_TypeLookup;

        internal int m_ManagedComponentIndex;
        internal int m_ManagedComponentIndexCapacity;
        internal UnsafeAppendBuffer m_ManagedComponentFreeIndex;

        internal ManagedDeferredCommands ManagedChangesTracker;

        internal ChunkListChanges m_ChunkListChangesTracker;

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

        int m_LinkedGroupType;
        int m_ChunkHeaderType;
        int m_PrefabType;
        int m_CleanupEntityType;
        int m_DisabledType;
        int m_EntityType;

        ComponentType m_ChunkHeaderComponentType;
        ComponentType m_EntityComponentType;

        TypeManager.TypeInfo* m_TypeInfos;
        TypeManager.EntityOffsetInfo* m_EntityOffsetInfos;

        internal byte memoryInitPattern;
        internal byte useMemoryInitPattern;        // should be bool, but it doesn't get along nice with burst so far, so we use a byte instead

        const int kMaximumEmptyChunksInPool = 16; // can't alloc forever
        const int kDefaultCapacity = 1024;
        internal const int kMaxSharedComponentCount = 8;

        struct AddressSpaceTagType { }
        static readonly SharedStatic<ulong> s_TotalChunkAddressSpaceInBytes = SharedStatic<ulong>.GetOrCreate<AddressSpaceTagType>();

        static readonly ulong DefaultChunkAddressSpaceInBytes = 1024UL * 1024UL * 1024UL;

        public static ulong TotalChunkAddressSpaceInBytes
        {
            get => s_TotalChunkAddressSpaceInBytes.Data > 0 ? s_TotalChunkAddressSpaceInBytes.Data - 1 : DefaultChunkAddressSpaceInBytes;
            set => s_TotalChunkAddressSpaceInBytes.Data = value + 1;
        }

#if UNITY_EDITOR
        [NativeDisableUnsafePtrRestriction]
        NumberedWords* m_NameByEntity;
#endif

        public int EntityOrderVersion => GetComponentTypeOrderVersion(m_EntityType);
        public int EntitiesCapacity => m_EntitiesCapacity;
        public uint GlobalSystemVersion => m_GlobalSystemVersion;

        public void SetGlobalSystemVersion(uint value)
        {
            m_GlobalSystemVersion = value;
        }

        void IncreaseCapacity()
        {
            EnsureCapacity(m_EntitiesCapacity * 2);
        }

        internal bool IsIntentionallyInconsistent => m_IntentionallyInconsistent == 1;
        internal const long k_MaximumEntitiesPerWorld = 128L * 1024L * 1024L; // roughly 128 million Entities per World, maximum

        void ResizeUnmanagedArrays(long oldValue, long newValue)
        {
            m_VersionByEntity = Memory.Unmanaged.Array.Resize(m_VersionByEntity, oldValue, newValue, Allocator.Persistent);
            IntPtr* temp = (IntPtr*) m_ArchetypeByEntity;
            m_ArchetypeByEntity = (Archetype**) Memory.Unmanaged.Array.Resize(temp, oldValue, newValue, Allocator.Persistent);
            m_EntityInChunkByEntity = Memory.Unmanaged.Array.Resize(m_EntityInChunkByEntity, oldValue, newValue, Allocator.Persistent);
#if UNITY_EDITOR
            m_NameByEntity = Memory.Unmanaged.Array.Resize(m_NameByEntity, oldValue, newValue, Allocator.Persistent);
#endif
        }

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

        private void InitializeAdditionalCapacity(int start)
        {
            for (var i = start; i != EntitiesCapacity; i++)
            {
                m_EntityInChunkByEntity[i].IndexInChunk = i + 1;
                m_VersionByEntity[i] = 1;
                m_EntityInChunkByEntity[i].Chunk = null;
#if UNITY_EDITOR
                m_NameByEntity[i] = new NumberedWords();
#endif
            }

            // Last entity indexInChunk identifies that we ran out of space...
            m_EntityInChunkByEntity[EntitiesCapacity - 1].IndexInChunk = -1;
        }

        public static void Create(EntityComponentStore* entities, ulong startChunkSequenceNumber, int newCapacity = kDefaultCapacity)
        {
            UnsafeUtility.MemClear(entities, sizeof(EntityComponentStore));

            entities->EnsureCapacity(newCapacity);
            entities->m_GlobalSystemVersion = ChangeVersionUtility.InitialGlobalSystemVersion;

            entities->m_ComponentTypeOrderVersion = Memory.Unmanaged.Array.Allocate<int>(TypeManager.MaximumTypesCount, Allocator.Persistent);
            Memory.Array.Clear(entities->m_ComponentTypeOrderVersion, TypeManager.MaximumTypesCount);

            entities->m_ArchetypeChunkAllocator = new BlockAllocator(AllocatorManager.Persistent, 16 * 1024 * 1024); // 16MB should be enough
            entities->m_TypeLookup = new ArchetypeListMap();
            entities->m_TypeLookup.Init(16);
            entities->m_NextChunkSequenceNumber = startChunkSequenceNumber;
            entities->m_Archetypes = new UnsafeArchetypePtrList(0, Allocator.Persistent);
            entities->ManagedChangesTracker = new ManagedDeferredCommands();
            entities->ManagedChangesTracker.Init();
            entities->m_ManagedComponentIndex = 1;
            entities->m_ManagedComponentIndexCapacity = 64;
            entities->m_ManagedComponentFreeIndex = new UnsafeAppendBuffer(1024, 16, Allocator.Persistent);
            entities->m_LinkedGroupType = TypeManager.GetTypeIndex<LinkedEntityGroup>();
            entities->m_ChunkHeaderType = TypeManager.GetTypeIndex<ChunkHeader>();
            entities->m_PrefabType = TypeManager.GetTypeIndex<Prefab>();
            entities->m_CleanupEntityType = TypeManager.GetTypeIndex<CleanupEntity>();
            entities->m_DisabledType = TypeManager.GetTypeIndex<Disabled>();
            entities->m_EntityType = TypeManager.GetTypeIndex<Entity>();

            entities->m_ChunkHeaderComponentType = ComponentType.ReadWrite<ChunkHeader>();
            entities->m_EntityComponentType = ComponentType.ReadWrite<Entity>();
            entities->InitializeTypeManagerPointers();

            entities->m_ChunkListChangesTracker = new ChunkListChanges();
            entities->m_ChunkListChangesTracker.Init();

            // Sanity check a few alignments
#if UNITY_ASSERTIONS
            // Buffer should be 16 byte aligned to ensure component data layout itself can guarantee being aligned
            var offset = UnsafeUtility.GetFieldOffset(typeof(Chunk).GetField("Buffer"));
            Assert.IsTrue(offset % TypeManager.MaximumSupportedAlignment == 0, $"Chunk buffer must be {TypeManager.MaximumSupportedAlignment} byte aligned (buffer offset at {offset})");
            Assert.IsTrue(sizeof(Entity) == 8, $"Unity.Entities.Entity is expected to be 8 bytes in size (is {sizeof(Entity)}); if this changes, update Chunk explicit layout");
#endif
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var bufHeaderSize = UnsafeUtility.SizeOf<BufferHeader>();
            Assert.IsTrue(bufHeaderSize % TypeManager.MaximumSupportedAlignment == 0,
                $"BufferHeader total struct size must be a multiple of the max supported alignment ({TypeManager.MaximumSupportedAlignment})");
#endif
        }

        internal void InitializeTypeManagerPointers()
        {
            m_TypeInfos = TypeManager.GetTypeInfoPointer();
            m_EntityOffsetInfos = TypeManager.GetEntityOffsetsPointer();
        }

        public ref readonly TypeManager.TypeInfo GetTypeInfo(int typeIndex)
        {
            return ref m_TypeInfos[typeIndex & TypeManager.ClearFlagsMask];
        }

        public TypeManager.EntityOffsetInfo* GetEntityOffsets(in TypeManager.TypeInfo typeInfo)
        {
            return m_EntityOffsetInfos + typeInfo.EntityOffsetStartIndex;
        }

        public int ChunkComponentToNormalTypeIndex(int typeIndex) => m_TypeInfos[typeIndex & TypeManager.ClearFlagsMask].TypeIndex;

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
#if UNITY_EDITOR
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
        }

        public void FreeAllEntities(bool resetVersion)
        {
            for (var i = 0; i != EntitiesCapacity; i++)
            {
                m_EntityInChunkByEntity[i].IndexInChunk = i + 1;
                m_EntityInChunkByEntity[i].Chunk = null;
#if UNITY_EDITOR
                m_NameByEntity[i] = new NumberedWords();
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
#if UNITY_EDITOR
                m_NameByEntity[index] = new NumberedWords();
#endif
                freeIndex = index;
            }

            m_NextFreeEntityIndex = freeIndex;
            m_EntityCreateDestroyVersion++;
        }

#if UNITY_EDITOR
        public string GetName(Entity entity)
        {
            return m_NameByEntity[entity.Index].ToString();
        }

        public void SetName(Entity entity, string name)
        {
            m_NameByEntity[entity.Index].SetString(name);
        }

        public void CopyName(Entity dstEntity, Entity srcEntity)
        {
            m_NameByEntity[dstEntity.Index] = m_NameByEntity[srcEntity.Index];
        }

#endif

        public int GetStoredVersion(Entity entity) => m_VersionByEntity[entity.Index];

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

        public void IncrementComponentTypeOrderVersion(Archetype* archetype)
        {
            // Increment type component version
            for (var t = 0; t < archetype->TypesCount; ++t)
            {
                var typeIndex = archetype->Types[t].TypeIndex;
                m_ComponentTypeOrderVersion[typeIndex & TypeManager.ClearFlagsMask]++;
            }
        }

        public bool Exists(Entity entity)
        {
            int index = entity.Index;

            ValidateEntity(entity);

            var versionMatches = m_VersionByEntity[index] == entity.Version;
            var hasChunk = m_EntityInChunkByEntity[index].Chunk != null;

            return versionMatches && hasChunk;
        }

        public int GetComponentTypeOrderVersion(int typeIndex)
        {
            return m_ComponentTypeOrderVersion[typeIndex & TypeManager.ClearFlagsMask];
        }

        public void IncrementGlobalSystemVersion()
        {
            ChangeVersionUtility.IncrementGlobalSystemVersion(ref m_GlobalSystemVersion);
        }

        public bool HasComponent(Entity entity, int type)
        {
            if (!Exists(entity))
                return false;

            var archetype = m_ArchetypeByEntity[entity.Index];
            return ChunkDataUtility.GetIndexInTypeArray(archetype, type) != -1;
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

        public void SetChunkComponent(ArchetypeChunk* chunks, int chunkCount, void* componentData, int componentTypeIndex)
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

        public byte* GetComponentDataWithTypeRO(Entity entity, int typeIndex)
        {
            var entityChunk = m_EntityInChunkByEntity[entity.Index].Chunk;
            var entityIndexInChunk = m_EntityInChunkByEntity[entity.Index].IndexInChunk;

            return ChunkDataUtility.GetComponentDataWithTypeRO(entityChunk, entityIndexInChunk, typeIndex);
        }

        public byte* GetComponentDataWithTypeRW(Entity entity, int typeIndex, uint globalVersion)
        {
            var entityChunk = m_EntityInChunkByEntity[entity.Index].Chunk;
            var entityIndexInChunk = m_EntityInChunkByEntity[entity.Index].IndexInChunk;

            return ChunkDataUtility.GetComponentDataWithTypeRW(entityChunk, entityIndexInChunk, typeIndex,
                globalVersion);
        }

        public byte* GetComponentDataWithTypeRO(Entity entity, int typeIndex, ref LookupCache cache)
        {
            return ChunkDataUtility.GetComponentDataWithTypeRO(m_EntityInChunkByEntity[entity.Index].Chunk, m_ArchetypeByEntity[entity.Index], m_EntityInChunkByEntity[entity.Index].IndexInChunk, typeIndex, ref cache);
        }

        public byte* GetComponentDataWithTypeRW(Entity entity, int typeIndex, uint globalVersion, ref LookupCache cache)
        {
            return ChunkDataUtility.GetComponentDataWithTypeRW(m_EntityInChunkByEntity[entity.Index].Chunk, m_ArchetypeByEntity[entity.Index], m_EntityInChunkByEntity[entity.Index].IndexInChunk, typeIndex, globalVersion, ref cache);
        }

        public void* GetComponentDataRawRW(Entity entity, int typeIndex)
        {
            AssertEntityHasComponent(entity, typeIndex);
            return GetComponentDataRawRWEntityHasComponent(entity, typeIndex);
        }

        internal void* GetComponentDataRawRWEntityHasComponent(Entity entity, int typeIndex)
        {
            AssertZeroSizedComponent(typeIndex);
            var ptr = GetComponentDataWithTypeRW(entity, typeIndex, GlobalSystemVersion);
            return ptr;
        }

        public void SetComponentDataRawEntityHasComponent(Entity entity, int typeIndex, void* data, int size)
        {
            AssertEntityHasComponent(entity, typeIndex);
            AssertComponentSizeMatches(typeIndex, size);
            var ptr = GetComponentDataWithTypeRW(entity, typeIndex,
                GlobalSystemVersion);
            UnsafeUtility.MemCpy(ptr, data, size);
        }

        public void SetBufferRawWithValidation(Entity entity, int componentTypeIndex, BufferHeader* tempBuffer, int sizeInChunk)
        {
            AssertEntityHasComponent(entity, componentTypeIndex);

            var ptr = GetComponentDataWithTypeRW(entity, componentTypeIndex,
                GlobalSystemVersion);

            BufferHeader.Destroy((BufferHeader*)ptr);

            UnsafeUtility.MemCpy(ptr, tempBuffer, sizeInChunk);
        }

        public int GetSharedComponentDataIndex(Entity entity, int typeIndex)
        {
            var archetype = m_ArchetypeByEntity[entity.Index];
            var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);
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
#if UNITY_EDITOR
                m_NameByEntity[i] = new NumberedWords();
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
#if UNITY_EDITOR
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
#if UNITY_EDITOR
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

        public enum ComponentOperation
        {
            Null,
            AddComponent,
            RemoveComponent
        }

        [BurstMonoInteropMethod(MakePublic = false)]
        private static void _EntityBatchFromEntityChunkDataShared(in EntityInChunk* chunkData,
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



        [BurstMonoInteropMethod(MakePublic = false)]
        private static void _SortEntityInChunk(EntityInChunk* entityInChunks, int count)
        {
            NativeSortExtension.Sort(entityInChunks,count);
        }

        [BurstMonoInteropMethod(MakePublic = false)]
        private static void _GatherEntityInChunkForEntities(Entity* Entities,
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
            out NativeList<EntityBatchInChunk> entityBatchList)
        {
            return CreateEntityBatchList(m_EntityInChunkByEntity, entities,
                nSharedComponentsToAdd, out entityBatchList);
        }

        private static bool CreateEntityBatchList(EntityInChunk* entityInChunk, NativeArray<Entity> entities,
            int nSharedComponentsToAdd,
            out NativeList<EntityBatchInChunk> entityBatchList)
        {
            if (entities.Length == 0)
            {
                entityBatchList = default;
                return false;
            }

            var entityChunkData = new NativeArray<EntityInChunk>(entities.Length, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);

            GatherEntityInChunkForEntities((Entity*) entities.GetUnsafeReadOnlyPtr(),
                entityInChunk, (EntityInChunk*) entityChunkData.GetUnsafePtr(),entities.Length);


            SortEntityInChunk((EntityInChunk*)entityChunkData.GetUnsafePtr(), entityChunkData.Length);


            entityBatchList = new NativeList<EntityBatchInChunk>(entityChunkData.Length, Allocator.Persistent);
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


        public ulong AssignSequenceNumber(Chunk* chunk)
        {
            var sequenceNumber = m_NextChunkSequenceNumber;
            m_NextChunkSequenceNumber++;
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

        struct Ulong16384
        {
            private Ulong4096 p00;
            private Ulong4096 p01;
            private Ulong4096 p02;
            private Ulong4096 p03;
        }
#pragma warning restore 169

        struct ChunkStore
        {
            Ulong16384 m_megachunk;
            Ulong16384 m_chunkInUse;
            static readonly int megachunks = 16384;

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

            int AllocationFailed(int megachunkIndex, int offset, int count)
            {
                fixed(Ulong16384* b = &m_chunkInUse)
                {
                    long* chunkInUse = (long*)b;
                    long mask = 1L << offset;
                    while (true) // back out our change to the bitmask
                    {
                        long originalValue = Interlocked.Read(ref chunkInUse[megachunkIndex]); // get a mask word.
                        if ((originalValue & mask) == 0)
                            return kErrorChunkAlreadyMarkedFree; // someone already zeroed our bit! shouldn't happen but might.
                        long newValue = originalValue & ~mask; // zero our bit.
                        if (originalValue == Interlocked.CompareExchange(ref chunkInUse[megachunkIndex], newValue, originalValue))
                            return kErrorAllocationFailed; // report that the allocation itself failed (it did).
                    }
                }
            }

            void longestConsecutiveOnes(long value, out int offset, out int count)
            {
                count = 0;
                var newvalue = value;
                while(newvalue != 0)
                {
                    value = newvalue;
                    newvalue = value & (long)((ulong)value >> 1);
                    ++count;
                }
                offset = math.tzcnt(value);
            }
            void longestConsecutiveZeroes(long value, out int offset, out int count)
            {
                longestConsecutiveOnes(~value, out offset, out count);
            }

            bool foundAtLeastThisManyConsecutiveZeroes(long value, int minimum, out int offset, out int count)
            {
                longestConsecutiveZeroes(value, out offset, out count);
                return count >= minimum;
            }

            public int AllocateContiguousChunks(out Chunk* value, int requestedCount, out int actualCount)
            {
                actualCount = math.min(64, requestedCount); // literally can't service requests for more
                value = null;
                fixed(Ulong16384* a = &m_megachunk)
                fixed(Ulong16384* b = &m_chunkInUse)
                {
                    long* megachunk = (long*)a;
                    long* chunkInUse = (long*)b;
                    while (actualCount > 0)
                    {
                        for (int megachunkIndex = 0; megachunkIndex < megachunks; ++megachunkIndex)
                        {
                            long originalValue = Interlocked.Read(ref chunkInUse[megachunkIndex]); // get a word.
                            while (foundAtLeastThisManyConsecutiveZeroes(originalValue, actualCount, out int offset,out int _)) // while this word has room for my request...
                            {
                                long mask = ((1L << actualCount) - 1) << offset; // make a mask for me
                                long newValue = originalValue | mask; // and mask me into the word
                                if (originalValue == Interlocked.CompareExchange(ref chunkInUse[megachunkIndex],
                                    newValue, originalValue)) // try to set my bits...
                                {
                                    if (originalValue == 0) // we are the one who allocates this megachunk! we set the first bit in the mask
                                    {
                                        long allocated = (long) Memory.Unmanaged.Allocate(MegachunkSizeInBytes,
                                            CollectionHelper.CacheLineSize, Allocator.Persistent);
                                        if (allocated == 0) // if the allocation failed...
                                            return AllocationFailed(megachunkIndex, offset, actualCount);
                                        while (0L != Interlocked.CompareExchange(ref megachunk[megachunkIndex],
                                            allocated, 0L))
                                        {
                                            // FreeChunk might have set bitmask to 0, without setting pointer to zero yet. wait for FreeChunk to proceed.
                                            // This will deadlock if we'd otherwise leak memory, which is by design.
                                        }
                                    }
                                    else
                                    {
                                        while (Interlocked.Read(ref megachunk[megachunkIndex]) == 0L)
                                        {
                                            // we didn't get to set bit 0, so we're not responsible for allocating. but the pointer is still zero so the guy
                                            // we expect to allocate on our behalf either isn't done, or failed and gave up.
                                            if ((Interlocked.Read(ref chunkInUse[megachunkIndex]) & 1) == 0) // did my leader fail and give up?
                                                return AllocationFailed(megachunkIndex, offset, actualCount);
                                            // AllocateChunk might have set bitmask to non-0, without writing allocated pointer yet. wait for AllocateChunk to proceed.
                                            // This will deadlock if we'd otherwise dereference a null pointer, which is by design.
                                        }
                                    }

                                    value = (Chunk*) ((byte*) megachunk[megachunkIndex] + (offset << Log2ChunkSizeInBytesRoundedUpToPow2));
                                    return kErrorNone;
                                }

                                originalValue = Interlocked.Read(ref chunkInUse[megachunkIndex]);
                                // failed to set bit! read the word again and try again...
                            }
                        }
                        actualCount >>= 1; // we tried to get what was asked for, but couldn't find space. halve request and try again.
                    }
                    return kErrorNoChunksAvailable;
                }
            }

            public int FreeContiguousChunks(Chunk* value, int count)
            {
                fixed (Ulong16384* a = &m_megachunk)
                fixed (Ulong16384* b = &m_chunkInUse)
                {
                    long* megachunk = (long*)a;
                    long* chunkInUse = (long*)b;
                    for (int megachunkIndex = 0; megachunkIndex < megachunks; ++megachunkIndex)
                    {
                        byte* begin = (byte*)Interlocked.Read(ref megachunk[megachunkIndex]);
                        byte* end = begin + MegachunkSizeInBytes;
                        if (value >= begin && value < end) // we found our chunk! nobody's allowed to compete with us for freeing it.
                        {
                            int bit = (int)((byte*)value - begin) >> Log2ChunkSizeInBytesRoundedUpToPow2;
                            long mask = ((1L << count) - 1) << bit;
                            while (true)
                            {
                                long originalValue = Interlocked.Read(ref chunkInUse[megachunkIndex]);
                                if ((originalValue & mask) == 0) // is our bit already 0? that'd mean someone already freed my pointer
                                    return kErrorChunkAlreadyMarkedFree; // yeah, it's zero. somebody already freed it. shouldn'tve happened
                                long newValue = originalValue & ~mask; // zero out our bit
                                if (originalValue == Interlocked.CompareExchange(ref chunkInUse[megachunkIndex], newValue, originalValue)) // try to clear the bit.
                                {
                                    if (newValue == 0L) // we successfully set a ulong with 1 bit set, to a ulong with 0 bits set.
                                    {
                                        long allocated = Interlocked.Exchange(ref megachunk[megachunkIndex], 0L); // swap the pointer with null
                                        if (allocated == 0L) // but if it was already null,
                                            return kErrorChunkAlreadyFreed; // someone already freed our pointer? that's uncool
                                        Memory.Unmanaged.Free((void*)allocated, Allocator.Persistent); // no need to hurry, nobody depends on this finishing
                                    }
                                    return kErrorNone;
                                }
                            } // fail! try to clear the bit again...
                        }
                    }
                    return kErrorChunkNotFound;
                }
            }
        }

        private static readonly SharedStatic<ChunkStore> s_chunkStore = SharedStatic<ChunkStore>.GetOrCreate<EntityComponentStore>();

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
                UnsafeUtility.MemSet(newChunk, memoryInitPattern, Chunk.kChunkSize);
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
            }

            Archetype* dstArchetype = null;
            ChunkAllocate<Archetype>(&dstArchetype);
            ChunkAllocate<ComponentTypeInArchetype>(&dstArchetype->Types, count);
            ChunkAllocate<int>(&dstArchetype->Offsets, count);
            ChunkAllocate<int>(&dstArchetype->SizeOfs, count);
            ChunkAllocate<int>(&dstArchetype->BufferCapacities, count);
            ChunkAllocate<int>(&dstArchetype->TypeMemoryOrder, count);
            ChunkAllocate<EntityRemapUtility.EntityPatchInfo>(&dstArchetype->ScalarEntityPatches, scalarEntityPatchCount);
            ChunkAllocate<EntityRemapUtility.BufferEntityPatchInfo>(&dstArchetype->BufferEntityPatches, bufferEntityPatchCount);

            dstArchetype->TypesCount = count;
            Memory.Array.Copy(dstArchetype->Types, types, count);
            dstArchetype->EntityCount = 0;
            dstArchetype->Chunks = new ArchetypeChunkData(count, numSharedComponents);
            dstArchetype->ChunksWithEmptySlots = new UnsafeChunkPtrList(0, Allocator.Persistent);
            dstArchetype->MatchingQueryData = new UnsafePtrList(0, Allocator.Persistent);
            dstArchetype->NextChangedArchetype = null;
            dstArchetype->InstantiateArchetype = null;
            dstArchetype->CopyArchetype = null;
            dstArchetype->MetaChunkArchetype = null;
            dstArchetype->SystemStateResidueArchetype = null;

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
                if (typeIndex == m_PrefabType)
                    dstArchetype->Flags |= ArchetypeFlags.Prefab;
                if (typeIndex == m_ChunkHeaderType)
                    dstArchetype->Flags |= ArchetypeFlags.HasChunkHeader;
                if (typeInfo.HasBlobAssetRefs)
                    dstArchetype->Flags |= ArchetypeFlags.HasBlobAssetRefs;
                if (!types[i].IsChunkComponent && types[i].IsManagedComponent && typeInfo.Category == TypeManager.TypeCategory.UnityEngineObject)
                    dstArchetype->Flags |= ArchetypeFlags.HasHybridComponents;
                if (types[i].IsManagedComponent && TypeManager.HasEntityReferences(typeIndex))
                    dstArchetype->Flags |= ArchetypeFlags.HasManagedEntityRefs;
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
                    if (cType.SizeInChunk > short.MaxValue)
                        throw new ArgumentException($"Component Data sizes may not be larger than {short.MaxValue}");

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

            dstArchetype->InstanceSize = 0;
            dstArchetype->InstanceSizeWithOverhead = 0;
            for (var i = 0; i < dstArchetype->NonZeroSizedTypesCount; ++i)
            {
                dstArchetype->InstanceSize += dstArchetype->SizeOfs[i];
                dstArchetype->InstanceSizeWithOverhead += GetComponentArraySize(dstArchetype->SizeOfs[i], 1);
            }

            Assert.IsTrue(dstArchetype->ChunkCapacity > 0);
            Assert.IsTrue(Chunk.kMaximumEntitiesPerChunk >= dstArchetype->ChunkCapacity);

            // For serialization a stable ordering of the components in the
            // chunk is desired. The type index is not stable, since it depends
            // on the order in which types are added to the TypeManager.
            // A permutation of the types ordered by a TypeManager-generated
            // memory ordering is used instead.
            var memoryOrderings = stackalloc UInt64[count];
            var typeFlags = stackalloc int[count];

            for (int i = 0; i < count; ++i)
            {
                int typeIndex = types[i].TypeIndex;
                memoryOrderings[i] = GetTypeInfo(typeIndex).MemoryOrdering;
                typeFlags[i] = typeIndex & ~TypeManager.ClearFlagsMask;
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

            if (ArchetypeSystemStateCleanupComplete(dstArchetype))
                dstArchetype->Flags |= ArchetypeFlags.SystemStateCleanupComplete;
            if (ArchetypeSystemStateCleanupNeeded(dstArchetype))
                dstArchetype->Flags |= ArchetypeFlags.SystemStateCleanupNeeded;

            fixed(EntityComponentStore* entityComponentStore = &this)
            {
                dstArchetype->EntityComponentStore = entityComponentStore;
            }

            return dstArchetype;
        }

        private bool ArchetypeSystemStateCleanupComplete(Archetype* archetype)
        {
            return archetype->TypesCount == 2 && archetype->Types[1].TypeIndex == m_CleanupEntityType;
        }

        private bool ArchetypeSystemStateCleanupNeeded(Archetype* archetype)
        {
            for (var t = 1; t < archetype->TypesCount; ++t)
            {
                var type = archetype->Types[t];
                if (type.IsSystemStateComponent)
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
            Assert.AreEqual(m_ArchetypeTrackingVersion, changes.ArchetypeTrackingVersion);
            if (m_Archetypes.Length - changes.StartIndex == 0)
                return;

            var changeList = new UnsafeArchetypePtrList(m_Archetypes.Ptr + changes.StartIndex, m_Archetypes.Length - changes.StartIndex);
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
                    queryData->MatchingChunkCache.InvalidateCache();
                }

                var nextArchetype = archetype->NextChangedArchetype;
                archetype->NextChangedArchetype = null;
                archetype = nextArchetype;
            }

            m_ChunkListChangesTracker.ArchetypeTrackingHead = null;
        }

        public int ManagedComponentIndexUsedCount => m_ManagedComponentIndex - 1 - m_ManagedComponentFreeIndex.Length / 4;
        public int ManagedComponentFreeCount => m_ManagedComponentIndexCapacity - m_ManagedComponentIndex + m_ManagedComponentFreeIndex.Length / 4;

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
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
    }

    unsafe struct LookupCache
    {
        [NativeDisableUnsafePtrRestriction]
        public Archetype* Archetype;
        public int        ComponentOffset;
        public ushort     ComponentSizeOf;
        public short      IndexInArcheType;
    }
}
