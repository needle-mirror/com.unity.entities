using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Serialization;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine.Profiling;
using UnityEngine.Scripting;
using JetBrains.Annotations;
using Unity.Profiling;

[assembly: InternalsVisibleTo("Unity.Entities.Hybrid")]
[assembly: InternalsVisibleTo("Unity.Tiny.Core")]

namespace Unity.Entities
{
    [AttributeUsage(AttributeTargets.Method)]
    internal class MonoPInvokeCallbackAttribute : Attribute
    {
        internal MonoPInvokeCallbackAttribute(Type type) {}
    }

    /// <summary>
    /// The EntityManager manages entities and components in a World.
    /// </summary>
    /// <remarks>
    /// The EntityManager provides an API to create, read, update, and destroy entities.
    ///
    /// A <see cref="World"/> has one EntityManager, which manages all the entities for that World.
    ///
    /// Many EntityManager operations result in *structural changes* that change the layout of entities in memory.
    /// Before it can perform such operations, the EntityManager must wait for all running Jobs to complete, an event
    /// called a *sync point*. A sync point both blocks the main thread and prevents the application from taking
    /// advantage of all available cores as the running Jobs wind down.
    ///
    /// Although you cannot prevent sync points entirely, you should avoid them as much as possible. To this end, the ECS
    /// framework provides the <see cref="EntityCommandBuffer"/>, which allows you to queue structural changes so that
    /// they all occur at one time in the frame.
    /// </remarks>
    [Preserve]
    [NativeContainer]
    [DebuggerTypeProxy(typeof(EntityManagerDebugView))]
    [BurstCompatible]
    public unsafe partial struct EntityManager : IEquatable<EntityManager>
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;

        private static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<EntityManager>();
        [BurstDiscard]
        private static void CreateStaticSafetyId()
        {
            s_staticSafetyId.Data = AtomicSafetyHandle.NewStaticSafetyId<EntityManager>();
        }
        private byte m_IsInExclusiveTransaction;
        private bool IsInExclusiveTransaction => m_IsInExclusiveTransaction != 0;
#endif

        static readonly ProfilerMarker k_ProfileMoveSharedComponents = new ProfilerMarker("MoveSharedComponents");
        static readonly ProfilerMarker k_ProfileMoveManagedComponents = new ProfilerMarker("MoveManagedComponents");

        [NativeDisableUnsafePtrRestriction]
        private EntityDataAccess* m_EntityDataAccess;

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void AssertIsExclusiveTransaction()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (IsInExclusiveTransaction == !m_EntityDataAccess->IsInExclusiveTransaction)
            {
                if (IsInExclusiveTransaction)
                    throw new InvalidOperationException("EntityManager cannot be used from this context because it is part of an exclusive transaction that has already ended.");
                throw new InvalidOperationException("EntityManager cannot be used from this context because it is not part of the exclusive transaction that is currently active.");
            }
#endif
        }

        internal EntityDataAccess* GetCheckedEntityDataAccess()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            AssertIsExclusiveTransaction();
            return m_EntityDataAccess;
        }

        internal EntityDataAccess* GetUncheckedEntityDataAccess()
        {
            return m_EntityDataAccess;
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [BurstCompatible(RequiredUnityDefine = "ENABLE_UNITY_COLLECTIONS_CHECKS", CompileTarget = BurstCompatibleAttribute.BurstCompatibleCompileTarget.Editor)]
        internal bool IsInsideForEach => GetCheckedEntityDataAccess()->m_InsideForEach != 0;

        internal struct InsideForEach : IDisposable
        {
            EntityManager m_Manager;
            int m_InsideForEachSafety;

            public InsideForEach(EntityManager manager)
            {
                m_Manager = manager;
                EntityDataAccess* g = manager.GetCheckedEntityDataAccess();
                m_InsideForEachSafety = g->m_InsideForEach++;
            }

            public void Dispose()
            {
                EntityDataAccess* g = m_Manager.GetCheckedEntityDataAccess();
                int newValue = --g->m_InsideForEach;
                if (m_InsideForEachSafety != newValue)
                {
                    throw new InvalidOperationException("for each unbalanced");
                }
            }
        }
#endif

        // Attribute to indicate an EntityManager method makes structural changes.
        // Do not remove form EntityManager and please apply to all appropriate methods.
        [AttributeUsage(AttributeTargets.Method)]
        private class StructuralChangeMethodAttribute : Attribute
        {
        }

        /// <summary>
        /// The <see cref="World"/> of this EntityManager.
        /// </summary>
        /// <value>A World has one EntityManager and an EntityManager manages the entities of one World.</value>
        [NotBurstCompatible]
        public World World => GetCheckedEntityDataAccess()->ManagedEntityDataAccess.m_World;

        /// <summary>
        /// The latest entity generational version.
        /// </summary>
        /// <value>This is the version number that is assigned to a new entity. See <see cref="Entity.Version"/>.</value>
        public int Version => GetCheckedEntityDataAccess()->EntityComponentStore->EntityOrderVersion;

        /// <summary>
        /// A counter that increments after every system update.
        /// </summary>
        /// <remarks>
        /// The ECS framework uses the GlobalSystemVersion to track changes in a conservative, efficient fashion.
        /// Changes are recorded per component per chunk.
        /// </remarks>
        /// <seealso cref="ArchetypeChunk.DidChange"/>
        /// <seealso cref="ChangedFilterAttribute"/>
        public uint GlobalSystemVersion => GetCheckedEntityDataAccess()->EntityComponentStore->GlobalSystemVersion;

        /// <summary>
        /// The capacity of the internal entities array.
        /// </summary>
        /// <value>The number of entities the array can hold before it must be resized.</value>
        /// <remarks>
        /// The entities array automatically resizes itself when the entity count approaches the capacity.
        /// You should rarely need to set this value directly.
        ///
        /// **Important:** when you set this value (or when the array automatically resizes), the EntityManager
        /// first ensures that all Jobs finish. This can prevent the Job scheduler from utilizing available CPU
        /// cores and threads, resulting in a temporary performance drop.
        /// </remarks>
        public int EntityCapacity => GetCheckedEntityDataAccess()->EntityComponentStore->EntitiesCapacity;

        /// <summary>
        /// A EntityQuery instance that matches all components.
        /// </summary>
        public EntityQuery UniversalQuery => GetCheckedEntityDataAccess()->m_UniversalQuery;

        /// <summary>
        /// An object providing debugging information and operations.
        /// </summary>
        public EntityManagerDebug Debug => new EntityManagerDebug(this);

        /// <summary>
        /// The total reserved address space for all Chunks in all Worlds.
        /// </summary>
        public static ulong TotalChunkAddressSpaceInBytes
        {
            get => Entities.EntityComponentStore.TotalChunkAddressSpaceInBytes;
            set => Entities.EntityComponentStore.TotalChunkAddressSpaceInBytes = value;
        }

        [NotBurstCompatible]
        internal void Initialize(World world)
        {
#pragma warning disable 0618
            WordStorage.Initialize();
#pragma warning restore 0618
            TypeManager.Initialize();
            StructuralChange.Initialize();
            ECBInterop.Initialize();
            Entities.EntityComponentStore.Initialize();
            ChunkIterationUtility.Initialize();
            EntityQueryImpl.Initialize();
            SerializeUtilityInterop.Initialize();

            // Pick any recorded types that have come in after a domain reload.
            EarlyInitHelpers.FlushEarlyInits();
            SystemBaseRegistry.InitializePendingTypes();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();

            if (s_staticSafetyId.Data == 0)
            {
                CreateStaticSafetyId();
            }
            AtomicSafetyHandle.SetStaticSafetyId(ref m_Safety, s_staticSafetyId.Data);

            m_IsInExclusiveTransaction = 0;

            // Install a panic function, which helps with generating good error messages blaming the correct system when things go wrong.
            // The way this works it that if a schedule or atomic safety handle validation fails in the native code, it will
            // call out to a user panic function, which can try to log some more information and then the native code will retry the operation.
            // Here we figure out if some other system was to blame, and if so we synchronize all outstanding jobs to make the downstream
            // systems keep running.
#if !UNITY_DOTSRUNTIME
            if (JobsUtility.PanicFunction == null)
            {
                JobsUtility.PanicFunction = () =>
                {
                    SystemState* state = SystemState.GetCurrentSystemFromJobDebugger();
                    if (state != null)
                    {
                        state->LogSafetyErrors();
                    }
                };
            }
#endif
#endif
            m_EntityDataAccess = (EntityDataAccess*)Memory.Unmanaged.Allocate(sizeof(EntityDataAccess), 16, Allocator.Persistent);
            UnsafeUtility.MemClear(m_EntityDataAccess, sizeof(EntityDataAccess));
            EntityDataAccess.Initialize(m_EntityDataAccess, world);
        }

        internal void PreDisposeCheck()
        {
            EndExclusiveEntityTransaction();
            GetCheckedEntityDataAccess()->DependencyManager->PreDisposeCheck();
        }

        [NotBurstCompatible]
        internal void DestroyInstance()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

            PreDisposeCheck();

            GetCheckedEntityDataAccess()->Dispose();
            Memory.Unmanaged.Free(m_EntityDataAccess, Allocator.Persistent);
            m_EntityDataAccess = null;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.Release(m_Safety);
            m_Safety = default;
#endif
        }

        internal static EntityManager CreateEntityManagerInUninitializedState()
        {
            return new EntityManager();
        }

        public bool Equals(EntityManager other)
        {
            return m_EntityDataAccess == other.m_EntityDataAccess;
        }

        [NotBurstCompatible]
        public override bool Equals(object obj)
        {
            return obj is EntityManager other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((int)(long)m_EntityDataAccess);
        }

        public static bool operator==(EntityManager lhs, EntityManager rhs)
        {
            return lhs.m_EntityDataAccess == rhs.m_EntityDataAccess;
        }

        public static bool operator!=(EntityManager lhs, EntityManager rhs)
        {
            return lhs.m_EntityDataAccess != rhs.m_EntityDataAccess;
        }
    // ----------------------------------------------------------------------------------------------------------
        // PUBLIC
        // ----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Gets the number of shared components managed by this EntityManager.
        /// </summary>
        /// <returns>The shared component count</returns>
        [NotBurstCompatible]
        public int GetSharedComponentCount()
        {
            var access = GetCheckedEntityDataAccess();
            return access->ManagedComponentStore.GetSharedComponentCount();
        }

        /// <summary>
        /// Gets the value of a component for an entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <typeparam name="T">The type of component to retrieve.</typeparam>
        /// <returns>A struct of type T containing the component value.</returns>
        /// <exception cref="ArgumentException">Thrown if the component type has no fields.</exception>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public T GetComponentData<T>(Entity entity) where T : struct, IComponentData
        {
            var access = GetCheckedEntityDataAccess();
            return access->GetComponentData<T>(entity);
        }

        /// <summary>
        /// Sets the value of a component of an entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="componentData">The data to set.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <exception cref="ArgumentException">Thrown if the component type has no fields.</exception>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public void SetComponentData<T>(Entity entity, T componentData) where T : struct, IComponentData
        {
            var access = GetCheckedEntityDataAccess();
            access->SetComponentData(entity, componentData);
        }

        /// <summary>
        /// Gets the value of a chunk component.
        /// </summary>
        /// <remarks>
        /// A chunk component is common to all entities in a chunk. You can access a chunk <see cref="IComponentData"/>
        /// instance through either the chunk itself or through an entity stored in that chunk.
        /// </remarks>
        /// <param name="chunk">The chunk.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <returns>A struct of type T containing the component value.</returns>
        /// <exception cref="ArgumentException">Thrown if the ArchetypeChunk object is invalid.</exception>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public T GetChunkComponentData<T>(ArchetypeChunk chunk) where T : struct, IComponentData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (chunk.Invalid())
                throw new System.ArgumentException(
                    $"GetChunkComponentData<{typeof(T)}> can not be called with an invalid archetype chunk.");
#endif
            var metaChunkEntity = chunk.m_Chunk->metaChunkEntity;
            return GetComponentData<T>(metaChunkEntity);
        }

        /// <summary>
        /// Gets the value of chunk component for the chunk containing the specified entity.
        /// </summary>
        /// <remarks>
        /// A chunk component is common to all entities in a chunk. You can access a chunk <see cref="IComponentData"/>
        /// instance through either the chunk itself or through an entity stored in that chunk.
        /// </remarks>
        /// <param name="entity">The entity.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <returns>A struct of type T containing the component value.</returns>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public T GetChunkComponentData<T>(Entity entity) where T : struct, IComponentData
        {
            var access = GetCheckedEntityDataAccess();
            var store = access->EntityComponentStore;
            store->AssertEntitiesExist(&entity, 1);
            var chunk = store->GetChunk(entity);
            var metaChunkEntity = chunk->metaChunkEntity;
            return access->GetComponentData<T>(metaChunkEntity);
        }

        /// <summary>
        /// Sets the value of a chunk component.
        /// </summary>
        /// <remarks>
        /// A chunk component is common to all entities in a chunk. You can access a chunk <see cref="IComponentData"/>
        /// instance through either the chunk itself or through an entity stored in that chunk.
        /// </remarks>
        /// <param name="chunk">The chunk to modify.</param>
        /// <param name="componentValue">The component data to set.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <exception cref="ArgumentException">Thrown if the ArchetypeChunk object is invalid.</exception>
        [StructuralChangeMethod]
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public void SetChunkComponentData<T>(ArchetypeChunk chunk, T componentValue) where T : struct, IComponentData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (chunk.Invalid())
                throw new System.ArgumentException(
                    $"SetChunkComponentData<{typeof(T)}> can not be called with an invalid archetype chunk.");
#endif
            var metaChunkEntity = chunk.m_Chunk->metaChunkEntity;
            SetComponentData<T>(metaChunkEntity, componentValue);
        }

        /// <summary>
        /// Gets the managed [UnityEngine.Component](https://docs.unity3d.com/ScriptReference/Component.html) object
        /// from an entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <typeparam name="T">The type of the managed object.</typeparam>
        /// <returns>The managed object, cast to type T.</returns>
        [NotBurstCompatible]
        public T GetComponentObject<T>(Entity entity)
        {
            var access = GetCheckedEntityDataAccess();
            return access->GetComponentObject<T>(entity, ComponentType.ReadWrite<T>());
        }

        [NotBurstCompatible]
        public T GetComponentObject<T>(Entity entity, ComponentType componentType)
        {
            var access = GetCheckedEntityDataAccess();
            return access->GetComponentObject<T>(entity, componentType);
        }

        /// <summary>
        /// Sets the shared component of an entity.
        /// </summary>
        /// <remarks>
        /// Changing a shared component value of an entity results in the entity being moved to a
        /// different chunk. The entity moves to a chunk with other entities that have the same shared component values.
        /// A new chunk is created if no chunk with the same archetype and shared component values currently exists.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before setting the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entity">The entity</param>
        /// <param name="componentData">A shared component object containing the values to set.</param>
        /// <typeparam name="T">The shared component type.</typeparam>
        [StructuralChangeMethod]
        [NotBurstCompatible]
        public void SetSharedComponentData<T>(Entity entity, T componentData) where T : struct, ISharedComponentData
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            access->SetSharedComponentData(entity, componentData);
            access->EndStructuralChanges(ref changes);
        }

        /// <summary>
        /// Sets the shared component of all entities in the query.
        /// </summary>
        /// <remarks>
        /// The component data stays in the same chunk, the internal shared component data indices will be adjusted.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before setting the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entity">The entity</param>
        /// <param name="componentData">A shared component object containing the values to set.</param>
        /// <typeparam name="T">The shared component type.</typeparam>
        [StructuralChangeMethod]
        [NotBurstCompatible]
        public void SetSharedComponentData<T>(EntityQuery query, T componentData) where T : struct, ISharedComponentData
        {
            var access = GetCheckedEntityDataAccess();
            access->AssertQueryIsValid(query);
            using (var chunks = query.CreateArchetypeChunkArray(Allocator.TempJob))
            {
                if (chunks.Length == 0)
                    return;

                var changes = access->BeginStructuralChanges();

                var type = ComponentType.ReadWrite<T>();
                access->RemoveComponentDuringStructuralChange(chunks, type);

                int sharedComponentIndex = access->InsertSharedComponent(componentData);
                access->AddSharedComponentDataDuringStructuralChange(chunks, sharedComponentIndex, type);
                access->EndStructuralChanges(ref changes);
            }
        }

        /// <summary>
        /// Gets a shared component from an entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <typeparam name="T">The type of shared component.</typeparam>
        /// <returns>A copy of the shared component.</returns>
        [NotBurstCompatible]
        public T GetSharedComponentData<T>(Entity entity) where T : struct, ISharedComponentData
        {
            var access = GetCheckedEntityDataAccess();
            return access->GetSharedComponentData<T>(entity);
        }

        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleSharedComponentData) })]
        public int GetSharedComponentDataIndex<T>(Entity entity) where T : struct, ISharedComponentData
        {
            var ecs = GetCheckedEntityDataAccess()->EntityComponentStore;
            var typeIndex = TypeManager.GetTypeIndex<T>();
            ecs->AssertEntityHasComponent(entity, typeIndex);
            return ecs->GetSharedComponentDataIndex(entity, typeIndex);
        }

        /// <summary>
        /// Gets a shared component by index.
        /// </summary>
        /// <remarks>
        /// The ECS framework maintains an internal list of unique shared components. You can get the components in this
        /// list, along with their indices using
        /// <see cref="GetAllUniqueSharedComponentData{T}(List{T},List{int})"/>. An
        /// index in the list is valid and points to the same shared component index as long as the shared component
        /// order version from <see cref="GetSharedComponentOrderVersion{T}(T)"/> remains the same.
        /// </remarks>
        /// <param name="sharedComponentIndex">The index of the shared component in the internal shared component
        /// list.</param>
        /// <typeparam name="T">The data type of the shared component.</typeparam>
        /// <returns>A copy of the shared component.</returns>
        [NotBurstCompatible]
        public T GetSharedComponentData<T>(int sharedComponentIndex) where T : struct, ISharedComponentData
        {
            var access = GetCheckedEntityDataAccess();
            return access->GetSharedComponentData<T>(sharedComponentIndex);
        }

        [NotBurstCompatible]
        public object GetSharedComponentDataBoxed(int sharedComponentIndex, int typeIndex)
        {
            var access = GetCheckedEntityDataAccess();
            var mcs = access->ManagedComponentStore;
            return mcs.GetSharedComponentDataBoxed(sharedComponentIndex, typeIndex);
        }

        /// <summary>
        /// Gets a list of all the unique instances of a shared component type.
        /// </summary>
        /// <remarks>
        /// All entities with the same archetype and the same values for a shared component are stored in the same set
        /// of chunks. This function finds the unique shared components existing across chunks and archetype and
        /// fills a list with copies of those components.
        /// </remarks>
        /// <param name="sharedComponentValues">A List&lt;T&gt; object to receive the unique instances of the
        /// shared component of type T.</param>
        /// <typeparam name="T">The type of shared component.</typeparam>
        [NotBurstCompatible]
        public void GetAllUniqueSharedComponentData<T>(List<T> sharedComponentValues)
            where T : struct, ISharedComponentData
        {
            var access = GetCheckedEntityDataAccess();
            access->GetAllUniqueSharedComponents(sharedComponentValues);
        }

        /// <summary>
        /// Gets a list of all unique shared components of the same type and a corresponding list of indices into the
        /// internal shared component list.
        /// </summary>
        /// <remarks>
        /// All entities with the same archetype and the same values for a shared component are stored in the same set
        /// of chunks. This function finds the unique shared components existing across chunks and archetype and
        /// fills a list with copies of those components and fills in a separate list with the indices of those components
        /// in the internal shared component list. You can use the indices to ask the same shared components directly
        /// by calling <see cref="GetSharedComponentData{T}(int)"/>, passing in the index. An index remains valid until
        /// the shared component order version changes. Check this version using
        /// <see cref="GetSharedComponentOrderVersion{T}(T)"/>.
        /// </remarks>
        /// <param name="sharedComponentValues"></param>
        /// <param name="sharedComponentIndices"></param>
        /// <typeparam name="T"></typeparam>
        [NotBurstCompatible]
        public void GetAllUniqueSharedComponentData<T>(List<T> sharedComponentValues, List<int> sharedComponentIndices)
            where T : struct, ISharedComponentData
        {
            var access = GetCheckedEntityDataAccess();
            access->GetAllUniqueSharedComponents(sharedComponentValues, sharedComponentIndices);
        }

        /// <summary>
        /// Gets the dynamic buffer of an entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="isReadOnly">Specify whether the access to the component through this object is read only
        /// or read and write. </param>
        /// <typeparam name="T">The type of the buffer's elements.</typeparam>
        /// <returns>The DynamicBuffer object for accessing the buffer contents.</returns>
        /// <exception cref="ArgumentException">Thrown if T is an unsupported type.</exception>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleBufferElement) })]
        public DynamicBuffer<T> GetBuffer<T>(Entity entity, bool isReadOnly = false) where T : struct, IBufferElementData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var access = GetCheckedEntityDataAccess();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safetyHandles = &access->DependencyManager->Safety;
