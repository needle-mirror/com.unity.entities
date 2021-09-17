using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Transforms;

namespace Unity.Entities.Editor
{
    [BurstCompatible]
    struct HierarchyEntityChanges : IDisposable
    {
        // Entity changes
        public NativeList<Entity> CreatedEntities;
        public NativeList<Entity> DestroyedEntities;
        
        // Scene reference component changes
        public NativeList<Entity> AddedSceneReferenceEntities;
        public NativeList<Entity> RemovedSceneReferenceEntities;
        
        // Scene reference without scene tag component changes
        public NativeList<Entity> AddedSceneReferenceWithoutSceneTagEntities;
        public NativeList<Entity> RemovedSceneReferenceWithoutSceneTagEntities;
        
        // Parent component changes
        public NativeList<Entity> AddedParentEntities;
        public NativeList<Entity> RemovedParentEntities;
        public NativeList<Parent> AddedParentComponents;
        public NativeList<Parent> RemovedParentComponents;
        
        // The scene tag component for added parent component whose value is 'Entity.Null'
        public NativeList<SceneTag> AddedParentSceneTagForNullParentComponents;
        
        // Scene tag without parent changes
        public NativeList<Entity> AddedSceneTagWithoutParentEntities;
        public NativeList<Entity> RemovedSceneTagWithoutParentEntities;
        public NativeList<SceneTag> AddedSceneTagWithoutParentComponents;
        
        // Scene tag with scene reference changes
        public NativeList<Entity> AddedSceneTagWithSceneReferenceEntities;
        public NativeList<Entity> RemovedSceneTagWithSceneReferenceEntities;
        public NativeList<SceneTag> AddedSceneTagWithSceneReferenceComponents;

        public bool HasChanges()
        {
            return !(CreatedEntities.Length == 0
                     && DestroyedEntities.Length == 0
                     && AddedSceneReferenceEntities.Length == 0
                     && RemovedSceneReferenceEntities.Length == 0
                     && AddedSceneReferenceWithoutSceneTagEntities.Length == 0
                     && RemovedSceneReferenceWithoutSceneTagEntities.Length == 0
                     && AddedParentEntities.Length == 0
                     && RemovedParentEntities.Length == 0
                     && AddedParentComponents.Length == 0
                     && RemovedParentComponents.Length == 0
                     && AddedSceneTagWithoutParentEntities.Length == 0
                     && RemovedSceneTagWithoutParentEntities.Length == 0
                     && AddedSceneTagWithoutParentComponents.Length == 0
                     && AddedSceneTagWithSceneReferenceEntities.Length == 0
                     && RemovedSceneTagWithSceneReferenceEntities.Length == 0
                     && AddedSceneTagWithSceneReferenceComponents.Length == 0);
        }

        public int GetChangeCount()
        {
            var count = 0;

            count += CreatedEntities.Length;
            count += DestroyedEntities.Length;
            count += AddedSceneReferenceEntities.Length;
            count += RemovedSceneReferenceEntities.Length;
            count += AddedSceneReferenceWithoutSceneTagEntities.Length;
            count += RemovedSceneReferenceWithoutSceneTagEntities.Length;
            count += AddedParentEntities.Length;
            count += RemovedParentEntities.Length;
            count += AddedSceneTagWithoutParentEntities.Length;
            count += RemovedSceneTagWithoutParentEntities.Length;
            count += AddedSceneTagWithSceneReferenceEntities.Length;
            count += RemovedSceneTagWithSceneReferenceEntities.Length;

            return count;
        }

        public void Clear()
        {
            CreatedEntities.Clear();
            DestroyedEntities.Clear();
            AddedSceneReferenceEntities.Clear();
            RemovedSceneReferenceEntities.Clear();
            AddedSceneReferenceWithoutSceneTagEntities.Clear();
            RemovedSceneReferenceWithoutSceneTagEntities.Clear();
            AddedParentEntities.Clear();
            RemovedParentEntities.Clear();
            AddedParentComponents.Clear();
            RemovedParentComponents.Clear();
            AddedParentSceneTagForNullParentComponents.Clear();
            AddedSceneTagWithoutParentEntities.Clear();
            RemovedSceneTagWithoutParentEntities.Clear();
            AddedSceneTagWithoutParentComponents.Clear();
            AddedSceneTagWithSceneReferenceEntities.Clear();
            RemovedSceneTagWithSceneReferenceEntities.Clear();
            AddedSceneTagWithSceneReferenceComponents.Clear();
        }

