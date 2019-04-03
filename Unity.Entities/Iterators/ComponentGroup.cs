using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Entities
{
    /// <summary>
    /// Defines a query to find archetypes with specific components.
    /// </summary>
    /// <remarks>
    /// A query combines components in the All, Any, and None sets according to the
    /// following rules:
    ///
    /// * All - Includes archetypes that have every component in this set
    /// * Any - Includes archetypes that have at least one component in this set
    /// * None - Excludes archetypes that have any component in this set
    ///
    /// For example, given entities with the following components:
    ///
    /// * Player has components: Position, Rotation, Player
    /// * Enemy1 has components: Position, Rotation, Melee
    /// * Enemy2 has components: Position, Rotation, Ranger
    ///
    /// The query below would give you all of the archetypes that:
    /// have any of [Melee or Ranger], AND have none of [Player], AND have all of [Position and Rotation]
    /// <code>
    /// new EntityArchetypeQuery {
    ///     Any = new ComponentType[] {typeof(Melee), typeof(Ranger)},
    ///     None = new ComponentType[] {typeof(Player)},
    ///     All = new ComponentType[] {typeof(Position), typeof(Rotation)}
    /// }
    /// </code>
    ///
    /// In other words, the query selects the Enemy1 and Enemy2 entities, but not the Player entity.
    /// </remarks>
    public class EntityArchetypeQuery
    {
        /// <summary>
        /// The query includes archetypes that contain at least one (but possibly more) of the
        /// components in the Any list.
        /// </summary>
        public ComponentType[] Any = Array.Empty<ComponentType>();
        /// <summary>
        /// The query excludes archetypes that contain any of the
        /// components in the None list.
        /// </summary>
        public ComponentType[] None = Array.Empty<ComponentType>();
        /// <summary>
        /// The query includes archetypes that contain all of the
        /// components in the All list.
        /// </summary>
        public ComponentType[] All = Array.Empty<ComponentType>();
        /// <summary>
        /// Specialized query options.
        /// </summary>
        /// <remarks>
        /// You should not need to set these options for most queries.
        ///
        /// Options is a bit mask; use the bitwise OR operator to combine multiple options.
        /// </remarks>
        public EntityArchetypeQueryOptions Options = EntityArchetypeQueryOptions.Default;
    }

    /// <summary>
    /// The bit flags to use for the <see cref="EntityArchetypeQuery.Options"/> field.
    /// </summary>
    [Flags]
    public enum EntityArchetypeQueryOptions
    {
        /// <summary>
        /// No options specified.
        /// </summary>
        Default = 0,
        /// <summary>
        /// The query includes the special <see cref="Prefab"/> component.
        /// </summary>
        IncludePrefab = 1,
        /// <summary>
        /// The query includes the special <see cref="Disabled"/> component.
        /// </summary>
        IncludeDisabled = 2,
        /// <summary>
        /// The query should filter selected entities based on the
        /// <see cref="WriteGroupAttribute"/> settings of the components specified in the query.
        /// </summary>
        FilterWriteGroup = 4,
    }

    //@TODO: Rename to EntityView
    /// <summary>
    /// A ComponentGroup provides a query-based view of your component data.
    /// </summary>
    /// <remarks>
    /// A ComponentGroup defines a view of your data based on a query for the set of
    /// component types that an archetype must contain in order for its chunks and entities
    /// to be included in the view. You can also exclude archetypes that contain specific types
    /// of components. For simple queries, you can create a ComponentGroup based on an array of
    /// component types. The following example defines a ComponentGroup that finds all entities
    /// with both RotationQuaternion and RotationSpeed components.
    ///
    /// <code>
    /// ComponentGroup m_Group = GetComponentGroup(typeof(RotationQuaternion),
    ///                                            ComponentType.ReadOnly{RotationSpeed}());
    /// </code>
    ///
    /// The query uses `ComponentType.ReadOnly` instead of the simpler `typeof` expression
    /// to designate that the system does not write to RotationSpeed. Always specify read only
    /// when possible, since there are fewer constraints on read access to data, which can help
    /// the Job scheduler execute your Jobs more efficiently.
    ///
    /// For more complex queries, you can use an <see cref="EntityArchetypeQuery"/> instead of a
    /// simple list of component types.
    ///
    /// Use the <see cref="EntityManager.CreateComponentGroup(Unity.Entities.ComponentType[])"/> or
    /// <see cref="ComponentSystemBase.GetComponentGroup(Unity.Entities.ComponentType[])"/> functions
    /// to get a ComponentGroup instance.
    /// </remarks>
    public unsafe class ComponentGroup : IDisposable
    {
        readonly ComponentJobSafetyManager m_SafetyManager;
        readonly EntityGroupData*          m_GroupData;
        readonly EntityDataManager*        m_EntityDataManager;
        ComponentGroupFilter               m_Filter;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal string                    DisallowDisposing = null;
#endif

        // TODO: this is temporary, used to cache some state to avoid recomputing the TransformAccessArray. We need to improve this.
        internal IDisposable               m_CachedState;

        internal ComponentGroup(EntityGroupData* groupData, ComponentJobSafetyManager safetyManager, ArchetypeManager typeManager, EntityDataManager* entityDataManager)
        {
            m_GroupData = groupData;
            m_EntityDataManager = entityDataManager;
            m_Filter = default(ComponentGroupFilter);
            m_SafetyManager = safetyManager;
            ArchetypeManager = typeManager;
            EntityDataManager = entityDataManager;
        }

        internal EntityDataManager* EntityDataManager { get; }
        internal ComponentJobSafetyManager SafetyManager => m_SafetyManager;

        /// <summary>
        ///      Ignore this ComponentGroup if it has no entities in any of its archetypes.
        /// </summary>
        /// <returns>True if this ComponentGroup has no entities. False if it has 1 or more entities.</returns>
        public bool IsEmptyIgnoreFilter
        {
            get
            {
                for (var m = m_GroupData->MatchingArchetypes.Count - 1; m >= 0; --m)
                {
                    var match = m_GroupData->MatchingArchetypes.p[m];
                    if (match->Archetype->EntityCount > 0)
                        return false;
                }

                return true;
            }
        }
#if UNITY_CSHARP_TINY
        internal class SlowListSet<T>
        {
            internal List<T> items;

            internal SlowListSet() {
                items = new List<T>();
            }

            internal void Add(T item)
            {
                if (!items.Contains(item))
                    items.Add(item);
            }

            internal int Count => items.Count;

            internal T[] ToArray()
            {
                return items.ToArray();
            }
        }
#endif

        /// <summary>
        /// Gets the array of <see cref="ComponentType"/> objects included in this ComponentGroup.
        /// </summary>
        /// <returns>Array of ComponentTypes</returns>
        internal ComponentType[] GetQueryTypes()
        {
#if !UNITY_CSHARP_TINY
            var types = new HashSet<ComponentType>();
#else
            var types = new SlowListSet<ComponentType>();
#endif

            for (var i = 0; i < m_GroupData->ArchetypeQueryCount; ++i)
            {
                for (var j = 0; j < m_GroupData->ArchetypeQuery[i].AnyCount; ++j)
                {
                    types.Add(TypeManager.GetType(m_GroupData->ArchetypeQuery[i].Any[j]));
                }
                for (var j = 0; j < m_GroupData->ArchetypeQuery[i].AllCount; ++j)
                {
                    types.Add(TypeManager.GetType(m_GroupData->ArchetypeQuery[i].All[j]));
                }
                for (var j = 0; j < m_GroupData->ArchetypeQuery[i].NoneCount; ++j)
                {
                    types.Add(ComponentType.Exclude(TypeManager.GetType(m_GroupData->ArchetypeQuery[i].None[j])));
                }
            }

#if !UNITY_CSHARP_TINY
            var array = new ComponentType[types.Count];
            var t = 0;
            foreach (var type in types)
                array[t++] = type;
            return array;
#else
            return types.ToArray();
#endif
        }

        /// <summary>
        ///     Packed array of this ComponentGroup's ReadOnly and writable ComponentTypes.
        ///     ReadOnly ComponentTypes come before writable types in this array.
        /// </summary>
        /// <returns>Array of ComponentTypes</returns>
        internal ComponentType[] GetReadAndWriteTypes()
        {
            var types = new ComponentType[m_GroupData->ReaderTypesCount + m_GroupData->WriterTypesCount];
            var typeArrayIndex = 0;
            for (var i = 0; i < m_GroupData->ReaderTypesCount; ++i)
            {
                types[typeArrayIndex++] = ComponentType.ReadOnly(TypeManager.GetType(m_GroupData->ReaderTypes[i]));
            }
            for (var i = 0; i < m_GroupData->WriterTypesCount; ++i)
            {
                types[typeArrayIndex++] = TypeManager.GetType(m_GroupData->WriterTypes[i]);
            }

            return types;
        }

        internal ArchetypeManager ArchetypeManager { get; }

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (DisallowDisposing != null)
                throw new ArgumentException(DisallowDisposing);
#endif

            if (m_CachedState != null)
                m_CachedState.Dispose();

            ResetFilter();
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        /// <summary>
        ///     Gets safety handle to a ComponentType required by this ComponentGroup.
        /// </summary>
        /// <param name="indexInComponentGroup">Index of a ComponentType in this ComponentGroup's RequiredComponents list./param>
        /// <returns>AtomicSafetyHandle for a ComponentType</returns>
        internal AtomicSafetyHandle GetSafetyHandle(int indexInComponentGroup)
        {
            var type = m_GroupData->RequiredComponents + indexInComponentGroup;
            var isReadOnly = type->AccessModeType == ComponentType.AccessMode.ReadOnly;
            return m_SafetyManager.GetSafetyHandle(type->TypeIndex, isReadOnly);
        }

        /// <summary>
        ///     Gets buffer safety handle to a ComponentType required by this ComponentGroup.
        /// </summary>
        /// <param name="indexInComponentGroup">Index of a ComponentType in this ComponentGroup's RequiredComponents list./param>
        /// <returns>AtomicSafetyHandle for a buffer</returns>
        internal AtomicSafetyHandle GetBufferSafetyHandle(int indexInComponentGroup)
        {
            var type = m_GroupData->RequiredComponents + indexInComponentGroup;
            return m_SafetyManager.GetBufferSafetyHandle(type->TypeIndex);
        }
#endif

        bool GetIsReadOnly(int indexInComponentGroup)
        {
            var type = m_GroupData->RequiredComponents + indexInComponentGroup;
            var isReadOnly = type->AccessModeType == ComponentType.AccessMode.ReadOnly;
            return isReadOnly;
        }

        /// <summary>
        /// Calculates the number of entities selected by this ComponentGroup.
        /// </summary>
        /// <remarks>
        /// The ComponentGroup must run the query and apply any filters to calculate the entity count.
        /// </remarks>
        /// <returns>The number of entities based on the current ComponentGroup properties.</returns>
        public int CalculateLength()
        {
            SyncFilterTypes();
            return ComponentChunkIterator.CalculateLength(m_GroupData->MatchingArchetypes, ref m_Filter);
        }

        /// <summary>
        ///     Gets iterator to chunks associated with this ComponentGroup.
        /// </summary>
        /// <returns>ComponentChunkIterator for this ComponentGroup</returns>
        internal ComponentChunkIterator GetComponentChunkIterator()
        {
            return new ComponentChunkIterator(m_GroupData->MatchingArchetypes, m_EntityDataManager->GlobalSystemVersion, ref m_Filter);
        }

        /// <summary>
        ///     Index of a ComponentType in this ComponentGroup's RequiredComponents list.
        ///     For example, you have a ComponentGroup that requires these ComponentTypes: Position, Velocity, and Color.
        ///
        ///     These are their type indices (according to the TypeManager):
        ///         Position.TypeIndex == 3
        ///         Velocity.TypeIndex == 5
        ///            Color.TypeIndex == 17
        ///
        ///     RequiredComponents: [Position -> Velocity -> Color] (a linked list)
        ///     Given Velocity's TypeIndex (5), the return value would be 1, since Velocity is in slot 1 of RequiredComponents.
        /// </summary>
        /// <param name="componentType">Index of a ComponentType in the TypeManager</param>
        /// <returns>An index into RequiredComponents.</returns>
        internal int GetIndexInComponentGroup(int componentType)
        {
            // Go through all the required component types in this ComponentGroup until you find the matching component type index.
            var componentIndex = 0;
            while (componentIndex < m_GroupData->RequiredComponentsCount && m_GroupData->RequiredComponents[componentIndex].TypeIndex != componentType)
                ++componentIndex;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (componentIndex >= m_GroupData->RequiredComponentsCount)
                throw new InvalidOperationException( $"Trying to get iterator for {TypeManager.GetType(componentType)} but the required component type was not declared in the EntityGroup.");
#endif
            return componentIndex;
        }

        [Obsolete("GetComponentDataArray is deprecated. Use IJobProcessComponentData or ToComponentDataArray/CopyFromComponentDataArray instead.")]
        internal void GetComponentDataArray<T>(ref ComponentChunkIterator iterator, int indexInComponentGroup,
            int length, out ComponentDataArray<T> output) where T : struct, IComponentData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var componentType = ComponentType.FromTypeIndex(typeIndex);
            if (componentType.IsZeroSized)
                throw new ArgumentException($"GetComponentDataArray<{typeof(T)}> cannot be called on zero-sized IComponentData");
#endif

            iterator.IndexInComponentGroup = indexInComponentGroup;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            output = new ComponentDataArray<T>(iterator, length, GetSafetyHandle(indexInComponentGroup));
#else
			output = new ComponentDataArray<T>(iterator, length);
#endif
        }

        [Obsolete("GetComponentDataArray is deprecated. Use IJobProcessComponentData or ToComponentDataArray/CopyFromComponentDataArray instead.")]
        public ComponentDataArray<T> GetComponentDataArray<T>() where T : struct, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var componentType = ComponentType.FromTypeIndex(typeIndex);
            if (componentType.IsZeroSized)
                throw new ArgumentException($"GetComponentDataArray<{typeof(T)}> cannot be called on zero-sized IComponentData");
#endif

            int length = CalculateLength();
            ComponentChunkIterator iterator = GetComponentChunkIterator();
            var indexInComponentGroup = GetIndexInComponentGroup(typeIndex);

            ComponentDataArray<T> res;
            GetComponentDataArray(ref iterator, indexInComponentGroup, length, out res);
            return res;
        }

        [Obsolete("GetSharedComponentDataArray is deprecated.")]
        internal void GetSharedComponentDataArray<T>(ref ComponentChunkIterator iterator, int indexInComponentGroup,
            int length, out SharedComponentDataArray<T> output) where T : struct, ISharedComponentData
        {
            iterator.IndexInComponentGroup = indexInComponentGroup;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var componentTypeIndex = m_GroupData->RequiredComponents[indexInComponentGroup].TypeIndex;
            output = new SharedComponentDataArray<T>(ArchetypeManager.GetSharedComponentDataManager(),
                indexInComponentGroup, iterator, length, m_SafetyManager.GetSafetyHandle(componentTypeIndex, true));
#else
            output = new SharedComponentDataArray<T>(ArchetypeManager.GetSharedComponentDataManager(),
                                                     indexInComponentGroup, iterator, length);
#endif
        }

        /// <summary>
        ///     Creates an array containing ISharedComponentData of a given type T.
        /// </summary>
        /// <returns>NativeArray of ISharedComponentData in this ComponentGroup.</returns>
        [Obsolete("GetSharedComponentDataArray is deprecated.")]
        public SharedComponentDataArray<T> GetSharedComponentDataArray<T>() where T : struct, ISharedComponentData
        {
            int length = CalculateLength();
            ComponentChunkIterator iterator = GetComponentChunkIterator();
            var indexInComponentGroup = GetIndexInComponentGroup(TypeManager.GetTypeIndex<T>());

            SharedComponentDataArray<T> res;
            GetSharedComponentDataArray(ref iterator, indexInComponentGroup, length, out res);
            return res;
        }

        [Obsolete("GetBufferArray is deprecated.")]
        internal void GetBufferArray<T>(ref ComponentChunkIterator iterator, int indexInComponentGroup, int length,
            out BufferArray<T> output) where T : struct, IBufferElementData
        {
            iterator.IndexInComponentGroup = indexInComponentGroup;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            output = new BufferArray<T>(iterator, length, GetIsReadOnly(indexInComponentGroup),
                GetSafetyHandle(indexInComponentGroup),
                GetBufferSafetyHandle(indexInComponentGroup));
#else
			output = new BufferArray<T>(iterator, length, GetIsReadOnly(indexInComponentGroup));
#endif
        }

        [Obsolete("GetBufferArray is deprecated.")]
        public BufferArray<T> GetBufferArray<T>() where T : struct, IBufferElementData
        {
            int length = CalculateLength();
            ComponentChunkIterator iterator = GetComponentChunkIterator();
            var indexInComponentGroup = GetIndexInComponentGroup(TypeManager.GetTypeIndex<T>());

            BufferArray<T> res;
            GetBufferArray(ref iterator, indexInComponentGroup, length, out res);
            return res;
        }

        /// <summary>
        ///     Creates an array with all the chunks in this ComponentGroup.
        ///     Gives the caller a job handle so it can wait for GatherChunks to finish.
        /// </summary>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <param name="jobhandle">Handle to the GatherChunks job used to fill the output array.</param>
        /// <returns>NativeArray of all the chunks in this ComponentChunkIterator.</returns>
        public NativeArray<ArchetypeChunk> CreateArchetypeChunkArray(Allocator allocator, out JobHandle jobhandle)
        {
            JobHandle dependency = default(JobHandle);

            if (m_Filter.Type == FilterType.Changed)
            {
                var filterCount = m_Filter.Changed.Count;
                var readerTypes = stackalloc int[filterCount];
                fixed (int* indexInComponentGroupPtr = m_Filter.Changed.IndexInComponentGroup)
                    for (int i = 0; i < filterCount; ++i)
                        readerTypes[i] = m_GroupData->RequiredComponents[indexInComponentGroupPtr[i]].TypeIndex;

                dependency = m_SafetyManager.GetDependency(readerTypes, filterCount,null, 0);
            }

            return ComponentChunkIterator.CreateArchetypeChunkArray(m_GroupData->MatchingArchetypes, allocator, out jobhandle, ref m_Filter, dependency);
        }

        /// <summary>
        ///     Creates an array with all the chunks in this ComponentGroup.
        ///     Waits for the GatherChunks job to complete here.
        /// </summary>
        /// <param name="allocator">Allocator to use for the array.</param>
        /// <returns>NativeArray of all the chunks in this ComponentChunkIterator.</returns>
        public NativeArray<ArchetypeChunk> CreateArchetypeChunkArray(Allocator allocator)
        {
            SyncFilterTypes();
            JobHandle job;
            var res = ComponentChunkIterator.CreateArchetypeChunkArray(m_GroupData->MatchingArchetypes, allocator, out job, ref m_Filter);
            job.Complete();
            return res;
        }


        /// <summary>
        /// Creates a NativeArray containing the selected entities.
        /// </summary>
        /// <param name="allocator">The type of memory to allocate.</param>
        /// <param name="jobhandle">A handle that you can use as a dependency for a Job
        /// that uses the NativeArray.</param>
        /// <returns>An array containing all the entities selected by the ComponentGroup.</returns>
        public NativeArray<Entity> ToEntityArray(Allocator allocator, out JobHandle jobhandle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var entityType = new ArchetypeChunkEntityType(m_SafetyManager.GetEntityManagerSafetyHandle());
#else
            var entityType = new ArchetypeChunkEntityType();
#endif

            return ComponentChunkIterator.CreateEntityArray(m_GroupData->MatchingArchetypes, allocator, entityType,  this, ref m_Filter, out jobhandle, GetDependency());
        }

        /// <summary>
        /// Creates a NativeArray containing the selected entities.
        /// </summary>
        /// <remarks>This version of the function blocks until the Job used to fill the array is complete.</remarks>
        /// <param name="allocator">The type of memory to allocate.</param>
        /// <returns>An array containing all the entities selected by the ComponentGroup.</returns>
        public NativeArray<Entity> ToEntityArray(Allocator allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var entityType = new ArchetypeChunkEntityType(m_SafetyManager.GetEntityManagerSafetyHandle());
#else
            var entityType = new ArchetypeChunkEntityType();
#endif
            JobHandle job;
            var res = ComponentChunkIterator.CreateEntityArray(m_GroupData->MatchingArchetypes, allocator, entityType, this, ref m_Filter, out job, GetDependency());
            job.Complete();
            return res;
        }

        /// <summary>
        /// Creates a NativeArray containing the components of type T for the selected entities.
        /// </summary>
        /// <param name="allocator">The type of memory to allocate.</param>
        /// <param name="jobhandle">A handle that you can use as a dependency for a Job
        /// that uses the NativeArray.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <returns>An array containing the specified component for all the entities selected
        /// by the ComponentGroup.</returns>
        public NativeArray<T> ToComponentDataArray<T>(Allocator allocator, out JobHandle jobhandle)
            where T : struct,IComponentData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var componentType = new ArchetypeChunkComponentType<T>(m_SafetyManager.GetSafetyHandle(TypeManager.GetTypeIndex<T>(), true), true, EntityDataManager->GlobalSystemVersion);
