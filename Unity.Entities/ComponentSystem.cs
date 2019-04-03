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

namespace Unity.Entities
{

    [DebuggerTypeProxy(typeof(IntListDebugView))]
    internal unsafe struct IntList
    {
        public int* p;
        public int Count;
        public int Capacity;
    }


    public unsafe abstract class ComponentSystemBase : ScriptBehaviourManager
    {
#if !UNITY_ZEROPLAYER
        InjectComponentGroupData[] 			m_InjectedComponentGroups;
        InjectFromEntityData                m_InjectFromEntityData;
#endif
#if !UNITY_CSHARP_TINY
        ComponentGroupArrayStaticCache[] 	m_CachedComponentGroupArrays;
#endif
        ComponentGroup[] 				    m_ComponentGroups;
        ComponentGroup[]  				    m_RequiredComponentGroups;

        internal IntList                    m_JobDependencyForReadingManagers;
        internal IntList                    m_JobDependencyForWritingManagers;

        uint                                m_LastSystemVersion;

        internal ComponentJobSafetyManager  m_SafetyManager;
        internal EntityManager              m_EntityManager;
        World                               m_World;

        bool                                m_AlwaysUpdateSystem;
        internal bool                       m_PreviouslyEnabled;

        public bool Enabled { get; set; } = true;
        public ComponentGroup[] 			ComponentGroups => m_ComponentGroups;

        public uint GlobalSystemVersion => m_EntityManager.GlobalSystemVersion;
        public uint LastSystemVersion   => m_LastSystemVersion;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal int                        m_SystemID;
        static internal ComponentSystemBase ms_ExecutingSystem;

        internal ComponentSystemBase GetSystemFromSystemID(World world, int systemID)
        {
            foreach(var m in world.BehaviourManagers)
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

        ref UnsafeList JobDependencyForReadingManagersUnsafeList => ref *(UnsafeList*)UnsafeUtility.AddressOf(ref m_JobDependencyForReadingManagers);
        ref UnsafeList JobDependencyForWritingManagersUnsafeList => ref *(UnsafeList*)UnsafeUtility.AddressOf(ref m_JobDependencyForWritingManagers);

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void CheckExists()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (World == null || !World.IsCreated)
                throw new InvalidOperationException($"{GetType()} has already been destroyed. It may not be used anymore.");
#endif
        }

        public bool ShouldRunSystem()
        {
            CheckExists();

            if (m_AlwaysUpdateSystem)
                return true;

            if (m_RequiredComponentGroups != null)
            {
                for (int i = 0;i != m_RequiredComponentGroups.Length;i++)
                {
                    if (m_RequiredComponentGroups[i].IsEmptyIgnoreFilter)
                        return false;
                }

                return true;
            }
            else
            {
                // Systems without component groups should always run. Specifically,
                // IJobProcessComponentData adds its component group the first time it's run.
                var length = m_ComponentGroups != null ? m_ComponentGroups.Length : 0;
                if (length == 0)
                    return true;

                // If all the groups are empty, skip it.
                // (There’s no way to know what they key value is without other markup)
                for (int i = 0;i != length;i++)
                {
                    if (!m_ComponentGroups[i].IsEmptyIgnoreFilter)
                        return true;
                }

                return false;
            }
        }

        protected override void OnBeforeCreateManagerInternal(World world)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_SystemID = World.AllocateSystemID();
#endif
            m_World = world;
            m_EntityManager = world.GetOrCreateManager<EntityManager>();
            m_SafetyManager = m_EntityManager.ComponentJobSafetyManager;

            m_ComponentGroups = new ComponentGroup[0];
#if !UNITY_CSHARP_TINY
            m_CachedComponentGroupArrays = new ComponentGroupArrayStaticCache[0];
            m_AlwaysUpdateSystem = GetType().GetCustomAttributes(typeof(AlwaysUpdateSystemAttribute), true).Length != 0;
#else
            m_AlwaysUpdateSystem = true;
#endif

#if !UNITY_ZEROPLAYER
            ComponentSystemInjection.Inject(this, world, m_EntityManager, out m_InjectedComponentGroups, out m_InjectFromEntityData);
            m_InjectFromEntityData.ExtractJobDependencyTypes(this);
#endif
            InjectNestedIJobProcessComponentDataJobs();

            UpdateInjectedComponentGroups();
        }

