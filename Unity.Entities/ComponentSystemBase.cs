using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using UnityEngine.Profiling;

namespace Unity.Entities
{
    /// <summary>
    /// A system provides behavior in an ECS architecture.
    /// </summary>
    /// <remarks>
    /// System implementations should inherit <see cref="SystemBase"/>, which is a subclass of ComponentSystemBase.
    /// </remarks>
    public abstract unsafe partial class ComponentSystemBase
    {
        internal SystemState* m_StatePtr;

        internal SystemState* CheckedState()
        {
            var state = m_StatePtr;
            if (state == null)
            {
                throw new InvalidOperationException("object is not initialized or has already been destroyed");
            }
            return state;
        }

        /// <summary>
        /// Controls whether this system executes when its OnUpdate function is called.
        /// </summary>
        /// <value>True, if the system is enabled.</value>
        /// <remarks>The Enabled property is intended for debugging so that you can easily turn on and off systems
        /// from the Entity Debugger window. A system with Enabled set to false will not update, even if its
        /// <see cref="ShouldRunSystem"/> function returns true.</remarks>
        public bool Enabled { get => CheckedState()->Enabled; set => CheckedState()->Enabled = value; }

        /// <summary>
        /// The query objects cached by this system.
        /// </summary>
        /// <remarks>A system caches any queries it implicitly creates through the IJob interfaces or
        /// <see cref="EntityQueryBuilder"/>, that you create explicitly by calling <see cref="GetEntityQuery"/>, or
        /// that you add to the system as a required query with <see cref="RequireForUpdate"/>.
        /// Implicit queries may be created lazily and not exist before a system has run for the first time.</remarks>
        /// <value>A read-only array of the cached <see cref="EntityQuery"/> objects.</value>
        public EntityQuery[] EntityQueries => UnsafeListToRefArray(ref CheckedState()->EntityQueries);

        internal static EntityQuery[] UnsafeListToRefArray(ref UnsafeList<EntityQuery> objs)
        {
            EntityQuery[] result = new EntityQuery[objs.Length];
            for (int i = 0; i < result.Length; ++i)
            {
                result[i] = objs.Ptr[i];
            }
            return result;
        }

        /// <summary>
        /// The current change version number in this <see cref="World"/>.
        /// </summary>
        /// <remarks>The system updates the component version numbers inside any <see cref="ArchetypeChunk"/> instances
        /// that this system accesses with write permissions to this value.</remarks>
        public uint GlobalSystemVersion => EntityManager.GlobalSystemVersion;

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
        public uint LastSystemVersion => CheckedState()->m_LastSystemVersion;

        /// <summary>
        /// The EntityManager object of the <see cref="World"/> in which this system exists.
        /// </summary>
        /// <value>The EntityManager for this system.</value>
        public EntityManager EntityManager => CheckedState()->m_EntityManager;

        /// <summary>
        /// The World in which this system exists.
        /// </summary>
        /// <value>The World of this system.</value>
        public World World => m_StatePtr != null ? (World)m_StatePtr->m_World.Target : null;

        /// <summary>
        /// The current Time data for this system's world.
        /// </summary>
        public ref readonly TimeData Time => ref World.Time;

        // ============