        public HierarchyEntityChanges(Allocator allocator)
        {
            CreatedEntities = new NativeList<Entity>(allocator);
            DestroyedEntities = new NativeList<Entity>(allocator);
            AddedSceneReferenceEntities = new NativeList<Entity>(allocator);
            RemovedSceneReferenceEntities = new NativeList<Entity>(allocator);
            AddedSceneReferenceWithoutSceneTagEntities = new NativeList<Entity>(allocator);
            RemovedSceneReferenceWithoutSceneTagEntities = new NativeList<Entity>(allocator);
            AddedParentEntities = new NativeList<Entity>(allocator);
            RemovedParentEntities = new NativeList<Entity>(allocator);
            AddedParentComponents = new NativeList<Parent>(allocator);
            RemovedParentComponents = new NativeList<Parent>(allocator);
            AddedParentSceneTagForNullParentComponents = new NativeList<SceneTag>(allocator);
            AddedSceneTagWithoutParentEntities = new NativeList<Entity>(allocator);
            RemovedSceneTagWithoutParentEntities = new NativeList<Entity>(allocator);
            AddedSceneTagWithoutParentComponents = new NativeList<SceneTag>(allocator);
            AddedSceneTagWithSceneReferenceEntities = new NativeList<Entity>(allocator);
            RemovedSceneTagWithSceneReferenceEntities = new NativeList<Entity>(allocator);
            AddedSceneTagWithSceneReferenceComponents = new NativeList<SceneTag>(allocator);
        }

        public void Dispose()
        {
            CreatedEntities.Dispose();
            DestroyedEntities.Dispose();
            AddedSceneReferenceEntities.Dispose();
            RemovedSceneReferenceEntities.Dispose();
            AddedSceneReferenceWithoutSceneTagEntities.Dispose();
            RemovedSceneReferenceWithoutSceneTagEntities.Dispose();
            AddedParentEntities.Dispose();
            RemovedParentEntities.Dispose();
            AddedParentComponents.Dispose();
            RemovedParentComponents.Dispose();
            AddedParentSceneTagForNullParentComponents.Dispose();
            AddedSceneTagWithoutParentEntities.Dispose();
            RemovedSceneTagWithoutParentEntities.Dispose();
            AddedSceneTagWithoutParentComponents.Dispose();
            AddedSceneTagWithSceneReferenceEntities.Dispose();
            RemovedSceneTagWithSceneReferenceEntities.Dispose();
            AddedSceneTagWithSceneReferenceComponents.Dispose();
        }
    }

    /// <summary>
    /// The <see cref="HierarchyEntityChangeTracker"/> is responsible for tracking hierarchy changes over time from the underlying data model (entity or gameObject).
    /// </summary>
    class HierarchyEntityChangeTracker : IDisposable
    {
        static readonly EntityQueryDesc k_EntityQueryDesc = new EntityQueryDesc();
        
        static readonly EntityQueryDesc k_SceneReferenceQueryDesc = new EntityQueryDesc {All = new ComponentType[] {typeof(SceneReference)}};
        static readonly EntityQueryDesc k_SceneReferenceWithoutSceneTagQueryDesc = new EntityQueryDesc {All = new ComponentType[] {typeof(SceneReference)}, None = new ComponentType[] {typeof(SceneTag)}};
        static readonly EntityQueryDesc k_ParentQueryDesc = new EntityQueryDesc {All = new ComponentType[] {typeof(Parent)}};
        static readonly EntityQueryDesc k_SceneTagWithoutParentQueryDesc = new EntityQueryDesc {All = new ComponentType[] {typeof(SceneTag)}, None = new ComponentType[] {typeof(SceneReference), typeof(Parent)}};
        static readonly EntityQueryDesc k_SceneTagWithSceneReference = new EntityQueryDesc {All = new ComponentType[] {typeof(SceneTag), typeof(SceneReference)}};

        readonly World m_World;

        /// <summary>
        /// Change trackers handle the low level entities APIs and gather a set of potentially changed data.
        /// </summary>
        readonly EntityDiffer m_EntityChangeTracker;

        readonly ComponentDataDiffer m_ParentChangeTracker;
        readonly ComponentDataDiffer m_SceneReferenceChangeTracker;
        readonly ComponentDataDiffer m_SceneReferenceWithoutSceneTagChangeTracker;
        readonly SharedComponentDataDiffer m_SceneTagWithoutParentChangeTracker;
        readonly SharedComponentDataDiffer m_SceneTagWithSceneReferenceChangeTracker;
        readonly EntityQuery m_EmptyQuery;

