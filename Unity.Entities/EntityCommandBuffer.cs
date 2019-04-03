using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Unity.Entities
{
    internal unsafe struct EntityCommandBufferData
    {
        public EntityCommandBuffer.Chunk* m_Tail;
        public EntityCommandBuffer.Chunk* m_Head;
        public EntityCommandBuffer.EntitySharedComponentCommand* m_CleanupList;

        public int m_MinimumChunkSize;

        public Allocator m_Allocator;
    }

    /// <summary>
    ///     A thread-safe command buffer that can buffer commands that affect entities and components for later playback.
    /// </summary>
    /// Command buffers are not created in user code directly, you get them from either a ComponentSystem or a Barrier.
    [StructLayout(LayoutKind.Sequential)]
    [NativeContainer]
    public unsafe struct EntityCommandBuffer
    {
        /// <summary>
        ///     The minimum chunk size to allocate from the job allocator.
        /// </summary>
        /// We keep this relatively small as we don't want to overload the temp allocator in case people make a ton of command buffers.
        /// <summary>
        ///     Organized in memory like a single block with Chunk header followed by Size bytes of data.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct Chunk
        {
            internal int Used;
            internal int Size;
            internal Chunk* Next;
            internal Chunk* Prev;

            internal int Capacity => Size - Used;

            internal int Bump(int size)
            {
                var off = Used;
                Used += size;
                return off;
            }
        }

        private const int kDefaultMinimumChunkSize = 4 * 1024;

        [NativeDisableUnsafePtrRestriction] private EntityCommandBufferData* m_Data;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private readonly AtomicSafetyHandle m_Safety;
#endif

        /// <summary>
        ///     Allows controlling the size of chunks allocated from the temp job allocator to back the command buffer.
        /// </summary>
        /// Larger sizes are more efficient, but create more waste in the allocator.
        public int MinimumChunkSize
        {
            get => m_Data->m_MinimumChunkSize > 0 ? m_Data->m_MinimumChunkSize : kDefaultMinimumChunkSize;
            set => m_Data->m_MinimumChunkSize = Math.Max(0, value);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BasicCommand
        {
            public int CommandType;
            public int TotalSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CreateCommand
        {
            public BasicCommand Header;
            public EntityArchetype Archetype;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EntityCommand
        {
            public BasicCommand Header;
            public Entity Entity;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EntityComponentCommand
        {
            public EntityCommand Header;
            public int ComponentTypeIndex;

            public int ComponentSize;
            // Data follows if command has an associated component payload
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EntitySharedComponentCommand
        {
            public EntityCommand Header;
            public int ComponentTypeIndex;
            public int HashCode;
            public GCHandle BoxedObject;
            public EntitySharedComponentCommand* Prev;

            internal object GetBoxedObject()
            {
                if (BoxedObject.IsAllocated)
                    return BoxedObject.Target;
                return null;
            }
        }

        private byte* Reserve(int size)
        {
            var data = m_Data;

            if (data->m_Tail == null || data->m_Tail->Capacity < size)
            {
                var chunkSize = math.max(MinimumChunkSize, size);

                var c = (Chunk*) UnsafeUtility.Malloc(sizeof(Chunk) + chunkSize, 16, data->m_Allocator);
                var prev = data->m_Tail;
                c->Next = null;
                c->Prev = prev;
                c->Used = 0;
                c->Size = chunkSize;

                if (prev != null) prev->Next = c;

                if (data->m_Head == null) data->m_Head = c;

                data->m_Tail = c;
            }

            var offset = data->m_Tail->Bump(size);
            var ptr = (byte*) data->m_Tail + sizeof(Chunk) + offset;
            return ptr;
        }

        private void AddCreateCommand(Command op, EntityArchetype archetype)
        {
            var data = (CreateCommand*) Reserve(sizeof(CreateCommand));

            data->Header.CommandType = (int) op;
            data->Header.TotalSize = sizeof(CreateCommand);
            data->Archetype = archetype;
        }

        private void AddEntityCommand(Command op, Entity e)
        {
            var data = (EntityCommand*) Reserve(sizeof(EntityCommand));

            data->Header.CommandType = (int) op;
            data->Header.TotalSize = sizeof(EntityCommand);
            data->Entity = e;
        }

        private void AddEntityComponentCommand<T>(Command op, Entity e, T component) where T : struct
        {
            var typeSize = UnsafeUtility.SizeOf<T>();
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var sizeNeeded = Align(sizeof(EntityComponentCommand) + typeSize, 8);

            var data = (EntityComponentCommand*) Reserve(sizeNeeded);

            data->Header.Header.CommandType = (int) op;
            data->Header.Header.TotalSize = sizeNeeded;
            data->Header.Entity = e;
            data->ComponentTypeIndex = typeIndex;
            data->ComponentSize = typeSize;

            UnsafeUtility.CopyStructureToPtr(ref component, (byte*) (data + 1));
        }

        private static int Align(int size, int alignmentPowerOfTwo)
        {
            return (size + alignmentPowerOfTwo - 1) & ~(alignmentPowerOfTwo - 1);
        }

        private void AddEntityComponentTypeCommand(Command op, Entity e, ComponentType t)
        {
            var sizeNeeded = Align(sizeof(EntityComponentCommand), 8);

            var data = (EntityComponentCommand*) Reserve(sizeNeeded);

            data->Header.Header.CommandType = (int) op;
            data->Header.Header.TotalSize = sizeNeeded;
            data->Header.Entity = e;
            data->ComponentTypeIndex = t.TypeIndex;
        }

        private void AddEntitySharedComponentCommand<T>(Command op, Entity e, int hashCode, object boxedObject)
            where T : struct
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var sizeNeeded = Align(sizeof(EntitySharedComponentCommand), 8);

            var data = (EntitySharedComponentCommand*) Reserve(sizeNeeded);

            data->Header.Header.CommandType = (int) op;
            data->Header.Header.TotalSize = sizeNeeded;
            data->Header.Entity = e;
            data->ComponentTypeIndex = typeIndex;
            data->HashCode = hashCode;

            if (boxedObject != null)
            {
                data->BoxedObject = GCHandle.Alloc(boxedObject);
                // We need to store all GCHandles on a cleanup list so we can dispose them later, regardless of if playback occurs or not.
                data->Prev = m_Data->m_CleanupList;
                m_Data->m_CleanupList = data;
            }
            else
            {
                data->BoxedObject = new GCHandle();
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void EnforceSingleThreadOwnership()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        }

        /// <summary>
        ///     Creates a new command buffer. Note that this is internal and not exposed to user code.
        /// </summary>
        /// <param name="label">Memory allocator to use for chunks and data</param>
        internal EntityCommandBuffer(Allocator label)
        {
            m_Data = (EntityCommandBufferData*) UnsafeUtility.Malloc(sizeof(EntityCommandBufferData),
                UnsafeUtility.AlignOf<EntityCommandBufferData>(), label);
            m_Data->m_Allocator = label;
            m_Data->m_Tail = null;
            m_Data->m_Head = null;
            m_Data->m_MinimumChunkSize = kDefaultMinimumChunkSize;
            m_Data->m_CleanupList = null;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
#endif
        }

        internal void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);
#endif
            if (m_Data != null)
            {
                var cleanup_list = m_Data->m_CleanupList;
                while (cleanup_list != null)
                {
                    cleanup_list->BoxedObject.Free();
                    cleanup_list = cleanup_list->Prev;
                }

                m_Data->m_CleanupList = null;

                var label = m_Data->m_Allocator;

                while (m_Data->m_Tail != null)
                {
                    var prev = m_Data->m_Tail->Prev;
                    UnsafeUtility.Free(m_Data->m_Tail, m_Data->m_Allocator);
                    m_Data->m_Tail = prev;
                }

                m_Data->m_Head = null;
                UnsafeUtility.Free(m_Data, label);
                m_Data = null;
            }
        }

        public void CreateEntity()
        {
            EnforceSingleThreadOwnership();
            AddCreateCommand(Command.CreateEntity, new EntityArchetype());
        }

        public void CreateEntity(EntityArchetype archetype)
        {
            EnforceSingleThreadOwnership();
            AddCreateCommand(Command.CreateEntity, archetype);
        }

        public void DestroyEntity(Entity e)
        {
            EnforceSingleThreadOwnership();
            AddEntityCommand(Command.DestroyEntity, e);
        }

        public void AddComponent<T>(Entity e, T component) where T : struct, IComponentData
        {
            EnforceSingleThreadOwnership();
            AddEntityComponentCommand(Command.AddComponent, e, component);
        }

        public void AddComponent<T>(T component) where T : struct, IComponentData
        {
            AddComponent(Entity.Null, component);
        }

        public void SetComponent<T>(T component) where T : struct, IComponentData
        {
            SetComponent(Entity.Null, component);
        }

        public void SetComponent<T>(Entity e, T component) where T : struct, IComponentData
        {
            EnforceSingleThreadOwnership();
            AddEntityComponentCommand(Command.SetComponent, e, component);
        }

        public void RemoveComponent<T>(Entity e)
        {
            EnforceSingleThreadOwnership();
            AddEntityComponentTypeCommand(Command.RemoveComponent, e, ComponentType.Create<T>());
        }

        public void RemoveSystemStateComponent<T>(Entity e)
        {
            EnforceSingleThreadOwnership();
            AddEntityComponentTypeCommand(Command.RemoveSystemStateComponent, e, ComponentType.Create<T>());
        }

        private static bool IsDefaultObject<T>(ref T component, out int hashCode) where T : struct, ISharedComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var layout = TypeManager.GetComponentType(typeIndex).FastEqualityLayout;
            var defaultValue = default(T);
            hashCode = FastEquality.GetHashCode(ref component, layout);
            return FastEquality.Equals(ref defaultValue, ref component, layout);
        }

        public void AddSharedComponent<T>(T component) where T : struct, ISharedComponentData
        {
            AddSharedComponent(Entity.Null, component);
        }

        public void AddSharedComponent<T>(Entity e, T component) where T : struct, ISharedComponentData
        {
            EnforceSingleThreadOwnership();
            int hashCode;
            if (IsDefaultObject(ref component, out hashCode))
                AddEntitySharedComponentCommand<T>(Command.AddSharedComponentData, e, hashCode, null);
            else
                AddEntitySharedComponentCommand<T>(Command.AddSharedComponentData, e, hashCode, component);
        }

        public void SetSharedComponent<T>(T component) where T : struct, ISharedComponentData
        {
            SetSharedComponent(Entity.Null, component);
        }

        public void SetSharedComponent<T>(Entity e, T component) where T : struct, ISharedComponentData
        {
            EnforceSingleThreadOwnership();
            int hashCode;
            if (IsDefaultObject(ref component, out hashCode))
                AddEntitySharedComponentCommand<T>(Command.SetSharedComponentData, e, hashCode, null);
            else
                AddEntitySharedComponentCommand<T>(Command.SetSharedComponentData, e, hashCode, component);
        }

        private enum Command
        {
            CreateEntity,
            DestroyEntity,

            AddComponent,
            RemoveComponent,
            RemoveSystemStateComponent,
            SetComponent,

            AddSharedComponentData,
            SetSharedComponentData
        }

        /// <summary>
        ///     Play back all recorded operations against an entity manager.
        /// </summary>
        /// <param name="mgr">The entity manager that will receive the operations</param>
        public void Playback(EntityManager mgr)
        {
            if (mgr == null)
                throw new NullReferenceException($"{nameof(mgr)} cannot be null");

            EnforceSingleThreadOwnership();

            var head = m_Data->m_Head;
            var lastEntity = new Entity();

            while (head != null)
            {
                var off = 0;
                var buf = (byte*) head + sizeof(Chunk);

                while (off < head->Used)
                {
                    var header = (BasicCommand*) (buf + off);

                    switch ((Command) header->CommandType)
                    {
                        case Command.DestroyEntity:
                        {
                            mgr.DestroyEntity(((EntityCommand*) header)->Entity);
                        }
                            break;

                        case Command.RemoveComponent:
                        {
                            var cmd = (EntityComponentCommand*) header;
                            var entity = cmd->Header.Entity == Entity.Null ? lastEntity : cmd->Header.Entity;
                            mgr.RemoveComponent(entity, TypeManager.GetType(cmd->ComponentTypeIndex));
                        }
                            break;

                        case Command.RemoveSystemStateComponent:
                        {
                            var cmd = (EntityComponentCommand*) header;
                            var entity = cmd->Header.Entity == Entity.Null ? lastEntity : cmd->Header.Entity;
                            mgr.RemoveSystemStateComponent(entity, TypeManager.GetType(cmd->ComponentTypeIndex));
                        }
                            break;

                        case Command.CreateEntity:
                        {
                            var cmd = (CreateCommand*) header;
                            if (cmd->Archetype.Valid)
                                lastEntity = mgr.CreateEntity(cmd->Archetype);
                            else
                                lastEntity = mgr.CreateEntity();
                            break;
                        }

                        case Command.AddComponent:
                        {
                            var cmd = (EntityComponentCommand*) header;
                            var componentType = (ComponentType) TypeManager.GetType(cmd->ComponentTypeIndex);
                            var entity = cmd->Header.Entity == Entity.Null ? lastEntity : cmd->Header.Entity;
                            mgr.AddComponent(entity, componentType);
                            mgr.SetComponentDataRaw(entity, cmd->ComponentTypeIndex, cmd + 1, cmd->ComponentSize);
                        }
                            break;

                        case Command.SetComponent:
                        {
                            var cmd = (EntityComponentCommand*) header;
                            var entity = cmd->Header.Entity == Entity.Null ? lastEntity : cmd->Header.Entity;
                            mgr.SetComponentDataRaw(entity, cmd->ComponentTypeIndex, cmd + 1, cmd->ComponentSize);
                        }
                            break;

                        case Command.AddSharedComponentData:
                        {
                            var cmd = (EntitySharedComponentCommand*) header;
                            var entity = cmd->Header.Entity == Entity.Null ? lastEntity : cmd->Header.Entity;
                            mgr.AddSharedComponentDataBoxed(entity, cmd->ComponentTypeIndex, cmd->HashCode,
                                cmd->GetBoxedObject());
                        }
                            break;

                        case Command.SetSharedComponentData:
                        {
                            var cmd = (EntitySharedComponentCommand*) header;
                            var entity = cmd->Header.Entity == Entity.Null ? lastEntity : cmd->Header.Entity;
                            mgr.SetSharedComponentDataBoxed(entity, cmd->ComponentTypeIndex, cmd->HashCode,
                                cmd->GetBoxedObject());
                        }
                            break;
                    }

                    off += header->TotalSize;
                }

                head = head->Next;
            }
        }
    }
}
