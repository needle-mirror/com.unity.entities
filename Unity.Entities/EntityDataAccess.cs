using System;
using System.Runtime.InteropServices;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Profiling;

unsafe struct EntityDataAccess : IDisposable
{ 
    internal ManagedComponentStore ManagedComponentStore => EntityManager?.ManagedComponentStore;
    internal EntityManager EntityManager => (EntityManager)m_EntityManager.Target;
    
    internal bool IsMainThread => m_IsMainThread;

    [NativeDisableUnsafePtrRestriction] GCHandle m_EntityManager;
    
    [NativeDisableUnsafePtrRestriction]
    readonly internal EntityComponentStore*      EntityComponentStore;
    [NativeDisableUnsafePtrRestriction]
    readonly internal EntityQueryManager*        EntityQueryManager;
    [NativeDisableUnsafePtrRestriction]
    readonly internal ComponentDependencyManager* DependencyManager;

    
    EntityArchetype                              m_EntityOnlyArchetype;
    bool                                         m_IsMainThread;

    [BurstCompile]
    struct DestroyChunks : IJob
    {
        [NativeDisableUnsafePtrRestriction]
        public EntityComponentStore* EntityComponentStore;
        public NativeArray<ArchetypeChunk> Chunks;

        public void Execute()
        {
            EntityComponentStore->DestroyEntities(Chunks);
        }
    }

    public EntityDataAccess(EntityManager entityManager, bool isMainThread)
    {
        m_EntityManager = GCHandle.Alloc(entityManager, GCHandleType.Weak);
        EntityComponentStore = entityManager.EntityComponentStore;
        EntityQueryManager = entityManager.EntityQueryManager;
        DependencyManager = entityManager.DependencyManager;
        
        m_IsMainThread = isMainThread;
        m_EntityOnlyArchetype = default;
    }

    public void Dispose()
    {
        m_EntityManager.Free();
    }    

    public bool Exists(Entity entity)
    {
        return EntityComponentStore->Exists(entity);
    }

    public void DestroyEntity(Entity entity)
    {
        DestroyEntityInternal(&entity, 1);
    }

    public void BeforeStructuralChange()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (DependencyManager->IsInTransaction)
        {
            throw new InvalidOperationException(
                "Access to EntityManager is not allowed after EntityManager.BeginExclusiveEntityTransaction(); has been called.");
        }

        if (DependencyManager->IsInForEachDisallowStructuralChange != 0)
        {
            throw new InvalidOperationException(
                "Structural changes are not allowed during Entities.ForEach. Please use EntityCommandBuffer instead.");
        }

        // This is not an end user error. If there are any managed changes at this point, it indicates there is some
        // (previous) EntityManager change that is not properly playing back the managed changes that were buffered 
        // afterward. That needs to be found and fixed. 
        EntityComponentStore->AssertNoQueuedManagedDeferredCommands();
#endif

        DependencyManager->CompleteAllJobsAndInvalidateArrays();
    }

    public void DestroyEntity(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter)
    {
        if (!m_IsMainThread)
            throw new InvalidOperationException("Must be called from the main thread");

        Profiler.BeginSample("DestroyEntity(EntityQuery entityQueryFilter)");

        Profiler.BeginSample("GetAllMatchingChunks");
        using (var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager))
        {
            Profiler.EndSample();

            if (chunks.Length != 0)
            {
                BeforeStructuralChange();

                Profiler.BeginSample("EditorOnlyChecks");
                EntityComponentStore->AssertCanDestroy(chunks);
                EntityComponentStore->AssertWillDestroyAllInLinkedEntityGroup(chunks, EntityManager.GetArchetypeChunkBufferType<LinkedEntityGroup>(false));
                Profiler.EndSample();

                // #todo @macton DestroyEntities should support IJobChunk. But internal writes need to be handled.
                Profiler.BeginSample("DeleteChunks");
                new DestroyChunks { EntityComponentStore = EntityComponentStore, Chunks = chunks }.Run();
                Profiler.EndSample();

                Profiler.BeginSample("Managed Playback");
                PlaybackManagedChanges();
                Profiler.EndSample();
            }
        }

        Profiler.EndSample();
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
        
        if (m_IsMainThread)
            BeforeStructuralChange();
        archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

        entityArchetype.Archetype = EntityComponentStore->GetOrCreateArchetype(typesInArchetype, cachedComponentCount);

        EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, EntityQueryManager);
    
        return entityArchetype;
    }

    static int FillSortedArchetypeArray(ComponentTypeInArchetype* dst, ComponentType* requiredComponents, int count)
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (count + 1 > 1024)
            throw new ArgumentException($"Archetypes can't hold more than 1024 components");