        EntityQueryDesc m_EntityQueryDesc;

        EntityQuery m_EntityQuery;
        EntityQuery m_ParentQuery;
        EntityQuery m_SceneReferenceQuery;
        EntityQuery m_SceneReferenceWithoutSceneTagQuery;
        EntityQuery m_SceneTagWithoutParentQuery;
        EntityQuery m_SceneTagWithSceneReference;

        NativeList<int> m_DistinctBuffer;

        /// <summary>
        /// Gets or sets the entity query used by the change tracker. 
        /// </summary>
        /// <remarks>
        /// A value of 'null' indicates the 'UniversalQuery' should be used.
        /// </remarks>
        public EntityQueryDesc EntityQueryDesc
        {
            get => m_EntityQueryDesc;
            set
            {
                if (m_EntityQueryDesc == value)
                    return;

                m_EntityQueryDesc = value;
                RebuildQueryCache(value);
            }
        }

        void RebuildQueryCache(EntityQueryDesc value)
        {
            var desc = null == value ? m_World.EntityManager.UniversalQuery.GetEntityQueryDesc() : value;

            m_EntityQuery = CreateEntityQuery(desc, k_EntityQueryDesc);
            m_ParentQuery = CreateEntityQuery(desc, k_ParentQueryDesc);
            m_SceneReferenceQuery = CreateEntityQuery(desc, k_SceneReferenceQueryDesc);
            m_SceneReferenceWithoutSceneTagQuery = CreateEntityQuery(desc, k_SceneReferenceWithoutSceneTagQueryDesc);
            m_SceneTagWithoutParentQuery = CreateEntityQuery(desc, k_SceneTagWithoutParentQueryDesc);
            m_SceneTagWithSceneReference = CreateEntityQuery(desc, k_SceneTagWithSceneReference);
        }

        public HierarchyEntityChangeTracker(World world, Allocator allocator)
        {
            m_World = world;
            m_EntityChangeTracker = new EntityDiffer(world);
            m_ParentChangeTracker = new ComponentDataDiffer(ComponentType.ReadOnly<Parent>());
            m_SceneReferenceChangeTracker = new ComponentDataDiffer(ComponentType.ReadOnly<SceneReference>());
            m_SceneReferenceWithoutSceneTagChangeTracker = new ComponentDataDiffer(ComponentType.ReadOnly<SceneReference>());
            m_SceneTagWithoutParentChangeTracker = new SharedComponentDataDiffer(ComponentType.ReadOnly<SceneTag>());
            m_SceneTagWithSceneReferenceChangeTracker = new SharedComponentDataDiffer(ComponentType.ReadOnly<SceneTag>());
            
            m_DistinctBuffer = new NativeList<int>(16, allocator);
            m_EmptyQuery = m_World.EntityManager.CreateEntityQuery(new EntityQueryDesc{None = new ComponentType[] {typeof(Entity)}});

            RebuildQueryCache(null);
        }

        public void Dispose()
        {
            m_EntityChangeTracker.Dispose();
            m_ParentChangeTracker.Dispose();
            m_SceneReferenceChangeTracker.Dispose();
            m_SceneReferenceWithoutSceneTagChangeTracker.Dispose();
            m_SceneTagWithoutParentChangeTracker.Dispose();
            m_SceneTagWithSceneReferenceChangeTracker.Dispose();
            m_DistinctBuffer.Dispose();
        }

        public HierarchyEntityChanges GetChanges(Allocator allocator)
        {
            var changes = new HierarchyEntityChanges(allocator);
            GetChanges(changes);
            return changes;
        }