        void InjectNestedIJobProcessComponentDataJobs()
        {
#if !UNITY_ZEROPLAYER
            // Create ComponentGroup for all nested IJobProcessComponentData jobs
            foreach (var nestedType in GetType().GetNestedTypes(BindingFlags.FlattenHierarchy | BindingFlags.NonPublic | BindingFlags.Public))
                JobProcessComponentDataExtensions.GetComponentGroupForIJobProcessComponentData(this, nestedType);
#endif
        }

        protected sealed override void OnAfterDestroyManagerInternal()
        {
            foreach (var group in m_ComponentGroups)
            {
                #if ENABLE_UNITY_COLLECTIONS_CHECKS
                group.DisallowDisposing = null;
                #endif
                group.Dispose();
            }
            m_ComponentGroups = null;
            m_EntityManager = null;
            m_World = null;
            m_SafetyManager = null;
#if !UNITY_ZEROPLAYER
            m_InjectedComponentGroups = null;
#endif
#if !UNITY_CSHARP_TINY
            m_CachedComponentGroupArrays = null;
#endif

            JobDependencyForReadingManagersUnsafeList.Dispose<int>();
            JobDependencyForWritingManagersUnsafeList.Dispose<int>();
        }

        internal override void InternalUpdate()
        {
            throw new NotImplementedException();
        }

        protected override void OnBeforeDestroyManagerInternal()
        {
            if (m_PreviouslyEnabled)
            {
                m_PreviouslyEnabled = false;
                OnStopRunning();
            }
            CompleteDependencyInternal();
            UpdateInjectedComponentGroups();
        }

        protected virtual void OnStartRunning()
        {

        }

        protected virtual void OnStopRunning()
        {

        }

        protected internal void BeforeUpdateVersioning()
        {
            m_EntityManager.Entities->IncrementGlobalSystemVersion();
            foreach (var group in m_ComponentGroups)
                group.SetFilterChangedRequiredVersion(m_LastSystemVersion);
        }

        protected internal void AfterUpdateVersioning()
        {
            m_LastSystemVersion = EntityManager.Entities->GlobalSystemVersion;
        }

        protected internal EntityManager EntityManager => m_EntityManager;
        protected internal World World => m_World;

        // TODO: this should be made part of UnityEngine?
        static void ArrayUtilityAdd<T>(ref T[] array, T item)
        {
            Array.Resize(ref array, array.Length + 1);
            array[array.Length - 1] = item;
        }

        public ArchetypeChunkComponentType<T> GetArchetypeChunkComponentType<T>(bool isReadOnly = false)
        {
            AddReaderWriter(isReadOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>());
            return EntityManager.GetArchetypeChunkComponentType<T>(isReadOnly);
        }

        public ArchetypeChunkBufferType<T> GetArchetypeChunkBufferType<T>(bool isReadOnly = false)
            where T : struct, IBufferElementData
        {
            AddReaderWriter(isReadOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>());
            return EntityManager.GetArchetypeChunkBufferType<T>(isReadOnly);
        }

        public ArchetypeChunkSharedComponentType<T> GetArchetypeChunkSharedComponentType<T>()
            where T : struct, ISharedComponentData
        {
            return EntityManager.GetArchetypeChunkSharedComponentType<T>();
        }

        public ArchetypeChunkEntityType GetArchetypeChunkEntityType()
        {
            return EntityManager.GetArchetypeChunkEntityType();
        }

        public ComponentDataFromEntity<T> GetComponentDataFromEntity<T>(bool isReadOnly = false)
            where T : struct, IComponentData
        {
            AddReaderWriter(isReadOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>());
            return EntityManager.GetComponentDataFromEntity<T>(isReadOnly);
        }

        public void RequireForUpdate(ComponentGroup group)
        {
            if (m_RequiredComponentGroups == null)
                m_RequiredComponentGroups =new ComponentGroup[1] { group };
            else
                ArrayUtilityAdd(ref m_RequiredComponentGroups, group);
        }

        public void RequireSingletonForUpdate<T>()
        {
            var type = ComponentType.ReadOnly<T>();
            var group = GetComponentGroupInternal(&type, 1);
            RequireForUpdate(group);
        }

        public bool HasSingleton<T>()
            where T : struct, IComponentData
        {
            var type = ComponentType.ReadOnly<T>();
            var group = GetComponentGroupInternal(&type, 1);
            return !group.IsEmptyIgnoreFilter;
        }

        public T GetSingleton<T>()
            where T : struct, IComponentData
        {
            var type = ComponentType.ReadOnly<T>();
            var group = GetComponentGroupInternal(&type, 1);

            return group.GetSingleton<T>();
        }

