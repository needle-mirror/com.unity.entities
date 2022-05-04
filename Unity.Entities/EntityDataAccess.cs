using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Entities
{
    class ManagedEntityDataAccess
    {
        volatile static ManagedEntityDataAccess[] s_Instances = { null, null, null, null };

        // AllocHandle and FreeHandle must be called from the main thread
        public static int AllocHandle(ManagedEntityDataAccess instance)
        {
            var count = s_Instances.Length;
            for (int i = 0; i < count; ++i)
            {
                if (s_Instances[i] == null)
                {
                    s_Instances[i] = instance;
                    return i;
                }
            }

            var newInstances = new ManagedEntityDataAccess[count * 2];
            for (int i = 0; i < count; ++i)
                newInstances[i] = s_Instances[i];
            newInstances[count] = instance;

            s_Instances = newInstances;
            return count;
        }

        public static void FreeHandle(int i)
        {
            s_Instances[i] = null;
        }

        // This is thread safe even if s_Instances is being resized concurrently since both the old and new versions
        // will have the same value at s_Instances[handle] and the reference is updated atomically
        public static ManagedEntityDataAccess GetInstance(int handle)
        {
            return s_Instances[handle];
        }

        public World m_World;
        public ManagedComponentStore m_ManagedComponentStore;
    }

    [StructLayout(LayoutKind.Sequential)]
    [BurstCompatible]
    unsafe struct EntityDataAccess : IDisposable
    {
        private delegate void PlaybackManagedDelegate(IntPtr self);

        // approximate count of entities past which it is faster
        // to form batches when iterating through them all
        internal static readonly int FASTER_TO_BATCH_THRESHOLD = 10;
        internal static readonly int MANAGED_REFERENCES_DEFAULT = 8;

        private static readonly SharedStatic<IntPtr> s_ManagedPlaybackTrampoline =
            SharedStatic<IntPtr>.GetOrCreate<PlaybackManagedDelegate>();

        private static object s_DelegateGCPrevention;

        [NotBurstCompatible]
        internal ManagedEntityDataAccess ManagedEntityDataAccess =>
            ManagedEntityDataAccess.GetInstance(m_ManagedAccessHandle);

        [NotBurstCompatible]
        internal ManagedComponentStore ManagedComponentStore => ManagedEntityDataAccess.m_ManagedComponentStore;

        // These pointer attributes freak debuggers out because they take
        // copies of the root struct when inspecting, and it generally
        // misbehaves from there.

#if !NET_DOTS
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
#endif
        internal EntityComponentStore* EntityComponentStore
        {
            // This is always safe as the EntityDataAccess is always unsafe heap allocated.
            get
            {
                fixed (EntityComponentStore* ptr = &m_EntityComponentStore)
                {
                    return ptr;
                }
            }
        }

#if !NET_DOTS
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
#endif
        internal EntityQueryManager* EntityQueryManager
        {
            // This is always safe as the EntityDataAccess is always unsafe heap allocated.
            get
            {
                fixed (EntityQueryManager* ptr = &m_EntityQueryManager)
                {
                    return ptr;
                }
            }
        }

#if !NET_DOTS
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
#endif
        internal ComponentDependencyManager* DependencyManager
        {
            // This is always safe as the EntityDataAccess is always unsafe heap allocated.
            get
            {
                fixed (ComponentDependencyManager* ptr = &m_DependencyManager)
                {
                    return ptr;
                }
            }
        }

        [NativeDisableUnsafePtrRestriction] private EntityComponentStore m_EntityComponentStore;
        [NativeDisableUnsafePtrRestriction] private EntityQueryManager m_EntityQueryManager;
        [NativeDisableUnsafePtrRestriction] private ComponentDependencyManager m_DependencyManager;
        [NativeDisableUnsafePtrRestriction] private UnsafeList<int> m_ManagedReferenceIndexList;
        [NativeDisableUnsafePtrRestriction] public EntityQuery m_UniversalQuery; // matches all components
        [NativeDisableUnsafePtrRestriction] public EntityQuery m_UniversalQueryWithChunks;
        [NativeDisableUnsafePtrRestriction] public EntityQuery m_EntityGuidQuery;
        [NativeDisableUnsafePtrRestriction] public WorldUnmanaged m_WorldUnmanaged;

#if UNITY_EDITOR
        public int m_CachedEntityGUIDToEntityIndexVersion;
        UntypedUnsafeParallelHashMap m_CachedEntityGUIDToEntityIndex;

        [BurstCompatible(CompileTarget = BurstCompatibleAttribute.BurstCompatibleCompileTarget.Editor)]
        public ref UnsafeParallelMultiHashMap<int, Entity> CachedEntityGUIDToEntityIndex
        {
            get
            {
                fixed (void* ptr = &m_CachedEntityGUIDToEntityIndex)
                {
                    return ref UnsafeUtility.AsRef<UnsafeParallelMultiHashMap<int, Entity>>(ptr);
                }
            }
        }

        internal const int kBuiltinEntityQueryCount = 4;
#else
        internal const int kBuiltinEntityQueryCount = 3;
#endif

        private int m_ManagedAccessHandle;

        EntityArchetype m_EntityOnlyArchetype;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public int m_InsideForEach;
#endif

        private UntypedUnsafeParallelHashMap m_AliveEntityQueries;

        internal ref UnsafeParallelHashMap<ulong, byte> AliveEntityQueries
        {
            get
            {
                fixed (void* ptr = &m_AliveEntityQueries)
                {
                    return ref UnsafeUtility.AsRef<UnsafeParallelHashMap<ulong, byte>>(ptr);
                }
            }
        }

        internal bool IsInExclusiveTransaction => m_DependencyManager.IsInTransaction;

        [BurstCompile]
        internal struct DestroyChunks : IJobBurstSchedulable
        {
            [NativeDisableUnsafePtrRestriction] public EntityComponentStore* EntityComponentStore;
            public NativeArray<ArchetypeChunk> Chunks;

            public void Execute()
            {
                EntityComponentStore->DestroyEntities(Chunks);
            }
        }

        [NotBurstCompatible]
        public static void Initialize(EntityDataAccess* self, World world)
        {
            var managedGuts = new ManagedEntityDataAccess();

            self->m_ManagedReferenceIndexList = new UnsafeList<int>(MANAGED_REFERENCES_DEFAULT, Allocator.Persistent);

            self->m_EntityOnlyArchetype = default;
            self->m_ManagedAccessHandle = ManagedEntityDataAccess.AllocHandle(managedGuts);

            self->AliveEntityQueries = new UnsafeParallelHashMap<ulong, byte>(32, Allocator.Persistent);
#if UNITY_EDITOR
            self->CachedEntityGUIDToEntityIndex = new UnsafeParallelMultiHashMap<int, Entity>(32, Allocator.Persistent);
#endif
            managedGuts.m_World = world;

            self->m_DependencyManager.OnCreate(world.Unmanaged);
            Entities.EntityComponentStore.Create(&self->m_EntityComponentStore, world.SequenceNumber << 32);
            Unity.Entities.EntityQueryManager.Create(&self->m_EntityQueryManager, &self->m_DependencyManager);

            self->m_WorldUnmanaged = world.Unmanaged;
            managedGuts.m_ManagedComponentStore = new ManagedComponentStore();

            var builder = new EntityQueryDescBuilder(Allocator.Temp);
            builder.Options(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabled);
            builder.FinalizeQuery();
            self->m_UniversalQuery = self->m_EntityQueryManager.CreateEntityQuery(self, builder);

            builder.Reset();
            builder.Options(EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab);
            builder.AddAll(ComponentType.ReadWrite<ChunkHeader>());
            builder.FinalizeQuery();
            builder.Options(EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab);
            builder.FinalizeQuery();

            self->m_UniversalQueryWithChunks = self->m_EntityQueryManager.CreateEntityQuery(self, builder);


#if UNITY_EDITOR
            builder.Reset();
            builder.Options(EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab);
            builder.AddAll(ComponentType.ReadWrite<EntityGuid>());
            builder.FinalizeQuery();

            self->m_EntityGuidQuery = self->m_EntityQueryManager.CreateEntityQuery(self, builder);
#endif

            builder.Dispose();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            self->m_UniversalQuery._GetImpl()->_DisallowDisposing = true;
            self->m_UniversalQueryWithChunks._GetImpl()->_DisallowDisposing = true;
#if UNITY_EDITOR
            self->m_EntityGuidQuery._GetImpl()->_DisallowDisposing = true;
#endif
#endif

            if (s_DelegateGCPrevention == null)
            {
                var trampoline = new PlaybackManagedDelegate(PlaybackManagedDelegateInMonoWithWrappedExceptions);
                s_DelegateGCPrevention = trampoline; // Need to hold on to this
                s_ManagedPlaybackTrampoline.Data = Marshal.GetFunctionPointerForDelegate(trampoline);
            }
        }

        [NotBurstCompatible]
        [MonoPInvokeCallback(typeof(PlaybackManagedDelegate))]
        private static void PlaybackManagedDelegateInMonoWithWrappedExceptions(IntPtr target)
        {
            try
            {
                ((EntityDataAccess*) target.ToPointer())->PlaybackManagedChanges();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        [NotBurstCompatible]
        public void Dispose()
        {
            ManagedEntityDataAccess managedGuts = ManagedEntityDataAccess;

            m_ManagedReferenceIndexList.Dispose();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_UniversalQuery._GetImpl()->_DisallowDisposing = false;
            m_UniversalQueryWithChunks._GetImpl()->_DisallowDisposing = false;
#if UNITY_EDITOR
            m_EntityGuidQuery._GetImpl()->_DisallowDisposing = false;
#endif
#endif
            m_UniversalQuery.Dispose();
            m_UniversalQueryWithChunks.Dispose();
            m_UniversalQuery = default;
            m_UniversalQueryWithChunks = default;
#if UNITY_EDITOR
            m_EntityGuidQuery.Dispose();
            m_EntityGuidQuery = default;
#endif

            m_DependencyManager.Dispose();
            Entities.EntityComponentStore.Destroy(EntityComponentStore);
            Entities.EntityQueryManager.Destroy(EntityQueryManager);

            managedGuts.m_ManagedComponentStore.Dispose();
            managedGuts.m_World = null;

            ManagedEntityDataAccess.FreeHandle(m_ManagedAccessHandle);
            m_ManagedAccessHandle = -1;

            AliveEntityQueries.Dispose();
            AliveEntityQueries = default;
#if UNITY_EDITOR
            CachedEntityGUIDToEntityIndex.Dispose();
            CachedEntityGUIDToEntityIndex = default;
#endif
        }

        public bool Exists(Entity entity)
        {
            return EntityComponentStore->Exists(entity);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private void CheckIsStructuralChange()
        {
            if (DependencyManager->IsInForEachDisallowStructuralChange != 0)
            {
                throw new InvalidOperationException(
                    "Structural changes are not allowed during Entities.ForEach. Please use EntityCommandBuffer instead.");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void AssertMainThread()
        {
            if (IsInExclusiveTransaction)
                throw new InvalidOperationException("Must be called from the main thread");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        internal void AssertQueryIsValid(EntityQuery query)
        {
            if (!IsQueryValid(query))
                throw new ArgumentException("query is invalid or disposed");
        }

        public bool IsQueryValid(EntityQuery query)
        {
            if (!AliveEntityQueries.ContainsKey((ulong) (IntPtr) query.__impl))
                return false;

            // Also check that the sequence number matches in case the same pointer was given back by malloc.
            return query.__seqno == query.__impl->_SeqNo;
        }


        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckMoreThan1024Components(int count)
        {
            if (count + 1 > 1024)
                throw new ArgumentException($"Archetypes can't hold more than 1024 components");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void AssertCountsMatch(int count, int outputCount)
        {
            if (count != outputCount)
                throw new System.ArgumentException(
                    $"srcEntities.Length ({count}) and outputEntities.Length (({outputCount})) must be the same.");
        }

        public void BeforeStructuralChange()
        {
            // This is not an end user error. If there are any managed changes at this point, it indicates there is some
            // (previous) EntityManager change that is not properly playing back the managed changes that were buffered
            // afterward. That needs to be found and fixed.
            CheckIsStructuralChange();
            EntityComponentStore->AssertNoQueuedManagedDeferredCommands();

            if (!m_DependencyManager.IsInTransaction)
                m_DependencyManager.CompleteAllJobsAndInvalidateArrays();
        }

        public EntityComponentStore.ArchetypeChanges BeginStructuralChanges()
        {
            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();
            m_ManagedReferenceIndexList.Clear();
            return EntityComponentStore->BeginArchetypeChangeTracking();
        }

        public void EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes)
        {
            if (EntityComponentStore->ManagedChangesTracker.CommandBuffer.Length > 0)
                PlaybackManagedChanges();

            if (!m_ManagedReferenceIndexList.IsEmpty)
                ResetManagedReferences();

            if (EntityComponentStore->m_Archetypes.Length - changes.StartIndex != 0)
                EntityComponentStore->EndArchetypeChangeTracking(changes, EntityQueryManager);

            if (EntityComponentStore->m_ChunkListChangesTracker.ArchetypeTrackingHead != null)
                EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
        }

        public void ResetManagedReferences()
        {
            fixed (EntityDataAccess* mgr = &this)
            {
                ECBInterop.RemoveManagedReferences(mgr, m_ManagedReferenceIndexList.Ptr,
                    m_ManagedReferenceIndexList.Length);
            }

            m_ManagedReferenceIndexList.Clear();
        }

        [BurstDiscard]
        private void RunDestroyChunksMono(NativeArray<ArchetypeChunk> chunks, ref bool didTheThing)
        {
            new DestroyChunks {EntityComponentStore = EntityComponentStore, Chunks = chunks}.Run();
            didTheThing = true;
        }

        private void RunDestroyChunks(NativeArray<ArchetypeChunk> chunks)
        {
            bool didTheThing = false;
            RunDestroyChunksMono(chunks, ref didTheThing);
            if (didTheThing)
                return;

            EntityComponentStore->DestroyEntities(chunks);
        }


        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="entities">The NativeArray of entities to destroy</param>
        public void DestroyEntityDuringStructuralChange(NativeArray<Entity> entities,
            in SystemHandleUntyped originSystem = default)
        {
            if (entities.Length == 0)
                return;

            EntityComponentStore->AssertValidEntities((Entity*) entities.GetUnsafePtr(), entities.Length);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordDestroyEntity(in m_WorldUnmanaged, in originSystem, (Entity*) entities.GetUnsafeReadOnlyPtr(), entities.Length);
#endif

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginDestroyEntity(m_WorldUnmanaged))
#endif
            {
                StructuralChange.DestroyEntity(EntityComponentStore, (Entity*) entities.GetUnsafePtr(),
                    entities.Length);
            }
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="archetypeList"></param>
        /// <param name="filter"></param>
        public void DestroyEntityDuringStructuralChange(UnsafeCachedChunkList cache,
            UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter,
            in SystemHandleUntyped originSystem = default)
        {
            if (archetypeList.Length == 0)
                return;

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginDestroyEntity(m_WorldUnmanaged))
#endif
            using (var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(cache, archetypeList, Allocator.TempJob,
                ref filter, DependencyManager))
            {
                var errorEntity = Entity.Null;
                var errorReferencedEntity = Entity.Null;
                if (chunks.Length > 0)
                {
                    EntityComponentStore->AssertWillDestroyAllInLinkedEntityGroup(chunks,
                        GetBufferTypeHandle<LinkedEntityGroup>(false), ref errorEntity, ref errorReferencedEntity);

                    // #todo @macton DestroyEntities should support IJobChunk. But internal writes need to be handled.
                    if (errorEntity == Entity.Null)
                    {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
                        EntitiesJournaling.RecordDestroyEntity(in m_WorldUnmanaged, in originSystem, (ArchetypeChunk*) chunks.GetUnsafeReadOnlyPtr(), chunks.Length);
#endif
                        RunDestroyChunks(chunks);
                    }
                    else
                    {
                        EntityComponentStore->ThrowDestroyEntityError(errorEntity, errorReferencedEntity);
                    }
                }
            }
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="entities"></param>
        /// <param name="count"></param>
        internal void DestroyEntityInternalDuringStructuralChange(Entity* entities, int count,
            in SystemHandleUntyped originSystem = default)
        {
            if (count == 0)
                return;
            EntityComponentStore->AssertValidEntities(entities, count);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordDestroyEntity(in m_WorldUnmanaged, in originSystem, entities, count);
#endif

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginDestroyEntity(m_WorldUnmanaged))
#endif
            {
                StructuralChange.DestroyEntity(EntityComponentStore, entities, count);
            }
        }

        internal EntityArchetype CreateArchetype(ComponentType* types, int count)
        {
            Assert.IsTrue(types != null || count == 0);
            EntityComponentStore->AssertCanCreateArchetype(types, count);

            ComponentTypeInArchetype* typesInArchetype = stackalloc ComponentTypeInArchetype[count + 1];

            var cachedComponentCount = FillSortedArchetypeArray(typesInArchetype, types, count);

            // Lookup existing archetype (cheap)
            EntityArchetype entityArchetype;
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            entityArchetype._DebugComponentStore = EntityComponentStore;
#endif

            entityArchetype.Archetype =
                EntityComponentStore->GetExistingArchetype(typesInArchetype, cachedComponentCount);
            if (entityArchetype.Archetype != null)
                return entityArchetype;

            // Creating an archetype invalidates all iterators / jobs etc
            // because it affects the live iteration linked lists...

            entityArchetype.Archetype =
                EntityComponentStore->GetOrCreateArchetype(typesInArchetype, cachedComponentCount);

            return entityArchetype;
        }

        internal EntityArchetype CreateArchetypeDuringStructuralChange(ComponentTypeInArchetype* typesInArchetype,
            int cachedComponentCount)
        {
            // Lookup existing archetype (cheap)
            EntityArchetype entityArchetype;
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            entityArchetype._DebugComponentStore = EntityComponentStore;
#endif

            entityArchetype.Archetype =
                EntityComponentStore->GetExistingArchetype(typesInArchetype, cachedComponentCount);
            if (entityArchetype.Archetype != null)
                return entityArchetype;

            // Creating an archetype invalidates all iterators / jobs etc
            // because it affects the live iteration linked lists...

            entityArchetype.Archetype =
                EntityComponentStore->GetOrCreateArchetype(typesInArchetype, cachedComponentCount);

            return entityArchetype;
        }

        internal static int FillSortedArchetypeArray(ComponentTypeInArchetype* dst, ComponentType* requiredComponents,
            int count)
        {
            CheckMoreThan1024Components(count);
            dst[0] = new ComponentTypeInArchetype(ComponentType.ReadWrite<Entity>());
            for (var i = 0; i < count; ++i)
                SortingUtilities.InsertSorted(dst, i + 1, requiredComponents[i]);
            return count + 1;
        }

        /// <summary>
        /// Creates an entity with no components.
        /// </summary>
        /// <remarks>
        /// Creates the entity in the first available chunk with the archetype having no components.
        ///
        /// **Important:** This function creates a sync point, which means that the EntityManager waits for all
        /// currently running Jobs to complete before creating the entity and no additional Jobs can start before
        /// the function is finished. A sync point can cause a drop in performance because the ECS framework may not
        /// be able to make use of the processing power of all available cores.
        /// </remarks>
        /// <returns>The Entity object that you can use to access the entity.</returns>
        public Entity CreateEntity(in SystemHandleUntyped originSystem = default)
        {
            Entity entity;
#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginCreateEntity(m_WorldUnmanaged))
#endif
            {
                StructuralChange.CreateEntity(EntityComponentStore, GetEntityOnlyArchetype().Archetype, &entity, 1);
            }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordCreateEntity(in m_WorldUnmanaged, in originSystem, &entity, 1, null, 0);
#endif

            return entity;
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="archetype"></param>
        /// <exception cref="ArgumentException">Thrown if the archetype is null.</exception>
        /// <returns></returns>
        public Entity CreateEntityDuringStructuralChange(EntityArchetype archetype, in SystemHandleUntyped originSystem = default)
        {
            Entity entity;
            CreateEntityDuringStructuralChange(archetype, &entity, 1, in originSystem);
            return entity;
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="archetype"></param>
        /// <param name="outEntities"></param>
        /// <param name="count"></param>
        /// <exception cref="ArgumentException">Thrown if the archetype is null.</exception>
        internal void CreateEntityDuringStructuralChange(EntityArchetype archetype, Entity* outEntities, int count,
            in SystemHandleUntyped originSystem = default)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (count < 0)
                throw new ArgumentOutOfRangeException("count must be non-negative");
#endif
            Unity.Entities.EntityComponentStore.AssertValidArchetype(EntityComponentStore, archetype);

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginCreateEntity(m_WorldUnmanaged))
#endif
            {
                StructuralChange.CreateEntity(EntityComponentStore, archetype.Archetype, outEntities, count);
            }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordCreateEntity(in m_WorldUnmanaged, in originSystem, outEntities, count, (int*) archetype.Types, archetype.TypesCount);
#endif
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="archetype"></param>
        /// <param name="entities"></param>
        public void CreateEntityDuringStructuralChange(EntityArchetype archetype, NativeArray<Entity> entities, in SystemHandleUntyped originSystem = default)
        {
            CreateEntityDuringStructuralChange(archetype, (Entity*) entities.GetUnsafePtr(), entities.Length, in originSystem);
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="componentType"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">Thrown if an entity does not exist.</exception>
        /// <exception cref="ArgumentException">Thrown if the component type being added is Entity type.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the component increases the shared component count of the entity's archetype to more than the maximum allowed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the component causes the size of the archetype to exceed the size of a chunk.</exception>
        public bool AddComponentDuringStructuralChange(Entity entity, ComponentType componentType,
            in SystemHandleUntyped originSystem = default)
        {
            if (HasComponent(entity, componentType))
                return false;

            EntityComponentStore->AssertCanAddComponent(entity, componentType);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordAddComponent(in m_WorldUnmanaged, in originSystem, &entity, 1, &componentType.TypeIndex, 1);
#endif

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginAddComponent(m_WorldUnmanaged))
#endif
            {
                return StructuralChange.AddComponentEntity(EntityComponentStore, &entity, componentType.TypeIndex);
            }
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="archetypeList">The set of archetypes that defines the set of matching entities.</param>
        /// <param name="filter">Additional filter criteria for the entities.</param>
        /// <param name="componentType">The component type being added to the entities.</param>
        /// <exception cref="InvalidOperationException">Thrown if an entity in the query does not exist.</exception>
        /// <exception cref="ArgumentException">Thrown if the component type being added is Entity type.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the component increases the shared component count of the entity's archetype to more than the maximum allowed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the component causes the size of the archetype to exceed the size of a chunk.</exception>
        public void AddComponentDuringStructuralChange(UnsafeCachedChunkList cache,
            UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, ComponentType componentType,
            in SystemHandleUntyped originSystem = default)
        {
            if (archetypeList.Length == 0)
                return;
            EntityComponentStore->AssertCanAddComponent(archetypeList, componentType);

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginAddComponent(m_WorldUnmanaged))
#endif
            using (var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(cache, archetypeList, Allocator.TempJob,
                ref filter, DependencyManager))
            {
                if (chunks.Length > 0)
                {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
                    EntitiesJournaling.RecordAddComponent(in m_WorldUnmanaged, in originSystem, (ArchetypeChunk*) chunks.GetUnsafeReadOnlyPtr(), chunks.Length, &componentType.TypeIndex, 1);
#endif

                    StructuralChange.AddComponentChunks(EntityComponentStore,
                        (ArchetypeChunk*) NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length,
                        componentType.TypeIndex);
                }
            }
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="archetypeList">The set of archetypes that defines the set of matching entities.</param>
        /// <param name="filter">Additional filter criteria for the entities.</param>
        /// <param name="types">The component types being added to the entities.</param>
        /// <exception cref="InvalidOperationException">Thrown if an entity in the query does not exist.</exception>
        /// <exception cref="ArgumentException">Thrown if one of the component types being added is Entity type.</exception>
        /// <exception cref="InvalidOperationException">Thrown if one of the components increases the shared component count of the entity's archetype to more than the maximum allowed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if one of the components causes the size of the archetype to exceed the size of a chunk.</exception>
        internal void AddComponentsDuringStructuralChange(UnsafeCachedChunkList cache,
            UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, ComponentTypes types,
            in SystemHandleUntyped originSystem = default)
        {
            if (archetypeList.Length == 0 || types.Length == 0)
                return;
            EntityComponentStore->AssertCanAddComponents(archetypeList, types);

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginAddComponent(m_WorldUnmanaged))
#endif
            using (var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(cache, archetypeList, Allocator.TempJob,
                ref filter, DependencyManager))
            {
                if (chunks.Length > 0)
                {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
                    EntitiesJournaling.RecordAddComponent(in m_WorldUnmanaged, in originSystem, (ArchetypeChunk*) chunks.GetUnsafeReadOnlyPtr(), chunks.Length, types.Types, types.Length);
#endif

                    StructuralChange.AddComponentsChunks(EntityComponentStore,
                        (ArchetypeChunk*) NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, ref types);
                }
            }
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="entities">The set of entities.</param>
        /// <param name="componentType">The component type added to the entities.</param>
        /// <exception cref="InvalidOperationException">Thrown if an entity in the native array does not exist.</exception>
        /// <exception cref="ArgumentException">Thrown if a component type being added is Entity type.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the component increases the shared component count of the entity's archetype to more than the maximum allowed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the component causes the size of the archetype to exceed the size of a chunk.</exception>
        public void AddComponentDuringStructuralChange(NativeArray<Entity> entities, ComponentType componentType,
            in SystemHandleUntyped originSystem = default)
        {
            if (entities.Length == 0)
                return;

            EntityComponentStore->AssertCanAddComponent(entities, componentType);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordAddComponent(in m_WorldUnmanaged, in originSystem, (Entity*) entities.GetUnsafeReadOnlyPtr(), entities.Length, &componentType.TypeIndex, 1);
#endif

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginAddComponent(m_WorldUnmanaged))
#endif
            {
                if (entities.Length > FASTER_TO_BATCH_THRESHOLD &&
                    EntityComponentStore->CreateEntityBatchList(entities, componentType.IsSharedComponent ? 1 : 0,
                        out var entityBatchList))
                {
                    StructuralChange.AddComponentEntitiesBatch(EntityComponentStore,
                        (UnsafeList<EntityBatchInChunk>*) NativeListUnsafeUtility.GetInternalListDataPtrUnchecked(
                            ref entityBatchList), componentType.TypeIndex);
                    entityBatchList.Dispose();
                }
                else
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        var entity = entities[i];
                        StructuralChange.AddComponentEntity(EntityComponentStore, &entity, componentType.TypeIndex);
                    }
                }
            }
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="entities">The set of entities.</param>
        /// <param name="componentTypes">The component types added to the entities.</param>
        /// <exception cref="InvalidOperationException">Thrown if an entity in the native array does not exist.</exception>
        /// <exception cref="ArgumentException">Thrown if a component type being added is Entity type.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the component increases the shared component count of the entity's archetype to more than the maximum allowed.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the component causes the size of the archetype to exceed the size of a chunk.</exception>
        public void AddMultipleComponentsDuringStructuralChange(NativeArray<Entity> entities,
            ComponentTypes componentTypes, in SystemHandleUntyped originSystem = default)
        {
            if (entities.Length == 0 || componentTypes.Length == 0)
                return;
            EntityComponentStore->AssertCanAddComponents(entities, componentTypes);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordAddComponent(in m_WorldUnmanaged, in originSystem, (Entity*) entities.GetUnsafeReadOnlyPtr(), entities.Length, componentTypes.Types, componentTypes.Length);
#endif

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginAddComponent(m_WorldUnmanaged))
#endif
            {
                if (entities.Length > FASTER_TO_BATCH_THRESHOLD &&
                    EntityComponentStore->CreateEntityBatchList(entities, componentTypes.m_masks.SharedComponents,
                        out var entityBatchList))
                {
                    StructuralChange.AddComponentsEntitiesBatch(EntityComponentStore,
                        (UnsafeList<EntityBatchInChunk>*) NativeListUnsafeUtility.GetInternalListDataPtrUnchecked(
                            ref entityBatchList), ref componentTypes);
                    entityBatchList.Dispose();
                }
                else
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        var entity = entities[i];
                        StructuralChange.AddComponentsEntity(EntityComponentStore, &entity, ref componentTypes);
                    }
                }
            }
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="entityArray"></param>
        /// <param name="componentTypes"></param>
        /// <exception cref="InvalidOperationException">Thrown if a componentType is Entity type.</exception>
        public void RemoveMultipleComponentsDuringStructuralChange(NativeArray<Entity> entities,
            ComponentTypes componentTypes, in SystemHandleUntyped originSystem = default)
        {
            if (entities.Length == 0 || componentTypes.Length == 0)
                return;
            EntityComponentStore->AssertCanRemoveComponents(componentTypes);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordRemoveComponent(in m_WorldUnmanaged, in originSystem, (Entity*) entities.GetUnsafeReadOnlyPtr(), entities.Length, componentTypes.Types, componentTypes.Length);
#endif

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginRemoveComponent(m_WorldUnmanaged))
#endif
            {
                if (entities.Length > FASTER_TO_BATCH_THRESHOLD &&
                    EntityComponentStore->CreateEntityBatchList(entities, 0, out var entityBatchList))
                {
                    StructuralChange.RemoveComponentsEntitiesBatch(EntityComponentStore,
                        (UnsafeList<EntityBatchInChunk>*) NativeListUnsafeUtility.GetInternalListDataPtrUnchecked(
                            ref entityBatchList), ref componentTypes);
                    entityBatchList.Dispose();
                }
                else
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        var entity = entities[i];
                        StructuralChange.RemoveComponentsEntity(EntityComponentStore, &entity, ref componentTypes);
                    }
                }
            }
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="componentType"></param>
        /// <returns></returns>
        public bool RemoveComponentDuringStructuralChange(Entity entity, ComponentType componentType,
            in SystemHandleUntyped originSystem = default)
        {
            EntityComponentStore->AssertCanRemoveComponent(componentType);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordRemoveComponent(in m_WorldUnmanaged, in originSystem, &entity, 1, &componentType.TypeIndex, 1);
#endif

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginRemoveComponent(m_WorldUnmanaged))
#endif
            {
                return EntityComponentStore->RemoveComponent(entity, componentType);
            }
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="archetypeList"></param>
        /// <param name="cache"></param>
        /// <param name="filter"></param>
        /// <param name="componentType"></param>
        /// <exception cref="InvalidOperationException">Thrown if the componentType is Entity type.</exception>
        public void RemoveComponentDuringStructuralChange(UnsafeCachedChunkList cache,
            UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, ComponentType componentType,
            in SystemHandleUntyped originSystem = default)
        {
            if (archetypeList.Length == 0)
                return;
            EntityComponentStore->AssertCanRemoveComponent(componentType);

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginRemoveComponent(m_WorldUnmanaged))
#endif
            using (var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(cache, archetypeList, Allocator.TempJob,
                ref filter, DependencyManager))
            {
                if (chunks.Length > 0)
                {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
                    EntitiesJournaling.RecordRemoveComponent(in m_WorldUnmanaged, in originSystem,
                        (ArchetypeChunk*) chunks.GetUnsafeReadOnlyPtr(), chunks.Length, &componentType.TypeIndex, 1);
#endif

                    StructuralChange.RemoveComponentChunks(EntityComponentStore,
                        (ArchetypeChunk*) NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length,
                        componentType.TypeIndex);
                }
            }
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="archetypeList"></param>
        /// <param name="cache"></param>
        /// <param name="filter"></param>
        /// <param name="componentTypes"></param>
        internal void RemoveMultipleComponentsDuringStructuralChange(UnsafeCachedChunkList cache,
            UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, ComponentTypes componentTypes,
            in SystemHandleUntyped originSystem = default)
        {
            if (archetypeList.Length == 0 || componentTypes.Length == 0)
                return;
            EntityComponentStore->AssertCanRemoveComponents(componentTypes);

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginRemoveComponent(m_WorldUnmanaged))
#endif
            using (var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(cache, archetypeList, Allocator.TempJob,
                ref filter, DependencyManager))
            {
                if (chunks.Length > 0)
                {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
                    EntitiesJournaling.RecordRemoveComponent(in m_WorldUnmanaged, in originSystem, (ArchetypeChunk*) chunks.GetUnsafeReadOnlyPtr(), chunks.Length, componentTypes.Types, componentTypes.Length);
#endif

                    StructuralChange.RemoveComponentsChunks(EntityComponentStore,
                        (ArchetypeChunk*) NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length,
                        ref componentTypes);
                }
            }
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="chunks"></param>
        /// <param name="componentType"></param>
        internal void RemoveComponentDuringStructuralChange(NativeArray<ArchetypeChunk> chunks,
            ComponentType componentType, in SystemHandleUntyped originSystem = default)
        {
            if (chunks.Length == 0)
                return;

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordRemoveComponent(in m_WorldUnmanaged, in originSystem, (ArchetypeChunk*) chunks.GetUnsafeReadOnlyPtr(), chunks.Length, &componentType.TypeIndex, 1);
#endif

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginRemoveComponent(m_WorldUnmanaged))
#endif
            {
                StructuralChange.RemoveComponentChunks(EntityComponentStore,
                    (ArchetypeChunk*) NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length,
                    componentType.TypeIndex);
            }
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="entityArray"></param>
        /// <param name="componentType"></param>
        /// <exception cref="ArgumentException">Thrown if componentType is Entity type.</exception>
        public void RemoveComponentDuringStructuralChange(NativeArray<Entity> entities, ComponentType componentType,
            in SystemHandleUntyped originSystem = default)
        {
            if (entities.Length == 0)
                return;

            EntityComponentStore->AssertCanRemoveComponent(componentType);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordRemoveComponent(in m_WorldUnmanaged, in originSystem, (Entity*) entities.GetUnsafeReadOnlyPtr(), entities.Length, &componentType.TypeIndex, 1);
#endif

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginRemoveComponent(m_WorldUnmanaged))
#endif
            {
                if (entities.Length > FASTER_TO_BATCH_THRESHOLD &&
                    EntityComponentStore->CreateEntityBatchList(entities, 0, out var entityBatchList))
                {
                    StructuralChange.RemoveComponentEntitiesBatch(EntityComponentStore,
                        (UnsafeList<EntityBatchInChunk>*) NativeListUnsafeUtility.GetInternalListDataPtrUnchecked(
                            ref entityBatchList), componentType.TypeIndex);
                    entityBatchList.Dispose();
                }
                else
                {
                    for (var i = 0; i < entities.Length; ++i)
                    {
                        var entity = entities[i];
                        StructuralChange.RemoveComponentEntity(EntityComponentStore, &entity, componentType.TypeIndex);
                    }
                }
            }
        }

        public bool HasComponent(Entity entity, ComponentType type)
        {
            return EntityComponentStore->HasComponent(entity, type);
        }

        [BurstCompatible(GenericTypeArguments = new[] {typeof(BurstCompatibleComponentData)})]
        public T GetComponentData<T>(Entity entity) where T : struct, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();

            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);
            EntityComponentStore->AssertNotZeroSizedComponent(typeIndex);

            if (!IsInExclusiveTransaction)
                DependencyManager->CompleteWriteDependency(typeIndex);

            var ptr = EntityComponentStore->GetComponentDataWithTypeRO(entity, typeIndex);

            T value;
            UnsafeUtility.CopyPtrToStructure(ptr, out value);
            return value;
        }

        public void* GetComponentDataRawRW(Entity entity, int typeIndex)
        {
            return EntityComponentStore->GetComponentDataRawRW(entity, typeIndex);
        }

        internal void* GetComponentDataRawRWEntityHasComponent(Entity entity, int typeIndex)
        {
            return EntityComponentStore->GetComponentDataRawRWEntityHasComponent(entity, typeIndex);
        }

        [BurstCompatible(GenericTypeArguments = new[] {typeof(BurstCompatibleComponentData)})]
        public void SetComponentData<T>(Entity entity, T componentData, in SystemHandleUntyped originSystem = default)
            where T : struct, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();

            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);
            EntityComponentStore->AssertNotZeroSizedComponent(typeIndex);

            if (!IsInExclusiveTransaction)
                DependencyManager->CompleteReadAndWriteDependency(typeIndex);

            var ptr = EntityComponentStore->GetComponentDataWithTypeRW(entity, typeIndex,
                EntityComponentStore->GlobalSystemVersion);
            UnsafeUtility.CopyStructureToPtr(ref componentData, ptr);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordSetComponentData(in m_WorldUnmanaged, in originSystem, &entity, 1, &typeIndex, 1, UnsafeUtility.AddressOf(ref componentData), UnsafeUtility.SizeOf<T>());