#endif

            return access->GetBuffer<T>(entity
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                , safetyHandles->GetSafetyHandle(typeIndex, isReadOnly),
                safetyHandles->GetBufferSafetyHandle(typeIndex), isReadOnly
#endif
            );
        }

        /// <summary>
        /// Gets a struct containing information about how an entity is stored.
        /// </summary>
        /// <param name="entity">The entity being queried for storage information.</param>
        public EntityStorageInfo GetStorageInfo(Entity entity)
        {
            var access = GetCheckedEntityDataAccess();
            return access->GetStorageInfo(entity);
        }

        /// <summary>
        /// Swaps the components of two entities.
        /// </summary>
        /// <remarks>
        /// The entities must have the same components. However, this function can swap the components of entities in
        /// different worlds, so they do not need to have identical archetype instances.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before swapping the components and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="leftChunk">A chunk containing one of the entities to swap.</param>
        /// <param name="leftIndex">The index within the `leftChunk` of the entity and components to swap. Must be in
        /// the range [0,leftChunk.Count).</param>
        /// <param name="rightChunk">The chunk containing the other entity to swap. This chunk can be the same as
        /// the `leftChunk`. It also does not need to be in the same World as `leftChunk`.</param>
        /// <param name="rightIndex">The index within the `rightChunk`  of the entity and components to swap. Must be in
        /// the range [0,rightChunk.Count).</param>
        [StructuralChangeMethod]
        public void SwapComponents(ArchetypeChunk leftChunk, int leftIndex, ArchetypeChunk rightChunk, int rightIndex)
        {
            var access = GetCheckedEntityDataAccess();
            access->SwapComponents(leftChunk, leftIndex, rightChunk, rightIndex);
        }

        /// <summary>
        /// Gets the chunk in which the specified entity is stored.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The chunk containing the entity.</returns>
        public ArchetypeChunk GetChunk(Entity entity)
        {
            var ecs = GetCheckedEntityDataAccess()->EntityComponentStore;
            var chunk = ecs->GetChunk(entity);
            return new ArchetypeChunk(chunk, ecs);
        }

        /// <summary>
        /// Gets the number of component types associated with an entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The number of components.</returns>
        public int GetComponentCount(Entity entity)
        {
            var ecs = GetCheckedEntityDataAccess()->EntityComponentStore;
            ecs->AssertEntitiesExist(&entity, 1);
            var archetype = ecs->GetArchetype(entity);
            return archetype->TypesCount - 1;
        }

        /// <summary>
        /// Adds a component to an entity.
        /// </summary>
        /// <remarks>
        /// Can add any kind of component except chunk components. For chunk
        /// components, use <see cref="AddChunkComponentData"/>.
        ///
        /// Adding a component changes the entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// The added component has the default values for the type.
        ///
        /// If the <see cref="Entity"/> object refers to an entity that already has the specified <see cref="ComponentType"/>,
        /// the function returns false without performing any modifications.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The Entity.</param>
        /// <param name="componentType">The type of component to add.</param>
        [StructuralChangeMethod]
        public bool AddComponent(Entity entity, ComponentType componentType)
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            var result = access->AddComponentDuringStructuralChange(entity, componentType);
            access->EndStructuralChanges(ref changes);
            return result;
        }

        /// <summary>
        /// Adds a component to an entity.
        /// </summary>
        /// <remarks>
        /// Can add any kind of component except chunk components. For chunk
        /// components, use <see cref="AddChunkComponentData"/>.
        ///
        /// Adding a component changes the entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// The added component has the default values for the type.
        ///
        /// If the <see cref="Entity"/> object refers to an entity that already has the specified <see cref="ComponentType"/>,
        /// the function returns false without performing any modifications.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The Entity.</param>
        /// <typeparam name="T">The type of component to add.</typeparam>
        [StructuralChangeMethod]
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public bool AddComponent<T>(Entity entity)
        {
            return AddComponent(entity, ComponentType.ReadWrite<T>());
        }

        /// <summary>
        /// Adds a component to a set of entities defined by a EntityQuery.
        /// </summary>
        /// <remarks>
        /// Can add any kind of component.
        ///
        /// Adding a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// The added components have the default values for the type.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entityQuery">The EntityQuery defining the entities to modify.</param>
        /// <param name="componentType">The type of component to add.</param>
        [StructuralChangeMethod]
        public void AddComponent(EntityQuery entityQuery, ComponentType componentType)
        {
            var access = GetCheckedEntityDataAccess();
            access->AssertQueryIsValid(entityQuery);
            var queryImpl = entityQuery._GetImpl();

            if (queryImpl->IsEmptyIgnoreFilter)
                return;

            access->AssertMainThread();
            var changes = access->BeginStructuralChanges();
            access->AddComponentDuringStructuralChange(queryImpl->_QueryData->GetMatchingChunkCache(), queryImpl->_QueryData->MatchingArchetypes, queryImpl->_Filter, componentType);
            access->EndStructuralChanges(ref changes);
        }


        /// <summary>
        /// Adds components to a set of entities defined by a EntityQuery.
        /// </summary>
        /// <remarks>
        /// Can add any kinds of components.
        ///
        /// The added components have the default values for the type.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entityQuery">The EntityQuery defining the entities to modify.</param>
        /// <param name="componentTypes">The type of components to add.</param>
        [StructuralChangeMethod]
        public void AddComponent(EntityQuery entityQuery, ComponentTypes componentTypes)
        {
            var access = GetCheckedEntityDataAccess();
            access->AssertQueryIsValid(entityQuery);
            var queryImpl = entityQuery._GetImpl();

            if (queryImpl->IsEmptyIgnoreFilter)
                return;

            access->AssertMainThread();
            var changes = access->BeginStructuralChanges();
            access->AddComponentsDuringStructuralChange(queryImpl->_QueryData->GetMatchingChunkCache(),queryImpl->_QueryData->MatchingArchetypes, queryImpl->_Filter, componentTypes);
            access->EndStructuralChanges(ref changes);
        }

        /// <summary>
        /// Adds a component to a set of entities defined by the EntityQuery and
        /// sets the component of each entity in the query to the value in the component array.
        /// </summary>
        /// <remarks>
        /// Can add any kind of component except chunk components, managed components, and shared components.
        ///
        /// componentArray.Length must match entityQuery.ToEntityArray().Length.
        /// </remarks>
        /// <param name="entityQuery">The EntityQuery defining the entities to add component to</param>
        /// <param name="componentArray"></param>
        [StructuralChangeMethod]
        [NotBurstCompatible]
        public void AddComponentData<T>(EntityQuery entityQuery, NativeArray<T> componentArray) where T : struct, IComponentData
        {
            var access = GetCheckedEntityDataAccess();
            access->AssertQueryIsValid(entityQuery);
            if (entityQuery.IsEmptyIgnoreFilter)
                return;

            var entities = entityQuery.ToEntityArray(Allocator.TempJob);
            {
                if (entities.Length != componentArray.Length)
                    throw new ArgumentException($"AddComponentData number of entities in query '{entities.Length}' must match componentArray.Length '{componentArray.Length}'.");

                AddComponent(entityQuery, ComponentType.ReadWrite<T>());

                var componentData = GetComponentDataFromEntity<T>();
                for (int i = 0; i != componentArray.Length; i++)
                    componentData[entities[i]] = componentArray[i];
            }
            entities.Dispose();
        }

        /// <summary>
        /// Adds a component to a set of entities defined by a EntityQuery.
        /// </summary>
        /// <remarks>
        /// Can add any kind of component except chunk components.
        ///
        /// Adding a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// The added components have the default values for the type.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entityQuery">The EntityQuery defining the entities to modify.</param>
        /// <typeparam name="T">The type of component to add.</typeparam>
        [StructuralChangeMethod]
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public void AddComponent<T>(EntityQuery entityQuery)
        {
            AddComponent(entityQuery, ComponentType.ReadWrite<T>());
        }

        /// <summary>
        /// Adds a component to a set of entities.
        /// </summary>
        /// <remarks>
        /// Can add any kind of component except chunk components.
        ///
        /// Adding a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// The added components have the default values for the type.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before creating these chunks and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The `entities` array refers to an entity that does not exist.</exception>
        /// <param name="entities">An array of Entity objects.</param>
        /// <param name="componentType">The type of component to add.</param>
        [StructuralChangeMethod]
        public void AddComponent(NativeArray<Entity> entities, ComponentType componentType)
        {
            if (entities.Length == 0)
                return;

            if (componentType.IsChunkComponent)
                throw new ArgumentException($"Cannot add ChunkComponent {componentType} on NativeArray of entities.");

            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            ecs->AssertEntitiesExist((Entity*)entities.GetUnsafeReadOnlyPtr(), entities.Length);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordAddComponent(in access->m_WorldUnmanaged, default, (Entity*)entities.GetUnsafeReadOnlyPtr(), entities.Length, &componentType.TypeIndex, 1);
#endif

            // This method does not use EntityDataAccess.AddComponentDuringStructuralChange because
            // we must get the entity batch list before the call to EntityDataAccess.BeforeStructuralChange
#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginAddComponent(access->m_WorldUnmanaged))
#endif
            {
                // must get entity batch list before BeforeStructuralChange()
                var entityBatchList = default(NativeList<EntityBatchInChunk>);
                var useBatches = entities.Length > EntityDataAccess.FASTER_TO_BATCH_THRESHOLD &&
                    ecs->CreateEntityBatchList(entities, componentType.IsSharedComponent ? 1 : 0, out entityBatchList);

                var changes = access->BeginStructuralChanges();
                if (useBatches)
                {
                    ecs->AssertCanAddComponent(entityBatchList, componentType);
                    StructuralChange.AddComponentEntitiesBatch(ecs, (UnsafeList<EntityBatchInChunk>*)NativeListUnsafeUtility.GetInternalListDataPtrUnchecked(ref entityBatchList), componentType.TypeIndex);
                    entityBatchList.Dispose();
                }
                else
                {
                    ecs->AssertCanAddComponent(entities, componentType);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        var entity = entities[i];
                        StructuralChange.AddComponentEntity(ecs, &entity, componentType.TypeIndex);
                    }
                }
                access->EndStructuralChanges(ref changes);
            }
        }

        /// <summary>
        /// Remove a component from a set of entities.
        /// </summary>
        /// <remarks>
        /// Can remove any kind of component.
        ///
        /// Removing a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before creating these chunks and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The `entities` array refers to an entity that does not exist.</exception>
        /// <param name="entities">An array of Entity objects.</param>
        /// <param name="componentType">The type of component to remove.</param>
        [StructuralChangeMethod]
        public void RemoveComponent(NativeArray<Entity> entities, ComponentType componentType)
        {
            if (entities.Length == 0)
                return;

            var access = GetCheckedEntityDataAccess();
            access->AssertMainThread();

            var changes = access->BeginStructuralChanges();
            access->RemoveComponentDuringStructuralChange(entities, componentType);
            access->EndStructuralChanges(ref changes);
        }

        /// <summary>
        /// Adds a component to a set of entities.
        /// </summary>
        /// <remarks>
        /// Can add any kind of component.
        ///
        /// Adding a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// The added components have the default values for the type.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before creating these chunks and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The `entities` array refers to an entity that does not exist.</exception>
        /// <param name="entities">An array of Entity objects.</param>
        /// <typeparam name="T">The type of component to add.</typeparam>
        [StructuralChangeMethod]
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public void AddComponent<T>(NativeArray<Entity> entities)
        {
            AddComponent(entities, ComponentType.ReadWrite<T>());
        }

        /// <summary>
        /// Adds a set of component to an entity.
        /// </summary>
        /// <remarks>
        /// Can add any kinds of components.
        ///
        /// Adding components changes the entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// The added components have the default values for the type.
        ///
        /// If the <see cref="Entity"/> object refers to an entity that has been destroyed, this function throws an ArgumentError
        /// exception.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding these components and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The entity to modify.</param>
        /// <param name="types">The types of components to add.</param>
        [StructuralChangeMethod]
        public void AddComponents(Entity entity, ComponentTypes types)
        {
            var access = GetCheckedEntityDataAccess();

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordAddComponent(in access->m_WorldUnmanaged, default, &entity, 1, types.Types, types.Length);
#endif

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginAddComponent(access->m_WorldUnmanaged))
#endif
            {
                var ecs = access->EntityComponentStore;

                ecs->AssertCanAddComponents(entity, types);
                access->AssertMainThread();

                var changes = access->BeginStructuralChanges();

                // TODO(DOTS-4174): Go through EntityDataAccess
                ecs->AddComponents(entity, types);

                access->EndStructuralChanges(ref changes);
            }
        }

        /// <summary>
        /// Removes a component from an entity.
        /// </summary>
        /// <remarks>
        /// Can remove any kind of component.
        ///
        /// Returns false if the entity already does not have the specified component.
        ///
        /// Removing a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before removing the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The entity to modify.</param>
        /// <param name="componentType">The type of component to remove.</param>
        [StructuralChangeMethod]
        public bool RemoveComponent(Entity entity, ComponentType componentType)
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            var result = access->RemoveComponentDuringStructuralChange(entity, componentType);
            access->EndStructuralChanges(ref changes);
            return result;
        }

        /// <summary>
        /// Removes multiple components from an entity.
        /// </summary>
        /// <remarks>
        /// Can remove any kinds of components.
        ///
        /// It's OK if some or all of the components to remove are already missing from the entity.
        ///
        /// Removing components changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before removing the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The entity to modify.</param>
        /// <param name="componentTypes">The types of components to remove.</param>
        [StructuralChangeMethod]
        public void RemoveComponent(Entity entity, ComponentTypes componentTypes)
        {
            var access = GetCheckedEntityDataAccess();

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordRemoveComponent(in access->m_WorldUnmanaged, default, &entity, 1, componentTypes.Types, componentTypes.Length);
#endif

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginRemoveComponent(access->m_WorldUnmanaged))
#endif
            {
                var ecs = access->EntityComponentStore;

                ecs->AssertCanRemoveComponents(componentTypes);
                access->AssertMainThread();

                var changes = access->BeginStructuralChanges();

                // TODO(DOTS-4174): Go through EntityDataAccess
                ecs->RemoveComponents(entity, componentTypes);

                access->EndStructuralChanges(ref changes);
            }
        }

        /// <summary>
        /// Removes a component from a set of entities defined by an EntityQuery.
        /// </summary>
        /// <remarks>
        /// Can remove any kind of component.
        ///
        /// It's OK if some or all of the components to remove are already missing from some or all of the entities.
        ///
        /// Removing a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before removing the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entityQuery">The EntityQuery defining the entities to modify.</param>
        /// <param name="componentType">The type of component to remove.</param>
        [StructuralChangeMethod]
        public void RemoveComponent(EntityQuery entityQuery, ComponentType componentType)
        {
            var access = GetCheckedEntityDataAccess();
            access->AssertQueryIsValid(entityQuery);
            var queryImpl = entityQuery._GetImpl();
            if (queryImpl->IsEmptyIgnoreFilter)
                return;

            access->AssertMainThread();
            var changes = access->BeginStructuralChanges();
            access->RemoveComponentDuringStructuralChange(queryImpl->_QueryData->GetMatchingChunkCache(),queryImpl->_QueryData->MatchingArchetypes, queryImpl->_Filter, componentType);
            access->EndStructuralChanges(ref changes);
        }

        /// <summary>
        /// Removes a set of components from a set of entities defined by an EntityQuery.
        /// </summary>
        /// <remarks>
        /// Can remove any kinds of components.
        ///
        /// It's OK if some or all of the components to remove are already missing from some or all of the entities.
        ///
        /// Removing a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before removing the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entityQuery">The EntityQuery defining the entities to modify.</param>
        /// <param name="types">The types of components to add.</param>
        [StructuralChangeMethod]
        public void RemoveComponent(EntityQuery entityQuery, ComponentTypes types)
        {
            var access = GetCheckedEntityDataAccess();
            access->AssertQueryIsValid(entityQuery);
            var queryImpl = entityQuery._GetImpl();
            if (queryImpl->IsEmptyIgnoreFilter)
                return;

            access->AssertMainThread();
            var changes = access->BeginStructuralChanges();

            access->RemoveMultipleComponentsDuringStructuralChange(queryImpl->_QueryData->GetMatchingChunkCache(),queryImpl->_QueryData->MatchingArchetypes, queryImpl->_Filter, types);

            access->EndStructuralChanges(ref changes);
        }

        /// <summary>
        /// Removes a component from an entity.
        /// </summary>
        /// <remarks>
        /// Can remove any kind of component except chunk components.
        ///
        /// Returns false if the entity was already missing the component.
        ///
        /// Removing a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before removing the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The entity.</param>
        /// <typeparam name="T">The type of component to remove.</typeparam>
        [StructuralChangeMethod]
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public bool RemoveComponent<T>(Entity entity)
        {
            return RemoveComponent(entity, ComponentType.ReadWrite<T>());
        }

        /// <summary>
        /// Removes a component from a set of entities defined by a EntityQuery.
        /// </summary>
        /// <remarks>
        /// Can remove any kind of component except chunk components.
        ///
        /// It's OK if the component to remove is already missing from some or all of the entities.
        ///
        /// Removing a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before removing the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entityQuery">The EntityQuery defining the entities to modify.</param>
        /// <typeparam name="T">The type of component to remove.</typeparam>
        [StructuralChangeMethod]
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public void RemoveComponent<T>(EntityQuery entityQuery)
        {
            RemoveComponent(entityQuery, ComponentType.ReadWrite<T>());
        }

        /// <summary>
        /// Removes a component from a set of entities.
        /// </summary>
        /// <remarks>
        /// Can remove any kind of component except chunk components.
        ///
        /// It's OK if the component to remove is already missing from some or all of the entities.
        ///
        /// Removing a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before removing the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entities">An array identifying the entities to modify.</param>
        /// <typeparam name="T">The type of component to remove.</typeparam>
        [StructuralChangeMethod]
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public void RemoveComponent<T>(NativeArray<Entity> entities)
        {
            RemoveComponent(entities, ComponentType.ReadWrite<T>());
        }

        /// <summary>
        /// Adds a component to an entity and set the value of that component.
        /// </summary>
        /// <remarks>
        /// Can add any kind of component except chunk components, managed components, or shared components.
        ///
        /// Returns false if the entity already had the component. The component's data is set regardless.
        ///
        /// Adding a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The entity.</param>
        /// <param name="componentData">The data to set.</param>
        /// <typeparam name="T">The type of component.</typeparam>
        /// <returns></returns>
        [StructuralChangeMethod]
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public bool AddComponentData<T>(Entity entity, T componentData) where T : struct, IComponentData
        {
            var type = ComponentType.ReadWrite<T>();
            var added = AddComponent(entity, type);
            if (!type.IsZeroSized)
                SetComponentData(entity, componentData);

            return added;
        }

        /// <summary>
        /// Removes a chunk component from the specified entity.
        /// </summary>
        /// <remarks>
        /// A chunk component is common to all entities in a chunk. Removing the chunk component from an entity changes
        /// that entity's archetype and results in the entity being moved to a different chunk (that does not have the
        /// removed component).
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before removing the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <returns>False if the entity did not have the component.</returns>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The entity.</param>
        /// <typeparam name="T">The type of component to remove.</typeparam>
        [StructuralChangeMethod]
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public bool RemoveChunkComponent<T>(Entity entity)
        {
            return RemoveComponent(entity, ComponentType.ChunkComponent<T>());
        }

        /// <summary>
        /// Adds a chunk component to the specified entity.
        /// </summary>
        /// <remarks>
        /// Adding a chunk component to an entity changes that entity's archetype and results in the entity being moved
        /// to a different chunk, either one that already has an archetype containing the chunk component or a new
        /// chunk.
        ///
        /// A chunk component is common to all entities in a chunk. You can access a chunk <see cref="IComponentData"/>
        /// instance through either the chunk itself or through an entity stored in that chunk. In either case, getting
        /// or setting the component reads or writes the same data.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <returns>False if the entity already had the chunk component. The chunk component's value is set regardless.</returns>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The entity.</param>
        /// <typeparam name="T">The type of component, which must implement IComponentData.</typeparam>
        [StructuralChangeMethod]
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public bool AddChunkComponentData<T>(Entity entity) where T : struct, IComponentData
        {
            return AddComponent(entity, ComponentType.ChunkComponent<T>());
        }

        /// <summary>
        /// Adds a chunk component to each of the chunks identified by an EntityQuery and sets the component values.
        /// </summary>
        /// <remarks>
        /// This function finds all chunks whose archetype satisfies the EntityQuery and adds the specified
        /// component to them.
        ///
        /// A chunk component is common to all entities in a chunk. You can access a chunk <see cref="IComponentData"/>
        /// instance through either the chunk itself or through an entity stored in that chunk.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entityQuery">The EntityQuery identifying the chunks to modify.</param>
        /// <param name="componentData">The data to set.</param>
        /// <typeparam name="T">The type of component, which must implement IComponentData.</typeparam>
        [StructuralChangeMethod]
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public void AddChunkComponentData<T>(EntityQuery entityQuery, T componentData) where T : unmanaged, IComponentData
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            access->AssertQueryIsValid(entityQuery);
            if (entityQuery.IsEmptyIgnoreFilter)
                return;

            bool validAdd = true;
            var chunks = entityQuery.CreateArchetypeChunkArray(Allocator.TempJob);

            if (chunks.Length > 0)
            {
                ecs->CheckCanAddChunkComponent(chunks, ComponentType.ChunkComponent<T>(), ref validAdd);

                if (validAdd)
                {
                    var changes = access->BeginStructuralChanges();

                    var componentType = ComponentType.ReadWrite<T>();
                    var componentTypeIndex = componentType.TypeIndex;
                    var componentTypeIndexForAdd = TypeManager.MakeChunkComponentTypeIndex(componentTypeIndex);
                    ArchetypeChunk* chunkPtr = (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
                    // Have to record here because method is not using EntityDataAccess
                    EntitiesJournaling.RecordAddComponent(in access->m_WorldUnmanaged, default, chunkPtr, chunks.Length, &componentTypeIndexForAdd, 1);
                    EntitiesJournaling.RecordSetComponentData(in access->m_WorldUnmanaged, default, chunkPtr, chunks.Length, &componentTypeIndexForAdd, 1, UnsafeUtility.AddressOf(ref componentData), UnsafeUtility.SizeOf<T>());
#endif

                    StructuralChange.AddComponentChunks(ecs, chunkPtr, chunks.Length, componentTypeIndexForAdd);
                    StructuralChange.SetChunkComponent(ecs, chunkPtr, chunks.Length, &componentData, componentTypeIndex);

                    access->EndStructuralChanges(ref changes);
                }
            }

            chunks.Dispose();

            if (!validAdd)
            {
                ecs->ThrowDuplicateChunkComponentError(ComponentType.ChunkComponent<T>());
            }
        }

        /// <summary>
        /// Removes a chunk component from the chunks identified by an EntityQuery.
        /// </summary>
        /// <remarks>
        /// A chunk component is common to all entities in a chunk. You can access a chunk <see cref="IComponentData"/>
        /// instance through either the chunk itself or through an entity stored in that chunk.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before removing the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entityQuery">The EntityQuery identifying the chunks to modify.</param>
        /// <typeparam name="T">The type of component to remove.</typeparam>
        [StructuralChangeMethod]
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public void RemoveChunkComponentData<T>(EntityQuery entityQuery)
        {
            RemoveComponent(entityQuery, ComponentType.ChunkComponent<T>());
        }

        /// <summary>
        /// Adds a dynamic buffer component to an entity.
        /// </summary>
        /// <remarks>
        /// A buffer component stores the number of elements inside the chunk defined by the [InternalBufferCapacity]
        /// attribute applied to the buffer element type declaration. Any additional elements are stored in a separate memory
        /// block that is managed by the EntityManager.
        ///
        /// Adding a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// (You can add a buffer component with the regular AddComponent methods, but unlike those methods, this
        /// method conveniently also returns the new buffer.)
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the buffer and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The entity.</param>
        /// <typeparam name="T">The type of buffer element. Must implement IBufferElementData.</typeparam>
        /// <returns>The buffer.</returns>
        /// <seealso cref="InternalBufferCapacityAttribute"/>
        [StructuralChangeMethod]
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleBufferElement) })]
        public DynamicBuffer<T> AddBuffer<T>(Entity entity) where T : struct, IBufferElementData
        {
            AddComponent<T>(entity);
            return GetBuffer<T>(entity);
        }

        /// <summary>
        /// Adds a managed [UnityEngine.Component](https://docs.unity3d.com/ScriptReference/Component.html)
        /// object to an entity.
        /// </summary>
        /// <remarks>
        /// Accessing data in a managed object forfeits many opportunities for increased performance. Adding
        /// managed objects to an entity should be avoided or used sparingly.
        ///
        /// Adding a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// The method also works for adding managed objects implementing `IComponentData`, but `AddComponentData` is the preferred method for those objects.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the object and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entity">The entity to modify.</param>
        /// <param name="componentData">An object inheriting UnityEngine.Component.</param>
        /// <exception cref="ArgumentNullException">If the componentData object is not an instance of
        /// UnityEngine.Component.</exception>
        [StructuralChangeMethod]
        [NotBurstCompatible]
        public void AddComponentObject(Entity entity, object componentData)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (componentData == null)
                throw new ArgumentNullException(nameof(componentData));