        public void GetChanges(HierarchyEntityChanges changes)
        {
            changes.Clear();
            
            // Component changes can be gathered in parallel.
            var entityChangesJobHandle = m_EntityChangeTracker.GetEntityQueryMatchDiffAsync(m_EntityQuery, changes.CreatedEntities, changes.DestroyedEntities);
            var parentChanges = m_ParentChangeTracker.GatherComponentChangesAsync(m_ParentQuery, Allocator.TempJob, out var parentComponentChangesJobHandle);
            var sceneReferenceChanges = m_SceneReferenceChangeTracker.GatherComponentChangesAsync(m_SceneReferenceQuery, Allocator.TempJob, out var sceneReferenceChangesJobHandle);
            var sceneReferenceWithoutSceneTagChanges = m_SceneReferenceWithoutSceneTagChangeTracker.GatherComponentChangesAsync(m_SceneReferenceWithoutSceneTagQuery, Allocator.TempJob, out var sceneReferenceWithoutSceneTagChangesJobHandle);

            unsafe
            {
                var handles = stackalloc JobHandle[4]
                {
                    entityChangesJobHandle,
                    parentComponentChangesJobHandle,
                    sceneReferenceChangesJobHandle,
                    sceneReferenceWithoutSceneTagChangesJobHandle
                };

                Jobs.LowLevel.Unsafe.JobHandleUnsafeUtility.CombineDependencies(handles, 4).Complete();
            }
            
            // Shared component changes must be gathered synchronously.
            var sceneTagWithoutParentChanges = m_SceneTagWithoutParentChangeTracker.GatherComponentChanges(m_World.EntityManager, m_SceneTagWithoutParentQuery, Allocator.TempJob);
            var sceneTagWithSceneReferenceChanges = m_SceneTagWithSceneReferenceChangeTracker.GatherComponentChanges(m_World.EntityManager, m_SceneTagWithSceneReference, Allocator.TempJob);

            parentChanges.GetAddedComponentEntities(changes.AddedParentEntities);
            parentChanges.GetRemovedComponentEntities(changes.RemovedParentEntities);
            parentChanges.GetAddedComponentData(changes.AddedParentComponents);
            parentChanges.GetRemovedComponentData(changes.RemovedParentComponents);
                
            sceneReferenceChanges.GetAddedComponentEntities(changes.AddedSceneReferenceEntities);
            sceneReferenceChanges.GetRemovedComponentEntities(changes.RemovedSceneReferenceEntities);
                
            sceneReferenceWithoutSceneTagChanges.GetAddedComponentEntities(changes.AddedSceneReferenceWithoutSceneTagEntities);
            sceneReferenceWithoutSceneTagChanges.GetRemovedComponentEntities(changes.RemovedSceneReferenceWithoutSceneTagEntities);

            sceneTagWithoutParentChanges.GetAddedComponentEntities(changes.AddedSceneTagWithoutParentEntities);
            sceneTagWithoutParentChanges.GetRemovedComponentEntities(changes.RemovedSceneTagWithoutParentEntities);
            sceneTagWithoutParentChanges.GetAddedComponentData(changes.AddedSceneTagWithoutParentComponents);
                
            sceneTagWithSceneReferenceChanges.GetAddedComponentEntities(changes.AddedSceneTagWithSceneReferenceEntities);
            sceneTagWithSceneReferenceChanges.GetRemovedComponentEntities(changes.RemovedSceneTagWithSceneReferenceEntities);
            sceneTagWithSceneReferenceChanges.GetAddedComponentData(changes.AddedSceneTagWithSceneReferenceComponents);

            if (changes.HasChanges())
            {
                new DistinctJob
                {
                    EntityCapacity = m_World.EntityManager.EntityCapacity,
                    Changes = changes,
                    DistinctBuffer = m_DistinctBuffer
                }.Run();

                // Include the SceneTag component for null 'Parent' references.
                changes.AddedParentSceneTagForNullParentComponents.ResizeUninitialized(changes.AddedParentEntities.Length);
                
                for (var i = 0; i < changes.AddedParentEntities.Length; i++)
                {
                    var entity = changes.AddedParentEntities[i];
                    
                    if (changes.AddedParentComponents[i].Value == Entity.Null && m_World.EntityManager.HasComponent<SceneTag>(entity))
                        changes.AddedParentSceneTagForNullParentComponents[i] = m_World.EntityManager.GetSharedComponentData<SceneTag>(entity);
                    else
                        changes.AddedParentSceneTagForNullParentComponents[i] = default;
                }
            }

            parentChanges.Dispose();
            sceneReferenceChanges.Dispose();
            sceneReferenceWithoutSceneTagChanges.Dispose();
            sceneTagWithoutParentChanges.Dispose();
            sceneTagWithSceneReferenceChanges.Dispose();
        }

        [BurstCompile]
        struct DistinctJob : IJob
        {
            public int EntityCapacity;
            public HierarchyEntityChanges Changes;
            public NativeList<int> DistinctBuffer;

