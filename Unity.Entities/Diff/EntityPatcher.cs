using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
#if !NET_DOTS
using Unity.Properties;
#endif

namespace Unity.Entities
{
    public static unsafe partial class EntityPatcher
    {
        static Profiling.ProfilerMarker s_ApplyChangeSetProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.ApplyChangeSet");
        static Profiling.ProfilerMarker s_BuildEntityLookupsProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.BuildEntityLookups");
        static Profiling.ProfilerMarker s_BuildPackedLookupsProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.BuildPackedLookups");
        static Profiling.ProfilerMarker s_ApplyDestroyEntitiesProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.ApplyDestroyEntities");
        static Profiling.ProfilerMarker s_ApplyCreateEntitiesProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.ApplyCreateEntities");
        static Profiling.ProfilerMarker s_ApplyEntityNamesProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.ApplyEntityNames");
        static Profiling.ProfilerMarker s_ApplyRemoveComponentsProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.ApplyRemoveComponents");
        static Profiling.ProfilerMarker s_ApplyAddComponentsProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.ApplyAddComponents");
        static Profiling.ProfilerMarker s_ApplySetSharedComponentsProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.ApplySetSharedComponents");
        static Profiling.ProfilerMarker s_ApplySetManagedComponentsProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.ApplySetManagedComponents");
        static Profiling.ProfilerMarker s_ApplySetComponentsProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.ApplySetComponents");
        static Profiling.ProfilerMarker s_BuildPrefabAndLinkedEntityGroupLookupsProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.BuildPrefabAndLinkedEntityGroupLookups");
        static Profiling.ProfilerMarker s_ApplyLinkedEntityGroupRemovalsProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.ApplyLinkedEntityGroupRemovals");
        static Profiling.ProfilerMarker s_ApplyLinkedEntityGroupAdditionsProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.ApplyLinkedEntityGroupAdditions");
        static Profiling.ProfilerMarker s_ApplyEntityPatchesProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.ApplyEntityPatches");
        static Profiling.ProfilerMarker s_ApplyBlobAssetChangesProfilerMarker = new Profiling.ProfilerMarker("EntityPatcher.ApplyBlobAssetChanges");

        static EntityQueryDesc EntityGuidQueryDesc { get; } = new EntityQueryDesc
        {
            All = new ComponentType[]
            {
                typeof(EntityGuid)
            },
            Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab
        };

        static EntityQueryDesc PrefabQueryDesc { get; } = new EntityQueryDesc
        {
            All = new ComponentType[]
            {
                typeof(EntityGuid), typeof(Prefab)
            },
            Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab
        };

        static EntityQueryDesc LinkedEntityGroupQueryDesc { get; } = new EntityQueryDesc
        {
            All = new ComponentType[]
            {
                typeof(EntityGuid), typeof(LinkedEntityGroup)
            },
            Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab
        };

        // Restore to BuildComponentToEntityMultiHashMap<TComponent> once fix goes in:
        // https://unity3d.atlassian.net/browse/DOTSR-354
        [BurstCompile]
        struct BuildComponentToEntityMultiHashMap : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<EntityGuid> ComponentTypeHandle;
            [ReadOnly] public EntityTypeHandle EntityTypeHandle;

