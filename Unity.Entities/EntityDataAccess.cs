using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Profiling;

namespace Unity.Entities
{
    class ManagedEntityDataAccess
    {
        volatile static ManagedEntityDataAccess[] s_Instances = {null, null, null, null};

        // AllocHandle and FreeHandle must be called from the main thread
        public static int AllocHandle(ManagedEntityDataAccess instance)
        {
            var count = s_Instances.Length;
            for(int i=0; i<count; ++i)
            {
                if (s_Instances[i] == null)
                {
                    s_Instances[i] = instance;
                    return i;
                }
            }

            var newInstances = new ManagedEntityDataAccess[count*2];
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

        public World                       m_World;
        public EntityManager.EntityManagerDebug m_Debug;
        public ManagedComponentStore       m_ManagedComponentStore;
    }

    [StructLayout(LayoutKind.Sequential)]
    [BurstCompatible]
    unsafe struct EntityDataAccess : IDisposable
    {
        private delegate void PlaybackManagedDelegate(IntPtr self);

        // approximate count of entities past which it is faster
        // to form batches when iterating through them all
        internal static readonly int FASTER_TO_BATCH_THRESHOLD = 10;

        private static readonly SharedStatic<IntPtr> s_ManagedPlaybackTrampoline = SharedStatic<IntPtr>.GetOrCreate<PlaybackManagedDelegate>();
        private static object s_DelegateGCPrevention;

        [NotBurstCompatible]
        internal ManagedEntityDataAccess ManagedEntityDataAccess => ManagedEntityDataAccess.GetInstance(m_ManagedAccessHandle);

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
            get { fixed (EntityComponentStore* ptr = &m_EntityComponentStore) { return ptr; } }
        }

#if !NET_DOTS
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
#endif
        internal EntityQueryManager* EntityQueryManager
        {
            // This is always safe as the EntityDataAccess is always unsafe heap allocated.
            get { fixed (EntityQueryManager* ptr = &m_EntityQueryManager) { return ptr; } }
        }

#if !NET_DOTS
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
#endif
        internal ComponentDependencyManager* DependencyManager
        {
            // This is always safe as the EntityDataAccess is always unsafe heap allocated.
            get { fixed (ComponentDependencyManager* ptr = &m_DependencyManager) { return ptr; } }
        }

        [NativeDisableUnsafePtrRestriction]
        private EntityComponentStore m_EntityComponentStore;
        [NativeDisableUnsafePtrRestriction]
        private EntityQueryManager m_EntityQueryManager;
        [NativeDisableUnsafePtrRestriction]
        private ComponentDependencyManager m_DependencyManager;
        [NativeDisableUnsafePtrRestriction]
        public EntityQuery                 m_UniversalQuery; // matches all components
        [NativeDisableUnsafePtrRestriction]
        public EntityQuery                 m_UniversalQueryWithChunks;
        [NativeDisableUnsafePtrRestriction]
        public EntityQuery                 m_EntityGuidQuery;
#if UNITY_EDITOR
        public int                         m_CachedEntityGUIDToEntityIndexVersion;
        UntypedUnsafeHashMap               m_CachedEntityGUIDToEntityIndex;