#else
            var componentType = new ArchetypeChunkComponentType<T>(true, EntityDataManager->GlobalSystemVersion);
#endif
            return ComponentChunkIterator.CreateComponentDataArray(m_GroupData->MatchingArchetypes, allocator, componentType, this, ref m_Filter, out jobhandle, GetDependency());
        }

        /// <summary>
        /// Creates a NativeArray containing the components of type T for the selected entities.
        /// </summary>
        /// <param name="allocator">The type of memory to allocate.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <returns>An array containing the specified component for all the entities selected
        /// by the ComponentGroup.</returns>
        /// <exception cref="InvalidOperationException">Thrown if you ask for a component that is not part of
        /// the group.</exception>
        public NativeArray<T> ToComponentDataArray<T>(Allocator allocator)
            where T : struct, IComponentData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var componentType = new ArchetypeChunkComponentType<T>(m_SafetyManager.GetSafetyHandle(TypeManager.GetTypeIndex<T>(), true), true, EntityDataManager->GlobalSystemVersion);
#else
            var componentType = new ArchetypeChunkComponentType<T>(true, EntityDataManager->GlobalSystemVersion);
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            int typeIndex = TypeManager.GetTypeIndex<T>();
            int indexInComponentGroup = GetIndexInComponentGroup(typeIndex);
            if (indexInComponentGroup == -1)
                throw new InvalidOperationException( $"Trying ToComponentDataArray of {TypeManager.GetType(typeIndex)} but the required component type was not declared in the EntityGroup.");