#endif

            ComponentType type = componentData.GetType();

            AddComponent(entity, type);
            SetComponentObject(entity, type, componentData);
        }

        /// <summary>
        /// Adds a shared component to an entity.
        /// </summary>
        /// <remarks>
        /// The fields of the `componentData` parameter are assigned to the added shared component.
        ///
        /// Adding a component to an entity changes its archetype and results in the entity being moved to a
        /// different chunk. The entity moves to a chunk with other entities that have the same shared component values.
        /// A new chunk is created if no chunk with the same archetype and shared component values currently exists.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <returns>Returns false if the entity already has the shared component. The shared component value is set either way.</returns>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The entity.</param>
        /// <param name="componentData">The shared component value to set.</param>
        /// <typeparam name="T">The shared component type.</typeparam>
        [StructuralChangeMethod]
        [NotBurstCompatible]
        public bool AddSharedComponentData<T>(Entity entity, T componentData) where T : struct, ISharedComponentData
        {
            var access = GetCheckedEntityDataAccess();
            access->AssertMainThread();
            var changes = access->BeginStructuralChanges();
            var result = access->AddSharedComponentDataDuringStructuralChange(entity, componentData);
            access->EndStructuralChanges(ref changes);
            return result;
        }

        /// <summary>
        /// Adds a shared component to a set of entities defined by a EntityQuery.
        /// </summary>
        /// <remarks>
        /// The fields of the `componentData` parameter are assigned to all of the added shared components.
        ///
        /// Adding a component to an entity changes its archetype and results in the entity being moved to a
        /// different chunk. The entity moves to a chunk with other entities that have the same shared component values.
        /// A new chunk is created if no chunk with the same archetype and shared component values currently exists.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entityQuery">The EntityQuery defining a set of entities to modify.</param>
        /// <param name="componentData">The data to set.</param>
        /// <typeparam name="T">The data type of the shared component.</typeparam>
        [StructuralChangeMethod]
        [NotBurstCompatible]
        public void AddSharedComponentData<T>(EntityQuery entityQuery, T componentData)
            where T : struct, ISharedComponentData
        {
            var access = GetCheckedEntityDataAccess();
            access->AssertQueryIsValid(entityQuery);
            if (entityQuery.IsEmptyIgnoreFilter)
                return;

            access->AssertMainThread();
            var changes = access->BeginStructuralChanges();

            var componentType = ComponentType.ReadWrite<T>();
            using (var chunks = entityQuery.CreateArchetypeChunkArray(Allocator.TempJob))
            {
                if (chunks.Length == 0)
                    return;
                var newSharedComponentDataIndex = access->InsertSharedComponent(componentData);
                access->AddSharedComponentDataDuringStructuralChange(chunks, newSharedComponentDataIndex, componentType);
            }

            access->EndStructuralChanges(ref changes);
        }

        /// <summary>
        /// Adds and removes components of an entity to match the specified EntityArchetype.
        /// </summary>
        /// <remarks>
        /// Components of the archetype which the entity already has will preserve their values.
        ///
        /// Components of the archetype which the entity does *not* have will get the default value for their types.
        ///
        /// Adding a component to an entity changes its archetype and results in the entity being moved to a
        /// different chunk. The entity moves to a chunk with other entities that have the same shared component values.
        /// A new chunk is created if no chunk with the same archetype and shared component values currently exists.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The entity whose archetype to change.</param>
        /// <param name="archetype">The new archetype for the entity.</param>
        [StructuralChangeMethod]
        public void SetArchetype(Entity entity, EntityArchetype archetype)
        {
            archetype.CheckValidEntityArchetype();

            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;

            EntityComponentStore.AssertValidArchetype(ecs, archetype);
            ecs->AssertEntitiesExist(&entity, 1);

            var oldArchetype = ecs->GetArchetype(entity);
            var newArchetype = archetype.Archetype;

            EntityComponentStore.AssertArchetypeDoesNotRemoveSystemStateComponents(oldArchetype, newArchetype);

            var changes = access->BeginStructuralChanges();

            StructuralChange.MoveEntityArchetype(ecs, &entity, archetype.Archetype);

            access->EndStructuralChanges(ref changes);
        }

        /// <summary>
        /// Enabled entities are processed by systems, disabled entities are not.
        /// Adds or removes the <see cref="Disabled"/> component. By default EntityQuery does not include entities containing the Disabled component.
        ///
        /// If the entity was converted from a prefab and thus has a <see cref="LinkedEntityGroup"/> component, the entire group will be enabled or disabled.
        /// </summary>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The entity to enable or disable</param>
        /// <param name="enabled">True if the entity should be enabled</param>
        [StructuralChangeMethod]
        public void SetEnabled(Entity entity, bool enabled)
        {
            if (GetEnabled(entity) == enabled)
                return;

            var disabledType = ComponentType.ReadWrite<Disabled>();
            if (HasComponent<LinkedEntityGroup>(entity))
            {
                //@TODO: AddComponent / Remove component should support Allocator.Temp
                var linkedEntities = GetBuffer<LinkedEntityGroup>(entity).Reinterpret<Entity>()
                    .ToNativeArray(Allocator.TempJob);
                {
                    if (enabled)
                        RemoveComponent(linkedEntities, disabledType);
                    else
                        AddComponent(linkedEntities, disabledType);
                }
                linkedEntities.Dispose();
            }
            else
            {
                if (!enabled)
                    AddComponent(entity, disabledType);
                else
                    RemoveComponent(entity, disabledType);
            }
        }

        public bool GetEnabled(Entity entity)
        {
            return !HasComponent<Disabled>(entity);
        }

        // TODO(https://unity3d.atlassian.net/browse/DOTS-4242): make these methods public once enabled bits support is complete
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleEnableableComponent) })]
        internal bool IsComponentEnabled<T>(Entity entity) where T: struct, IEnableableComponent
        {
            int typeIndex = TypeManager.GetTypeIndex<T>();
            var access = GetCheckedEntityDataAccess();
            if (!access->IsInExclusiveTransaction)
                access->DependencyManager->CompleteWriteDependency(typeIndex);
            return access->IsComponentEnabled(entity, typeIndex);
        }
        internal bool IsComponentEnabled(Entity entity, ComponentType componentType)
        {
            var access = GetCheckedEntityDataAccess();
            if (!access->IsInExclusiveTransaction)
                access->DependencyManager->CompleteWriteDependency(componentType.TypeIndex);
            return access->IsComponentEnabled(entity, componentType.TypeIndex);
        }
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleEnableableComponent) })]
        internal void SetComponentEnabled<T>(Entity entity, bool value) where T: struct, IEnableableComponent
        {
            int typeIndex = TypeManager.GetTypeIndex<T>();
            var access = GetCheckedEntityDataAccess();
            if (!access->IsInExclusiveTransaction)
                access->DependencyManager->CompleteReadAndWriteDependency(typeIndex);
            access->SetComponentEnabled(entity, typeIndex, value);
        }
        internal void SetComponentEnabled(Entity entity, ComponentType componentType, bool value)
        {
            var access = GetCheckedEntityDataAccess();
            if (!access->IsInExclusiveTransaction)
                access->DependencyManager->CompleteReadAndWriteDependency(componentType.TypeIndex);
            access->SetComponentEnabled(entity, componentType.TypeIndex, value);
        }

        /// <summary>
        /// Creates an entity having the specified archetype.
        /// </summary>
        /// <remarks>
        /// The EntityManager creates the entity in the first available chunk with the matching archetype that has
        /// enough space.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before creating the entity and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="archetype">The archetype for the new entity.</param>
        /// <returns>The Entity object that you can use to access the entity.</returns>
        [StructuralChangeMethod]
        public Entity CreateEntity(EntityArchetype archetype)
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            var result = access->CreateEntityDuringStructuralChange(archetype);
            access->EndStructuralChanges(ref changes);
            return result;
        }

        /// <summary>
        /// Creates an entity having components of the specified types.
        /// </summary>
        /// <remarks>
        /// The EntityManager creates the entity in the first available chunk with the matching archetype that has
        /// enough space.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before creating the entity and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="types">The types of components to add to the new entity.</param>
        /// <returns>The Entity object that you can use to access the entity.</returns>
        [StructuralChangeMethod]
        [NotBurstCompatible]
        public Entity CreateEntity(params ComponentType[] types)
        {
            return CreateEntity(CreateArchetype(types));
        }

        /// <summary>
        /// Creates an entity with no components.
        /// </summary>
        /// <remarks>
        /// The EntityManager creates the entity in the first available chunk with the archetype having no components.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before creating the entity and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <returns>The Entity object that you can use to access the entity.</returns>
        [StructuralChangeMethod]
        public Entity CreateEntity()
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            var result = access->CreateEntity();
            access->EndStructuralChanges(ref changes);
            return result;
        }

        /// <summary>
        /// Creates a set of entities of the specified archetype.
        /// </summary>
        /// <remarks>Fills the [NativeArray](https://docs.unity3d.com/ScriptReference/Unity.Collections.NativeArray_1.html)
        /// object assigned to the `entities` parameter with the Entity objects of the created entities. Each entity
        /// has the components specified by the <see cref="EntityArchetype"/> object assigned
        /// to the `archetype` parameter. The EntityManager adds these entities to the <see cref="World"/> entity list. Use the
        /// Entity objects in the array for further processing, such as setting the component values.</remarks>
        /// <param name="archetype">The archetype defining the structure for the new entities.</param>
        /// <param name="entities">An array to hold the Entity objects needed to access the new entities.
        /// The length of the array determines how many entities are created.</param>
        [StructuralChangeMethod]
        public void CreateEntity(EntityArchetype archetype, NativeArray<Entity> entities)
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            access->CreateEntityDuringStructuralChange(archetype, entities);
            access->EndStructuralChanges(ref changes);
        }

        /// <summary>
        /// Creates a set of entities of the specified archetype.
        /// </summary>
        /// <remarks>Creates a [NativeArray](https://docs.unity3d.com/ScriptReference/Unity.Collections.NativeArray_1.html) of entities,
        /// each of which has the components specified by the <see cref="EntityArchetype"/> object assigned
        /// to the `archetype` parameter. The EntityManager adds these entities to the <see cref="World"/> entity list.</remarks>
        /// <param name="archetype">The archetype defining the structure for the new entities.</param>
        /// <param name="entityCount">The number of entities to create with the specified archetype.</param>
        /// <param name="allocator">How the created native array should be allocated.</param>
        /// <returns>
        /// A [NativeArray](https://docs.unity3d.com/ScriptReference/Unity.Collections.NativeArray_1.html) of entities
        /// with the given archetype.
        /// </returns>
        [StructuralChangeMethod]
        public NativeArray<Entity> CreateEntity(EntityArchetype archetype, int entityCount, Allocator allocator)
        {
            NativeArray<Entity> entities;
            entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, allocator);
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            access->CreateEntityDuringStructuralChange(archetype, entities);
            access->EndStructuralChanges(ref changes);

            return entities;
        }

        /// <summary>
        /// Creates a set of entities of the specified archetype.
        /// </summary>
        /// <remarks>Unlike the other overloads, this does not create an array of Entity values. You don't always need the Entity value of a newly created entity because maybe you only need to access the entity through queries.</remarks>
        /// <param name="archetype">The archetype defining the structure for the new entities.</param>
        /// <param name="entityCount">The number of entities to create with the specified archetype.</param>
        [StructuralChangeMethod]
        public void CreateEntity(EntityArchetype archetype, int entityCount)
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            access->CreateEntityDuringStructuralChange(archetype, null, entityCount);
            access->EndStructuralChanges(ref changes);
        }

        /// <summary>
        /// Destroy all entities having a common set of component types.
        /// </summary>
        /// <remarks>Since entities in the same chunk share the same component structure, this function effectively destroys
        /// the chunks holding any entities identified by the `entityQueryFilter` parameter.</remarks>
        /// <param name="entityQueryFilter">Defines the components an entity must have to qualify for destruction.</param>
        [StructuralChangeMethod]
        public void DestroyEntity(EntityQuery entityQuery)
        {
            var access = GetCheckedEntityDataAccess();
            access->AssertQueryIsValid(entityQuery);
            var queryImpl = entityQuery._GetImpl();
            DestroyEntity(queryImpl->_QueryData->GetMatchingChunkCache(),queryImpl->_QueryData->MatchingArchetypes, queryImpl->_Filter);
        }

        /// <summary>
        /// Destroys all entities in the EntityManager and resets the internal entity ID version table.
        /// </summary>
        /// <remarks>
        /// This method can be used to reset an EntityManager for the purpose of creating data that can be written to disk with a deterministic, exact matching file on disk.
        /// It resets all chunk and entity version state so that it can be serialized to disk back to a state that is the same as a clean world.
        /// Archetypes and EntityQuery are not reset since they are often cached / owned by systems, but these are also not stored on disk.
        /// </remarks>
        [NotBurstCompatible]
        public void DestroyAndResetAllEntities()
        {
            DestroyEntity(UniversalQuery);
            if (Debug.EntityCount != 0)
                throw new System.ArgumentException("Destroying all entities failed. Some entities couldn't be deleted.");

            // FreeAllEntities also resets entity index
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            ecs->FreeAllEntities(true);

            access->ManagedComponentStore.ResetManagedComponentStoreForDeserialization(0, ref *ecs);
            access->ManagedComponentStore.PrepareForDeserialize();
        }


        /// <summary>
        /// Destroys all entities in an array.
        /// </summary>
        /// <remarks>
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before destroying the entity and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entities">An array containing the Entity objects of the entities to destroy.</param>
        [StructuralChangeMethod]
        public void DestroyEntity(NativeArray<Entity> entities)
        {
            DestroyEntityInternal((Entity*)entities.GetUnsafeReadOnlyPtr(), entities.Length);
        }

        /// <summary>
        /// Destroys all entities in a slice of an array.
        /// </summary>
        /// <remarks>
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before destroying the entity and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entities">The slice of an array containing the Entity objects of the entities to destroy.</param>
        [StructuralChangeMethod]
        public void DestroyEntity(NativeSlice<Entity> entities)
        {
            DestroyEntityInternal((Entity*)entities.GetUnsafeReadOnlyPtr(), entities.Length);
        }

        /// <summary>
        /// Destroys an entity.
        /// </summary>
        /// <remarks>
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before destroying the entity and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entity">The Entity object of the entity to destroy.</param>
        [StructuralChangeMethod]
        public void DestroyEntity(Entity entity)
        {
            DestroyEntityInternal(&entity, 1);
        }

        /// <summary>
        /// Clones an entity.
        /// </summary>
        /// <remarks>
        /// The new entity has the same archetype and component values as the original; however, <see cref="ISystemStateComponentData"/>
        /// and <see cref="Prefab"/> components are removed from the clone.
        ///
        /// If the source entity was converted from a prefab and thus has a <see cref="LinkedEntityGroup"/> component,
        /// the entire group is cloned as a new set of entities. Entity references on components that are being cloned to entities inside
        /// the set are remapped to the instantiated entities.
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before creating the entity and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="srcEntity">The entity to clone.</param>
        /// <returns>The Entity object for the new entity.</returns>
        [StructuralChangeMethod]
        public Entity Instantiate(Entity srcEntity)
        {
            var access = GetCheckedEntityDataAccess();
            Entity entity;
            var changes = access->BeginStructuralChanges();
            access->InstantiateInternalDuringStructuralChange(srcEntity, &entity, 1);
            access->EndStructuralChanges(ref changes);
            return entity;
        }

        /// <summary>
        /// Makes multiple clones of an entity.
        /// </summary>
        /// <remarks>
        /// The new entity has the same archetype and component values as the original, however system state and prefab tag components are removed from the clone.
        ///
        /// If the source entity has a <see cref="LinkedEntityGroup"/> component, the entire group is cloned as a new
        /// set of entities. Entity references on components that are being cloned to entities inside the set are remapped to the instantiated entities.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before creating these entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="srcEntity">The entity to clone.</param>
        /// <param name="outputEntities">An array to receive the Entity objects of the root entity in each clone.
        /// The length of this array determines the number of clones.</param>
        [StructuralChangeMethod]
        public void Instantiate(Entity srcEntity, NativeArray<Entity> outputEntities)
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            access->InstantiateInternalDuringStructuralChange(srcEntity, (Entity*)outputEntities.GetUnsafePtr(), outputEntities.Length);
            access->EndStructuralChanges(ref changes);
        }

        /// <summary>
        /// Makes multiple clones of an entity.
        /// </summary>
        /// <remarks>
        /// The new entity has the same archetype and component values as the original, however system state and prefab tag components are removed from the clone.
        ///
        /// If the source entity has a <see cref="LinkedEntityGroup"/> component, the entire group is cloned as a new
        /// set of entities. Entity references on components that are being cloned to entities inside the set are remapped to the instantiated entities.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before creating these entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="srcEntity">The entity to clone.</param>
        /// <param name="instanceCount">The number of entities to instantiate with the same components as the source entity.</param>
        /// <param name="allocator">How the created native array should be allocated.</param>
        /// <returns>A [NativeArray](https://docs.unity3d.com/ScriptReference/Unity.Collections.NativeArray_1.html) of entities.</returns>
        [StructuralChangeMethod]
        public NativeArray<Entity> Instantiate(Entity srcEntity, int instanceCount, Allocator allocator)
        {
            var access = GetCheckedEntityDataAccess();

            NativeArray<Entity> entities;
            entities = CollectionHelper.CreateNativeArray<Entity>(instanceCount, allocator);
            var changes = access->BeginStructuralChanges();
            access->InstantiateInternalDuringStructuralChange(srcEntity, (Entity*)entities.GetUnsafePtr(), instanceCount);
            access->EndStructuralChanges(ref changes);
            return entities;
        }

        /// <summary>
        /// Clones a set of entities.
        /// </summary>
        /// <remarks>
        /// The new entity has the same archetype and component values as the original, however system state and prefab tag components are removed from the clone.
        ///
        /// Entity references on components that are being cloned to entities inside the set are remapped to the instantiated entities.
        /// This method overload ignores the <see cref="LinkedEntityGroup"/> component,
        /// since the group of entities that will be cloned is passed explicitly.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before creating the entity and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="srcEntities">The set of entities to clone</param>
        /// <param name="outputEntities">the set of entities that were cloned. outputEntities.Length must match srcEntities.Length</param>
        [StructuralChangeMethod]
        public void Instantiate(NativeArray<Entity> srcEntities, NativeArray<Entity> outputEntities)
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            access->InstantiateInternalDuringStructuralChange((Entity*)srcEntities.GetUnsafeReadOnlyPtr(), (Entity*)outputEntities.GetUnsafePtr(), srcEntities.Length, outputEntities.Length, true);
            access->EndStructuralChanges(ref changes);
        }

        /// <summary>
        /// Clones a set of entities, different from Instantiate because it does not remove the prefab tag component.
        /// </summary>
        /// <remarks>
        /// The new entity has the same archetype and component values as the original, however system state components are removed from the clone.
        ///
        /// Entity references on components that are being cloned to entities inside the set are remapped to the instantiated entities.
        /// This method overload ignores the <see cref="LinkedEntityGroup"/> component,
        /// since the group of entities that will be cloned is passed explicitly.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before creating the entity and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="srcEntities">The set of entities to clone</param>
        /// <param name="outputEntities">the set of entities that were cloned. outputEntities.Length must match srcEntities.Length</param>
        [StructuralChangeMethod]
        public void CopyEntities(NativeArray<Entity> srcEntities, NativeArray<Entity> outputEntities)
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            access->InstantiateInternalDuringStructuralChange((Entity*)srcEntities.GetUnsafeReadOnlyPtr(), (Entity*)outputEntities.GetUnsafePtr(), srcEntities.Length, outputEntities.Length, false);
            access->EndStructuralChanges(ref changes);
        }

        /// <summary>
        /// Detects the created and destroyed entities compared to last time the method was called with the given state.
        /// </summary>
        /// <remarks>
        /// Entities must be fully destroyed, if system state components keep it alive it still counts as not yet destroyed.
        /// <see cref="EntityCommandBuffer"/> instances that have not been played back will have no effect on this until they are played back.
        /// </remarks>
        /// <param name="state">The same state list must be passed when you call this method, it remembers the entities that were already notified created and destroyed.</param>
        /// <param name="createdEntities">The entities that were created.</param>
        /// <param name="destroyedEntities">The entities that were destroyed.</param>
        public JobHandle GetCreatedAndDestroyedEntitiesAsync(NativeList<int> state, NativeList<Entity> createdEntities, NativeList<Entity> destroyedEntities)
        {
            return GetCheckedEntityDataAccess()->GetCreatedAndDestroyedEntitiesAsync(state, createdEntities, destroyedEntities);
        }

        /// <summary>
        /// Detects the created and destroyed entities compared to last time the method was called with the given state.
        /// </summary>
        /// <remarks>
        /// Entities must be fully destroyed, if system state components keep it alive it still counts as not yet destroyed.
        /// <see cref="EntityCommandBuffer"/> instances that have not been played back will have no effect on this until they are played back.
        /// </remarks>
        /// <param name="state">The same state list must be passed when you call this method, it remembers the entities that were already notified created and destroyed.</param>
        /// <param name="createdEntities">The entities that were created.</param>
        /// <param name="destroyedEntities">The entities that were destroyed.</param>
        public void GetCreatedAndDestroyedEntities(NativeList<int> state, NativeList<Entity> createdEntities, NativeList<Entity> destroyedEntities)
        {
            GetCheckedEntityDataAccess()->GetCreatedAndDestroyedEntities(state,  createdEntities, destroyedEntities);
        }

        /// <summary>
        /// Creates an archetype from a set of component types.
        /// </summary>
        /// <remarks>
        /// Creates a new archetype in the ECS framework's internal type registry, unless the archetype already exists.
        /// </remarks>
        /// <param name="types">The component types to include as part of the archetype.</param>
        /// <returns>The EntityArchetype object for the archetype.</returns>
        [NotBurstCompatible]
        public EntityArchetype CreateArchetype(params ComponentType[] types)
        {
            if (types == null)
                throw new NullReferenceException(nameof(types));

            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            fixed(ComponentType* typesPtr = types)
            {
                var result = access->CreateArchetype(typesPtr, types.Length);
                access->EndStructuralChanges(ref changes);
                return result;
            }
        }

        /// <summary>
        /// Creates an archetype from a set of component types.
        /// </summary>
        /// <remarks>
        /// Creates a new archetype in the ECS framework's internal type registry, unless the archetype already exists.
        /// </remarks>
        /// <param name="types">The component types to include as part of the archetype.</param>
        /// <returns>The EntityArchetype object for the archetype.</returns>
        public EntityArchetype CreateArchetype(NativeArray<ComponentType> types)
        {
            return CreateArchetype((ComponentType*)types.GetUnsafeReadOnlyPtr(), types.Length);
        }

        struct IsolateCopiedEntities : IComponentData {}

        /// <summary>
        /// Instantiates / Copies all entities from srcEntityManager and copies them into this EntityManager.
        /// Entity references on components that are being cloned to entities inside the srcEntities set are remapped to the instantiated entities.
        /// </summary>
        [NotBurstCompatible]
        public void CopyEntitiesFrom(EntityManager srcEntityManager, NativeArray<Entity> srcEntities, NativeArray<Entity> outputEntities = default)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (outputEntities.IsCreated && outputEntities.Length != srcEntities.Length)
                throw  new ArgumentException("outputEntities.Length must match srcEntities.Length");