            public void Execute()
            {
                DistinctBuffer.ResizeUninitialized(EntityCapacity);

                if (Changes.CreatedEntities.Length > 0 && Changes.DestroyedEntities.Length > 0)
                    RemoveDuplicate(DistinctBuffer, Changes.CreatedEntities, Changes.DestroyedEntities);

                if (Changes.AddedParentEntities.Length > 0 && Changes.RemovedParentEntities.Length > 0)
                    RemoveDuplicate(DistinctBuffer, Changes.AddedParentEntities, Changes.RemovedParentEntities, Changes.AddedParentComponents, Changes.RemovedParentComponents);

                if (Changes.AddedSceneReferenceEntities.Length > 0 && Changes.RemovedSceneReferenceEntities.Length > 0)
                    RemoveDuplicate(DistinctBuffer, Changes.AddedSceneReferenceEntities, Changes.RemovedSceneReferenceEntities);

                if (Changes.AddedSceneReferenceWithoutSceneTagEntities.Length > 0 && Changes.RemovedSceneReferenceWithoutSceneTagEntities.Length > 0)
                    RemoveDuplicate(DistinctBuffer, Changes.AddedSceneReferenceWithoutSceneTagEntities, Changes.RemovedSceneReferenceWithoutSceneTagEntities);

                if (Changes.AddedSceneTagWithoutParentEntities.Length > 0 && Changes.RemovedSceneTagWithoutParentEntities.Length > 0)
                    RemoveDuplicate(DistinctBuffer, Changes.AddedSceneTagWithoutParentEntities, Changes.RemovedSceneTagWithoutParentEntities, Changes.AddedSceneTagWithoutParentComponents);

                if (Changes.AddedSceneTagWithSceneReferenceEntities.Length > 0 && Changes.RemovedSceneTagWithSceneReferenceEntities.Length > 0)
                    RemoveDuplicate(DistinctBuffer, Changes.AddedSceneTagWithSceneReferenceEntities, Changes.RemovedSceneTagWithSceneReferenceEntities, Changes.AddedSceneTagWithSceneReferenceComponents);
            }

            static unsafe void RemoveDuplicate(NativeList<int> index, NativeList<Entity> added, NativeList<Entity> removed)
            {
                UnsafeUtility.MemClear(index.GetUnsafePtr(), index.Length * sizeof(int));

                var addedLength = added.Length;
                var removedLength = removed.Length;

                for (var i = 0; i < addedLength; i++)
                    index[added[i].Index] = i + 1;

                for (var i = 0; i < removedLength; i++)
                {
                    var addIndex = index[removed[i].Index] - 1;

                    if (addIndex < 0)
                        continue;

                    // An entity was recorded as added AND removed with the same index.
                    var addedEntity = added[addIndex];
                    var removedEntity = removed[i];

                    if (addedEntity.Version != removedEntity.Version)
                        continue;

                    // Swap back
                    added[addIndex] = added[addedLength - 1];
                    removed[i] = removed[removedLength - 1];

                    index[added[addIndex].Index] = addIndex + 1;

                    addedLength--;
                    removedLength--;
                    i--;
                }

                added.ResizeUninitialized(addedLength);
                removed.ResizeUninitialized(removedLength);
            }

            unsafe void RemoveDuplicate<TData>(NativeArray<int> index, NativeList<Entity> added, NativeList<Entity> removed, NativeList<TData> data) where TData : unmanaged
            {
                UnsafeUtility.MemClear(index.GetUnsafePtr(), index.Length * sizeof(int));

                var addedLength = added.Length;
                var removedLength = removed.Length;

                for (var i = 0; i < addedLength; i++)
                    index[added[i].Index] = i + 1;

                for (var i = 0; i < removedLength; i++)
                {
                    var addIndex = index[removed[i].Index] - 1;

                    if (addIndex < 0)
                        continue;

                    // An entity was recorded as added AND removed with the same index.
                    var addedEntity = added[addIndex];
                    var removedEntity = removed[i];

                    if (addedEntity.Version != removedEntity.Version)
                        continue;

                    // Swap back
                    added[addIndex] = added[addedLength - 1];
                    data[addIndex] = data[addedLength - 1];
                    removed[i] = removed[removedLength - 1];

                    index[added[addIndex].Index] = addIndex + 1;

                    addedLength--;
                    removedLength--;
                }

                added.ResizeUninitialized(addedLength);
                data.ResizeUninitialized(addedLength);
                removed.ResizeUninitialized(removedLength);
            }
            
