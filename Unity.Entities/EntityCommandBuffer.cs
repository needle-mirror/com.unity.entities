using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;

namespace Unity.Entities
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct BasicCommand
    {
        public ECBCommand CommandType;
        public int TotalSize;
        public int SortKey;  /// Used to order command execution during playback
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CreateCommand
    {
        public BasicCommand Header;
        public EntityArchetype Archetype;
        public int IdentityIndex;
        public int BatchCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityCommand
    {
        public BasicCommand Header;
        public Entity Entity;
        public int IdentityIndex;
        public int BatchCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MultipleEntitiesCommand
    {
        public BasicCommand Header;
        public EntityNode Entities;
        public int EntitiesCount;
        public Allocator Allocator;
        public int SkipDeferredEntityLookup;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MultipleEntitiesComponentCommand
    {
        public MultipleEntitiesCommand Header;
        public int ComponentTypeIndex;
        public int ComponentSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct MultipleEntitiesComponentCommandWithObject
    {
        public MultipleEntitiesCommand Header;
        public int ComponentTypeIndex;
        public int HashCode;
        public EntityComponentGCNode GCNode;

        internal object GetBoxedObject()
        {
            if (GCNode.BoxedObject.IsAllocated)
                return GCNode.BoxedObject.Target;
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MultipleEntitiesAndComponentsCommand
    {
        public MultipleEntitiesCommand Header;
        public ComponentTypes Types;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityComponentCommand
    {
        public EntityCommand Header;
        public int ComponentTypeIndex;
        public int ComponentSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityComponentEnabledCommand
    {
        public EntityCommand Header;
        public int ComponentTypeIndex;
        public byte IsEnabled;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityNameCommand
    {
        public EntityCommand Header;
        public FixedString64Bytes Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityMultipleComponentsCommand
    {
        public EntityCommand Header;
        public ComponentTypes Types;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityBufferCommand
    {
        public EntityCommand Header;
        public int ComponentTypeIndex;
        public int ComponentSize;
        public BufferHeaderNode BufferNode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityQueryCommand
    {
        public BasicCommand Header;
        public unsafe EntityQueryData* QueryData;
        public EntityQueryFilter EntityQueryFilter;
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
        public unsafe EntityComponentStore* Store;
#endif
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityQueryComponentCommand
    {
        public EntityQueryCommand Header;
        public int ComponentTypeIndex;

        public int ComponentSize;
        // Data follows if command has an associated component payload
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct EntityQueryMultipleComponentsCommand
    {
        public EntityQueryCommand Header;
        public ComponentTypes Types;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct EntityQueryManagedComponentCommand
    {
        public EntityQueryCommand Header;
        public int ComponentTypeIndex;
        public EntityComponentGCNode GCNode;

        internal object GetBoxedObject()
        {
            if (GCNode.BoxedObject.IsAllocated)
                return GCNode.BoxedObject.Target;
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct EntityQuerySharedComponentCommand
    {
        public EntityQueryCommand Header;
        public int ComponentTypeIndex;
        public int HashCode;
        public EntityComponentGCNode GCNode;

        internal object GetBoxedObject()
        {
            if (GCNode.BoxedObject.IsAllocated)
                return GCNode.BoxedObject.Target;
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct EntityManagedComponentCommand
    {
        public EntityCommand Header;
        public int ComponentTypeIndex;
        public EntityComponentGCNode GCNode;

        internal object GetBoxedObject()
        {
            if (GCNode.BoxedObject.IsAllocated)
                return GCNode.BoxedObject.Target;
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct EntitySharedComponentCommand
    {
        public EntityCommand Header;
        public int ComponentTypeIndex;
        public int HashCode;
        public EntityComponentGCNode GCNode;

        internal object GetBoxedObject()
        {
            if (GCNode.BoxedObject.IsAllocated)
                return GCNode.BoxedObject.Target;
            return null;
        }
    }

    internal unsafe struct EntityComponentGCNode
    {
        public GCHandle BoxedObject;
        public EntityComponentGCNode* Prev;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct BufferHeaderNode
    {
        public BufferHeaderNode* Prev;
        public BufferHeader TempBuffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct EntityNode
    {
        public Entity* Ptr;
        public EntityNode* Prev;
    }


    [StructLayout(LayoutKind.Sequential, Size = 32)]
    internal unsafe struct ChainCleanup
    {
        public EntityNode* EntityArraysCleanupList;
        public BufferHeaderNode* BufferCleanupList;
        public EntityComponentGCNode* CleanupList;
    }

    [StructLayout(LayoutKind.Sequential, Size = (64 > JobsUtility.CacheLineSize) ? 64: JobsUtility.CacheLineSize)]
    internal unsafe struct EntityCommandBufferChain
    {
        public ECBChunk* m_Tail;
        public ECBChunk* m_Head;
        public ChainCleanup* m_Cleanup;
        public CreateCommand*                m_PrevCreateCommand;
        public EntityCommand*                m_PrevEntityCommand;
        public EntityCommandBufferChain* m_NextChain;
        public int m_LastSortKey;
        public bool m_CanBurstPlayback;

        internal static void InitChain(EntityCommandBufferChain* chain, Allocator allocator)
        {
            chain->m_Cleanup = (ChainCleanup*)Memory.Unmanaged.Allocate(sizeof(ChainCleanup), sizeof(ChainCleanup), allocator);
            chain->m_Cleanup->CleanupList = null;
            chain->m_Cleanup->BufferCleanupList = null;
            chain->m_Cleanup->EntityArraysCleanupList = null;

            chain->m_Tail = null;
            chain->m_Head = null;
            chain->m_PrevCreateCommand = null;
            chain->m_PrevEntityCommand = null;
            chain->m_LastSortKey = -1;
            chain->m_NextChain = null;
            chain->m_CanBurstPlayback = true;
        }
    }

    internal unsafe struct ECBSharedPlaybackState
    {
        public struct BufferWithFixUp
        {
            public EntityBufferCommand* cmd;
        }

        public Entity* CreateEntityBatch;
        public BufferWithFixUp* BuffersWithFixUp;
        public int CreatedEntityCount;
        public int LastBuffer;
        public int CommandBufferID;
    }

    internal unsafe struct ECBChainPlaybackState
    {
        public ECBChunk* Chunk;
        public int Offset;
        public int NextSortKey;
        public bool CanBurstPlayback;
    }

    internal unsafe struct ECBChainHeapElement
    {
        public int SortKey;
        public int ChainIndex;
    }
    internal unsafe struct ECBChainPriorityQueue : IDisposable
    {
        private readonly ECBChainHeapElement* m_Heap;
        private int m_Size;
        private readonly Allocator m_Allocator;
        private static readonly int BaseIndex = 1;
        public ECBChainPriorityQueue(ECBChainPlaybackState* chainStates, int chainStateCount, Allocator alloc)
        {
            m_Size = chainStateCount;
            m_Allocator = alloc;
            m_Heap = (ECBChainHeapElement*)Memory.Unmanaged.Allocate((m_Size + BaseIndex) * sizeof(ECBChainHeapElement), 64, m_Allocator);
            for (int i = m_Size - 1; i >= m_Size / 2; --i)
            {
                m_Heap[BaseIndex + i].SortKey = chainStates[i].NextSortKey;
                m_Heap[BaseIndex + i].ChainIndex = i;
            }
            for (int i = m_Size / 2 - 1; i >= 0; --i)
            {
                m_Heap[BaseIndex + i].SortKey = chainStates[i].NextSortKey;
                m_Heap[BaseIndex + i].ChainIndex = i;
                Heapify(BaseIndex + i);
            }
        }

        public void Dispose()
        {
            Memory.Unmanaged.Free(m_Heap, m_Allocator);
        }

        public bool Empty { get { return m_Size <= 0; } }
        public ECBChainHeapElement Peek()
        {
            //Assert.IsTrue(!Empty, "Can't Peek() an empty heap");
            if (Empty)
            {
                return new ECBChainHeapElement { ChainIndex = -1, SortKey = -1};
            }
            return m_Heap[BaseIndex];
        }

        public ECBChainHeapElement Pop()
        {
            //Assert.IsTrue(!Empty, "Can't Pop() an empty heap");
            if (Empty)
            {
                return new ECBChainHeapElement { ChainIndex = -1, SortKey = -1};
            }
            ECBChainHeapElement top = Peek();
            m_Heap[BaseIndex] = m_Heap[m_Size--];
            if (!Empty)
            {
                Heapify(BaseIndex);
            }
            return top;
        }

        public void ReplaceTop(ECBChainHeapElement value)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (Empty)
                Assert.IsTrue(false, "Can't ReplaceTop() an empty heap");
#endif
            m_Heap[BaseIndex] = value;
            Heapify(BaseIndex);
        }

        private void Heapify(int i)
        {
            // The index taken by this function is expected to be already biased by BaseIndex.
            // Thus, m_Heap[size] is a valid element (specifically, the final element in the heap)
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (i < BaseIndex || i > m_Size)
                Assert.IsTrue(false, "heap index " + i + " is out of range with size=" + m_Size);
#endif
            ECBChainHeapElement val = m_Heap[i];
            while (i <= m_Size / 2)
            {
                int child = 2 * i;
                if (child < m_Size && (m_Heap[child + 1].SortKey < m_Heap[child].SortKey))
                {
                    child++;
                }
                if (val.SortKey < m_Heap[child].SortKey)
                {
                    break;
                }
                m_Heap[i] = m_Heap[child];
                i = child;
            }
            m_Heap[i] = val;
        }
    }

    internal enum ECBCommand
    {
        InstantiateEntity,

        CreateEntity,
        DestroyEntity,

        AddComponent,
        AddMultipleComponents,
        AddComponentWithEntityFixUp,
        RemoveComponent,
        RemoveMultipleComponents,
        SetComponent,
        SetComponentWithEntityFixUp,
        SetComponentEnabled,
        SetName,

        AddBuffer,
        AddBufferWithEntityFixUp,
        SetBuffer,
        SetBufferWithEntityFixUp,
        AppendToBuffer,
        AppendToBufferWithEntityFixUp,

        AddManagedComponentData,
        SetManagedComponentData,

        AddSharedComponentData,
        SetSharedComponentData,

        AddComponentForMultipleEntities,
        AddComponentObjectForMultipleEntities,
        AddSharedComponentWithValueForMultipleEntities,
        AddMultipleComponentsForMultipleEntities,
        SetComponentObjectForMultipleEntities,
        SetSharedComponentValueForMultipleEntities,
        RemoveComponentForMultipleEntities,
        RemoveMultipleComponentsForMultipleEntities,
        DestroyMultipleEntities
    }

    /// <summary>
    /// Organized in memory like a single block with Chunk header followed by Size bytes of data.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct ECBChunk
    {
        internal int Used;
        internal int Size;
        internal ECBChunk* Next;
        internal ECBChunk* Prev;

        internal int Capacity => Size - Used;

        internal int Bump(int size)
        {
            var off = Used;
            Used += size;
            return off;
        }

        internal int BaseSortKey
        {
            get
            {
                fixed(ECBChunk* pThis = &this)
                {
                    if (Used < sizeof(BasicCommand))
                    {
                        return -1;
                    }
                    var buf = (byte*)pThis + sizeof(ECBChunk);
                    var header = (BasicCommand*)(buf);
                    return header->SortKey;
                }
            }
        }
    }

    internal unsafe struct EntityCommandBufferData
    {
        public EntityCommandBufferChain m_MainThreadChain;

        public EntityCommandBufferChain* m_ThreadedChains;

        public int m_RecordedChainCount;

        public int m_MinimumChunkSize;

        public Allocator m_Allocator;

        public PlaybackPolicy m_PlaybackPolicy;

        public bool m_ShouldPlayback;

        public bool m_DidPlayback;

        public Entity m_Entity;

        public int m_BufferWithFixupsCount;
        public UnsafeAtomicCounter32 m_BufferWithFixups;

        private static readonly int ALIGN_64_BIT = 8;

        public int m_CommandBufferID;

        internal void InitForParallelWriter()
        {
            if (m_ThreadedChains != null)
                return;

            // PERF: It's be great if we had a way to actually get the number of worst-case threads so we didn't have to allocate 128.
            int allocSize = sizeof(EntityCommandBufferChain) * JobsUtility.MaxJobThreadCount;

            m_ThreadedChains = (EntityCommandBufferChain*)Memory.Unmanaged.Allocate(allocSize, JobsUtility.CacheLineSize, m_Allocator);
            UnsafeUtility.MemClear(m_ThreadedChains, allocSize);

            for (var i = 0; i < JobsUtility.MaxJobThreadCount; ++i)
            {
                EntityCommandBufferChain.InitChain(&m_ThreadedChains[i], m_Allocator);
            }
        }

        internal void DestroyForParallelWriter()
        {
            if (m_ThreadedChains != null)
            {
                Memory.Unmanaged.Free(m_ThreadedChains, m_Allocator);
                m_ThreadedChains = null;
            }
        }

        private void ResetCreateCommandBatching(EntityCommandBufferChain* chain)
        {
            chain->m_PrevCreateCommand = null;
        }

        private void ResetEntityCommandBatching(EntityCommandBufferChain* chain)
        {
            chain->m_PrevEntityCommand = null;
        }

        internal void ResetCommandBatching(EntityCommandBufferChain* chain)
        {
            ResetCreateCommandBatching(chain);
            ResetEntityCommandBatching(chain);
        }

        internal Entity* CloneAndSearchForDeferredEntities(NativeArray<Entity> entities,
            out bool containsDeferredEntities)
        {
            var output = (Entity*)Memory.Unmanaged.Allocate(entities.Length * sizeof(Entity), ALIGN_64_BIT, m_Allocator);
            containsDeferredEntities = false;
            int i = 0;
            int len = entities.Length;
            for (; i < len; ++i)
            {
                var e = entities[i];
                output[i] = e;
                if (e.Index < 0)
                {
                    containsDeferredEntities = true;
                    break;
                }
            }
            for (; i < len; ++i)
            {
                output[i] = entities[i];
            }
            return output;
        }

        internal void AddCreateCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, int index, EntityArchetype archetype, bool batchable)
        {
            if (batchable &&
                chain->m_PrevCreateCommand != null &&
                chain->m_PrevCreateCommand->Archetype == archetype)
            {
                ++chain->m_PrevCreateCommand->BatchCount;
            }
            else
            {
                ResetEntityCommandBatching(chain);
                var cmd = (CreateCommand*)Reserve(chain, sortKey, sizeof(CreateCommand));

                cmd->Header.CommandType = op;
                cmd->Header.TotalSize = sizeof(CreateCommand);
                cmd->Header.SortKey = chain->m_LastSortKey;
                cmd->Archetype = archetype;
                cmd->IdentityIndex = index;
                cmd->BatchCount = 1;

                chain->m_PrevCreateCommand = cmd;
            }
        }

        internal void AddEntityCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, int index, Entity e, bool batchable)
        {
            if (batchable &&
                chain->m_PrevEntityCommand != null &&
                chain->m_PrevEntityCommand->Entity == e)
            {
                ++chain->m_PrevEntityCommand->BatchCount;
            }
            else
            {
                ResetCreateCommandBatching(chain);
                var cmd = (EntityCommand*)Reserve(chain, sortKey, sizeof(EntityCommand));

                cmd->Header.CommandType = op;
                cmd->Header.TotalSize = sizeof(EntityCommand);
                cmd->Header.SortKey = chain->m_LastSortKey;
                cmd->Entity = e;
                cmd->IdentityIndex = index;
                cmd->BatchCount = 1;
                chain->m_PrevEntityCommand = cmd;
            }
        }

        internal void AddMultipleEntityCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, int firstIndex, int count, Entity e, bool batchable)
        {
            if (batchable &&
                chain->m_PrevEntityCommand != null &&
                chain->m_PrevEntityCommand->Entity == e)
            {
                chain->m_PrevEntityCommand->BatchCount += count;
            }
            else
            {
                ResetCreateCommandBatching(chain);
                var cmd = (EntityCommand*)Reserve(chain, sortKey, sizeof(EntityCommand));

                cmd->Header.CommandType = op;
                cmd->Header.TotalSize = sizeof(EntityCommand);
                cmd->Header.SortKey = chain->m_LastSortKey;
                cmd->Entity = e;
                cmd->IdentityIndex = firstIndex;
                cmd->BatchCount = count;
                chain->m_PrevEntityCommand = null;
            }
        }

        internal bool RequiresEntityFixUp(byte* data, int typeIndex)
        {
            if (!TypeManager.HasEntityReferences(typeIndex))
                return false;

            var offsets = TypeManager.GetEntityOffsets(typeIndex, out var offsetCount);
            for (int i = 0; i < offsetCount; i++)
            {
                if (((Entity*)(data + offsets[i].Offset))->Index < 0)
                {
                    return true;
                }
            }
            return false;
        }

        internal void AddEntityComponentCommand<T>(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, Entity e, T component) where T : struct, IComponentData
        {
            var ctype = ComponentType.ReadWrite<T>();
            if (ctype.IsZeroSized)
            {
                AddEntityComponentTypeCommand(chain, sortKey, op, e, ctype);
                return;
            }

            // NOTE: This has to be sizeof not TypeManager.SizeInChunk since we use UnsafeUtility.CopyStructureToPtr
            //       even on zero size components.
            var typeSize = UnsafeUtility.SizeOf<T>();
            var sizeNeeded = Align(sizeof(EntityComponentCommand) + typeSize, ALIGN_64_BIT);

            ResetCommandBatching(chain);
            var cmd = (EntityComponentCommand*)Reserve(chain, sortKey, sizeNeeded);

            cmd->Header.Header.CommandType = op;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;
            cmd->Header.Entity = e;
            cmd->Header.IdentityIndex = 0;
            cmd->Header.BatchCount = 1;
            cmd->ComponentTypeIndex = ctype.TypeIndex;
            cmd->ComponentSize = typeSize;

            byte* data = (byte*)(cmd + 1);
            UnsafeUtility.CopyStructureToPtr(ref component, data);

            if (RequiresEntityFixUp(data, ctype.TypeIndex))
            {
                if (op == ECBCommand.AddComponent)
                    cmd->Header.Header.CommandType = ECBCommand.AddComponentWithEntityFixUp;
                else if (op == ECBCommand.SetComponent)
                    cmd->Header.Header.CommandType = ECBCommand.SetComponentWithEntityFixUp;
            }
        }

        internal void AddEntityComponentEnabledCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, Entity e, int typeIndex, bool value)
        {
            var sizeNeeded = Align(sizeof(EntityComponentEnabledCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);
            var cmd = (EntityComponentEnabledCommand*)Reserve(chain, sortKey, sizeNeeded);

            cmd->Header.Header.CommandType = op;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;
            cmd->Header.Entity = e;
            cmd->Header.IdentityIndex = 0;
            cmd->Header.BatchCount = 1;
            cmd->ComponentTypeIndex = typeIndex;
            cmd->IsEnabled = value ? (byte)1 : (byte)0;
        }

        internal void AddEntityNameCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, Entity e, in FixedString64Bytes name)
        {
            var sizeNeeded = Align(sizeof(EntityNameCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);
            var cmd = (EntityNameCommand*)Reserve(chain, sortKey, sizeNeeded);

            cmd->Header.Header.CommandType = op;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;
            cmd->Header.Entity = e;
            cmd->Header.IdentityIndex = 0;
            cmd->Header.BatchCount = 1;
            cmd->Name = name;
        }

        internal BufferHeader* AddEntityBufferCommand<T>(EntityCommandBufferChain* chain, int sortKey, ECBCommand op,
            Entity e, out int internalCapacity) where T : struct, IBufferElementData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            ref readonly var type = ref TypeManager.GetTypeInfo<T>();
            var sizeNeeded = Align(sizeof(EntityBufferCommand) + type.SizeInChunk, ALIGN_64_BIT);

            ResetCommandBatching(chain);
            var cmd = (EntityBufferCommand*)Reserve(chain, sortKey, sizeNeeded);

            cmd->Header.Header.CommandType = op;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;
            cmd->Header.Entity = e;
            cmd->Header.IdentityIndex = 0;
            cmd->Header.BatchCount = 1;
            cmd->ComponentTypeIndex = typeIndex;
            cmd->ComponentSize = type.SizeInChunk;

            BufferHeader* header = &cmd->BufferNode.TempBuffer;
            BufferHeader.Initialize(header, type.BufferCapacity);

            cmd->BufferNode.Prev = chain->m_Cleanup->BufferCleanupList;
            chain->m_Cleanup->BufferCleanupList = &(cmd->BufferNode);

            internalCapacity = type.BufferCapacity;

            if (TypeManager.HasEntityReferences(typeIndex))
            {
                if (op == ECBCommand.AddBuffer)
                {
                    m_BufferWithFixups.Add(1);
                    cmd->Header.Header.CommandType = ECBCommand.AddBufferWithEntityFixUp;
                }
                else if (op == ECBCommand.SetBuffer)
                {
                    m_BufferWithFixups.Add(1);
                    cmd->Header.Header.CommandType = ECBCommand.SetBufferWithEntityFixUp;
                }
            }

            return header;
        }

        internal static int Align(int size, int alignmentPowerOfTwo)
        {
            return (size + alignmentPowerOfTwo - 1) & ~(alignmentPowerOfTwo - 1);
        }

        internal void AddEntityComponentTypeCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, Entity e, ComponentType t)
        {
            var sizeNeeded = Align(sizeof(EntityComponentCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);
            var data = (EntityComponentCommand*)Reserve(chain, sortKey, sizeNeeded);

            data->Header.Header.CommandType = op;
            data->Header.Header.TotalSize = sizeNeeded;
            data->Header.Header.SortKey = chain->m_LastSortKey;
            data->Header.Entity = e;
            data->Header.IdentityIndex = 0;
            data->Header.BatchCount = 1;
            data->ComponentTypeIndex = t.TypeIndex;
            data->ComponentSize = 0;
        }

        internal void AddEntityComponentTypesCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, Entity e, ComponentTypes t)
        {
            var sizeNeeded = Align(sizeof(EntityMultipleComponentsCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);
            var data = (EntityMultipleComponentsCommand*)Reserve(chain, sortKey, sizeNeeded);

            data->Header.Header.CommandType = op;
            data->Header.Header.TotalSize = sizeNeeded;
            data->Header.Header.SortKey = chain->m_LastSortKey;
            data->Header.Entity = e;
            data->Header.IdentityIndex = 0;
            data->Header.BatchCount = 1;
            data->Types = t;
        }

        internal bool AppendMultipleEntitiesCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op,
            EntityQuery entityQuery)
        {
            var entities = entityQuery.ToEntityArray(m_Allocator);
            if (entities.Length == 0)
            {
                entities.Dispose();
                return false;
            }
            var result = AppendMultipleEntitiesCommand(chain, sortKey, op,
                (Entity*)entities.GetUnsafeReadOnlyPtr(), entities.Length, false);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref entities.m_Safety, ref entities.m_DisposeSentinel); // dispose safety handle, but we'll dispose of the actual data in playback
#endif
            return result;
        }
        internal bool AppendMultipleEntitiesCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op,
            Entity* entities, int entityCount, bool mayContainDeferredEntities)
        {
            var sizeNeeded = Align(sizeof(MultipleEntitiesCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);

            var cmd = (MultipleEntitiesCommand*)Reserve(chain, sortKey, sizeNeeded);
            cmd->Entities.Ptr = entities;
            cmd->Entities.Prev = chain->m_Cleanup->EntityArraysCleanupList;
            chain->m_Cleanup->EntityArraysCleanupList = &(cmd->Entities);

            cmd->EntitiesCount = entityCount;
            cmd->Allocator = m_Allocator;
            cmd->SkipDeferredEntityLookup = mayContainDeferredEntities ? 0 : 1;

            cmd->Header.CommandType = op;
            cmd->Header.TotalSize = sizeNeeded;
            cmd->Header.SortKey = chain->m_LastSortKey;

            return true;
        }

        internal bool AppendMultipleEntitiesComponentCommandWithValue<T>(EntityCommandBufferChain* chain, int sortKey,
            ECBCommand op, EntityQuery entityQuery, T component) where T : struct, IComponentData
        {
            var entities = entityQuery.ToEntityArray(m_Allocator); // disposed in playback
            if (entities.Length == 0)
            {
                entities.Dispose();
                return false;
            }
            var result = AppendMultipleEntitiesComponentCommandWithValue(chain, sortKey, op,
                (Entity*)entities.GetUnsafeReadOnlyPtr(), entities.Length, false, component);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref entities.m_Safety, ref entities.m_DisposeSentinel); // dispose safety handle, but we'll dispose of the actual data in playback
#endif
            return result;
        }
        internal bool AppendMultipleEntitiesComponentCommandWithValue<T>(EntityCommandBufferChain* chain, int sortKey,
            ECBCommand op, Entity* entities, int entityCount, bool mayContainDeferredEntities, T component) where T : struct, IComponentData
        {
            var ctype = ComponentType.ReadWrite<T>();
            if (ctype.IsZeroSized)
                return AppendMultipleEntitiesComponentCommand(chain, sortKey, op, entities, entityCount, mayContainDeferredEntities, ctype);

            var typeSize = UnsafeUtility.SizeOf<T>();
            var sizeNeeded = Align(sizeof(MultipleEntitiesComponentCommand) + typeSize, ALIGN_64_BIT);

            ResetCommandBatching(chain);

            var cmd = (MultipleEntitiesComponentCommand*)Reserve(chain, sortKey, sizeNeeded);
            cmd->Header.Entities.Ptr = entities;
            cmd->Header.Entities.Prev = chain->m_Cleanup->EntityArraysCleanupList;
            chain->m_Cleanup->EntityArraysCleanupList = &(cmd->Header.Entities);

            cmd->Header.EntitiesCount = entityCount;
            cmd->Header.Allocator = m_Allocator;
            cmd->Header.SkipDeferredEntityLookup = mayContainDeferredEntities ? 0 : 1;

            cmd->Header.Header.CommandType = op;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;
            cmd->ComponentTypeIndex = ctype.TypeIndex;
            cmd->ComponentSize = typeSize;

            byte* componentData = (byte*)(cmd + 1);
            // TODO(https://unity3d.atlassian.net/browse/DOTS-3465)
            Assert.IsFalse(RequiresEntityFixUp(componentData, ctype.TypeIndex),
                "This component value passed to this command contains a reference to a temporary Entity, which is not currently supported.");
            UnsafeUtility.CopyStructureToPtr(ref component, componentData);
            return true;
        }

        internal bool AppendMultipleEntitiesComponentCommandWithObject(EntityCommandBufferChain* chain, int sortKey,
            ECBCommand op, EntityQuery entityQuery, object boxedComponent, ComponentType ctype)
        {
            var entities = entityQuery.ToEntityArray(m_Allocator);
            if (entities.Length == 0)
            {
                entities.Dispose();
                return false;
            }
            var result = AppendMultipleEntitiesComponentCommandWithObject(chain, sortKey, op,
                (Entity*)entities.GetUnsafeReadOnlyPtr(), entities.Length, false, boxedComponent, ctype);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref entities.m_Safety, ref entities.m_DisposeSentinel); // dispose safety handle, but we'll dispose of the actual data in playback
#endif
            return result;
        }
        internal bool AppendMultipleEntitiesComponentCommandWithObject(EntityCommandBufferChain* chain, int sortKey,
            ECBCommand op, Entity* entities, int entityCount, bool mayContainDeferredEntities, object boxedComponent, ComponentType ctype)
        {
            var sizeNeeded = Align(sizeof(MultipleEntitiesComponentCommandWithObject), ALIGN_64_BIT);

            ResetCommandBatching(chain);

            var cmd = (MultipleEntitiesComponentCommandWithObject*)Reserve(chain, sortKey, sizeNeeded);
            chain->m_CanBurstPlayback = false;

            cmd->Header.Entities.Ptr = entities;
            cmd->Header.Entities.Prev = chain->m_Cleanup->EntityArraysCleanupList;
            chain->m_Cleanup->EntityArraysCleanupList = &(cmd->Header.Entities);

            cmd->Header.EntitiesCount = entityCount;
            cmd->Header.Allocator = m_Allocator;
            cmd->Header.SkipDeferredEntityLookup = mayContainDeferredEntities ? 0 : 1;

            cmd->Header.Header.CommandType = op;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;
            cmd->ComponentTypeIndex = ctype.TypeIndex;

            // TODO(https://unity3d.atlassian.net/browse/DOTS-3465): if boxedComponent contains Entity references to temporary Entities, they will not currently be fixed up.

            if (boxedComponent != null)
            {
                cmd->GCNode.BoxedObject = GCHandle.Alloc(boxedComponent);
                // We need to store all GCHandles on a cleanup list so we can dispose them later, regardless of if playback occurs or not.
                cmd->GCNode.Prev = chain->m_Cleanup->CleanupList;
                chain->m_Cleanup->CleanupList = &(cmd->GCNode);
            }
            else
            {
                cmd->GCNode.BoxedObject = new GCHandle();
            }
            return true;
        }

        internal bool AppendMultipleEntitiesComponentCommandWithSharedValue<T>(EntityCommandBufferChain* chain,
            int sortKey, ECBCommand op, EntityQuery entityQuery, int hashCode,
            object boxedComponent) where T : struct, ISharedComponentData
        {
            var entities = entityQuery.ToEntityArray(m_Allocator);
            if (entities.Length == 0)
            {
                entities.Dispose();
                return false;
            }
            var result = AppendMultipleEntitiesComponentCommandWithSharedValue<T>(chain, sortKey, op,
                (Entity*)entities.GetUnsafeReadOnlyPtr(), entities.Length, false, hashCode,
                boxedComponent);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref entities.m_Safety, ref entities.m_DisposeSentinel); // dispose safety handle, but we'll dispose of the actual data in playback
#endif
            return result;
        }
        internal bool AppendMultipleEntitiesComponentCommandWithSharedValue<T>(EntityCommandBufferChain* chain,
            int sortKey, ECBCommand op, Entity* entities, int entityCount, bool mayContainDeferredEntities, int hashCode,
            object boxedComponent) where T : struct, ISharedComponentData
        {
            var ctype = ComponentType.ReadWrite<T>();
            var sizeNeeded = Align(sizeof(MultipleEntitiesComponentCommandWithObject), ALIGN_64_BIT);

            ResetCommandBatching(chain);

            var cmd = (MultipleEntitiesComponentCommandWithObject*)Reserve(chain, sortKey, sizeNeeded);
            chain->m_CanBurstPlayback = false;

            cmd->Header.Entities.Ptr = entities;
            cmd->Header.Entities.Prev = chain->m_Cleanup->EntityArraysCleanupList;
            chain->m_Cleanup->EntityArraysCleanupList = &(cmd->Header.Entities);

            cmd->Header.EntitiesCount = entityCount;
            cmd->Header.Allocator = m_Allocator;
            cmd->Header.SkipDeferredEntityLookup = mayContainDeferredEntities ? 0 : 1;

            cmd->Header.Header.CommandType = op;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;
            cmd->ComponentTypeIndex = ctype.TypeIndex;
            cmd->HashCode = hashCode;

            // TODO(https://unity3d.atlassian.net/browse/DOTS-3465): if boxedComponent contains Entity references to temporary Entities, they will not currently be fixed up.

            if (boxedComponent != null)
            {
                cmd->GCNode.BoxedObject = GCHandle.Alloc(boxedComponent);
                // We need to store all GCHandles on a cleanup list so we can dispose them later, regardless of if playback occurs or not.
                cmd->GCNode.Prev = chain->m_Cleanup->CleanupList;
                chain->m_Cleanup->CleanupList = &(cmd->GCNode);
            }
            else
            {
                cmd->GCNode.BoxedObject = new GCHandle();
            }
            return true;
        }

        internal bool AppendMultipleEntitiesComponentCommand(EntityCommandBufferChain* chain, int sortKey,
            ECBCommand op, EntityQuery entityQuery, ComponentType t)
        {
            var entities = entityQuery.ToEntityArray(m_Allocator); // disposed in playback
            if (entities.Length == 0)
            {
                entities.Dispose();
                return false;
            }
            var result = AppendMultipleEntitiesComponentCommand(chain, sortKey, op,
                (Entity*)entities.GetUnsafeReadOnlyPtr(), entities.Length, false, t);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref entities.m_Safety, ref entities.m_DisposeSentinel); // dispose safety handle, but we'll dispose of the actual data in playback
#endif
            return result;
        }
        internal bool AppendMultipleEntitiesComponentCommand(EntityCommandBufferChain* chain, int sortKey, ECBCommand op,
            Entity* entities, int entityCount, bool mayContainDeferredEntities, ComponentType t)
        {
            var sizeNeeded = Align(sizeof(MultipleEntitiesComponentCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);

            var cmd = (MultipleEntitiesComponentCommand*)Reserve(chain, sortKey, sizeNeeded);
            cmd->Header.Entities.Ptr = entities;
            cmd->Header.Entities.Prev = chain->m_Cleanup->EntityArraysCleanupList;
            chain->m_Cleanup->EntityArraysCleanupList = &(cmd->Header.Entities);

            cmd->Header.EntitiesCount = entityCount;
            cmd->Header.Allocator = m_Allocator;
            cmd->Header.SkipDeferredEntityLookup = mayContainDeferredEntities ? 0 : 1;

            cmd->Header.Header.CommandType = op;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;
            cmd->ComponentTypeIndex = t.TypeIndex;
            cmd->ComponentSize = 0;   // signifies that the command doesn't include a value for the new component
            return true;
        }

        internal bool AppendMultipleEntitiesMultipleComponentsCommand(EntityCommandBufferChain* chain, int sortKey,
            ECBCommand op, EntityQuery entityQuery, ComponentTypes t)
        {
            var entities = entityQuery.ToEntityArray(m_Allocator); // disposed in playback
            if (entities.Length == 0)
            {
                entities.Dispose();
                return false;
            }
            var result = AppendMultipleEntitiesMultipleComponentsCommand(chain, sortKey, op,
                (Entity*)entities.GetUnsafeReadOnlyPtr(), entities.Length, false, t);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref entities.m_Safety, ref entities.m_DisposeSentinel); // dispose safety handle, but we'll dispose of the actual data in playback
#endif
            return result;
        }
        internal bool AppendMultipleEntitiesMultipleComponentsCommand(EntityCommandBufferChain* chain, int sortKey,
            ECBCommand op, Entity* entities, int entityCount, bool mayContainDeferredEntities, ComponentTypes t)
        {
            var sizeNeeded = Align(sizeof(MultipleEntitiesAndComponentsCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);

            var cmd = (MultipleEntitiesAndComponentsCommand*)Reserve(chain, sortKey, sizeNeeded);
            cmd->Header.Entities.Ptr = entities;
            cmd->Header.Entities.Prev = chain->m_Cleanup->EntityArraysCleanupList;
            chain->m_Cleanup->EntityArraysCleanupList = &(cmd->Header.Entities);

            cmd->Header.EntitiesCount = entityCount;
            cmd->Header.Allocator = m_Allocator;
            cmd->Header.SkipDeferredEntityLookup = mayContainDeferredEntities ? 0 : 1;

            cmd->Header.Header.CommandType = op;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;

            cmd->Types = t;
            return true;
        }

        internal void AddEntitySharedComponentCommand<T>(EntityCommandBufferChain* chain, int sortKey, ECBCommand op, Entity e, int hashCode, object boxedObject)
            where T : struct
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var sizeNeeded = Align(sizeof(EntitySharedComponentCommand), ALIGN_64_BIT);

            ResetCommandBatching(chain);
            var data = (EntitySharedComponentCommand*)Reserve(chain, sortKey, sizeNeeded);
            chain->m_CanBurstPlayback = false;

            data->Header.Header.CommandType = op;
            data->Header.Header.TotalSize = sizeNeeded;
            data->Header.Header.SortKey = chain->m_LastSortKey;
            data->Header.Entity = e;
            data->Header.IdentityIndex = 0;
            data->Header.BatchCount = 1;
            data->ComponentTypeIndex = typeIndex;
            data->HashCode = hashCode;

            // TODO(https://unity3d.atlassian.net/browse/DOTS-3465): if boxedComponent contains Entity references to temporary Entities, they will not currently be fixed up.

            if (boxedObject != null)
            {
                data->GCNode.BoxedObject = GCHandle.Alloc(boxedObject);
                // We need to store all GCHandles on a cleanup list so we can dispose them later, regardless of if playback occurs or not.
                data->GCNode.Prev = chain->m_Cleanup->CleanupList;
                chain->m_Cleanup->CleanupList = &(data->GCNode);
            }
            else
            {
                data->GCNode.BoxedObject = new GCHandle();
            }
        }

        internal byte* Reserve(EntityCommandBufferChain* chain, int sortKey, int size)
        {
            int newSortKey = sortKey;
            if (newSortKey < chain->m_LastSortKey)
            {
                // copy current chain to new next and reset current chain
                EntityCommandBufferChain* nextChain = (EntityCommandBufferChain*)Memory.Unmanaged.Allocate(sizeof(EntityCommandBufferChain), ALIGN_64_BIT, m_Allocator);
                *nextChain = *chain;
                EntityCommandBufferChain.InitChain(chain, m_Allocator);
                chain->m_NextChain = nextChain;
            }
            chain->m_LastSortKey = newSortKey;

            if (chain->m_Tail == null || chain->m_Tail->Capacity < size)
            {
                var chunkSize = math.max(m_MinimumChunkSize, size);

                var c = (ECBChunk*)Memory.Unmanaged.Allocate(sizeof(ECBChunk) + chunkSize, 16, m_Allocator);
                var prev = chain->m_Tail;
                c->Next = null;
                c->Prev = prev;
                c->Used = 0;
                c->Size = chunkSize;

                if (prev != null) prev->Next = c;

                if (chain->m_Head == null)
                {
                    chain->m_Head = c;
                    // This seems to be the best place to track the number of non-empty command buffer chunks
                    // during the recording process.
                    Interlocked.Increment(ref m_RecordedChainCount);
                }

                chain->m_Tail = c;
            }

            var offset = chain->m_Tail->Bump(size);
            var ptr = (byte*)chain->m_Tail + sizeof(ECBChunk) + offset;
            return ptr;
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public DynamicBuffer<T> CreateBufferCommand<T>(ECBCommand commandType, EntityCommandBufferChain* chain, int sortKey, Entity e, AtomicSafetyHandle bufferSafety, AtomicSafetyHandle arrayInvalidationSafety) where T : struct, IBufferElementData
#else
        public DynamicBuffer<T> CreateBufferCommand<T>(ECBCommand commandType, EntityCommandBufferChain* chain, int sortKey, Entity e) where T : struct, IBufferElementData
#endif
        {
            int internalCapacity;
            BufferHeader* header = AddEntityBufferCommand<T>(chain, sortKey, commandType, e, out internalCapacity);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safety = bufferSafety;
            AtomicSafetyHandle.UseSecondaryVersion(ref safety);
            var arraySafety = arrayInvalidationSafety;
            return new DynamicBuffer<T>(header, safety, arraySafety, false, false, 0, internalCapacity);
#else
            return new DynamicBuffer<T>(header, internalCapacity);
#endif
        }

        public void AppendToBufferCommand<T>(EntityCommandBufferChain* chain, int sortKey, Entity e, T element) where T : struct, IBufferElementData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            // NOTE: This has to be sizeof not TypeManager.SizeInChunk since we use UnsafeUtility.CopyStructureToPtr
            //       even on zero size components.
            var typeSize = UnsafeUtility.SizeOf<T>();
            var sizeNeeded = Align(sizeof(EntityComponentCommand) + typeSize, ALIGN_64_BIT);

            ResetCommandBatching(chain);
            var cmd = (EntityComponentCommand*)Reserve(chain, sortKey, sizeNeeded);

            cmd->Header.Header.CommandType = ECBCommand.AppendToBuffer;
            cmd->Header.Header.TotalSize = sizeNeeded;
            cmd->Header.Header.SortKey = chain->m_LastSortKey;
            cmd->Header.Entity = e;
            cmd->Header.IdentityIndex = 0;
            cmd->Header.BatchCount = 1;
            cmd->ComponentTypeIndex = typeIndex;
            cmd->ComponentSize = typeSize;

            byte* data = (byte*)(cmd + 1);
            UnsafeUtility.CopyStructureToPtr(ref element, data);

            if (TypeManager.HasEntityReferences(typeIndex))
            {
                cmd->Header.Header.CommandType = ECBCommand.AppendToBufferWithEntityFixUp;
            }
        }
    }

    /// <summary>
    /// Specifies if the <see cref="EntityCommandBuffer"/> can be played a single time or multiple times.
    /// </summary>
    public enum PlaybackPolicy
    {
        /// <summary>
        /// The <see cref="EntityCommandBuffer"/> can only be played once. After a first playback, the EntityCommandBuffer must be disposed.
        /// </summary>
        SinglePlayback,
        /// <summary>
        /// The <see cref="EntityCommandBuffer"/> can be played back more than once.
        /// </summary>
        /// <remarks>Even though the EntityCommandBuffer can be played back more than once, no commands can be added after the first playback.</remarks>
        MultiPlayback
    }

    /// <summary>
    ///     A thread-safe command buffer that can buffer commands that affect entities and components for later playback.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [NativeContainer]
    [BurstCompile]
    public unsafe partial struct EntityCommandBuffer : IDisposable
    {
        /// <summary>
        ///     The minimum chunk size to allocate from the job allocator.
        /// </summary>
        /// We keep this relatively small as we don't want to overload the temp allocator in case people make a ton of command buffers.
        private const int kDefaultMinimumChunkSize = 4 * 1024;

        [NativeDisableUnsafePtrRestriction] internal EntityCommandBufferData* m_Data;

        internal int SystemID;
        internal SystemHandleUntyped OriginSystemHandle;

        private static int ms_CommandBufferIDAllocator = 0;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety0;
        private AtomicSafetyHandle m_BufferSafety;
        private AtomicSafetyHandle m_ArrayInvalidationSafety;
        private int m_SafetyReadOnlyCount;
        private int m_SafetyReadWriteCount;

        [NativeSetClassTypeToNullOnSchedule]
        private DisposeSentinel m_DisposeSentinel;

        internal void WaitForWriterJobs()
        {
            AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_Safety0);
            AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_BufferSafety);
            AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_ArrayInvalidationSafety);
        }

        private static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<EntityCommandBuffer>();
        [BurstDiscard]
        private static void CreateStaticSafetyId()
        {
            s_staticSafetyId.Data = AtomicSafetyHandle.NewStaticSafetyId<EntityCommandBuffer>();
        }
#endif
        static readonly ProfilerMarker k_ProfileEcbPlayback = new ProfilerMarker("EntityCommandBuffer.Playback");

        /// <summary>
        ///     Allows controlling the size of chunks allocated from the temp job allocator to back the command buffer.
        /// </summary>
        /// Larger sizes are more efficient, but create more waste in the allocator.
        public int MinimumChunkSize
        {
            get { return m_Data->m_MinimumChunkSize > 0 ? m_Data->m_MinimumChunkSize : kDefaultMinimumChunkSize; }
            set { m_Data->m_MinimumChunkSize = Math.Max(0, value); }
        }

        /// <summary>
        /// Controls whether this command buffer should play back.
        /// </summary>
        ///
        /// This property is normally true, but can be useful to prevent
        /// the buffer from playing back when the user code is not in control
        /// of the site of playback.
        ///
        /// For example, is a buffer has been acquired from an EntityCommandBufferSystem and partially
        /// filled in with data, but it is discovered that the work should be aborted,
        /// this property can be set to false to prevent the buffer from playing back.
        public bool ShouldPlayback
        {
            get { return m_Data != null ? m_Data->m_ShouldPlayback : false; }
            set { if (m_Data != null) m_Data->m_ShouldPlayback = value; }
        }

        public bool IsEmpty => (m_Data == null) ? true : m_Data->m_RecordedChainCount == 0;

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void EnforceSingleThreadOwnership()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (m_Data == null)
                throw new NullReferenceException("The EntityCommandBuffer has not been initialized! The EntityCommandBuffer needs to be passed an Allocator when created!");
#endif
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety0);
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        internal void AssertDidNotPlayback()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (m_Data != null && m_Data->m_DidPlayback)
                throw new InvalidOperationException("The EntityCommandBuffer has already been played back and no further commands can be added.");
#endif
        }

        /// <summary>
        ///  Creates a new command buffer.
        /// </summary>
        /// <param name="label">Memory allocator to use for chunks and data</param>
        [NotBurstCompatible]
        public EntityCommandBuffer(Allocator label)
            : this(label, 1, PlaybackPolicy.SinglePlayback)
        {
        }

        /// <summary>
        ///  Creates a new command buffer.
        /// </summary>
        /// <param name="label">Memory allocator to use for chunks and data</param>
        /// <param name="playbackPolicy">Specifies if the EntityCommandBuffer can be played a single time or more than once.</param>
        [NotBurstCompatible]
        public EntityCommandBuffer(Allocator label, PlaybackPolicy playbackPolicy)
            : this(label, 1, playbackPolicy)
        {
        }

        /// <summary>
        ///  Creates a new command buffer.
        /// </summary>
        /// <param name="label">Memory allocator to use for chunks and data</param>
        /// <param name="disposeSentinelStackDepth">
        /// Specify how many stack frames to skip when reporting memory leaks.
        /// -1 will disable leak detection
        /// 0 or positive values
        /// </param>
        /// <param name="playbackPolicy">Specifies if the EntityCommandBuffer can be played a single time or more than once.</param>
        [NotBurstCompatible]
        internal EntityCommandBuffer(Allocator label, int disposeSentinelStackDepth, PlaybackPolicy playbackPolicy)
        {
            m_Data = (EntityCommandBufferData*)Memory.Unmanaged.Allocate(sizeof(EntityCommandBufferData), UnsafeUtility.AlignOf<EntityCommandBufferData>(), label);
            m_Data->m_Allocator = label;
            m_Data->m_PlaybackPolicy = playbackPolicy;
            m_Data->m_MinimumChunkSize = kDefaultMinimumChunkSize;
            m_Data->m_ShouldPlayback = true;
            m_Data->m_DidPlayback = false;
            m_Data->m_BufferWithFixupsCount = 0;
            m_Data->m_BufferWithFixups = new UnsafeAtomicCounter32(&m_Data->m_BufferWithFixupsCount);
            m_Data->m_CommandBufferID = --ms_CommandBufferIDAllocator;

            EntityCommandBufferChain.InitChain(&m_Data->m_MainThreadChain, label);

            m_Data->m_ThreadedChains = null;
            m_Data->m_RecordedChainCount = 0;

            SystemID = 0;
            OriginSystemHandle = default;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (disposeSentinelStackDepth >= 0 && !AllocatorManager.IsCustomAllocator(label))
            {
                DisposeSentinel.Create(out m_Safety0, out m_DisposeSentinel, disposeSentinelStackDepth, label);
            }
            else
            {
                m_DisposeSentinel = null;
                m_Safety0 = AtomicSafetyHandle.Create();
            }

            // Used for all buffers returned from the API, so we can invalidate them once Playback() has been called.
            m_BufferSafety = AtomicSafetyHandle.Create();
            // Used to invalidate array aliases to buffers
            m_ArrayInvalidationSafety = AtomicSafetyHandle.Create();

            m_SafetyReadOnlyCount = 0;
            m_SafetyReadWriteCount = 3;

            if (s_staticSafetyId.Data == 0)
            {
                CreateStaticSafetyId();
            }
            AtomicSafetyHandle.SetStaticSafetyId(ref m_Safety0, s_staticSafetyId.Data);
            AtomicSafetyHandle.SetStaticSafetyId(ref m_BufferSafety, s_staticSafetyId.Data);
            AtomicSafetyHandle.SetStaticSafetyId(ref m_ArrayInvalidationSafety, s_staticSafetyId.Data);
#endif
            m_Data->m_Entity = new Entity();
            m_Data->m_Entity.Version = m_Data->m_CommandBufferID;
            m_Data->m_BufferWithFixups.Reset();
        }

        public bool IsCreated   { get { return m_Data != null; } }

        [NotBurstCompatible]
        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref m_Safety0, ref m_DisposeSentinel);
            AtomicSafetyHandle.Release(m_ArrayInvalidationSafety);
            AtomicSafetyHandle.Release(m_BufferSafety);
#endif

            if (m_Data != null)
            {
                FreeChain(&m_Data->m_MainThreadChain, m_Data->m_PlaybackPolicy, m_Data->m_DidPlayback);

                if (m_Data->m_ThreadedChains != null)
                {
                    for (int i = 0; i < JobsUtility.MaxJobThreadCount; ++i)
                    {
                        FreeChain(&m_Data->m_ThreadedChains[i], m_Data->m_PlaybackPolicy, m_Data->m_DidPlayback);
                    }

                    m_Data->DestroyForParallelWriter();
                }

                Memory.Unmanaged.Free(m_Data, m_Data->m_Allocator);
                m_Data = null;
            }
        }

        private void FreeChain(EntityCommandBufferChain* chain, PlaybackPolicy playbackPolicy, bool didPlayback)
        {
            if (chain == null)
            {
                return;
            }

            var cleanup_list = chain->m_Cleanup->CleanupList;
            while (cleanup_list != null)
            {
                cleanup_list->BoxedObject.Free();
                cleanup_list = cleanup_list->Prev;
            }
            chain->m_Cleanup->CleanupList = null;

            // Buffers played in ecbs which can be played back more than once are always copied during playback.
            if (playbackPolicy == PlaybackPolicy.MultiPlayback || !didPlayback)
            {
                var bufferCleanupList = chain->m_Cleanup->BufferCleanupList;
                while (bufferCleanupList != null)
                {
                    var prev = bufferCleanupList->Prev;
                    BufferHeader.Destroy(&bufferCleanupList->TempBuffer);
                    bufferCleanupList = prev;
                }
            }
            chain->m_Cleanup->BufferCleanupList = null;

            // Arrays of entities captured from an input EntityQuery at record time are always cleaned up
            // at Dispose time.
            var entityArraysCleanupList = chain->m_Cleanup->EntityArraysCleanupList;
            while (entityArraysCleanupList != null)
            {
                var prev = entityArraysCleanupList->Prev;
                Memory.Unmanaged.Free(entityArraysCleanupList->Ptr, m_Data->m_Allocator);
                entityArraysCleanupList = prev;
            }
            chain->m_Cleanup->EntityArraysCleanupList = null;

            Memory.Unmanaged.Free(chain->m_Cleanup, m_Data->m_Allocator);

            while (chain->m_Tail != null)
            {
                var prev = chain->m_Tail->Prev;
                Memory.Unmanaged.Free(chain->m_Tail, m_Data->m_Allocator);
                chain->m_Tail = prev;
            }

            chain->m_Head = null;
            if (chain->m_NextChain != null)
            {
                FreeChain(chain->m_NextChain, playbackPolicy, didPlayback);
                Memory.Unmanaged.Free(chain->m_NextChain, m_Data->m_Allocator);
                chain->m_NextChain = null;
            }
        }

        internal int MainThreadSortKey => Int32.MaxValue;
        private const bool kBatchableCommand = true;

        /// <summary>Records a command to create an entity with specified archetype.</summary>
        /// <param name="archetype">The archetype of the new entity.</param>
        /// <returns>An entity that is deferred and will be fully realized when this EntityCommandBuffer is played back.</returns>
        /// <exception cref="ArgumentException">Throws if the archetype is null.</exception>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public Entity CreateEntity(EntityArchetype archetype)
        {
            archetype.CheckValidEntityArchetype();
            return _CreateEntity(archetype);
        }

        /// <summary>Records a command to create an entity with no components.</summary>
        /// <returns>An entity that is deferred and will be fully realized when this EntityCommandBuffer is played back.</returns>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public Entity CreateEntity()
        {
            EntityArchetype archetype = new EntityArchetype();
            return _CreateEntity(archetype);
        }

        private Entity _CreateEntity(EntityArchetype archetype)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            int index = --m_Data->m_Entity.Index;
            m_Data->AddCreateCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.CreateEntity, index, archetype, kBatchableCommand);
            return m_Data->m_Entity;
        }

        /// <summary>Records a command to create an entity with specified entity prefab.</summary>
        /// <remarks>An instantiated entity will have the same components and component values as the prefab entity, minus the Prefab tag component.
        /// Behavior at Playback: This command will throw an error if the source entity was destroyed before playback.</remarks>
        /// <param name="e">The entity prefab.</param>
        /// <returns>An entity that is deferred and will be fully realized when this EntityCommandBuffer is played back.</returns>
        /// <exception cref="ArgumentNullException"> Thrown if Entity e is null and if safety checks are enabled.</exception>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public Entity Instantiate(Entity e)
        {
            CheckEntityNotNull(e);
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            int index = --m_Data->m_Entity.Index;
            m_Data->AddEntityCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.InstantiateEntity,
                index, e, kBatchableCommand);
            return m_Data->m_Entity;
        }

        /// <summary>Records a command to create a NativeArray of entities with specified entity prefab.</summary>
        /// <remarks>An instantiated entity will have the same components and component values as the prefab entity, minus the Prefab tag component.
        /// Behavior at Playback: This command will throw an error if the source entity was destroyed before playback.</remarks>
        /// <param name="e">The entity prefab.</param>
        /// <param name="entities">The NativeArray of entities that will be populated with realized entities when this EntityCommandBuffer is played back.</param>
        /// <exception cref="ArgumentNullException"> Thrown if Entity e is null and if safety checks are enabled.</exception>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void Instantiate(Entity e, NativeArray<Entity> entities)
        {
            CheckEntityNotNull(e);
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            var entity = m_Data->m_Entity;
            int baseIndex = Interlocked.Add(ref m_Data->m_Entity.Index, -entities.Length) + entities.Length - 1;
            for (int i=0; i<entities.Length; ++i)
            {
                entity.Index = baseIndex - i;
                entities[i] = entity;
            }
            m_Data->AddMultipleEntityCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.InstantiateEntity, baseIndex, entities.Length, e, kBatchableCommand);
        }

        /// <summary>Records a command to destroy an entity.</summary>
        /// <remarks>Behavior at Playback: This command will throw an error if the entity is still deferred or was destroyed between recording and playback.
        /// </remarks>
        /// <param name="e">The entity to destroy.</param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void DestroyEntity(Entity e)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.DestroyEntity, 0, e, false);
        }

        /// <summary>Records a command to destroy a NativeArray of entities.</summary>
        /// <remarks>Behavior at Playback: This command will do nothing if entities has a count of 0.
        /// This command will throw an error if any of the entities are still deferred or were destroyed between recording and playback.
        /// </remarks>
        /// <param name="entities">The NativeArray of entities to destroy.</param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void DestroyEntity(NativeArray<Entity> entities)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
            m_Data->AppendMultipleEntitiesCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.DestroyMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities);
        }

        /// <summary>Records a command to add a dynamic buffer to an entity.</summary>
        /// <remarks>Behavior at Playback: If the entity already has this type of dynamic buffer, the dynamic buffer will just be set.
        /// Will throw an error if this entity is destroyed before playback, if this entity is still deferred,
        /// or if the entity doesn't have a <see cref="DynamicBuffer{T}"/> component storing elements of type T.</remarks>
        /// <param name="e">The entity to add the dynamic buffer to.</param>
        /// <typeparam name="T">The <see cref="IBufferElementData"/> type stored by the <see cref="DynamicBuffer{T}"/>.</typeparam>
        /// <returns>The <see cref="DynamicBuffer{T}"/> that will be added when the command plays back.</returns>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public DynamicBuffer<T> AddBuffer<T>(Entity e) where T : struct, IBufferElementData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            return m_Data->CreateBufferCommand<T>(ECBCommand.AddBuffer, &m_Data->m_MainThreadChain, MainThreadSortKey, e, m_BufferSafety, m_ArrayInvalidationSafety);
