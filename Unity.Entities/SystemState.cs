using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Profiling;

namespace Unity.Entities
{
    /// <summary>
    /// Contains raw entity system state. Used by unmanaged systems (ISystemBase) as well as managed systems behind the scenes.
    /// </summary>
    [BurstCompatible(RequiredUnityDefine = "UNITY_2020_2_OR_NEWER && !UNITY_DOTSRUNTIME")]
    public unsafe struct SystemState
    {
        // For unmanaged systems, points to user struct that was allocated to front this system state
        internal void* m_SystemPtr;

        private UnsafeList m_EntityQueries;
        private UnsafeList m_RequiredEntityQueries;

        /// <summary>
        /// Return a debug name for unmanaged systems.
        /// </summary>
        public FixedString64 DebugName => UnmanagedMetaIndex >= 0 ? SystemBaseRegistry.GetDebugName(UnmanagedMetaIndex) : default;

        internal ref UnsafeList<EntityQuery> EntityQueries
        {
            get
            {
                fixed(void* ptr = &m_EntityQueries)
                {
                    return ref UnsafeUtility.AsRef<UnsafeList<EntityQuery>>(ptr);
                }
            }
        }

        internal ref UnsafeList<EntityQuery> RequiredEntityQueries
        {
            get
            {
                fixed(void* ptr = &m_RequiredEntityQueries)
                {
                    return ref UnsafeUtility.AsRef<UnsafeList<EntityQuery>>(ptr);
                }
            }
        }

        internal UnsafeIntList m_JobDependencyForReadingSystems;
        internal UnsafeIntList m_JobDependencyForWritingSystems;

        internal uint m_LastSystemVersion;

        internal EntityManager m_EntityManager;
        internal EntityComponentStore* m_EntityComponentStore;
        internal ComponentDependencyManager* m_DependencyManager;
        internal GCHandle m_World;
        internal WorldUnmanaged m_WorldUnmanaged;

        internal bool m_AlwaysUpdateSystem;
        internal bool m_Enabled;
        internal bool m_PreviouslyEnabled;

        // SystemBase stuff
        internal bool      m_GetDependencyFromSafetyManager;
        internal JobHandle m_JobHandle;

    #if ENABLE_PROFILER
        internal ProfilerMarker m_ProfilerMarker;
    #endif

        internal int                        m_SystemID;

        /// <summary>
        /// Controls whether this system executes when its OnUpdate function is called.
        /// </summary>
        /// <value>True, if the system is enabled.</value>
        /// <remarks>The Enabled property is intended for debugging so that you can easily turn on and off systems
        /// from the Entity Debugger window. A system with Enabled set to false will not update, even if its
        /// <see cref="ShouldRunSystem"/> function returns true.</remarks>
        public bool Enabled { get => m_Enabled; set => m_Enabled = value; }

        /// <summary>
        /// The current change version number in this <see cref="World"/>.
        /// </summary>
        /// <remarks>The system updates the component version numbers inside any <see cref="ArchetypeChunk"/> instances
        /// that this system accesses with write permissions to this value.</remarks>
        public uint GlobalSystemVersion => m_EntityComponentStore->GlobalSystemVersion;

        /// <summary>
        /// The current version of this system.
        /// </summary>
        /// <remarks>
        /// LastSystemVersion is updated to match the <see cref="GlobalSystemVersion"/> whenever a system runs.
        ///
        /// When you use <seealso cref="EntityQuery.SetChangedVersionFilter(ComponentType)"/>
        /// or <seealso cref="ArchetypeChunk.DidChange"/>, LastSystemVersion provides the basis for determining
        /// whether a component could have changed since the last time the system ran.
        ///
        /// When a system accesses a component and has write permission, it updates the change version of that component
        /// type to the current value of LastSystemVersion. The system updates the component type's version whether or not
        /// it actually modifies data in any instances of the component type -- this is one reason why you should
        /// specify read-only access to components whenever possible.
        ///
        /// For efficiency, ECS tracks the change version of component types by chunks, not by individual entities. If a system
        /// updates the component of a given type for any entity in a chunk, then ECS assumes that the components of all
        /// entities in that chunk could have been changed. Change filtering allows you to save processing time by
        /// skipping all entities in an unchanged chunk, but does not support skipping individual entities in a chunk
        /// that does contain changes.
        /// </remarks>
        /// <value>The <see cref="GlobalSystemVersion"/> the last time this system ran.</value>
        public uint LastSystemVersion => m_LastSystemVersion;