#endif

            using (var srcManagerInstances = new NativeArray<Entity>(srcEntities.Length, Allocator.Temp))
            {
                srcEntityManager.CopyEntities(srcEntities, srcManagerInstances);
                srcEntityManager.AddComponent(srcManagerInstances, ComponentType.ReadWrite<IsolateCopiedEntities>());

                var instantiated = srcEntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new ComponentType[] { typeof(IsolateCopiedEntities) },
                    Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab
                });

                using (var entityRemapping = srcEntityManager.CreateEntityRemapArray(Allocator.TempJob))
                {
                    MoveEntitiesFromInternalQuery(srcEntityManager, instantiated, entityRemapping);

                    EntityRemapUtility.GetTargets(out var output, entityRemapping);
                    RemoveComponent(output, ComponentType.ReadWrite<IsolateCopiedEntities>());
                    output.Dispose();

                    if (outputEntities.IsCreated)
                    {
                        for (int i = 0; i != outputEntities.Length; i++)
                            outputEntities[i] = entityRemapping[srcManagerInstances[i].Index].Target;
                    }
                }
            }
        }

        /// <summary>
        /// Copies all entities from srcEntityManager and replaces all entities in this EntityManager
        /// </summary>
        /// <remarks>
        /// Guarantees that the chunk layout and order of the entities will match exactly, thus this method can be used for deterministic rollback.
        /// This feature is not complete and only supports a subset of the EntityManager features at the moment:
        /// * Currently it copies all SystemStateComponents (They should not be copied)
        /// * Currently does not support class based components
        /// </remarks>
        [NotBurstCompatible]
        public void CopyAndReplaceEntitiesFrom(EntityManager srcEntityManager)
        {
            srcEntityManager.CompleteAllJobs();
            CompleteAllJobs();

            var srcAccess = srcEntityManager.GetCheckedEntityDataAccess();
            var selfAccess = GetCheckedEntityDataAccess();

            using (var srcChunks = srcAccess->m_UniversalQueryWithChunks.CreateArchetypeChunkArrayAsync(Allocator.TempJob, out var srcChunksJob))
            using (var dstChunks = selfAccess->m_UniversalQueryWithChunks.CreateArchetypeChunkArrayAsync(Allocator.TempJob, out var dstChunksJob))
            {
                using (var archetypeChunkChanges = EntityDiffer.GetArchetypeChunkChanges(
                    srcChunks,
                    dstChunks,
                    Allocator.TempJob,
                    jobHandle: out var archetypeChunkChangesJob,
                    dependsOn: JobHandle.CombineDependencies(srcChunksJob, dstChunksJob)))
                {
                    archetypeChunkChangesJob.Complete();

                    EntityDiffer.CopyAndReplaceChunks(srcEntityManager, this, selfAccess->m_UniversalQueryWithChunks, archetypeChunkChanges);
                    EntityComponentStore.AssertAllEntitiesCopied(srcAccess->EntityComponentStore, selfAccess->EntityComponentStore);
                }
            }
        }

        /// <summary>
        /// Moves all entities managed by the specified EntityManager to the world of this EntityManager.
        /// </summary>
        /// <remarks>
        /// The entities moved are owned by this EntityManager.
        ///
        /// Each <see cref="World"/> has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one World to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        [NotBurstCompatible]
        public void MoveEntitiesFrom(EntityManager srcEntities)
        {
            using (var entityRemapping = srcEntities.CreateEntityRemapArray(Allocator.TempJob))
                MoveEntitiesFromInternalAll(srcEntities, entityRemapping);
        }

        /// <summary>
        /// Moves all entities managed by the specified EntityManager to the <see cref="World"/> of this EntityManager and fills
        /// an array with their Entity objects.
        /// </summary>
        /// <remarks>
        /// After the move, the entities are managed by this EntityManager. Use the `output` array to make post-move
        /// changes to the transferred entities.
        ///
        /// Each world has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one World to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="output">An array to receive the Entity objects of the transferred entities.</param>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        [NotBurstCompatible]
        public void MoveEntitiesFrom(out NativeArray<Entity> output, EntityManager srcEntities)
        {
            using (var entityRemapping = srcEntities.CreateEntityRemapArray(Allocator.TempJob))
            {
                MoveEntitiesFromInternalAll(srcEntities, entityRemapping);
                EntityRemapUtility.GetTargets(out output, entityRemapping);
            }
        }

        /// <summary>
        /// Moves all entities managed by the specified EntityManager to the <see cref="World"/> of this EntityManager and fills
        /// an array with their <see cref="Entity"/> objects.
        /// </summary>
        /// <remarks>
        /// After the move, the entities are managed by this EntityManager. Use the `output` array to make post-move
        /// changes to the transferred entities.
        ///
        /// Each world has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one World to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="output">An array to receive the Entity objects of the transferred entities.</param>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        /// <param name="entityRemapping">A set of entity transformations to make during the transfer.</param>
        /// <exception cref="ArgumentException"></exception>
        [NotBurstCompatible]
        public void MoveEntitiesFrom(out NativeArray<Entity> output, EntityManager srcEntities, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
            MoveEntitiesFromInternalAll(srcEntities, entityRemapping);
            EntityRemapUtility.GetTargets(out output, entityRemapping);
        }

        /// <summary>
        /// Moves all entities managed by the specified EntityManager to the <see cref="World"/> of this EntityManager.
        /// </summary>
        /// <remarks>
        /// After the move, the entities are managed by this EntityManager.
        ///
        /// Each World has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one world to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        /// <param name="entityRemapping">A set of entity transformations to make during the transfer.</param>
        /// <exception cref="ArgumentException">Thrown if you attempt to transfer entities to the EntityManager
        /// that already owns them.</exception>
        [NotBurstCompatible]
        public void MoveEntitiesFrom(EntityManager srcEntities, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
            MoveEntitiesFromInternalAll(srcEntities, entityRemapping);
        }

        /// <summary>
        /// Moves a selection of the entities managed by the specified EntityManager to the <see cref="World"/> of this EntityManager
        /// and fills an array with their <see cref="Entity"/> objects.
        /// </summary>
        /// <remarks>
        /// After the move, the entities are managed by this EntityManager. Use the `output` array to make post-move
        /// changes to the transferred entities.
        ///
        /// Each world has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one World to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        /// <param name="filter">A EntityQuery that defines the entities to move. Must be part of the source
        /// World.</param>
        /// <exception cref="ArgumentException"></exception>
        [NotBurstCompatible]
        public void MoveEntitiesFrom(EntityManager srcEntities, EntityQuery filter)
        {
            using (var entityRemapping = srcEntities.CreateEntityRemapArray(Allocator.TempJob))
                MoveEntitiesFromInternalQuery(srcEntities, filter, entityRemapping);
        }

        /// <summary>
        /// Moves a selection of the entities managed by the specified EntityManager to the <see cref="World"/> of this EntityManager
        /// and fills an array with their <see cref="Entity"/> objects.
        /// </summary>
        /// <remarks>
        /// After the move, the entities are managed by this EntityManager. Use the `output` array to make post-move
        /// changes to the transferred entities.
        ///
        /// Each world has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one World to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="output">An array to receive the Entity objects of the transferred entities.</param>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        /// <param name="filter">A EntityQuery that defines the entities to move. Must be part of the source
        /// World.</param>
        /// <param name="entityRemapping">A set of entity transformations to make during the transfer.</param>
        /// <exception cref="ArgumentException"></exception>
        [NotBurstCompatible]
        public void MoveEntitiesFrom(out NativeArray<Entity> output, EntityManager srcEntities, EntityQuery filter, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
            MoveEntitiesFromInternalQuery(srcEntities, filter, entityRemapping);
            EntityRemapUtility.GetTargets(out output, entityRemapping);
        }

        /// <summary>
        /// Moves a selection of the entities managed by the specified EntityManager to the <see cref="World"/> of this EntityManager.
        /// </summary>
        /// <remarks>
        /// After the move, the entities are managed by this EntityManager.
        ///
        /// Each world has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one World to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        /// <param name="filter">A EntityQuery that defines the entities to move. Must be part of the source
        /// World.</param>
        /// <param name="entityRemapping">A set of entity transformations to make during the transfer.</param>
        /// <exception cref="ArgumentException">Thrown if the EntityQuery object used as the `filter` comes
        /// from a different world than the `srcEntities` EntityManager.</exception>
        [NotBurstCompatible]
        public void MoveEntitiesFrom(EntityManager srcEntities, EntityQuery filter, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
            MoveEntitiesFromInternalQuery(srcEntities, filter, entityRemapping);
        }

        /// <summary>
        /// Moves a selection of the entities managed by the specified EntityManager to the <see cref="World"/> of this EntityManager
        /// and fills an array with their <see cref="Entity"/> objects.
        /// </summary>
        /// <remarks>
        /// After the move, the entities are managed by this EntityManager. Use the `output` array to make post-move
        /// changes to the transferred entities.
        ///
        /// Each world has one EntityManager, which manages all the entities in that world. This function
        /// allows you to transfer entities from one World to another.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before moving the entities and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="output">An array to receive the Entity objects of the transferred entities.</param>
        /// <param name="srcEntities">The EntityManager whose entities are appropriated.</param>
        /// <param name="filter">A EntityQuery that defines the entities to move. Must be part of the source
        /// World.</param>
        /// <exception cref="ArgumentException"></exception>
        [NotBurstCompatible]
        public void MoveEntitiesFrom(out NativeArray<Entity> output, EntityManager srcEntities, EntityQuery filter)
        {
            using (var entityRemapping = srcEntities.CreateEntityRemapArray(Allocator.TempJob))
            {
                MoveEntitiesFromInternalQuery(srcEntities, filter, entityRemapping);
                EntityRemapUtility.GetTargets(out output, entityRemapping);
            }
        }

        /// <summary>
        /// Creates a remapping array with one element for each entity in the <see cref="World"/>.
        /// </summary>
        /// <param name="allocator">The type of memory allocation to use when creating the array.</param>
        /// <returns>An array containing a no-op identity transformation for each entity.</returns>
        public NativeArray<EntityRemapUtility.EntityRemapInfo> CreateEntityRemapArray(Allocator allocator)
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var array = CollectionHelper.CreateNativeArray<EntityRemapUtility.EntityRemapInfo>(ecs->EntitiesCapacity, allocator);
            return array;
        }

        /// <summary>
        /// Gets the version number of the specified component type.
        /// </summary>
        /// <remarks>This version number is incremented each time there is a structural change involving the specified
        /// type of component. Such changes include creating or destroying entities that have this component and adding
        /// or removing the component type from an entity. Shared components are not covered by this version;
        /// see <see cref="GetSharedComponentOrderVersion{T}(T)"/>.
        ///
        /// Version numbers can overflow. To compare if one version is more recent than another use a calculation such as:
        ///
        /// <code>
        /// bool VersionBisNewer = (VersionB - VersionA) > 0;
        /// </code>
        /// </remarks>
        /// <typeparam name="T">The component type.</typeparam>
        /// <returns>The current version number.</returns>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public int GetComponentOrderVersion<T>()
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            return ecs->GetComponentTypeOrderVersion(TypeManager.GetTypeIndex<T>());
        }

        /// <summary>
        /// Gets the version number of the specified shared component.
        /// </summary>
        /// <remarks>
        /// This version number is incremented each time there is a structural change involving entities in the chunk of
        /// the specified shared component. Such changes include creating or destroying entities or anything that changes
        /// the archetype of an entity.
        ///
        /// Version numbers can overflow. To compare if one version is more recent than another use a calculation such as:
        ///
        /// <code>
        /// bool VersionBisNewer = (VersionB - VersionA) > 0;
        /// </code>
        /// </remarks>
        /// <param name="sharedComponent">The shared component instance.</param>
        /// <typeparam name="T">The shared component type.</typeparam>
        /// <returns>The current version number.</returns>
        [NotBurstCompatible]
        public int GetSharedComponentOrderVersion<T>(T sharedComponent) where T : struct, ISharedComponentData
        {
            var access = GetCheckedEntityDataAccess();
            return access->GetSharedComponentVersion(sharedComponent);
        }

        // @TODO documentation for serialization/deserialization
        /// <summary>
        /// Prepares an empty <see cref="World"/> to load serialized entities.
        /// </summary>
        [NotBurstCompatible]
        public void PrepareForDeserialize()
        {
            if (Debug.EntityCount != 0)
            {
                using (var allEntities = GetAllEntities())
                {
                    throw new System.ArgumentException($"PrepareForDeserialize requires the world to be completely empty, but there are {allEntities.Length}.\nFor example: {Debug.GetEntityInfo(allEntities[0])}");
                }
            }

            GetCheckedEntityDataAccess()->ManagedComponentStore.PrepareForDeserialize();
        }

        //@TODO: Not clear to me what this method is really for...
        /// <summary>
        /// Waits for all Jobs to complete.
        /// </summary>
        /// <remarks>Calling CompleteAllJobs() blocks the main thread until all currently running Jobs finish.</remarks>
        public void CompleteAllJobs()
        {
            GetCheckedEntityDataAccess()->CompleteAllJobs();
        }

        /// <summary>
        /// Gets the dynamic type object required to access a chunk component of type T.
        /// </summary>
        /// <remarks>
        /// To access a component stored in a chunk, you must have the type registry information for the component.
        /// This function provides that information. Use the returned <see cref="ComponentTypeHandle{T}"/>
        /// object with the functions of an <see cref="ArchetypeChunk"/> object to get information about the components
        /// in that chunk and to access the component values.
        /// </remarks>
        /// <param name="isReadOnly">Specify whether the access to the component through this object is read only
        /// or read and write. For managed components isReadonly will always be treated as false.</param>
        /// <typeparam name="T">The compile-time type of the component.</typeparam>
        /// <returns>The run-time type information of the component.</returns>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public ComponentTypeHandle<T> GetComponentTypeHandle<T>(bool isReadOnly)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var access = GetCheckedEntityDataAccess();
            var typeIndex = TypeManager.GetTypeIndex<T>();
            return new ComponentTypeHandle<T>(
                access->DependencyManager->Safety.GetSafetyHandleForComponentTypeHandle(typeIndex, isReadOnly), isReadOnly,
                GlobalSystemVersion);