#else
            return m_Data->CreateBufferCommand<T>(ECBCommand.AddBuffer, &m_Data->m_MainThreadChain, MainThreadSortKey, e);
#endif
        }

        /// <summary>Records a command to set a dynamic buffer on an entity.</summary>
        /// <remarks>Behavior at Playback: Will throw an error if this entity is destroyed before playback,
        /// if this entity is still deferred, or if the entity doesn't have a <see cref="DynamicBuffer{T}"/> component storing elements of type T.</remarks>
        /// <param name="e">The entity to set the dynamic buffer on.</param>
        /// <typeparam name="T">The <see cref="IBufferElementData"/> type stored by the <see cref="DynamicBuffer{T}"/>.</typeparam>
        /// <returns>The <see cref="DynamicBuffer{T}"/> that will be set when the command plays back.</returns>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public DynamicBuffer<T> SetBuffer<T>(Entity e) where T : struct, IBufferElementData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            return m_Data->CreateBufferCommand<T>(ECBCommand.SetBuffer, &m_Data->m_MainThreadChain, MainThreadSortKey, e, m_BufferSafety, m_ArrayInvalidationSafety);
#else
            return m_Data->CreateBufferCommand<T>(ECBCommand.SetBuffer, &m_Data->m_MainThreadChain, MainThreadSortKey, e);