        /// <summary>
        /// The EntityManager object of the <see cref="World"/> in which this system exists.
        /// </summary>
        /// <value>The EntityManager for this system.</value>
        public EntityManager EntityManager => m_EntityManager;

        /// <summary>
        /// The World in which this system exists.
        /// </summary>
        /// <value>The World of this system.</value>
        [NotBurstCompatible]
        public World World => (World)m_World.Target;

        /// <summary>
        /// The unmanaged portion of the world in which this system exists.
        /// </summary>
        /// <value>The unmanaged world of this system.</value>
        public WorldUnmanaged WorldUnmanaged => m_WorldUnmanaged;

        /// <summary>
        /// The current Time data for this system's world.
        /// </summary>
        public ref readonly TimeData Time => ref WorldUnmanaged.CurrentTime;

        // Used by managed systems to heap allocate a system state when they are created.
        // Unmanaged systems use a separate allocator optimized for speed, found in World.cs
        internal static SystemState* Allocate()
        {
            void* result = UnsafeUtility.Malloc(sizeof(SystemState), 16, Allocator.Persistent);
            UnsafeUtility.MemClear(result, sizeof(SystemState));
            return (SystemState*)result;
        }

        internal static void Deallocate(SystemState* state)
        {
            UnsafeUtility.Free(state, Allocator.Persistent);
        }

        // Managed systems call this function to initialize their backing system state
        [NotBurstCompatible] // Because world
        internal void InitManaged(World world, Type managedType)
        {
            UnmanagedMetaIndex = -1;

            CommonInit(world);

            if (managedType != null)
            {
#if !UNITY_DOTSRUNTIME
                m_AlwaysUpdateSystem = Attribute.IsDefined(managedType, typeof(AlwaysUpdateSystemAttribute), true);
#else
                var attrs = TypeManager.GetSystemAttributes(managedType, typeof(AlwaysUpdateSystemAttribute));
                if (attrs.Length > 0)
                    m_AlwaysUpdateSystem = true;
#endif
            }

#if ENABLE_PROFILER
            m_ProfilerMarker = new Profiling.ProfilerMarker($"{world.Name} {TypeManager.GetSystemName(managedType)}");
#endif
        }

        // Initialization common to managed and unmanaged systems
        private void CommonInit(World world)
        {
            m_Enabled = true;
            m_SystemID = World.AllocateSystemID();
            m_World = GCHandle.Alloc(world);
            m_WorldUnmanaged = world.Unmanaged;
            m_EntityManager = world.EntityManager;
            m_EntityComponentStore = m_EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore;
            m_DependencyManager = m_EntityManager.GetCheckedEntityDataAccess()->DependencyManager;

            EntityQueries = new UnsafeList<EntityQuery>(0, Allocator.Persistent);
            RequiredEntityQueries = new UnsafeList<EntityQuery>(0, Allocator.Persistent);

            m_JobDependencyForReadingSystems = new UnsafeIntList(0, Allocator.Persistent);
            m_JobDependencyForWritingSystems = new UnsafeIntList(0, Allocator.Persistent);

            m_AlwaysUpdateSystem = false;
        }