#endif
        }

        public void SetComponentDataRaw(Entity entity, int typeIndex, void* data, int size,
            in SystemHandleUntyped originSystem = default)
        {
            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);
            EntityComponentStore->SetComponentDataRawEntityHasComponent(entity, typeIndex, data, size);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordSetComponentData(in m_WorldUnmanaged, in originSystem, &entity, 1, &typeIndex, 1, data, size);
#endif
        }

        public bool IsComponentEnabled(Entity entity, int typeIndex)
        {
            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);
            Unity.Entities.EntityComponentStore.AssertComponentEnableable(typeIndex);

            return EntityComponentStore->IsComponentEnabled(entity, typeIndex);
        }

        public void SetComponentEnabled(Entity entity, int typeIndex, bool value)
        {
            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);
            Unity.Entities.EntityComponentStore.AssertComponentEnableable(typeIndex);

            EntityComponentStore->SetComponentEnabled(entity, typeIndex, value);
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="componentData"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [NotBurstCompatible]
        public bool AddSharedComponentDataDuringStructuralChange<T>(Entity entity, T componentData, in SystemHandleUntyped originSystem = default)
            where T : struct, ISharedComponentData
        {
            //TODO: optimization: set value when component is added, not afterwards
            var added = AddComponentDuringStructuralChange(entity, ComponentType.ReadWrite<T>(), in originSystem);
            SetSharedComponentData(entity, componentData, in originSystem);
            return added;
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="chunks"></param>
        /// <param name="sharedComponentIndex"></param>
        /// <param name="componentType"></param>
        internal void AddSharedComponentDataDuringStructuralChange(NativeArray<ArchetypeChunk> chunks,
            int sharedComponentIndex, ComponentType componentType, in SystemHandleUntyped originSystem = default)
        {
            Assert.IsTrue(componentType.IsSharedComponent);
            if (chunks.Length == 0)
                return;

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            // Have to record here because method is not using EntityDataAccess
            var chunksPtr = (ArchetypeChunk*) chunks.GetUnsafeReadOnlyPtr();
            EntitiesJournaling.RecordAddComponent(in m_WorldUnmanaged, in originSystem, chunksPtr, chunks.Length, &componentType.TypeIndex, 1);
            EntitiesJournaling.RecordSetSharedComponentData(in m_WorldUnmanaged, in originSystem, chunksPtr, chunks.Length, &componentType.TypeIndex, 1);
#endif

            StructuralChange.AddSharedComponentChunks(EntityComponentStore,
                (ArchetypeChunk*) NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, componentType.TypeIndex,
                sharedComponentIndex);
        }

        [NotBurstCompatible]
        public void AddSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(NativeArray<Entity> entities,
            int typeIndex, int hashCode, object componentData, in SystemHandleUntyped originSystem = default)
        {
            if (entities.Length == 0)
                return;

            //TODO: optimization: set value when component is added, not afterwards
            AddComponentDuringStructuralChange(entities, ComponentType.FromTypeIndex(typeIndex), in originSystem);
            SetSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(entities, typeIndex, hashCode,
                componentData, in originSystem);
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="typeIndex"></param>
        /// <param name="hashCode"></param>
        /// <param name="componentData"></param>
        [NotBurstCompatible]
        public bool AddSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(Entity entity, int typeIndex,
            int hashCode, object componentData, in SystemHandleUntyped originSystem = default)
        {
            //TODO: optimize this (no need to move the entity to a new chunk twice)
            var added = AddComponentDuringStructuralChange(entity, ComponentType.FromTypeIndex(typeIndex),
                originSystem);
            SetSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(entity, typeIndex, hashCode,
                componentData, in originSystem);

            return added;
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="archetypeList"></param>
        /// <param name="cache"></param>
        /// <param name="filter"></param>
        /// <param name="typeIndex"></param>
        /// <param name="hashCode"></param>
        /// <param name="componentData"></param>
        [NotBurstCompatible]
        public void AddSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(
            UnsafeMatchingArchetypePtrList archetypeList, UnsafeCachedChunkList cache, EntityQueryFilter filter,
            int typeIndex, int hashCode, object componentData, in SystemHandleUntyped originSystem = default)
        {
            if (archetypeList.Length == 0)
                return;
            ComponentType componentType = ComponentType.FromTypeIndex(typeIndex);
            using (var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(cache, archetypeList, Allocator.TempJob,
                ref filter, DependencyManager))
            {
                if (chunks.Length == 0)
                    return;
                var newSharedComponentDataIndex = 0;
                if (componentData != null) // null means default
                    newSharedComponentDataIndex =
                        ManagedComponentStore.InsertSharedComponentAssumeNonDefault(typeIndex, hashCode, componentData);

                AddSharedComponentDataDuringStructuralChange(chunks, newSharedComponentDataIndex, componentType,
                    originSystem);
                m_ManagedReferenceIndexList.Add(newSharedComponentDataIndex);
            }
        }

        public EntityStorageInfo GetStorageInfo(Entity entity)
        {
            EntityComponentStore->AssertEntitiesExist(&entity, 1);

            var entityInChunk = EntityComponentStore->GetEntityInChunk(entity);
            return new EntityStorageInfo
            {
                Chunk = new ArchetypeChunk(entityInChunk.Chunk, EntityComponentStore),
                IndexInChunk = entityInChunk.IndexInChunk
            };
        }

        private void PlaybackManagedChangesMono()
        {
            ManagedComponentStore.Playback(ref EntityComponentStore->ManagedChangesTracker);
        }

        [BurstDiscard]
        private void PlaybackManagedDirectly(ref bool didTheThing)
        {
            didTheThing = true;
            PlaybackManagedChangesMono();
        }

        internal void PlaybackManagedChanges()
        {
            if (EntityComponentStore->ManagedChangesTracker.Empty)
                return;

            bool monoDitIt = false;
            PlaybackManagedDirectly(ref monoDitIt);
            if (monoDitIt)
                return;

            fixed (void* self = &this)
            {
                new FunctionPointer<PlaybackManagedDelegate>(s_ManagedPlaybackTrampoline.Data).Invoke((IntPtr) self);
            }
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
        public JobHandle GetCreatedAndDestroyedEntitiesAsync(NativeList<int> state, NativeList<Entity> createdEntities,
            NativeList<Entity> destroyedEntities)
        {
            var jobHandle = Entities.EntityComponentStore.GetCreatedAndDestroyedEntities(EntityComponentStore, state,
                createdEntities, destroyedEntities, true);
            DependencyManager->AddDependency(null, 0, null, 0, jobHandle);

            return jobHandle;
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
        public void GetCreatedAndDestroyedEntities(NativeList<int> state, NativeList<Entity> createdEntities,
            NativeList<Entity> destroyedEntities)
        {
            Entities.EntityComponentStore.GetCreatedAndDestroyedEntities(EntityComponentStore, state, createdEntities,
                destroyedEntities, false);
        }

        [NotBurstCompatible]
        public T GetSharedComponentData<T>(Entity entity) where T : struct, ISharedComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);

            var sharedComponentIndex = EntityComponentStore->GetSharedComponentDataIndex(entity, typeIndex);
            return GetSharedComponentData<T>(sharedComponentIndex);
        }

        [NotBurstCompatible]
        public void SetSharedComponentData<T>(Entity entity, T componentData, in SystemHandleUntyped originSystem = default)
            where T : struct, ISharedComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);

            var componentType = ComponentType.FromTypeIndex(typeIndex);
            var newSharedComponentDataIndex = InsertSharedComponent(componentData);
            EntityComponentStore->SetSharedComponentDataIndex(entity, componentType, newSharedComponentDataIndex);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordSetSharedComponentData(in m_WorldUnmanaged, in originSystem, &entity, 1, &typeIndex, 1);
#endif
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="typeIndex"></param>
        /// <param name="hashCode"></param>
        /// <param name="componentData"></param>
        [NotBurstCompatible]
        public void SetSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(Entity entity, int typeIndex,
            int hashCode, object componentData, in SystemHandleUntyped originSystem = default)
        {
            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);

            var newSharedComponentDataIndex = 0;
            if (componentData != null) // null means default
                newSharedComponentDataIndex = ManagedComponentStore.InsertSharedComponentAssumeNonDefault(typeIndex,
                    hashCode, componentData);
            var componentType = ComponentType.FromTypeIndex(typeIndex);
            EntityComponentStore->SetSharedComponentDataIndex(entity, componentType, newSharedComponentDataIndex);

            m_ManagedReferenceIndexList.Add(newSharedComponentDataIndex);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordSetSharedComponentData(in m_WorldUnmanaged, in originSystem, &entity, 1, &typeIndex, 1);
#endif
        }

        [NotBurstCompatible]
        public void SetSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(NativeArray<Entity> entities,
            int typeIndex, int hashCode, object componentData, in SystemHandleUntyped originSystem = default)
        {
            if (entities.Length == 0)
                return;

            var type = ComponentType.FromTypeIndex(typeIndex);
            EntityComponentStore->AssertEntityHasComponent(entities, type);

            var newSharedComponentDataIndex = 0;
            if (componentData != null) // null means default
                newSharedComponentDataIndex = ManagedComponentStore.InsertSharedComponentAssumeNonDefault(typeIndex,
                    hashCode, componentData);

            for (int i = 0; i < entities.Length; i++)
            {
                EntityComponentStore->SetSharedComponentDataIndex(entities[i], type, newSharedComponentDataIndex);
            }

            m_ManagedReferenceIndexList.Add(newSharedComponentDataIndex);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordSetSharedComponentData(in m_WorldUnmanaged, in originSystem, (Entity*) entities.GetUnsafeReadOnlyPtr(), entities.Length, &typeIndex, 1);
#endif
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [BurstCompatible(GenericTypeArguments = new[] {typeof(BurstCompatibleBufferElement)},
            CompileTarget = BurstCompatibleAttribute.BurstCompatibleCompileTarget.Editor)]
        public DynamicBuffer<T> GetBuffer<T>(Entity entity, AtomicSafetyHandle safety,
            AtomicSafetyHandle arrayInvalidationSafety, bool isReadOnly = false) where T : struct, IBufferElementData
#else
        [BurstCompatible(GenericTypeArguments = new[] {typeof(BurstCompatibleBufferElement)})]
        public DynamicBuffer<T> GetBuffer<T>(Entity entity, bool isReadOnly = false) where T : struct, IBufferElementData
#endif
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);
            if (!TypeManager.IsBuffer(typeIndex))
                throw new ArgumentException(
                    $"GetBuffer<{typeof(T)}> may not be IComponentData or ISharedComponentData; currently {TypeManager.GetTypeInfo<T>().Category}");
#endif

            if (!IsInExclusiveTransaction)
            {
                if (isReadOnly)
                    DependencyManager->CompleteWriteDependency(typeIndex);
                else
                    DependencyManager->CompleteReadAndWriteDependency(typeIndex);
            }

            BufferHeader* header;
            if (isReadOnly)
            {
                header = (BufferHeader*) EntityComponentStore->GetComponentDataWithTypeRO(entity, typeIndex);
            }
            else
            {
                header = (BufferHeader*) EntityComponentStore->GetComponentDataWithTypeRW(entity, typeIndex,
                    EntityComponentStore->GlobalSystemVersion);
            }

            int internalCapacity = TypeManager.GetTypeInfo(typeIndex).BufferCapacity;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var useMemoryInit = EntityComponentStore->useMemoryInitPattern != 0;
            byte memoryInitPattern = EntityComponentStore->memoryInitPattern;
            return new DynamicBuffer<T>(header, safety, arrayInvalidationSafety, isReadOnly, useMemoryInit,
                memoryInitPattern, internalCapacity);
#else
            return new DynamicBuffer<T>(header, internalCapacity);
#endif
        }

        public void SetBufferRaw(Entity entity, int componentTypeIndex, BufferHeader* tempBuffer, int sizeInChunk,
            in SystemHandleUntyped originSystem = default)
        {
            if (!IsInExclusiveTransaction)
                DependencyManager->CompleteReadAndWriteDependency(componentTypeIndex);

            EntityComponentStore->AssertEntityHasComponent(entity, componentTypeIndex);

            EntityComponentStore->SetBufferRaw(entity, componentTypeIndex, tempBuffer, sizeInChunk);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordSetBuffer(in m_WorldUnmanaged, in originSystem, &entity, 1, &componentTypeIndex, 1);
#endif
        }

        public EntityArchetype GetEntityOnlyArchetype()
        {
            if (!m_EntityOnlyArchetype.Valid)
                m_EntityOnlyArchetype = CreateArchetype(null, 0);

            return m_EntityOnlyArchetype;
        }

        /// <summary>
        /// This function must be wrapped in BeginStructuralChanges() and EndStructuralChanges(ref EntityComponentStore.ArchetypeChanges changes).
        /// </summary>
        /// <param name="srcEntity"></param>
        /// <param name="outputEntities"></param>
        /// <param name="count"></param>
        internal void InstantiateInternalDuringStructuralChange(Entity srcEntity, Entity* outputEntities, int count,
            in SystemHandleUntyped originSystem = default)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (count < 0)
                throw new ArgumentOutOfRangeException("count must be non-negative");