#endif
        }

        /// <summary>Records a command to append a single element to the end of a dynamic buffer component.</summary>
        /// <remarks>Behavior at Playback: Will throw an error if this entity is destroyed before playback,
        /// if this entity is still deferred, or if the entity doesn't have a <see cref="DynamicBuffer{T}"/> component storing elements of type T.</remarks>
        /// <param name="e">The entity to which the dynamic buffer belongs.</param>
        /// <param name="element">The new element to add to the <see cref="DynamicBuffer{T}"/> component.</param>
        /// <typeparam name="T">The <see cref="IBufferElementData"/> type stored by the <see cref="DynamicBuffer{T}"/>.</typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AppendToBuffer<T>(Entity e, T element) where T : struct, IBufferElementData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AppendToBufferCommand<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, e, element);
        }

        /// <summary> Records a command to add component of type T to an entity. </summary>
        /// <remarks>Behavior at Playback: If the entity already has this type of component, the value will just be set.
        /// Will throw an error if this entity is destroyed before playback, if this entity is still deferred,
        /// if T is type Entity, or adding this componentType makes the archetype too large.</remarks>
        /// <param name="e"> The entity to have the component added. </param>
        /// <param name="component">The value to add on the new component in playback for the entity.</param>
        /// <typeparam name="T"> The type of component to add. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddComponent<T>(Entity e, T component) where T : struct, IComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddComponent, e, component);
        }

        /// <summary> Records a command to add component of type T to a NativeArray of entities. </summary>
        /// <remarks>Behavior at Playback: If any entity already has this type of component, the value will just be set.
        /// Will throw an error if any entity is destroyed before playback, if any entity is still deferred,
        /// if T is type Entity, or adding this componentType makes the archetype too large.</remarks>
        /// <param name="entities"> The NativeArray of entities to have the component added. </param>
        /// <param name="component">The value to add on the new component in playback for all entities in the NativeArray.</param>
        /// <typeparam name="T"> The type of component to add. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddComponent<T>(NativeArray<Entity> entities, T component) where T : struct, IComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
            m_Data->AppendMultipleEntitiesComponentCommandWithValue(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.AddComponentForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, component);
        }

        /// <summary> Records a command to add component of type T to an entity. </summary>
        /// <remarks>Behavior at Playback: This command will do nothing if the entity already has the component.
        /// Will throw an error if this entity is destroyed before playback, if this entity is still deferred,
        /// if T is type Entity, or adding this componentType makes the archetype too large.</remarks>
        /// <param name="e"> The entity to have the component added. </param>
        /// <typeparam name="T"> The type of component to add. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddComponent<T>(Entity e) where T : struct, IComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentTypeCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddComponent, e, ComponentType.ReadWrite<T>());
        }

        /// <summary> Records a command to add component of type T to a NativeArray of entities. </summary>
        /// <remarks>Behavior at Playback: If an entity already has this component, it will be skipped.
        /// Will throw an error if any entity is destroyed before playback, if any entity is still deferred,
        /// if T is type Entity, or adding this componentType makes the archetype too large.</remarks>
        /// <param name="entities"> The NativeArray of entities to have the component added. </param>
        /// <typeparam name="T"> The type of component to add. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddComponent<T>(NativeArray<Entity> entities) where T : struct, IComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
            m_Data->AppendMultipleEntitiesComponentCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.AddComponentForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, ComponentType.ReadWrite<T>());
        }

        /// <summary> Records a command to add a component to an entity. </summary>
        /// <remarks>Behavior at Playback: This command will do nothing if the entity already has the component.
        /// Will throw an error if this entity is destroyed before playback, if this entity is still deferred,
        /// if component type is type Entity, or adding this componentType makes the archetype too large.</remarks>
        /// <param name="e"> The entity to get the additional component. </param>
        /// <param name="componentType"> The type of component to add. </param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddComponent(Entity e, ComponentType componentType)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentTypeCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddComponent, e, componentType);
        }

        /// <summary> Records a command to add a component to a NativeArray of entities. </summary>
        /// <remarks>Behavior at Playback: If an entity already has this component, it will be skipped.
        /// Will throw an error if any entity is destroyed before playback, if any entity is still deferred,
        /// if component type is type Entity, or adding this componentType makes the archetype too large.</remarks>
        /// <param name="entities"> The NativeArray of entities to have the component added. </param>
        /// <param name="componentType"> The type of component to add. </param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddComponent(NativeArray<Entity> entities, ComponentType componentType)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
            m_Data->AppendMultipleEntitiesComponentCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.AddComponentForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, componentType);
        }


        /// <summary> Records a command to add one or more components to an entity. </summary>
        /// <remarks>Behavior at Playback: It is not an error to include a component type that the entity already has.
        /// Will throw an error if this entity is destroyed before playback, if this entity is still deferred,
        /// if any component type is type Entity, or adding a component type makes the archetype too large.</remarks>
        /// <param name="e"> The entity to get additional components. </param>
        /// <param name="componentTypes"> The types of components to add. </param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddComponent(Entity e, ComponentTypes componentTypes)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentTypesCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddMultipleComponents, e, componentTypes);
        }

        /// <summary> Records a command to add one or more components to a NativeArray of entities. </summary>
        /// <remarks>Behavior at Playback: It is not an error to include a component type that any of the entities already have.
        /// Will throw an error if any entity is destroyed before playback, if any entity is still deferred,
        /// if any component type is type Entity, or adding a component type makes the archetype too large.</remarks>
        /// <param name="entities"> The NativeArray of entities to have the components added. </param>
        /// <param name="componentTypes"> The types of components to add. </param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddComponent(NativeArray<Entity> entities, ComponentTypes componentTypes)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
            m_Data->AppendMultipleEntitiesMultipleComponentsCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.AddMultipleComponentsForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, componentTypes);
        }

        /// <summary> Records a command to set a component value on an entity.</summary>
        /// <remarks> Behavior at Playback: Will throw an error if this entity is destroyed before playback,
        /// if this entity is still deferred, if the entity doesn't have the component type, or if T is zero sized.</remarks>
        /// <param name="e"> The entity to set the component value of. </param>
        /// <param name="component"> The component value to set. </param>
        /// <typeparam name="T"> The type of component to set. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void SetComponent<T>(Entity e, T component) where T : struct, IComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.SetComponent, e, component);
        }

        // TODO(https://unity3d.atlassian.net/browse/DOTS-4242): make these methods public once enabled bits support is complete
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        internal void SetComponentEnabled<T>(Entity e, bool value)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentEnabledCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.SetComponentEnabled, e, TypeManager.GetTypeIndex<T>(), value);
        }
        internal void SetComponentEnabled(Entity e, ComponentType componentType, bool value)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentEnabledCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.SetComponentEnabled, e, componentType.TypeIndex, value);
        }

        /// <summary> Records a command to set a name of an entity if Debug Names is enabled.</summary>
        /// <remarks> Behavior at Playback: Will throw an error if this entity is destroyed before playback,
        /// if this entity is still deferred, or if the EntityNameStore has reached its limit.</remarks>
        /// <param name="e"> The entity to set the name value of. </param>
        /// <param name="name"> The name to set. </param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void SetName(Entity e, in FixedString64Bytes name)
        {
#if !DOTS_DISABLE_DEBUG_NAMES
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityNameCommand(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.SetName, e, name);
#endif //!DOTS_DISABLE_DEBUG_NAMES
        }

        /// <summary> Records a command to remove component of type T from an entity. </summary>
        /// <remarks> Behavior at Playback: It is not an error if the entity doesn't have component T.
        /// Will throw an error if this entity is destroyed before playback,
        /// if this entity is still deferred, or if T is type Entity.</remarks>
        /// <param name="e"> The entity to have the component removed. </param>
        /// <typeparam name="T"> The type of component to remove. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void RemoveComponent<T>(Entity e)
        {
            RemoveComponent(e, ComponentType.ReadWrite<T>());
        }

        /// <summary> Records a command to remove component of type T from a NativeArray of entities. </summary>
        /// <remarks>Behavior at Playback: It is not an error if any entity doesn't have component T.
        /// Will throw an error if one of these entities is destroyed before playback,
        /// if one of these entities is still deferred, or if T is type Entity.</remarks>
        /// <param name="entities"> The NativeArray of entities to have the component removed. </param>
        /// <typeparam name="T"> The type of component to remove. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void RemoveComponent<T>(NativeArray<Entity> entities)
        {
            RemoveComponent(entities, ComponentType.ReadWrite<T>());
        }

        /// <summary> Records a command to remove a component from an entity. </summary>
        /// <remarks>Behavior at Playback: It is not an error if the entity doesn't have the component type.
        /// Will throw an error if this entity is destroyed before playback,
        /// if this entity is still deferred, or if the component type is Entity.</remarks>
        /// <param name="e"> The entity to have the component removed. </param>
        /// <param name="componentType"> The type of component to remove. </param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void RemoveComponent(Entity e, ComponentType componentType)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentTypeCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.RemoveComponent, e, componentType);
        }

        /// <summary> Records a command to remove one or more components from a NativeArray of entities. </summary>
        /// <remarks>Behavior at Playback: It is not an error if any entity doesn't have the component type.
        /// Will throw an error if one of these entities is destroyed before playback,
        /// if one of these entities is still deferred, or if the component type is Entity.</remarks>
        /// <param name="entities"> The NativeArray of entities to have the component removed. </param>
        /// <param name="componentType"> The type of component to remove. </param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void RemoveComponent(NativeArray<Entity> entities, ComponentType componentType)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
            m_Data->AppendMultipleEntitiesComponentCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.RemoveComponentForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, componentType);
        }

        /// <summary> Records a command to remove one or more components from an entity. </summary>
        /// <remarks>Behavior at Playback: It is not an error if the entity doesn't have one of the component types.
        /// Will throw an error if this entity is destroyed before playback,
        /// if this entity is still deferred, or if any of the component types are Entity.</remarks>
        /// <param name="e"> The entity to have components removed. </param>
        /// <param name="componentTypes"> The types of components to remove. </param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void RemoveComponent(Entity e, ComponentTypes componentTypes)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AddEntityComponentTypesCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.RemoveMultipleComponents, e, componentTypes);
        }

        /// <summary> Records a command to remove one or more components from a NativeArray of entities. </summary>
        /// <remarks>Behavior at Playback: It is not an error if any entity doesn't have one of the component types.
        /// Will throw an error if one of these entities is destroyed before playback,
        /// if one of these entities is still deferred, or if any of the component types are Entity.</remarks>
        /// <param name="entities"> The NativeArray of entities to have components removed. </param>
        /// <param name="componentTypes"> The types of components to remove. </param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void RemoveComponent(NativeArray<Entity> entities, ComponentTypes componentTypes)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
            m_Data->AppendMultipleEntitiesMultipleComponentsCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.RemoveMultipleComponentsForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, componentTypes);
        }

        /// <summary>Records a command to add a component to all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Does not affect entities which already have the component.
        ///
        /// Behavior at Playback: Will throw an error if one of these entities is destroyed before playback. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities to which the component is added. </param>
        /// <param name="componentType">The type of component to add.</param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddComponentForEntityQuery(EntityQuery entityQuery, ComponentType componentType)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AppendMultipleEntitiesComponentCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.AddComponentForMultipleEntities, entityQuery, componentType);
        }

        /// <summary>Records a command to add a component to all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Does not affect entities which already have the component.
        ///
        /// Behavior at Playback: Will throw an error if one of these entities is destroyed before playback. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities to which the component is added. </param>
        /// <typeparam name="T"> The type of component to add. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddComponentForEntityQuery<T>(EntityQuery entityQuery)
        {
            AddComponentForEntityQuery(entityQuery, ComponentType.ReadWrite<T>());
        }

        /// <summary>Records a command to add a component to all entities matching a query. Also sets the value of this new component on all the matching entities.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Entities which already have the component type will have the component set to the value.
        ///
        /// Behavior at Playback: Will throw an error if one of these entities is destroyed before playback. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities to which the component is added. </param>
        /// <param name="value">The value to set on the new component in playback for all entities matching the query.</param>
        /// <typeparam name="T"> The type of component to add. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddComponentForEntityQuery<T>(EntityQuery entityQuery, T value) where T : struct, IComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AppendMultipleEntitiesComponentCommandWithValue(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.AddComponentForMultipleEntities, entityQuery, value);
        }

        /// <summary>Records a command to add multiple components to all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Some matching entities may already have some or all of the specified components. After this operation, all matching entities will have all of the components.
        ///
        /// Behavior at Playback: Will throw an error if one of these entities is destroyed before playback. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities to which the components are added. </param>
        /// <param name="componentTypes">The types of components to add.</param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddComponentForEntityQuery(EntityQuery entityQuery, ComponentTypes componentTypes)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AppendMultipleEntitiesMultipleComponentsCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.AddMultipleComponentsForMultipleEntities, entityQuery, componentTypes);
        }

        /// <summary> Records a command to add a shared component to all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Entities which already have the component type will have the component set to the value.
        ///
        /// Behavior at Playback: Will throw an error if one of these entities is destroyed before playback. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery"> The query specifying which entities to add the component value to. </param>
        /// <param name="component"> The component value to add. </param>
        /// <typeparam name="T"> The type of shared component to set. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddSharedComponentForEntityQuery<T>(EntityQuery entityQuery, T component) where T : struct, ISharedComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();

            int hashCode;
            if (IsDefaultObject(ref component, out hashCode))
                m_Data->AppendMultipleEntitiesComponentCommandWithSharedValue<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddSharedComponentWithValueForMultipleEntities, entityQuery, hashCode, null);
            else
                m_Data->AppendMultipleEntitiesComponentCommandWithSharedValue<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddSharedComponentWithValueForMultipleEntities, entityQuery, hashCode, component);
        }

        /// <summary> Records a command to add a hybrid component and set its value for all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Behavior at Playback: Will throw an error if one of these entities is destroyed before playback.
        /// Entities which already have the component type will have the component set to the value.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown in playback if one or more of the entities has been destroyed. (With safety checks disabled,
        /// playback will perform invalid and unsafe memory access.).</exception>
        /// <param name="entityQuery"> The query specifying which entities to add the component value to.</param>
        /// <param name="componentData"> The component object to add. </param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        /// <exception cref="ArgumentNullException">Throws if componentData is null.</exception>
        public void AddComponentObjectForEntityQuery(EntityQuery entityQuery, object componentData)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOGS_DEBUG
            if (componentData == null)
                throw new ArgumentNullException(nameof(componentData));