        // Unmanaged systems call this function to initialize their backing system state
        [NotBurstCompatible]
        internal void InitUnmanaged(World world, int unmanagedMetaIndex, void* systemptr)
        {
            m_Enabled = true;
            UnmanagedMetaIndex = unmanagedMetaIndex;
            m_SystemPtr = systemptr;

            CommonInit(world);

#if ENABLE_PROFILER
            m_ProfilerMarker = new Profiling.ProfilerMarker($"{world.Name} {SystemBaseRegistry.GetDebugName(UnmanagedMetaIndex)}");
#endif

            // TODO: AlwaysUpdate reflection code needs to go here as for managed, or be backed into systembaseregistry and/or typemanager
        }

        [NotBurstCompatible]
        internal void Dispose()
        {
            DisposeQueries(ref EntityQueries);
            DisposeQueries(ref RequiredEntityQueries);

            EntityQueries.Dispose();
            EntityQueries = default;

            RequiredEntityQueries.Dispose();
            RequiredEntityQueries = default;

            if (m_World.IsAllocated)
            {
                m_World.Free();
            }

            m_JobDependencyForReadingSystems.Dispose();
            m_JobDependencyForWritingSystems.Dispose();
        }

        private void DisposeQueries(ref UnsafeList<EntityQuery> queries)
        {
            for (var i = 0; i < queries.Length; ++i)
            {
                var query = queries[i];

                if (m_EntityManager.IsQueryValid(query))
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    query._GetImpl()->_DisallowDisposing = false;
#endif
                    query.Dispose();
                }
            }
        }

        /// <summary>
        /// The ECS-related data dependencies of the system.
        /// </summary>
        /// <remarks>
        /// Before <see cref="ISystemState.OnUpdate"/>, the Dependency property represents the combined job handles of any job that
        /// writes to the same components that the current system reads -- or reads the same components that the current
        /// system writes to. When you use [Entities.ForEach] or [Job.WithCode], the system uses the Dependency property
        /// to specify a job’s dependencies when scheduling it. The system also combines the new job's [JobHandle]
        /// with Dependency so that any subsequent job scheduled in the system depends on the earlier jobs (in sequence).
        ///
        /// You can opt out of this default dependency management by explicitly passing a [JobHandle] to
        /// [Entities.ForEach] or [Job.WithCode]. When you pass in a [JobHandle], these constructions also return a
        /// [JobHandle] representing the input dependencies combined with the new job. The [JobHandle] objects of any
        /// jobs scheduled with explicit dependencies are not combined with the system’s Dependency property. You must set the Dependency
        /// property manually to make sure that later systems receive the correct job dependencies.
        ///
        /// You can combine implicit and explicit dependency management (by using [JobHandle.CombineDependencies]);
        /// however, doing so can be error prone. When you set the Dependency property, the assigned [JobHandle]
        /// replaces any existing dependency, it is not combined with them.
        ///
        /// Note that the default, implicit dependency management does not include <see cref="IJobChunk"/> jobs.
        /// You must manage the dependencies for IJobChunk explicitly.
        ///
        /// [JobHandle]: https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.html
        /// [JobHandle.CombineDependencies]: https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.CombineDependencies.html
        /// [Entities.ForEach]: xref:ecs-entities-foreach
        /// [Job.WithCode]: xref:ecs-entities-foreach
        /// </remarks>
        public JobHandle Dependency
        {
            get
            {
                if (m_GetDependencyFromSafetyManager)
                {
                    var depMgr = m_DependencyManager;
                    m_GetDependencyFromSafetyManager = false;
                    m_JobHandle = depMgr->GetDependency(m_JobDependencyForReadingSystems.Ptr,
                        m_JobDependencyForReadingSystems.Length, m_JobDependencyForWritingSystems.Ptr,
                        m_JobDependencyForWritingSystems.Length);
                }

                return m_JobHandle;
            }
            set
            {
                m_GetDependencyFromSafetyManager = false;
                m_JobHandle = value;
            }
        }

        /// <summary>
        /// Return the unmanaged type index of the system (>= 0 for ISystemBase-type systems), or -1 for managed systems.
        /// </summary>
        public int UnmanagedMetaIndex { get; private set; }