#endif

        dst[0] = new ComponentTypeInArchetype(ComponentType.ReadWrite<Entity>());
        for (var i = 0; i < count; ++i)
            SortingUtilities.InsertSorted(dst, i + 1, requiredComponents[i]);
        return count + 1;
    }

    public Entity CreateEntity(EntityArchetype archetype)
    {
        Unity.Entities.EntityComponentStore.AssertValidArchetype(EntityComponentStore, archetype);
        
        Entity entity;
        if (m_IsMainThread)
            BeforeStructuralChange();
        StructuralChange.CreateEntity(EntityComponentStore, archetype.Archetype, &entity, 1);
        Assert.IsTrue(EntityComponentStore->ManagedChangesTracker.Empty);
        
        return entity;
    }

    internal void CreateEntity(EntityArchetype archetype, Entity* outEntities, int count)
    {
        Unity.Entities.EntityComponentStore.AssertValidArchetype(EntityComponentStore, archetype);
        
        if (m_IsMainThread)
            BeforeStructuralChange();
        StructuralChange.CreateEntity(EntityComponentStore, archetype.Archetype, outEntities, count);
        Assert.IsTrue(EntityComponentStore->ManagedChangesTracker.Empty);
    }

    public void CreateEntity(EntityArchetype archetype, NativeArray<Entity> entities)
    {
        CreateEntity(archetype, (Entity*) entities.GetUnsafePtr(), entities.Length);
    }

    public bool AddComponent(Entity entity, ComponentType componentType)
    {
        if (HasComponent(entity, componentType))
            return false;

        EntityComponentStore->AssertCanAddComponent(entity, componentType);

        if (m_IsMainThread)
            BeforeStructuralChange();

        var archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

        StructuralChange.AddComponentEntity(EntityComponentStore, &entity, componentType.TypeIndex);

        EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, EntityQueryManager);
        PlaybackManagedChanges();

        return true;
    }

    public void AddComponent(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, ComponentType componentType)
    {
        if (!m_IsMainThread)
            throw new InvalidOperationException("Must be called from the main thread");

        EntityComponentStore->AssertCanAddComponent(archetypeList, componentType);

        using (var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager))
        {
            if (chunks.Length == 0)
                return;

            EntityComponentStore->AssertCanAddComponent(chunks, componentType);

            BeforeStructuralChange();
            var archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

            //@TODO the fast path for a chunk that contains a single entity is only possible if the chunk doesn't have a Locked Entity Order
            //but we should still be allowed to add zero sized components to chunks with a Locked Entity Order, even ones that only contain a single entity

            /*
            if ((chunks.Length == 1) && (chunks[0].Count == 1))
            {
                var entityPtr = (Entity*) chunks[0].m_Chunk->Buffer;
                StructuralChange.AddComponentEntity(EntityComponentStore, entityPtr, componentType.TypeIndex);
            }
            else
            {
            */
            StructuralChange.AddComponentChunks(EntityComponentStore, (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, componentType.TypeIndex);
            /*
            }
            */

            EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, EntityQueryManager);
            PlaybackManagedChanges();
        }
    }

    public bool RemoveComponent(Entity entity, ComponentType componentType)
    {
        EntityComponentStore->ValidateEntity(entity);
        EntityComponentStore->AssertCanRemoveComponent(entity, componentType);
        if (m_IsMainThread)
            BeforeStructuralChange();

        var archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

        var removed = StructuralChange.RemoveComponentEntity(EntityComponentStore, &entity, componentType.TypeIndex);

        EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, EntityQueryManager);
        PlaybackManagedChanges();

        return removed;
    }

    public void RemoveComponent(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, ComponentType componentType)
    {
        if (!m_IsMainThread)
            throw new InvalidOperationException("Must be called from the main thread");

        using (var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager))
        {
            RemoveComponent(chunks, componentType);
        }
    }

    internal void RemoveComponent(NativeArray<ArchetypeChunk> chunks, ComponentType componentType)
    {
        if (chunks.Length == 0)
            return;

        EntityComponentStore->AssertCanRemoveComponent(chunks, componentType);

        if (m_IsMainThread)
            BeforeStructuralChange();
        var archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

        StructuralChange.RemoveComponentChunks(EntityComponentStore, (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, componentType.TypeIndex);

        EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, EntityQueryManager);
        PlaybackManagedChanges();
    }
    public bool HasComponent(Entity entity, ComponentType type)
    {
        return EntityComponentStore->HasComponent(entity, type);
    }

    public T GetComponentData<T>(Entity entity) where T : struct, IComponentData
    {
        var typeIndex = TypeManager.GetTypeIndex<T>();

        EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (ComponentType.FromTypeIndex(typeIndex).IsZeroSized)
            throw new System.ArgumentException(
                $"GetComponentData<{typeof(T)}> can not be called with a zero sized component.");
#endif

        if (m_IsMainThread)
            DependencyManager->CompleteWriteDependency(typeIndex);

        var ptr = EntityComponentStore->GetComponentDataWithTypeRO(entity, typeIndex);

        T value;
        UnsafeUtility.CopyPtrToStructure(ptr, out value);
        return value;
    }

    public void* GetComponentDataRawRW(Entity entity, int typeIndex)
    {
        EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);
        return GetComponentDataRawRWEntityHasComponent(entity, typeIndex);
    }

    internal void* GetComponentDataRawRWEntityHasComponent(Entity entity, int typeIndex)
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (TypeManager.GetTypeInfo(typeIndex).IsZeroSized)
            throw new System.ArgumentException(
                $"GetComponentData<{TypeManager.GetType(typeIndex)}> can not be called with a zero sized component.");