#else
            return new ComponentTypeHandle<T>(isReadOnly, GlobalSystemVersion);
#endif
        }

        /// <summary>
        /// Gets the dynamic type object required to access a chunk component of dynamic type acquired from reflection.
        /// </summary>
        /// <remarks>
        /// To access a component stored in a chunk, you must have the type registry information for the component.
        /// This function provides that information. Use the returned <see cref="DynamicComponentTypeHandle"/>
        /// object with the functions of an <see cref="ArchetypeChunk"/> object to get information about the components
        /// in that chunk and to access the component values.
        /// </remarks>
        /// <param name="componentType">Type of the component</param>
        /// <returns>The run-time type information of the component.</returns>
        public DynamicComponentTypeHandle GetDynamicComponentTypeHandle(ComponentType componentType)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var access = GetCheckedEntityDataAccess();
            if (!componentType.IsBuffer)
            {
                return new DynamicComponentTypeHandle(componentType,
                    access->DependencyManager->Safety.GetSafetyHandleForDynamicComponentTypeHandle(componentType.TypeIndex, componentType.AccessModeType == ComponentType.AccessMode.ReadOnly),
                    default(AtomicSafetyHandle), GlobalSystemVersion);
            }
            else
            {
                return new DynamicComponentTypeHandle(componentType,
                    access->DependencyManager->Safety.GetSafetyHandleForDynamicComponentTypeHandle(componentType.TypeIndex, componentType.AccessModeType == ComponentType.AccessMode.ReadOnly),
                    access->DependencyManager->Safety.GetBufferHandleForBufferTypeHandle(componentType.TypeIndex),
                    GlobalSystemVersion);
            }

#else
            return new DynamicComponentTypeHandle(componentType, GlobalSystemVersion);
#endif
        }

        /// <summary>
        /// Gets the dynamic type object required to access a chunk buffer containing elements of type T.
        /// </summary>
        /// <remarks>
        /// To access a component stored in a chunk, you must have the type registry information for the component.
        /// This function provides that information for buffer components. Use the returned
        /// <see cref="ComponentTypeHandle{T}"/> object with the functions of an <see cref="ArchetypeChunk"/>
        /// object to get information about the components in that chunk and to access the component values.
        /// </remarks>
        /// <param name="isReadOnly">Specify whether the access to the component through this object is read only
        /// or read and write. </param>
        /// <typeparam name="T">The compile-time type of the buffer elements.</typeparam>
        /// <returns>The run-time type information of the buffer component.</returns>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleBufferElement) })]
        public BufferTypeHandle<T> GetBufferTypeHandle<T>(bool isReadOnly)
            where T : struct, IBufferElementData
        {
            return GetCheckedEntityDataAccess()->GetBufferTypeHandle<T>(isReadOnly);
        }

        /// <summary>
        /// Gets the dynamic type object required to access a shared component of type T.
        /// </summary>
        /// <remarks>
        /// To access a component stored in a chunk, you must have the type registry information for the component.
        /// This function provides that information for shared components. Use the returned
        /// <see cref="ComponentTypeHandle{T}"/> object with the functions of an <see cref="ArchetypeChunk"/>
        /// object to get information about the components in that chunk and to access the component values.
        /// </remarks>
        /// <typeparam name="T">The compile-time type of the shared component.</typeparam>
        /// <returns>The run-time type information of the shared component.</returns>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleSharedComponentData) })]
        public SharedComponentTypeHandle<T> GetSharedComponentTypeHandle<T>()
            where T : struct, ISharedComponentData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var access = GetCheckedEntityDataAccess();
            return new SharedComponentTypeHandle<T>(access->DependencyManager->Safety.GetSafetyHandleForSharedComponentTypeHandle(typeIndex));
#else
            return new SharedComponentTypeHandle<T>(false);
#endif
        }

        /// <summary>
        /// Gets the dynamic type object required to access a shared component of the given type.
        /// </summary>
        /// <remarks>
        /// To access a component stored in a chunk, you must have the type registry information for the component.
        /// This function provides that information for shared components. Use the returned
        /// <see cref="DynamicSharedComponentTypeHandle"/> object with the functions of an <see cref="ArchetypeChunk"/>
        /// object to get information about the components in that chunk and to access the component values.
        /// </remarks>
        /// <param name="componentType">The component type to get access to.</param>
        /// <returns>The run-time type information of the shared component.</returns>
        public DynamicSharedComponentTypeHandle GetDynamicSharedComponentTypeHandle(ComponentType componentType)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var access = GetCheckedEntityDataAccess();
            return new DynamicSharedComponentTypeHandle(componentType,
                access->DependencyManager->Safety.GetSafetyHandleForDynamicComponentTypeHandle(componentType.TypeIndex,
                    // Only read only mode supported for DynamicSharedComponentTypeHandle
                    true));
#else
            return new DynamicSharedComponentTypeHandle(componentType);
#endif
        }

        /// <summary>
        /// Gets the dynamic type object required to access the <see cref="Entity"/> component of a chunk.
        /// </summary>
        /// <remarks>
        /// All chunks have an implicit <see cref="Entity"/> component referring to the entities in that chunk.
        ///
        /// To access any component stored in a chunk, you must have the type registry information for the component.
        /// This function provides that information for the implicit <see cref="Entity"/> component. Use the returned
        /// <see cref="ComponentTypeHandle{T}"/> object with the functions of an <see cref="ArchetypeChunk"/>
        /// object to access the component values.
        /// </remarks>
        /// <returns>The run-time type information of the Entity component.</returns>
        public EntityTypeHandle GetEntityTypeHandle()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var access = GetCheckedEntityDataAccess();
            return new EntityTypeHandle(
                access->DependencyManager->Safety.GetSafetyHandleForEntityTypeHandle());
#else
            return new EntityTypeHandle(false);