#endif

            JobHandle job;
            var res = ComponentChunkIterator.CreateComponentDataArray(m_GroupData->MatchingArchetypes, allocator, componentType, this, ref m_Filter, out job, GetDependency());
            job.Complete();
            return res;
        }

        public void CopyFromComponentDataArray<T>(NativeArray<T> componentDataArray)
        where T : struct,IComponentData
        {
            // throw if non equal size
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var groupLength = CalculateLength();
            if(groupLength != componentDataArray.Length)
                throw new ArgumentException($"Length of input array ({componentDataArray.Length}) does not match length of ComponentGroup ({groupLength})");
            var componentType = new ArchetypeChunkComponentType<T>(m_SafetyManager.GetSafetyHandle(TypeManager.GetTypeIndex<T>(), false), false, EntityDataManager->GlobalSystemVersion);
#else
            var componentType = new ArchetypeChunkComponentType<T>(false, EntityDataManager->GlobalSystemVersion);
#endif

            ComponentChunkIterator.CopyFromComponentDataArray(m_GroupData->MatchingArchetypes, componentDataArray, componentType, this, ref m_Filter, out var job, GetDependency());
            job.Complete();
        }

        public void CopyFromComponentDataArray<T>(NativeArray<T> componentDataArray, out JobHandle jobhandle)
            where T : struct,IComponentData
        {
            // throw if non equal size
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var groupLength = CalculateLength();
            if(groupLength != componentDataArray.Length)
                throw new ArgumentException($"Length of input array ({componentDataArray.Length}) does not match length of ComponentGroup ({groupLength})");
#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var componentType = new ArchetypeChunkComponentType<T>(m_SafetyManager.GetSafetyHandle(TypeManager.GetTypeIndex<T>(), false), false, EntityDataManager->GlobalSystemVersion);
#else
            var componentType = new ArchetypeChunkComponentType<T>(false, EntityDataManager->GlobalSystemVersion);
#endif

            ComponentChunkIterator.CopyFromComponentDataArray(m_GroupData->MatchingArchetypes, componentDataArray, componentType, this, ref m_Filter, out jobhandle, GetDependency());
        }

        /// <summary>
        ///     Creates an EntityArray that gives you access to the entities in this ComponentGroup.
        /// </summary>
        /// <returns>EntityArray of all the entities in this ComponentGroup.</returns>
        [Obsolete("GetEntityArray is deprecated. Use IJobProcessComponentDataWithEntity or ToEntityArray instead.")]
        public EntityArray GetEntityArray()
        {
            int length = CalculateLength();
            ComponentChunkIterator iterator = GetComponentChunkIterator();

            EntityArray output;
            iterator.IndexInComponentGroup = 0;
#if ENABLE_UNITY_COLLECTIONS_CHECKS

            if (m_GroupData->RequiredComponentsCount == 0)
                throw new InvalidOperationException( $"GetEntityArray() is currently not supported from a ComponentGroup created with EntityArchetypeQuery.");

            output = new EntityArray(iterator, length, m_SafetyManager.GetEntityManagerSafetyHandle());
#else
			output = new EntityArray(iterator, length);
#endif
            return output;
        }

        public Entity GetSingletonEntity()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var groupLength = CalculateLength();
            if (groupLength != 1)
                throw new System.InvalidOperationException($"GetSingletonEntity() requires that exactly one exists but there are {groupLength}.");