#endif

            ComponentType type = componentData.GetType();
            m_Data->AppendMultipleEntitiesComponentCommandWithObject(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.AddComponentObjectForMultipleEntities, entityQuery, componentData, type);
        }

        /// <summary> Records a command to set a hybrid component value for all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Behavior at Playback: Will throw an error if one of these entities is destroyed before playback or
        /// if any entity does not have the component type at playback. Playback Entities which already have the component type will have the component set to the value.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown in playback if one or more of the entities does not have the component type or has been destroyed. (With safety checks disabled,
        /// playback will perform invalid and unsafe memory access.).</exception>
        /// <param name="entityQuery"> The query specifying which entities to set the component value for.</param>
        /// <param name="componentData"> The component object to set.</param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        /// <exception cref="ArgumentNullException">Throws if componentData is null.</exception>
        public void SetComponentObjectForEntityQuery(EntityQuery entityQuery, object componentData)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (componentData == null)
                throw new ArgumentNullException(nameof(componentData));
#endif

            ComponentType type = componentData.GetType();
            m_Data->AppendMultipleEntitiesComponentCommandWithObject(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.SetComponentObjectForMultipleEntities, entityQuery, componentData, type);
        }

        /// <summary> Records a command to set a shared component value on all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Fails if any of the entities do not have the type of shared component. [todo: should it be required that the component type is included in the query?]
        ///
        /// Behavior at Playback: Will throw an error if one of these entities is destroyed before playback. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery"> The query specifying which entities to add the component value to. </param>
        /// <param name="component"> The component value to add. </param>
        /// <typeparam name="T"> The type of shared component to set. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void SetSharedComponentForEntityQuery<T>(EntityQuery entityQuery, T component) where T : struct, ISharedComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();

            int hashCode;
            if (IsDefaultObject(ref component, out hashCode))
                m_Data->AppendMultipleEntitiesComponentCommandWithSharedValue<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.SetSharedComponentValueForMultipleEntities, entityQuery, hashCode, null);
            else
                m_Data->AppendMultipleEntitiesComponentCommandWithSharedValue<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.SetSharedComponentValueForMultipleEntities, entityQuery, hashCode, component);
        }

        /// <summary>Records a command to remove a component from all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Does not affect entities already missing the component.
        ///
        /// Behavior at Playback: Will throw an error if one of these entities is destroyed before playback. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities from which the component is removed. </param>
        /// <param name="componentType">The types of component to remove.</param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void RemoveComponentForEntityQuery(EntityQuery entityQuery, ComponentType componentType)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AppendMultipleEntitiesComponentCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.RemoveComponentForMultipleEntities, entityQuery, componentType);
        }

        /// <summary>Records a command to remove a component from all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Does not affect entities already missing the component.
        ///
        /// Behavior at Playback: Will throw an error if one of these entities is destroyed before playback. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities from which the component is removed. </param>
        /// <typeparam name="T"> The type of component to remove. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void RemoveComponentForEntityQuery<T>(EntityQuery entityQuery)
        {
            RemoveComponentForEntityQuery(entityQuery, ComponentType.ReadWrite<T>());
        }

        /// <summary>Records a command to remove multiple components from all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Some matching entities may already be missing some or all of the specified components. After this operation, all matching entities will have none of the components.
        ///
        /// Behavior at Playback: Will throw an error if one of these entities is destroyed before playback. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities from which the components are removed. </param>
        /// <param name="componentTypes">The types of components to remove.</param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void RemoveComponentForEntityQuery(EntityQuery entityQuery, ComponentTypes componentTypes)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AppendMultipleEntitiesMultipleComponentsCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.RemoveMultipleComponentsForMultipleEntities, entityQuery, componentTypes);
        }

        /// <summary>Records a command to destroy all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Behavior at Playback: Will throw an error if one of these entities is destroyed before playback. (With safety checks enabled, an exception is thrown. Without safety checks,
        /// playback will perform invalid and unsafe memory access.)
        /// </remarks>
        /// <param name="entityQuery">The query specifying the entities to destroy.</param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void DestroyEntitiesForEntityQuery(EntityQuery entityQuery)
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();
            m_Data->AppendMultipleEntitiesCommand(&m_Data->m_MainThreadChain, MainThreadSortKey,
                ECBCommand.DestroyMultipleEntities, entityQuery);
        }

        static bool IsDefaultObject<T>(ref T component, out int hashCode) where T : struct, ISharedComponentData
        {
            var defaultValue = default(T);

            hashCode = TypeManager.GetHashCode(ref component);
            return TypeManager.Equals(ref defaultValue, ref component);
        }

        /// <summary> Records a command to add a shared component value on an entity.</summary>
        /// <remarks>Behavior at Playback: Will throw an error if this entity is destroyed before playback,
        /// if this entity is still deferred, if adding this shared component exceeds the maximum number of shared components,
        /// or adding a component type makes the archetype too large.</remarks>
        /// <param name="e"> The entity to add the shared component value to. </param>
        /// <param name="component"> The shared component value to add. </param>
        /// <typeparam name="T"> The type of shared component to add. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddSharedComponent<T>(Entity e, T component) where T : struct, ISharedComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();

            int hashCode;
            if (IsDefaultObject(ref component, out hashCode))
                m_Data->AddEntitySharedComponentCommand<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddSharedComponentData, e, hashCode, null);
            else
                m_Data->AddEntitySharedComponentCommand<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddSharedComponentData, e, hashCode, component);
        }

        /// <summary> Records a command to add a shared component value on a NativeArray of entities.</summary>
        /// <remarks>Behavior at Playback: Will throw an error if any entity is destroyed before playback,
        /// if any entity is still deferred, if adding this shared component exceeds the maximum number of shared components,
        /// or adding a component type makes the archetype too large.</remarks>
        /// <param name="entities"> The NativeArray of entities to add the shared component value to. </param>
        /// <param name="component"> The shared component value to add. </param>
        /// <typeparam name="T"> The type of shared component to add. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void AddSharedComponent<T>(NativeArray<Entity> entities, T component) where T : struct, ISharedComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();

            int hashCode;
            var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
            if (IsDefaultObject(ref component, out hashCode))
                m_Data->AppendMultipleEntitiesComponentCommandWithSharedValue<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddSharedComponentWithValueForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, hashCode, null);
            else
                m_Data->AppendMultipleEntitiesComponentCommandWithSharedValue<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.AddSharedComponentWithValueForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, hashCode, component);
        }

        /// <summary> Records a command to set a shared component value on an entity.</summary>
        /// <remarks> Behavior at Playback: Will throw an error if this entity is destroyed before playback,
        /// if this entity is still deferred, or if the entity doesn't have the shared component type.</remarks>
        /// <param name="e"> The entity to set the shared component value of. </param>
        /// <param name="component"> The shared component value to set. </param>
        /// <typeparam name="T"> The type of shared component to set. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void SetSharedComponent<T>(Entity e, T component) where T : struct, ISharedComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();

            int hashCode;
            if (IsDefaultObject(ref component, out hashCode))
                m_Data->AddEntitySharedComponentCommand<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.SetSharedComponentData, e, hashCode, null);
            else
                m_Data->AddEntitySharedComponentCommand<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.SetSharedComponentData, e, hashCode, component);
        }

        /// <summary> Records a command to set a shared component value on a NativeArray of entities.</summary>
        /// <remarks> Behavior at Playback: Will throw an error if any entity is destroyed before playback,
        /// if any entity is still deferred, or if any entity doesn't have the shared component type.</remarks>
        /// <param name="entities"> The NativeArray of entities to set the shared component value of. </param>
        /// <param name="component"> The shared component value to set. </param>
        /// <typeparam name="T"> The type of shared component to set. </typeparam>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public void SetSharedComponent<T>(NativeArray<Entity> entities, T component) where T : struct, ISharedComponentData
        {
            EnforceSingleThreadOwnership();
            AssertDidNotPlayback();

            int hashCode;
            var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
            if (IsDefaultObject(ref component, out hashCode))
                m_Data->AppendMultipleEntitiesComponentCommandWithSharedValue<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.SetSharedComponentValueForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, hashCode, null);
            else
                m_Data->AppendMultipleEntitiesComponentCommandWithSharedValue<T>(&m_Data->m_MainThreadChain, MainThreadSortKey, ECBCommand.SetSharedComponentValueForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, hashCode, component);
        }

        /// <summary>
        /// Play back all recorded operations against an entity manager.
        /// </summary>
        /// <param name="mgr">The entity manager that will receive the operations</param>
        public void Playback(EntityManager mgr)
        {
            PlaybackInternal(mgr.GetCheckedEntityDataAccess());
        }

        /// <summary>
        /// Play back all recorded operations with an exclusive entity transaction.
        /// <seealso cref="EntityManager.BeginExclusiveEntityTransaction"/>.
        /// </summary>
        /// <param name="mgr">The exclusive entity transaction that will process the operations</param>
        public void Playback(ExclusiveEntityTransaction mgr)
        {
            PlaybackInternal(mgr.EntityManager.GetCheckedEntityDataAccess());
        }

        void PlaybackInternal(EntityDataAccess* mgr)
        {
            EnforceSingleThreadOwnership();

            if (!ShouldPlayback || m_Data == null)
                return;
            if (m_Data != null && m_Data->m_DidPlayback && m_Data->m_PlaybackPolicy == PlaybackPolicy.SinglePlayback)
            {
                throw new InvalidOperationException(
                    "Attempt to call Playback() on an EntityCommandBuffer that has already been played back. " +
                    "EntityCommandBuffers created with the SinglePlayback policy can only be played back once.");
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_BufferSafety);
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_ArrayInvalidationSafety);
#endif

            k_ProfileEcbPlayback.Begin();

            var walker = new EcbWalker<PlaybackProcessor>(default, ECBProcessorType.PlaybackProcessor);
            walker.processor.Init(mgr, m_Data, in OriginSystemHandle);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            walker.processor.ecbSafetyHandle = m_Safety0;