        public void SetSingleton<T>(T value)
            where T : struct, IComponentData
        {
            var type = ComponentType.ReadWrite<T>();
            var group = GetComponentGroupInternal(&type, 1);
            group.SetSingleton(value);
        }

        public Entity GetSingletonEntity<T>()
            where T : struct, IComponentData
        {
            var type = ComponentType.ReadOnly<T>();
            var group = GetComponentGroupInternal(&type, 1);

            return group.GetSingletonEntity();
        }

        internal void AddReaderWriter(ComponentType componentType)
        {
            if (CalculateReaderWriterDependency.Add(componentType, ref JobDependencyForReadingManagersUnsafeList, ref JobDependencyForWritingManagersUnsafeList))
            {
                CompleteDependencyInternal();
            }
        }

        internal void AddReaderWriters(ComponentGroup group)
        {
            if (group.AddReaderWritersToLists(ref JobDependencyForReadingManagersUnsafeList, ref JobDependencyForWritingManagersUnsafeList))
            {
                CompleteDependencyInternal();
            }
        }

        internal ComponentGroup GetComponentGroupInternal(ComponentType* componentTypes, int count)
        {
            for (var i = 0; i != m_ComponentGroups.Length; i++)
            {
                if (m_ComponentGroups[i].CompareComponents(componentTypes, count))
                    return m_ComponentGroups[i];
            }

            var group = EntityManager.CreateComponentGroup(componentTypes, count);

            AddReaderWriters(group);
            AfterGroupCreated(group);

            return group;
        }

        internal ComponentGroup GetComponentGroupInternal(ComponentType[] componentTypes)
        {
            fixed (ComponentType* componentTypesPtr = componentTypes)
            {
                return GetComponentGroupInternal(componentTypesPtr, componentTypes.Length);
            }
        }

        internal ComponentGroup GetComponentGroupInternal(EntityArchetypeQuery[] query)
        {
            for (var i = 0; i != m_ComponentGroups.Length; i++)
            {
                if (m_ComponentGroups[i].CompareQuery(query))
                    return m_ComponentGroups[i];
            }

            var group = EntityManager.CreateComponentGroup(query);

            AddReaderWriters(group);
            AfterGroupCreated(group);

            return group;
        }

        void AfterGroupCreated(ComponentGroup group)
        {
            group.SetFilterChangedRequiredVersion(m_LastSystemVersion);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            group.DisallowDisposing = "ComponentGroup.Dispose() may not be called on a ComponentGroup created with ComponentSystem.GetComponentGroup. The ComponentGroup will automatically be disposed by the ComponentSystem.";
#endif

            ArrayUtilityAdd(ref m_ComponentGroups, group);
        }

        protected internal ComponentGroup GetComponentGroup(params ComponentType[] componentTypes)
        {
            return GetComponentGroupInternal(componentTypes);
        }
        protected ComponentGroup GetComponentGroup(NativeArray<ComponentType> componentTypes)
        {
            return GetComponentGroupInternal((ComponentType*)componentTypes.GetUnsafeReadOnlyPtr(), componentTypes.Length);
        }

        protected internal ComponentGroup GetComponentGroup(params EntityArchetypeQuery[] query)
        {
            return GetComponentGroupInternal(query);
        }

#if !UNITY_CSHARP_TINY
        [Obsolete("GetEntities has been deprecated. Use ComponentSystem.ForEach to access managed components.")]
        protected ComponentGroupArray<T> GetEntities<T>() where T : struct
        {
            for (var i = 0; i != m_CachedComponentGroupArrays.Length; i++)
            {
                if (m_CachedComponentGroupArrays[i].CachedType == typeof(T))
                    return new ComponentGroupArray<T>(m_CachedComponentGroupArrays[i]);
            }

            var cache = new ComponentGroupArrayStaticCache(typeof(T), EntityManager, this);
            ArrayUtilityAdd(ref m_CachedComponentGroupArrays, cache);
            return new ComponentGroupArray<T>(cache);
        }
#endif

        protected void UpdateInjectedComponentGroups()
        {
#if !UNITY_ZEROPLAYER
            if (null == m_InjectedComponentGroups)
                return;

            ulong gchandle;
            var pinnedSystemPtr = (byte*)UnsafeUtility.PinGCObjectAndGetAddress(this, out gchandle);

            try
            {
                foreach (var group in m_InjectedComponentGroups)
                    group.UpdateInjection (pinnedSystemPtr);

                m_InjectFromEntityData.UpdateInjection(pinnedSystemPtr, EntityManager);
            }
            catch
            {
                UnsafeUtility.ReleaseGCObject(gchandle);
                throw;
            }
            UnsafeUtility.ReleaseGCObject(gchandle);
#endif
        }