            unsafe void RemoveDuplicate<TData>(NativeArray<int> index, NativeList<Entity> addedEntities, NativeList<Entity> removedEntities, NativeList<TData> addedData, NativeList<TData> removedData) where TData : unmanaged
            {
                UnsafeUtility.MemClear(index.GetUnsafePtr(), index.Length * sizeof(int));

                var addedLength = addedEntities.Length;
                var removedLength = removedEntities.Length;

                for (var i = 0; i < addedLength; i++)
                    index[addedEntities[i].Index] = i + 1;

                for (var i = 0; i < removedLength; i++)
                {
                    var addIndex = index[removedEntities[i].Index] - 1;

                    if (addIndex < 0)
                        continue;

                    var a = addedData[addIndex];
                    var b = removedData[i];

                    // Only filter if the data is the same.
                    if (UnsafeUtility.MemCmp(&a, &b, sizeof(TData)) != 0)
                        continue;
                
                    // An entity was recorded as added AND removed with the same index.
                    var addedEntity = addedEntities[addIndex];
                    var removedEntity = removedEntities[i];

                    if (addedEntity.Version != removedEntity.Version)
                        continue;

                    // Swap back
                    addedEntities[addIndex] = addedEntities[addedLength - 1];
                    addedData[addIndex] = addedData[addedLength - 1];
                    removedEntities[i] = removedEntities[removedLength - 1];
                    removedData[i] = removedData[removedLength - 1];

                    index[addedEntities[addIndex].Index] = addIndex + 1;

                    addedLength--;
                    removedLength--;
                }

                addedEntities.ResizeUninitialized(addedLength);
                addedData.ResizeUninitialized(addedLength);
                removedEntities.ResizeUninitialized(removedLength);
            }
        }
        
        EntityQuery CreateEntityQuery(params EntityQueryDesc[] queriesDesc)
        {
            try
            {
                ValidateEntityQueryDesc(queriesDesc);
            }
            catch
            {
                return m_EmptyQuery;
            }
            
            using var builder = new EntityQueryDescBuilder(Allocator.Temp);
            
            for (var q = 0; q != queriesDesc.Length; q++)
            {
                foreach (var type in queriesDesc[q].All)
                    builder.AddAll(type);

                foreach (var type in queriesDesc[q].Any)
                    builder.AddAny(type);

                foreach (var type in queriesDesc[q].None)
                    builder.AddNone(type);
            }
            
            builder.Options(queriesDesc[0].Options);
            builder.FinalizeQuery();
            
            return m_World.EntityManager.CreateEntityQuery(builder);
        }

        static void ValidateEntityQueryDesc(params EntityQueryDesc[] queriesDesc)
        {
            var count = 0;
            
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var entityQueryDesc in queriesDesc)
                count += entityQueryDesc.None.Length + entityQueryDesc.All.Length + entityQueryDesc.Any.Length;

            var componentTypeIds = new NativeArray<int>(count, Allocator.Temp);
            
            var componentIndex = 0;

            foreach (var entityQueryDesc in queriesDesc)
            {
                ValidateComponentTypes(entityQueryDesc.None, ref componentTypeIds, ref componentIndex);
                ValidateComponentTypes(entityQueryDesc.All, ref componentTypeIds, ref componentIndex);
                ValidateComponentTypes(entityQueryDesc.Any, ref componentTypeIds, ref componentIndex);
            }

            // Check for duplicate, only if necessary
            if (count > 1)
            {
                // Sort the Ids to have identical value adjacent
                componentTypeIds.Sort();

                // Check for identical values
                var refId = componentTypeIds[0];
                
                for (var i = 1; i < componentTypeIds.Length; i++)
                {
                    var curId = componentTypeIds[i];
                    if (curId == refId)
                    {
                        var compType = TypeManager.GetType(curId);
                        throw new EntityQueryDescValidationException(
                            $"EntityQuery contains a filter with duplicate component type name {compType.Name}.  Queries can only contain a single component of a given type in a filter.");
                    }

                    refId = curId;
                }
            }

            componentTypeIds.Dispose();
        }
        
        static void ValidateComponentTypes(ComponentType[] componentTypes, ref NativeArray<int> allComponentTypeIds, ref int curComponentTypeIndex)
        {
            foreach (var componentType in componentTypes)
            {
                allComponentTypeIds[curComponentTypeIndex++] = componentType.TypeIndex;
                
                if (componentType.AccessModeType == ComponentType.AccessMode.Exclude)
                    throw new ArgumentException("EntityQueryDesc cannot contain Exclude Component types");
            }
        }
    }
}