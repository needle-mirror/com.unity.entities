using System;
using System.Reflection;
using Unity.Collections;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace Unity.Entities
{

    [DebuggerTypeProxy(typeof(IntListDebugView))]
    internal unsafe struct IntList
    {
        public int* p;
        public int Count;
        public int Capacity;
    }

    /// <summary>
    /// Provides the base functionality common to component system classes.
    /// </summary>
    /// <remarks>
    /// A component system provides the behavior in an ECS architecture.
    ///
    /// A typical system operates on a set of entities which have specific components. The system identifies
    /// the components of interest using an <see cref="EntityQuery"/> (JobComponentSystem) or
    /// <see cref="EntityQueryBuilder"/> (ComponentSystem). The system then iterates over the selected entities, reading
    /// and writing data to components, and performing other entity operations as appropriate.
    /// </remarks>
    public unsafe abstract partial class ComponentSystemBase
    {
        EntityQuery[] m_EntityQueries;
        EntityQuery[] m_RequiredEntityQueries;

        internal IntList m_JobDependencyForReadingSystems;
        internal IntList m_JobDependencyForWritingSystems;

        uint m_LastSystemVersion;

        internal ComponentJobSafetyManager* m_SafetyManager;
        internal EntityManager m_EntityManager;
        World m_World;

        bool m_AlwaysUpdateSystem;
        internal bool m_PreviouslyEnabled;

        /// <summary>
        /// Controls whether this system executes when its `OnUpdate` function is called.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// The query objects cached by this system.
        /// </summary>
        /// <remarks>A system caches any queries it implicitly creates through the IJob interfaces or
        /// <see cref="EntityQueryBuilder"/> and that you create explicitly by calling <see cref="GetEntityQuery"/>.
        /// Implicit queries may be created lazily and not exist before a system has run for the first time.</remarks>
        /// <value>A read-only array of the cached <see cref="EntityQuery"/> objects.</value>
        public EntityQuery[] EntityQueries => m_EntityQueries;

        /// <summary>
        /// The current change version number in this <see cref="World"/>.
        /// </summary>
        /// <remarks>The system updates the component version numbers inside any <see cref="ArchetypeChunk"/> instances
        /// that this system accesses with write permissions to this value.</remarks>
        public uint GlobalSystemVersion => m_EntityManager.GlobalSystemVersion;

        /// <summary>
        /// The <see cref="GlobalSystemVersion"/> the last time this system ran.
        /// </summary>
        /// <remarks>
        /// When a system accesses a component and has write permission, it updates the change version of that component
        /// type to the current value of `LastSystemVersion`. When you use <seealso cref="EntityQuery.SetFilterChanged"/>
        /// or <seealso cref="ArchetypeChunk.DidChange"/>, `LastSystemVersion` provides the basis for determining
        /// whether a component could have changed since the last time the system ran.
        ///
        /// **Note:** For efficiency, ECS tracks the change version by chunks, not by individual entities. If a system
        /// updates the component of a given type for any entity in a chunk, then ECS assumes that the components of all
        /// entities in that chunk could have been changed. Change filtering allows you to save processing time by
        /// skipping entities chunk by chunk, but not entity by entity.
        /// </remarks>
        /// <value></value>
        public uint LastSystemVersion => m_LastSystemVersion;

        /// <summary>
        /// The EntityManager object of the <see cref="World"/> in which this system exists.
        /// </summary>
        public EntityManager EntityManager => m_EntityManager;

        /// <summary>
        /// The World in which this system exists.
        /// </summary>
        public World World => m_World;

        // ============

#if UNITY_EDITOR
        private UnityEngine.Profiling.CustomSampler m_Sampler;
#endif

#if !NET_DOTS
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private static HashSet<Type> s_ObsoleteAPICheckedTypes = new HashSet<Type>();

        void CheckForObsoleteAPI()
        {
            var type = this.GetType();
            while (type != typeof(ComponentSystemBase))
            {
                if (s_ObsoleteAPICheckedTypes.Contains(type))
                    break;

                if (type.GetMethod("OnCreateManager", BindingFlags.DeclaredOnly | BindingFlags.Instance) != null)
                {
                    Debug.LogWarning($"The OnCreateManager overload in {type} is obsolete; please rename it to OnCreate.  OnCreateManager will stop being called in a future release.");
                }

                if (type.GetMethod("OnDestroyManager", BindingFlags.DeclaredOnly | BindingFlags.Instance) != null)
                {
                    Debug.LogWarning($"The OnDestroyManager overload in {type} is obsolete; please rename it to OnDestroy.  OnDestroyManager will stop being called in a future release.");
                }

                s_ObsoleteAPICheckedTypes.Add(type);

                type = type.BaseType;
            }
        }

        /// <summary>
        /// Base class constructor that should be called by subclasses.
        /// </summary>
        protected ComponentSystemBase()
        {
             CheckForObsoleteAPI();
        }
#endif
#endif

        internal void CreateInstance(World world)
        {
            OnBeforeCreateInternal(world);
            try
            {
                OnCreateManager(); // DELETE after obsolete period!
                OnCreate();
#if UNITY_EDITOR
                var type = GetType();
                m_Sampler = UnityEngine.Profiling.CustomSampler.Create($"{world.Name} {type.FullName}");
#endif
            }
            catch
            {
                OnBeforeDestroyInternal();
                OnAfterDestroyInternal();
                throw;
            }
        }

        internal void DestroyInstance()
        {
            OnBeforeDestroyInternal();
            OnDestroy();
            OnDestroyManager(); // DELETE after obsolete period!
            OnAfterDestroyInternal();
        }

        /// <summary>
        /// Deprecated. Use <see cref="OnCreate"/>.
        /// </summary>
        protected virtual void OnCreateManager()
        {
        }

        /// <summary>
        /// Deprecated. Use <see cref="OnDestroy"/>.
        /// </summary>
        protected virtual void OnDestroyManager()
        {
        }

        /// <summary>
        /// Called when this system is created.
        /// </summary>
        /// <remarks>
        /// Implement an `OnCreate()` function to set up system resources when it is created.
        ///
        /// OnCreate is invoked before the the first time <see cref="OnStartRunning"/> and <see cref="OnUpdate"/> are invoked.
        /// </remarks>
        protected virtual void OnCreate()
        {
        }

        /// <summary>
        /// Called before the first call to OnUpdate and when a system resumes updating after being stopped or disabled.
        /// </summary>
        /// <remarks>If the <see cref="EntityQuery"/> objects defined for a system do not match any existing entities
        /// then the system skips updating until a successful match is found. Likewise, if you set <see cref="Enabled"/>
        /// to false, then the system stops running. In both cases, <see cref="OnStopRunning"/> is
        /// called when a running system stops updating; `OnStartRunning` is called when it starts updating again.
        /// </remarks>
        protected virtual void OnStartRunning()
        {
        }

        /// <summary>
        /// Called when this system stops running because no entities match the system's <see cref="EntityQuery"/>
        /// objects or because the system <see cref="Enabled"/> property is changed to false.
        /// </summary>
        /// <remarks>If the <see cref="EntityQuery"/> objects defined for a system do not match any existing entities
        /// then the system skips updating until a successful match is found. Likewise, if you set <see cref="Enabled"/>
        /// to false, then the system stops running. In both cases, <see cref="OnStopRunning"/> is
        /// called when a running system stops updating; `OnStartRunning` is called when it starts updating again.
        /// </remarks>
        protected virtual void OnStopRunning()
        {
        }

        /// <summary>
        /// Called when this system is destroyed.
        /// </summary>
        /// <remarks>Systems are destroyed when the application shuts down, the World is destroyed, or you
        /// call <see cref="World.DestroySystem"/>. This includes when you exit Play Mode in the Unity Editor and
        /// when scripts are reloaded.</remarks>
        protected virtual void OnDestroy()
        {
        }

        /// <summary>
        /// Executes the system immediately.
        /// </summary>
        /// <remarks>The exact behavior is determined by this system's specific subclass.</remarks>
        public void Update()
        {
#if UNITY_EDITOR
            m_Sampler?.Begin();
#endif
            InternalUpdate();

#if UNITY_EDITOR
            m_Sampler?.End();
#endif
        }

        // ===================

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal int                        m_SystemID;
        static internal ComponentSystemBase ms_ExecutingSystem;

        internal ComponentSystemBase GetSystemFromSystemID(World world, int systemID)
        {
            foreach(var m in world.Systems)
            {
                var system = m as ComponentSystemBase;
                if (system == null)
                    continue;
                if (system.m_SystemID == systemID)
                    return system;
            }

            return null;
        }
#endif

        ref UnsafeList JobDependencyForReadingSystemsUnsafeList =>
            ref *(UnsafeList*) UnsafeUtility.AddressOf(ref m_JobDependencyForReadingSystems);

        ref UnsafeList JobDependencyForWritingSystemsUnsafeList =>
            ref *(UnsafeList*) UnsafeUtility.AddressOf(ref m_JobDependencyForWritingSystems);

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void CheckExists()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (World == null || !World.IsCreated)
            {
                if (m_SystemID == 0)
                {
                    throw new InvalidOperationException(
                        $"{GetType()}.m_systemID is zero (invalid); This usually means it was not created with World.GetOrCreateSystem<{GetType()}>().");
                }
                else
                {
                    throw new InvalidOperationException(
                        $"{GetType()} has already been destroyed. It may not be used anymore.");
                }
            }
#endif
        }

        /// <summary>
        /// Reports whether this system should run its update loop.
        /// </summary>
        /// <returns>True, if the queries in this system match existing entities or the system has the
        /// <see cref="AlwaysUpdateSystemAttribute"/>.</returns>
        public bool ShouldRunSystem()
        {
            CheckExists();

            if (m_AlwaysUpdateSystem)
                return true;

            if (m_RequiredEntityQueries != null)
            {
                for (int i = 0; i != m_RequiredEntityQueries.Length; i++)
                {
                    if (m_RequiredEntityQueries[i].IsEmptyIgnoreFilter)
                        return false;
                }

                return true;
            }
            else
            {
                // Systems without queriesDesc should always run. Specifically,
                // IJobForEach adds its queriesDesc the first time it's run.
                var length = m_EntityQueries != null ? m_EntityQueries.Length : 0;
                if (length == 0)
                    return true;

                // If all the queriesDesc are empty, skip it.
                // (There’s no way to know what the key value is without other markup)
                for (int i = 0; i != length; i++)
                {
                    if (!m_EntityQueries[i].IsEmptyIgnoreFilter)
                        return true;
                }

                return false;
            }
        }

        internal virtual void OnBeforeCreateInternal(World world)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_SystemID = World.AllocateSystemID();
#endif
            m_World = world;
            m_EntityManager = world.EntityManager;
            m_SafetyManager = m_EntityManager.ComponentJobSafetyManager;

            m_EntityQueries = new EntityQuery[0];
#if !NET_DOTS
            m_AlwaysUpdateSystem = GetType().GetCustomAttributes(typeof(AlwaysUpdateSystemAttribute), true).Length != 0;
#else
            m_AlwaysUpdateSystem = true;
#endif
        }

        internal void OnAfterDestroyInternal()
        {
            foreach (var query in m_EntityQueries)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                query.DisallowDisposing = null;
#endif
                query.Dispose();
            }

            m_EntityQueries = null;
            m_EntityManager = null;
            m_World = null;
            m_SafetyManager = null;

            JobDependencyForReadingSystemsUnsafeList.Dispose<int>();
            JobDependencyForWritingSystemsUnsafeList.Dispose<int>();
        }

        internal abstract void InternalUpdate();

        internal virtual void OnBeforeDestroyInternal()
        {
            if (m_PreviouslyEnabled)
            {
                m_PreviouslyEnabled = false;
                OnStopRunning();
            }
        }

        internal void BeforeUpdateVersioning()
        {
            m_EntityManager.EntityComponentStore->IncrementGlobalSystemVersion();
            foreach (var query in m_EntityQueries)
                query.SetFilterChangedRequiredVersion(m_LastSystemVersion);
        }

        internal void AfterUpdateVersioning()
        {
            m_LastSystemVersion = EntityManager.EntityComponentStore->GlobalSystemVersion;
        }

        // TODO: this should be made part of UnityEngine?
        static void ArrayUtilityAdd<T>(ref T[] array, T item)
        {
            Array.Resize(ref array, array.Length + 1);
            array[array.Length - 1] = item;
        }

        /// <summary>
        /// Gets the run-time type information required to access an array of component data in a chunk.
        /// </summary>
        /// <param name="isReadOnly">Whether the component data is only read, not written. Access components as
        /// read-only whenever possible.</param>
        /// <typeparam name="T">A struct that implements <see cref="IComponentData"/>.</typeparam>
        /// <returns>An object representing the type information required to safely access component data stored in a
        /// chunk.</returns>
        /// <remarks>Pass an ArchetypeChunkComponentType instance to a job that has access to chunk data, such as an
        /// <see cref="IJobChunk"/> job, to access that type of component inside the job.</remarks>
        public ArchetypeChunkComponentType<T> GetArchetypeChunkComponentType<T>(bool isReadOnly = false)
        {
            AddReaderWriter(isReadOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>());
            return EntityManager.GetArchetypeChunkComponentType<T>(isReadOnly);
        }

        /// <summary>
        /// Gets the run-time type information required to access an array of buffer components in a chunk.
        /// </summary>
        /// <param name="isReadOnly">Whether the data is only read, not written. Access data as
        /// read-only whenever possible.</param>
        /// <typeparam name="T">A struct that implements <see cref="IBufferElementData"/>.</typeparam>
        /// <returns>An object representing the type information required to safely access buffer components stored in a
        /// chunk.</returns>
        /// <remarks>Pass an GetArchetypeChunkBufferType instance to a job that has access to chunk data, such as an
        /// <see cref="IJobChunk"/> job, to access that type of buffer component inside the job.</remarks>
        public ArchetypeChunkBufferType<T> GetArchetypeChunkBufferType<T>(bool isReadOnly = false)
            where T : struct, IBufferElementData
        {
            AddReaderWriter(isReadOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>());
            return EntityManager.GetArchetypeChunkBufferType<T>(isReadOnly);
        }

        /// <summary>
        /// Gets the run-time type information required to access a shared component data in a chunk.
        /// </summary>
        /// <typeparam name="T">A struct that implements <see cref="ISharedComponentData"/>.</typeparam>
        /// <returns>An object representing the type information required to safely access shared component data stored in a
        /// chunk.</returns>
        public ArchetypeChunkSharedComponentType<T> GetArchetypeChunkSharedComponentType<T>()
            where T : struct, ISharedComponentData
        {
            return EntityManager.GetArchetypeChunkSharedComponentType<T>();
        }

        /// <summary>
        /// Gets the run-time type information required to access the array of <see cref="Entity"/> objects in a chunk.
        /// </summary>
        /// <returns>An object representing the type information required to safely access Entity instances stored in a
        /// chunk.</returns>
        public ArchetypeChunkEntityType GetArchetypeChunkEntityType()
        {
            return EntityManager.GetArchetypeChunkEntityType();
        }

        /// <summary>
        /// Gets an array-like container containing all components of type T, indexed by Entity.
        /// </summary>
        /// <param name="isReadOnly">Whether the data is only read, not written. Access data as
        /// read-only whenever possible.</param>
        /// <typeparam name="T">A struct that implements IComponentData.</typeparam>
        /// <returns>All component data of type T.</returns>
        public ComponentDataFromEntity<T> GetComponentDataFromEntity<T>(bool isReadOnly = false)
            where T : struct, IComponentData
        {
            AddReaderWriter(isReadOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>());
            return EntityManager.GetComponentDataFromEntity<T>(isReadOnly);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="query"></param>
        public void RequireForUpdate(EntityQuery query)
        {
            if (m_RequiredEntityQueries == null)
                m_RequiredEntityQueries = new EntityQuery[1] {query};
            else
                ArrayUtilityAdd(ref m_RequiredEntityQueries, query);
        }

        /// <summary>
        ///
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void RequireSingletonForUpdate<T>()
        {
            var type = ComponentType.ReadOnly<T>();
            var query = GetEntityQueryInternal(&type, 1);
            RequireForUpdate(query);
        }

        /// <summary>
        ///
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public bool HasSingleton<T>()
            where T : struct, IComponentData
        {
            var type = ComponentType.ReadOnly<T>();
            var query = GetEntityQueryInternal(&type, 1);
            return !query.IsEmptyIgnoreFilter;
        }

        /// <summary>
        ///
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T GetSingleton<T>()
            where T : struct, IComponentData
        {
            var type = ComponentType.ReadOnly<T>();
            var query = GetEntityQueryInternal(&type, 1);

            return query.GetSingleton<T>();
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="value"></param>
        /// <typeparam name="T"></typeparam>
        public void SetSingleton<T>(T value)
            where T : struct, IComponentData
        {
            var type = ComponentType.ReadWrite<T>();
            var query = GetEntityQueryInternal(&type, 1);
            query.SetSingleton(value);
        }

        /// <summary>
        ///
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public Entity GetSingletonEntity<T>()
            where T : struct, IComponentData
        {
            var type = ComponentType.ReadOnly<T>();
            var query = GetEntityQueryInternal(&type, 1);

            return query.GetSingletonEntity();
        }

        internal void AddReaderWriter(ComponentType componentType)
        {
            if (CalculateReaderWriterDependency.Add(componentType, ref JobDependencyForReadingSystemsUnsafeList,
                ref JobDependencyForWritingSystemsUnsafeList))
            {
                CompleteDependencyInternal();
            }
        }

        internal void AddReaderWriters(EntityQuery query)
        {
            if (query.AddReaderWritersToLists(ref JobDependencyForReadingSystemsUnsafeList,
                ref JobDependencyForWritingSystemsUnsafeList))
            {
                CompleteDependencyInternal();
            }
        }

        internal EntityQuery GetEntityQueryInternal(ComponentType* componentTypes, int count)
        {
            for (var i = 0; i != m_EntityQueries.Length; i++)
            {
                if (m_EntityQueries[i].CompareComponents(componentTypes, count))
                    return m_EntityQueries[i];
            }

            var query = EntityManager.CreateEntityQuery(componentTypes, count);

            AddReaderWriters(query);
            AfterQueryCreated(query);

            return query;
        }

        internal EntityQuery GetEntityQueryInternal(ComponentType[] componentTypes)
        {
            fixed (ComponentType* componentTypesPtr = componentTypes)
            {
                return GetEntityQueryInternal(componentTypesPtr, componentTypes.Length);
            }
        }

        internal EntityQuery GetEntityQueryInternal(EntityQueryDesc[] desc)
        {
            for (var i = 0; i != m_EntityQueries.Length; i++)
            {
                if (m_EntityQueries[i].CompareQuery(desc))
                    return m_EntityQueries[i];
            }

            var query = EntityManager.CreateEntityQuery(desc);

            AddReaderWriters(query);
            AfterQueryCreated(query);

            return query;
        }

        void AfterQueryCreated(EntityQuery query)
        {
            query.SetFilterChangedRequiredVersion(m_LastSystemVersion);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            query.DisallowDisposing =
 "EntityQuery.Dispose() may not be called on a EntityQuery created with ComponentSystem.GetEntityQuery. The EntityQuery will automatically be disposed by the ComponentSystem.";
#endif

            ArrayUtilityAdd(ref m_EntityQueries, query);
        }

        protected internal EntityQuery GetEntityQuery(params ComponentType[] componentTypes)
        {
            return GetEntityQueryInternal(componentTypes);
        }

        protected EntityQuery GetEntityQuery(NativeArray<ComponentType> componentTypes)
        {
            return GetEntityQueryInternal((ComponentType*) componentTypes.GetUnsafeReadOnlyPtr(),
                componentTypes.Length);
        }

        protected internal EntityQuery GetEntityQuery(params EntityQueryDesc[] queryDesc)
        {
            return GetEntityQueryInternal(queryDesc);
        }

        internal void CompleteDependencyInternal()
        {
            m_SafetyManager->CompleteDependenciesNoChecks(m_JobDependencyForReadingSystems.p,
                m_JobDependencyForReadingSystems.Count, m_JobDependencyForWritingSystems.p,
                m_JobDependencyForWritingSystems.Count);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("GetEntities has been deprecated. Use Entities.ForEach to access managed components. More information: https://forum.unity.com/threads/api-deprecation-faq-0-0-23.636994/", true)]
        protected void GetEntities<T>() where T : struct { }
    }

    /// <summary>
    ///
    /// </summary>
    public abstract partial class ComponentSystem : ComponentSystemBase
    {
        EntityCommandBuffer m_DeferredEntities;
        EntityQueryCache m_EntityQueryCache;

        /// <summary>
        ///
        /// </summary>
        public EntityCommandBuffer PostUpdateCommands => m_DeferredEntities;

        protected internal void InitEntityQueryCache(int cacheSize) =>
            m_EntityQueryCache = new EntityQueryCache(cacheSize);

        internal EntityQueryCache EntityQueryCache => m_EntityQueryCache;

        internal EntityQueryCache GetOrCreateEntityQueryCache()
            => m_EntityQueryCache ?? (m_EntityQueryCache = new EntityQueryCache());

        protected internal EntityQueryBuilder Entities => new EntityQueryBuilder(this);

        unsafe void BeforeOnUpdate()
        {
            BeforeUpdateVersioning();
            CompleteDependencyInternal();

            m_DeferredEntities = new EntityCommandBuffer(Allocator.TempJob, -1);
        }

        void AfterOnUpdate()
        {
            AfterUpdateVersioning();

            JobHandle.ScheduleBatchedJobs();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            try
            {
                m_DeferredEntities.Playback(EntityManager);
            }
            catch (Exception e)
            {
                m_DeferredEntities.Dispose();
                var error = $"{e.Message}\nEntityCommandBuffer was recorded in {GetType()} using PostUpdateCommands.\n" + e.StackTrace;
                throw new System.ArgumentException(error);
            }
#else
            m_DeferredEntities.Playback(EntityManager);
#endif
            m_DeferredEntities.Dispose();
        }

        internal sealed override void InternalUpdate()
        {
            if (Enabled && ShouldRunSystem())
            {
                if (!m_PreviouslyEnabled)
                {
                    m_PreviouslyEnabled = true;
                    OnStartRunning();
                }

                BeforeOnUpdate();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var oldExecutingSystem = ms_ExecutingSystem;
                ms_ExecutingSystem = this;
#endif

                try
                {
                    OnUpdate();
                }
                finally
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    ms_ExecutingSystem = oldExecutingSystem;
#endif
                    AfterOnUpdate();
                }
            }
            else if (m_PreviouslyEnabled)
            {
                m_PreviouslyEnabled = false;
                OnStopRunning();
            }
        }

        internal sealed override void OnBeforeCreateInternal(World world)
        {
            base.OnBeforeCreateInternal(world);
        }

        internal sealed override void OnBeforeDestroyInternal()
        {
            base.OnBeforeDestroyInternal();
        }

        /// <summary>
        /// Called once per frame on the main thread when any of this system's
        /// EntityQueries would match, or if the system has the AlwaysUpdate
        /// attribute.
        /// </summary>
        protected abstract void OnUpdate();

    }

    /// <summary>
    ///
    /// </summary>
    public abstract class JobComponentSystem : ComponentSystemBase
    {
        JobHandle m_PreviousFrameDependency;

        unsafe JobHandle BeforeOnUpdate()
        {
            BeforeUpdateVersioning();

            // We need to wait on all previous frame dependencies, otherwise it is possible that we create infinitely long dependency chains
            // without anyone ever waiting on it
            m_PreviousFrameDependency.Complete();

            return GetDependency();
        }

        unsafe void AfterOnUpdate(JobHandle outputJob, bool throwException)
        {
            AfterUpdateVersioning();

            JobHandle.ScheduleBatchedJobs();

            AddDependencyInternal(outputJob);

#if ENABLE_UNITY_COLLECTIONS_CHECKS

            if (!JobsUtility.JobDebuggerEnabled)
                return;

            // Check that all reading and writing jobs are a dependency of the output job, to
            // catch systems that forget to add one of their jobs to the dependency graph.
            //
            // Note that this check is not strictly needed as we would catch the mistake anyway later,
            // but checking it here means we can flag the system that has the mistake, rather than some
            // other (innocent) system that is doing things correctly.

            //@TODO: It is not ideal that we call m_SafetyManager.GetDependency,
            //       it can result in JobHandle.CombineDependencies calls.
            //       Which seems like debug code might have side-effects

            string dependencyError = null;
            for (var index = 0; index < m_JobDependencyForReadingSystems.Count && dependencyError == null; index++)
            {
                var type = m_JobDependencyForReadingSystems.p[index];
                dependencyError = CheckJobDependencies(type);
            }

            for (var index = 0; index < m_JobDependencyForWritingSystems.Count && dependencyError == null; index++)
            {
                var type = m_JobDependencyForWritingSystems.p[index];
                dependencyError = CheckJobDependencies(type);
            }

            if (dependencyError != null)
            {
                EmergencySyncAllJobs();

                if (throwException)
                    throw new System.InvalidOperationException(dependencyError);
            }
#endif
        }

        internal sealed override void InternalUpdate()
        {
            if (Enabled && ShouldRunSystem())
            {
                if (!m_PreviouslyEnabled)
                {
                    m_PreviouslyEnabled = true;
                    OnStartRunning();
                }

                var inputJob = BeforeOnUpdate();
                JobHandle outputJob = new JobHandle();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var oldExecutingSystem = ms_ExecutingSystem;
                ms_ExecutingSystem = this;
#endif
                try
                {
                    outputJob = OnUpdate(inputJob);
                }
                catch
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    ms_ExecutingSystem = oldExecutingSystem;
#endif

                    AfterOnUpdate(outputJob, false);
                    throw;
                }

                AfterOnUpdate(outputJob, true);
            }
            else if (m_PreviouslyEnabled)
            {
                m_PreviouslyEnabled = false;
                OnStopRunning();
            }
        }

        internal sealed override void OnBeforeDestroyInternal()
        {
            base.OnBeforeDestroyInternal();
            m_PreviousFrameDependency.Complete();
        }

        public BufferFromEntity<T> GetBufferFromEntity<T>(bool isReadOnly = false) where T : struct, IBufferElementData
        {
            AddReaderWriter(isReadOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>());
            return EntityManager.GetBufferFromEntity<T>(isReadOnly);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="inputDeps"></param>
        /// <returns></returns>
        protected abstract JobHandle OnUpdate(JobHandle inputDeps);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        unsafe string CheckJobDependencies(int type)
        {
            var h = m_SafetyManager->GetSafetyHandle(type, true);

            var readerCount = AtomicSafetyHandle.GetReaderArray(h, 0, IntPtr.Zero);
            JobHandle* readers = stackalloc JobHandle[readerCount];
            AtomicSafetyHandle.GetReaderArray(h, readerCount, (IntPtr) readers);

            for (var i = 0; i < readerCount; ++i)
            {
                if (!m_SafetyManager->HasReaderOrWriterDependency(type, readers[i]))
                    return $"The system {GetType()} reads {TypeManager.GetType(type)} via {AtomicSafetyHandle.GetReaderName(h, i)} but that type was not returned as a job dependency. To ensure correct behavior of other systems, the job or a dependency of it must be returned from the OnUpdate method.";
            }

            if (!m_SafetyManager->HasReaderOrWriterDependency(type, AtomicSafetyHandle.GetWriter(h)))
                return $"The system {GetType()} writes {TypeManager.GetType(type)} via {AtomicSafetyHandle.GetWriterName(h)} but that was not returned as a job dependency. To ensure correct behavior of other systems, the job or a dependency of it must be returned from the OnUpdate method.";

            return null;
        }

        unsafe void EmergencySyncAllJobs()
        {
            for (int i = 0;i != m_JobDependencyForReadingSystems.Count;i++)
            {
                int type = m_JobDependencyForReadingSystems.p[i];
                AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_SafetyManager->GetSafetyHandle(type, true));
            }

            for (int i = 0;i != m_JobDependencyForWritingSystems.Count;i++)
            {
                int type = m_JobDependencyForWritingSystems.p[i];
                AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_SafetyManager->GetSafetyHandle(type, true));
            }
        }
