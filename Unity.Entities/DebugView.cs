using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Entities
{
    sealed class ArchetypeChunkDataDebugView
    {
#if !NET_DOTS
        private ArchetypeChunkData m_ChunkData;

        public ArchetypeChunkDataDebugView(ArchetypeChunkData chunkData)
        {
            m_ChunkData = chunkData;
        }

        public unsafe ArchetypeChunk[] Items
        {
            get
            {
                var result = new ArchetypeChunk[m_ChunkData.Count];
                for (var i = 0; i < result.Length; ++i)
                    result[i] = new ArchetypeChunk(m_ChunkData[i], null);
                return result;
            }
        }
#endif
    }

    sealed class UnsafeMatchingArchetypePtrListDebugView
    {
#if !NET_DOTS
        private UnsafeMatchingArchetypePtrList m_MatchingArchetypeList;

        public UnsafeMatchingArchetypePtrListDebugView(UnsafeMatchingArchetypePtrList MatchingArchetypeList)
        {
            m_MatchingArchetypeList = MatchingArchetypeList;
        }

        public unsafe MatchingArchetype*[] Items
        {
            get
            {
                var result = new MatchingArchetype*[m_MatchingArchetypeList.Length];
                for (var i = 0; i < result.Length; ++i)
                    result[i] = m_MatchingArchetypeList.Ptr[i];
                return result;
            }
        }
#endif
    }

    public struct SharedComponentView
    {
#if !NET_DOTS
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public ComponentType Type;
        public object Value;

        public SharedComponentView(int typeIndex, int valueIndex)
        {
            Type = ComponentType.FromTypeIndex(typeIndex);
            Value = null;
            var world = GetCurrentWorld();
            if (TypeManager.IsSharedComponentType(typeIndex))
            {
                if (world != null && world.IsCreated)
                {
                    Value = world.EntityManager.GetSharedComponentDataBoxed(valueIndex, typeIndex);
                }
            }
        }

        public override string ToString()
        {
            return Type.ToString();
        }

        // Utility to guess which World is currently "active", based on the following heuristic:
        // - if a World has a system executing, return that one.
        // - if the default World exists, return that one.
        // - otherwise, return null.
        public static World GetCurrentWorld()
        {
            if (!JobsUtility.IsExecutingJob)
            {
                foreach (var world in World.All)
                {
                    if (world.Unmanaged.ExecutingSystem != default)
                        return world;
                }
            }

            if (World.DefaultGameObjectInjectionWorld != null && World.DefaultGameObjectInjectionWorld.IsCreated)
                return World.DefaultGameObjectInjectionWorld;

            return null;
        }
#endif
    }

    sealed unsafe class DebugViewUtility
    {
#if !NET_DOTS
        [DebuggerDisplay("{name} {entity} Components: {components.Count}")]
        public struct Components
        {
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public string name;
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public Entity entity;
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public List<object> components;
        }

        public static object GetComponent(void* pointer, Type type)
        {
            if (typeof(IBufferElementData).IsAssignableFrom(type))
            {
                var listType = typeof(List<>);
                var constructedListType = listType.MakeGenericType(type);
                var instance = (IList)Activator.CreateInstance(constructedListType);
                var size = Marshal.SizeOf(type);
                BufferHeader* header = (BufferHeader*)pointer;
                var begin = BufferHeader.GetElementPointer(header);
                for (var i = 0; i < header->Length; ++i)
                {
                    var item = begin + (size * i);
                    instance.Add(Marshal.PtrToStructure((IntPtr)item, type));
                }
                return instance;
            }
            if (typeof(IComponentData).IsAssignableFrom(type) || typeof(Entity).IsAssignableFrom(type))
            {
                return Marshal.PtrToStructure((IntPtr)pointer, type);
            }
            return null;
        }

        public static Components GetComponents(EntityManager m, Entity e)
        {
            Components components = new Components();
            components.entity = e;
            components.components = new List<object>();
            if (!m.Exists(e))
                return components;
#if !DOTS_DISABLE_DEBUG_NAMES
            components.name = m.GetName(e);
            components.components.Add(components.name);
#endif
            var access = m.GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var mcs = access->ManagedComponentStore;

            ecs->GetChunk(e, out var chunk, out var chunkIndex);
            if (chunk == null)
                return components;
            var archetype = chunk->Archetype;
            var types = chunk->Archetype->TypesCount;
            for (var i = 0; i < types; ++i)
            {
                var componentType = chunk->Archetype->Types[i];
                if (componentType.IsSharedComponent)
                {
                    var sharedComponentIndex = ecs->GetSharedComponentDataIndex(e, componentType.TypeIndex);
                    var sharedComponent = mcs.GetSharedComponentDataBoxed(sharedComponentIndex, componentType.TypeIndex);
                    components.components.Add(sharedComponent);
                }
                else
                {
                    ref readonly var typeInfo = ref TypeManager.GetTypeInfo(componentType.TypeIndex);
                    var type = TypeManager.GetType(typeInfo.TypeIndex);
                    var offset = archetype->Offsets[i];
                    var size = archetype->SizeOfs[i];
                    var pointer = chunk->Buffer + (offset + size * chunkIndex);
                    components.components.Add(GetComponent(pointer, type));
                }
            }
            return components;
        }
#endif //!NET_DOTS
    }


    sealed class EntityManagerDebugView
    {
#if !NET_DOTS
        private EntityManager m_target;
        public EntityManagerDebugView(EntityManager target)
        {
            m_target = target;
        }

        public Entity[] Entities
        {
            get
            {
                var entities = m_target.GetAllEntities(Allocator.Temp, EntityManager.GetAllEntitiesOptions.IncludeMeta);
                var result = entities.ToArray();
                entities.Dispose();
                return result;
            }
        }

        public struct CountAndCapacityView
        {
            public long Count;
            public long Capacity;
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public string Scope;
            public override string ToString()
            {
                float currentUsage = Count / (float)Capacity;
                //When to show % utilization. e.g. Would not be useful for worlds only using a few ten thousand entites
                float threshold = 0.01f;
                if (currentUsage > threshold)
                    return $"{Count} ({currentUsage:P2} {Scope} capacity)";

                return $"{Count}";
            }
        }

        public EntityArchetype[] EntityArchetypes
        {
            get
            {
                var entityArchetypes = new NativeList<EntityArchetype>(Allocator.Temp);
                m_target.GetAllArchetypes(entityArchetypes);

                var result = entityArchetypes.ToArray();
                entityArchetypes.Dispose();
                return result;
            }
        }

        public CountAndCapacityView NumEntities
        {
            get
            {
                var entities = m_target.GetAllEntities(Allocator.Temp, EntityManager.GetAllEntitiesOptions.IncludeMeta);
                var result = new CountAndCapacityView
                {
                    Count = entities.Length,
                    Capacity = EntityComponentStore.k_MaximumEntitiesPerWorld,
                    Scope = "World"
                };
                entities.Dispose();
                return result;
            }
        }

        public int NumChunks
        {
            get
            {
                var chunks = m_target.GetAllChunks();
                var result = chunks.Length;
                chunks.Dispose();
                return result;
            }
        }

        public CountAndCapacityView NumEntityNames
        {
            get
            {
                var result = new CountAndCapacityView
                {
                    Count = EntityNameStorage.s_State.Data.entries,
                    Capacity = EntityNameStorage.kMaxEntries,
                    Scope = "Global"
                };
                return result;
            }
        }

#endif //!NET_DOTS
    }

    sealed class WorldDebugView
    {
#if !NET_DOTS
        private World m_world;
        public WorldDebugView(World world)
        {
            m_world = world;
        }

        public List<ComponentSystemBase> AllSystems
        {
            get
            {
                var systems = m_world.Systems;
                List<ComponentSystemBase> result = new List<ComponentSystemBase>();

               for(int i = 0; i < systems.Count; i++)
                    result.Add(systems[i]);

                return result;
            }
        }

        public ComponentSystemBase InitializationSystemGroup => m_world.GetExistingSystem<InitializationSystemGroup>();
        public ComponentSystemBase SimulationSystemGroup => m_world.GetExistingSystem<SimulationSystemGroup>();
        public ComponentSystemBase PresentationSystemGroup => m_world.GetExistingSystem<PresentationSystemGroup>();
#endif //#!NET_DOTS
    }
    sealed class ArchetypeChunkDebugView
    {
#if !NET_DOTS
        private ArchetypeChunk m_ArchetypeChunk;
        public ArchetypeChunkDebugView(ArchetypeChunk ArchetypeChunk)
        {
            m_ArchetypeChunk = ArchetypeChunk;
        }

        public unsafe EntityArchetype Archetype => new EntityArchetype
        {
            Archetype = m_ArchetypeChunk.m_Chunk->Archetype,
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            _DebugComponentStore = m_ArchetypeChunk.m_EntityComponentStore
#endif
        };

        public unsafe ComponentType[] ComponentTypes
        {
            get
            {
                var chunk = m_ArchetypeChunk.m_Chunk;
                if (chunk == null)
                    return new ComponentType[0];
                var types = chunk->Archetype->TypesCount;
                var result = new ComponentType[types];
                for (var i = 0; i < types; ++i)
                {
                    var componentType = chunk->Archetype->Types[i];
                    if (componentType.IsSharedComponent)
                        continue;
                    result[i] = componentType.ToComponentType();
                }

                return result;
            }
        }

        public unsafe Entity[] Entities
        {
            get
            {
                var chunk = m_ArchetypeChunk.m_Chunk;
                if (chunk == null)
                    return new Entity[0];

                var archetype = chunk->Archetype;
                var buffer = chunk->Buffer;
                var length = m_ArchetypeChunk.Count;

                var result = new Entity[length];
                var startOffset = archetype->Offsets[0] + m_ArchetypeChunk.m_BatchStartEntityIndex * archetype->SizeOfs[0];

                var entityPtr = (Entity*) (buffer + startOffset);

                for (int i = 0; i < length; i++)
                {
                    result[i] = entityPtr[i];
                }

                return result;

            }
        }

        public unsafe SharedComponentView[] SharedComponents
        {
            get
            {
                var chunk = m_ArchetypeChunk.m_Chunk;
                if (chunk == null)
                    return new SharedComponentView[0];

                var archetype = chunk->Archetype;
                SharedComponentView[] result = new SharedComponentView[archetype->NumSharedComponents];
                var types = chunk->Archetype->TypesCount;
                int sharedIter = 0;
                for (var i = 0; i < types; ++i)
                {
                    var componentType = chunk->Archetype->Types[i];
                    if (componentType.IsSharedComponent)
                    {
                        result[sharedIter] = new SharedComponentView(componentType.TypeIndex,
                            chunk->SharedComponentValues[sharedIter]);
                        sharedIter++;
                    }
                }
                return result;
            }
        }

        public unsafe Entity ChunkComponent
        {
            get
            {
                var chunk = m_ArchetypeChunk.m_Chunk;
                if (chunk == null)
                    return Entity.Null;

                return m_ArchetypeChunk.m_Chunk->metaChunkEntity;
            }
        }

        public struct ComponentTypeVersionView
        {
            public ComponentType Type;
            public uint Version;

            public override string ToString()
            {
                return $"{Type} {Version}";
            }
        }
        public unsafe ComponentTypeVersionView[] ChangeVersions
        {
            get
            {
                var chunk = m_ArchetypeChunk.m_Chunk;
                if (chunk == null)
                    return new ComponentTypeVersionView[0];

                var archetype = chunk->Archetype;
                ComponentTypeVersionView[] result = new ComponentTypeVersionView[archetype->TypesCount];
                for (var i = 0; i < archetype->TypesCount; ++i)
                {
                    result[i].Type = archetype->Types[i].ToComponentType();
                    result[i].Version = chunk->GetChangeVersion(i);
                }

                return result;
            }
        }

        public unsafe uint OrderVersion
        {
            get
            {
                var chunk = m_ArchetypeChunk.m_Chunk;
                if (chunk == null)
                    return 0;
                return chunk->GetOrderVersion();

            }
        }
#endif //!NET_DOTS
    }

    sealed class EntityArchetypeDebugView
    {
#if !NET_DOTS
        private EntityArchetype m_EntityArchetype;
        public EntityArchetypeDebugView(EntityArchetype entityArchetype)
        {
            m_EntityArchetype = entityArchetype;
        }

        public unsafe ComponentType[] ComponentTypes
        {
            get
            {
                var archetype = m_EntityArchetype.Archetype;
                if (archetype == null)
                    return new ComponentType[0];
                var types = archetype->TypesCount;

                var result = new ComponentType[types];
                for (var i = 0; i < types; ++i)
                {
                    result[i] = archetype->Types[i].ToComponentType();
                }
                return result;
            }
        }

        public struct ChunkReport
        {
            public float AvgUtilization;
            public float WorstUtilization;
            public List<float> PerChunk;

            public override string ToString()
            {
                return $"{AvgUtilization * 100}%";
            }
        }
        public unsafe ChunkReport ChunkUtilization
        {
            get
            {
                var archetype = m_EntityArchetype.Archetype;
                if (archetype == null)
                    return new ChunkReport();

                var numChunks = m_EntityArchetype.ChunkCount;

                if (numChunks <= 0)
                    return new ChunkReport();

                var result = new ChunkReport
                {
                    PerChunk = new List<float>(numChunks),
                    WorstUtilization = archetype->Chunks[0]->Count / (float) archetype->Chunks[0]->Capacity
                };

                for (var i = 0; i < numChunks; ++i)
                {
                    var avg = archetype->Chunks[i]->Count / (float)archetype->Chunks[i]->Capacity;
                    result.PerChunk.Add(avg);
                    result.AvgUtilization += avg;
                    if (avg < result.WorstUtilization)
                        result.WorstUtilization = avg;
                }

                result.AvgUtilization /= numChunks;
                return result;
            }
        }

        public unsafe List<ArchetypeChunk> ArchetypeChunks
        {
            get
            {
                List<ArchetypeChunk> result = new List<ArchetypeChunk>();
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                var archetype = m_EntityArchetype.Archetype;
                var chunkCount = archetype->Chunks.Count;
                for (int i = 0; i < chunkCount; i++)
                {
                    var srcChunk = archetype->Chunks[i];
                    result.Add(new ArchetypeChunk(srcChunk, m_EntityArchetype._DebugComponentStore));
                }
#endif
                return result;
            }
        }

        public unsafe int[] Offsets
        {
            get
            {
                var archetype = m_EntityArchetype.Archetype;
                if (archetype == null)
                    return new int[0];
                int[] result = new int[archetype->TypesCount];
                Marshal.Copy((IntPtr)archetype->Offsets, result, 0, archetype->TypesCount);
                return result;
            }
        }

        public unsafe int[] SizeOfs
        {
            get
            {
                var archetype = m_EntityArchetype.Archetype;
                if (archetype == null)
                    return new int[0];
                int[] result = new int[archetype->TypesCount];
                Marshal.Copy((IntPtr)archetype->SizeOfs, result, 0, archetype->TypesCount);
                return result;
            }
        }

        public unsafe int[] TypeMemoryOrder
        {
            get
            {
                var archetype = m_EntityArchetype.Archetype;
                if (archetype == null)
                    return new int[0];
                int[] result = new int[archetype->TypesCount];
                Marshal.Copy((IntPtr)archetype->TypeMemoryOrder, result, 0, archetype->TypesCount);
                return result;
            }
        }
#endif //!NET_DOTS
    }

    sealed class EntityQueryDebugView
    {
#if !NET_DOTS
        private EntityQuery m_EntityQuery;

        public EntityQueryDesc Desc
        {
            get
            {
                unsafe
                {
                    var impl = m_EntityQuery._GetImpl();
                    return impl == null ? default : impl->GetEntityQueryDesc();
                }
            }
        }

        public unsafe Entity[] MatchingEntities
        {
            get
            {
                var impl = m_EntityQuery._GetImpl();
                if (impl == null)
                    return new Entity[0];

                var entities = impl->ToEntityArrayImmediate(Allocator.TempJob, m_EntityQuery);
                var result = entities.ToArray();
                entities.Dispose();

                return result;
            }
        }

        public unsafe List<ArchetypeChunk> MatchingChunks
        {
            get
            {
                var impl = m_EntityQuery._GetImpl();
                if (impl == null)
                    return default;
                // We explicitly do NOT access the MatchingChunkCache here, or anything that might implicitly trigger
                // a chunk cache update. Debug views should never mutate the objects they're viewing!
                // TODO(https://unity3d.atlassian.net/browse/DOTS-4623): based on the resolution to this issue, this API usage may need to change.
                var chunkIterator = m_EntityQuery.GetArchetypeChunkIterator();
                var chunkList = new List<ArchetypeChunk>();
                while (chunkIterator.MoveNext())
                {
                    chunkList.Add(chunkIterator.CurrentArchetypeChunk);
                }
                return chunkList;
            }
        }

        public unsafe bool IsMatchingChunkCacheValid
        {
            get
            {
                var impl = m_EntityQuery._GetImpl();
                if (impl == null)
                    return false;
                return impl->_QueryData->MatchingChunkCache.IsCacheValid;
            }
        }

        public unsafe EntityQueryFilter.ChangedFilter ChangedFilter
        {
            get
            {
                var impl = m_EntityQuery._GetImpl();
                return impl == null ? default : impl->_Filter.Changed;
            }
        }

        public unsafe SharedComponentView[] SharedFilter
        {
            get
            {
                var impl = m_EntityQuery._GetImpl();
                if (impl == null)
                    return default;
                int numFilterValues = impl->_Filter.Shared.Count;
                SharedComponentView[] result = new SharedComponentView[numFilterValues];
                for (var i = 0; i < numFilterValues; ++i)
                {
                    int valueIndex = impl->_Filter.Shared.SharedComponentIndex[i];
                    int indexInQuery = impl->_Filter.Shared.IndexInEntityQuery[i];
                    var typeIndex = impl->_QueryData->RequiredComponents[indexInQuery].TypeIndex;
                    result[i] = new SharedComponentView(typeIndex, valueIndex);
                }
                return result;
            }
        }

        public EntityQueryDebugView(EntityQuery query)
        {
            m_EntityQuery = query;
        }

#endif //!NET_DOTS
    }

    struct SystemView
    {
#if !NET_DOTS
        public readonly SystemHandleUntyped m_unmanagedSystem;
        public readonly ComponentSystemBase m_managedSystem;

        public SystemView(SystemHandleUntyped mUnmanagedSystem)
        {
            m_unmanagedSystem = mUnmanagedSystem;
            m_managedSystem = null;
        }

        public SystemView(ComponentSystemBase mManagedSystem)
        {
            m_managedSystem = mManagedSystem;
            m_unmanagedSystem = new SystemHandleUntyped();
        }

        public override string ToString()
        {
            string result = "";

            if (m_managedSystem != null)
            {
                result = "(Managed) " + m_managedSystem.GetType().Name;
            }
            else
            {
                //find the system's world
                World world = null;
                foreach (var w in World.s_AllWorlds)
                {
                    if (w.SequenceNumber == m_unmanagedSystem.m_WorldSeqNo)
                        world = w;
                }

                if (world == null)
                    result = "";
                else
                {
                    result = "(Unmanaged) " + world.Unmanaged.GetTypeOfSystem(m_unmanagedSystem).Name;
                }

            }

            return result;
        }
#endif //!NET_DOTS
    }

    sealed class ComponentSystemGroupDebugView
    {
#if !NET_DOTS
        private ComponentSystemGroup m_componentSystemGroup;

        public ComponentSystemGroupDebugView(ComponentSystemGroup mComponentSystemGroup)
        {
            m_componentSystemGroup = mComponentSystemGroup;
        }

        public SystemView[] Systems
        {
            get
            {
                var numSystems = m_componentSystemGroup.m_MasterUpdateList.Length;
                var systems = new SystemView[numSystems];

                for (int i = 0; i < numSystems; i++)
                {
                    var updateIndex = m_componentSystemGroup.m_MasterUpdateList[i];
                    if (updateIndex.IsManaged)
                    {
                        systems[i] = new SystemView(m_componentSystemGroup.m_systemsToUpdate[updateIndex.Index]);
                    }
                    else
                    {
                        systems[i] = new SystemView(m_componentSystemGroup.m_UnmanagedSystemsToUpdate[updateIndex.Index]);
                    }
                }

                return systems;
            }
        }

        public bool Enabled => m_componentSystemGroup.Enabled;
        public bool EnableSystemSorting => m_componentSystemGroup.EnableSystemSorting;
        public World World => m_componentSystemGroup.World;
#endif //!NET_DOTS
    }

    sealed class SystemStateDebugView
    {
#if !NET_DOTS
        private SystemState m_systemState;

        public SystemStateDebugView(SystemState systemState)
        {
            m_systemState = systemState;
        }

        public List<EntityQuery> EntityQueries
        {
            get
            {
                var unsafeQueries = m_systemState.EntityQueries;
                var result = new List<EntityQuery>();
                foreach(var query in unsafeQueries)
                    result.Add(query);

                return result;
            }
        }
        public List<ComponentType> ReadOnlyTypes
        {
            get
            {
                var result = new List<ComponentType>();


                foreach (var type in m_systemState.m_JobDependencyForReadingSystems)
                {
                    result.Add(ComponentType.ReadOnly(type));
                }

                return result;
            }
        }
        public List<ComponentType> ReadWriteTypes
        {
            get
            {
                var result = new List<ComponentType>();

                foreach (var type in m_systemState.m_JobDependencyForWritingSystems)
                {
                    result.Add(ComponentType.ReadWrite(type));
                }

                return result;
            }
        }

#endif //!NET_DOTS
    }

}