#endif
            walker.WalkChains(this);
            walker.processor.Cleanup();

            m_Data->m_DidPlayback = true;
            k_ProfileEcbPlayback.End();
        }

        // This enum is used by the ECBInterop to allow us to have generic chain walking code
        // Each IEcbProcessor should have a type here
        // ECBInterop._ProcessChainChunk(...) needs to be updated with the new option as well
        internal enum ECBProcessorType
        {
            PlaybackProcessor,
            DebugViewProcessor
        }

        internal interface IEcbProcessor
        {
            public void DestroyEntity(BasicCommand* header);
            public void RemoveComponent(BasicCommand* header);
            public void RemoveMultipleComponents(BasicCommand* header);
            public void CreateEntity(BasicCommand* header);
            public void InstantiateEntity(BasicCommand* header);
            public void AddComponent(BasicCommand* header);
            public void AddMultipleComponents(BasicCommand* header);
            public void AddComponentWithEntityFixUp(BasicCommand* header);
            public void SetComponent(BasicCommand* header);
            public void SetComponentEnabled(BasicCommand* header);
            public void SetName(BasicCommand* header);
            public void SetComponentWithEntityFixUp(BasicCommand* header);
            public void AddBuffer(BasicCommand* header);
            public void AddBufferWithEntityFixUp(BasicCommand* header);
            public void SetBuffer(BasicCommand* header);
            public void SetBufferWithEntityFixUp(BasicCommand* header);
            public void AppendToBuffer(BasicCommand* header);
            public void AppendToBufferWithEntityFixUp(BasicCommand* header);
            public void AddComponentForMultipleEntities(BasicCommand* header);
            public void RemoveComponentForMultipleEntities(BasicCommand* header);
            public void AddMultipleComponentsForMultipleEntities(BasicCommand* header);
            public void RemoveMultipleComponentsForMultipleEntities(BasicCommand* header);
            public void DestroyMultipleEntities(BasicCommand* header);
            public void AddManagedComponentData(BasicCommand* header);
            public void AddSharedComponentData(BasicCommand* header);
            public void AddComponentObjectForMultipleEntities(BasicCommand* header);
            public void SetComponentObjectForMultipleEntities(BasicCommand* header);
            public void AddSharedComponentWithValueForMultipleEntities(BasicCommand* header);
            public void SetSharedComponentValueForMultipleEntities(BasicCommand* header);
            public void SetManagedComponentData(BasicCommand* header);
            public void SetSharedComponentData(BasicCommand* header);
        }

        internal struct EcbWalker<T> where T: unmanaged, IEcbProcessor {
            public T processor;
            public ECBProcessorType processorType;
            public EcbWalker(T ecbProcessor, ECBProcessorType type)
            {
                processor = ecbProcessor;
                processorType = type;
            }
            public void WalkChains(EntityCommandBuffer ecb)
            {
                EntityCommandBufferData* data = ecb.m_Data;
                // Walk all chains (Main + Threaded) and build a NativeArray of PlaybackState objects.
                // Only chains with non-null Head pointers will be included.
                if (data->m_RecordedChainCount <= 0)
                    return;
                fixed (void* pThis = &this)
                {
                    var chainStates = stackalloc ECBChainPlaybackState[data->m_RecordedChainCount];
                    int initialChainCount = 0;
                    for (var chain = &data->m_MainThreadChain; chain != null; chain = chain->m_NextChain)
                    {
                        if (chain->m_Head != null)
                        {
#pragma warning disable 728
                            chainStates[initialChainCount++] = new ECBChainPlaybackState
                            {
                                Chunk = chain->m_Head,
                                Offset = 0,
                                NextSortKey = chain->m_Head->BaseSortKey,
                                CanBurstPlayback = chain->m_CanBurstPlayback
                            };
#pragma warning restore 728
                        }
                    }

                    if (data->m_ThreadedChains != null)
                    {
                        for (int i = 0; i < JobsUtility.MaxJobThreadCount; ++i)
                        {
                            for (var chain = &data->m_ThreadedChains[i]; chain != null; chain = chain->m_NextChain)
                            {
                                if (chain->m_Head != null)
                                {
#pragma warning disable 728
                                    chainStates[initialChainCount++] = new ECBChainPlaybackState
                                    {
                                        Chunk = chain->m_Head,
                                        Offset = 0,
                                        NextSortKey = chain->m_Head->BaseSortKey,
                                        CanBurstPlayback = chain->m_CanBurstPlayback
                                    };
#pragma warning restore 728
                                }
                            }
                        }
                    }
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                    if (data->m_RecordedChainCount != initialChainCount)
                        Assert.IsTrue(false,
                            "RecordedChainCount (" + data->m_RecordedChainCount + ") != initialChainCount (" +
                            initialChainCount + ")");
#endif

                    using (ECBChainPriorityQueue chainQueue = new ECBChainPriorityQueue(chainStates,
                        data->m_RecordedChainCount, Allocator.Temp))
                    {
                        ECBChainHeapElement currentElem = chainQueue.Pop();

                        while (currentElem.ChainIndex != -1)
                        {
                            ECBChainHeapElement nextElem = chainQueue.Peek();

                            var chunk = chainStates[currentElem.ChainIndex].Chunk;
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                            if (chunk == null)
                                Assert.IsTrue(false, $"chainStates[{currentElem.ChainIndex}].Chunk is null.");
#endif
                            var off = chainStates[currentElem.ChainIndex].Offset;
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                            if (off < 0 || off >= chunk->Used)
                                Assert.IsTrue(false, $"chainStates[{currentElem.ChainIndex}].Offset is invalid: {off}. Should be between 0 and {chunk->Used}");
#endif

                            if (chainStates[currentElem.ChainIndex].CanBurstPlayback)
                            {
                                // Bursting PlaybackChain
                                ECBInterop.ProcessChainChunk(pThis, (int)processorType, chainStates,
                                    currentElem.ChainIndex, nextElem.ChainIndex);
                            }
                            else
                            {
                                // Non-Bursted PlaybackChain
                                ECBInterop._ProcessChainChunk(pThis, (int)processorType, chainStates,
                                    currentElem.ChainIndex, nextElem.ChainIndex);
                            }

                            if (chainStates[currentElem.ChainIndex].Chunk == null)
                            {
                                chainQueue.Pop(); // ignore return value; we already have it as nextElem
                            }
                            else
                            {
                                currentElem.SortKey = chainStates[currentElem.ChainIndex].NextSortKey;
                                chainQueue.ReplaceTop(currentElem);
                            }

                            currentElem = nextElem;
                        }
                    }
                }
            }

            internal void ProcessChain(ECBChainPlaybackState* chainStates, int currentChain,
                int nextChain)
            {
                int nextChainSortKey = (nextChain != -1) ? chainStates[nextChain].NextSortKey : -1;
                var chunk = chainStates[currentChain].Chunk;
                var off = chainStates[currentChain].Offset;

                while (chunk != null)
                {
                    var buf = (byte*) chunk + sizeof(ECBChunk);
                    while (off < chunk->Used)
                    {
                        var header = (BasicCommand*) (buf + off);
                        if (nextChain != -1 && header->SortKey > nextChainSortKey)
                        {
                            // early out because a different chain needs to playback
                            var state = chainStates[currentChain];
                            state.Chunk = chunk;
                            state.Offset = off;
                            state.NextSortKey = header->SortKey;
                            chainStates[currentChain] = state;
                            return;
                        }

                        var foundCommand = ProcessUnmanagedCommand(header);

                        // foundCommand will be false if either:
                        // 1) We are inside of Burst and therefore need to call the non-Burst function pointer
                        // 2) It's a managed command and we are not inside of Burst
                        if (!foundCommand)
                        {
                            var didExecuteManaged = false;
                            ProcessManagedCommand(header, ref didExecuteManaged);
                            ThrowIfPlaybackManagedCommandWasUsedFromBurstedCommandBuffer(didExecuteManaged);
                        }

                        off += header->TotalSize;
                    }

                    // Reached the end of a chunk; advance to the next one
                    chunk = chunk->Next;
                    off = 0;
                }

                // Reached the end of the chain; update its playback state to make sure it's ignored
                // for the remainder of playback.
                {
                    var state = chainStates[currentChain];
                    state.Chunk = null;
                    state.Offset = 0;
                    state.NextSortKey = Int32.MinValue;
                    chainStates[currentChain] = state;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool ProcessUnmanagedCommand(BasicCommand* header)
            {
                switch ((ECBCommand)header->CommandType)
                {
                    case ECBCommand.DestroyEntity:
                        processor.DestroyEntity(header);
                        return true;

                    case ECBCommand.RemoveComponent:
                        processor.RemoveComponent(header);
                        return true;

                    case ECBCommand.RemoveMultipleComponents:
                        processor.RemoveMultipleComponents(header);
                        return true;

                    case ECBCommand.CreateEntity:
                        processor.CreateEntity(header);
                        return true;

                    case ECBCommand.InstantiateEntity:
                        processor.InstantiateEntity(header);
                        return true;

                    case ECBCommand.AddComponent:
                        processor.AddComponent(header);
                        return true;

                    case ECBCommand.AddMultipleComponents:
                        processor.AddMultipleComponents(header);
                        return true;

                    case ECBCommand.AddComponentWithEntityFixUp:
                        processor.AddComponentWithEntityFixUp(header);
                        return true;

                    case ECBCommand.SetComponent:
                        processor.SetComponent(header);
                        return true;

                    case ECBCommand.SetComponentEnabled:
                        processor.SetComponentEnabled(header);
                        return true;

                    case ECBCommand.SetName:
                        processor.SetName(header);
                        return true;

                    case ECBCommand.SetComponentWithEntityFixUp:
                        processor.SetComponentWithEntityFixUp(header);
                        return true;

                    case ECBCommand.AddBuffer:
                        processor.AddBuffer(header);
                        return true;

                    case ECBCommand.AddBufferWithEntityFixUp:
                        processor.AddBufferWithEntityFixUp(header);
                        return true;

                    case ECBCommand.SetBuffer:
                        processor.SetBuffer(header);
                        return true;

                    case ECBCommand.SetBufferWithEntityFixUp:
                        processor.SetBufferWithEntityFixUp(header);
                        return true;

                    case ECBCommand.AppendToBuffer:
                        processor.AppendToBuffer(header);
                        return true;

                    case ECBCommand.AppendToBufferWithEntityFixUp:
                        processor.AppendToBufferWithEntityFixUp(header);
                        return true;

                    case ECBCommand.AddComponentForMultipleEntities:
                        processor.AddComponentForMultipleEntities(header);
                        return true;

                    case ECBCommand.RemoveComponentForMultipleEntities:
                        processor.RemoveComponentForMultipleEntities(header);
                        return true;

                    case ECBCommand.AddMultipleComponentsForMultipleEntities:
                        processor.AddMultipleComponentsForMultipleEntities(header);
                        return true;

                    case ECBCommand.RemoveMultipleComponentsForMultipleEntities:
                        processor.RemoveMultipleComponentsForMultipleEntities(header);
                        return true;

                    case ECBCommand.DestroyMultipleEntities:
                        processor.DestroyMultipleEntities(header);
                        return true;
                }

                return false;
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ProcessManagedCommand(BasicCommand* header, ref bool didExecuteMethod)
            {
                didExecuteMethod = true;
                switch ((ECBCommand)header->CommandType)
                {
                    case ECBCommand.AddManagedComponentData:
                        processor.AddManagedComponentData(header);
                        break;

                    case ECBCommand.AddSharedComponentData:
                        processor.AddSharedComponentData(header);
                        break;

                    case ECBCommand.AddComponentObjectForMultipleEntities:
                        processor.AddComponentObjectForMultipleEntities(header);
                        break;

                    case ECBCommand.SetComponentObjectForMultipleEntities:
                        processor.SetComponentObjectForMultipleEntities(header);
                        break;

                    case ECBCommand.AddSharedComponentWithValueForMultipleEntities:
                        processor.AddSharedComponentWithValueForMultipleEntities(header);
                        break;

                    case ECBCommand.SetSharedComponentValueForMultipleEntities:
                        processor.SetSharedComponentValueForMultipleEntities(header);
                        break;

                    case ECBCommand.SetManagedComponentData:
                        processor.SetManagedComponentData(header);
                        break;

                    case ECBCommand.SetSharedComponentData:
                        processor.SetSharedComponentData(header);
                        break;

    #if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                    default:
                    {
                        throw new InvalidOperationException("Invalid command not recognized for EntityCommandBuffer.");
                    }
    #endif
                }
            }
        }

        internal struct PlaybackProcessor : IEcbProcessor
        {
            public EntityDataAccess* mgr;
            public EntityComponentStore.ArchetypeChanges archetypeChanges;
            public ECBSharedPlaybackState playbackState;
            public PlaybackPolicy playbackPolicy;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            public AtomicSafetyHandle ecbSafetyHandle; // used for temporary NativeArray views created & destroyed during playback
#endif
            public bool isFirstPlayback;
            public int entityCount;
            public int bufferCount;
            public SystemHandleUntyped originSystem;
            private bool trackStructuralChanges;

            public void Init(EntityDataAccess* entityDataAccess, EntityCommandBufferData* data, in SystemHandleUntyped originSystemHandle)
            {
                mgr = entityDataAccess;
                playbackPolicy = data->m_PlaybackPolicy;
                isFirstPlayback = !data->m_DidPlayback;
                originSystem = originSystemHandle;

                // Don't begin/end structural changes unless at least one command was recorded.
                // This prevents empty command buffers from needlessly causing structural changes, which some
                // existing code relies on.
                if (data->m_RecordedChainCount > 0)
                {
                    archetypeChanges = mgr->BeginStructuralChanges();
                    trackStructuralChanges = true;
                }

                // Play back the recorded commands in increasing sortKey order
                entityCount = -data->m_Entity.Index;
                bufferCount = *data->m_BufferWithFixups.Counter;

                Entity* createEntitiesBatch = null;
                ECBSharedPlaybackState.BufferWithFixUp* buffersWithFixup = null;

                if (entityCount > 0)
                    createEntitiesBatch = (Entity*) Memory.Unmanaged.Allocate(entityCount * sizeof(Entity),
                            4, Allocator.Temp);
                if (bufferCount > 0)
                    buffersWithFixup = (ECBSharedPlaybackState.BufferWithFixUp*)
                        Memory.Unmanaged.Allocate(bufferCount * sizeof(ECBSharedPlaybackState.BufferWithFixUp),
                            4, Allocator.Temp);


                playbackState = new ECBSharedPlaybackState
                {
                    CommandBufferID = data->m_CommandBufferID,
                    CreateEntityBatch = createEntitiesBatch,
                    BuffersWithFixUp = buffersWithFixup,
                    CreatedEntityCount = entityCount,
                    LastBuffer = 0,
                };
            }

            public void Cleanup()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                if (bufferCount != playbackState.LastBuffer)
                    Assert.IsTrue(false, "bufferCount (" + bufferCount + ") != playbackState.LastBuffer (" + playbackState.LastBuffer + ")");
#endif
                for (int i = 0; i < playbackState.LastBuffer; i++)
                {
                    ECBSharedPlaybackState.BufferWithFixUp* fixup = playbackState.BuffersWithFixUp + i;
                    EntityBufferCommand* cmd = fixup->cmd;
                    var entity = SelectEntity(cmd->Header.Entity, playbackState);
                    if (mgr->Exists(entity) && mgr->HasComponent(entity, TypeManager.GetType(cmd->ComponentTypeIndex)))
                        FixupBufferContents(mgr, cmd, entity, playbackState);
                }

                Memory.Unmanaged.Free(playbackState.CreateEntityBatch, Allocator.Temp);
                Memory.Unmanaged.Free(playbackState.BuffersWithFixUp, Allocator.Temp);

                if (trackStructuralChanges)
                {
                    mgr->EndStructuralChanges(ref archetypeChanges);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void DestroyEntity(BasicCommand* header)
            {
                var cmd = (EntityCommand*)header;
                Entity entity = SelectEntity(cmd->Entity, playbackState);
                mgr->DestroyEntityInternalDuringStructuralChange(&entity, 1, in originSystem);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveComponent(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                mgr->RemoveComponentDuringStructuralChange(entity, ComponentType.FromTypeIndex(cmd->ComponentTypeIndex), in originSystem);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveMultipleComponents(BasicCommand* header)
            {
                var cmd = (EntityMultipleComponentsCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                var componentTypes = cmd->Types;

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
                EntitiesJournaling.RecordRemoveComponent(in mgr->m_WorldUnmanaged, in originSystem, &entity, 1, componentTypes.Types, componentTypes.Length);
#endif

#if ENABLE_PROFILER
                using (var scope = StructuralChangesProfiler.BeginRemoveComponent(mgr->m_WorldUnmanaged))
#endif
                {
                    // TODO(DOTS-4174): Go through EntityDataAccess, Add EntitiesJournaling call
                    mgr->EntityComponentStore->RemoveMultipleComponentsWithValidation(entity, componentTypes);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void CreateEntity(BasicCommand* header)
            {
                var cmd = (CreateCommand*)header;
                EntityArchetype at = cmd->Archetype;

                if (!at.Valid)
                {
                    ComponentTypeInArchetype* typesInArchetype = stackalloc ComponentTypeInArchetype[1];

                    var cachedComponentCount = EntityDataAccess.FillSortedArchetypeArray(typesInArchetype, null, 0);

                    // Lookup existing archetype (cheap)
                    EntityArchetype entityArchetype = mgr->CreateArchetypeDuringStructuralChange(typesInArchetype, cachedComponentCount);;
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                    entityArchetype._DebugComponentStore = mgr->EntityComponentStore;
#endif

                    at = entityArchetype;
                }

                int index = -cmd->IdentityIndex - 1;

                mgr->CreateEntityDuringStructuralChange(at, playbackState.CreateEntityBatch + index, cmd->BatchCount, in originSystem);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void InstantiateEntity(BasicCommand* header)
            {
                var cmd = (EntityCommand*)header;

                var index = -cmd->IdentityIndex - 1;
                Entity srcEntity = SelectEntity(cmd->Entity, playbackState);
                mgr->InstantiateInternalDuringStructuralChange(srcEntity, playbackState.CreateEntityBatch + index,
                    cmd->BatchCount, in originSystem);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponent(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;
                var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                mgr->AddComponentDuringStructuralChange(entity, componentType, in originSystem);
                if (cmd->ComponentSize != 0)
                    mgr->SetComponentDataRaw(entity, cmd->ComponentTypeIndex, cmd + 1, cmd->ComponentSize, in originSystem);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddMultipleComponents(BasicCommand* header)
            {
                var cmd = (EntityMultipleComponentsCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                var componentTypes = cmd->Types;

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
                EntitiesJournaling.RecordAddComponent(in mgr->m_WorldUnmanaged, in originSystem, &entity, 1, componentTypes.Types, componentTypes.Length);
#endif

#if ENABLE_PROFILER
                using (var scope = StructuralChangesProfiler.BeginAddComponent(mgr->m_WorldUnmanaged))
#endif
                {
                    // TODO(DOTS-4174): Go through EntityDataAccess, Add EntitiesJournaling call
                    mgr->EntityComponentStore->AddMultipleComponentsWithValidation(entity, componentTypes);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponentWithEntityFixUp(BasicCommand* header)
            {
                AssertSinglePlayback((ECBCommand)header->CommandType, isFirstPlayback);

                var cmd = (EntityComponentCommand*)header;
                var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                mgr->AddComponentDuringStructuralChange(entity, componentType, in originSystem);
                SetCommandDataWithFixup(mgr->EntityComponentStore, cmd, entity, playbackState);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetComponent(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                mgr->SetComponentDataRaw(entity, cmd->ComponentTypeIndex, cmd + 1, cmd->ComponentSize, in originSystem);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetComponentEnabled(BasicCommand* header)
            {
                var cmd = (EntityComponentEnabledCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                mgr->SetComponentEnabled(entity, cmd->ComponentTypeIndex, cmd->IsEnabled != 0);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetName(BasicCommand* header)
            {
                var cmd = (EntityNameCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                mgr->SetName(entity, cmd->Name);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetComponentWithEntityFixUp(BasicCommand* header)
            {
                AssertSinglePlayback((ECBCommand)header->CommandType, isFirstPlayback);

                var cmd = (EntityComponentCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                SetCommandDataWithFixup(mgr->EntityComponentStore, cmd, entity, playbackState);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddBuffer(BasicCommand* header)
            {
                var cmd = (EntityBufferCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                mgr->AddComponentDuringStructuralChange(entity, ComponentType.FromTypeIndex(cmd->ComponentTypeIndex), in originSystem);

                if (playbackPolicy == PlaybackPolicy.SinglePlayback)
                    mgr->SetBufferRaw(entity, cmd->ComponentTypeIndex,
                        &cmd->BufferNode.TempBuffer,
                        cmd->ComponentSize, in originSystem);
                else
                {
                    // copy the buffer to ensure that no two entities point to the same buffer from the ECB
                    // either in the same world or in different worlds
                    var buffer = CloneBuffer(&cmd->BufferNode.TempBuffer, cmd->ComponentTypeIndex);
                    mgr->SetBufferRaw(entity, cmd->ComponentTypeIndex, &buffer,
                        cmd->ComponentSize, in originSystem);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddBufferWithEntityFixUp(BasicCommand* header)
            {
                AssertSinglePlayback((ECBCommand)header->CommandType, isFirstPlayback);

                var cmd = (EntityBufferCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                mgr->AddComponentDuringStructuralChange(entity, ComponentType.FromTypeIndex(cmd->ComponentTypeIndex), in originSystem);
                mgr->SetBufferRaw(entity, cmd->ComponentTypeIndex, &cmd->BufferNode.TempBuffer, cmd->ComponentSize, in originSystem);
                AddToPostPlaybackFixup(cmd, ref playbackState);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetBuffer(BasicCommand* header)
            {
                var cmd = (EntityBufferCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                if (playbackPolicy == PlaybackPolicy.SinglePlayback)
                    mgr->SetBufferRaw(entity, cmd->ComponentTypeIndex, &cmd->BufferNode.TempBuffer,
                        cmd->ComponentSize, in originSystem);
                else
                {
                    // copy the buffer to ensure that no two entities point to the same buffer from the ECB
                    // either in the same world or in different worlds
                    var buffer = CloneBuffer(&cmd->BufferNode.TempBuffer, cmd->ComponentTypeIndex);
                    mgr->SetBufferRaw(entity, cmd->ComponentTypeIndex, &buffer, cmd->ComponentSize, in originSystem);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetBufferWithEntityFixUp(BasicCommand* header)
            {
                AssertSinglePlayback((ECBCommand)header->CommandType, isFirstPlayback);

                var cmd = (EntityBufferCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                mgr->SetBufferRaw(entity, cmd->ComponentTypeIndex, &cmd->BufferNode.TempBuffer, cmd->ComponentSize, in originSystem);
                AddToPostPlaybackFixup(cmd, ref playbackState);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AppendToBuffer(BasicCommand* header)
            {
                var cmd = (EntityComponentCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);

                CheckBufferExistsOnEntity(mgr->EntityComponentStore, entity, cmd);

                // TODO(DOTS-4174): Go through EntityDataAccess
                BufferHeader* bufferHeader = (BufferHeader*)mgr->EntityComponentStore->GetComponentDataWithTypeRW(entity, cmd->ComponentTypeIndex, mgr->EntityComponentStore->GlobalSystemVersion);

                ref readonly var typeInfo = ref TypeManager.GetTypeInfo(cmd->ComponentTypeIndex);
                var alignment = typeInfo.AlignmentInBytes;
                var elementSize = typeInfo.ElementSize;

                BufferHeader.EnsureCapacity(bufferHeader, bufferHeader->Length + 1, elementSize, alignment, BufferHeader.TrashMode.RetainOldData, false, 0);

                var offset = bufferHeader->Length * elementSize;
                UnsafeUtility.MemCpy(BufferHeader.GetElementPointer(bufferHeader) + offset, cmd + 1, (long)elementSize);
                bufferHeader->Length += 1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AppendToBufferWithEntityFixUp(BasicCommand* header)
            {
                AssertSinglePlayback((ECBCommand)header->CommandType, isFirstPlayback);

                var cmd = (EntityComponentCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);

                CheckBufferExistsOnEntity(mgr->EntityComponentStore, entity, cmd);

                // TODO(DOTS-4174): Go through EntityDataAccess
                BufferHeader* bufferHeader = (BufferHeader*)mgr->EntityComponentStore->GetComponentDataWithTypeRW(entity, cmd->ComponentTypeIndex, mgr->EntityComponentStore->GlobalSystemVersion);

                ref readonly var typeInfo = ref TypeManager.GetTypeInfo(cmd->ComponentTypeIndex);
                var alignment = typeInfo.AlignmentInBytes;
                var elementSize = typeInfo.ElementSize;

                BufferHeader.EnsureCapacity(bufferHeader, bufferHeader->Length + 1, elementSize, alignment, BufferHeader.TrashMode.RetainOldData, false, 0);

                var offset = bufferHeader->Length * elementSize;
                UnsafeUtility.MemCpy(BufferHeader.GetElementPointer(bufferHeader) + offset, cmd + 1, (long)elementSize);
                bufferHeader->Length += 1;
                FixupComponentData(BufferHeader.GetElementPointer(bufferHeader) + offset, typeInfo.TypeIndex, playbackState);
            }

            // Creates a temporary NativeArray<Entity> view data from an Entity* pointer+count. This array does not need to be Disposed();
            // it does not own any of its data. The array will share a safety handle with the ECB, and its lifetime must not exceed that
            // of the ECB itself.
            private void CreateTemporaryNativeArrayView(Entity* entities, int entityCount,
                out NativeArray<Entity> outArray)
            {
                outArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(entities, entityCount,
                    Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // The temporary array still needs an atomic handle, but the array itself will not have Dispose() called
                // on it (it doesn't own its data). And even if it did, NativeArray.Dispose() skips disposing the safety
                // handle if the array uses Allocator.None. The solution is to pass the primary ECB safety handle into
                // the playback processor, and use it for temporary arrays created during playback.
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref outArray, ecbSafetyHandle);
#endif
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponentForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommand*)header;
                var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                CreateTemporaryNativeArrayView(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, out var entities);

                if (cmd->Header.SkipDeferredEntityLookup == 0)
                {
                    for (int len = entities.Length, i = 0; i < len; ++i)
                    {
                        if (entities[i].Index < 0)
                            entities[i] = SelectEntity(entities[i], playbackState);
                    }
                }
                mgr->AddComponentDuringStructuralChange(entities, componentType, in originSystem);

                if (cmd->ComponentSize > 0)
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        mgr->SetComponentDataRaw(entities[i], cmd->ComponentTypeIndex, cmd + 1, cmd->ComponentSize, in originSystem);
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveComponentForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommand*)header;
                var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                CreateTemporaryNativeArrayView(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, out var entities);

                if (cmd->Header.SkipDeferredEntityLookup == 0)
                {
                    for (int len = entities.Length, i = 0; i < len; ++i)
                    {
                        if (entities[i].Index < 0)
                            entities[i] = SelectEntity(entities[i], playbackState);
                    }
                }
                mgr->RemoveComponentDuringStructuralChange(entities, componentType, in originSystem);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddMultipleComponentsForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesAndComponentsCommand*)header;
                CreateTemporaryNativeArrayView(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, out var entities);

                if (cmd->Header.SkipDeferredEntityLookup == 0)
                {
                    for (int len = entities.Length, i = 0; i < len; ++i)
                    {
                        if (entities[i].Index < 0)
                            entities[i] = SelectEntity(entities[i], playbackState);
                    }
                }
                mgr->AddMultipleComponentsDuringStructuralChange(entities, cmd->Types, in originSystem);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RemoveMultipleComponentsForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesAndComponentsCommand*)header;
                CreateTemporaryNativeArrayView(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, out var entities);

                if (cmd->Header.SkipDeferredEntityLookup == 0)
                {
                    for (int len = entities.Length, i = 0; i < len; ++i)
                    {
                        if (entities[i].Index < 0)
                            entities[i] = SelectEntity(entities[i], playbackState);
                    }
                }
                mgr->RemoveMultipleComponentsDuringStructuralChange(entities, cmd->Types, in originSystem);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void DestroyMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesCommand*)header;
                CreateTemporaryNativeArrayView(cmd->Entities.Ptr, cmd->EntitiesCount, out var entities);

                if (cmd->SkipDeferredEntityLookup == 0)
                {
                    for (int len = entities.Length, i = 0; i < len; ++i)
                    {
                        if (entities[i].Index < 0)
                            entities[i] = SelectEntity(entities[i], playbackState);
                    }
                }
                mgr->DestroyEntityDuringStructuralChange(entities, in originSystem);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddManagedComponentData(BasicCommand* header)
            {
                var cmd = (EntityManagedComponentCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);

                var addedManaged = mgr->AddComponentDuringStructuralChange(entity, ComponentType.FromTypeIndex(cmd->ComponentTypeIndex), in originSystem);
                if (addedManaged)
                {
                    CommitStructuralChanges(mgr, ref archetypeChanges);
                }

                var box = cmd->GetBoxedObject();
#if !NET_DOTS
                if (box != null && TypeManager.HasEntityReferences(cmd->ComponentTypeIndex))
                    FixupManagedComponent.FixUpComponent(box, playbackState);
#endif

                mgr->SetComponentObject(entity, ComponentType.FromTypeIndex(cmd->ComponentTypeIndex), box, in originSystem);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddSharedComponentData(BasicCommand* header)
            {
                var cmd = (EntitySharedComponentCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                var addedShared = mgr->AddSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(entity, cmd->ComponentTypeIndex, cmd->HashCode,
                    cmd->GetBoxedObject(), in originSystem);
                if (addedShared)
                {
                    CommitStructuralChanges(mgr, ref archetypeChanges);
                }
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddComponentObjectForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommandWithObject*)header;
                var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                CreateTemporaryNativeArrayView(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, out var entities);
                if (cmd->Header.SkipDeferredEntityLookup == 0)
                {
                    for (int len = entities.Length, i = 0; i < len; ++i)
                    {
                        if (entities[i].Index < 0)
                            entities[i] = SelectEntity(entities[i], playbackState);
                    }
                }

                mgr->AddComponentDuringStructuralChange(entities, componentType, in originSystem);

                CommitStructuralChanges(mgr, ref archetypeChanges);

                var box = cmd->GetBoxedObject();
                var typeIndex = cmd->ComponentTypeIndex;
#if !NET_DOTS
                if (box != null && TypeManager.HasEntityReferences(typeIndex))
                    FixupManagedComponent.FixUpComponent(box, playbackState);
#endif

                for (int len = entities.Length, i = 0; i < len; i++)
                {
                    mgr->SetComponentObject(entities[i], componentType, box, in originSystem);
                }
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetComponentObjectForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommandWithObject*)header;
                var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                CreateTemporaryNativeArrayView(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, out var entities);
                if (cmd->Header.SkipDeferredEntityLookup == 0)
                {
                    for (int len = entities.Length, i = 0; i < len; ++i)
                    {
                        if (entities[i].Index < 0)
                            entities[i] = SelectEntity(entities[i], playbackState);
                    }
                }

                if (!mgr->EntityComponentStore->ManagedChangesTracker.Empty)
                {
                    CommitStructuralChanges(mgr, ref archetypeChanges);
                }

                var box = cmd->GetBoxedObject();
                var typeIndex = cmd->ComponentTypeIndex;
#if !NET_DOTS
                if (box != null && TypeManager.HasEntityReferences(typeIndex))
                    FixupManagedComponent.FixUpComponent(box, playbackState);
#endif

                for (int len = entities.Length, i = 0; i < len; i++)
                {
                    mgr->SetComponentObject(entities[i], componentType, box, in originSystem);
                }
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddSharedComponentWithValueForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommandWithObject*)header;
                var componentType = ComponentType.FromTypeIndex(cmd->ComponentTypeIndex);
                CreateTemporaryNativeArrayView(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, out var entities);
                if (cmd->Header.SkipDeferredEntityLookup == 0)
                {
                    for (int len = entities.Length, i = 0; i < len; ++i)
                    {
                        if (entities[i].Index < 0)
                            entities[i] = SelectEntity(entities[i], playbackState);
                    }
                }

                var boxedObject = cmd->GetBoxedObject();
                var hashcode = cmd->HashCode;
                var typeIndex = cmd->ComponentTypeIndex;

                // TODO: we aren't yet doing fix-up for Entity fields (see DOTS-3465)
                mgr->AddSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(entities, typeIndex,
                    hashcode, boxedObject, in originSystem);

                CommitStructuralChanges(mgr, ref archetypeChanges);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetSharedComponentValueForMultipleEntities(BasicCommand* header)
            {
                var cmd = (MultipleEntitiesComponentCommandWithObject*)header;
                var ecs = mgr->EntityComponentStore;
                CreateTemporaryNativeArrayView(cmd->Header.Entities.Ptr, cmd->Header.EntitiesCount, out var entities);
                if (cmd->Header.SkipDeferredEntityLookup == 0)
                {
                    for (int len = entities.Length, i = 0; i < len; ++i)
                    {
                        if (entities[i].Index < 0)
                            entities[i] = SelectEntity(entities[i], playbackState);
                    }
                }

                var boxedObject = cmd->GetBoxedObject();
                var hashcode = cmd->HashCode;
                var typeIndex = cmd->ComponentTypeIndex;

                for (int len = entities.Length, i = 0; i < len; i++)
                {
                    // TODO: we aren't yet doing fix-up for Entity fields (see DOTS-3465)
                    var e = entities[i];
                    mgr->SetSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(e, typeIndex,
                        hashcode, boxedObject, in originSystem);
                }

                CommitStructuralChanges(mgr, ref archetypeChanges);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetManagedComponentData(BasicCommand* header)
            {
                var cmd = (EntityManagedComponentCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                if (!mgr->EntityComponentStore->ManagedChangesTracker.Empty)
                {
                    CommitStructuralChanges(mgr, ref archetypeChanges);
                }

                var box = cmd->GetBoxedObject();
#if !NET_DOTS
                if (box != null && TypeManager.HasEntityReferences(cmd->ComponentTypeIndex))
                    FixupManagedComponent.FixUpComponent(box, playbackState);
#endif

                mgr->SetComponentObject(entity, ComponentType.FromTypeIndex(cmd->ComponentTypeIndex), cmd->GetBoxedObject(), in originSystem);
            }

            [BurstDiscard]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetSharedComponentData(BasicCommand* header)
            {
                var cmd = (EntitySharedComponentCommand*)header;
                var entity = SelectEntity(cmd->Header.Entity, playbackState);
                mgr->SetSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(entity, cmd->ComponentTypeIndex, cmd->HashCode,
                    cmd->GetBoxedObject(), in originSystem);
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private static void CheckEntityNotNull(Entity entity)
        {
            if (entity == Entity.Null)
                throw new InvalidOperationException("Invalid Entity.Null passed.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private static void CheckCommandEntity(in ECBSharedPlaybackState playbackState, Entity deferredEntity)
        {
            if (playbackState.CreateEntityBatch == null)
                throw new InvalidOperationException(
                    "playbackState.CreateEntityBatch passed to SelectEntity is null (likely due to an ECB command recording an invalid temporary Entity).");
            if (deferredEntity.Version != playbackState.CommandBufferID)
                throw new InvalidOperationException(
                    $"Deferred Entity {deferredEntity} was created by a different command buffer. Deferred Entities can only be used by the command buffer that created them.");
            int index = -deferredEntity.Index - 1;
            if (index < 0 || index >= playbackState.CreatedEntityCount)
                throw new InvalidOperationException(
                    $"Deferred Entity {deferredEntity} is out of range. Was it created by a different EntityCommandBuffer?");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private static void CheckEntityVersionValid(Entity entity)
        {
            if (entity.Version <= 0)
                throw new InvalidOperationException("Invalid Entity version");
        }

        private static unsafe Entity SelectEntity(Entity cmdEntity, in ECBSharedPlaybackState playbackState)
        {
            CheckEntityNotNull(cmdEntity);
            if (cmdEntity.Index < 0)
            {
                CheckCommandEntity(playbackState, cmdEntity);
                int index = -cmdEntity.Index - 1;
                Entity e = *(playbackState.CreateEntityBatch + index);
                CheckEntityVersionValid(e);
                return e;
            }
            return cmdEntity;
        }

        private static void CommitStructuralChanges(EntityDataAccess* mgr,
            ref EntityComponentStore.ArchetypeChanges archetypeChanges)
        {
            mgr->EndStructuralChanges(ref archetypeChanges);
            archetypeChanges = mgr->BeginStructuralChanges();
        }

        private static void FixupComponentData(byte* data, int typeIndex, ECBSharedPlaybackState playbackState)
        {
            FixupComponentData(data, 1, typeIndex, playbackState);
        }

        private static void FixupComponentData(byte* data, int count, int typeIndex, ECBSharedPlaybackState playbackState)
        {
            ref readonly var componentTypeInfo = ref TypeManager.GetTypeInfo(typeIndex);

            var offsets = TypeManager.GetEntityOffsets(componentTypeInfo);
            var offsetCount = componentTypeInfo.EntityOffsetCount;
            for (var componentCount = 0; componentCount < count; componentCount++, data += componentTypeInfo.ElementSize)
            {
                for (int i = 0; i < offsetCount; i++)
                {
                    // Need fix ups
                    Entity* e = (Entity*)(data + offsets[i].Offset);
                    if (e->Index < 0)
                    {
                        var index = -e->Index - 1;
                        Entity real = *(playbackState.CreateEntityBatch + index);
                        *e = real;
                    }
                }
            }
        }

#if !NET_DOTS
        class FixupManagedComponent : Unity.Properties.PropertyVisitor, Properties.Adapters.IVisit<Entity>
        {
            [ThreadStatic]
            public static FixupManagedComponent _CachedVisitor;

            ECBSharedPlaybackState PlaybackState;
            public FixupManagedComponent()
            {
                AddAdapter(this);
            }

            public static void FixUpComponent(object obj, in ECBSharedPlaybackState state)
            {
                var visitor = FixupManagedComponent._CachedVisitor;
                if (FixupManagedComponent._CachedVisitor == null)
                    FixupManagedComponent._CachedVisitor = visitor = new FixupManagedComponent();

                visitor.PlaybackState = state;
                Unity.Properties.PropertyContainer.Visit(ref obj, visitor);
            }

            Unity.Properties.VisitStatus Properties.Adapters.IVisit<Entity>.Visit<TContainer>(Unity.Properties.Property<TContainer, Entity> property, ref TContainer container, ref Entity value)
            {
                if (value.Index < 0)
                {
                    var index = -value.Index - 1;
                    Entity real = *(PlaybackState.CreateEntityBatch + index);
                    value = real;
                }

                return Unity.Properties.VisitStatus.Stop;
            }
        }
#endif

        static void SetCommandDataWithFixup(
            EntityComponentStore* mgr, EntityComponentCommand* cmd, Entity entity,
            ECBSharedPlaybackState playbackState)
        {
            byte* data = (byte*)mgr->GetComponentDataRawRW(entity, cmd->ComponentTypeIndex);
            UnsafeUtility.MemCpy(data, cmd + 1, cmd->ComponentSize);
            FixupComponentData(data, cmd->ComponentTypeIndex,
                playbackState);
        }

        private static unsafe void AddToPostPlaybackFixup(EntityBufferCommand* cmd, ref ECBSharedPlaybackState playbackState)
        {
            var entity = SelectEntity(cmd->Header.Entity, playbackState);
            ECBSharedPlaybackState.BufferWithFixUp* toFixup =
                playbackState.BuffersWithFixUp + playbackState.LastBuffer++;
            toFixup->cmd = cmd;
        }

        static void FixupBufferContents(
            EntityDataAccess* mgr, EntityBufferCommand* cmd, Entity entity,
            ECBSharedPlaybackState playbackState)
        {
            BufferHeader* bufferHeader = (BufferHeader*)mgr->EntityComponentStore->GetComponentDataWithTypeRW(entity, cmd->ComponentTypeIndex, mgr->EntityComponentStore->GlobalSystemVersion);
            FixupComponentData(BufferHeader.GetElementPointer(bufferHeader), bufferHeader->Length,
                cmd->ComponentTypeIndex, playbackState);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private static void ThrowIfPlaybackManagedCommandWasUsedFromBurstedCommandBuffer(bool didExecuteManagedPlayack)
        {
            if (!didExecuteManagedPlayack)
                throw new InvalidOperationException("PlaybackManagedCommand can't be used from a bursted command buffer playback");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private static void CheckBufferExistsOnEntity(EntityComponentStore* mgr, Entity entity, EntityComponentCommand* cmd)
        {
            if (!mgr->HasComponent(entity, cmd->ComponentTypeIndex))
                throw new InvalidOperationException("Buffer does not exist on entity, cannot append element.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        static void AssertSinglePlayback(ECBCommand commandType, bool isFirstPlayback)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (isFirstPlayback)
                return;

            throw new InvalidOperationException("EntityCommandBuffer commands which set components with entity references cannot be played more than once.");
#endif
        }

        static BufferHeader CloneBuffer(BufferHeader* srcBuffer, int componentTypeIndex)
        {
            BufferHeader clone = new BufferHeader();
            BufferHeader.Initialize(&clone, 0);

            var alignment = 8; // TODO: Need a way to compute proper alignment for arbitrary non-generic types in TypeManager
            ref readonly var elementSize = ref TypeManager.GetTypeInfo(componentTypeIndex).ElementSize;
            BufferHeader.Assign(&clone, BufferHeader.GetElementPointer(srcBuffer), srcBuffer->Length, elementSize, alignment, false, 0);
            return clone;
        }

        /// <summary>An extension of EntityCommandBuffer that allows concurrent (deterministic) command buffer recording.</summary>
        /// <returns>The <see cref="ParallelWriter"/> that can be used to record commands in parallel.</returns>
        public ParallelWriter AsParallelWriter()
        {
            ParallelWriter parallelWriter;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety0);
            parallelWriter.m_Safety0 = m_Safety0;
            AtomicSafetyHandle.UseSecondaryVersion(ref parallelWriter.m_Safety0);
            parallelWriter.m_BufferSafety = m_BufferSafety;
            parallelWriter.m_ArrayInvalidationSafety = m_ArrayInvalidationSafety;
            parallelWriter.m_SafetyReadOnlyCount = 0;
            parallelWriter.m_SafetyReadWriteCount = 3;

            if (m_Data->m_Allocator == Allocator.Temp)
            {
                throw new InvalidOperationException($"{nameof(EntityCommandBuffer.ParallelWriter)} can not use Allocator.Temp; use Allocator.TempJob instead");
            }
#endif
            parallelWriter.m_Data = m_Data;
            parallelWriter.m_ThreadIndex = -1;

            if (parallelWriter.m_Data != null)
            {
                parallelWriter.m_Data->InitForParallelWriter();
            }

            return parallelWriter;
        }

        /// <summary>
        /// Allows concurrent (deterministic) command buffer recording.
        /// </summary>
        [NativeContainer]
        [NativeContainerIsAtomicWriteOnly]
        [StructLayout(LayoutKind.Sequential)]
        unsafe public struct ParallelWriter
        {
            [NativeDisableUnsafePtrRestriction] internal EntityCommandBufferData* m_Data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety0;
            internal AtomicSafetyHandle m_BufferSafety;
            internal AtomicSafetyHandle m_ArrayInvalidationSafety;
            internal int m_SafetyReadOnlyCount;
            internal int m_SafetyReadWriteCount;
#endif

            // NOTE: Until we have a way to safely batch, let's keep it off
            private const bool kBatchableCommand = false;

            //internal ref int m_EntityIndex;
            [NativeSetThreadIndex]
            internal int m_ThreadIndex;

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private void CheckWriteAccess()
            {
                if (m_Data == null)
                    throw new NullReferenceException("The EntityCommandBuffer has not been initialized! The EntityCommandBuffer needs to be passed an Allocator when created!");
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety0);
#endif
            }

            private EntityCommandBufferChain* ThreadChain => (m_ThreadIndex >= 0) ? &m_Data->m_ThreadedChains[m_ThreadIndex] : &m_Data->m_MainThreadChain;

            /// <summary>Records a command to create an entity with specified archetype.</summary>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="archetype">The archetype of the new entity.</param>
            /// <returns>An entity that is deferred and will be fully realized when this EntityCommandBuffer is played back.</returns>
            /// <exception cref="ArgumentException">Throws if the archetype is null.</exception>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public Entity CreateEntity(int sortKey, EntityArchetype archetype)
            {
                archetype.CheckValidEntityArchetype();
                return _CreateEntity(sortKey, archetype);
            }

            /// <summary>Records a command to create an entity with no components.</summary>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <returns>An entity that is deferred and will be fully realized when this EntityCommandBuffer is played back.</returns>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public Entity CreateEntity(int sortKey)
            {
                EntityArchetype archetype = new EntityArchetype();
                return _CreateEntity(sortKey, archetype);
            }

            private Entity _CreateEntity(int sortKey, EntityArchetype archetype)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                // NOTE: Contention could be a performance problem especially on ARM
                // architecture. Maybe reserve a few indices for each job would be a better
                // approach or hijack the Version field of an Entity and store sortKey
                var entity = m_Data->m_Entity;
                entity.Index = Interlocked.Decrement(ref m_Data->m_Entity.Index);
                m_Data->AddCreateCommand(chain, sortKey, ECBCommand.CreateEntity,  entity.Index, archetype, kBatchableCommand);
                return entity;
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
            private static void CheckNotNull(Entity e)
            {
                if (e == Entity.Null)
                    throw new ArgumentNullException(nameof(e));
            }

            /// <summary>Records a command to create an entity with specified entity prefab.</summary>
            /// <remarks>An instantiated entity will have the same components and component values as the prefab entity, minus the Prefab tag component.
            /// Behavior at Playback: This command will throw an error if the source entity was destroyed before playback.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e">The entity prefab.</param>
            /// <returns>An entity that is deferred and will be fully realized when this EntityCommandBuffer is played back.</returns>
            /// <exception cref="ArgumentNullException"> Thrown if Entity e is null and if safety checks are enabled.</exception>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public Entity Instantiate(int sortKey, Entity e)
            {
                CheckNotNull(e);

                CheckWriteAccess();
                var chain = ThreadChain;
                var entity = m_Data->m_Entity;
                entity.Index = Interlocked.Decrement(ref m_Data->m_Entity.Index);
                m_Data->AddEntityCommand(chain, sortKey, ECBCommand.InstantiateEntity, entity.Index, e, kBatchableCommand);
                return entity;
            }

            /// <summary>Records a command to create a NativeArray of entities with specified entity prefab.</summary>
            /// <remarks>An instantiated entity will have the same components and component values as the prefab entity, minus the Prefab tag component.
            /// Behavior at Playback: This command will throw an error if the source entity was destroyed before playback.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e">The entity prefab.</param>
            /// <param name="entities">The NativeArray of entities that will be populated with realized entities when this EntityCommandBuffer is played back.</param>
            /// <exception cref="ArgumentNullException"> Thrown if Entity e is null and if safety checks are enabled.</exception>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void Instantiate(int sortKey, Entity e, NativeArray<Entity> entities)
            {
                CheckNotNull(e);

                CheckWriteAccess();
                var chain = ThreadChain;
                var entity = m_Data->m_Entity;
                int baseIndex = Interlocked.Add(ref m_Data->m_Entity.Index, -entities.Length) + entities.Length - 1;
                for (int i=0; i<entities.Length; ++i)
                {
                    entity.Index = baseIndex - i;
                    entities[i] = entity;
                }
                m_Data->AddMultipleEntityCommand(chain, sortKey, ECBCommand.InstantiateEntity, baseIndex, entities.Length, e, kBatchableCommand);
            }


            /// <summary>Records a command to destroy an entity.</summary>
            /// <remarks>Behavior at Playback: This command will throw an error if any of the entities are still deferred or were destroyed between recording and playback.
            /// </remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e">The entity to destroy.</param>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void DestroyEntity(int sortKey, Entity e)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityCommand(chain, sortKey, ECBCommand.DestroyEntity, 0, e, false);
            }

            /// <summary>Records a command to destroy a NativeArray of entities.</summary>
            /// <remarks>Behavior at Playback: This command will do nothing if entities has a count of 0.
            /// This command will throw an error if any of the entities are still deferred or were destroyed between recording and playback.
            /// </remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="entities">The NativeArray of entities to destroy.</param>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void DestroyEntity(int sortKey, NativeArray<Entity> entities)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
                m_Data->AppendMultipleEntitiesCommand(chain, sortKey, ECBCommand.DestroyMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities);
            }

            /// <summary> Records a command to add component of type T to an entity. </summary>
            /// <remarks>Behavior at Playback: If the entity already has this type of component, the value will just be set.
            /// Will throw an error if this entity is destroyed before playback, if this entity is still deferred,
            /// if T is type Entity, or adding this componentType makes the archetype too large.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e"> The entity to have the component added. </param>
            /// <param name="component">The value to add on the new component in playback for the entity.</param>
            /// <typeparam name="T"> The type of component to add. </typeparam>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void AddComponent<T>(int sortKey, Entity e, T component) where T : struct, IComponentData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentCommand(chain, sortKey, ECBCommand.AddComponent, e, component);
            }

            /// <summary> Records a command to add component of type T to a NativeArray of entities. </summary>
            /// <remarks>Behavior at Playback: If any entity already has this type of component, the value will just be set.
            /// Will throw an error if any entity is destroyed before playback, if any entity is still deferred,
            /// if T is type Entity, or adding this componentType makes the archetype too large.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="entities"> The NativeArray of entities to have the component added. </param>
            /// <param name="component">The value to add on the new component in playback for all entities in the NativeArray.</param>
            /// <typeparam name="T"> The type of component to add. </typeparam>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void AddComponent<T>(int sortKey, NativeArray<Entity> entities, T component) where T : struct, IComponentData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
                m_Data->AppendMultipleEntitiesComponentCommandWithValue(chain, sortKey,
                    ECBCommand.AddComponentForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, component);
            }

            /// <summary> Records a command to add component of type T to an entity. </summary>
            /// <remarks>Behavior at Playback: This command will do nothing if the entity already has the component.
            /// Will throw an error if this entity is destroyed before playback, if this entity is still deferred,
            /// if T is type Entity, or adding this componentType makes the archetype too large.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e"> The entity to have the component added. </param>
            /// <typeparam name="T"> The type of component to add. </typeparam>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void AddComponent<T>(int sortKey, Entity e) where T : struct, IComponentData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentTypeCommand(chain, sortKey, ECBCommand.AddComponent, e, ComponentType.ReadWrite<T>());
            }

            /// <summary> Records a command to add component of type T to a NativeArray of entities. </summary>
            /// <remarks>Behavior at Playback: If an entity already has this component, it will be skipped.
            /// Will throw an error if any entity is destroyed before playback, if any entity is still deferred,
            /// if T is type Entity, or adding this componentType makes the archetype too large.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="entities"> The NativeArray of entities to have the component added. </param>
            /// <typeparam name="T"> The type of component to add. </typeparam>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void AddComponent<T>(int sortKey, NativeArray<Entity> entities) where T : struct, IComponentData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
                m_Data->AppendMultipleEntitiesComponentCommand(chain, sortKey,
                    ECBCommand.AddComponentForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities,
                    ComponentType.ReadWrite<T>());
            }

            /// <summary> Records a command to add a component to an entity. </summary>
            /// <remarks>Behavior at Playback: This command will do nothing if the entity already has the component.
            /// Will throw an error if this entity is destroyed before playback, if this entity is still deferred,
            /// if component type is type Entity, or adding this componentType makes the archetype too large.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e"> The entity to get the additional component. </param>
            /// <param name="componentType"> The type of component to add. </param>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void AddComponent(int sortKey, Entity e, ComponentType componentType)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentTypeCommand(chain, sortKey, ECBCommand.AddComponent, e, componentType);
            }

            /// <summary> Records a command to add a component to a NativeArray of entities. </summary>
            /// <remarks>Behavior at Playback: If an entity already has this component, it will be skipped.
            /// Will throw an error if any entity is destroyed before playback, if any entity is still deferred,
            /// if component type is type Entity, or adding this componentType makes the archetype too large.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="entities"> The NativeArray of entities to have the component added. </param>
            /// <param name="componentType"> The type of component to add. </param>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void AddComponent(int sortKey, NativeArray<Entity> entities, ComponentType componentType)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
                m_Data->AppendMultipleEntitiesComponentCommand(chain, sortKey,
                    ECBCommand.AddComponentForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, componentType);
            }

            /// <summary> Records a command to add one or more components to an entity. </summary>
            /// <remarks>Behavior at Playback: It is not an error to include a component type that the entity already has.
            /// Will throw an error if this entity is destroyed before playback, if this entity is still deferred,
            /// if any component type is type Entity, or adding a component type makes the archetype too large.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e"> The entity to get additional components. </param>
            /// <param name="types"> The types of components to add. </param>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void AddComponent(int sortKey, Entity e, ComponentTypes types)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentTypesCommand(chain, sortKey,ECBCommand.AddMultipleComponents, e, types);
            }

            /// <summary> Records a command to add one or more components to a NativeArray of entities. </summary>
            /// <remarks>Behavior at Playback: It is not an error to include a component type that any of the entities already have.
            /// Will throw an error if any entity is destroyed before playback, if any entity is still deferred,
            /// if any component type is type Entity, or adding a component type makes the archetype too large.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="entities"> The NativeArray of entities to have the components added. </param>
            /// <param name="types"> The types of components to add. </param>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void AddComponent(int sortKey, NativeArray<Entity> entities, ComponentTypes types)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
                m_Data->AppendMultipleEntitiesMultipleComponentsCommand(chain, sortKey,
                    ECBCommand.AddMultipleComponentsForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, types);
            }

            /// <summary>Records a command to add a dynamic buffer to an entity.</summary>
            /// <remarks>Behavior at Playback: If the entity already has this type of dynamic buffer, the dynamic buffer will just be set.
            /// Will throw an error if this entity is destroyed before playback, if this entity is still deferred,
            /// or if the entity doesn't have a <see cref="DynamicBuffer{T}"/> component storing elements of type T.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e">The entity to add the dynamic buffer to.</param>
            /// <typeparam name="T">The <see cref="IBufferElementData"/> type stored by the <see cref="DynamicBuffer{T}"/>.</typeparam>
            /// <returns>The <see cref="DynamicBuffer{T}"/> that will be added when the command plays back.</returns>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public DynamicBuffer<T> AddBuffer<T>(int sortKey, Entity e) where T : struct, IBufferElementData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                return m_Data->CreateBufferCommand<T>(ECBCommand.AddBuffer, chain, sortKey, e, m_BufferSafety, m_ArrayInvalidationSafety);
#else
                return m_Data->CreateBufferCommand<T>(ECBCommand.AddBuffer, chain, sortKey, e);
#endif
            }

            /// <summary>Records a command to set a dynamic buffer on an entity.</summary>
            /// <remarks>Behavior at Playback: Will throw an error if this entity is destroyed before playback,
            /// if this entity is still deferred, or if the entity doesn't have a <see cref="DynamicBuffer{T}"/> component storing elements of type T.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e">The entity to set the dynamic buffer on.</param>
            /// <typeparam name="T">The <see cref="IBufferElementData"/> type stored by the <see cref="DynamicBuffer{T}"/>.</typeparam>
            /// <returns>The <see cref="DynamicBuffer{T}"/> that will be set when the command plays back.</returns>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public DynamicBuffer<T> SetBuffer<T>(int sortKey, Entity e) where T : struct, IBufferElementData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                return m_Data->CreateBufferCommand<T>(ECBCommand.SetBuffer, chain, sortKey, e, m_BufferSafety, m_ArrayInvalidationSafety);
#else
                return m_Data->CreateBufferCommand<T>(ECBCommand.SetBuffer, chain, sortKey, e);
#endif
            }

            /// <summary>Records a command to append a single element to the end of a dynamic buffer component.</summary>
            /// <remarks>Behavior at Playback: Will throw an error if this entity is destroyed before playback,
            /// if this entity is still deferred, or if the entity doesn't have a <see cref="DynamicBuffer{T}"/> component storing elements of type T.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e">The entity to which the dynamic buffer belongs.</param>
            /// <param name="element">The new element to add to the <see cref="DynamicBuffer{T}"/> component.</param>
            /// <typeparam name="T">The <see cref="IBufferElementData"/> type stored by the <see cref="DynamicBuffer{T}"/>.</typeparam>
            /// <exception cref="InvalidOperationException">Thrown if the entity does not have a <see cref="DynamicBuffer{T}"/>
            /// component storing elements of type T at the time the entity command buffer executes this append-to-buffer command.</exception>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void AppendToBuffer<T>(int sortKey, Entity e, T element) where T : struct, IBufferElementData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AppendToBufferCommand<T>(chain, sortKey, e, element);
            }

            /// <summary> Records a command to set a component value on an entity.</summary>
            /// <remarks> Behavior at Playback: Will throw an error if this entity is destroyed before playback,
            /// if this entity is still deferred, if the entity doesn't have the component type, or if T is zero sized.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e"> The entity to set the component value of. </param>
            /// <param name="component"> The component value to set. </param>
            /// <typeparam name="T"> The type of component to set. </typeparam>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void SetComponent<T>(int sortKey, Entity e, T component) where T : struct, IComponentData
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentCommand(chain, sortKey, ECBCommand.SetComponent, e, component);
            }

            // TODO(https://unity3d.atlassian.net/browse/DOTS-4242): make these methods public once enabled bits support is complete
            [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleEnableableComponent) })]
            internal void SetComponentEnabled<T>(int sortKey, Entity e, bool value) where T: struct, IEnableableComponent
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentEnabledCommand(chain, sortKey,
                    ECBCommand.SetComponentEnabled, e, TypeManager.GetTypeIndex<T>(), value);
            }
            internal void SetComponentEnabled(int sortKey, Entity e, ComponentType componentType, bool value)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentEnabledCommand(chain, sortKey,
                    ECBCommand.SetComponentEnabled, e, componentType.TypeIndex, value);
            }

            /// <summary> Records a command to set a name of an entity if Debug Names is enabled.</summary>
            /// <remarks> Behavior at Playback: Will throw an error if this entity is destroyed before playback,
            /// if this entity is still deferred, or if the EntityNameStore has reached its limit.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e"> The entity to set the name value of. </param>
            /// <param name="name"> The name to set. </param>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void SetName(int sortKey, Entity e, in FixedString64Bytes name)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityNameCommand(chain, sortKey, ECBCommand.SetName, e, name);
            }

            /// <summary> Records a command to remove component of type T from an entity. </summary>
            /// <remarks>Behavior at Playback: It is not an error if the entity doesn't have component T.
            /// Will throw an error if this entity is destroyed before playback,
            /// if this entity is still deferred, or if T is type Entity.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e"> The entity to have the component removed. </param>
            /// <typeparam name="T"> The type of component to remove. </typeparam>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void RemoveComponent<T>(int sortKey, Entity e)
            {
                RemoveComponent(sortKey, e, ComponentType.ReadWrite<T>());
            }

            /// <summary> Records a command to remove component of type T from a NativeArray of entities. </summary>
            /// <remarks>Behavior at Playback: It is not an error if any entity doesn't have component T.
            /// Will throw an error if one of these entities is destroyed before playback,
            /// if one of these entities is still deferred, or if T is type Entity.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="entities"> The NativeArray of entities to have the component removed. </param>
            /// <typeparam name="T"> The type of component to remove. </typeparam>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void RemoveComponent<T>(int sortKey, NativeArray<Entity> entities)
            {
                RemoveComponent(sortKey, entities, ComponentType.ReadWrite<T>());
            }

            /// <summary> Records a command to remove a component from an entity. </summary>
            /// <remarks>Behavior at Playback: It is not an error if the entity doesn't have the component type.
            /// Will throw an error if this entity is destroyed before playback,
            /// if this entity is still deferred, or if the component type is Entity.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e"> The entity to have the component removed. </param>
            /// <param name="componentType"> The type of component to remove. </param>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void RemoveComponent(int sortKey, Entity e, ComponentType componentType)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentTypeCommand(chain, sortKey, ECBCommand.RemoveComponent, e, componentType);
            }

            /// <summary> Records a command to remove one or more components from a NativeArray of entities. </summary>
            /// <remarks>Behavior at Playback: It is not an error if any entity doesn't have the component type.
            /// Will throw an error if one of these entities is destroyed before playback,
            /// if one of these entities is still deferred, or if the component type is Entity.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="entities"> The NativeArray of entities to have the component removed. </param>
            /// <param name="componentType"> The type of component to remove. </param>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void RemoveComponent(int sortKey, NativeArray<Entity> entities, ComponentType componentType)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
                m_Data->AppendMultipleEntitiesComponentCommand(chain, sortKey,
                    ECBCommand.RemoveComponentForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, componentType);
            }

            /// <summary> Records a command to remove one or more components from an entity. </summary>
            /// <remarks>Behavior at Playback: It is not an error if the entity doesn't have one of the component types.
            /// Will throw an error if this entity is destroyed before playback,
            /// if this entity is still deferred, or if any of the component types are Entity.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e"> The entity to have the components removed. </param>
            /// <param name="types"> The types of components to remove. </param>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void RemoveComponent(int sortKey, Entity e, ComponentTypes types)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                m_Data->AddEntityComponentTypesCommand(chain, sortKey,ECBCommand.RemoveMultipleComponents, e, types);
            }

            /// <summary> Records a command to remove one or more components from a NativeArray of entities. </summary>
            /// <remarks>Behavior at Playback: It is not an error if any entity doesn't have one of the component types.
            /// Will throw an error if one of these entities is destroyed before playback,
            /// if one of these entities is still deferred, or if any of the component types are Entity.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="entities"> The NativeArray of entities to have components removed. </param>
            /// <param name="types"> The types of components to remove. </param>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void RemoveComponent(int sortKey, NativeArray<Entity> entities, ComponentTypes types)
            {
                CheckWriteAccess();
                var chain = ThreadChain;
                var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
                m_Data->AppendMultipleEntitiesMultipleComponentsCommand(chain, sortKey,
                    ECBCommand.RemoveMultipleComponentsForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, types);
            }

            /// <summary> Records a command to add a shared component value on an entity.</summary>
            /// <remarks>Behavior at Playback: Will throw an error if this entity is destroyed before playback,
            /// if this entity is still deferred, if adding this shared component exceeds the maximum number of shared components,
            /// or adding a component type makes the archetype too large.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e"> The entity to add the shared component value to. </param>
            /// <param name="component"> The shared component value to add. </param>
            /// <typeparam name="T"> The type of shared component to add. </typeparam>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void AddSharedComponent<T>(int sortKey, Entity e, T component) where T : struct, ISharedComponentData
            {
                CheckWriteAccess();
                var chain = ThreadChain;

                int hashCode;
                if (IsDefaultObject(ref component, out hashCode))
                    m_Data->AddEntitySharedComponentCommand<T>(chain, sortKey, ECBCommand.AddSharedComponentData, e, hashCode, null);
                else
                    m_Data->AddEntitySharedComponentCommand<T>(chain, sortKey, ECBCommand.AddSharedComponentData, e, hashCode, component);
            }

            /// <summary> Records a command to add a shared component value on a NativeArray of entities.</summary>
            /// <remarks>Behavior at Playback: Will throw an error if any entity is destroyed before playback,
            /// if any entity is still deferred, if adding this shared component exceeds the maximum number of shared components,
            /// or adding a component type makes the archetype too large.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="entities"> The NativeArray of entities to add the shared component value to. </param>
            /// <param name="component"> The shared component value to add. </param>
            /// <typeparam name="T"> The type of shared component to add. </typeparam>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void AddSharedComponent<T>(int sortKey, NativeArray<Entity> entities, T component) where T : struct, ISharedComponentData
            {
                CheckWriteAccess();
                var chain = ThreadChain;

                int hashCode;
                var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
                if (IsDefaultObject(ref component, out hashCode))
                    m_Data->AppendMultipleEntitiesComponentCommandWithSharedValue<T>(chain, sortKey, ECBCommand.AddSharedComponentWithValueForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, hashCode, null);
                else
                    m_Data->AppendMultipleEntitiesComponentCommandWithSharedValue<T>(chain, sortKey, ECBCommand.AddSharedComponentWithValueForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, hashCode, component);
            }

            /// <summary> Records a command to set a shared component value on an entity.</summary>
            /// <remarks> Behavior at Playback: Will throw an error if this entity is destroyed before playback,
            /// if this entity is still deferred, or if the entity doesn't have the shared component type.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="e"> The entity to set the shared component value of. </param>
            /// <param name="component"> The shared component value to set. </param>
            /// <typeparam name="T"> The type of shared component to set. </typeparam>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void SetSharedComponent<T>(int sortKey, Entity e, T component) where T : struct, ISharedComponentData
            {
                CheckWriteAccess();
                var chain = ThreadChain;

                int hashCode;
                if (IsDefaultObject(ref component, out hashCode))
                    m_Data->AddEntitySharedComponentCommand<T>(chain, sortKey, ECBCommand.SetSharedComponentData, e, hashCode, null);
                else
                    m_Data->AddEntitySharedComponentCommand<T>(chain, sortKey, ECBCommand.SetSharedComponentData, e, hashCode, component);
            }

            /// <summary> Records a command to set a shared component value on a NativeArray of entities.</summary>
            /// <remarks> Behavior at Playback: Will throw an error if any entity is destroyed before playback,
            /// if any entity is still deferred, or if any entity doesn't have the shared component type.</remarks>
            /// <param name="sortKey">A unique index for each set of commands added to this EntityCommandBuffer
            /// across all parallel jobs writing commands to this buffer. The `entityInQueryIndex` argument provided by
            /// <see cref="SystemBase.Entities"/> is an appropriate value to use for this parameter. In an <see cref="IJobEntityBatch"/>
            /// pass the 'batchIndex' value from <see cref="IJobEntityBatch.Execute(ArchetypeChunk, int)"/>.</param>
            /// <param name="entities"> The NativeArray of entities to set the shared component value of. </param>
            /// <param name="component"> The shared component value to set. </param>
            /// <typeparam name="T"> The type of shared component to set. </typeparam>
            /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
            public void SetSharedComponent<T>(int sortKey, NativeArray<Entity> entities, T component) where T : struct, ISharedComponentData
            {
                CheckWriteAccess();
                var chain = ThreadChain;

                int hashCode;
                var entitiesCopy = m_Data->CloneAndSearchForDeferredEntities(entities, out var containsDeferredEntities);
                if (IsDefaultObject(ref component, out hashCode))
                    m_Data->AppendMultipleEntitiesComponentCommandWithSharedValue<T>(chain, sortKey, ECBCommand.SetSharedComponentValueForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, hashCode, null);
                else
                    m_Data->AppendMultipleEntitiesComponentCommandWithSharedValue<T>(chain, sortKey, ECBCommand.SetSharedComponentValueForMultipleEntities, entitiesCopy, entities.Length, containsDeferredEntities, hashCode, component);
            }
        }
    }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
    public static unsafe class EntityCommandBufferManagedComponentExtensions
    {
        /// <summary> Records a command to add and set a managed component for an entity.</summary>
        /// <remarks>Behavior at Playback: If the entity already has this type of component, the value will just be set.
        /// Will throw an error if this entity is destroyed before playback, if this entity is still deferred,
        /// or adding this componentType makes the archetype too large.</remarks>
        /// <param name="e"> The entity to set the component value on.</param>
        /// <param name="component"> The component value to add. </param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public static void AddComponent<T>(this EntityCommandBuffer ecb, Entity e, T component) where T : class, IComponentData
        {
            ecb.EnforceSingleThreadOwnership();
            ecb.AssertDidNotPlayback();
            AddEntityComponentCommandFromMainThread(ecb.m_Data, ecb.MainThreadSortKey, ECBCommand.AddManagedComponentData, e, component);
            ecb.m_Data->m_MainThreadChain.m_CanBurstPlayback = false;
        }

        /// <summary> Records a command to add a managed component for an entity.</summary>
        /// <remarks>Behavior at Playback: Will throw an error if this entity is destroyed before playback,
        /// if this entity is still deferred, or adding this componentType makes the archetype too large.</remarks>
        /// <param name="e"> The entity to set the component value on.</param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public static void AddComponent<T>(this EntityCommandBuffer ecb, Entity e) where T : class, IComponentData
        {
            ecb.EnforceSingleThreadOwnership();
            ecb.AssertDidNotPlayback();
            ecb.m_Data->AddEntityComponentTypeCommand(&ecb.m_Data->m_MainThreadChain, ecb.MainThreadSortKey, ECBCommand.AddManagedComponentData, e, ComponentType.ReadWrite<T>());
            ecb.m_Data->m_MainThreadChain.m_CanBurstPlayback = false;
        }

        /// <summary> Records a command to set a managed component for an entity.</summary>
        /// <remarks> Behavior at Playback: Will throw an error if this entity is destroyed before playback,
        /// if this entity is still deferred, or if the entity doesn't have the shared component type.</remarks>
        /// <param name="e"> The entity to set the component value on.</param>
        /// <param name="component"> The component value to add. </param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public static void SetComponent<T>(this EntityCommandBuffer ecb, Entity e, T component) where T : class, IComponentData
        {
            ecb.EnforceSingleThreadOwnership();
            ecb.AssertDidNotPlayback();
            AddEntityComponentCommandFromMainThread(ecb.m_Data, ecb.MainThreadSortKey, ECBCommand.SetManagedComponentData, e, component);
            ecb.m_Data->m_MainThreadChain.m_CanBurstPlayback = false;
        }

        /// <summary> Records a command to add a managed component and set its value for all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// Entities which already have the component type will have the component set to the value.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown in playback if one or more of the entities has been destroyed. (With safety checks disabled,
        /// playback will perform invalid and unsafe memory access.).</exception>
        /// <param name="query"> The query specifying which entities to add the component value to.</param>
        /// <param name="component"> The component value to add. </param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public static void AddComponentForEntityQuery<T>(this EntityCommandBuffer ecb, EntityQuery query, T component) where T : class, IComponentData
        {
            ecb.AddComponentObjectForEntityQuery(query, component);
        }

        /// <summary> Records a command to set a managed component value for all entities matching a query.</summary>
        /// <remarks>The set of entities matching the query is 'captured' in the method call, and the recorded command stores an array of all these entities.
        ///
        /// If any entity does not have the component type at playback , playback Entities which already have the component type will have the component set to the value.
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown in playback if one or more of the entities does not have the component type or has been destroyed. (With safety checks disabled,
        /// playback will perform invalid and unsafe memory access.).</exception>
        /// <param name="query"> The query specifying which entities to set the component value for.</param>
        /// <param name="component"> The component value to set.</param>
        /// <exception cref="NullReferenceException">Throws if an Allocator was not passed in when the EntityCommandBuffer was created.</exception>
        /// <exception cref="InvalidOperationException">Throws if this EntityCommandBuffer has already been played back.</exception>
        public static void SetComponentForEntityQuery<T>(this EntityCommandBuffer ecb, EntityQuery query, T component) where T : class, IComponentData
        {
            ecb.SetComponentObjectForEntityQuery(query, component);
        }

        internal static void AddEntityComponentCommandFromMainThread<T>(EntityCommandBufferData* ecbd, int sortKey, ECBCommand op, Entity e, T component) where T : class, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var sizeNeeded = EntityCommandBufferData.Align(sizeof(EntityManagedComponentCommand), 8);

            var chain = &ecbd->m_MainThreadChain;
            ecbd->ResetCommandBatching(chain);
            var data = (EntityManagedComponentCommand*)ecbd->Reserve(chain, sortKey, sizeNeeded);

            data->Header.Header.CommandType = op;
            data->Header.Header.TotalSize = sizeNeeded;
            data->Header.Header.SortKey = chain->m_LastSortKey;
            data->Header.Entity = e;
            data->Header.IdentityIndex = 0;
            data->Header.BatchCount = 1;
            data->ComponentTypeIndex = typeIndex;

            if (component != null)
            {
                data->GCNode.BoxedObject = GCHandle.Alloc(component);
                // We need to store all GCHandles on a cleanup list so we can dispose them later, regardless of if playback occurs or not.
                data->GCNode.Prev = chain->m_Cleanup->CleanupList;
                chain->m_Cleanup->CleanupList = &(data->GCNode);
            }
            else
            {
                data->GCNode.BoxedObject = new GCHandle();
            }
        }
    }
#endif
}