#endif


            var iterator = GetComponentChunkIterator();
            iterator.MoveToChunkWithoutFiltering(0);

            Entity entity;
            var array = iterator.GetCurrentChunkComponentDataPtr(false, 0);
            UnsafeUtility.CopyPtrToStructure(array, out entity);
            return entity;
        }

        /// <summary>
        /// Gets the value of a singleton component.
        /// </summary>
        /// <remarks>A singleton component is a component of which only one instance exists in the world
        /// and which has been set with <see cref="SetSingleton{T}(T)"/>.</remarks>
        /// <typeparam name="T">The component type.</typeparam>
        /// <returns>A copy of the singleton component.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public T GetSingleton<T>()
            where T : struct, IComponentData
        {
            #if ENABLE_UNITY_COLLECTIONS_CHECKS
            if(GetIndexInComponentGroup(TypeManager.GetTypeIndex<T>()) != 1)
                throw new System.InvalidOperationException($"GetSingleton<{typeof(T)}>() requires that {typeof(T)} is the only component type in its archetype.");

            var groupLength = CalculateLength();
            if (groupLength != 1)
                throw new System.InvalidOperationException($"GetSingleton<{typeof(T)}>() requires that exactly one {typeof(T)} exists but there are {groupLength}.");
            #endif

            CompleteDependency();

            var iterator = GetComponentChunkIterator();
            iterator.MoveToChunkWithoutFiltering(0);

            var array = iterator.GetCurrentChunkComponentDataPtr(false, 1);
            UnsafeUtility.CopyPtrToStructure(array, out T value);
            return value;
        }

        /// <summary>
        /// Sets the value of a singleton component.
        /// </summary>
        /// <remarks>
        /// For a component to be a singleton, there can be only one instance of that component
        /// in a <see cref="World"/>. The component must be the only component in its archetype
        /// and you cannot use the same type of component as a normal component.
        ///
        /// To create a singleton, create an entity with the singleton component as its only component,
        /// and then use `SetSingleton()` to assign a value.
        ///
        /// For example, if you had a component defined as:
        /// <code>
        /// public struct Singlet: IComponentData{ public int Value; }
        /// </code>
        ///
        /// You could create a singleton as follows:
        ///
        /// <code>
        /// var entityManager = World.Active.EntityManager;
        /// var singletonEntity = entityManager.CreateEntity(typeof(Singlet));
        /// var singletonGroup = entityManager.CreateComponentGroup(typeof(Singlet));
        /// singletonGroup.SetSingleton&lt;Singlet&gt;(new Singlet {Value = 1});
        /// </code>
        ///
        /// You can set and get the singleton value from a ComponentGroup or a ComponentSystem.
        /// </remarks>
        /// <param name="value">An instance of type T containing the values to set.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <exception cref="InvalidOperationException">Thrown if more than one instance of this component type
        /// exists in the world or the component type appears in more than one archetype.</exception>
        public void SetSingleton<T>(T value)
            where T : struct, IComponentData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if(GetIndexInComponentGroup(TypeManager.GetTypeIndex<T>()) != 1)
                throw new System.InvalidOperationException($"GetSingleton<{typeof(T)}>() requires that {typeof(T)} is the only component type in its archetype.");

            var groupLength = CalculateLength();
            if (groupLength != 1)
                throw new System.InvalidOperationException($"SetSingleton<{typeof(T)}>() requires that exactly one {typeof(T)} exists but there are {groupLength}.");