#endif
        }

        /// <summary>
        /// Gets an entity's component types.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="allocator">The type of allocation for creating the NativeArray to hold the ComponentType
        /// objects.</param>
        /// <returns>An array of ComponentType containing all the types of components associated with the entity.</returns>
        public NativeArray<ComponentType> GetComponentTypes(Entity entity, Allocator allocator = Allocator.Temp)
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;

            ecs->AssertEntitiesExist(&entity, 1);
            var archetype = new EntityArchetype { Archetype = ecs->GetArchetype(entity) };
            return archetype.GetComponentTypes(allocator);
        }

        /// <summary>
        /// Gets a list of the types of components that can be assigned to the specified component.
        /// </summary>
        /// <remarks>Assignable components include those with the same compile-time type and those that
        /// inherit from the same compile-time type.</remarks>
        /// <param name="interfaceType">The type to check.</param>
        /// <param name="listOut">The list to receive the output.</param>
        /// <returns>The list that was passed in, containing the System.Types that can be assigned to `interfaceType`.</returns>
        [NotBurstCompatible]
        public List<Type> GetAssignableComponentTypes(Type interfaceType, List<Type> listOut)
        {
            // #todo Cache this. It only can change when TypeManager.GetTypeCount() changes
            var componentTypeCount = TypeManager.GetTypeCount();
            for (var i = 0; i < componentTypeCount; i++)
            {
                var type = TypeManager.GetType(i);
                if (interfaceType.IsAssignableFrom(type)) listOut.Add(type);
            }

            return listOut;
        }

        /// <summary>
        /// Gets a list of the types of components that can be assigned to the specified component.
        /// </summary>
        /// <remarks>Assignable components include those with the same compile-time type and those that
        /// inherit from the same compile-time type.</remarks>
        /// <param name="interfaceType">The type to check.</param>
        /// <returns>A new List object containing the System.Types that can be assigned to `interfaceType`.</returns>
        [NotBurstCompatible]
        public List<Type> GetAssignableComponentTypes(Type interfaceType)
            => GetAssignableComponentTypes(interfaceType, new List<Type>());

        /// <summary>
        /// Creates a EntityQuery from an array of component types.
        /// </summary>
        /// <param name="requiredComponents">An array containing the component types.</param>
        /// <returns>The EntityQuery derived from the specified array of component types.</returns>
        /// <seealso cref="EntityQueryDesc"/>
        [NotBurstCompatible]
        public EntityQuery CreateEntityQuery(params ComponentType[] requiredComponents)
        {
            var access = GetCheckedEntityDataAccess();
            fixed(ComponentType* requiredComponentsPtr = requiredComponents)
            {
                return access->EntityQueryManager->CreateEntityQuery(access, requiredComponentsPtr, requiredComponents.Length);
            }
        }

        /// <summary>
        /// Creates a EntityQuery from an EntityQueryDesc.
        /// </summary>
        /// <param name="queriesDesc">A queryDesc identifying a set of component types.</param>
        /// <returns>The EntityQuery corresponding to the queryDesc.</returns>
        [NotBurstCompatible]
        public EntityQuery CreateEntityQuery(params EntityQueryDesc[] queriesDesc)
        {
            var builder = new EntityQueryDescBuilder(Allocator.Temp);
            EntityQueryManager.ConvertToEntityQueryDescBuilder(ref builder, queriesDesc);
            var result = CreateEntityQuery(builder);
            builder.Dispose();
            return result;
        }

        /// <summary>
        /// Creates an EntityQuery from an EntityQueryDescBuilder.
        /// </summary>
        /// <param name="queriesDesc">A queryDesc identifying a set of component types.</param>
        /// <returns>The EntityQuery corresponding to the queryDesc.</returns>
        public EntityQuery CreateEntityQuery(in EntityQueryDescBuilder queriesDesc)
        {
            var access = GetCheckedEntityDataAccess();
            return access->EntityQueryManager->CreateEntityQuery(access, queriesDesc);
        }

        /// <summary>
        /// Gets all the chunks managed by this EntityManager.
        /// </summary>
        /// <remarks>
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before getting these chunks and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="allocator">The type of allocation for creating the NativeArray to hold the ArchetypeChunk
        /// objects.</param>
        /// <returns>An array of ArchetypeChunk objects referring to all the chunks in the <see cref="World"/>.</returns>
        public NativeArray<ArchetypeChunk> GetAllChunks(Allocator allocator = Allocator.TempJob)
        {
            var access = GetCheckedEntityDataAccess();
            access->BeforeStructuralChange();
            var query = access->m_UniversalQuery;
            return GetAllChunks(allocator, query);
        }

        /// <summary>
        /// Gets all the chunks managed by this EntityManager, including the meta chunks (containing chunk components).
        /// </summary>
        /// <remarks>
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before getting these chunks and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="allocator">The type of allocation for creating the NativeArray to hold the ArchetypeChunk
        /// objects.</param>
        /// <returns>An array of ArchetypeChunk objects referring to all the chunks in the <see cref="World"/>.</returns>
        public NativeArray<ArchetypeChunk> GetAllChunksAndMetaChunks(Allocator allocator = Allocator.TempJob)
        {
            var access = GetCheckedEntityDataAccess();
            access->BeforeStructuralChange();
            var query = access->m_UniversalQueryWithChunks;
            return GetAllChunks(allocator, query);
        }

        private NativeArray<ArchetypeChunk> GetAllChunks(Allocator allocator, EntityQuery query)
        {
            var access = GetCheckedEntityDataAccess();
            access->AssertQueryIsValid(query);
            access->BeforeStructuralChange();
            return query.CreateArchetypeChunkArray(allocator);
        }

        /// <summary>
        /// Gets all the archetypes.
        /// </summary>
        /// <remarks>The function adds the archetype objects to the existing contents of the list.
        /// The list is not cleared.</remarks>
        /// <param name="allArchetypes">A native list to receive the EntityArchetype objects.</param>
        public void GetAllArchetypes(NativeList<EntityArchetype> allArchetypes)
        {
            var access = GetCheckedEntityDataAccess();
            for (var i = 0; i < access->EntityComponentStore->m_Archetypes.Length; ++i)
            {
                var archetype = access->EntityComponentStore->m_Archetypes.Ptr[i];
                var entityArchetype = new EntityArchetype()
                {
                    Archetype = archetype,
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                    _DebugComponentStore = access->EntityComponentStore
#endif
                };
                allArchetypes.Add(entityArchetype);
            }
        }

        /// <summary>
        /// Gets an <see cref="EntityQueryMask"/> that can be used to quickly match if an entity belongs to an EntityQuery.
        /// There is a maximum limit of 1024 EntityQueryMasks that can be created.  EntityQueryMasks cannot be created
        /// from EntityQueries with filters.
        /// </summary>
        /// <remarks>Note that EntityQueryMask only filters by Archetype, it doesn't support EntityQuery shared component or change filtering.</remarks>
        /// <param name="query">The EntityQuery that describes the EntityQueryMask.</param>
        /// <returns>The EntityQueryMask corresponding to the EntityQuery.</returns>
        public EntityQueryMask GetEntityQueryMask(EntityQuery query)
        {
            var access = GetCheckedEntityDataAccess();
            access->AssertQueryIsValid(query);
            return access->EntityQueryManager->GetEntityQueryMask(query.__impl->_QueryData, access->EntityComponentStore);
        }

        // @TODO Point to documentation for multithreaded way to check Entity validity.
        /// <summary>
        /// Reports whether an Entity object is still valid.
        /// </summary>
        /// <remarks>
        /// An Entity object does not contain a reference to its entity. Instead, the Entity struct contains an index
        /// and a generational version number. When an entity is destroyed, the EntityManager increments the version
        /// of the entity within the internal array of entities. The index of a destroyed entity is recycled when a
        /// new entity is created.
        ///
        /// After an entity is destroyed, any existing Entity objects will still contain the
        /// older version number. This function compares the version numbers of the specified Entity object and the
        /// current version of the entity recorded in the entities array. If the versions are different, the Entity
        /// object no longer refers to an existing entity and cannot be used.
        /// </remarks>
        /// <param name="entity">The Entity object to check.</param>
        /// <returns>True, if <see cref="Entity.Version"/> matches the version of the current entity at
        /// <see cref="Entity.Index"/> in the entities array.</returns>
        public bool Exists(Entity entity)
        {
            return GetCheckedEntityDataAccess()->Exists(entity);
        }

        /// <summary>
        /// Checks whether an entity has a specific type of component.
        /// </summary>
        /// <remarks>Always returns false for an entity that has been destroyed.</remarks>
        /// <param name="entity">The Entity object.</param>
        /// <typeparam name="T">The data type of the component.</typeparam>
        /// <returns>True, if the specified entity has the component.</returns>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public bool HasComponent<T>(Entity entity)
        {
            return GetCheckedEntityDataAccess()->HasComponent(entity, ComponentType.ReadWrite<T>());
        }

        /// <summary>
        /// Checks whether an entity has a specific type of component.
        /// </summary>
        /// <remarks>Always returns false for an entity that has been destroyed.</remarks>
        /// <param name="entity">The Entity object.</param>
        /// <param name="type">The data type of the component.</param>
        /// <returns>True, if the specified entity has the component.</returns>
        public bool HasComponent(Entity entity, ComponentType type)
        {
            return GetCheckedEntityDataAccess()->HasComponent(entity, type);
        }

        /// <summary>
        /// Checks whether the chunk containing an entity has a specific type of component.
        /// </summary>
        /// <remarks>Always returns false for an entity that has been destroyed.</remarks>
        /// <param name="entity">The Entity object.</param>
        /// <typeparam name="T">The data type of the chunk component.</typeparam>
        /// <returns>True, if the chunk containing the specified entity has the component.</returns>
        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public bool HasChunkComponent<T>(Entity entity)
        {
            return GetCheckedEntityDataAccess()->HasComponent(entity, ComponentType.ChunkComponent<T>());
        }

        // ----------------------------------------------------------------------------------------------------------
        // INTERNAL
        // ----------------------------------------------------------------------------------------------------------

        [NotBurstCompatible]
        internal void SetSharedComponentDataBoxedDefaultMustBeNull(Entity entity, int typeIndex, object componentData)
        {
            var hashCode = 0;
            if (componentData != null)
                hashCode = TypeManager.GetHashCode(componentData, typeIndex);

            SetSharedComponentDataBoxedDefaultMustBeNull(entity, typeIndex, hashCode, componentData);
        }

        [NotBurstCompatible]
        void SetSharedComponentDataBoxedDefaultMustBeNull(Entity entity, int typeIndex, int hashCode, object componentData)
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            access->SetSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(entity, typeIndex, hashCode, componentData);
            access->EndStructuralChanges(ref changes);
        }

        [NotBurstCompatible]
        internal void SetComponentObject(Entity entity, ComponentType componentType, object componentObject)
        {
            var access = GetCheckedEntityDataAccess();
            access->SetComponentObject(entity, componentType, componentObject);
        }

        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        internal ComponentDataFromEntity<T> GetComponentDataFromEntity<T>(bool isReadOnly = false)
            where T : struct, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            return GetComponentDataFromEntity<T>(typeIndex, isReadOnly);
        }

        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        internal ComponentDataFromEntity<T> GetComponentDataFromEntity<T>(int typeIndex, bool isReadOnly)
            where T : struct, IComponentData
        {
            var access = GetCheckedEntityDataAccess();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safetyHandles = &access->DependencyManager->Safety;
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            return new ComponentDataFromEntity<T>(typeIndex, access,
                safetyHandles->GetSafetyHandleForComponentDataFromEntity(typeIndex, isReadOnly));
#else
            return new ComponentDataFromEntity<T>(typeIndex, access);
#endif
        }

        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleBufferElement) })]
        internal BufferFromEntity<T> GetBufferFromEntity<T>(bool isReadOnly = false)
            where T : struct, IBufferElementData
        {
            return GetBufferFromEntity<T>(TypeManager.GetTypeIndex<T>(), isReadOnly);
        }

        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleBufferElement) })]
        internal BufferFromEntity<T> GetBufferFromEntity<T>(int typeIndex, bool isReadOnly = false)
            where T : struct, IBufferElementData
        {
            var access = GetCheckedEntityDataAccess();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safetyHandles = &access->DependencyManager->Safety;
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            return new BufferFromEntity<T>(typeIndex, access, isReadOnly,
                safetyHandles->GetSafetyHandleForComponentDataFromEntity(typeIndex, isReadOnly),
                safetyHandles->GetBufferHandleForBufferFromEntity(typeIndex));
#else
            return new BufferFromEntity<T>(typeIndex, access, isReadOnly);
#endif
        }

        internal StorageInfoFromEntity GetStorageInfoFromEntity()
        {
            var access = GetCheckedEntityDataAccess();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safetyHandles = &access->DependencyManager->Safety;
            return new StorageInfoFromEntity(access->EntityComponentStore, safetyHandles->GetSafetyHandleForEntityTypeHandle());
#else
            return new StorageInfoFromEntity(access->EntityComponentStore);
#endif
        }

        internal void SetComponentDataRaw(Entity entity, int typeIndex, void* data, int size)
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var deps = access->DependencyManager;

            deps->CompleteReadAndWriteDependency(typeIndex);
            access->SetComponentDataRaw(entity, typeIndex, data, size);
        }

        internal void* GetComponentDataRawRW(Entity entity, int typeIndex)
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var deps = access->DependencyManager;
            ecs->AssertEntityHasComponent(entity, typeIndex);
            deps->CompleteReadAndWriteDependency(typeIndex);

            return access->GetComponentDataRawRWEntityHasComponent(entity, typeIndex);
        }

        internal void* GetComponentDataRawRO(Entity entity, int typeIndex)
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var deps = access->DependencyManager;
            ecs->AssertEntityHasComponent(entity, typeIndex);
            deps->CompleteWriteDependency(typeIndex);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (TypeManager.GetTypeInfo(typeIndex).IsZeroSized)
                throw new System.ArgumentException(
                    $"GetComponentDataRawRO can not be called with a zero sized component.");