#endif
            EntityComponentStore->AssertEntitiesExist(&srcEntity, 1);
            EntityComponentStore->AssertCanInstantiateEntities(srcEntity, outputEntities, count);

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginCreateEntity(m_WorldUnmanaged))
#endif
            {
                StructuralChange.InstantiateEntities(EntityComponentStore, &srcEntity, outputEntities, count);
            }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordCreateEntity(in m_WorldUnmanaged, in originSystem, outputEntities, count, null, 0); //TODO: component types
#endif
        }

        internal void InstantiateInternalDuringStructuralChange(Entity* srcEntities, Entity* outputEntities, int count,
            int outputCount, bool removePrefab, in SystemHandleUntyped originSystem = default)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (count < 0)
                throw new ArgumentOutOfRangeException("count must be non-negative");
#endif
            AssertCountsMatch(count, outputCount);
            EntityComponentStore->AssertEntitiesExist(srcEntities, count);
            EntityComponentStore->AssertCanInstantiateEntities(srcEntities, count, removePrefab);

#if ENABLE_PROFILER
            using (var scope = StructuralChangesProfiler.BeginCreateEntity(m_WorldUnmanaged))
#endif
            {
                EntityComponentStore->InstantiateEntities(srcEntities, outputEntities, count, removePrefab);
            }

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordCreateEntity(in m_WorldUnmanaged, in originSystem, outputEntities, outputCount, null, 0); //TODO: component types
#endif
        }

        public void SwapComponents(ArchetypeChunk leftChunk, int leftIndex, ArchetypeChunk rightChunk, int rightIndex)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (leftIndex < 0 || leftIndex >= leftChunk.Count)
                throw new ArgumentOutOfRangeException(
                    $"leftIndex {leftIndex} is out of range for chunk count {leftChunk.Count}");
            if (rightIndex < 0 || rightIndex >= rightChunk.Count)
                throw new ArgumentOutOfRangeException(
                    $"rightIndex {rightIndex} is out of range for chunk count {rightChunk.Count}");