        public ref UnsafeMultiHashMap<int, Entity> CachedEntityGUIDToEntityIndex
        {
            get
            {
                fixed (void* ptr = &m_CachedEntityGUIDToEntityIndex)
                {
                    return ref UnsafeUtility.AsRef<UnsafeMultiHashMap<int, Entity>>(ptr);
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

        private UntypedUnsafeHashMap m_AliveEntityQueries;

        internal ref UnsafeHashMap<ulong, byte> AliveEntityQueries
        {
            get
            {
                fixed (void* ptr = &m_AliveEntityQueries)
                {
                    return ref UnsafeUtility.AsRef<UnsafeHashMap<ulong, byte>>(ptr);
                }
            }
        }

        internal byte m_IsInExclusiveTransaction;
        internal bool IsInExclusiveTransaction => m_IsInExclusiveTransaction != 0;

        [BurstCompile]
        internal struct DestroyChunks : IJobBurstSchedulable
        {
            [NativeDisableUnsafePtrRestriction]
            public EntityComponentStore* EntityComponentStore;
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

            self->m_EntityOnlyArchetype = default;
            self->m_ManagedAccessHandle = ManagedEntityDataAccess.AllocHandle(managedGuts);

            self->AliveEntityQueries = new UnsafeHashMap<ulong, byte>(32, Allocator.Persistent);
#if UNITY_EDITOR
            self->CachedEntityGUIDToEntityIndex = new UnsafeMultiHashMap<int, Entity>(32, Allocator.Persistent);
#endif
            managedGuts.m_World = world;

            self->m_DependencyManager.OnCreate(world.Unmanaged);
            Entities.EntityComponentStore.Create(&self->m_EntityComponentStore, world.SequenceNumber << 32);
            Unity.Entities.EntityQueryManager.Create(&self->m_EntityQueryManager, &self->m_DependencyManager);

            managedGuts.m_ManagedComponentStore = new ManagedComponentStore();

            self->m_UniversalQuery = self->m_EntityQueryManager.CreateEntityQuery(
                self,
                new EntityQueryDesc { Options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabled }
            );

            self->m_UniversalQueryWithChunks = self->m_EntityQueryManager.CreateEntityQuery(
                self,
                new EntityQueryDesc
                {
                    All = new[] {ComponentType.ReadWrite<ChunkHeader>()},
                    Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab
                },
                new EntityQueryDesc
                {
                    Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab
                });
#if UNITY_EDITOR
            self->m_EntityGuidQuery = self->m_EntityQueryManager.CreateEntityQuery(
                self,
                new EntityQueryDesc
                {
                    All = new[] {ComponentType.ReadWrite<EntityGuid>()},
                    Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab
                }
            );
#endif

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

#if ENABLE_PROFILER
            self->marker_DestroyEntity = new ProfilerMarker("DestroyEntity(EntityQuery entityQueryFilter)");
            self->marker_GetAllMatchingChunks = new ProfilerMarker("GetAllMatchingChunks");
            self->marker_EditorOnlyChecks = new ProfilerMarker("EditorOnlyChecks");
            self->marker_DestroyChunks = new ProfilerMarker("DestroyChunks");
            self->marker_ManagedPlayback = new ProfilerMarker("Managed Playback");
#endif
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
            managedGuts.m_Debug = null;

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

        public void DestroyEntity(Entity entity)
        {
            DestroyEntityInternal(&entity, 1);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void CheckIsStructuralChange()
        {
            if (DependencyManager->IsInForEachDisallowStructuralChange != 0)
            {
                throw new InvalidOperationException(
                    "Structural changes are not allowed during Entities.ForEach. Please use EntityCommandBuffer instead.");
            }
        }

        public void BeforeStructuralChange()
        {
            // This is not an end user error. If there are any managed changes at this point, it indicates there is some
            // (previous) EntityManager change that is not properly playing back the managed changes that were buffered
            // afterward. That needs to be found and fixed.
            CheckIsStructuralChange();
            EntityComponentStore->AssertNoQueuedManagedDeferredCommands();

            if (m_IsInExclusiveTransaction == 0)
                DependencyManager->CompleteAllJobsAndInvalidateArrays();
        }

        private ProfilerMarker marker_DestroyEntity;  // Profiler.BeginSample("DestroyEntity(EntityQuery entityQueryFilter)");
        private ProfilerMarker marker_GetAllMatchingChunks; // Profiler.BeginSample("GetAllMatchingChunks");
        private ProfilerMarker marker_EditorOnlyChecks; // Profiler.BeginSample("EditorOnlyChecks");
        private ProfilerMarker marker_DestroyChunks; //  Profiler.BeginSample("DestroyChunks");
        private ProfilerMarker marker_ManagedPlayback; // Profiler.BeginSample("Managed Playback");

        [Conditional("ENABLE_PROFILER")]
        static void BeginMarker(ref ProfilerMarker marker)
        {
            marker.Begin();
        }

        [Conditional("ENABLE_PROFILER")]
        static void EndMarker(ref ProfilerMarker marker)
        {
            marker.End();
        }


        public void DestroyEntity(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter)
        {
            AssertMainThread();

            BeginMarker(ref marker_DestroyEntity);

            BeginMarker(ref marker_GetAllMatchingChunks);
            var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager);
            EndMarker(ref marker_GetAllMatchingChunks);

            var errorEntity = Entity.Null;
            var errorReferencedEntity = Entity.Null;

            if (chunks.Length != 0)
            {
                BeforeStructuralChange();

                BeginMarker(ref marker_EditorOnlyChecks);
                EntityComponentStore->AssertWillDestroyAllInLinkedEntityGroup(chunks, GetBufferTypeHandle<LinkedEntityGroup>(false), ref errorEntity, ref errorReferencedEntity);
                EndMarker(ref marker_EditorOnlyChecks);

                if (errorEntity == Entity.Null)
                {
                    // #todo @macton DestroyEntities should support IJobChunk. But internal writes need to be handled.
                    BeginMarker(ref marker_DestroyChunks);
                    RunDestroyChunks(chunks);
                    EndMarker(ref marker_DestroyChunks);

                    EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();

                    BeginMarker(ref marker_ManagedPlayback);
                    PlaybackManagedChanges();
                    EndMarker(ref marker_ManagedPlayback);
                }
            }
            chunks.Dispose();

            EndMarker(ref marker_DestroyEntity);

            // Defer throwing so we don't leak native arrays unnecessarily
            if (errorEntity != Entity.Null)
            {
                EntityComponentStore->ThrowDestroyEntityError(errorEntity, errorReferencedEntity);
            }
        }

        [BurstDiscard]
        private void RunDestroyChunksMono(NativeArray<ArchetypeChunk> chunks, ref bool didTheThing)
        {
            new DestroyChunks { EntityComponentStore = EntityComponentStore, Chunks = chunks }.Run();
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
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="archetypeList"></param>
        /// <param name="filter"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void DestroyEntityDuringStructuralChange(NativeArray<Entity> entities)
        {
            AssertMainThread();
            StructuralChange.DestroyEntity(EntityComponentStore, (Entity*)entities.GetUnsafePtr(), entities.Length);
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="archetypeList"></param>
        /// <param name="filter"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void DestroyEntityDuringStructuralChange(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter)
        {
            AssertMainThread();

            BeginMarker(ref marker_DestroyEntity);

            BeginMarker(ref marker_GetAllMatchingChunks);
            var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager);
            EndMarker(ref marker_GetAllMatchingChunks);

            var errorEntity = Entity.Null;
            var errorReferencedEntity = Entity.Null;

            if (chunks.Length != 0)
            {
                BeginMarker(ref marker_EditorOnlyChecks);
                EntityComponentStore->AssertWillDestroyAllInLinkedEntityGroup(chunks, GetBufferTypeHandle<LinkedEntityGroup>(false), ref errorEntity, ref errorReferencedEntity);
                EndMarker(ref marker_EditorOnlyChecks);

                if (errorEntity == Entity.Null)
                {
                    // #todo @macton DestroyEntities should support IJobChunk. But internal writes need to be handled.
                    BeginMarker(ref marker_DestroyChunks);
                    RunDestroyChunks(chunks);
                    EndMarker(ref marker_DestroyChunks);
                }
            }
            chunks.Dispose();

            EndMarker(ref marker_DestroyEntity);

            if (errorEntity != default)
            {
                EntityComponentStore->ThrowDestroyEntityError(errorEntity, errorReferencedEntity);
            }
        }

        internal EntityArchetype CreateArchetype(ComponentType* types, int count)
        {
            ComponentTypeInArchetype* typesInArchetype = stackalloc ComponentTypeInArchetype[count + 1];

            var cachedComponentCount = FillSortedArchetypeArray(typesInArchetype, types, count);

            // Lookup existing archetype (cheap)
            EntityArchetype entityArchetype;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            entityArchetype._DebugComponentStore = EntityComponentStore;
#endif

            entityArchetype.Archetype = EntityComponentStore->GetExistingArchetype(typesInArchetype, cachedComponentCount);
            if (entityArchetype.Archetype != null)
                return entityArchetype;

            // Creating an archetype invalidates all iterators / jobs etc
            // because it affects the live iteration linked lists...
            EntityComponentStore.ArchetypeChanges archetypeChanges = default;

            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();
            archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

            entityArchetype.Archetype = EntityComponentStore->GetOrCreateArchetype(typesInArchetype, cachedComponentCount);

            EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, EntityQueryManager);

            return entityArchetype;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckMoreThan1024Components(int count)
        {
            if (count + 1 > 1024)
                throw new ArgumentException($"Archetypes can't hold more than 1024 components");
        }

        internal static int FillSortedArchetypeArray(ComponentTypeInArchetype* dst, ComponentType* requiredComponents, int count)
        {
            CheckMoreThan1024Components(count);
            dst[0] = new ComponentTypeInArchetype(ComponentType.ReadWrite<Entity>());
            for (var i = 0; i < count; ++i)
                SortingUtilities.InsertSorted(dst, i + 1, requiredComponents[i]);
            return count + 1;
        }

        public Entity CreateEntity(EntityArchetype archetype)
        {
            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();

            Entity entity = CreateEntityDuringStructuralChange(archetype);
            EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
            PlaybackManagedChanges();
            return entity;
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="archetype"></param>
        /// <returns></returns>
        public Entity CreateEntityDuringStructuralChange(EntityArchetype archetype)
        {
            Entity entity = EntityComponentStore->CreateEntityWithValidation(archetype);
            return entity;
        }

        internal void CreateEntity(EntityArchetype archetype, Entity* outEntities, int count)
        {
            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();

            StructuralChange.CreateEntity(EntityComponentStore, archetype.Archetype, outEntities, count);

            EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
            Assert.IsTrue(EntityComponentStore->ManagedChangesTracker.Empty);
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="archetype"></param>
        /// <param name="outEntities"></param>
        /// <param name="count"></param>
        internal void CreateEntityDuringStructuralChange(EntityArchetype archetype, Entity* outEntities, int count)
        {
            EntityComponentStore->CreateEntityWithValidation(archetype, outEntities, count);
        }

        public void CreateEntity(EntityArchetype archetype, NativeArray<Entity> entities)
        {
            CreateEntity(archetype, (Entity*)entities.GetUnsafePtr(), entities.Length);
        }

        public void CreateEntity(EntityArchetype archetype, int entityCount)
        {
            CreateEntity(archetype, null, entityCount);
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="archetype"></param>
        /// <param name="entities"></param>
        public void CreateEntityDuringStructuralChange(EntityArchetype archetype, NativeArray<Entity> entities)
        {
            CreateEntityDuringStructuralChange(archetype, (Entity*)entities.GetUnsafePtr(), entities.Length);
        }

        public bool AddComponent(Entity entity, ComponentType componentType)
        {
            if (HasComponent(entity, componentType))
                return false;

            EntityComponentStore->AssertCanAddComponent(entity, componentType);

            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();

            var archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

            var result = AddComponentDuringStructuralChange(entity, componentType);

            EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, EntityQueryManager);
            EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
            PlaybackManagedChanges();

            return result;
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="componentType"></param>
        /// <returns></returns>
        public bool AddComponentDuringStructuralChange(Entity entity, ComponentType componentType)
        {
            if (HasComponent(entity, componentType))
                return false;

            var result = StructuralChange.AddComponentEntity(EntityComponentStore, &entity, componentType.TypeIndex);

            return result;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void AssertMainThread()
        {
            if (IsInExclusiveTransaction)
                throw new InvalidOperationException("Must be called from the main thread");
        }

        public void AddComponent(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter,
            ComponentType componentType)
        {
            AssertMainThread();
            EntityComponentStore->AssertCanAddComponent(archetypeList, componentType);

            var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager);

            if (chunks.Length > 0)
            {
                BeforeStructuralChange();
                var archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

                //@TODO the fast path for a chunk that contains a single entity is only possible if the chunk doesn't have a Locked Entity Order
                //but we should still be allowed to add zero sized components to chunks with a Locked Entity Order, even ones that only contain a single entity

                StructuralChange.AddComponentChunks(EntityComponentStore, (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, componentType.TypeIndex);

                EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, EntityQueryManager);
                EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
                PlaybackManagedChanges();
            }

            chunks.Dispose();
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="archetypeList">The set of archetypes that defines the set of matching entities.</param>
        /// <param name="filter">Additional filter criteria for the entities.</param>
        /// <param name="componentType">The component type being added to the entities.</param>
        /// <exception cref="InvalidOperationException">Thrown if the component cannot be added to the entities.</exception>
        public void AddComponentDuringStructuralChange(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, ComponentType componentType)
        {
            AssertMainThread();
            var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager);
            if (chunks.Length > 0)
            {
                StructuralChange.AddComponentChunks(EntityComponentStore, (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, componentType.TypeIndex);
            }
            chunks.Dispose();
        }

        internal void AddComponentsDuringStructuralChange(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, ComponentTypes types)
        {
            AssertMainThread();
            var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager);
            if (chunks.Length > 0)
            {
                StructuralChange.AddComponentsChunks(EntityComponentStore, (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, ref types);
            }
            chunks.Dispose();
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="entities">The set of entities.</param>
        /// <param name="componentType">The component type added to the entities.</param>
        /// <exception cref="InvalidOperationException">Thrown if the component cannot be added to the entities.</exception>
        public void AddComponentDuringStructuralChange(NativeArray<Entity> entities, ComponentType componentType)
        {
            AssertMainThread();

            EntityComponentStore->AssertCanAddComponent(entities, componentType);

            // use batches
            NativeList<EntityBatchInChunk> entityBatchList;
            if (entities.Length > FASTER_TO_BATCH_THRESHOLD
                && EntityComponentStore->CreateEntityBatchList(entities,
                    componentType.IsSharedComponent ? 1 : 0, out entityBatchList))
            {
                    StructuralChange.AddComponentEntitiesBatch(EntityComponentStore,
                            (UnsafeList*)NativeListUnsafeUtility.GetInternalListDataPtrUnchecked(ref entityBatchList), componentType.TypeIndex);
                    entityBatchList.Dispose();
                    return;
            }

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                StructuralChange.AddComponentEntity(EntityComponentStore, &entity, componentType.TypeIndex);
            }
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="entities">The set of entities.</param>
        /// <param name="componentTypes">The component types added to the entities.</param>
        /// <exception cref="InvalidOperationException">Thrown if the component cannot be added to the entities.</exception>
        public void AddMultipleComponentsDuringStructuralChange(NativeArray<Entity> entities, ComponentTypes componentTypes)
        {
            AssertMainThread();

            EntityComponentStore->AssertCanAddComponents(entities, componentTypes);

            // use batches
            NativeList<EntityBatchInChunk> entityBatchList;
            if (entities.Length > FASTER_TO_BATCH_THRESHOLD
                && EntityComponentStore->CreateEntityBatchList(entities,
                    componentTypes.m_masks.SharedComponents, out entityBatchList))
            {
                StructuralChange.AddComponentsEntitiesBatch(EntityComponentStore,
                        (UnsafeList*)NativeListUnsafeUtility.GetInternalListDataPtrUnchecked(ref entityBatchList), ref componentTypes);
                entityBatchList.Dispose();
                return;
            }

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                StructuralChange.AddComponentsEntity(EntityComponentStore, &entity, ref componentTypes);
            }
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="entityArray"></param>
        /// <param name="count"></param>
        /// <param name="componentTypes"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void RemoveMultipleComponentsDuringStructuralChange(NativeArray<Entity> entities, ComponentTypes componentTypes)
        {
            AssertMainThread();

            EntityComponentStore->AssertCanRemoveComponents(componentTypes);

            // use batches
            NativeList<EntityBatchInChunk> entityBatchList;
            if (entities.Length > FASTER_TO_BATCH_THRESHOLD
                && EntityComponentStore->CreateEntityBatchList(entities,
                    0, out entityBatchList))
            {
                StructuralChange.AddComponentsEntitiesBatch(EntityComponentStore,
                    (UnsafeList*)NativeListUnsafeUtility.GetInternalListDataPtrUnchecked(ref entityBatchList), ref componentTypes);
                entityBatchList.Dispose();
                return;
            }

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                StructuralChange.RemoveComponentsEntity(EntityComponentStore, &entity, ref componentTypes);
            }
        }

        internal void AddComponents(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, ComponentTypes types)
        {
            AssertMainThread();
            EntityComponentStore->AssertCanAddComponents(archetypeList, types);

            var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager);

            if (chunks.Length > 0)
            {
                BeforeStructuralChange();
                var archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

                StructuralChange.AddComponentsChunks(EntityComponentStore, (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, ref types);

                EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, EntityQueryManager);
                EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
                PlaybackManagedChanges();
            }

            chunks.Dispose();
        }

        public bool RemoveComponent(Entity entity, ComponentType componentType)
        {
            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();

            var archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

            var removed = RemoveComponentDuringStructuralChange(entity, componentType);

            EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, EntityQueryManager);
            EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
            PlaybackManagedChanges();

            return removed;
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="componentType"></param>
        /// <returns></returns>
        public bool RemoveComponentDuringStructuralChange(Entity entity, ComponentType componentType)
        {
            var removed = EntityComponentStore->RemoveComponent(entity, componentType);

            return removed;
        }

        public void RemoveComponent(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, ComponentType componentType)
        {
            AssertMainThread();
            var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager);
            RemoveComponent(chunks, componentType);
            chunks.Dispose();
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="archetypeList"></param>
        /// <param name="filter"></param>
        /// <param name="componentType"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void RemoveComponentDuringStructuralChange(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, ComponentType componentType)
        {
            AssertMainThread();
            var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager);
            StructuralChange.RemoveComponentChunks(EntityComponentStore, (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, componentType.TypeIndex);
            chunks.Dispose();
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="archetypeList"></param>
        /// <param name="filter"></param>
        /// <param name="componentTypes"></param>
        /// <exception cref="InvalidOperationException"></exception>
        internal void RemoveMultipleComponentsDuringStructuralChange(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, ComponentTypes componentTypes)
        {
            if (IsInExclusiveTransaction)
                throw new InvalidOperationException("Must be called from the main thread");

            var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager);
            StructuralChange.RemoveComponentsChunks(EntityComponentStore, (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, ref componentTypes);
            chunks.Dispose();
        }

        internal void RemoveComponent(NativeArray<ArchetypeChunk> chunks, ComponentType componentType)
        {
            if (chunks.Length == 0)
                return;

            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();
            var archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

            StructuralChange.RemoveComponentChunks(EntityComponentStore, (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, componentType.TypeIndex);

            EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, EntityQueryManager);
            EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
            PlaybackManagedChanges();
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="chunks"></param>
        /// <param name="componentType"></param>
        internal void RemoveComponentDuringStructuralChange(NativeArray<ArchetypeChunk> chunks, ComponentType componentType)
        {
            EntityComponentStore->RemoveComponentWithValidation(chunks, componentType);
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="entityArray"></param>
        /// <param name="count"></param>
        /// <param name="componentType"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void RemoveComponentDuringStructuralChange(NativeArray<Entity> entities, ComponentType componentType)
        {
            AssertMainThread();

            bool useBatches = entities.Length > 10;
            NativeList<EntityBatchInChunk> entityBatchList = default;
            if (useBatches)
            {
                useBatches = EntityComponentStore->CreateEntityBatchList(entities, 0, out entityBatchList);
            }

            EntityComponentStore->AssertCanRemoveComponent(componentType);
            if (!useBatches)
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    StructuralChange.RemoveComponentEntity(EntityComponentStore, &entity, componentType.TypeIndex);
                }
            }
            else
            {
                StructuralChange.RemoveComponentEntitiesBatch(EntityComponentStore, (UnsafeList*)NativeListUnsafeUtility.GetInternalListDataPtrUnchecked(ref entityBatchList), componentType.TypeIndex);
                entityBatchList.Dispose();
            }
        }

        public bool HasComponent(Entity entity, ComponentType type)
        {
            return EntityComponentStore->HasComponent(entity, type);
        }

        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public T GetComponentData<T>(Entity entity) where T : struct, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();

            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);
            EntityComponentStore->AssertZeroSizedComponent(typeIndex);

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

        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleComponentData) })]
        public void SetComponentData<T>(Entity entity, T componentData) where T : struct, IComponentData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();

            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);
            EntityComponentStore->AssertZeroSizedComponent(typeIndex);

            if (!IsInExclusiveTransaction)
                DependencyManager->CompleteReadAndWriteDependency(typeIndex);

            var ptr = EntityComponentStore->GetComponentDataWithTypeRW(entity, typeIndex,
                EntityComponentStore->GlobalSystemVersion);
            UnsafeUtility.CopyStructureToPtr(ref componentData, ptr);
        }

        public void SetComponentDataRaw(Entity entity, int typeIndex, void* data, int size)
        {
            EntityComponentStore->SetComponentDataRawEntityHasComponent(entity, typeIndex, data, size);
        }

        [NotBurstCompatible]
        public bool AddSharedComponentData<T>(Entity entity, T componentData, ManagedComponentStore managedComponentStore) where T : struct, ISharedComponentData
        {
            //TODO: optimize this (no need to move the entity to a new chunk twice)
            var added = AddComponent(entity, ComponentType.ReadWrite<T>());
            SetSharedComponentData(entity, componentData, managedComponentStore);
            return added;
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="componentData"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [NotBurstCompatible]
        public bool AddSharedComponentDataDuringStructuralChange<T>(Entity entity, T componentData) where T : struct, ISharedComponentData
        {
            //TODO: optimize this (no need to move the entity to a new chunk twice)
            var added = AddComponentDuringStructuralChange(entity, ComponentType.ReadWrite<T>());
            SetSharedComponentData(entity, componentData, ManagedComponentStore);
            return added;
        }

        [NotBurstCompatible]
        public void AddSharedComponentDataBoxedDefaultMustBeNull(Entity entity, int typeIndex, int hashCode, object componentData, ManagedComponentStore managedComponentStore)
        {
            //TODO: optimize this (no need to move the entity to a new chunk twice)
            AddComponent(entity, ComponentType.FromTypeIndex(typeIndex));
            SetSharedComponentDataBoxedDefaultMustBeNull(entity, typeIndex, hashCode, componentData, managedComponentStore);
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="typeIndex"></param>
        /// <param name="hashCode"></param>
        /// <param name="componentData"></param>
        [NotBurstCompatible]
        public bool AddSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(Entity entity, int typeIndex, int hashCode, object componentData, UnsafeList* managedReferenceIndexRemovalCount)
        {
            //TODO: optimize this (no need to move the entity to a new chunk twice)
            var added = AddComponentDuringStructuralChange(entity, ComponentType.FromTypeIndex(typeIndex));
            SetSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(entity, typeIndex, hashCode, componentData, managedReferenceIndexRemovalCount);

            return added;
        }

        [NotBurstCompatible]
        public void AddSharedComponentDataBoxedDefaultMustBeNull(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, int typeIndex, int hashCode, object componentData, ManagedComponentStore managedComponentStore)
        {
            AssertMainThread();

            ComponentType componentType = ComponentType.FromTypeIndex(typeIndex);
            using (var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager))
            {
                if (chunks.Length == 0)
                    return;
                var newSharedComponentDataIndex = 0;
                if (componentData != null) // null means default
                    newSharedComponentDataIndex = managedComponentStore.InsertSharedComponentAssumeNonDefault(typeIndex, hashCode, componentData);

                AddSharedComponentData(chunks, newSharedComponentDataIndex, componentType);
                RemoveSharedComponentReference(newSharedComponentDataIndex);
            }
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// ManagedComponentStore.RemoveReference() must be called after Playback for each newSharedComponentDataIndex added
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="archetypeList"></param>
        /// <param name="filter"></param>
        /// <param name="typeIndex"></param>
        /// <param name="hashCode"></param>
        /// <param name="componentData"></param>
        /// <exception cref="InvalidOperationException"></exception>
        [NotBurstCompatible]
        public void AddSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, int typeIndex, int hashCode, object componentData, UnsafeList* managedReferenceIndexRemovalCount)
        {
            AssertMainThread();

            ComponentType componentType = ComponentType.FromTypeIndex(typeIndex);
            using (var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager))
            {
                if (chunks.Length == 0)
                    return;
                var newSharedComponentDataIndex = 0;
                if (componentData != null) // null means default
                    newSharedComponentDataIndex = ManagedComponentStore.InsertSharedComponentAssumeNonDefault(typeIndex, hashCode, componentData);

                AddSharedComponentDataDuringStructuralChange(chunks, newSharedComponentDataIndex, componentType);
                managedReferenceIndexRemovalCount->Add(newSharedComponentDataIndex);
            }
        }

        internal void AddSharedComponentData(NativeArray<ArchetypeChunk> chunks, int sharedComponentIndex, ComponentType componentType)
        {
            Assert.IsTrue(componentType.IsSharedComponent);

            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();
            var archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

            StructuralChange.AddSharedComponentChunks(EntityComponentStore, (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, componentType.TypeIndex, sharedComponentIndex);

            EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, EntityQueryManager);
            EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
            PlaybackManagedChanges();
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
                new FunctionPointer<PlaybackManagedDelegate>(s_ManagedPlaybackTrampoline.Data).Invoke((IntPtr)self);
            }
        }


        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="chunks"></param>
        /// <param name="sharedComponentIndex"></param>
        /// <param name="componentType"></param>
        internal void AddSharedComponentDataDuringStructuralChange(NativeArray<ArchetypeChunk> chunks, int sharedComponentIndex, ComponentType componentType)
        {
            Assert.IsTrue(componentType.IsSharedComponent);
            StructuralChange.AddSharedComponentChunks(EntityComponentStore, (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, componentType.TypeIndex, sharedComponentIndex);
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
        public void SetSharedComponentData<T>(Entity entity, T componentData, ManagedComponentStore managedComponentStore) where T : struct, ISharedComponentData
        {
            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();

            var typeIndex = TypeManager.GetTypeIndex<T>();
            var componentType = ComponentType.FromTypeIndex(typeIndex);
            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);

            var newSharedComponentDataIndex = InsertSharedComponent(componentData);
            EntityComponentStore->SetSharedComponentDataIndex(entity, componentType, newSharedComponentDataIndex);
            EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
            managedComponentStore.Playback(ref EntityComponentStore->ManagedChangesTracker);
            RemoveSharedComponentReference(newSharedComponentDataIndex);
        }

        [NotBurstCompatible]
        public void SetSharedComponentDataBoxedDefaultMustBeNull(Entity entity, int typeIndex, int hashCode, object componentData, ManagedComponentStore managedComponentStore)
        {
            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();

            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);

            var newSharedComponentDataIndex = 0;
            if (componentData != null) // null means default
                newSharedComponentDataIndex = managedComponentStore.InsertSharedComponentAssumeNonDefault(typeIndex, hashCode, componentData);

            var componentType = ComponentType.FromTypeIndex(typeIndex);
            EntityComponentStore->SetSharedComponentDataIndex(entity, componentType, newSharedComponentDataIndex);
            EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
            ManagedComponentStore.Playback(ref EntityComponentStore->ManagedChangesTracker);
            RemoveSharedComponentReference(newSharedComponentDataIndex);
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// ManagedComponentStore.RemoveReference() must be called after Playback for each newSharedComponentDataIndex added
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="typeIndex"></param>
        /// <param name="hashCode"></param>
        /// <param name="componentData"></param>
        [NotBurstCompatible]
        public void SetSharedComponentDataBoxedDefaultMustBeNullDuringStructuralChange(Entity entity, int typeIndex, int hashCode, object componentData, UnsafeList* managedReferenceIndexRemovalCount)
        {
            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);

            var newSharedComponentDataIndex = 0;
            if (componentData != null) // null means default
                newSharedComponentDataIndex = ManagedComponentStore.InsertSharedComponentAssumeNonDefault(typeIndex,
                    hashCode, componentData);
            var componentType = ComponentType.FromTypeIndex(typeIndex);
            EntityComponentStore->SetSharedComponentDataIndex(entity, componentType, newSharedComponentDataIndex);

            managedReferenceIndexRemovalCount->Add(newSharedComponentDataIndex);
        }

        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleBufferElement) })]
        public DynamicBuffer<T> GetBuffer<T>(Entity entity
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            , AtomicSafetyHandle safety, AtomicSafetyHandle arrayInvalidationSafety
#endif
        ) where T : struct, IBufferElementData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);
            if (!TypeManager.IsBuffer(typeIndex))
                throw new ArgumentException(
                    $"GetBuffer<{typeof(T)}> may not be IComponentData or ISharedComponentData; currently {TypeManager.GetTypeInfo<T>().Category}");