            [WriteOnly] public NativeMultiHashMap<EntityGuid, Entity>.ParallelWriter ComponentToEntity;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var components = batchInChunk.GetNativeArray(ComponentTypeHandle);
                var entities = batchInChunk.GetNativeArray(EntityTypeHandle);
                for (var i = 0; i != entities.Length; i++)
                {
                    ComponentToEntity.Add(components[i], entities[i]);
                }
            }
        }

        // Restore to BuildComponentToEntityMultiHashMap<TComponent> once fix goes in:
        // https://unity3d.atlassian.net/browse/DOTSR-354
        [BurstCompile]
        struct BuildComponentToEntityHashMap : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<EntityGuid> ComponentTypeHandle;
            [ReadOnly] public EntityTypeHandle EntityTypeHandle;

            [WriteOnly] public NativeHashMap<EntityGuid, Entity>.ParallelWriter ComponentToEntity;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var components = batchInChunk.GetNativeArray(ComponentTypeHandle);
                var entities = batchInChunk.GetNativeArray(EntityTypeHandle);
                for (var i = 0; i != entities.Length; i++)
                {
                    ComponentToEntity.TryAdd(components[i], entities[i]);
                }
            }
        }

        // Restore to BuildComponentToEntityMultiHashMap<TComponent> once fix goes in:
        // https://unity3d.atlassian.net/browse/DOTSR-354
        [BurstCompile]
        struct BuildEntityToComponentHashMap : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<EntityGuid> EntityGuidComponentTypeHandle;
            [ReadOnly] public EntityTypeHandle EntityTypeHandle;

            [WriteOnly] public NativeHashMap<Entity, EntityGuid>.ParallelWriter EntityToEntityGuid;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var components = batchInChunk.GetNativeArray(EntityGuidComponentTypeHandle);
                var entities = batchInChunk.GetNativeArray(EntityTypeHandle);
                for (var i = 0; i != entities.Length; i++)
                {
                    EntityToEntityGuid.TryAdd(entities[i], components[i]);
                }
            }
        }

        [BurstCompile]
        struct CalculateLinkedEntityGroupEntitiesLengthJob : IJob
        {
            [NativeDisableUnsafePtrRestriction] public int* Count;
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;
            [ReadOnly] public BufferTypeHandle<LinkedEntityGroup> LinkedEntityGroupTypeHandle;

            public void Execute()
            {
                var count = 0;
                for (var chunkIndex = 0; chunkIndex < Chunks.Length; chunkIndex++)
                {
                    var linkedEntityGroups = Chunks[chunkIndex].GetBufferAccessor(LinkedEntityGroupTypeHandle);
                    for (var linkedEntityGroupIndex = 0; linkedEntityGroupIndex < linkedEntityGroups.Length; linkedEntityGroupIndex++)
                    {
                        count += linkedEntityGroups[linkedEntityGroupIndex].Length;
                    }
                }

                *Count = count;
            }
        }

        [BurstCompile]
        struct BuildLinkedEntityGroupHashMap : IJobEntityBatch
        {
            [WriteOnly] public NativeHashMap<Entity, Entity>.ParallelWriter EntityToLinkedEntityGroupRoot;
            [ReadOnly] public BufferTypeHandle<LinkedEntityGroup> LinkedEntityGroupTypeHandle;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var linkedEntityGroups = batchInChunk.GetBufferAccessor(LinkedEntityGroupTypeHandle);

                for (var bufferIndex = 0; bufferIndex != linkedEntityGroups.Length; bufferIndex++)
                {
                    var linkedEntityGroup = linkedEntityGroups[bufferIndex];
                    for (var elementIndex = 0; elementIndex != linkedEntityGroup.Length; elementIndex++)
                    {
                        EntityToLinkedEntityGroupRoot.TryAdd(linkedEntityGroup[elementIndex].Value, linkedEntityGroup[0].Value);
                    }
                }
            }
        }

        [BurstCompile]
        struct BuildPackedEntityLookupJob : IJobParallelFor
        {
            public int StartIndex;
            [ReadOnly] public NativeArray<EntityGuid> EntityGuids;
            [ReadOnly] public NativeMultiHashMap<EntityGuid, Entity> EntityGuidToEntity;
            [WriteOnly] public NativeMultiHashMap<int, Entity>.ParallelWriter PackedEntities;

            public void Execute(int index)
            {
                var entityGuid = EntityGuids[index + StartIndex];
                if (EntityGuidToEntity.TryGetFirstValue(entityGuid, out var entity, out var iterator))
                {
                    do
                    {
                        PackedEntities.Add(index + StartIndex, entity);
                    }
                    while (EntityGuidToEntity.TryGetNextValue(out entity, ref iterator));
                }
            }
        }

        struct BuildPackedTypeLookupJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ComponentTypeHash> TypeHashes;
            [WriteOnly] public NativeArray<ComponentType> PackedTypes;

            public void Execute(int index)
            {
                var typeHash = TypeHashes[index];
                var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(typeHash.StableTypeHash);
                var type = TypeManager.GetType(typeIndex);
                ComponentType componentType;
                if ((typeHash.Flags & ComponentTypeFlags.ChunkComponent) == ComponentTypeFlags.ChunkComponent)
                {
                    componentType = ComponentType.ChunkComponent(type);
                }
                else
                {
                    componentType = new ComponentType(type);
                }
                PackedTypes[index] = componentType;
            }
        }

        /// <summary>
        /// Applies the given change set to the given entity manager.
        /// </summary>
        /// <param name="entityManager">The <see cref="EntityManager"/> to apply the change set to.</param>
        /// <param name="changeSet">The <see cref="EntityChangeSet"/> to apply.</param>
        public static void ApplyChangeSet(EntityManager entityManager, EntityChangeSet changeSet)
        {
            if (!changeSet.IsCreated)
            {
                return;
            }

            s_ApplyChangeSetProfilerMarker.Begin();

            var entityQuery = entityManager.CreateEntityQuery(EntityGuidQueryDesc);
            var prefabQuery = entityManager.CreateEntityQuery(PrefabQueryDesc);
            var linkedEntityGroupQuery = entityManager.CreateEntityQuery(LinkedEntityGroupQueryDesc);

            var entityCount = entityQuery.CalculateEntityCount();

            using (var packedEntities = new NativeMultiHashMap<int, Entity>(entityCount, Allocator.TempJob))
            using (var packedTypes = new NativeArray<ComponentType>(changeSet.TypeHashes.Length, Allocator.TempJob))
            using (var entityGuidToEntity = new NativeMultiHashMap<EntityGuid, Entity>(entityCount, Allocator.TempJob))
            using (var entityToEntityGuid = new NativeHashMap<Entity, EntityGuid>(entityQuery.CalculateEntityCount(), Allocator.TempJob))
            {
                BuildEntityLookups(
                    entityManager,
                    entityQuery,
                    entityGuidToEntity,
                    entityToEntityGuid);

                BuildPackedLookups(
                    changeSet,
                    entityGuidToEntity,
                    packedEntities,
                    packedTypes);

                ApplyDestroyEntities(
                    entityManager,
                    changeSet,
                    packedEntities,
                    entityGuidToEntity);

                ApplyCreateEntities(
                    entityManager,
                    changeSet,
                    packedEntities);

#if UNITY_EDITOR
                ApplyEntityNames(
                    entityManager,
                    changeSet,
                    packedEntities);
#endif

                ApplyRemoveComponents(
                    entityManager,
                    changeSet.RemoveComponents,
                    changeSet.Entities,
                    packedEntities,
                    packedTypes);

                ApplyAddComponents(
                    entityManager,
                    changeSet.AddComponents,
                    changeSet.Entities,
                    packedEntities,
                    packedTypes);

                ApplySetSharedComponents(
                    entityManager,
                    changeSet.SetSharedComponents,
                    changeSet.Entities,
                    packedEntities,
                    packedTypes);

                ApplySetManagedComponents(
                    entityManager,
                    changeSet.SetManagedComponents,
                    changeSet.Entities,
                    packedEntities,
                    packedTypes);

                ApplySetComponents(
                    entityManager,
                    changeSet.SetComponents,
                    changeSet.ComponentData,
                    changeSet.Entities,
                    packedEntities,
                    packedTypes,
                    entityGuidToEntity,
                    entityToEntityGuid);

                var linkedEntityGroupEntitiesLength = CalculateLinkedEntityGroupEntitiesLength(entityManager, linkedEntityGroupQuery);

                using (var entityGuidToPrefab = new NativeHashMap<EntityGuid, Entity>(prefabQuery.CalculateEntityCount(), Allocator.TempJob))
                using (var entityToLinkedEntityGroupRoot = new NativeHashMap<Entity, Entity>(linkedEntityGroupEntitiesLength, Allocator.TempJob))
                {
                    BuildPrefabAndLinkedEntityGroupLookups(
                        entityManager,
                        entityQuery,
                        prefabQuery,
                        linkedEntityGroupQuery,
                        entityGuidToPrefab,
                        entityToLinkedEntityGroupRoot);

                    ApplyLinkedEntityGroupRemovals(
                        entityManager,
                        changeSet.LinkedEntityGroupRemovals,
                        changeSet.Entities,
                        packedEntities,
                        entityGuidToEntity,
                        entityToEntityGuid,
                        entityToLinkedEntityGroupRoot);

                    ApplyLinkedEntityGroupAdditions(
                        entityManager,
                        changeSet.LinkedEntityGroupAdditions,
                        changeSet.Entities,
                        packedEntities,
                        entityGuidToEntity,
                        entityToEntityGuid,
                        entityGuidToPrefab,
                        entityToLinkedEntityGroupRoot);

                    ApplyEntityPatches(
                        entityManager,
                        changeSet.EntityReferenceChanges,
                        changeSet.Entities,
                        packedEntities,
                        packedTypes,
                        entityGuidToEntity,
                        entityToEntityGuid,
                        entityGuidToPrefab,
                        entityToLinkedEntityGroupRoot);
                }

                ApplyBlobAssetChanges(
                    entityManager,
                    changeSet.Entities,
                    packedEntities,
                    packedTypes,
                    changeSet.CreatedBlobAssets,
                    changeSet.BlobAssetData,
                    changeSet.DestroyedBlobAssets,
                    changeSet.BlobAssetReferenceChanges);
            }
            s_ApplyChangeSetProfilerMarker.End();
        }

        /// <summary>
        /// Builds a lookup of <see cref="NativeMultiHashMap{TEntityGuidComponent, Entity}"/> for the target world.
        /// </summary>
        /// <remarks>
        /// This will run over ALL entities in the world. This is very expensive.
        /// </remarks>
        static void BuildEntityLookups(
            EntityManager entityManager,
            EntityQuery entityQuery,
            NativeMultiHashMap<EntityGuid, Entity> entityGuidToEntity,
            NativeHashMap<Entity, EntityGuid> entityToEntityGuid)
        {
            s_BuildEntityLookupsProfilerMarker.Begin();
            var buildEntityGuidToEntity = new BuildComponentToEntityMultiHashMap
            {
                EntityTypeHandle = entityManager.GetEntityTypeHandle(),
                ComponentTypeHandle = entityManager.GetComponentTypeHandle<EntityGuid>(true),
                ComponentToEntity = entityGuidToEntity.AsParallelWriter()
            }.ScheduleParallel(entityQuery, 1);

            var buildEntityToEntityGuid = new BuildEntityToComponentHashMap
            {
                EntityTypeHandle = entityManager.GetEntityTypeHandle(),
                EntityGuidComponentTypeHandle = entityManager.GetComponentTypeHandle<EntityGuid>(true),
                EntityToEntityGuid = entityToEntityGuid.AsParallelWriter()
            }.ScheduleParallel(entityQuery, 1);

            JobHandle.CombineDependencies(buildEntityGuidToEntity, buildEntityToEntityGuid).Complete();
            s_BuildEntityLookupsProfilerMarker.End();
        }

        static void BuildPrefabAndLinkedEntityGroupLookups(
            EntityManager entityManager,
            EntityQuery entityQuery,
            EntityQuery prefabQuery,
            EntityQuery linkedEntityGroupQuery,
            NativeHashMap<EntityGuid, Entity> entityGuidToPrefab,
            NativeHashMap<Entity, Entity> entityToLinkedEntityGroupRoot)
        {
            s_BuildPrefabAndLinkedEntityGroupLookupsProfilerMarker.Begin();
            var buildPrefabLookups = new BuildComponentToEntityHashMap
            {
                EntityTypeHandle = entityManager.GetEntityTypeHandle(),
                ComponentTypeHandle = entityManager.GetComponentTypeHandle<EntityGuid>(true),
                ComponentToEntity = entityGuidToPrefab.AsParallelWriter()
            }.ScheduleParallel(prefabQuery, 1);

            var buildLinkedEntityGroupLookups = new BuildLinkedEntityGroupHashMap
            {
                EntityToLinkedEntityGroupRoot = entityToLinkedEntityGroupRoot.AsParallelWriter(),
                LinkedEntityGroupTypeHandle = entityManager.GetBufferTypeHandle<LinkedEntityGroup>(true)
            }.ScheduleParallel(linkedEntityGroupQuery, 1);

            JobHandle.CombineDependencies(buildPrefabLookups, buildLinkedEntityGroupLookups).Complete();
            s_BuildPrefabAndLinkedEntityGroupLookupsProfilerMarker.End();
        }

        /// <summary>
        /// This method will generate lookups into the packed change set.
        ///
        /// 1) Maps existing entities in the world to <see cref="EntityChangeSet.Entities"/>
        /// 2) Maps types in the world to <see cref="EntityChangeSet.TypeHashes"/>
        ///
        /// These tables are used by subsequent methods to quickly access the packed data.
        /// </summary>
        static void BuildPackedLookups(
            EntityChangeSet changeSet,
            NativeMultiHashMap<EntityGuid, Entity> entityGuidToEntity,
            NativeMultiHashMap<int, Entity> packedEntities,
            NativeArray<ComponentType> packedTypes)
        {
            s_BuildPackedLookupsProfilerMarker.Begin();
            var buildPackedEntityLookups = new BuildPackedEntityLookupJob
            {
                StartIndex = changeSet.CreatedEntityCount,
                EntityGuids = changeSet.Entities,
                EntityGuidToEntity = entityGuidToEntity,
                PackedEntities = packedEntities.AsParallelWriter()
            }.Schedule(changeSet.Entities.Length - changeSet.CreatedEntityCount, 64);

            var buildPackedTypeLookups = new BuildPackedTypeLookupJob
            {
                TypeHashes = changeSet.TypeHashes,
                PackedTypes = packedTypes,
            }.Schedule(changeSet.TypeHashes.Length, 64);

            JobHandle.CombineDependencies(buildPackedEntityLookups, buildPackedTypeLookups).Complete();
            s_BuildPackedLookupsProfilerMarker.End();
        }

        /// <summary>
        /// Creates all new entities described in the <see cref="EntityChangeSet"/>
        /// </summary>
        /// <remarks>
        /// This method only creates the entities and does not set any data.
        /// </remarks>
        static void ApplyCreateEntities(
            EntityManager entityManager,
            EntityChangeSet changeSet,
            NativeMultiHashMap<int, Entity> packedEntities)
        {
            s_ApplyCreateEntitiesProfilerMarker.Begin();
            var entityGuidArchetype = entityManager.CreateArchetype(null, 0);
            using (var entities = new NativeArray<Entity>(changeSet.CreatedEntityCount, Allocator.Temp))
            {
                entityManager.CreateEntity(entityGuidArchetype, entities);
                for (var i = 0; i < changeSet.CreatedEntityCount; ++i)
                {
                    packedEntities.Add(i, entities[i]);
                }
            }
            s_ApplyCreateEntitiesProfilerMarker.End();
        }