#endif
            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();

            var globalSystemVersion = EntityComponentStore->GlobalSystemVersion;

            ChunkDataUtility.SwapComponents(leftChunk.m_Chunk, leftIndex, rightChunk.m_Chunk, rightIndex, 1,
                globalSystemVersion, globalSystemVersion);
        }

        [BurstCompatible(GenericTypeArguments = new[] {typeof(BurstCompatibleBufferElement)})]
        public BufferTypeHandle<T> GetBufferTypeHandle<T>(bool isReadOnly)
            where T : struct, IBufferElementData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var typeIndex = TypeManager.GetTypeIndex<T>();
            return new BufferTypeHandle<T>(
                DependencyManager->Safety.GetSafetyHandleForBufferTypeHandle(typeIndex, isReadOnly),
                DependencyManager->Safety.GetBufferHandleForBufferTypeHandle(typeIndex),
                isReadOnly, EntityComponentStore->GlobalSystemVersion);
#else
            return new BufferTypeHandle<T>(isReadOnly, EntityComponentStore->GlobalSystemVersion);
#endif
        }

        [NotBurstCompatible]
        public void GetAllUniqueSharedComponents<T>(List<T> sharedComponentValues)
            where T : struct, ISharedComponentData
        {
            ManagedComponentStore.GetAllUniqueSharedComponents_Managed(sharedComponentValues);
        }

        [NotBurstCompatible]
        public void GetAllUniqueSharedComponents<T>(List<T> sharedComponentValues, List<int> sharedComponentIndices)
            where T : struct, ISharedComponentData
        {
            ManagedComponentStore.GetAllUniqueSharedComponents_Managed(sharedComponentValues, sharedComponentIndices);
        }

        [NotBurstCompatible]
        public int InsertSharedComponent<T>(T newData) where T : struct
        {
            var index = ManagedComponentStore.InsertSharedComponent_Managed(newData);
            m_ManagedReferenceIndexList.Add(index);
            return index;
        }

        [NotBurstCompatible]
        public int GetSharedComponentVersion<T>(T sharedData) where T : struct
        {
            return ManagedComponentStore.GetSharedComponentVersion_Managed(sharedData);
        }

        [NotBurstCompatible]
        public T GetSharedComponentData<T>(int sharedComponentIndex) where T : struct
        {
            return ManagedComponentStore.GetSharedComponentData_Managed<T>(sharedComponentIndex);
        }

        [NotBurstCompatible]
        public void AddSharedComponentReference(int sharedComponentIndex, int numRefs = 1)
        {
            ManagedComponentStore.AddSharedComponentReference_Managed(sharedComponentIndex, numRefs);
        }

        [NotBurstCompatible]
        public void RemoveSharedComponentReference(int sharedComponentIndex, int numRefs = 1)
        {
            ManagedComponentStore.RemoveSharedComponentReference_Managed(sharedComponentIndex, numRefs);
        }

        [NotBurstCompatible]
        public NativeArray<int> MoveAllSharedComponents(ManagedComponentStore srcManagedComponents, Allocator allocator)
        {
            return ManagedComponentStore.MoveAllSharedComponents_Managed(srcManagedComponents, allocator);
        }

        [NotBurstCompatible]
        public NativeArray<int> MoveSharedComponents(ManagedComponentStore srcManagedComponents,
            NativeArray<ArchetypeChunk> chunks, Allocator allocator)
        {
            return ManagedComponentStore.MoveSharedComponents_Managed(srcManagedComponents, chunks, allocator);
        }

        internal Entity GetEntityByEntityIndex(int index)
        {
            return EntityComponentStore->GetEntityByEntityIndex(index);
        }

        internal int GetNameIndexByEntityIndex(int index)
        {
            int nameIndex = 0;
#if !DOTS_DISABLE_DEBUG_NAMES
            var entityName = EntityComponentStore->GetEntityNameByEntityIndex(index);
            nameIndex = entityName.Index;
#endif
            return nameIndex;
        }

        /// <summary>
        /// Gets the name assigned to an entity.
        /// </summary>
        /// <remarks>For performance, entity names only exist when running in the Unity Editor.</remarks>
        /// <param name="entity">The Entity object of the entity of interest.</param>
        /// <returns>The entity name.</returns>
        [NotBurstCompatible]
        public string GetName(Entity entity)
        {
            return EntityComponentStore->GetName(entity);
        }

        public void GetName(Entity entity, out FixedString64Bytes name)
        {
            EntityComponentStore->GetName(entity, out name);
        }

        /// <summary>
        /// Sets the name of an entity.
        /// </summary>
        /// <remarks>For performance, entity names only exist when running in the Unity Editor.</remarks>
        /// <param name="entity">The Entity object of the entity to name.</param>
        /// <param name="name">The name to assign.</param>
        [NotBurstCompatible]
        public void SetName(Entity entity, string name)
        {
            EntityComponentStore->SetName(entity, name);
        }

        public void SetName(Entity entity, FixedString64Bytes name)
        {
            EntityComponentStore->SetName(entity, name);
        }

        /// <summary>
        /// Waits for all Jobs to complete.
        /// </summary>
        /// <remarks>Calling CompleteAllJobs() blocks the main thread until all currently running Jobs finish.</remarks>
        public void CompleteAllJobs()
        {
            DependencyManager->CompleteAllJobsAndInvalidateArrays();
        }
    }

    static unsafe partial class EntityDataAccessManagedComponentExtensions
    {
        internal static int* GetManagedComponentIndex(ref this EntityDataAccess dataAccess, Entity entity, int typeIndex)
        {
            dataAccess.EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);

            if (!dataAccess.IsInExclusiveTransaction)
                dataAccess.DependencyManager->CompleteReadAndWriteDependency(typeIndex);

            return (int*)dataAccess.EntityComponentStore->GetComponentDataWithTypeRW(entity, typeIndex, dataAccess.EntityComponentStore->GlobalSystemVersion);
        }

        public static T GetComponentData<T>(ref this EntityDataAccess dataAccess, Entity entity, ManagedComponentStore managedComponentStore) where T : class, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var index = *dataAccess.GetManagedComponentIndex(entity, typeIndex);
            return (T)managedComponentStore.GetManagedComponent(index);
        }

        public static T GetComponentObject<T>(ref this EntityDataAccess dataAccess, Entity entity, ComponentType componentType)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (!componentType.IsManagedComponent)
                throw new System.ArgumentException($"GetComponentObject must be called with a managed component type.");