#endif

        var ptr = EntityComponentStore->GetComponentDataWithTypeRW(entity, typeIndex,
            EntityComponentStore->GlobalSystemVersion);
        return ptr;
    }

    public void SetComponentData<T>(Entity entity, T componentData) where T : struct, IComponentData
    {
        var typeIndex = TypeManager.GetTypeIndex<T>();

        EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (ComponentType.FromTypeIndex(typeIndex).IsZeroSized)
            throw new System.ArgumentException(
                $"SetComponentData<{typeof(T)}> can not be called with a zero sized component.");
#endif

        if (m_IsMainThread)
            DependencyManager->CompleteReadAndWriteDependency(typeIndex);

        var ptr = EntityComponentStore->GetComponentDataWithTypeRW(entity, typeIndex,
            EntityComponentStore->GlobalSystemVersion);
        UnsafeUtility.CopyStructureToPtr(ref componentData, ptr);
    }

    public void SetComponentDataRaw(Entity entity, int typeIndex, void* data, int size)
    {
        EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);
        SetComponentDataRawEntityHasComponent(entity, typeIndex, data, size);
    }

    internal void SetComponentDataRawEntityHasComponent(Entity entity, int typeIndex, void* data, int size)
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (TypeManager.GetTypeInfo(typeIndex).SizeInChunk != size)
            throw new System.ArgumentException(
                $"SetComponentData<{TypeManager.GetType(typeIndex)}> can not be called with a zero sized component and must have same size as sizeof(T).");