#endif

            CompleteDependency();

            var iterator = GetComponentChunkIterator();
            iterator.MoveToChunkWithoutFiltering(0);

            var array = iterator.GetCurrentChunkComponentDataPtr(true, 1);
            UnsafeUtility.CopyStructureToPtr(ref value, array);
        }

        internal bool CompareComponents(ComponentType* componentTypes, int count)
        {
            return EntityGroupManager.CompareComponents(componentTypes, count, m_GroupData);
        }

        // @TODO: Define what CompareComponents() does
        /// <summary>
        ///
        /// </summary>
        /// <param name="componentTypes"></param>
        /// <returns></returns>
        public bool CompareComponents(ComponentType[] componentTypes)
        {
            fixed (ComponentType* componentTypesPtr = componentTypes)
            {
                return EntityGroupManager.CompareComponents(componentTypesPtr, componentTypes.Length, m_GroupData);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="componentTypes"></param>
        /// <returns></returns>
        public bool CompareComponents(NativeArray<ComponentType> componentTypes)
        {
            return EntityGroupManager.CompareComponents((ComponentType*)componentTypes.GetUnsafeReadOnlyPtr(), componentTypes.Length, m_GroupData);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public bool CompareQuery(EntityArchetypeQuery[] query)
        {
            return EntityGroupManager.CompareQuery(query, m_GroupData);
        }

        /// <summary>
        /// Resets this ComponentGroup's filter.
        /// </summary>
        /// <remarks>
        /// Removes references to shared component data, if applicable, then resets the filter type to None.
        /// </remarks>
        public void ResetFilter()
        {
            if (m_Filter.Type == FilterType.SharedComponent)
            {
                var filteredCount = m_Filter.Shared.Count;

                var sm = ArchetypeManager.GetSharedComponentDataManager();
                fixed (int* sharedComponentIndexPtr = m_Filter.Shared.SharedComponentIndex)
                {
                    for (var i = 0; i < filteredCount; ++i)
                        sm.RemoveReference(sharedComponentIndexPtr[i]);
                }
            }

            m_Filter.Type = FilterType.None;
        }

        /// <summary>
        ///     Sets this ComponentGroup's filter while preserving its version number.
        /// </summary>
        /// <param name="filter">ComponentGroupFilter to use all data but RequiredChangeVersion from.</param>
        void SetFilter(ref ComponentGroupFilter filter)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            filter.AssertValid();
#endif
            var version = m_Filter.RequiredChangeVersion;
            ResetFilter();
            m_Filter = filter;
            m_Filter.RequiredChangeVersion = version;
        }

        /// <summary>
        /// Filters this ComponentGroup so that it only selects entities with shared component values
        /// matching the values specified by the `sharedComponent1` parameter.
        /// </summary>
        /// <param name="sharedComponent1">The shared component values on which to filter.</param>
        /// <typeparam name="SharedComponent1">The type of shared component. (The type must also be
        /// one of the types used to create the ComponentGroup.</typeparam>
        public void SetFilter<SharedComponent1>(SharedComponent1 sharedComponent1)
            where SharedComponent1 : struct, ISharedComponentData
        {
            var sm = ArchetypeManager.GetSharedComponentDataManager();

            var filter = new ComponentGroupFilter();
            filter.Type = FilterType.SharedComponent;
            filter.Shared.Count = 1;
            filter.Shared.IndexInComponentGroup[0] = GetIndexInComponentGroup(TypeManager.GetTypeIndex<SharedComponent1>());
            filter.Shared.SharedComponentIndex[0] = sm.InsertSharedComponent(sharedComponent1);

            SetFilter(ref filter);
        }

        /// <summary>
        /// Filters this ComponentGroup based on the values of two separate shared components.
        /// </summary>
        /// <remarks>
        /// The filter only selects entities for which both shared component values
        /// specified by the `sharedComponent1` and `sharedComponent2` parameters match.
        /// </remarks>
        /// <param name="sharedComponent1">Shared component values on which to filter.</param>
        /// <param name="sharedComponent2">Shared component values on which to filter.</param>
        /// <typeparam name="SharedComponent1">The type of shared component. (The type must also be
        /// one of the types used to create the ComponentGroup.</typeparam>
        /// <typeparam name="SharedComponent2">The type of shared component. (The type must also be
        /// one of the types used to create the ComponentGroup.</typeparam>
        public void SetFilter<SharedComponent1, SharedComponent2>(SharedComponent1 sharedComponent1,
            SharedComponent2 sharedComponent2)
            where SharedComponent1 : struct, ISharedComponentData
            where SharedComponent2 : struct, ISharedComponentData
        {
            var sm = ArchetypeManager.GetSharedComponentDataManager();

            var filter = new ComponentGroupFilter();
            filter.Type = FilterType.SharedComponent;
            filter.Shared.Count = 2;
            filter.Shared.IndexInComponentGroup[0] = GetIndexInComponentGroup(TypeManager.GetTypeIndex<SharedComponent1>());
            filter.Shared.SharedComponentIndex[0] = sm .InsertSharedComponent(sharedComponent1);

            filter.Shared.IndexInComponentGroup[1] = GetIndexInComponentGroup(TypeManager.GetTypeIndex<SharedComponent2>());
            filter.Shared.SharedComponentIndex[1] = sm.InsertSharedComponent(sharedComponent2);

            SetFilter(ref filter);
        }

        /// <summary>
        /// Filters out entities in chunks for which the specified component has not changed.
        /// </summary>
        /// <remarks>
        ///     Saves a given ComponentType's index in RequiredComponents in this group's Changed filter.
        /// </remarks>
        /// <param name="componentType">ComponentType to mark as changed on this ComponentGroup's filter.</param>
        public void SetFilterChanged(ComponentType componentType)
        {
            var filter = new ComponentGroupFilter();
            filter.Type = FilterType.Changed;
            filter.Changed.Count = 1;
            filter.Changed.IndexInComponentGroup[0] = GetIndexInComponentGroup(componentType.TypeIndex);

            SetFilter(ref filter);
        }

        internal void SetFilterChangedRequiredVersion(uint requiredVersion)
        {
            m_Filter.RequiredChangeVersion = requiredVersion;
        }

        /// <summary>
        /// Filters out entities in chunks for which the specified components have not changed.
        /// </summary>
        /// <remarks>
        ///     Saves given ComponentTypes' indices in RequiredComponents in this group's Changed filter.
        /// </remarks>
        /// <param name="componentType">Array of up to two ComponentTypes to mark as changed on this ComponentGroup's filter.</param>
        public void SetFilterChanged(ComponentType[] componentType)
        {
            if (componentType.Length > ComponentGroupFilter.ChangedFilter.Capacity)
                throw new ArgumentException(
                    $"ComponentGroup.SetFilterChanged accepts a maximum of {ComponentGroupFilter.ChangedFilter.Capacity} component array length");
            if (componentType.Length <= 0)
                throw new ArgumentException(
                    $"ComponentGroup.SetFilterChanged component array length must be larger than 0");

            var filter = new ComponentGroupFilter();
            filter.Type = FilterType.Changed;
            filter.Changed.Count = componentType.Length;
            for (var i = 0; i != componentType.Length; i++)
                filter.Changed.IndexInComponentGroup[i] = GetIndexInComponentGroup(componentType[i].TypeIndex);

            SetFilter(ref filter);
        }

        /// <summary>
        ///     Ensures all jobs running on this ComponentGroup complete.
        /// </summary>
        public void CompleteDependency()
        {
            m_SafetyManager.CompleteDependenciesNoChecks(m_GroupData->ReaderTypes, m_GroupData->ReaderTypesCount,
                m_GroupData->WriterTypes, m_GroupData->WriterTypesCount);
        }

        /// <summary>
        ///     Combines all dependencies in this ComponentGroup into a single JobHandle.
        /// </summary>
        /// <returns>JobHandle that represents the combined dependencies of this ComponentGroup</returns>
        public JobHandle GetDependency()
        {
            return m_SafetyManager.GetDependency(m_GroupData->ReaderTypes, m_GroupData->ReaderTypesCount,
                m_GroupData->WriterTypes, m_GroupData->WriterTypesCount);
        }

        /// <summary>
        ///     Adds another job handle to this ComponentGroup's dependencies.
        /// </summary>
        public void AddDependency(JobHandle job)
        {
            m_SafetyManager.AddDependency(m_GroupData->ReaderTypes, m_GroupData->ReaderTypesCount,
                m_GroupData->WriterTypes, m_GroupData->WriterTypesCount, job);
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        public int GetCombinedComponentOrderVersion()
        {
            var version = 0;

            for (var i = 0; i < m_GroupData->RequiredComponentsCount; ++i)
                version += m_EntityDataManager->GetComponentTypeOrderVersion(m_GroupData->RequiredComponents[i].TypeIndex);

            return version;
        }

        /// <summary>
        ///     Total number of chunks in this ComponentGroup's MatchingArchetypes list.
        /// </summary>
        /// <param name="firstMatchingArchetype">First node of MatchingArchetypes linked list.</param>
        /// <returns>Number of chunks in this ComponentGroup.</returns>
        internal int CalculateNumberOfChunksWithoutFiltering()
        {
            return ComponentChunkIterator.CalculateNumberOfChunksWithoutFiltering(m_GroupData->MatchingArchetypes);
        }

        internal bool AddReaderWritersToLists(ref UnsafeList reading, ref UnsafeList writing)
        {
            bool anyAdded = false;
            for (int i = 0; i < m_GroupData->ReaderTypesCount; ++i)
                anyAdded |= CalculateReaderWriterDependency.AddReaderTypeIndex(m_GroupData->ReaderTypes[i], ref reading, ref writing);
 
            for (int i = 0; i < m_GroupData->WriterTypesCount; ++i)
                anyAdded |=CalculateReaderWriterDependency.AddWriterTypeIndex(m_GroupData->WriterTypes[i], ref reading, ref writing);
            return anyAdded;
        }

        /// <summary>
        /// Syncs the needed types for the filter.
        /// For every type that is change filtered we need to CompleteWriteDependency to avoid race conditions on the
        /// change version of those types
        /// </summary>
        internal void SyncFilterTypes()
        {
            if (m_Filter.Type == FilterType.Changed)
            {
                fixed (int* indexInComponentGroupPtr = m_Filter.Changed.IndexInComponentGroup)
                    for (int i = 0; i < m_Filter.Changed.Count; ++i)
                    {
                        var type = m_GroupData->RequiredComponents[indexInComponentGroupPtr[i]];
                        SafetyManager.CompleteWriteDependency(type.TypeIndex);
                    }
            }
        }
    }
}