#endif

        unsafe JobHandle GetDependency ()
        {
            return m_SafetyManager->GetDependency(m_JobDependencyForReadingSystems.p, m_JobDependencyForReadingSystems.Count, m_JobDependencyForWritingSystems.p, m_JobDependencyForWritingSystems.Count);
        }

        unsafe void AddDependencyInternal(JobHandle dependency)
        {
            m_PreviousFrameDependency = m_SafetyManager->AddDependency(m_JobDependencyForReadingSystems.p, m_JobDependencyForReadingSystems.Count, m_JobDependencyForWritingSystems.p, m_JobDependencyForWritingSystems.Count, dependency);
        }


    }

    [Obsolete("BarrierSystem has been renamed. Use EntityCommandBufferSystem instead (UnityUpgradable) -> EntityCommandBufferSystem", true)]
    [System.ComponentModel.EditorBrowsable(EditorBrowsableState.Never)]
    public struct BarrierSystem { }

    /// <summary>
    ///
    /// </summary>
    public unsafe abstract class EntityCommandBufferSystem : ComponentSystem
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private List<EntityCommandBuffer> m_PendingBuffers;
        internal List<EntityCommandBuffer> PendingBuffers
        {
            get { return m_PendingBuffers; }
        }
#else
        private NativeList<EntityCommandBuffer> m_PendingBuffers;
        internal NativeList<EntityCommandBuffer> PendingBuffers
        {
            get { return m_PendingBuffers; }
        }