#if UNITY_EDITOR
        static void ApplyEntityNames(
            EntityManager entityManager,
            EntityChangeSet changeSet,
            NativeMultiHashMap<int, Entity> packedEntities)
        {
            s_ApplyEntityNamesProfilerMarker.Begin();
            for (var i = 0; i < changeSet.Entities.Length; i++)
            {
                if (packedEntities.TryGetFirstValue(i, out var entity, out var it))
                {
                    do
                    {
                        entityManager.SetName(entity, changeSet.Names[i].ToString());
                    }
                    while (packedEntities.TryGetNextValue(out entity, ref it));
                }
            }
            s_ApplyEntityNamesProfilerMarker.End();
        }

#endif

        /// <summary>
        /// Destroys all entities described in the <see cref="EntityChangeSet"/>
        /// </summary>
        /// <remarks>
        /// Since building the <see cref="NativeMultiHashMap{TEntityGuidComponent, Entity}"/> the entire world is expensive
        /// this method will incrementally update the map based on the destroyed entities.
        /// </remarks>
        static void ApplyDestroyEntities(
            EntityManager entityManager,
            EntityChangeSet changeSet,
            NativeMultiHashMap<int, Entity> packedEntities,
            NativeMultiHashMap<EntityGuid, Entity> entityGuidToEntity)
        {
            s_ApplyDestroyEntitiesProfilerMarker.Begin();
            var linkedEntityGroupType = ComponentType.ReadOnly<LinkedEntityGroup>();
            var s = entityManager.GetCheckedEntityDataAccess();
            for (var i = changeSet.Entities.Length - changeSet.DestroyedEntityCount; i < changeSet.Entities.Length; i++)
            {
                if (!packedEntities.TryGetFirstValue(i, out var entity, out var iterator))
                {
                    continue;
                }

                do
                {
                    // Perform incremental updates on the entityGuidToEntity map to avoid a full rebuild.
                    // @NOTE We do NOT remove from the `entityToEntityGuid` here since the LinkedEntityGroup removal will need it to map back groups.
                    entityGuidToEntity.Remove(changeSet.Entities[i], entity);

                    // It's possible that this entity has already been deleted. This can happen in two different scenarios:
                    //  - the change set is inconsistent with the state of the world. That means that the world has diverged.
                    //  - the entity was part of a LinkedEntityGroup that has already been destroyed earlier while applying this patch
                    if (s->Exists(entity))
                    {
                        // We need to remove the linked entity group component before destroying the entity, because
                        // otherwise we cannot handle the case when an entity has its linked entity group modified and
                        // is then destroyed: The Differ won't pick up the changes to the linked entity group, which
                        // means that the patcher operates on a stale linked entity group and deletes too many entities.
                        s->RemoveComponent(entity, linkedEntityGroupType);
                        s->DestroyEntity(entity);
                    }
                }
                while (packedEntities.TryGetNextValue(out entity, ref iterator));
            }
            s_ApplyDestroyEntitiesProfilerMarker.End();
        }

        static void ApplyAddComponents(
            EntityManager entityManager,
            NativeArray<PackedComponent> addComponents,
            NativeArray<EntityGuid> packedEntityGuids,
            NativeMultiHashMap<int, Entity> packedEntities,
            NativeArray<ComponentType> packedTypes)
        {
            s_ApplyAddComponentsProfilerMarker.Begin();
            var s = entityManager.GetCheckedEntityDataAccess();
            var linkedEntityGroupTypeIndex = TypeManager.GetTypeIndex<LinkedEntityGroup>();

            for (var i = 0; i < addComponents.Length; i++)
            {
                var packedComponent = addComponents[i];

                if (!packedEntities.TryGetFirstValue(packedComponent.PackedEntityIndex, out var entity, out var iterator))
                {
                    continue;
                }

                var component = packedTypes[packedComponent.PackedTypeIndex];

                do
                {
                    if (!s->HasComponent(entity, component))
                    {
                        s->AddComponent(entity, component);

                        // magic is required to force the first entity in the LinkedEntityGroup to be the entity
                        // that owns the component. this magic doesn't seem to exist at a lower level, so let's
                        // shim it in here. we'll probably need to move the magic lower someday.
                        if (component.TypeIndex == linkedEntityGroupTypeIndex)
                        {
                            var buffer = entityManager.GetBuffer<LinkedEntityGroup>(entity);
                            buffer.Add(entity);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"AddComponent({packedEntityGuids[packedComponent.PackedEntityIndex]}, {component}) but the component already exists.");
                    }
                }
                while (packedEntities.TryGetNextValue(out entity, ref iterator));
            }
            s_ApplyAddComponentsProfilerMarker.End();
        }

        static void ApplyRemoveComponents(
            EntityManager entityManager,
            NativeArray<PackedComponent> removeComponents,
            NativeArray<EntityGuid> packedEntityGuids,
            NativeMultiHashMap<int, Entity> packedEntities,
            NativeArray<ComponentType> packedTypes)
        {
            s_ApplyRemoveComponentsProfilerMarker.Begin();
            var entityGuidTypeIndex = TypeManager.GetTypeIndex<EntityGuid>();
            var access = entityManager.GetCheckedEntityDataAccess();

            for (var i = 0; i < removeComponents.Length; i++)
            {
                var packedComponent = removeComponents[i];

                if (!packedEntities.TryGetFirstValue(packedComponent.PackedEntityIndex, out var entity, out var iterator))
                {
                    continue;
                }

                var component = packedTypes[packedComponent.PackedTypeIndex];

                do
                {
                    if (component.TypeIndex == entityGuidTypeIndex)
                    {
                        // @TODO Add test cases around this.
                        // Should entityGuidToEntity be updated or should we throw and error.
                    }

                    if (access->HasComponent(entity, component))
                    {
                        access->RemoveComponent(entity, component);
                    }
                    else
                    {
                        Debug.LogWarning($"RemoveComponent({packedEntityGuids[packedComponent.PackedEntityIndex]}, {component}) but the component was already removed.");
                    }
                }
                while (packedEntities.TryGetNextValue(out entity, ref iterator));
            }
            s_ApplyRemoveComponentsProfilerMarker.End();
        }

        static void ApplySetSharedComponents(
            EntityManager entityManager,
            PackedSharedComponentDataChange[] sharedComponentDataChanges,
            NativeArray<EntityGuid> packedEntityGuid,
            NativeMultiHashMap<int, Entity> packedEntities,
            NativeArray<ComponentType> packedTypes)
        {
            s_ApplySetSharedComponentsProfilerMarker.Begin();
            for (var i = 0; i < sharedComponentDataChanges.Length; i++)
            {
                var packedSharedComponentDataChange = sharedComponentDataChanges[i];
                var packedComponent = packedSharedComponentDataChange.Component;

                if (!packedEntities.TryGetFirstValue(packedComponent.PackedEntityIndex, out var entity, out var iterator))
                {
                    continue;
                }

                var component = packedTypes[packedComponent.PackedTypeIndex];

                do
                {
                    if (!entityManager.Exists(entity))
                    {
                        Debug.LogWarning($"SetComponent<{component}>({packedEntityGuid[packedComponent.PackedEntityIndex]}) but entity does not exist.");
                    }
                    else if (!entityManager.HasComponent(entity, component))
                    {
                        Debug.LogWarning($"SetComponent<{component}>({packedEntityGuid[packedComponent.PackedEntityIndex]}) but component does not exist.");
                    }
                    else
                    {
                        entityManager.SetSharedComponentDataBoxedDefaultMustBeNull(entity, component.TypeIndex, packedSharedComponentDataChange.BoxedSharedValue);
                    }
                }
                while (packedEntities.TryGetNextValue(out entity, ref iterator));
            }
            s_ApplySetSharedComponentsProfilerMarker.End();
        }

        static void ApplySetManagedComponents(
            EntityManager entityManager,
            PackedManagedComponentDataChange[] managedComponentDataChanges,
            NativeArray<EntityGuid> packedEntityGuid,
            NativeMultiHashMap<int, Entity> packedEntities,
            NativeArray<ComponentType> packedTypes)
        {
            s_ApplySetManagedComponentsProfilerMarker.Begin();
            var entitiesWithCompanionLink = new NativeList<Entity>(Allocator.Temp);
            var managedObjectClone = new ManagedObjectClone();

            for (var i = 0; i < managedComponentDataChanges.Length; i++)
            {
                var packedManagedComponentDataChange = managedComponentDataChanges[i];
                var packedComponent = packedManagedComponentDataChange.Component;

                if (!packedEntities.TryGetFirstValue(packedComponent.PackedEntityIndex, out var entity, out var iterator))
                {
                    continue;
                }

                var component = packedTypes[packedComponent.PackedTypeIndex];

                do
                {
                    if (!entityManager.Exists(entity))
                    {
                        Debug.LogWarning($"SetComponent<{component}>({packedEntityGuid[packedComponent.PackedEntityIndex]}) but entity does not exist.");
                    }
                    else if (!entityManager.HasComponent(entity, component))
                    {
                        Debug.LogWarning($"SetComponent<{component}>({packedEntityGuid[packedComponent.PackedEntityIndex]}) but component does not exist.");
                    }
                    else
                    {
                        var clone = managedObjectClone.Clone(packedManagedComponentDataChange.BoxedValue);
                        entityManager.SetComponentObject(entity, component, clone);

                        if (component.TypeIndex == ManagedComponentStore.CompanionLinkTypeIndex)
                        {
                            entitiesWithCompanionLink.Add(entity);
                        }
                    }
                }
                while (packedEntities.TryGetNextValue(out entity, ref iterator));
            }

            var entitiesWithCompanionLinkCount = entitiesWithCompanionLink.Length;
            if (entitiesWithCompanionLinkCount > 0)
            {
                ManagedComponentStore.AssignHybridComponentsToCompanionGameObjects(entityManager, entitiesWithCompanionLink.AsArray());
            }
            s_ApplySetManagedComponentsProfilerMarker.End();
        }

        struct SetComponentError
        {
            public ComponentType ComponentType;
            public EntityGuid Guid;
        }

        [BurstCompile]
        struct ApplySetComponentsJob : IJob
        {
            public EntityManager EntityManager;
            [ReadOnly]
            public NativeArray<PackedComponentDataChange> Changes;
            [ReadOnly]
            public NativeArray<byte> Payload;
            [ReadOnly]
            public NativeArray<EntityGuid> PackedEntityGuids;
            [ReadOnly]
            public NativeMultiHashMap<int, Entity> PackedEntities;
            [ReadOnly]
            public NativeArray<ComponentType> PackedTypes;

            public NativeMultiHashMap<EntityGuid, Entity> EntityGuidToEntity;
            public NativeHashMap<Entity, EntityGuid> EntityToEntityGuid;

            public NativeList<SetComponentError> EntityDoesNotExist;
            public NativeList<SetComponentError> ComponentDoesNotExist;

            public void Execute()
            {
                var entityGuidTypeIndex = TypeManager.GetTypeIndex<EntityGuid>();

                var offset = 0L;
                for (var i = 0; i < Changes.Length; i++)
                {
                    var packedComponentDataChange = Changes[i];
                    var packedComponent = packedComponentDataChange.Component;
                    var component = PackedTypes[packedComponent.PackedTypeIndex];
                    var size = packedComponentDataChange.Size;
                    var data = (byte*)Payload.GetUnsafeReadOnlyPtr() + offset;
                    var componentTypeInArchetype = new ComponentTypeInArchetype(component);

                    if (PackedEntities.TryGetFirstValue(packedComponent.PackedEntityIndex, out var entity, out var iterator))
                    {
                        do
                        {
                            if (!EntityManager.Exists(entity))
                            {
                                EntityDoesNotExist.Add(new SetComponentError
                                {
                                    ComponentType = component, Guid =PackedEntityGuids[packedComponent.PackedEntityIndex]
                                });
                            }
                            else if (!EntityManager.HasComponent(entity, component))
                            {
                                ComponentDoesNotExist.Add(new SetComponentError
                                {
                                    ComponentType = component, Guid =PackedEntityGuids[packedComponent.PackedEntityIndex]
                                });
                            }
                            else
                            {
                                if (componentTypeInArchetype.IsZeroSized)
                                {
                                    // Nothing to set.
                                }
                                else if (componentTypeInArchetype.IsBuffer)
                                {
                                    ref readonly var typeInfo = ref TypeManager.GetTypeInfo(componentTypeInArchetype.TypeIndex);
                                    var elementSize = typeInfo.ElementSize;
                                    var lengthInElements = size / elementSize;
                                    var header = (BufferHeader*)EntityManager.GetComponentDataRawRW(entity, component.TypeIndex);
                                    BufferHeader.Assign(header, data, lengthInElements, elementSize, 16, false, 0);
                                }
                                else
                                {
                                    var target = (byte*)EntityManager.GetComponentDataRawRW(entity, component.TypeIndex);

                                    // Perform incremental updates on the entityGuidToEntity map to avoid a full rebuild.
                                    if (componentTypeInArchetype.TypeIndex == entityGuidTypeIndex)
                                    {
                                        EntityGuid entityGuid;
                                        UnsafeUtility.MemCpy(&entityGuid, target, sizeof(EntityGuid));

                                        if (!entityGuid.Equals(default))
                                        {
                                            EntityGuidToEntity.Remove(entityGuid, entity);
                                        }

                                        UnsafeUtility.MemCpy(&entityGuid, data + packedComponentDataChange.Offset, size);
                                        EntityGuidToEntity.Add(entityGuid, entity);
                                        EntityToEntityGuid.TryAdd(entity, entityGuid);
                                    }

                                    UnsafeUtility.MemCpy(target + packedComponentDataChange.Offset, data, size);
                                }
                            }
                        }
                        while (PackedEntities.TryGetNextValue(out entity, ref iterator));
                    }

                    offset += size;
                }
            }
        }

        static void ApplySetComponents(
            EntityManager entityManager,
            NativeArray<PackedComponentDataChange> changes,
            NativeArray<byte> payload,
            NativeArray<EntityGuid> packedEntityGuids,
            NativeMultiHashMap<int, Entity> packedEntities,
            NativeArray<ComponentType> packedTypes,
            NativeMultiHashMap<EntityGuid, Entity> entityGuidToEntity,
            NativeHashMap<Entity, EntityGuid> entityToEntityGuid)
        {
            s_ApplySetComponentsProfilerMarker.Begin();
            var entityDoesNotExist = new NativeList<SetComponentError>(Allocator.TempJob);
            var componentDoesNotExist = new NativeList<SetComponentError>(Allocator.TempJob);
            new ApplySetComponentsJob
            {
                Changes = changes,
                ComponentDoesNotExist = componentDoesNotExist,
                EntityDoesNotExist = entityDoesNotExist,
                EntityGuidToEntity = entityGuidToEntity,
                EntityManager = entityManager,
                EntityToEntityGuid = entityToEntityGuid,
                PackedEntities = packedEntities,
                PackedEntityGuids = packedEntityGuids,
                PackedTypes = packedTypes,
                Payload = payload
            }.Run();

            for (int i = 0; i < entityDoesNotExist.Length; i++)
            {
                var error = entityDoesNotExist[i];
                Debug.LogWarning($"SetComponent<{error.ComponentType}>({error.Guid}) but entity does not exist.");
            }
            for (int i = 0; i < componentDoesNotExist.Length; i++)
            {
                var error = componentDoesNotExist[i];
                Debug.LogWarning($"SetComponent<{error.ComponentType}>({error.Guid}) but entity does not exist.");
            }

            entityDoesNotExist.Dispose();
            componentDoesNotExist.Dispose();
            s_ApplySetComponentsProfilerMarker.End();
        }

        internal struct ManagedObjectEntityReferencePatch
        {
            public int Id;
            public Entity TargetEntity;
        }

        internal struct ManagedObjectBlobAssetReferencePatch
        {
            public int Id;
            public ulong Target;
        }

        internal struct EntityComponentPair : IEquatable<EntityComponentPair>, IComparable<EntityComponentPair>
        {
            public Entity Entity;
            public ComponentType Component;

            public bool Equals(EntityComponentPair other)
            {
                return Entity == other.Entity && Component == other.Component;
            }

            public int CompareTo(EntityComponentPair other)
            {
                var result = Entity.CompareTo(other.Entity);
                if (result != 0) return result;
                result = Component.TypeIndex.CompareTo(other.Component.TypeIndex);
                return result;
            }
        }

        static void ApplyEntityPatches(
            EntityManager entityManager,
            NativeArray<EntityReferenceChange> changes,
            NativeArray<EntityGuid> packedEntityGuids,
            NativeMultiHashMap<int, Entity> packedEntities,
            NativeArray<ComponentType> packedTypes,
            NativeMultiHashMap<EntityGuid, Entity> entityGuidToEntity,
            NativeHashMap<Entity, EntityGuid> entityToEntityGuid,
            NativeHashMap<EntityGuid, Entity> entityGuidToPrefab,
            NativeHashMap<Entity, Entity> entityToLinkedEntityGroupRoot)
        {
            s_ApplyEntityPatchesProfilerMarker.Begin();

            var managedObjectEntityReferencePatches = new NativeMultiHashMap<EntityComponentPair, ManagedObjectEntityReferencePatch>(changes.Length, Allocator.Temp);

            for (var i = 0; i < changes.Length; i++)
            {
                var patch = changes[i];
                var packedComponent = patch.Component;
                var component = packedTypes[packedComponent.PackedTypeIndex];
                var targetEntityGuid = patch.Value;
                var targetOffset = patch.Offset;
                var multipleTargetEntities = false;
                Entity targetEntity;

                if (targetEntityGuid.Equals(default))
                {
                    targetEntity = Entity.Null;
                }
                else
                {
                    if (!entityGuidToEntity.TryGetFirstValue(targetEntityGuid, out targetEntity, out var patchSourceIterator))
                    {
                        Debug.LogWarning($"PatchEntities<{component}>({packedEntityGuids[packedComponent.PackedEntityIndex]}) but entity with guid-to-patch-to does not exist.");
                        continue;
                    }
                    multipleTargetEntities = entityGuidToEntity.TryGetNextValue(out _, ref patchSourceIterator);
                }

                if (packedEntities.TryGetFirstValue(packedComponent.PackedEntityIndex, out var entity, out var iterator))
                {
                    do
                    {
                        if (!entityManager.Exists(entity))
                        {
                            Debug.LogWarning($"PatchEntities<{component}>({packedEntityGuids[packedComponent.PackedEntityIndex]}) but entity to patch does not exist.");
                        }
                        else if (!entityManager.HasComponent(entity, component))
                        {
                            Debug.LogWarning($"PatchEntities<{component}>({packedEntityGuids[packedComponent.PackedEntityIndex]}) but component in entity to patch does not exist.");
                        }
                        else
                        {
                            // If just one entity has the GUID we're patching to, we can just use that entity.
                            // but if multiple entities have that GUID, we need to patch to the (one) entity that's in the destination entity's "group."
                            // that group is defined by a LinkedEntityGroup component on the destination entity's "root entity," which contains an array of entity references.
                            // the destination entity's "root entity" is defined by whatever entity owns the (one) LinkedEntityGroup that refers to the destination entity.
                            // so, we had to build a lookup table earlier, to take us from "destination entity" to "root entity of my group," so we can find this LinkedEntityGroup
                            // component, and riffle through it to find the (one) entity with the GUID we're looking for.
                            if (multipleTargetEntities)
                            {
                                targetEntity = Entity.Null;

                                if (entityToLinkedEntityGroupRoot.TryGetValue(entity, out var linkedEntityGroupRoot))
                                {
                                    // This entity is part of a LinkedEntityGroup
                                    var linkedEntityGroup = entityManager.GetBuffer<LinkedEntityGroup>(linkedEntityGroupRoot);

                                    // Scan through the group and look for the entity with the target entityGuid.
                                    for (var elementIndex = 0; elementIndex < linkedEntityGroup.Length; elementIndex++)
                                    {
                                        // Get the entityGuid from each element.
                                        if (entityToEntityGuid.TryGetValue(linkedEntityGroup[elementIndex].Value, out var entityGuidInGroup))
                                        {
                                            if (entityGuidInGroup.Equals(targetEntityGuid))
                                            {
                                                // Match found this is our entity
                                                targetEntity = linkedEntityGroup[elementIndex].Value;
                                                break;
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    // We are not dealing with a LinkedEntityGroup at this point, let's hope it's a prefab.
                                    if (!entityGuidToPrefab.TryGetValue(targetEntityGuid, out targetEntity))
                                    {
                                        Debug.LogWarning($"PatchEntities<{component}>({packedEntityGuids[packedComponent.PackedEntityIndex]}) but 2+ entities for GUID of entity-to-patch-to, and no root for entity-to-patch is, so we can't disambiguate.");
                                        continue;
                                    }
                                }
                            }

                            if (component.IsBuffer)
                            {
                                var pointer = (byte*)entityManager.GetBufferRawRW(entity, component.TypeIndex);
                                UnsafeUtility.MemCpy(pointer + targetOffset, &targetEntity, sizeof(Entity));
                            }
                            else if (component.IsManagedComponent || component.IsSharedComponent)
                            {
                                managedObjectEntityReferencePatches.Add(
                                    new EntityComponentPair { Entity = entity, Component = component },
                                    new ManagedObjectEntityReferencePatch { Id = targetOffset, TargetEntity = targetEntity });
                            }
                            else
                            {
                                var pointer = (byte*)entityManager.GetComponentDataRawRW(entity, component.TypeIndex);
                                UnsafeUtility.MemCpy(pointer + targetOffset, &targetEntity, sizeof(Entity));
                            }
                        }
                    }
                    while (packedEntities.TryGetNextValue(out entity, ref iterator));
                }
            }

#if !UNITY_DOTSRUNTIME
            var managedObjectPatcher = new ManagedObjectEntityReferencePatcher();

            // Apply all managed entity patches
            using (var keys = managedObjectEntityReferencePatches.GetKeyArray(Allocator.Temp))
            {
                keys.Sort();
                var uniqueCount = keys.Unique();

                for (var i = 0; i < uniqueCount; i++)
                {
                    var pair = keys[i];
                    var patches = managedObjectEntityReferencePatches.GetValuesForKey(pair);

                    if (pair.Component.IsManagedComponent)
                    {
                        var obj = entityManager.GetComponentObject<object>(pair.Entity, pair.Component);
                        managedObjectPatcher.ApplyPatches(ref obj, patches);
                    }
                    else if (pair.Component.IsSharedComponent)
                    {
                        var obj = entityManager.GetSharedComponentData(pair.Entity, pair.Component.TypeIndex);
                        managedObjectPatcher.ApplyPatches(ref obj, patches);
                        entityManager.SetSharedComponentDataBoxedDefaultMustBeNull(pair.Entity, pair.Component.TypeIndex, obj);
                    }

                    patches.Dispose();
                }
            }
#endif
            managedObjectEntityReferencePatches.Dispose();
            s_ApplyEntityPatchesProfilerMarker.End();
        }

        struct Child
        {
            public Entity RootEntity;
            public Entity ChildEntity;
            public EntityGuid ChildEntityGuid;
        }

        static void ApplyLinkedEntityGroupAdditions(
            EntityManager entityManager,
            NativeArray<LinkedEntityGroupChange> linkedEntityGroupChanges,
            NativeArray<EntityGuid> packedEntityGuids,
            NativeMultiHashMap<int, Entity> packedEntities,
            NativeMultiHashMap<EntityGuid, Entity> entityGuidToEntity,
            NativeHashMap<Entity, EntityGuid> entityToEntityGuid,
            NativeHashMap<EntityGuid, Entity> entityGuidToPrefab,
            NativeHashMap<Entity, Entity> entityToLinkedEntityGroupRoot)
        {
            s_ApplyLinkedEntityGroupAdditionsProfilerMarker.Begin();
            using (var additions = new NativeList<Child>(Allocator.TempJob))
            {
                for (var i = 0; i < linkedEntityGroupChanges.Length; i++)
                {
                    var linkedEntityGroupAddition = linkedEntityGroupChanges[i];

                    // If we are asked to add a child to a linked entity group, then that child's guid must correspond to
                    // exactly one entity in the destination world that also has a Prefab component. Since we made a lookup
                    // from EntityGuid to Prefab entity before, we can use it to find the specific entity we want.
                    if (entityGuidToPrefab.TryGetValue(linkedEntityGroupAddition.ChildEntityGuid, out var prefabEntityToInstantiate))
                    {
                        if (entityGuidToEntity.TryGetFirstValue(linkedEntityGroupAddition.RootEntityGuid, out var rootEntity, out var iterator))
                        {
                            do
                            {
                                if (rootEntity == prefabEntityToInstantiate)
                                {
                                    Debug.LogWarning($"Trying to instantiate self as child?");
                                    continue;
                                }

                                if (entityManager.HasComponent<Prefab>(rootEntity))
                                {
                                    entityManager.GetBuffer<LinkedEntityGroup>(rootEntity).Add(prefabEntityToInstantiate);
                                    entityToLinkedEntityGroupRoot.TryAdd(prefabEntityToInstantiate, rootEntity);
                                }
                                else
                                {
                                    var instantiatedEntity = entityManager.Instantiate(prefabEntityToInstantiate);
                                    var linkedEntityGroup = entityManager.GetBuffer<LinkedEntityGroup>(rootEntity);
                                    linkedEntityGroup.Add(instantiatedEntity);

                                    additions.Add(new Child
                                    {
                                        RootEntity = rootEntity,
                                        ChildEntity = instantiatedEntity,
                                        ChildEntityGuid = linkedEntityGroupAddition.ChildEntityGuid
                                    });
                                }
                            }
                            while (entityGuidToEntity.TryGetNextValue(out rootEntity, ref iterator));
                        }
                        else
                        {
                            Debug.LogWarning($"Tried to add a child to a linked entity group, but root entity didn't exist in destination world.");
                        }
                    }
                    else
                    {
                        // At this point we are dealing with a non-prefab linked entity group
                        // This can happen for disabled entities during conversion.

                        var hasRootEntity = entityGuidToEntity.TryGetFirstValue(linkedEntityGroupAddition.RootEntityGuid, out var rootEntity, out var rootIterator);
                        var hasChildEntity = entityGuidToEntity.TryGetFirstValue(linkedEntityGroupAddition.ChildEntityGuid, out var childEntityToLink, out var childIterator);

                        if (!hasRootEntity)
                        {
                            Debug.LogWarning("Failed to add a linked child. The specified root entity was not found in the destination world.");
                            continue;
                        }

                        if (!hasChildEntity)
                        {
                            Debug.LogWarning("Failed to add a linked child. The specified child entity was not found in the destination world.");
                            continue;
                        }

                        var multipleRootEntities = entityGuidToEntity.TryGetNextValue(out _, ref rootIterator);
                        var multipleChildEntities = entityGuidToEntity.TryGetNextValue(out _, ref childIterator);

                        if (multipleRootEntities)
                        {
                            Debug.LogWarning("Failed to add a linked child. Multiple instances of the root entity were found in the destination world.");
                            continue;
                        }

                        if (multipleChildEntities)
                        {
                            Debug.LogWarning("Failed to add a linked child. Multiple instances of the child entity were found in the destination world.");
                            continue;
                        }

                        if (rootEntity == childEntityToLink)
                        {
                            // While this is actually valid and the intended way to use LinkedEntityGroup.
                            // We should never receive change set with this change.
                            // Instead we automatically add the root when adding the LinkedEntityGroup component.
                            Debug.LogWarning("Failed to add a linked child. Unable to link the root as a child.");
                            continue;
                        }

                        entityManager.GetBuffer<LinkedEntityGroup>(rootEntity).Add(childEntityToLink);
                        entityToLinkedEntityGroupRoot.TryAdd(childEntityToLink, rootEntity);
                    }
                }

                for (var i = 0; i < additions.Length; i++)
                {
                    var addition = additions[i];
                    for (var packedEntityGuidIndex = 0; packedEntityGuidIndex < packedEntityGuids.Length; ++packedEntityGuidIndex)
                    {
                        if (!packedEntityGuids[packedEntityGuidIndex].Equals(addition.ChildEntityGuid))
                        {
                            continue;
                        }

                        packedEntities.Add(packedEntityGuidIndex, addition.ChildEntity);
                        break;
                    }

                    entityToEntityGuid.TryAdd(addition.ChildEntity, addition.ChildEntityGuid);
                    entityGuidToEntity.Add(addition.ChildEntityGuid, addition.ChildEntity);
                    entityToLinkedEntityGroupRoot.TryAdd(addition.ChildEntity, addition.RootEntity);
                }
            }
            s_ApplyLinkedEntityGroupAdditionsProfilerMarker.End();
        }

        static void ApplyLinkedEntityGroupRemovals(
            EntityManager entityManager,
            NativeArray<LinkedEntityGroupChange> linkedEntityGroupChanges,
            NativeArray<EntityGuid> packedEntityGuids,
            NativeMultiHashMap<int, Entity> packedEntities,
            NativeMultiHashMap<EntityGuid, Entity> entityGuidToEntity,
            NativeHashMap<Entity, EntityGuid> entityToEntityGuid,
            NativeHashMap<Entity, Entity> entityToLinkedEntityGroupRoot)
        {
            s_ApplyLinkedEntityGroupRemovalsProfilerMarker.Begin();
            var s = entityManager.GetCheckedEntityDataAccess();
            using (var removals = new NativeList<Child>(Allocator.TempJob))
            {
                for (var i = 0; i < linkedEntityGroupChanges.Length; ++i)
                {
                    var linkedEntityGroupRemoval = linkedEntityGroupChanges[i];
                    if (entityGuidToEntity.TryGetFirstValue(linkedEntityGroupRemoval.RootEntityGuid, out var rootEntity, out var iterator))
                    {
                        do
                        {
                            var linkedEntityGroup = entityManager.GetBuffer<LinkedEntityGroup>(rootEntity);

                            // Look for the remove child in the LinkedEntityGroupBuffer
                            for (var bufferIndex = 0; bufferIndex < linkedEntityGroup.Length; bufferIndex++)
                            {
                                var childEntity = linkedEntityGroup[bufferIndex].Value;

                                if (entityToEntityGuid.TryGetValue(childEntity, out var childEntityGuid) &&
                                    childEntityGuid.Equals(linkedEntityGroupRemoval.ChildEntityGuid))
                                {
                                    // This entity does not exist. It was most likely destroyed.
                                    // Remove it from the LinkedEntityGroup
                                    linkedEntityGroup.RemoveAt(bufferIndex);

                                    removals.Add(new Child
                                    {
                                        RootEntity = rootEntity,
                                        ChildEntity = childEntity,
                                        ChildEntityGuid = linkedEntityGroupRemoval.ChildEntityGuid,
                                    });
                                    break;
                                }
                            }

                            // if we got here without destroying an entity, then maybe the destination world destroyed it before we synced?
                            // not sure if that is a fatal error, or what.
                        }
                        while (entityGuidToEntity.TryGetNextValue(out rootEntity, ref iterator));
                    }
                }

                for (var i = 0; i < removals.Length; ++i)
                {
                    entityToLinkedEntityGroupRoot.Remove(removals[i].ChildEntity);
                }
            }
            s_ApplyLinkedEntityGroupRemovalsProfilerMarker.End();
        }

        static int CalculateLinkedEntityGroupEntitiesLength(EntityManager entityManager, EntityQuery linkedEntityGroupQuery)
        {
            var count = 0;

            using (var chunks = linkedEntityGroupQuery.CreateArchetypeChunkArray(Allocator.TempJob))
            {
                new CalculateLinkedEntityGroupEntitiesLengthJob
                {
                    Count = &count,
                    Chunks = chunks,
                    LinkedEntityGroupTypeHandle = entityManager.GetBufferTypeHandle<LinkedEntityGroup>(true)
                }.Schedule().Complete();
            }

            return count;
        }

#if !UNITY_DOTSRUNTIME
        class ManagedObjectEntityReferencePatcher : PropertyVisitor, Properties.Adapters.IVisit<Entity>
        {
            NativeMultiHashMap<EntityComponentPair, ManagedObjectEntityReferencePatch>.Enumerator Patches;

            public ManagedObjectEntityReferencePatcher()
            {
                AddAdapter(this);
            }

            public void ApplyPatches(ref object obj, NativeMultiHashMap<EntityComponentPair, ManagedObjectEntityReferencePatch>.Enumerator patches)
            {
                Patches = patches;
                PropertyContainer.Visit(ref obj, this);
            }

            VisitStatus Properties.Adapters.IVisit<Entity>.Visit<TContainer>(Property<TContainer, Entity> property, ref TContainer container, ref Entity value)
            {
                // Make a copy for we can re-use the enumerator
                var patches = Patches;
                foreach (var patch in patches)
                {
                    if (value.Index == patch.Id)
                    {
                        value = patch.TargetEntity;
                        break;
                    }
                }

                return VisitStatus.Stop;
            }
        }
#endif
    }
}