#endif


            var ptr = ecs->GetComponentDataWithTypeRO(entity, typeIndex);
            return ptr;
        }

        [NotBurstCompatible]
        internal object GetSharedComponentData(Entity entity, int typeIndex)
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var mcs = access->ManagedComponentStore;

            ecs->AssertEntityHasComponent(entity, typeIndex);

            var sharedComponentIndex = ecs->GetSharedComponentDataIndex(entity, typeIndex);
            return mcs.GetSharedComponentDataBoxed(sharedComponentIndex, typeIndex);
        }

        internal void* GetBufferRawRW(Entity entity, int typeIndex)
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;

            ecs->AssertEntityHasComponent(entity, typeIndex);

            access->DependencyManager->CompleteReadAndWriteDependency(typeIndex);

            BufferHeader* header = (BufferHeader*)ecs->GetComponentDataWithTypeRW(entity, typeIndex, ecs->GlobalSystemVersion);

            return BufferHeader.GetElementPointer(header);
        }

        internal void* GetBufferRawRO(Entity entity, int typeIndex)
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;

            ecs->AssertEntityHasComponent(entity, typeIndex);

            access->DependencyManager->CompleteWriteDependency(typeIndex);

            BufferHeader* header = (BufferHeader*)ecs->GetComponentDataWithTypeRO(entity, typeIndex);

            return BufferHeader.GetElementPointer(header);
        }

        internal int GetBufferLength(Entity entity, int typeIndex)
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;

            ecs->AssertEntityHasComponent(entity, typeIndex);

            access->DependencyManager->CompleteWriteDependency(typeIndex);

            BufferHeader* header = (BufferHeader*)ecs->GetComponentDataWithTypeRO(entity, typeIndex);

            return header->Length;
        }

        // these are used by tiny, do not remove
        [UsedImplicitly]
        internal void AddComponentRaw(Entity entity, int typeIndex) => AddComponent(entity, ComponentType.FromTypeIndex(typeIndex));
        [UsedImplicitly]
        internal void RemoveComponentRaw(Entity entity, int typeIndex) => RemoveComponent(entity, ComponentType.FromTypeIndex(typeIndex));

        internal void DestroyEntityInternal(Entity* entities, int count)
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            access->DestroyEntityInternalDuringStructuralChange(entities, count);
            access->EndStructuralChanges(ref changes);
        }

        void DestroyEntity(UnsafeCachedChunkList cache, UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter)
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            access->DestroyEntityDuringStructuralChange(cache, archetypeList, filter);
            access->EndStructuralChanges(ref changes);
        }

        internal EntityArchetype CreateArchetype(ComponentType* types, int count)
        {
            var access = GetCheckedEntityDataAccess();
            var changes = access->BeginStructuralChanges();
            var result = access->CreateArchetype(types, count);
            access->EndStructuralChanges(ref changes);
            return result;
        }

        void MoveEntitiesFromInternalQuery(EntityManager srcEntities, EntityQuery filter, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
            var srcAccess = srcEntities.GetCheckedEntityDataAccess();
            var selfAccess = GetCheckedEntityDataAccess();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (filter._GetImpl()->_Access != srcAccess)
                throw new ArgumentException(
                    "EntityManager.MoveEntitiesFrom failed - srcEntities and filter must belong to the same World)");

            if (srcEntities.m_EntityDataAccess == m_EntityDataAccess)
                throw new ArgumentException("srcEntities must not be the same as this EntityManager.");
#endif
            BeforeStructuralChange();
            srcEntities.BeforeStructuralChange();

            using (var chunks = filter.CreateArchetypeChunkArray(Allocator.TempJob))
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                for (int i = 0; i < chunks.Length; ++i)
                    if (chunks[i].m_Chunk->Archetype->HasChunkHeader)
                        throw new ArgumentException("MoveEntitiesFrom can not move chunks that contain ChunkHeader components.");
#endif

                var archetypeChanges = selfAccess->EntityComponentStore->BeginArchetypeChangeTracking();

                MoveChunksFromFiltered(chunks, entityRemapping, srcAccess->EntityComponentStore, srcAccess->ManagedComponentStore);

                selfAccess->EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, selfAccess->EntityQueryManager);
                selfAccess->EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
                srcAccess->EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
            }
        }

        [NotBurstCompatible]
        public void MoveEntitiesFromInternalAll(EntityManager srcEntities, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
            var srcAccess = srcEntities.GetCheckedEntityDataAccess();
            var selfAccess = GetCheckedEntityDataAccess();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (srcEntities.m_EntityDataAccess == m_EntityDataAccess)
                throw new ArgumentException("srcEntities must not be the same as this EntityManager.");

            if (entityRemapping.Length < srcAccess->EntityComponentStore->EntitiesCapacity)
                throw new ArgumentException("entityRemapping.Length isn't large enough, use srcEntities.CreateEntityRemapArray");

            if (!srcAccess->ManagedComponentStore.AllSharedComponentReferencesAreFromChunks(srcAccess->EntityComponentStore))
                throw new ArgumentException(
                    "EntityManager.MoveEntitiesFrom failed - All ISharedComponentData references must be from EntityManager. (For example EntityQuery.SetFilter with a shared component type is not allowed during EntityManager.MoveEntitiesFrom)");
#endif

            srcEntities.BeforeStructuralChange();
            var archetypeChanges = selfAccess->BeginStructuralChanges();

            MoveChunksFromAll(entityRemapping, srcAccess->EntityComponentStore, srcAccess->ManagedComponentStore);

            selfAccess->EndStructuralChanges(ref archetypeChanges);
            srcAccess->EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
        }

        [NotBurstCompatible]
        internal void MoveChunksFromFiltered(
            NativeArray<ArchetypeChunk> chunks,
            NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping,
            EntityComponentStore* srcEntityComponentStore,
            ManagedComponentStore srcManagedComponentStore)
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var mcs = access->ManagedComponentStore;

            new MoveChunksJob
            {
                srcEntityComponentStore = srcEntityComponentStore,
                dstEntityComponentStore = ecs,
                entityRemapping = entityRemapping,
                chunks = chunks
            }.Run();

            int chunkCount = chunks.Length;
            var remapChunks = new NativeArray<RemapChunk>(chunkCount, Allocator.TempJob);

            Archetype* previousSrcArchetypee = null;
            Archetype* dstArchetype = null;
            int managedComponentCount = 0;

            NativeList<IntPtr> managedChunks = new NativeList<IntPtr>(0, Allocator.TempJob);

            for (int i = 0; i < chunkCount; ++i)
            {
                var chunk = chunks[i].m_Chunk;
                var archetype = chunk->Archetype;

                // Move Chunk World. ChangeVersion:Yes OrderVersion:Yes
                if (previousSrcArchetypee != archetype)
                {
                    dstArchetype = ecs->GetOrCreateArchetype(archetype->Types, archetype->TypesCount);
                }

                remapChunks[i] = new RemapChunk {chunk = chunk, dstArchetype = dstArchetype};

                if (dstArchetype->NumManagedComponents > 0)
                {
                    managedComponentCount += chunk->Count * dstArchetype->NumManagedComponents;
                    managedChunks.Add((IntPtr)chunk);
                }

                if (archetype->MetaChunkArchetype != null)
                {
                    Entity srcEntity = chunk->metaChunkEntity;
                    Entity dstEntity;

                    ecs->CreateEntities(dstArchetype->MetaChunkArchetype, &dstEntity, 1);

                    var srcEntityInChunk = srcEntityComponentStore->GetEntityInChunk(srcEntity);
                    var dstEntityInChunk = ecs->GetEntityInChunk(dstEntity);

                    ChunkDataUtility.SwapComponents(srcEntityInChunk.Chunk, srcEntityInChunk.IndexInChunk, dstEntityInChunk.Chunk, dstEntityInChunk.IndexInChunk, 1,
                        srcEntityComponentStore->GlobalSystemVersion, ecs->GlobalSystemVersion);
                    EntityRemapUtility.AddEntityRemapping(ref entityRemapping, srcEntity, dstEntity);

                    srcEntityComponentStore->DestroyEntities(&srcEntity, 1);
                }
            }

            NativeArray<int> srcManagedIndices = default;
            NativeArray<int> dstManagedIndices = default;
            int nonNullManagedComponentCount = 0;
            if (managedComponentCount > 0)
            {
                srcManagedIndices = new NativeArray<int>(managedComponentCount, Allocator.TempJob);
                dstManagedIndices = new NativeArray<int>(managedComponentCount, Allocator.TempJob);
                new
                GatherManagedComponentIndicesInChunkJob()
                {
                    SrcEntityComponentStore = srcEntityComponentStore,
                    DstEntityComponentStore = ecs,
                    SrcManagedIndices = srcManagedIndices,
                    DstManagedIndices = dstManagedIndices,
                    Chunks = managedChunks,
                    NonNullCount = &nonNullManagedComponentCount
                }.Run();
            }

            mcs.Playback(ref ecs->ManagedChangesTracker);
            srcManagedComponentStore.Playback(ref srcEntityComponentStore->ManagedChangesTracker);

            k_ProfileMoveSharedComponents.Begin();
            var remapShared = access->MoveSharedComponents(srcManagedComponentStore, chunks, Allocator.TempJob);
            k_ProfileMoveSharedComponents.End();

            if (managedComponentCount > 0)
            {
                k_ProfileMoveManagedComponents.Begin();
                mcs.MoveManagedComponentsFromDifferentWorld(srcManagedIndices, dstManagedIndices, nonNullManagedComponentCount, srcManagedComponentStore);
                srcEntityComponentStore->m_ManagedComponentFreeIndex.Add(srcManagedIndices.GetUnsafeReadOnlyPtr(), sizeof(int) * srcManagedIndices.Length);
                k_ProfileMoveManagedComponents.End();
            }

            new ChunkPatchEntities
            {
                RemapChunks = remapChunks,
                EntityRemapping = entityRemapping,
                EntityComponentStore = ecs
            }.Run();

            var remapChunksJob = new RemapChunksFilteredJob
            {
                dstEntityComponentStore = ecs,
                remapChunks = remapChunks,
                entityRemapping = entityRemapping,
                chunkHeaderType = TypeManager.GetTypeIndex<ChunkHeader>()
            }.Schedule(remapChunks.Length, 1);

            var moveChunksBetweenArchetypeJob = new MoveFilteredChunksBetweenArchetypeJob
            {
                RemapChunks = remapChunks,
                RemapShared = remapShared,
                GlobalSystemVersion = ecs->GlobalSystemVersion
            }.Schedule(remapChunksJob);

            moveChunksBetweenArchetypeJob.Complete();

            mcs.Playback(ref ecs->ManagedChangesTracker);

            if (managedComponentCount > 0)
            {
                srcManagedIndices.Dispose();
                dstManagedIndices.Dispose();
            }
            managedChunks.Dispose();
            remapShared.Dispose();
            remapChunks.Dispose();
        }

        [NotBurstCompatible]
        internal void MoveChunksFromAll(
            NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping,
            EntityComponentStore* srcEntityComponentStore,
            ManagedComponentStore srcManagedComponentStore)
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var mcs = access->ManagedComponentStore;

            var moveChunksJob = new MoveAllChunksJob
            {
                srcEntityComponentStore = srcEntityComponentStore,
                dstEntityComponentStore = ecs,
                entityRemapping = entityRemapping
            }.Schedule();

            int managedComponentCount = srcEntityComponentStore->ManagedComponentIndexUsedCount;
            NativeArray<int> srcManagedIndices = default;
            NativeArray<int> dstManagedIndices = default;
            JobHandle gatherManagedComponentIndices = default;
            if (managedComponentCount > 0)
            {
                srcManagedIndices = new NativeArray<int>(managedComponentCount, Allocator.TempJob);
                dstManagedIndices = new NativeArray<int>(managedComponentCount, Allocator.TempJob);
                ecs->ReserveManagedComponentIndices(managedComponentCount);
                gatherManagedComponentIndices = new
                    GatherAllManagedComponentIndicesJob
                {
                    SrcEntityComponentStore = srcEntityComponentStore,
                    DstEntityComponentStore = ecs,
                    SrcManagedIndices = srcManagedIndices,
                    DstManagedIndices = dstManagedIndices
                }.Schedule();
            }

            JobHandle.ScheduleBatchedJobs();

            int chunkCount = 0;
            for (var i = 0; i < srcEntityComponentStore->m_Archetypes.Length; ++i)
            {
                var srcArchetype = srcEntityComponentStore->m_Archetypes.Ptr[i];
                chunkCount += srcArchetype->Chunks.Count;
            }

            var remapChunks = new NativeArray<RemapChunk>(chunkCount, Allocator.TempJob);
            var remapArchetypes = new NativeArray<RemapArchetype>(srcEntityComponentStore->m_Archetypes.Length, Allocator.TempJob);

            int chunkIndex = 0;
            int archetypeIndex = 0;
            for (var i = 0; i < srcEntityComponentStore->m_Archetypes.Length; ++i)
            {
                var srcArchetype = srcEntityComponentStore->m_Archetypes.Ptr[i];
                if (srcArchetype->Chunks.Count != 0)
                {
                    var dstArchetype = ecs->GetOrCreateArchetype(srcArchetype->Types,
                        srcArchetype->TypesCount);

                    remapArchetypes[archetypeIndex] = new RemapArchetype
                    {srcArchetype = srcArchetype, dstArchetype = dstArchetype};

                    srcEntityComponentStore->m_ChunkListChangesTracker.TrackArchetype(srcArchetype);
                    ecs->m_ChunkListChangesTracker.TrackArchetype(dstArchetype);

                    for (var j = 0; j < srcArchetype->Chunks.Count; ++j)
                    {
                        var srcChunk = srcArchetype->Chunks[j];
                        remapChunks[chunkIndex] = new RemapChunk {chunk = srcChunk, dstArchetype = dstArchetype};
                        chunkIndex++;
                    }

                    archetypeIndex++;
                    ecs->IncrementComponentTypeOrderVersion(dstArchetype);
                }
            }

            moveChunksJob.Complete();

            mcs.Playback(ref ecs->ManagedChangesTracker);
            srcManagedComponentStore.Playback(ref srcEntityComponentStore->ManagedChangesTracker);

            k_ProfileMoveSharedComponents.Begin();
            var remapShared = access->MoveAllSharedComponents(srcManagedComponentStore, Allocator.TempJob);
            k_ProfileMoveSharedComponents.End();

            gatherManagedComponentIndices.Complete();

            k_ProfileMoveManagedComponents.Begin();
            mcs.MoveManagedComponentsFromDifferentWorld(srcManagedIndices, dstManagedIndices, srcManagedIndices.Length, srcManagedComponentStore);
            srcEntityComponentStore->m_ManagedComponentFreeIndex.Length = 0;
            srcEntityComponentStore->m_ManagedComponentIndex = 1;
            k_ProfileMoveManagedComponents.End();

            new ChunkPatchEntities
            {
                RemapChunks = remapChunks,
                EntityRemapping = entityRemapping,
                EntityComponentStore = ecs
            }.Run();

            var remapAllChunksJob = new RemapAllChunksJob
            {
                dstEntityComponentStore = ecs,
                remapChunks = remapChunks,
                entityRemapping = entityRemapping
            }.Schedule(remapChunks.Length, 1);

            var remapArchetypesJob = new RemapAllArchetypesJob
            {
                remapArchetypes = remapArchetypes,
                remapShared = remapShared,
                dstEntityComponentStore = ecs,
                chunkHeaderType = TypeManager.GetTypeIndex<ChunkHeader>()
            }.Schedule(archetypeIndex, 1, remapAllChunksJob);

            mcs.Playback(ref ecs->ManagedChangesTracker);

            if (managedComponentCount > 0)
            {
                srcManagedIndices.Dispose();
                dstManagedIndices.Dispose();
            }

            remapArchetypesJob.Complete();
            remapShared.Dispose();
            remapChunks.Dispose();
        }

        internal struct RemapChunk
        {
            public Chunk* chunk;
            public Archetype* dstArchetype;
        }

        internal struct RemapArchetype
        {
            public Archetype* srcArchetype;
            public Archetype* dstArchetype;
        }

        [BurstCompile]
        internal struct ChunkPatchEntities : IJobBurstSchedulable
        {
            public NativeArray<RemapChunk> RemapChunks;
            public NativeArray<EntityRemapUtility.EntityRemapInfo> EntityRemapping;
            [NativeDisableUnsafePtrRestriction]
            public EntityComponentStore* EntityComponentStore;

            public void Execute()
            {
                for (int i = 0; i < RemapChunks.Length; i++)
                {
                    var remapChunk = RemapChunks[i];
                    Chunk* chunk = remapChunk.chunk;
                    Archetype* dstArchetype = remapChunk.dstArchetype;
                    EntityComponentStore->ManagedChangesTracker.PatchEntities(dstArchetype, chunk, chunk->Count, EntityRemapping);
                }
            }
        }

        [BurstCompile]
        internal struct MoveChunksJob : IJobBurstSchedulable
        {
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* srcEntityComponentStore;
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* dstEntityComponentStore;
            public NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping;
            [Collections.ReadOnly] public NativeArray<ArchetypeChunk> chunks;

            public void Execute()
            {
                int chunkCount = chunks.Length;
                for (int i = 0; i < chunkCount; ++i)
                {
                    var chunk = chunks[i].m_Chunk;
                    dstEntityComponentStore->AllocateEntitiesForRemapping(chunk, srcEntityComponentStore, ref entityRemapping);
                    srcEntityComponentStore->FreeEntities(chunk);
                }
            }
        }

        [BurstCompile]
        internal struct GatherAllManagedComponentIndicesJob : IJobBurstSchedulable
        {
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* SrcEntityComponentStore;
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* DstEntityComponentStore;

            public NativeArray<int> SrcManagedIndices;
            public NativeArray<int> DstManagedIndices;
            public void Execute()
            {
                DstEntityComponentStore->AllocateManagedComponentIndices((int*)DstManagedIndices.GetUnsafePtr(), DstManagedIndices.Length);
                int srcCounter = 0;
                for (var iChunk = 0; iChunk < SrcEntityComponentStore->m_Archetypes.Length; ++iChunk)
                {
                    var srcArchetype = SrcEntityComponentStore->m_Archetypes.Ptr[iChunk];
                    for (var j = 0; j < srcArchetype->Chunks.Count; ++j)
                    {
                        var chunk = srcArchetype->Chunks[j];
                        var firstManagedComponent = srcArchetype->FirstManagedComponent;
                        var numManagedComponents = srcArchetype->NumManagedComponents;
                        for (int i = 0; i < numManagedComponents; ++i)
                        {
                            int type = i + firstManagedComponent;
                            var a = (int*)ChunkDataUtility.GetComponentDataRO(chunk, 0, type);
                            for (int ei = 0; ei < chunk->Count; ++ei)
                            {
                                var managedComponentIndex = a[ei];
                                if (managedComponentIndex == 0)
                                    continue;

                                SrcManagedIndices[srcCounter] = managedComponentIndex;
                                a[ei] = DstManagedIndices[srcCounter++];
                            }
                        }
                    }
                }

                Assert.AreEqual(SrcManagedIndices.Length, srcCounter);
            }
        }

        [BurstCompile]
        struct GatherManagedComponentIndicesInChunkJob : IJobBurstSchedulable
        {
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* SrcEntityComponentStore;
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* DstEntityComponentStore;
            public NativeArray<IntPtr> Chunks;

            public NativeArray<int> SrcManagedIndices;
            public NativeArray<int> DstManagedIndices;

            [NativeDisableUnsafePtrRestriction] public int* NonNullCount;

            public void Execute()
            {
                var count = DstManagedIndices.Length;
                DstEntityComponentStore->AllocateManagedComponentIndices((int*)DstManagedIndices.GetUnsafePtr(), count);

                int srcCounter = 0;
                for (var iChunk = 0; iChunk < Chunks.Length; ++iChunk)
                {
                    var chunk = (Chunk*)Chunks[iChunk];
                    var srcArchetype = chunk->Archetype;
                    var firstManagedComponent = srcArchetype->FirstManagedComponent;
                    var numManagedComponents = srcArchetype->NumManagedComponents;
                    for (int i = 0; i < numManagedComponents; ++i)
                    {
                        int type = i + firstManagedComponent;
                        var a = (int*)ChunkDataUtility.GetComponentDataRO(chunk, 0, type);
                        for (int ei = 0; ei < chunk->Count; ++ei)
                        {
                            var managedComponentIndex = a[ei];
                            if (managedComponentIndex == 0)
                                continue;

                            SrcManagedIndices[srcCounter] = managedComponentIndex;
                            a[ei] = DstManagedIndices[srcCounter++];
                        }
                    }
                }

                if (srcCounter < count)
                    DstEntityComponentStore->m_ManagedComponentFreeIndex.Add((int*)DstManagedIndices.GetUnsafePtr() + srcCounter, (count - srcCounter) * sizeof(int));
                *NonNullCount = srcCounter;
            }
        }

        [BurstCompile]
        struct RemapChunksFilteredJob : IJobParallelForBurstSchedulable
        {
            [Collections.ReadOnly] public NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping;
            [Collections.ReadOnly] public NativeArray<RemapChunk> remapChunks;

            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* dstEntityComponentStore;

            public int chunkHeaderType;

            public void Execute(int index)
            {
                Chunk* chunk = remapChunks[index].chunk;
                Archetype* dstArchetype = remapChunks[index].dstArchetype;

                dstEntityComponentStore->RemapChunk(dstArchetype, chunk, 0, chunk->Count, ref entityRemapping);
                EntityRemapUtility.PatchEntities(dstArchetype->ScalarEntityPatches + 1,
                    dstArchetype->ScalarEntityPatchCount - 1, dstArchetype->BufferEntityPatches,
                    dstArchetype->BufferEntityPatchCount, chunk->Buffer, chunk->Count, ref entityRemapping);

                // Fix up chunk pointers in ChunkHeaders
                if (dstArchetype->HasChunkComponents)
                {
                    var metaArchetype = dstArchetype->MetaChunkArchetype;
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(metaArchetype, chunkHeaderType);
                    var offset = metaArchetype->Offsets[indexInTypeArray];
                    var sizeOf = sizeof(ChunkHeader);

                    // Set chunk header without bumping change versions since they are zeroed when processing meta chunk
                    // modifying them here would be a race condition
                    var metaChunkEntity = chunk->metaChunkEntity;
                    var metaEntityInChunk = dstEntityComponentStore->GetEntityInChunk(metaChunkEntity);
                    var chunkHeader = (ChunkHeader*)(metaEntityInChunk.Chunk->Buffer + (offset + sizeOf * metaEntityInChunk.IndexInChunk));
                    chunkHeader->ArchetypeChunk = new ArchetypeChunk(chunk, dstEntityComponentStore);
                }
            }
        }

        [BurstCompile]
        struct MoveFilteredChunksBetweenArchetypeJob : IJobBurstSchedulable
        {
            [Collections.ReadOnly] public NativeArray<RemapChunk> RemapChunks;
            [Collections.ReadOnly] public NativeArray<int> RemapShared;
            public uint GlobalSystemVersion;

            public void Execute()
            {
                int chunkCount = RemapChunks.Length;
                for (int iChunk = 0; iChunk < chunkCount; ++iChunk)
                {
                    var chunk = RemapChunks[iChunk].chunk;
                    var dstArchetype = RemapChunks[iChunk].dstArchetype;
                    int numSharedComponents = dstArchetype->NumSharedComponents;
                    var sharedComponentValues = chunk->SharedComponentValues;
                    if (numSharedComponents != 0)
                    {
                        var alloc = stackalloc int[numSharedComponents];

                        for (int i = 0; i < numSharedComponents; ++i)
                            alloc[i] = RemapShared[sharedComponentValues[i]];
                        sharedComponentValues = alloc;
                    }

                    ChunkDataUtility.MoveArchetype(chunk, dstArchetype, sharedComponentValues);
                }
            }
        }

        [BurstCompile]
        struct RemapAllChunksJob : IJobParallelForBurstSchedulable
        {
            [Collections.ReadOnly] public NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping;
            [Collections.ReadOnly] public NativeArray<RemapChunk> remapChunks;

            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* dstEntityComponentStore;

            public void Execute(int index)
            {
                Chunk* chunk = remapChunks[index].chunk;
                Archetype* dstArchetype = remapChunks[index].dstArchetype;

                dstEntityComponentStore->RemapChunk(dstArchetype, chunk, 0, chunk->Count, ref entityRemapping);
                EntityRemapUtility.PatchEntities(dstArchetype->ScalarEntityPatches + 1,
                    dstArchetype->ScalarEntityPatchCount - 1, dstArchetype->BufferEntityPatches,
                    dstArchetype->BufferEntityPatchCount, chunk->Buffer, chunk->Count, ref entityRemapping);

                chunk->Archetype = dstArchetype;
                chunk->ListIndex += dstArchetype->Chunks.Count;
                chunk->ListWithEmptySlotsIndex += dstArchetype->ChunksWithEmptySlots.Length;
            }
        }

        [BurstCompile]
        struct RemapAllArchetypesJob : IJobParallelForBurstSchedulable
        {
            [DeallocateOnJobCompletion][Collections.ReadOnly] public NativeArray<RemapArchetype> remapArchetypes;

            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* dstEntityComponentStore;

            [Collections.ReadOnly] public NativeArray<int> remapShared;

            public int chunkHeaderType;

            // This must be run after chunks have been remapped since FreeChunksBySharedComponents needs the shared component
            // indices in the chunks to be remapped
            public void Execute(int index)
            {
                var srcArchetype = remapArchetypes[index].srcArchetype;
                int srcChunkCount = srcArchetype->Chunks.Count;

                var dstArchetype = remapArchetypes[index].dstArchetype;
                int dstChunkCount = dstArchetype->Chunks.Count;

                dstArchetype->Chunks.MoveChunks(srcArchetype->Chunks);

                if (srcArchetype->NumSharedComponents == 0)
                {
                    if (srcArchetype->ChunksWithEmptySlots.Length != 0)
                    {
                        dstArchetype->ChunksWithEmptySlots.SetCapacity(
                            srcArchetype->ChunksWithEmptySlots.Length + dstArchetype->ChunksWithEmptySlots.Length);
                        dstArchetype->ChunksWithEmptySlots.AddRange(srcArchetype->ChunksWithEmptySlots);
                        srcArchetype->ChunksWithEmptySlots.Resize(0);
                    }
                }
                else
                {
                    for (int i = 0; i < dstArchetype->NumSharedComponents; ++i)
                    {
                        var srcArray = srcArchetype->Chunks.GetSharedComponentValueArrayForType(i);
                        var dstArray = dstArchetype->Chunks.GetSharedComponentValueArrayForType(i) + dstChunkCount;
                        for (int j = 0; j < srcChunkCount; ++j)
                        {
                            int srcIndex = srcArray[j];
                            int remapped = remapShared[srcIndex];
                            dstArray[j] = remapped;
                        }
                    }

                    for (int i = 0; i < srcChunkCount; ++i)
                    {
                        var chunk = dstArchetype->Chunks[i + dstChunkCount];
                        if (chunk->Count < chunk->Capacity)
                            dstArchetype->FreeChunksBySharedComponents.Add(dstArchetype->Chunks[i + dstChunkCount]);
                    }

                    srcArchetype->FreeChunksBySharedComponents.Init(16);
                }

                var globalSystemVersion = dstEntityComponentStore->GlobalSystemVersion;
                // Set change versions to GlobalSystemVersion
                for (int iType = 0; iType < dstArchetype->TypesCount; ++iType)
                {
                    var dstArray = dstArchetype->Chunks.GetChangeVersionArrayForType(iType) + dstChunkCount;
                    for (int i = 0; i < srcChunkCount; ++i)
                    {
                        dstArray[i] = globalSystemVersion;
                    }
                }

                // Copy chunk count array
                var dstCountArray = dstArchetype->Chunks.GetChunkEntityCountArray() + dstChunkCount;
                UnsafeUtility.MemCpy(dstCountArray, srcArchetype->Chunks.GetChunkEntityCountArray(),
                    sizeof(int) * srcChunkCount);

                // Copy enabled bits
                var srcEnabledBits = srcArchetype->Chunks.GetComponentEnabledArrayForArchetype();
                var dstEnabledBits = dstArchetype->Chunks.GetComponentEnabledArrayForArchetypeAndChunkOffset(dstChunkCount);
                dstEnabledBits.Copy(0, ref srcEnabledBits, 0, srcEnabledBits.Length);

                // Adjust enabled bits hierarchical values
                for (int t = 0; t < dstArchetype->EnableableTypesCount; ++t)
                {
                    var dstIndexInArchetype = dstArchetype->EnableableTypeIndexInArchetype[t];
                    for (int chunkIndex = 0; chunkIndex < srcChunkCount; ++chunkIndex)
                    {
                        dstArchetype->Chunks.InitializeDisabledCountForChunk(chunkIndex);

                        var chunk = dstArchetype->Chunks[chunkIndex];
                        var dstEnabledBitsForChunk = dstArchetype->Chunks.GetComponentEnabledArrayForTypeInChunk(dstIndexInArchetype, chunkIndex);

                        var disabledCount = chunk->Count - dstEnabledBitsForChunk.CountBits(0, chunk->Count);
                        dstArchetype->Chunks.AdjustChunkDisabledCountForType(dstIndexInArchetype, chunkIndex, disabledCount);
                    }
                }

                // Fix up chunk pointers in ChunkHeaders
                if (dstArchetype->HasChunkComponents)
                {
                    var metaArchetype = dstArchetype->MetaChunkArchetype;
                    var indexInTypeArray = ChunkDataUtility.GetIndexInTypeArray(metaArchetype, chunkHeaderType);
                    var offset = metaArchetype->Offsets[indexInTypeArray];
                    var sizeOf = metaArchetype->SizeOfs[indexInTypeArray];

                    for (int i = 0; i < srcChunkCount; ++i)
                    {
                        // Set chunk header without bumping change versions since they are zeroed when processing meta chunk
                        // modifying them here would be a race condition
                        var chunk = dstArchetype->Chunks[i + dstChunkCount];
                        var metaChunkEntity = chunk->metaChunkEntity;
                        var metaEntityInChunk = dstEntityComponentStore->GetEntityInChunk(metaChunkEntity);
                        var chunkHeader = (ChunkHeader*)(metaEntityInChunk.Chunk->Buffer + (offset + sizeOf * metaEntityInChunk.IndexInChunk));
                        chunkHeader->ArchetypeChunk = new ArchetypeChunk(chunk, dstEntityComponentStore);
                    }
                }

                dstArchetype->EntityCount += srcArchetype->EntityCount;
                srcArchetype->Chunks.Dispose();
                srcArchetype->EntityCount = 0;
            }
        }

        [BurstCompile]
        struct MoveAllChunksJob : IJobBurstSchedulable
        {
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* srcEntityComponentStore;
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* dstEntityComponentStore;
            public NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping;

            public void Execute()
            {
                dstEntityComponentStore->AllocateEntitiesForRemapping(srcEntityComponentStore, ref entityRemapping);
                srcEntityComponentStore->FreeAllEntities(false);
            }
        }

        internal uint GetChunkVersionHash(Entity entity)
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;

            if (!ecs->Exists(entity))
                return 0;

            var chunk = ecs->GetChunk(entity);
            var typeCount = chunk->Archetype->TypesCount;

            uint hash = 0;
            for (int i = 0; i < typeCount; ++i)
            {
                hash += chunk->GetChangeVersion(i);
            }

            return hash;
        }

        internal void BeforeStructuralChange()
        {
            GetCheckedEntityDataAccess()->BeforeStructuralChange();
        }

        internal int GetComponentTypeIndex(Entity entity, int index)
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;

            ecs->AssertEntitiesExist(&entity, 1);
            var archetype = ecs->GetArchetype(entity);

            if ((uint)index >= archetype->TypesCount) return -1;

            return archetype->Types[index + 1].TypeIndex;
        }

        [NotBurstCompatible]
        internal EntityQuery CreateEntityQuery(ComponentType* requiredComponents, int count)
        {
            var access = GetCheckedEntityDataAccess();
            return access->EntityQueryManager->CreateEntityQuery(access, requiredComponents, count);
        }

        bool TestMatchingArchetypeAny(Archetype* archetype, ComponentType* anyTypes, int anyCount)
        {
            if (anyCount == 0) return true;

            var componentTypes = archetype->Types;
            var componentTypesCount = archetype->TypesCount;
            for (var i = 0; i < componentTypesCount; i++)
            {
                var componentTypeIndex = componentTypes[i].TypeIndex;
                for (var j = 0; j < anyCount; j++)
                {
                    var anyTypeIndex = anyTypes[j].TypeIndex;
                    if (componentTypeIndex == anyTypeIndex) return true;
                }
            }

            return false;
        }

        bool TestMatchingArchetypeNone(Archetype* archetype, ComponentType* noneTypes, int noneCount)
        {
            var componentTypes = archetype->Types;
            var componentTypesCount = archetype->TypesCount;
            for (var i = 0; i < componentTypesCount; i++)
            {
                var componentTypeIndex = componentTypes[i].TypeIndex;
                for (var j = 0; j < noneCount; j++)
                {
                    var noneTypeIndex = noneTypes[j].TypeIndex;
                    if (componentTypeIndex == noneTypeIndex) return false;
                }
            }

            return true;
        }

        bool TestMatchingArchetypeAll(Archetype* archetype, ComponentType* allTypes, int allCount)
        {
            var componentTypes = archetype->Types;
            var componentTypesCount = archetype->TypesCount;
            var foundCount = 0;
            var disabledTypeIndex = TypeManager.GetTypeIndex<Disabled>();
            var prefabTypeIndex = TypeManager.GetTypeIndex<Prefab>();
            var requestedDisabled = false;
            var requestedPrefab = false;
            for (var i = 0; i < componentTypesCount; i++)
            {
                var componentTypeIndex = componentTypes[i].TypeIndex;
                for (var j = 0; j < allCount; j++)
                {
                    var allTypeIndex = allTypes[j].TypeIndex;
                    if (allTypeIndex == disabledTypeIndex)
                        requestedDisabled = true;
                    if (allTypeIndex == prefabTypeIndex)
                        requestedPrefab = true;
                    if (componentTypeIndex == allTypeIndex) foundCount++;
                }
            }

            if (archetype->Disabled && (!requestedDisabled))
                return false;
            if (archetype->Prefab && (!requestedPrefab))
                return false;

            return foundCount == allCount;
        }

        /// <summary>
        /// Check if an entity query is still valid
        /// </summary>
        /// <param name="query"></param>
        /// <returns>Return true if the specified query handle is still valid (and can be disposed)</returns>
        public bool IsQueryValid(EntityQuery query)
        {
            return GetCheckedEntityDataAccess()->IsQueryValid(query);
        }

        internal bool HasComponentRaw(Entity entity, int typeIndex)
        {
            var access = GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            return ecs->HasComponent(entity, typeIndex);
        }
    }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
    public static unsafe partial class EntityManagerManagedComponentExtensions
    {
        /// <summary>
        /// Gets the value of a component for an entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <typeparam name="T">The type of component to retrieve.</typeparam>
        /// <returns>A struct of type T containing the component value.</returns>
        /// <exception cref="ArgumentException">Thrown if the component type has no fields.</exception>
        public static T GetComponentData<T>(this EntityManager manager, Entity entity) where T : class, IComponentData
        {
            var access = manager.GetCheckedEntityDataAccess();
            return access->GetComponentData<T>(entity, access->ManagedComponentStore);
        }

        /// <summary>
        /// Sets the value of a component of an entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="componentData">The data to set.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <exception cref="ArgumentException">Thrown if the component type has no fields.</exception>
        public static void SetComponentData<T>(this EntityManager manager, Entity entity, T componentData) where T : class, IComponentData
        {
            var type = ComponentType.ReadWrite<T>();
            manager.SetComponentObject(entity, type, componentData);
        }

        /// <summary>
        /// Gets the value of a chunk component.
        /// </summary>
        /// <remarks>
        /// A chunk component is common to all entities in a chunk. You can access a chunk <see cref="IComponentData"/>
        /// instance through either the chunk itself or through an entity stored in that chunk.
        /// </remarks>
        /// <param name="chunk">The chunk.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <returns>A struct of type T containing the component value.</returns>
        /// <exception cref="ArgumentException">Thrown if the ArchetypeChunk object is invalid.</exception>
        public static T GetChunkComponentData<T>(this EntityManager manager, ArchetypeChunk chunk) where T : class, IComponentData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (chunk.Invalid())
                throw new System.ArgumentException(
                    $"GetChunkComponentData<{typeof(T)}> can not be called with an invalid archetype chunk.");
#endif
            var metaChunkEntity = chunk.m_Chunk->metaChunkEntity;
            return manager.GetComponentData<T>(metaChunkEntity);
        }

        /// <summary>
        /// Gets the value of chunk component for the chunk containing the specified entity.
        /// </summary>
        /// <remarks>
        /// A chunk component is common to all entities in a chunk. You can access a chunk <see cref="IComponentData"/>
        /// instance through either the chunk itself or through an entity stored in that chunk.
        /// </remarks>
        /// <param name="entity">The entity.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <returns>A struct of type T containing the component value.</returns>
        public static T GetChunkComponentData<T>(this EntityManager manager, Entity entity) where T : class, IComponentData
        {
            var access = manager.GetCheckedEntityDataAccess();
            access->EntityComponentStore->AssertEntitiesExist(&entity, 1);
            var chunk = access->EntityComponentStore->GetChunk(entity);
            var metaChunkEntity = chunk->metaChunkEntity;
            return access->GetComponentData<T>(metaChunkEntity, access->ManagedComponentStore);
        }

        /// <summary>
        /// Sets the value of a chunk component.
        /// </summary>
        /// <remarks>
        /// A chunk component is common to all entities in a chunk. You can access a chunk <see cref="IComponentData"/>
        /// instance through either the chunk itself or through an entity stored in that chunk.
        /// </remarks>
        /// <param name="chunk">The chunk to modify.</param>
        /// <param name="componentValue">The component data to set.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <exception cref="ArgumentException">Thrown if the ArchetypeChunk object is invalid.</exception>
        public static void SetChunkComponentData<T>(this EntityManager manager, ArchetypeChunk chunk, T componentValue) where T : class, IComponentData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (chunk.Invalid())
                throw new System.ArgumentException(
                    $"SetChunkComponentData<{typeof(T)}> can not be called with an invalid archetype chunk.");