#endif

        var ptr = EntityComponentStore->GetComponentDataWithTypeRW(entity, typeIndex,
            EntityComponentStore->GlobalSystemVersion);
        UnsafeUtility.MemCpy(ptr, data, size);
    }

    public bool AddSharedComponentData<T>(Entity entity, T componentData, ManagedComponentStore managedComponentStore) where T : struct, ISharedComponentData
    {
        //TODO: optimize this (no need to move the entity to a new chunk twice)
        var added = AddComponent(entity, ComponentType.ReadWrite<T>());
        SetSharedComponentData(entity, componentData, managedComponentStore);
        return added;
    }

    public void AddSharedComponentDataBoxedDefaultMustBeNull(Entity entity, int typeIndex, int hashCode, object componentData, ManagedComponentStore managedComponentStore)
    {
        //TODO: optimize this (no need to move the entity to a new chunk twice)
        AddComponent(entity, ComponentType.FromTypeIndex(typeIndex));
        SetSharedComponentDataBoxedDefaultMustBeNull(entity, typeIndex, hashCode, componentData, managedComponentStore);
    }

    public void AddSharedComponentDataBoxedDefaultMustBeNull(UnsafeMatchingArchetypePtrList archetypeList, EntityQueryFilter filter, int typeIndex, int hashCode, object componentData, ManagedComponentStore managedComponentStore)
    {
        if (!m_IsMainThread)
            throw new InvalidOperationException("Must be called from the main thread");

        ComponentType componentType = ComponentType.FromTypeIndex(typeIndex);
        using (var chunks = ChunkIterationUtility.CreateArchetypeChunkArray(archetypeList, Allocator.TempJob, ref filter, DependencyManager))
        {
            if (chunks.Length == 0)
                return;
            var newSharedComponentDataIndex = 0;
            if (componentData != null) // null means default
                newSharedComponentDataIndex = managedComponentStore.InsertSharedComponentAssumeNonDefault(typeIndex, hashCode, componentData);

            AddSharedComponentData(chunks, newSharedComponentDataIndex, componentType);
            managedComponentStore.RemoveReference(newSharedComponentDataIndex);
        }
    }

    internal void AddSharedComponentData(NativeArray<ArchetypeChunk> chunks, int sharedComponentIndex, ComponentType componentType)
    {
        Assert.IsTrue(componentType.IsSharedComponent);
        EntityComponentStore->AssertCanAddComponent(chunks, componentType);

        if (m_IsMainThread)
            BeforeStructuralChange();
        var archetypeChanges = EntityComponentStore->BeginArchetypeChangeTracking();

        StructuralChange.AddSharedComponentChunks(EntityComponentStore, (ArchetypeChunk*)NativeArrayUnsafeUtility.GetUnsafePtr(chunks), chunks.Length, componentType.TypeIndex, sharedComponentIndex);

        EntityComponentStore->EndArchetypeChangeTracking(archetypeChanges, EntityQueryManager);
        PlaybackManagedChanges();
    }

    private void PlaybackManagedChanges()
    {
        if (!EntityComponentStore->ManagedChangesTracker.Empty)
            ManagedComponentStore.Playback(ref EntityComponentStore->ManagedChangesTracker);
    }

    public T GetSharedComponentData<T>(Entity entity, ManagedComponentStore managedComponentStore) where T : struct, ISharedComponentData
    {
        var typeIndex = TypeManager.GetTypeIndex<T>();
        EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);

        var sharedComponentIndex = EntityComponentStore->GetSharedComponentDataIndex(entity, typeIndex);
        return managedComponentStore.GetSharedComponentData<T>(sharedComponentIndex);
    }

    public void SetSharedComponentData<T>(Entity entity, T componentData, ManagedComponentStore managedComponentStore) where T : struct, ISharedComponentData
    {
        if (m_IsMainThread)
            BeforeStructuralChange();

        var typeIndex = TypeManager.GetTypeIndex<T>();
        var componentType = ComponentType.FromTypeIndex(typeIndex);
        EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);

        var newSharedComponentDataIndex = managedComponentStore.InsertSharedComponent(componentData);
        EntityComponentStore->SetSharedComponentDataIndex(entity, componentType, newSharedComponentDataIndex);
        managedComponentStore.Playback(ref EntityComponentStore->ManagedChangesTracker);
        managedComponentStore.RemoveReference(newSharedComponentDataIndex);
    }

    public void SetSharedComponentDataBoxedDefaultMustBeNull(Entity entity, int typeIndex, int hashCode, object componentData, ManagedComponentStore managedComponentStore)
    {
        if (m_IsMainThread)
            BeforeStructuralChange();

        EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);

        var newSharedComponentDataIndex = 0;
        if (componentData != null) // null means default
            newSharedComponentDataIndex = managedComponentStore.InsertSharedComponentAssumeNonDefault(typeIndex, hashCode, componentData);

        var componentType = ComponentType.FromTypeIndex(typeIndex);
        EntityComponentStore->SetSharedComponentDataIndex(entity, componentType, newSharedComponentDataIndex);
        managedComponentStore.Playback(ref EntityComponentStore->ManagedChangesTracker);
        managedComponentStore.RemoveReference(newSharedComponentDataIndex);
    }

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

        if (m_IsMainThread)
            DependencyManager->CompleteReadAndWriteDependency(typeIndex);

        BufferHeader* header =
            (BufferHeader*) EntityComponentStore->GetComponentDataWithTypeRW(entity, typeIndex,
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
        EntityComponentStore->AssertEntityHasComponent(entity, componentTypeIndex);

        if (m_IsMainThread)
            DependencyManager->CompleteReadAndWriteDependency(componentTypeIndex);

        var ptr = EntityComponentStore->GetComponentDataWithTypeRW(entity, componentTypeIndex,
            EntityComponentStore->GlobalSystemVersion);

        BufferHeader.Destroy((BufferHeader*) ptr);

        UnsafeUtility.MemCpy(ptr, tempBuffer, sizeInChunk);
    }

    public EntityArchetype GetEntityOnlyArchetype()
    {
        if (!m_EntityOnlyArchetype.Valid)
            m_EntityOnlyArchetype = CreateArchetype(null, 0);

        return m_EntityOnlyArchetype;
    }

    internal void InstantiateInternal(Entity srcEntity, Entity* outputEntities, int count)
    {
        if (m_IsMainThread)
            BeforeStructuralChange();

        EntityComponentStore->AssertEntitiesExist(&srcEntity, 1);
        EntityComponentStore->AssertCanInstantiateEntities(srcEntity, outputEntities, count);
        StructuralChange.InstantiateEntities(EntityComponentStore, &srcEntity, outputEntities, count);
        PlaybackManagedChanges();
    }

    internal void DestroyEntityInternal(Entity* entities, int count)
    {
        if (m_IsMainThread)
            BeforeStructuralChange();

        EntityComponentStore->AssertCanDestroy(entities, count);
        EntityComponentStore->DestroyEntities(entities, count);
        PlaybackManagedChanges();
    }

    public void SwapComponents(ArchetypeChunk leftChunk, int leftIndex, ArchetypeChunk rightChunk, int rightIndex)
    {
        if (m_IsMainThread)
            BeforeStructuralChange();

        var globalSystemVersion = EntityComponentStore->GlobalSystemVersion;

        ChunkDataUtility.SwapComponents(leftChunk.m_Chunk, leftIndex, rightChunk.m_Chunk, rightIndex, 1,
            globalSystemVersion, globalSystemVersion);
    }

}

static unsafe partial class EntityDataAccessManagedComponentExtensions
{
    internal static int* GetManagedComponentIndex(ref this EntityDataAccess dataAccess, Entity entity, int typeIndex)
    {
        dataAccess.EntityComponentStore->AssertEntityHasComponent(entity, typeIndex);

        if (dataAccess.IsMainThread)
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
#endif
        var ptr = dataAccess.GetManagedComponentIndex(entity, componentType.TypeIndex);
        managedComponentStore.UpdateManagedComponentValue(ptr, componentObject, ref *dataAccess.EntityComponentStore);
    }
    
}