#endif

            if (!IsInExclusiveTransaction)
                DependencyManager->CompleteReadAndWriteDependency(typeIndex);

            BufferHeader* header =
                (BufferHeader*)EntityComponentStore->GetComponentDataWithTypeRW(entity, typeIndex,
                    EntityComponentStore->GlobalSystemVersion);

            int internalCapacity = TypeManager.GetTypeInfo(typeIndex).BufferCapacity;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var useMemoryInit = EntityComponentStore->useMemoryInitPattern != 0;
            byte memoryInitPattern = EntityComponentStore->memoryInitPattern;
            return new DynamicBuffer<T>(header, safety, arrayInvalidationSafety, false, useMemoryInit, memoryInitPattern, internalCapacity);
#else
            return new DynamicBuffer<T>(header, internalCapacity);
#endif
        }

        public void SetBufferRaw(Entity entity, int componentTypeIndex, BufferHeader* tempBuffer, int sizeInChunk)
        {
            if (!IsInExclusiveTransaction)
                DependencyManager->CompleteReadAndWriteDependency(componentTypeIndex);

            EntityComponentStore->SetBufferRawWithValidation(entity, componentTypeIndex, tempBuffer, sizeInChunk);
        }

        public EntityArchetype GetEntityOnlyArchetype()
        {
            if (!m_EntityOnlyArchetype.Valid)
                m_EntityOnlyArchetype = CreateArchetype(null, 0);

            return m_EntityOnlyArchetype;
        }

        internal void InstantiateInternal(Entity srcEntity, Entity* outputEntities, int count)
        {
            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();
            EntityComponentStore->AssertEntitiesExist(&srcEntity, 1);
            EntityComponentStore->AssertCanInstantiateEntities(srcEntity, outputEntities, count);
            StructuralChange.InstantiateEntities(EntityComponentStore, &srcEntity, outputEntities, count);
            EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
            PlaybackManagedChanges();
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="srcEntity"></param>
        /// <param name="outputEntities"></param>
        /// <param name="count"></param>
        internal void InstantiateInternalDuringStructuralChange(Entity srcEntity, Entity* outputEntities, int count)
        {
            EntityComponentStore->InstantiateWithValidation(srcEntity, outputEntities, count);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void AssertCountsMatch(int count, int outputCount)
        {
            if (count != outputCount)
                throw new System.ArgumentException($"srcEntities.Length ({count}) and outputEntities.Length (({outputCount})) must be the same.");
        }

        internal void InstantiateInternal(Entity* srcEntities, Entity* outputEntities, int count, int outputCount, bool removePrefab)
        {
            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();

            AssertCountsMatch(count, outputCount);

            EntityComponentStore->AssertEntitiesExist(srcEntities, count);
            EntityComponentStore->AssertCanInstantiateEntities(srcEntities, count, removePrefab);
            EntityComponentStore->InstantiateEntities(srcEntities, outputEntities, count, removePrefab);
            EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
            PlaybackManagedChanges();
        }

        internal void DestroyEntityInternal(Entity* entities, int count)
        {
            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();

            EntityComponentStore->AssertValidEntities(entities, count);

            EntityComponentStore->DestroyEntities(entities, count);

            EntityComponentStore->InvalidateChunkListCacheForChangedArchetypes();
            PlaybackManagedChanges();
        }

        /// <summary>
        /// EntityManager.BeforeStructuralChange must be called before invoking this.
        /// ManagedComponentStore.Playback must be called after invoking this.
        /// EntityQueryManager.AddAdditionalArchetypes must be called after invoking this.
        /// Invoking this must be wrapped in ArchetypeChangeTracking.
        /// </summary>
        /// <param name="entities"></param>
        /// <param name="count"></param>
        internal void DestroyEntityInternalDuringStructuralChange(Entity* entities, int count)
        {
            EntityComponentStore->DestroyEntityWithValidation(entities, count);
        }

        public void SwapComponents(ArchetypeChunk leftChunk, int leftIndex, ArchetypeChunk rightChunk, int rightIndex)
        {
            if (!IsInExclusiveTransaction)
                BeforeStructuralChange();

            var globalSystemVersion = EntityComponentStore->GlobalSystemVersion;

            ChunkDataUtility.SwapComponents(leftChunk.m_Chunk, leftIndex, rightChunk.m_Chunk, rightIndex, 1,
                globalSystemVersion, globalSystemVersion);
        }

        [BurstCompatible(GenericTypeArguments = new[] { typeof(BurstCompatibleBufferElement) })]
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
            return ManagedComponentStore.InsertSharedComponent_Managed(newData);
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
        public NativeArray<int> MoveSharedComponents(ManagedComponentStore srcManagedComponents, NativeArray<ArchetypeChunk> chunks,  Allocator allocator)
        {
            return ManagedComponentStore.MoveSharedComponents_Managed(srcManagedComponents, chunks, allocator);
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

        public static T GetComponentObject<T>(ref this EntityDataAccess dataAccess, Entity entity, ComponentType componentType, ManagedComponentStore managedComponentStore)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!componentType.IsManagedComponent)
                throw new System.ArgumentException($"GetComponentObject must be called with a managed component type.");
#endif
            var index = *dataAccess.GetManagedComponentIndex(entity, componentType.TypeIndex);
            return (T)managedComponentStore.GetManagedComponent(index);
        }

        public static void SetComponentObject(ref this EntityDataAccess dataAccess, Entity entity, ComponentType componentType, object componentObject, ManagedComponentStore managedComponentStore)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!componentType.IsManagedComponent)
                throw new System.ArgumentException($"SetComponentObject must be called with a managed component type.");
            if (componentObject != null && componentObject.GetType() != TypeManager.GetType(componentType.TypeIndex))
                throw new System.ArgumentException($"SetComponentObject {componentObject.GetType()} doesn't match the specified component type: {TypeManager.GetType(componentType.TypeIndex)}");
#endif
            var ptr = dataAccess.GetManagedComponentIndex(entity, componentType.TypeIndex);
            managedComponentStore.UpdateManagedComponentValue(ptr, componentObject, ref *dataAccess.EntityComponentStore);
        }
    }
}