        public void CompleteDependency()
        {
            // Previous frame job
            m_JobHandle.Complete();

            // We need to get more job handles from other systems
            if (m_GetDependencyFromSafetyManager)
            {
                m_GetDependencyFromSafetyManager = false;
                CompleteDependencyInternal();
            }
        }

        internal void CompleteDependencyInternal()
        {
            m_DependencyManager->CompleteDependenciesNoChecks(m_JobDependencyForReadingSystems.Ptr,
                m_JobDependencyForReadingSystems.Length, m_JobDependencyForWritingSystems.Ptr,
                m_JobDependencyForWritingSystems.Length);
        }

        internal void BeforeUpdateVersioning()
        {
            m_EntityComponentStore->IncrementGlobalSystemVersion();
            ref var qs = ref EntityQueries;
            for (int i = 0; i < qs.Length; ++i)
            {
                qs[i].SetChangedFilterRequiredVersion(m_LastSystemVersion);
            }
        }

        internal void BeforeOnUpdate()
        {
            BeforeUpdateVersioning();

            // We need to wait on all previous frame dependencies, otherwise it is possible that we create infinitely long dependency chains
            // without anyone ever waiting on it
            m_JobHandle.Complete();
            m_GetDependencyFromSafetyManager = true;
        }

#pragma warning disable 649
        private unsafe struct JobHandleData
        {
            public void* jobGroup;
            public int version;
        }
#pragma warning restore 649