        internal void CreateInstance(World world, SystemState* statePtr)
        {
            m_StatePtr = statePtr;
            OnBeforeCreateInternal(world);
            try
            {
                OnCreateForCompiler();
                OnCreate();
            }
            catch
            {
                OnBeforeDestroyInternal();
                OnAfterDestroyInternal();
                throw;
            }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        protected internal virtual void OnCreateForCompiler()
        {
            //do not remove, dots compiler will emit methods that implement this method.
        }

        internal void DestroyInstance()
        {
            OnBeforeDestroyInternal();
            OnDestroy();
            OnAfterDestroyInternal();
        }

        /// <summary>
        /// Called when this system is created.
        /// </summary>
        /// <remarks>
        /// Implement an OnCreate() function to set up system resources when it is created.
        ///
        /// OnCreate is invoked before the the first time <see cref="OnStartRunning"/> and OnUpdate are invoked.
        /// </remarks>
        protected virtual void OnCreate()
        {
        }

        /// <summary>
        /// Called before the first call to OnUpdate and when a system resumes updating after being stopped or disabled.
        /// </summary>
        /// <remarks>If the <see cref="EntityQuery"/> objects defined for a system do not match any existing entities
        /// then the system skips updates until a successful match is found. Likewise, if you set <see cref="Enabled"/>
        /// to false, then the system stops running. In both cases, <see cref="OnStopRunning"/> is
        /// called when a running system stops updating; OnStartRunning is called when it starts updating again.
        /// </remarks>
        protected virtual void OnStartRunning()
        {
        }

        /// <summary>
        /// Called when this system stops running because no entities match the system's <see cref="EntityQuery"/>
        /// objects or because you change the system <see cref="Enabled"/> property to false.
        /// </summary>
        /// <remarks>If the <see cref="EntityQuery"/> objects defined for a system do not match any existing entities
        /// then the system skips updating until a successful match is found. Likewise, if you set <see cref="Enabled"/>
        /// to false, then the system stops running. In both cases, <see cref="OnStopRunning"/> is
        /// called when a running system stops updating; OnStartRunning is called when it starts updating again.
        /// </remarks>
        protected virtual void OnStopRunning()
        {
        }

        internal virtual void OnStopRunningInternal()
        {
            OnStopRunning();
        }

        /// <summary>
        /// Called when this system is destroyed.
        /// </summary>
        /// <remarks>Systems are destroyed when the application shuts down, the World is destroyed, or you
        /// call <see cref="World.DestroySystem"/>. In the Unity Editor, system destruction occurs when you exit
        /// Play Mode and when scripts are reloaded.</remarks>
        protected virtual void OnDestroy()
        {
        }

        internal void OnDestroy_Internal()
        {
            OnDestroy();
        }

        /// <summary>
        /// Executes the system immediately.
        /// </summary>
        /// <remarks>The exact behavior is determined by this system's specific subclass.</remarks>
        /// <seealso cref="SystemBase"/>
        /// <seealso cref="ComponentSystemGroup"/>
        /// <seealso cref="EntityCommandBufferSystem"/>
        abstract public void Update();

        // ===================

        internal ComponentSystemBase GetSystemFromSystemID(World world, int systemID)
        {
            foreach (var system in world.Systems)
            {
                if (system == null) continue;

                if (system.CheckedState()->m_SystemID == systemID)
                {
                    return system;
                }
            }

            return null;
        }


#if ENABLE_PROFILER
        internal string GetProfilerMarkerName()
        {
            string name = default;
            CheckedState()->m_ProfilerMarker.GetName(ref name);
            return name;
        }
#endif

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckExists()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_StatePtr != null && World != null && World.IsCreated) return;

            throw new InvalidOperationException(
                $"System {GetType()} is invalid. This usually means it was not created with World.GetOrCreateSystem<{GetType()}>() or has already been destroyed.");
#endif
        }

        /// <summary>
        /// Reports whether any of this system's entity queries currently match any chunks. This function is used
        /// internally to determine whether the system's OnUpdate function can be skipped.
        /// </summary>
        /// <returns>True, if the queries in this system match existing entities or the system has the
        /// <see cref="AlwaysUpdateSystemAttribute"/>.</returns>
        /// <remarks>A system without any queries also returns true. Note that even if this function returns true,
        /// other factors may prevent a system from updating. For example, a system will not be updated if its
        /// <see cref="Enabled"/> property is false.</remarks>
        public bool ShouldRunSystem() => CheckedState()->ShouldRunSystem();

        internal virtual void OnBeforeCreateInternal(World world)
        {
        }

        internal void OnAfterDestroyInternal()
        {
            var state = CheckedState();
            World.Unmanaged.DestroyManagedSystem(state);
            m_StatePtr = null;
        }

        private static void DisposeQueries(ref UnsafeList<GCHandle> queries)
        {
            for (var i = 0; i < queries.Length; ++i)
            {
                var query = (EntityQuery)queries[i].Target;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                query._GetImpl()->_DisallowDisposing = false;
#endif
                query.Dispose();
                queries[i].Free();
            }
        }

        internal virtual void OnBeforeDestroyInternal()
        {
            var state = CheckedState();

            if (state->PreviouslyEnabled)
            {
                state->PreviouslyEnabled = false;
                OnStopRunning();
            }
        }

        internal void BeforeUpdateVersioning()
        {
            var state = CheckedState();
            state->m_EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
            ref var qs = ref state->EntityQueries;
            for (int i = 0; i < qs.Length; ++i)
            {
                qs[i].SetChangedFilterRequiredVersion(state->m_LastSystemVersion);
            }
        }