#endif
            var index = *dataAccess.GetManagedComponentIndex(entity, componentType.TypeIndex);
            return (T)dataAccess.ManagedComponentStore.GetManagedComponent(index);
        }

        public static object Debugger_GetComponentObject(ref this EntityDataAccess dataAccess, Entity entity, ComponentType componentType)
        {
            int* ptr = (int*)EntityComponentStore.Debugger_GetComponentDataWithTypeRO(dataAccess.EntityComponentStore, entity, componentType.TypeIndex);
            if (ptr == null)
                return null;
            return dataAccess.ManagedComponentStore.Debugger_GetManagedComponent(*ptr);
        }


        public static void SetComponentObject(ref this EntityDataAccess dataAccess, Entity entity, ComponentType componentType, object componentObject, in SystemHandleUntyped originSystem = default)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (!componentType.IsManagedComponent)
                throw new System.ArgumentException($"SetComponentObject must be called with a managed component type.");
            if (componentObject != null && componentObject.GetType() != TypeManager.GetType(componentType.TypeIndex))
                throw new System.ArgumentException($"SetComponentObject {componentObject.GetType()} doesn't match the specified component type: {TypeManager.GetType(componentType.TypeIndex)}");
#endif
            var ptr = dataAccess.GetManagedComponentIndex(entity, componentType.TypeIndex);
            dataAccess.ManagedComponentStore.UpdateManagedComponentValue(ptr, componentObject, ref *dataAccess.EntityComponentStore);

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            EntitiesJournaling.RecordSetComponentObject(in dataAccess.m_WorldUnmanaged, in originSystem, &entity, 1, &componentType.TypeIndex, 1);
#endif
        }
    }
}