        internal void AfterOnUpdate()
        {
            AfterUpdateVersioning();

            var depMgr = m_DependencyManager;

            // If outputJob says no relevant jobs were scheduled,
            // then no need to batch them up or register them.
            // This is a big optimization if we only Run methods on main thread...
            var outputJob = m_JobHandle;
            if (((JobHandleData*)&outputJob)->jobGroup != null)
            {
                JobHandle.ScheduleBatchedJobs();
                m_JobHandle = depMgr->AddDependency(m_JobDependencyForReadingSystems.Ptr,
                    m_JobDependencyForReadingSystems.Length, m_JobDependencyForWritingSystems.Ptr,
                    m_JobDependencyForWritingSystems.Length, outputJob);
            }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void CheckSafety(ref SystemDependencySafetyUtility.SafetyErrorDetails details, ref bool errorFound)
        {
            var depMgr = m_DependencyManager;
            if (JobsUtility.JobDebuggerEnabled)
            {
                var dependencyError = SystemDependencySafetyUtility.CheckSafetyAfterUpdate(ref m_JobDependencyForReadingSystems, ref m_JobDependencyForWritingSystems, depMgr, out details);

                if (dependencyError)
                {
                    SystemDependencySafetyUtility.EmergencySyncAllJobs(ref m_JobDependencyForReadingSystems, ref m_JobDependencyForWritingSystems, depMgr);
                }

                errorFound = dependencyError;
            }
        }

#endif

        internal void AfterUpdateVersioning()
        {
            m_LastSystemVersion = m_EntityComponentStore->GlobalSystemVersion;
        }

        internal bool ShouldRunSystem()
        {
            if (m_AlwaysUpdateSystem)
                return true;

            ref var required = ref RequiredEntityQueries;

            if (required.Length > 0)
            {
                for (int i = 0; i != required.Length; i++)
                {
                    EntityQuery query = required[i];
                    if (query.IsEmptyIgnoreFilter)
                        return false;
                }

                return true;
            }
            else
            {
                // Systems without queriesDesc should always run. Specifically,
                // IJobForEach adds its queriesDesc the first time it's run.
                ref var eqs = ref EntityQueries;
                var length = eqs.Length;
                if (length == 0)
                    return true;

                // If all the queriesDesc are empty, skip it.
                // (There’s no way to know what the key value is without other markup)
                for (int i = 0; i != length; i++)
                {
                    EntityQuery query = eqs[i];
                    if (!query.IsEmptyIgnoreFilter)
                        return true;
                }

                return false;
            }
        }

        [NotBurstCompatible] // We need to fix up EntityQueryManager
        internal EntityQuery GetEntityQueryInternal(ComponentType* componentTypes, int count)
        {
            ref var handles = ref EntityQueries;

            for (var i = 0; i != handles.Length; i++)
            {
                var query = handles[i];

                if (query.CompareComponents(componentTypes, count))
                    return query;
            }

            var newQuery = EntityManager.CreateEntityQuery(componentTypes, count);

            AddReaderWriters(newQuery);
            AfterQueryCreated(newQuery);

            return newQuery;
        }

        internal void AddReaderWriter(ComponentType componentType)
        {
            if (CalculateReaderWriterDependency.Add(componentType, ref m_JobDependencyForReadingSystems, ref m_JobDependencyForWritingSystems))
            {
                CompleteDependencyInternal();
            }
        }

        internal void AddReaderWriters(EntityQuery query)
        {
            if (query.AddReaderWritersToLists(ref m_JobDependencyForReadingSystems, ref m_JobDependencyForWritingSystems))
            {
                CompleteDependencyInternal();
            }
        }

        private void AfterQueryCreated(EntityQuery query)
        {
            query.SetChangedFilterRequiredVersion(m_LastSystemVersion);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            query._GetImpl()->_DisallowDisposing = true;
#endif

            EntityQueries.Add(query);
        }

        [NotBurstCompatible] // We need to fix up EntityQueryManager
        internal EntityQuery GetSingletonEntityQueryInternal(ComponentType type)
        {
            ref var handles = ref EntityQueries;

            for (var i = 0; i != handles.Length; i++)
            {
                var query = handles[i];
                var queryData = query._GetImpl()->_QueryData;

                // EntityQueries are constructed including the Entity ID
                if (2 != queryData->RequiredComponentsCount)
                    continue;

                if (queryData->RequiredComponents[1] != type)
                    continue;

                return query;
            }

            var newQuery = EntityManager.CreateEntityQuery(&type, 1);

            AddReaderWriters(newQuery);
            AfterQueryCreated(newQuery);

            return newQuery;
        }

        [NotBurstCompatible]
        internal EntityQuery GetEntityQueryInternal(EntityQueryDesc[] desc)
        {
            ref var handles = ref EntityQueries;

            for (var i = 0; i != handles.Length; i++)
            {
                var query = handles[i];

                if (query.CompareQuery(desc))
                    return query;
            }

            var newQuery = EntityManager.CreateEntityQuery(desc);

            AddReaderWriters(newQuery);
            AfterQueryCreated(newQuery);

            return newQuery;
        }

        /// <summary>
        /// Gets the cached query for the specified component types, if one exists; otherwise, creates a new query
        /// instance and caches it.
        /// </summary>
        /// <param name="componentTypes">An array or comma-separated list of component types.</param>
        /// <returns>The new or cached query.</returns>
        [NotBurstCompatible]
        public EntityQuery GetEntityQuery(params ComponentType[] componentTypes)
        {
            fixed (ComponentType* types = componentTypes)
            {
                return GetEntityQueryInternal(types, componentTypes.Length);
            }
        }

        /// <summary>
        /// Gets the cached query for the specified component types, if one exists; otherwise, creates a new query
        /// instance and caches it.
        /// </summary>
        /// <param name="componentTypes">An array of component types.</param>
        /// <returns>The new or cached query.</returns>
        [NotBurstCompatible]
        public EntityQuery GetEntityQuery(NativeArray<ComponentType> componentTypes)
        {
            return GetEntityQueryInternal((ComponentType*)componentTypes.GetUnsafeReadOnlyPtr(), componentTypes.Length);
        }

        /// <summary>
        /// Combines an array of query description objects into a single query.
        /// </summary>
        /// <remarks>This function looks for a cached query matching the combined query descriptions, and returns it
        /// if one exists; otherwise, the function creates a new query instance and caches it.</remarks>
        /// <returns>The new or cached query.</returns>
        /// <param name="queryDesc">An array of query description objects to be combined to define the query.</param>
        [NotBurstCompatible]
        public EntityQuery GetEntityQuery(params EntityQueryDesc[] queryDesc)
        {
            return GetEntityQuery(queryDesc);
        }

        /// <summary>
        /// Gets the run-time type information required to access an array of component data in a chunk.
        /// </summary>
        /// <param name="isReadOnly">Whether the component data is only read, not written. Access components as
        /// read-only whenever possible.</param>
        /// <typeparam name="T">A struct that implements <see cref="IComponentData"/>.</typeparam>
        /// <returns>An object representing the type information required to safely access component data stored in a
        /// chunk.</returns>
        /// <remarks>Pass an <see cref="ComponentTypeHandle{T}"/> instance to a job that has access to chunk data,
        /// such as an <see cref="IJobChunk"/> job, to access that type of component inside the job.</remarks>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public ComponentTypeHandle<T> GetComponentTypeHandle<T>(bool isReadOnly = false) where T : struct, IComponentData
        {
            AddReaderWriter(isReadOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>());
            return EntityManager.GetComponentTypeHandle<T>(isReadOnly);
        }

        /// <summary>
        /// Gets the run-time type information required to access an array of component data in a chunk.
        /// </summary>
        /// <param name="componentType">Type of the component</param>
        /// <returns>An object representing the type information required to safely access component data stored in a
        /// chunk.</returns>
        /// <remarks>Pass an DynamicComponentTypeHandle instance to a job that has access to chunk data, such as an
        /// <see cref="IJobChunk"/> job, to access that type of component inside the job.</remarks>
        public DynamicComponentTypeHandle GetDynamicComponentTypeHandle(ComponentType componentType)
        {
            AddReaderWriter(componentType);
            return EntityManager.GetDynamicComponentTypeHandle(componentType);
        }

        /// <summary>
        /// Gets the run-time type information required to access an array of buffer components in a chunk.
        /// </summary>
        /// <param name="isReadOnly">Whether the data is only read, not written. Access data as
        /// read-only whenever possible.</param>
        /// <typeparam name="T">A struct that implements <see cref="IBufferElementData"/>.</typeparam>
        /// <returns>An object representing the type information required to safely access buffer components stored in a
        /// chunk.</returns>
        /// <remarks>Pass a BufferTypeHandle instance to a job that has access to chunk data, such as an
        /// <see cref="IJobChunk"/> job, to access that type of buffer component inside the job.</remarks>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleBufferElement) })]
        public BufferTypeHandle<T> GetBufferTypeHandle<T>(bool isReadOnly = false)
            where T : struct, IBufferElementData
        {
            AddReaderWriter(isReadOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>());
            return EntityManager.GetBufferTypeHandle<T>(isReadOnly);
        }

        /// <summary>
        /// Gets the run-time type information required to access a shared component data in a chunk.
        /// </summary>
        /// <typeparam name="T">A struct that implements <see cref="ISharedComponentData"/>.</typeparam>
        /// <returns>An object representing the type information required to safely access shared component data stored in a
        /// chunk.</returns>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleSharedComponentData) })]
        public SharedComponentTypeHandle<T> GetSharedComponentTypeHandle<T>()
            where T : struct, ISharedComponentData
        {
            return EntityManager.GetSharedComponentTypeHandle<T>();
        }

        /// <summary>
        /// Gets the run-time type information required to access the array of <see cref="Entity"/> objects in a chunk.
        /// </summary>
        /// <returns>An object representing the type information required to safely access Entity instances stored in a
        /// chunk.</returns>
        public EntityTypeHandle GetEntityTypeHandle()
        {
            return EntityManager.GetEntityTypeHandle();
        }
    }
}