#endif
            var metaChunkEntity = chunk.m_Chunk->metaChunkEntity;
            manager.SetComponentData<T>(metaChunkEntity, componentValue);
        }

        /// <summary>
        /// Adds a managed component to an entity and set the value of that component.
        /// </summary>
        /// <remarks>
        /// Adding a component changes an entity's archetype and results in the entity being moved to a different
        /// chunk.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The entity.</param>
        /// <param name="componentData">The data to set.</param>
        /// <typeparam name="T">The type of component.</typeparam>
        public static void AddComponentData<T>(this EntityManager manager, Entity entity, T componentData) where T : class, IComponentData
        {
            var type = ComponentType.ReadWrite<T>();

            manager.AddComponent(entity, type);
            manager.SetComponentData(entity, componentData);
        }

        /// <summary>
        /// Adds a chunk component to the specified entity.
        /// </summary>
        /// <remarks>
        /// Adding a chunk component to an entity changes that entity's archetype and results in the entity being moved
        /// to a different chunk, either one that already has an archetype containing the chunk component or a new
        /// chunk.
        ///
        /// A chunk component is common to all entities in a chunk. You can access a chunk <see cref="IComponentData"/>
        /// instance through either the chunk itself or through an entity stored in that chunk. In either case, getting
        /// or setting the component reads or writes the same data.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <exception cref="ArgumentException">The <see cref="Entity"/> does not exist.</exception>
        /// <param name="entity">The entity.</param>
        /// <typeparam name="T">The type of component, which must implement IComponentData.</typeparam>
        public static void AddChunkComponentData<T>(this EntityManager manager, Entity entity) where T : class, IComponentData
        {
            manager.AddComponent(entity, ComponentType.ChunkComponent<T>());
        }

        /// <summary>
        /// Adds a managed chunk component to each of the chunks identified by an EntityQuery and set the component values.
        /// </summary>
        /// <remarks>
        /// This function finds all chunks whose archetype satisfies the EntityQuery and adds the specified
        /// component to them.
        ///
        /// A chunk component is common to all entities in a chunk. You can access a chunk <see cref="IComponentData"/>
        /// instance through either the chunk itself or through an entity stored in that chunk.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before adding the component and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <param name="entityQuery">The EntityQuery identifying the chunks to modify.</param>
        /// <param name="componentData">The data to set.</param>
        /// <typeparam name="T">The type of component, which must implement IComponentData.</typeparam>
        public static void AddChunkComponentData<T>(this EntityManager manager, EntityQuery entityQuery, T componentData) where T : class, IComponentData
        {
            var access = manager.GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;

            access->AssertQueryIsValid(entityQuery);

            bool validAdd = true;
            var chunks = entityQuery.CreateArchetypeChunkArray(Allocator.TempJob);

            if (chunks.Length > 0)
            {
                ecs->CheckCanAddChunkComponent(chunks, ComponentType.ChunkComponent<T>(), ref validAdd);

                if (validAdd)
                {
                    var changes = access->BeginStructuralChanges();

                    var type = ComponentType.ReadWrite<T>();
                    var chunkType = ComponentType.FromTypeIndex(TypeManager.MakeChunkComponentTypeIndex(type.TypeIndex));

                    StructuralChange.AddComponentChunks(ecs, (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, chunkType.TypeIndex);

                    access->EndStructuralChanges(ref changes);
                }

                manager.SetChunkComponent(chunks, componentData);
            }

            chunks.Dispose();

            if (!validAdd)
            {
                ecs->ThrowDuplicateChunkComponentError(ComponentType.ChunkComponent<T>());
            }
        }

        static void SetChunkComponent<T>(this EntityManager manager, NativeArray<ArchetypeChunk> chunks, T componentData) where T : class, IComponentData
        {
            var type = TypeManager.GetTypeIndex<T>();
            for (int i = 0; i < chunks.Length; i++)
            {
                var srcChunk = chunks[i].m_Chunk;
                manager.SetComponentData<T>(srcChunk->metaChunkEntity, componentData);
            }
        }
    }
#endif
}