        internal void AfterUpdateVersioning()
        {
            CheckedState()->m_LastSystemVersion = EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->GlobalSystemVersion;
        }

        internal void CompleteDependencyInternal()
        {
            CheckedState()->CompleteDependencyInternal();
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
        public ComponentTypeHandle<T> GetComponentTypeHandle<T>(bool isReadOnly = false) where T : struct, IComponentData
        {
            return CheckedState()->GetComponentTypeHandle<T>(isReadOnly);
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
            return CheckedState()->GetDynamicComponentTypeHandle(componentType);
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
        public BufferTypeHandle<T> GetBufferTypeHandle<T>(bool isReadOnly = false)
            where T : struct, IBufferElementData
        {
            return CheckedState()->GetBufferTypeHandle<T>(isReadOnly);
        }

        /// <summary>
        /// Gets the run-time type information required to access a shared component data in a chunk.
        /// </summary>
        /// <typeparam name="T">A struct that implements <see cref="ISharedComponentData"/>.</typeparam>
        /// <returns>An object representing the type information required to safely access shared component data stored in a
        /// chunk.</returns>
        public SharedComponentTypeHandle<T> GetSharedComponentTypeHandle<T>()
            where T : struct, ISharedComponentData
        {
            return CheckedState()->GetSharedComponentTypeHandle<T>();
        }

        /// <summary>
        /// Gets the run-time type information required to access a shared component data in a chunk.
        /// </summary>
        /// <param name="componentType">The component type to get access to.</param>
        /// <returns>An object representing the type information required to safely access shared component data stored in a
        /// chunk.</returns>
        public DynamicSharedComponentTypeHandle GetDynamicSharedComponentTypeHandle(ComponentType componentType)
        {
            return CheckedState()->GetDynamicSharedComponentTypeHandle(componentType);
        }

        /// <summary>
        /// Gets the run-time type information required to access the array of <see cref="Entity"/> objects in a chunk.
        /// </summary>
        /// <returns>An object representing the type information required to safely access Entity instances stored in a
        /// chunk.</returns>
        public EntityTypeHandle GetEntityTypeHandle()
        {
            return CheckedState()->GetEntityTypeHandle();
        }

        /// <summary>
        /// Gets an dictionary-like container containing all components of type T, keyed by Entity.
        /// </summary>
        /// <param name="isReadOnly">Whether the data is only read, not written. Access data as
        /// read-only whenever possible.</param>
        /// <typeparam name="T">A struct that implements <see cref="IComponentData"/>.</typeparam>
        /// <returns>All component data of type T.</returns>
        public ComponentDataFromEntity<T> GetComponentDataFromEntity<T>(bool isReadOnly = false)
            where T : struct, IComponentData
        {
            return CheckedState()->GetComponentDataFromEntity<T>(isReadOnly);
        }

        /// <summary>
        /// Gets a BufferFromEntity&lt;T&gt; object that can access a <seealso cref="DynamicBuffer{T}"/>.
        /// </summary>
        /// <remarks>Assign the returned object to a field of your Job struct so that you can access the
        /// contents of the buffer in a Job.</remarks>
        /// <param name="isReadOnly">Whether the buffer data is only read or is also written. Access data in
        /// a read-only fashion whenever possible.</param>
        /// <typeparam name="T">The type of <see cref="IBufferElementData"/> stored in the buffer.</typeparam>
        /// <returns>An array-like object that provides access to buffers, indexed by <see cref="Entity"/>.</returns>
        /// <seealso cref="ComponentDataFromEntity{T}"/>
        public BufferFromEntity<T> GetBufferFromEntity<T>(bool isReadOnly = false) where T : struct, IBufferElementData
        {
            return CheckedState()->GetBufferFromEntity<T>(isReadOnly);
        }

        /// <summary>
        /// Adds a query that must return entities for the system to run. You can add multiple required queries to a
        /// system; all of them must match at least one entity for the system to run.
        /// </summary>
        /// <param name="query">A query that must match entities this frame in order for this system to run.</param>
        /// <remarks>Any queries added through RequireforUpdate override all other queries cached by this system.
        /// In other words, if any required query does not find matching entities, the update is skipped even
        /// if another query created for the system (either explicitly or implicitly) does match entities and
        /// vice versa.</remarks>
        public void RequireForUpdate(EntityQuery query)
        {
            CheckedState()->RequireForUpdate(query);
        }

        /// <summary>
        /// Require that a specific singleton component exist for this system to run.
        /// </summary>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.</typeparam>
        public void RequireSingletonForUpdate<T>()
        {
            CheckedState()->RequireSingletonForUpdate<T>();
        }

        /// <summary>
        /// Checks whether a singelton component of the specified type exists.
        /// </summary>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.</typeparam>
        /// <returns>True, if a singleton of the specified type exists in the current <see cref="World"/>.</returns>
        public bool HasSingleton<T>()
        {
            return CheckedState()->HasSingleton<T>();
        }

        /// <summary>
        /// Gets the value of a singleton component.
        /// </summary>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.</typeparam>
        /// <returns>The component.</returns>
        /// <seealso cref="EntityQuery.GetSingleton{T}"/>
        public T GetSingleton<T>()
            where T : struct, IComponentData
        {
            return CheckedState()->GetSingleton<T>();
        }

        /// <summary>
        /// Gets the value of a singleton component, and returns whether or not a singleton component of the specified type exists in the <see cref="World"/>.
        /// </summary>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.</typeparam>
        /// <typeparam name="value">The component. if an <see cref="Entity"/> with the specified type does not exist in the <see cref="World"/>, this is assigned a default value</typeparam>
        /// <returns>True, if exactly one <see cref="Entity"/> exists in the <see cref="World"/> with the provided component type.</returns>
        public bool TryGetSingleton<T>(out T value)
            where T : struct, IComponentData
        {
            return CheckedState()->TryGetSingleton<T>(out value);
        }

        /// <summary>
        /// Sets the value of a singleton component.
        /// </summary>
        /// <param name="value">A component containing the value to assign to the singleton.</param>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.</typeparam>
        /// <seealso cref="EntityQuery.SetSingleton{T}"/>
        public void SetSingleton<T>(T value)
            where T : struct, IComponentData
        {
            CheckedState()->SetSingleton<T>(value);
        }

        /// <summary>
        /// Gets the Entity instance for a singleton.
        /// </summary>
        /// <typeparam name="T">The Type of the singleton component.</typeparam>
        /// <returns>The entity associated with the specified singleton component.</returns>
        /// <seealso cref="EntityQuery.GetSingletonEntity"/>
        public Entity GetSingletonEntity<T>()
        {
            return CheckedState()->GetSingletonEntity<T>();
        }

        /// <summary>
        /// Gets the singleton Entity, and returns whether or not a singleton <see cref="Entity"/> of the specified type exists in the <see cref="World"/>.
        /// </summary>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.</typeparam>
        /// <typeparam name="value">The <see cref="Entity"/> associated with the specified singleton component.
        ///  If a singleton of the specified types does not exist in the current <see cref="World"/>, this is set to Entity.Null</typeparam>
        /// <returns>True, if exactly one <see cref="Entity"/> exists in the <see cref="World"/> with the provided component type.</returns>
        public bool TryGetSingletonEntity<T>(out Entity value)
        {
            return CheckedState()->TryGetSingletonEntity<T>(out value);
        }

        // Fast path for singletons
        internal EntityQuery GetSingletonEntityQueryInternal(ComponentType type)
        {
            return CheckedState()->GetSingletonEntityQueryInternal(type);
        }

        internal EntityQuery GetEntityQueryInternal(ComponentType* componentTypes, int count)
        {
            return CheckedState()->GetEntityQueryInternal(componentTypes, count);
        }

        internal EntityQuery GetEntityQueryInternal(ComponentType[] componentTypes)
        {
            fixed(ComponentType* componentTypesPtr = componentTypes)
            {
                return GetEntityQueryInternal(componentTypesPtr, componentTypes.Length);
            }
        }

        internal EntityQuery GetEntityQueryInternal(EntityQueryDesc[] desc)
        {
            return CheckedState()->GetEntityQueryInternal(desc);
        }

        /// <summary>
        /// Gets the cached query for the specified component types, if one exists; otherwise, creates a new query
        /// instance and caches it.
        /// </summary>
        /// <param name="componentTypes">An array or comma-separated list of component types.</param>
        /// <returns>The new or cached query.</returns>
        protected internal EntityQuery GetEntityQuery(params ComponentType[] componentTypes)
        {
            return GetEntityQueryInternal(componentTypes);
        }

        /// <summary>
        /// Gets the cached query for the specified component types, if one exists; otherwise, creates a new query
        /// instance and caches it.
        /// </summary>
        /// <param name="componentTypes">An array of component types.</param>
        /// <returns>The new or cached query.</returns>
        protected EntityQuery GetEntityQuery(NativeArray<ComponentType> componentTypes)
        {
            return GetEntityQueryInternal((ComponentType*)componentTypes.GetUnsafeReadOnlyPtr(),
                componentTypes.Length);
        }

        /// <summary>
        /// Combines an array of query description objects into a single query.
        /// </summary>
        /// <remarks>This function looks for a cached query matching the combined query descriptions, and returns it
        /// if one exists; otherwise, the function creates a new query instance and caches it.</remarks>
        /// <returns>The new or cached query.</returns>
        /// <param name="queryDesc">An array of query description objects to be combined to define the query.</param>
        protected internal EntityQuery GetEntityQuery(params EntityQueryDesc[] queryDesc)
        {
            return GetEntityQueryInternal(queryDesc);
        }

#if UNITY_ENTITIES_RUNTIME_TOOLING
        /// <summary>
        /// Return the Stopwatch ticks at the start of when this system last actually executed.
        /// Only available with UNITY_ENTITIES_RUNTIME_TOOLING defined
        /// </summary>
        public long SystemStartTicks => this.m_StatePtr->m_LastSystemStartTime;

        /// <summary>
        /// Return the Stopwatch ticks at the end of when this system last actually executed.
        /// Only available with UNITY_ENTITIES_RUNTIME_TOOLING defined
        /// </summary>
        public long SystemEndTicks => this.m_StatePtr->m_LastSystemEndTime;

        /// <summary>
        /// Return the Stopwatch ticks for how long this system ran the last time Update() was called.
        /// If the system was disabled or didn't run due to no matching queries at last Update(), 0
        /// is returned.
        /// Only available with UNITY_ENTITIES_RUNTIME_TOOLING defined
        /// </summary>
        public long SystemElapsedTicks
        {
            get
            {
                if (!this.m_StatePtr->m_RanLastUpdate)
                    return 0;

                return SystemEndTicks - SystemStartTicks;
            }
        }

        /// <summary>
        /// Return SystemElapsedTicks converted to float milliseconds.
        /// Only available with UNITY_ENTITIES_RUNTIME_TOOLING defined
        /// </summary>
        public float SystemElapsedMilliseconds => (float) (SystemElapsedTicks * 1000.0 / Stopwatch.Frequency);
#endif
    }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
    public static unsafe class ComponentSystemBaseManagedComponentExtensions
    {
        /// <summary>
        /// Checks whether a singleton component of the specified type exists.
        /// </summary>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.</typeparam>
        /// <returns>True, if a singleton of the specified type exists in the current <see cref="World"/>.</returns>
        public static bool HasSingleton<T>(this ComponentSystemBase sys) where T : class, IComponentData
        {
            var type = ComponentType.ReadOnly<T>();
            var query = sys.GetSingletonEntityQueryInternal(type);
            return query.CalculateEntityCount() == 1;
        }

        /// <summary>
        /// Gets the value of a singleton component.
        /// </summary>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.</typeparam>
        /// <returns>The component.</returns>
        /// <seealso cref="EntityQuery.GetSingleton{T}"/>
        public static T GetSingleton<T>(this ComponentSystemBase sys) where T : class, IComponentData
        {
            var type = ComponentType.ReadOnly<T>();
            var query = sys.GetSingletonEntityQueryInternal(type);
            return query.GetSingleton<T>();
        }

        /// <summary>
        /// Sets the value of a singleton component.
        /// </summary>
        /// <param name="value">A component containing the value to assign to the singleton.</param>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the singleton component.</typeparam>
        /// <seealso cref="EntityQuery.SetSingleton{T}"/>
        public static void SetSingleton<T>(this ComponentSystemBase sys, T value) where T : class, IComponentData
        {
            var type = ComponentType.ReadWrite<T>();
            var query = sys.GetSingletonEntityQueryInternal(type);
            query.SetSingleton(value);
        }
    }
#endif
}