        internal void CompleteDependencyInternal()
        {
            m_SafetyManager.CompleteDependenciesNoChecks(m_JobDependencyForReadingManagers.p, m_JobDependencyForReadingManagers.Count, m_JobDependencyForWritingManagers.p, m_JobDependencyForWritingManagers.Count);
        }
    }

    public abstract partial class ComponentSystem : ComponentSystemBase
    {
        EntityCommandBuffer m_DeferredEntities;
        EntityQueryCache m_EntityQueryCache;

        public EntityCommandBuffer PostUpdateCommands => m_DeferredEntities;

        protected internal void InitEntityQueryCache(int cacheSize) =>
            m_EntityQueryCache = new EntityQueryCache(cacheSize);

        internal EntityQueryCache GetOrCreateEntityQueryCache()
            => m_EntityQueryCache ?? (m_EntityQueryCache = new EntityQueryCache());

        protected internal EntityQueryBuilder Entities => new EntityQueryBuilder(this);

        unsafe void BeforeOnUpdate()
        {
            BeforeUpdateVersioning();
            CompleteDependencyInternal();
            UpdateInjectedComponentGroups();

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

        protected sealed override void OnBeforeCreateManagerInternal(World world)
        {
            base.OnBeforeCreateManagerInternal(world);
        }

        protected sealed override void OnBeforeDestroyManagerInternal()
        {
            base.OnBeforeDestroyManagerInternal();
        }

        /// <summary>
        /// Called once per frame on the main thread.
        /// </summary>
        protected abstract void OnUpdate();
    }

    public abstract class JobComponentSystem : ComponentSystemBase
    {
        JobHandle m_PreviousFrameDependency;
        EntityCommandBufferSystem[] m_EntityCommandBufferSystemList;

        unsafe JobHandle BeforeOnUpdate()
        {
            BeforeUpdateVersioning();

            UpdateInjectedComponentGroups();

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

            // Notify all injected barrier systems that they will need to sync on any jobs we spawned.
            // This is conservative currently - the barriers will sync on too much if we use more than one.
            for (int i = 0; i < m_EntityCommandBufferSystemList.Length; ++i)
            {
                m_EntityCommandBufferSystemList[i].AddJobHandleForProducer(outputJob);
            }

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
            for (var index = 0; index < m_JobDependencyForReadingManagers.Count && dependencyError == null; index++)
            {
                var type = m_JobDependencyForReadingManagers.p[index];
                dependencyError = CheckJobDependencies(type);
            }

            for (var index = 0; index < m_JobDependencyForWritingManagers.Count && dependencyError == null; index++)
            {
                var type = m_JobDependencyForWritingManagers.p[index];
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

#if !UNITY_ZEROPLAYER
        protected sealed override void OnBeforeCreateManagerInternal(World world)
        {
            base.OnBeforeCreateManagerInternal(world);

            m_EntityCommandBufferSystemList = ComponentSystemInjection.GetAllInjectedManagers<EntityCommandBufferSystem>(this, world);
        }
#endif
        protected sealed override void OnBeforeDestroyManagerInternal()
        {
            base.OnBeforeDestroyManagerInternal();
            m_PreviousFrameDependency.Complete();
        }

        public BufferFromEntity<T> GetBufferFromEntity<T>(bool isReadOnly = false) where T : struct, IBufferElementData
        {
            AddReaderWriter(isReadOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>());
            return EntityManager.GetBufferFromEntity<T>(isReadOnly);
        }

        protected abstract JobHandle OnUpdate(JobHandle inputDeps);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        unsafe string CheckJobDependencies(int type)
        {
            var h = m_SafetyManager.GetSafetyHandle(type, true);

            var readerCount = AtomicSafetyHandle.GetReaderArray(h, 0, IntPtr.Zero);
            JobHandle* readers = stackalloc JobHandle[readerCount];
            AtomicSafetyHandle.GetReaderArray(h, readerCount, (IntPtr) readers);

            for (var i = 0; i < readerCount; ++i)
            {
                if (!m_SafetyManager.HasReaderOrWriterDependency(type, readers[i]))
                    return $"The system {GetType()} reads {TypeManager.GetType(type)} via {AtomicSafetyHandle.GetReaderName(h, i)} but that type was not returned as a job dependency. To ensure correct behavior of other systems, the job or a dependency of it must be returned from the OnUpdate method.";
            }

            if (!m_SafetyManager.HasReaderOrWriterDependency(type, AtomicSafetyHandle.GetWriter(h)))
                return $"The system {GetType()} writes {TypeManager.GetType(type)} via {AtomicSafetyHandle.GetWriterName(h)} but that was not returned as a job dependency. To ensure correct behavior of other systems, the job or a dependency of it must be returned from the OnUpdate method.";

            return null;
        }

        unsafe void EmergencySyncAllJobs()
        {
            for (int i = 0;i != m_JobDependencyForReadingManagers.Count;i++)
            {
                int type = m_JobDependencyForReadingManagers.p[i];
                AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_SafetyManager.GetSafetyHandle(type, true));
            }

            for (int i = 0;i != m_JobDependencyForWritingManagers.Count;i++)
            {
                int type = m_JobDependencyForWritingManagers.p[i];
                AtomicSafetyHandle.EnforceAllBufferJobsHaveCompleted(m_SafetyManager.GetSafetyHandle(type, true));
            }
        }
#endif

        unsafe JobHandle GetDependency ()
        {
            return m_SafetyManager.GetDependency(m_JobDependencyForReadingManagers.p, m_JobDependencyForReadingManagers.Count, m_JobDependencyForWritingManagers.p, m_JobDependencyForWritingManagers.Count);
        }

        unsafe void AddDependencyInternal(JobHandle dependency)
        {
            m_PreviousFrameDependency = m_SafetyManager.AddDependency(m_JobDependencyForReadingManagers.p, m_JobDependencyForReadingManagers.Count, m_JobDependencyForWritingManagers.p, m_JobDependencyForWritingManagers.Count, dependency);
        }
    }

    [Obsolete("BarrierSystem has been renamed. Use EntityCommandBufferSystem instead (UnityUpgradable) -> EntityCommandBufferSystem", true)]
    [System.ComponentModel.EditorBrowsable(EditorBrowsableState.Never)]
    public struct BarrierSystem { }

    public unsafe abstract class EntityCommandBufferSystem : ComponentSystem
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private List<EntityCommandBuffer> m_PendingBuffers;
#else
        private NativeList<EntityCommandBuffer> m_PendingBuffers;
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
        ///
        /// This is usually called by a system that's building an EntityCommandBuffer created
        /// by this EntityCommandBufferSystem, to prevent the command buffer from being played back before
        /// it's complete. The general usage looks like:
        ///    MyEntityCommandBufferSystem _barrier;
        ///    // in OnCreateManager():
        ///    _barrier = World.GetOrCreateManager<MyEntityCommandBufferSystem>();
        ///    // in OnUpdate():
        ///    EntityCommandBuffer cmd = _barrier.CreateCommandBuffer();
        ///    var job = new MyProducerJob {
        ///        CommandBuffer = cmd,
        ///    }.Schedule(this, inputDeps);
        ///    _barrier.AddJobHandleForProducer(job);
        /// </summary>
        /// <param name="producerJob">A JobHandle which this barrier system should wait on before playing back its
        /// pending EntityCommandBuffers.</param>
        public void AddJobHandleForProducer(JobHandle producerJob)
        {
            m_ProducerHandle = JobHandle.CombineDependencies(m_ProducerHandle, producerJob);
        }

        protected override void OnCreateManager()
        {
            base.OnCreateManager();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_PendingBuffers = new List<EntityCommandBuffer>();
#else
            m_PendingBuffers = new NativeList<EntityCommandBuffer>(Allocator.Persistent);
#endif
        }

        protected override void OnDestroyManager()
        {
            FlushBuffers(false);

#if !ENABLE_UNITY_COLLECTIONS_CHECKS
            m_PendingBuffers.Dispose();
#endif

            base.OnDestroyManager();
        }

        protected override void OnUpdate()
        {
            FlushBuffers(true);
        }

        private void FlushBuffers(bool playBack)
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
#endif
            for (int i = 0; i < length; ++i)
            {
                var buffer = m_PendingBuffers[i];
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
                    }
#else
                    buffer.Playback(EntityManager);
#endif
                }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                try
                {
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
            }
            m_PendingBuffers.Clear();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (playbackErrorLog != null)
            {
#if !UNITY_CSHARP_TINY
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