#endif

        private JobHandle m_ProducerHandle;

        /// <summary>
        /// Create an EntityCommandBuffer which will be played back during this EntityCommandBufferSystem's OnUpdate().
        /// If this command buffer is written to by job code using its Concurrent interface, the caller
        /// must call EntityCommandBufferSystem.AddJobHandleForProducer() to ensure that the EntityCommandBufferSystem waits
        /// for the job to complete before playing back the command buffer. See AddJobHandleForProducer()
        /// for a complete example.
        /// </summary>
        /// <returns></returns>
        public EntityCommandBuffer CreateCommandBuffer()
        {
            var cmds = new EntityCommandBuffer(Allocator.TempJob, -1);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            cmds.SystemID = ms_ExecutingSystem != null ? ms_ExecutingSystem.m_SystemID : 0;
#endif
            m_PendingBuffers.Add(cmds);

            return cmds;
        }

        /// <summary>
        /// Adds the specified JobHandle to this system's list of dependencies.
        /// </summary>
        /// <remarks>
        /// This is usually called by a system that's building an EntityCommandBuffer created
        /// by this EntityCommandBufferSystem, to prevent the command buffer from being played back before
        /// it's complete. The general usage looks like:
        /// <code>
        ///    MyEntityCommandBufferSystem _cmdBufferSystem;
        ///    // in OnCreate():
        ///    _cmdBufferSystem = World.GetOrCreateManager<MyEntityCommandBufferSystem>();
        ///    // in OnUpdate():
        ///    EntityCommandBuffer cmd = _cmdBufferSystem.CreateCommandBuffer();
        ///    var job = new MyProducerJob {
        ///        CommandBuffer = cmd,
        ///    }.Schedule(this, inputDeps);
        ///    _cmdBufferSystem.AddJobHandleForProducer(job);
        /// </code>
        /// </remarks>
        /// <param name="producerJob">A JobHandle which this barrier system should wait on before playing back its
        /// pending EntityCommandBuffers.</param>
        public void AddJobHandleForProducer(JobHandle producerJob)
        {
            m_ProducerHandle = JobHandle.CombineDependencies(m_ProducerHandle, producerJob);
        }

        /// <summary>
        ///
        /// </summary>
        protected override void OnCreate()
        {
            base.OnCreate();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_PendingBuffers = new List<EntityCommandBuffer>();
#else
            m_PendingBuffers = new NativeList<EntityCommandBuffer>(Allocator.Persistent);
#endif
        }

        /// <summary>
        ///
        /// </summary>
        protected override void OnDestroy()
        {
            FlushPendingBuffers(false);
            m_PendingBuffers.Clear();

#if !ENABLE_UNITY_COLLECTIONS_CHECKS
            m_PendingBuffers.Dispose();
#endif

            base.OnDestroy();
        }

        /// <summary>
        ///
        /// </summary>
        protected override void OnUpdate()
        {
            FlushPendingBuffers(true);
            m_PendingBuffers.Clear();
        }

        internal void FlushPendingBuffers(bool playBack)
        {
            m_ProducerHandle.Complete();
            m_ProducerHandle = new JobHandle();

            int length;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            length = m_PendingBuffers.Count;
#else
            length = m_PendingBuffers.Length;
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            List<string> playbackErrorLog = null;
            bool completeAllJobsBeforeDispose = false;
#endif
            for (int i = 0; i < length; ++i)
            {
                var buffer = m_PendingBuffers[i];
                if (!buffer.IsCreated)
                {
                    continue;
                }
                if (playBack)
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    try
                    {
                        buffer.Playback(EntityManager);
                    }
                    catch (Exception e)
                    {
                        var system = GetSystemFromSystemID(World, buffer.SystemID);
                        var systemType = system != null ? system.GetType().ToString() : "Unknown";
                        var error = $"{e.Message}\nEntityCommandBuffer was recorded in {systemType} and played back in {GetType()}.\n" + e.StackTrace;
                        if (playbackErrorLog == null)
                        {
                            playbackErrorLog = new List<string>();
                        }
                        playbackErrorLog.Add(error);
                        completeAllJobsBeforeDispose = true;
                    }
#else
                    buffer.Playback(EntityManager);
#endif
                }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                try
                {
                    if (completeAllJobsBeforeDispose)
                    {
                        // If we get here, there was an error during playback (potentially a race condition on the
                        // buffer itself), and we should wait for all jobs writing to this command buffer to complete before attempting
                        // to dispose of the command buffer to prevent a potential race condition.
                        buffer.WaitForWriterJobs();
                        completeAllJobsBeforeDispose = false;
                    }
                    buffer.Dispose();
                }
                catch (Exception e)
                {
                    var system = GetSystemFromSystemID(World, buffer.SystemID);
                    var systemType = system != null ? system.GetType().ToString() : "Unknown";
                    var error = $"{e.Message}\nEntityCommandBuffer was recorded in {systemType} and disposed in {GetType()}.\n" + e.StackTrace;
                    if (playbackErrorLog == null)
                    {
                        playbackErrorLog = new List<string>();
                    }
                    playbackErrorLog.Add(error);
                }
#else
                buffer.Dispose();
#endif
                m_PendingBuffers[i] = buffer;
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (playbackErrorLog != null)
            {
#if !NET_DOTS
                string exceptionMessage = playbackErrorLog.Aggregate((str1, str2) => str1 + "\n" + str2);
#else
                foreach (var err in playbackErrorLog)
                {
                    Console.WriteLine(err);
                }
                string exceptionMessage = "Errors occurred during ECB playback; see stdout";
#endif
                Exception exception = new System.ArgumentException(exceptionMessage);
                throw exception;
            }
#endif
        }
    }
}
