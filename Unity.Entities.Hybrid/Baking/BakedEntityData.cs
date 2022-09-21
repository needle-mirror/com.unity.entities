using System;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Baking;
using Unity.Entities.Conversion;
using Unity.Entities.Hybrid.Baking;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Entities
{
    //@todo: Would it be better to store this outside of it in one big NativeMultiHashMap?

    //@TODO: Test for adding a condional component... (Test that reset actually runs incremental)

    internal struct PrefabState
    {
        public Hash128 GUID;
        public Hash128 Hash;
        public int RefCount;

        public PrefabState(Hash128 guid, Hash128 hash)
        {
            GUID = guid;
            Hash = hash;
            RefCount = 0;
        }
    }

    internal enum DefaultArchetype
    {
        Default,
        AdditionalEntity,
        AdditionalEntityBakeOnly,
        Prefab,
        PrefabAdditionalEntity,
        PrefabAdditionalEntityBakeOnly,
    }

    /// <summary>
    /// Stores the <see cref="BakerState"/> and the mapping of GameObject to Entity.
    /// </summary>
    /// <remarks>
    /// Responsible for playing back IncrementalBakingContext.IncrementalBakeInstructions by invoking the users
    /// <see cref="Baker{TAuthoringType}"/> code.
    /// This creates the entities and components and records dependencies as part of it.
    /// This stores what components were added by which baker so they can be reverted when a component / gameobject is removed
    /// or a dependency triggers during incremental baking.
    /// </remarks>
    unsafe struct BakedEntityData : IDisposable
    {
        UnsafeParallelHashMap<int, BakerState>                       _AuthoringComponentToBakerState;
        UnsafeParallelHashMap<Entity, TransformUsageFlagCounters>    _ReferencedEntities;
        bool                                                 _IsReferencedEntitiesDirty;
        UnsafeParallelHashMap<int, int>                              _ComponentToAdditionalEntityCounter;
        UnsafeParallelHashSet<int>                                   _AdditionalGameObjectsToBake;
        UnsafeParallelHashMap<int, PrefabState>                      _PrefabStates;

        // GameObject => Entity
        internal UnsafeParallelHashMap<int, Entity>                  _GameObjectToEntity;
        internal EntityManager                               _EntityManager;
        EntityArchetype                                      _DefaultArchetype;
        EntityArchetype                                      _DefaultArchetypeAdditionalEntity;
        EntityArchetype                                      _DefaultArchetypeAdditionalEntityBakeOnly;
        EntityArchetype                                      _DefaultArchetypePrefab;
        EntityArchetype                                      _DefaultArchetypePrefabAdditionalEntity;
        EntityArchetype                                      _DefaultArchetypePrefabAdditionalEntityBakeOnly;
        EntityQuery                                          _HasRemoveEntityInBake;
        EntityQuery                                          _AllBakedQuery;
        uint                                                 _EntityGUIDNameSpaceID;
        bool                                                 _AssignEntityGUID;
        internal Hash128                                     _SceneGUID;
        internal BakingUtility.BakingFlags _ConversionFlags;


        static string s_CreateEntityForGameObjectStr = "Baking.CreateEntityForGameObject";
        static string s_DestroyEntityForGameObjectStr = "Baking.DestroyEntityForGameObject";
        static string s_RevertStr = "Baking.Revert";
        static string s_RevertComponentsStr = "Baking.RevertComponents";
        static string s_RevertDependenciesStr = "Baking.RevertDependencies";
        static string s_RenameStr = "Baking.Rename";
        static string s_BakeStr = "Baking.Bake";
        static string s_RegisterDependenciesStr = "Baking.RegisterDependencies";
        static string s_CreateBakerStateStr = "Baking.CreateBakerState";
        static string s_BuilderPlaybackStr = "Baking.BuilderPlayback";

        static ProfilerMarker s_CreateEntityForGameObject = new ProfilerMarker(s_CreateEntityForGameObjectStr);
        static ProfilerMarker s_DestroyEntityForGameObject = new ProfilerMarker(s_DestroyEntityForGameObjectStr);
        static ProfilerMarker s_Revert = new ProfilerMarker(s_RevertStr);
        static ProfilerMarker s_RevertComponents = new ProfilerMarker(s_RevertComponentsStr);
        static ProfilerMarker s_RevertDependencies = new ProfilerMarker(s_RevertDependenciesStr);
        static ProfilerMarker s_Rename = new ProfilerMarker(s_RenameStr);
        static ProfilerMarker s_Bake = new ProfilerMarker(s_BakeStr);
        static ProfilerMarker s_RegisterDependencies = new ProfilerMarker(s_RegisterDependenciesStr);
        static ProfilerMarker s_CreateBakerState = new ProfilerMarker(s_CreateBakerStateStr);

        static ProfilerMarker s_BuilderPlayback = new ProfilerMarker(s_BuilderPlaybackStr);

        internal static string[] CollectImportantProfilerMarkerStrings()
        {
            return new string [] {
                s_CreateEntityForGameObjectStr,
                s_DestroyEntityForGameObjectStr,
                s_RevertStr,
                s_RevertComponentsStr,
                s_RevertDependenciesStr,
                s_RenameStr,
                s_BakeStr,
                s_RegisterDependenciesStr,
                s_CreateBakerStateStr,
                s_BuilderPlaybackStr
            };
        }

        public BakedEntityData(EntityManager manager)
        {
            _AuthoringComponentToBakerState = new UnsafeParallelHashMap<int, BakerState>(10, Allocator.Persistent);
            _ComponentToAdditionalEntityCounter = new UnsafeParallelHashMap<int, int>(10, Allocator.Persistent);
            _AdditionalGameObjectsToBake = new UnsafeParallelHashSet<int>(10, Allocator.Persistent);
            _PrefabStates = new UnsafeParallelHashMap<int, PrefabState>(10, Allocator.Persistent);
            _GameObjectToEntity = new UnsafeParallelHashMap<int, Entity>(10, Allocator.Persistent);
            _ReferencedEntities = new UnsafeParallelHashMap<Entity, TransformUsageFlagCounters>(10, Allocator.Persistent);
            _EntityManager = manager;
            _DefaultArchetype = default;
            _DefaultArchetypeAdditionalEntity = default;
            _DefaultArchetypeAdditionalEntityBakeOnly = default;
            _DefaultArchetypePrefab = default;
            _DefaultArchetypePrefabAdditionalEntity = default;
            _DefaultArchetypePrefabAdditionalEntityBakeOnly = default;
            _SceneGUID = default;
            _AssignEntityGUID = false;
            _EntityGUIDNameSpaceID = 0;
            _IsReferencedEntitiesDirty = true;
            _ConversionFlags = default;
            _HasRemoveEntityInBake = manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<TransformAuthoring>(), ComponentType.ReadOnly<RemoveUnusedEntityInBake>() },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
            });

            _AllBakedQuery = manager.CreateEntityQuery(
                new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<TransformAuthoring>() },
                    Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
                });
        }

        public void Dispose()
        {
            foreach (var context in _AuthoringComponentToBakerState)
                context.Value.Dispose();
            _AuthoringComponentToBakerState.Dispose();
            _AdditionalGameObjectsToBake.Dispose();
            _PrefabStates.Dispose();
            _ComponentToAdditionalEntityCounter.Dispose();
            _GameObjectToEntity.Dispose();
            _ReferencedEntities.Dispose();
            _AllBakedQuery.Dispose();
            _HasRemoveEntityInBake.Dispose();
        }

        public void Clear()
        {
            foreach (var context in _AuthoringComponentToBakerState)
                context.Value.Dispose();
            _AuthoringComponentToBakerState.Clear();
            _AdditionalGameObjectsToBake.Clear();
            _PrefabStates.Clear();
            _ComponentToAdditionalEntityCounter.Clear();
            _GameObjectToEntity.Clear();
            _ReferencedEntities.Clear();
            _IsReferencedEntitiesDirty = true;
        }

        public void ConfigureDefaultArchetypes(BakingSettings settings, Scene scene)
        {
            var types = stackalloc ComponentType[16];
            int count = 0;
            types[count++] = ComponentType.ReadWrite<TransformAuthoring>();
            if ((settings.BakingFlags & BakingUtility.BakingFlags.AddEntityGUID) != 0)
                types[count++] = ComponentType.ReadWrite<EntityGuid>();
            if (settings.SceneGUID != default)
                types[count++] = ComponentType.ReadWrite<SceneSection>();

            // Archetypes for additional entities
            ConfigureDefaultArchetype(types, count, DefaultArchetype.AdditionalEntity);
            ConfigureDefaultArchetype(types, count, DefaultArchetype.AdditionalEntityBakeOnly);
            ConfigureDefaultArchetype(types, count, DefaultArchetype.PrefabAdditionalEntity);
            ConfigureDefaultArchetype(types, count, DefaultArchetype.PrefabAdditionalEntityBakeOnly);

            // AdditionalEntitiesBakingData contains a buffer of additional entities on primary entities only
            // This will add AdditionalEntitiesBakingData to the default and prefab archetypes
            types[count++] = ComponentType.ReadWrite<AdditionalEntitiesBakingData>();
            types[count++] = ComponentType.ReadWrite<BakedEntity>();

            ConfigureDefaultArchetype(types, count, DefaultArchetype.Default);
            ConfigureDefaultArchetype(types, count, DefaultArchetype.Prefab);

            _EntityGUIDNameSpaceID = settings.NamespaceID ^ (uint) scene.handle;
            _AssignEntityGUID =
                (settings.BakingFlags & BakingUtility.BakingFlags.AddEntityGUID) != 0;
            _SceneGUID = settings.SceneGUID;
            _ConversionFlags = settings.BakingFlags;
        }


        public void ConfigureDefaultArchetype(ComponentType* baseType, int count, DefaultArchetype defaultArchetype)
        {
            switch (defaultArchetype)
            {
                case DefaultArchetype.Default:
                    _DefaultArchetype = _EntityManager.CreateArchetype(baseType, count);
                    break;
                case DefaultArchetype.AdditionalEntity:
                    baseType[count++] = ComponentType.ReadWrite<AdditionalEntityParent>();
                    _DefaultArchetypeAdditionalEntity = _EntityManager.CreateArchetype(baseType, count);
                    break;
                case DefaultArchetype.AdditionalEntityBakeOnly:
                    baseType[count++] = ComponentType.ReadWrite<AdditionalEntityParent>();
                    baseType[count++] = ComponentType.ReadWrite<BakingOnlyEntity>();
                    _DefaultArchetypeAdditionalEntityBakeOnly = _EntityManager.CreateArchetype(baseType, count);
                    break;
                case DefaultArchetype.Prefab:
                    baseType[count++] = ComponentType.ReadWrite<Prefab>();
                    _DefaultArchetypePrefab = _EntityManager.CreateArchetype(baseType, count);
                    break;
                case DefaultArchetype.PrefabAdditionalEntity:
                    baseType[count++] = ComponentType.ReadWrite<Prefab>();
                    baseType[count++] = ComponentType.ReadWrite<AdditionalEntityParent>();
                    _DefaultArchetypePrefabAdditionalEntity = _EntityManager.CreateArchetype(baseType, count);
                    break;
                case DefaultArchetype.PrefabAdditionalEntityBakeOnly:
                    baseType[count++] = ComponentType.ReadWrite<Prefab>();
                    baseType[count++] = ComponentType.ReadWrite<AdditionalEntityParent>();
                    baseType[count++] = ComponentType.ReadWrite<BakingOnlyEntity>();
                    _DefaultArchetypePrefabAdditionalEntityBakeOnly = _EntityManager.CreateArchetype(baseType, count);
                    break;
            }
        }

        public NativeList<int> RemoveInvalidEntities(Allocator allocator)
        {
            NativeList<int> gameObjectsNoEntity = new NativeList<int>(100, allocator);

            EntityQueryDesc desc = new EntityQueryDesc()
            {
                All = new[] {ComponentType.FromTypeIndex(TypeManager.GetTypeIndex<EntityGuid>())},
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
            };
            var query = _EntityManager.CreateEntityQuery(desc);
            Assert.IsFalse(query.HasFilter(), "The use of EntityQueryMask in this job will not respect the query's active filter settings.");
            var mask = query.GetEntityQueryMask();

            // Check if there is any invalid entity stored in _GameObjectToEntity
            var job = new FindDeletedEntitiesJob()
            {
                GameObjectEntities = _GameObjectToEntity,
                Mask = mask,
                DeletedList = gameObjectsNoEntity
            };
            job.Run();

            // Delete invalid entities from _GameObjectToEntity
            foreach (var gameObject in gameObjectsNoEntity)
            {
                _GameObjectToEntity.Remove(gameObject);
            }

            query.Dispose();

            return gameObjectsNoEntity;
        }

        [BurstCompile]
        struct FindDeletedEntitiesJob : IJob
        {
            [ReadOnly] public UnsafeParallelHashMap<int, Entity> GameObjectEntities;

            [ReadOnly] public EntityQueryMask Mask;

            [NativeDisableParallelForRestriction]
            public NativeList<int> DeletedList;

            public void Execute()
            {
                foreach (var entry in GameObjectEntities)
                {
                    var entity = entry.Value;
                    if (!Mask.MatchesIgnoreFilter(entity))
                    {
                        var gameObjectID = entry.Key;
                        DeletedList.Add(gameObjectID);
                    }
                }
            }
        }

#if UNITY_EDITOR
        public void UpdatePrefabs(IncrementalBakingChangeTracker changeTracker)
        {
            foreach (var kvp in _PrefabStates)
            {
                var instanceId = kvp.Key;
                var prefabState = kvp.Value;
                var newHash = (Hash128)UnityEditor.AssetDatabase.GetAssetDependencyHash(prefabState.GUID);

                if (newHash != prefabState.Hash)
                {
                    if (newHash == default)
                    {
                        // Prefab was deleted, so mark for delete
                        changeTracker.MarkRemoved(instanceId);
                    }
                    else
                    {
                        // Prefab was changed, so force rebake
                        changeTracker.MarkForceBakeHierarchy(instanceId);
                    }
                }
            }
        }
#endif

        public void ApplyBakeInstructions(ref BakeDependencies dependencies, IncrementalBakingContext.IncrementalBakeInstructions instructions, BlobAssetStore blobAssetStore, BakingSettings bakingSettings, ref IncrementalHierarchy hierarchy, ref GameObjectComponents components)
        {
            // Run the actual baking instructions, and record into an EntityCommandBuffer
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Revert all components or additional entities added by previous bakers
            using (s_Revert.Auto())
            {
                foreach (var component in instructions.RevertComponents)
                {
                    if (_AuthoringComponentToBakerState.TryGetValue(component, out var bakerState))
                    {
                        ResetComponentAdditionalEntityCount(component, bakerState.GetPrimaryEntity());

                        using (s_RevertDependencies.Auto())
                            BakeDependencies.ResetBakerDependencies(component, ref dependencies, ref bakerState.Dependencies);

                        using (s_RevertComponents.Auto())
                        {
                            bakerState.Revert(ecb, default, ref _ReferencedEntities, blobAssetStore, ref _IsReferencedEntitiesDirty, ref this);
                            bakerState.Usage.Revert(default, ref _ReferencedEntities, ref _IsReferencedEntitiesDirty);
                        }

                        bakerState.Dispose();
                        _AuthoringComponentToBakerState.Remove(component);

                        // Debug.Log($"ApplyInstruction - RevertBake: {component}");
                    }
                }
            }

            using (s_Rename.Auto())
            {
                foreach (var gameObject in instructions.PotentiallyRenamedGameObjects)
                {
                    if (_GameObjectToEntity.TryGetValue(gameObject.GetInstanceID(), out var entity))
                    {
                        // Debug.Log($"ApplyInstruction - Possible Rename: {component}");
                        _EntityManager.SetName(entity, gameObject.name);
                    }
                }
            }

            // Create primary entity for every game objects that were created
            using (s_CreateEntityForGameObject.Auto())
            {
                foreach (var gameObject in instructions.CreatedGameObjects)
                {
                    var entity = CreateEntityForGameObject(gameObject, 0, _DefaultArchetype);
                    var didAdd = _GameObjectToEntity.TryAdd(gameObject.GetInstanceID(), entity);
                    if (!didAdd)
                        Debug.LogError("Internally inconsistent _GameObjectToEntity table");
                    // Debug.Log($"ApplyInstruction - CreateEntity: {entity} GameObject: {gameObject.GetInstanceID()} ({gameObject.name})");
                }
            }

            // Destroy primary entity for every game object that was destroyed
            using (s_DestroyEntityForGameObject.Auto())
            {
                foreach (var gameObject in instructions.DestroyedGameObjects)
                {
                    //@TODO: DOTS-5446
                    _GameObjectToEntity.TryGetValue(gameObject, out var entity);
                    _GameObjectToEntity.Remove(gameObject);
                    _EntityManager.DestroyEntity(entity);

                    // We might be a prefab, so we can just remove from the Prefab data with the key
                    _PrefabStates.Remove(gameObject);
                    //Debug.Log($"ApplyInstruction - DestroyEntity: {entity} GameObject: {gameObject})");
                }
            }

            var tempDependencies = new BakeDependencies.RecordedDependencies(16, Allocator.TempJob);
            var tempUsage = new BakerEntityUsage(default, 16, Allocator.TempJob);

            // debug state used to track duplicate destination component adds
            BakerDebugState bakerDebugState = new BakerDebugState(Allocator.Temp);
            IBaker.BakerExecutionState state;
            state.Ecb = ecb;
            state.World = _EntityManager.World;
            state.BakedEntityData = (BakedEntityData*)UnsafeUtility.AddressOf(ref this);
            state.DebugState = &bakerDebugState;
            state.BlobAssetStore = blobAssetStore;
#if UNITY_EDITOR
            state.BuildConfiguration = bakingSettings.BuildConfiguration;
            state.DotsSettings = bakingSettings.DotsSettings;
            state.IsBuiltInBuildsEnabled = bakingSettings.IsBuiltInBuildsEnabled;
#endif

            // Base size on created game objects, as worse case is initial bake and this will avoid too many allocations in that case
            var entitiesBaked = new NativeParallelHashSet<Entity>(instructions.CreatedGameObjects.Count, Allocator.Temp);

            using (s_Bake.Auto())
            {
                foreach (var component in instructions.BakeComponents)
                {
                    var instanceID = component.ComponentID;

                    _GameObjectToEntity.TryGetValue(component.GameObjectInstanceID, out var entity);
                    if (!_EntityManager.Exists(entity))
                        Debug.LogError($"Baking entity that doesn't exist: {entity} GameObject: {component.GameObjectInstanceID} Component: {component}", (GameObject)Resources.InstanceIDToObject(component.GameObjectInstanceID));

#if UNITY_EDITOR
                    if (bakingSettings != null && bakingSettings.IsBuiltInBuildsEnabled && bakingSettings.DotsSettings != null)
                    {
                        BakerDataUtility.ApplyAssemblyFilter(bakingSettings.DotsSettings.GetFilterSettings());
                    }
                    else
                    {
                        if (bakingSettings != null && bakingSettings.BuildConfiguration != null)
                        {
                            bakingSettings.BuildConfiguration.TryGetComponent<ConversionSystemFilterSettings>(out var filter);
                            BakerDataUtility.ApplyAssemblyFilter(filter);
                        }
                        else
                        {
                            // Reset a filter in case one was there before
                            BakerDataUtility.ApplyAssemblyFilter((ConversionSystemFilterSettings)null);
                        }
                    }
#endif

                    var bakeTypeIndex = TypeManager.GetTypeIndex(component.Component.GetType());
                    var bakers = BakerDataUtility.GetBakers(bakeTypeIndex);
                    if (bakers == null)
                        continue;

                    entitiesBaked.Add(entity);
                    var didExist = _AuthoringComponentToBakerState.TryGetValue(instanceID, out var bakerState);
                    try
                    {
                        // Need full revert / rebake
                        if (didExist)
                        {
                            ResetComponentAdditionalEntityCount(instanceID, entity);
                            bakerState.Revert(ecb, entity, ref _ReferencedEntities, blobAssetStore, ref _IsReferencedEntitiesDirty, ref this);

                            tempDependencies.Clear();
                            tempUsage.Clear(entity);

                            // We don't bake disabled components
                            if (!component.Component.IsComponentDisabled())
                            {
                                // Rebake all bakers for this component
                                for (int i = 0, n = bakers.Length; i < n; ++i)
                                {
                                    var baker = bakers[i];

                                    // We don't run bake if the baker belongs to a disabled assembly
                                    if (!baker.AssemblyEnabled.Enabled)
                                        continue;

                                    using (baker.Profiler.Auto())
                                    {
                                        try
                                        {
                                            state.Usage = &tempUsage;
                                            state.Dependencies = &tempDependencies;
                                            state.DebugIndex.TypeIndex = bakeTypeIndex;
                                            state.DebugIndex.IndexInBakerArray = i;
                                            state.BakerState = &bakerState;
                                            state.AuthoringSource = component.Component;
                                            state.PrimaryEntity = entity;

                                            // baker.Baker.BakeInternal(ref tempDependencies, ref tempUsage, ref bakerState, ref bakerDebugState, i, ref this, ref ecb, component.Component, blobAssetStore);
                                            baker.Baker.InvokeBake(state);
                                        }
                                        catch (Exception e)
                                        {
                                            Debug.LogException(e);
                                        }
                                    }
                                }

                                using (s_RegisterDependencies.Auto())
                                {
                                    if (BakeDependencies.UpdateDependencies(ref dependencies, instanceID, ref bakerState.Dependencies, ref tempDependencies))
                                    {
                                        //Debug.Log($"Updating dependencies for: '{Resources.InstanceIDToObject(component.GameObjectInstanceID).name}' {component.Component.GetType().Name}");
                                    }

                                    if (BakerEntityUsage.Update(ref _ReferencedEntities, ref _IsReferencedEntitiesDirty, ref bakerState.Usage, ref tempUsage, instanceID))
                                    {
                                        //Debug.Log($"Updating usage for: '{Resources.InstanceIDToObject(component.GameObjectInstanceID).name}' {component.Component.GetType().Name}");
                                    }
                                }
                            }
                        }
                        // Added baker for the first time, can just add.
                        else
                        {
                            using (s_CreateBakerState.Auto())
                            {
                                bakerState = new BakerState(entity, Allocator.Persistent);
                            }

                            // We don't bake disabled components
                            if (!component.Component.IsComponentDisabled())
                            {
                                // Rebake all bakers for this component
                                for (int i = 0, n = bakers.Length; i < n; ++i)
                                {
                                    var baker = bakers[i];

                                    // We don't run bake if the baker belongs to a disabled assembly
                                    if (!baker.AssemblyEnabled.Enabled)
                                        continue;

                                    using (baker.Profiler.Auto())
                                    {
                                        try
                                        {
                                            state.Usage = &bakerState.Usage;
                                            state.Dependencies = &bakerState.Dependencies;
                                            state.DebugIndex.TypeIndex = bakeTypeIndex;
                                            state.DebugIndex.IndexInBakerArray = i;
                                            state.BakerState = &bakerState;
                                            state.AuthoringSource = component.Component;
                                            state.PrimaryEntity = entity;

                                            baker.Baker.InvokeBake(state);
                                        }
                                        catch (Exception e)
                                        {
                                            Debug.LogException(e);
                                        }
                                    }
                                }

                                using (s_RegisterDependencies.Auto())
                                {
                                    BakeDependencies.AddDependencies(ref dependencies, instanceID,
                                        ref bakerState.Dependencies);
                                    bakerState.Usage.AddTransformUsage(ref _ReferencedEntities,
                                        ref _IsReferencedEntitiesDirty, instanceID);
                                }
                            }
                        }
                    }
                    finally
                    {
                        // NOTE: Have to copy back to the baking context since BakerState is copied by value
                        // Would be better to keep a ref to the bakerState (Should come into master soon)
                        _AuthoringComponentToBakerState[instanceID] = bakerState;
                    }

                    //Debug.Log($"ApplyInstruction - Bake: {entity} GameObject: {component.GameObjectInstanceID} ({Resources.InstanceIDToObject(component.GameObjectInstanceID)}) Component: {component.Component.GetInstanceID()} ({component.Component})");
                }
            }

            bakerDebugState.Dispose();
            tempDependencies.Dispose();
            tempUsage.Dispose();

            // Clean up unused prefabs
            var removedPrefabs = new NativeList<int>(Allocator.Temp);
            foreach (var kvp in _PrefabStates)
            {
                var instanceId = kvp.Key;
                var prefabState = kvp.Value;
                if (prefabState.RefCount <= 0)
                {
                    if (_GameObjectToEntity.TryGetValue(instanceId, out var prefabEntity))
                    {
                        ecb.DestroyEntity(prefabEntity);

                        _PrefabStates.Remove(instanceId);

                        // We must remove all potential children of this GameObject as well
                        if (hierarchy.IndexByInstanceId.TryGetValue(instanceId, out var parentIndex))
                        {
                            var children = IncrementalHierarchyFunctions.GetChildrenRecursively(hierarchy, parentIndex);
                            foreach(var childIndex in children)
                            {
                                var childInstanceId = hierarchy.InstanceId[childIndex];
                                _GameObjectToEntity.Remove(childInstanceId);
                            }
                        }

                        _GameObjectToEntity.Remove(instanceId);

                        removedPrefabs.Add(instanceId);

                        foreach (var componentData in components.GetComponents(instanceId))
                        {
                            var componentID = componentData.InstanceID;
                            IncrementalBakingLog.RecordComponentDestroyed(componentID);

                            if (_AuthoringComponentToBakerState.TryGetValue(componentID, out var bakerState))
                            {
                                ResetComponentAdditionalEntityCount(componentID, bakerState.GetPrimaryEntity());

                                using (s_RevertDependencies.Auto())
                                    BakeDependencies.ResetBakerDependencies(componentID, ref dependencies, ref bakerState.Dependencies);

                                using (s_RevertComponents.Auto())
                                {
                                    bakerState.Revert(ecb, default, ref _ReferencedEntities,  blobAssetStore,ref _IsReferencedEntitiesDirty, ref this);
                                    bakerState.Usage.Revert(default, ref _ReferencedEntities, ref _IsReferencedEntitiesDirty);
                                }

                                bakerState.Dispose();
                                _AuthoringComponentToBakerState.Remove(componentID);
                            }
                        }
                    }
                }
            }

            IncrementalHierarchyFunctions.Remove(hierarchy, removedPrefabs.AsArray());
            removedPrefabs.Dispose();

            // For all entities baked, add the BakedEntity tag
            foreach (var entity in entitiesBaked)
            {
                ecb.AddComponent<BakedEntity>(entity);
            }
            entitiesBaked.Dispose();

            // Play back the entity command buffer so it can be done in batch.
            using (s_BuilderPlayback.Auto())
            {
                ecb.Playback(_EntityManager);
                ecb.Dispose();
            }
        }

        [BurstCompile]
        struct UpdateReferencedEntitiesJob : IJobChunk
        {
            public EntityQueryMask  HasRemoveEntityInBake;

            public EntityTypeHandle Entities;

            public NativeList<Entity> Remove;
            public NativeList<Entity> Add;

            [ReadOnly] public UnsafeParallelHashMap<Entity, TransformUsageFlagCounters> ReferencedEntities;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);
                var hasRemoveEntityInBake = HasRemoveEntityInBake.MatchesIgnoreFilter(chunk);

                var entities = chunk.GetNativeArray(Entities);
                foreach (var e in entities)
                {
                    var shouldRemoveEntityInBake = !ReferencedEntities.TryGetValue(e, out var usage) || usage.IsUnused;

                    if (shouldRemoveEntityInBake != hasRemoveEntityInBake)
                    {
                        if (shouldRemoveEntityInBake)
                            Add.Add(e);
                        else
                            Remove.Add(e);
                    }
                }
            }
        }

        public void UpdateReferencedEntities()
        {
            if (!_IsReferencedEntitiesDirty)
                return;
            _IsReferencedEntitiesDirty = false;

            Assert.IsFalse(_HasRemoveEntityInBake.HasFilter(), "The use of EntityQueryMask in this job will not respect the query's active filter settings.");
            var job = new UpdateReferencedEntitiesJob
            {
                Entities = _EntityManager.GetEntityTypeHandle(),
                HasRemoveEntityInBake = _HasRemoveEntityInBake.GetEntityQueryMask(),
                Add = new NativeList<Entity>(Allocator.TempJob),
                Remove = new NativeList<Entity>(Allocator.TempJob),
                ReferencedEntities = _ReferencedEntities
            };
            job.Run(_AllBakedQuery);

            _EntityManager.RemoveComponent<RemoveUnusedEntityInBake>(job.Remove.AsArray());
            _EntityManager.AddComponent<RemoveUnusedEntityInBake>(job.Add.AsArray());

            job.Remove.Dispose();
            job.Add.Dispose();
        }

        Entity CreateEntityForGameObject(GameObject gameObject, int authoringInstanceId, EntityArchetype archetype, int serial = 0, string entityName = "")
        {
            if (gameObject == null)
                throw new ArgumentNullException(nameof(gameObject),
                    $"{nameof(CreateEntityForGameObject)} must be called with a valid UnityEngine.Object");

            var entity = _EntityManager.CreateEntity(archetype);

            if (_AssignEntityGUID)
            {
                var entityGuid = new EntityGuid(gameObject.GetInstanceID(), authoringInstanceId, _EntityGUIDNameSpaceID, (uint)serial);
                _EntityManager.SetComponentData(entity, entityGuid);
            }

            //@TODO: DOTS-5445
            if (_SceneGUID != default)
            {
                int sectionIndex = 0;
                var section = gameObject.GetComponentInParent<SceneSectionComponent>(true);
                if (section != null)
                {
                    sectionIndex = section.SectionIndex;
                }

                _EntityManager.SetSharedComponent(entity,
                    new SceneSection {SceneGUID = _SceneGUID, Section = sectionIndex});
            }

#if UNITY_EDITOR
            var AssignName = true;
            if (AssignName)
            {
                // Truncate the name to ensure it fits.
                var fs = new FixedString64Bytes();
                if(String.IsNullOrEmpty(entityName))
                    FixedStringMethods.CopyFromTruncated(ref fs, gameObject.name);
                else
                    FixedStringMethods.CopyFromTruncated(ref fs, entityName);

                _EntityManager.SetName(entity, fs);
            }
#endif

            return entity;
        }

        public bool HasAdditionalGameObjectsToBake()
        {
            return !_AdditionalGameObjectsToBake.IsEmpty;
        }

        public NativeArray<int> GetAndClearAdditionalObjectsToBake(Allocator allocator)
        {
            var additionalObjectsToBakeArray = _AdditionalGameObjectsToBake.ToNativeArray(allocator);
            _AdditionalGameObjectsToBake.Clear();
            return additionalObjectsToBakeArray;
        }

#if UNITY_EDITOR
        public void AddPrefabRef(int instanceId)
        {
            if(!_PrefabStates.TryGetValue(instanceId, out var prefabState))
            {
                var prefabGUID = UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(instanceId).assetGUID;
                if (prefabGUID != default)
                {
                    //@TODO: DOTS-5441
                    var newHash = UnityEditor.AssetDatabase.GetAssetDependencyHash(prefabGUID);

                    prefabState = new PrefabState(prefabGUID, newHash);
                }
            }
            prefabState.RefCount++;
            _PrefabStates[instanceId] = prefabState;
        }

        public void RemovePrefabRef(int instanceId)
        {
            _PrefabStates.TryGetValue(instanceId, out var prefabState);
            prefabState.RefCount--;
            _PrefabStates[instanceId] = prefabState;
        }
#endif

        internal Entity CreateEntityForPrefab(GameObject prefab)
        {
            var instanceId = prefab.GetInstanceID();
            var entity = CreateEntityForGameObject(prefab, 0, _DefaultArchetypePrefab, 0);
            _GameObjectToEntity[instanceId] = entity;

            // Now register the Prefab for lazy baking
            _AdditionalGameObjectsToBake.Add(instanceId);

            // Add all children
            var allTransforms = prefab.GetComponentsInChildren<Transform>(true);
            var linkedEntityGroupArray = new NativeArray<LinkedEntityGroupBakingData>(allTransforms.Length, Allocator.Temp);

            // Assign self to first position in linked entity group
            linkedEntityGroupArray[0] = new LinkedEntityGroupBakingData {Value = entity};

            for(int i = 1; i < allTransforms.Length; i++)
            {
                var childGameObject = allTransforms[i].gameObject;
                var childEntity = CreateEntityForGameObject(childGameObject, 0, _DefaultArchetypePrefab, 0);

                _GameObjectToEntity[childGameObject.GetInstanceID()] = childEntity;

                linkedEntityGroupArray[i] = new LinkedEntityGroupBakingData {Value = childEntity};
            }

            var buffer = _EntityManager.AddBuffer<LinkedEntityGroupBakingData>(entity);
            buffer.AddRange(linkedEntityGroupArray);
            linkedEntityGroupArray.Dispose();

            return entity;
        }

        public Entity GetEntity(GameObject gameObject)
        {
            if (gameObject == null)
                return Entity.Null;

            var gameObjectId = gameObject.GetInstanceID();

            // If it already exists, just give back the reference
            if (!_GameObjectToEntity.TryGetValue(gameObjectId, out var entity))
            {
                if (gameObject.IsPrefab())
                {
                    // Okay, it doesn't exist so we need to create it
                    entity = CreateEntityForPrefab(gameObject);
                }
            }

            return entity;
        }

        public Entity GetEntity(Component component)
        {
            return component == null ? Entity.Null : GetEntity(component.gameObject);
        }

        public Entity CreateAdditionalEntity(GameObject gameObject, int authoringInstanceId, bool bakingOnlyEntity, string entityName = "")
        {
            var instanceId = gameObject.GetInstanceID();
            var primaryEntity = _GameObjectToEntity[instanceId];

            _ComponentToAdditionalEntityCounter.TryGetValue(authoringInstanceId, out var counter);
            counter += 1;

            _ComponentToAdditionalEntityCounter[authoringInstanceId] = counter;

            EntityArchetype entityArchetype;
            if (gameObject.IsPrefab())
            {
                if (bakingOnlyEntity)
                    entityArchetype = _DefaultArchetypePrefabAdditionalEntityBakeOnly;
                else
                    entityArchetype = _DefaultArchetypePrefabAdditionalEntity;
            }
            else
            {
                if (bakingOnlyEntity)
                    entityArchetype = _DefaultArchetypeAdditionalEntityBakeOnly;
                else
                    entityArchetype = _DefaultArchetypeAdditionalEntity;
            }

            var entity = CreateEntityForGameObject(gameObject, authoringInstanceId, entityArchetype, counter, entityName);
            _EntityManager.SetComponentData(entity, new AdditionalEntityParent { Parent = primaryEntity, ParentInstanceID = instanceId });

            var buffer = _EntityManager.GetBuffer<AdditionalEntitiesBakingData>(primaryEntity);
            buffer.Add(new AdditionalEntitiesBakingData()
                {
                    Value = entity,
                    AuthoringComponentID = authoringInstanceId
                });

            return entity;
        }

        public UnsafeList<Entity> GetEntitiesForBakers(Component component)
        {
            var builder = _AuthoringComponentToBakerState[component.GetInstanceID()];
            return builder.GetEntities();
        }


        public void UpdateTransforms(TransformAuthoringBaking transformAuthoringBaking)
        {
            transformAuthoringBaking.UpdateTransforms(_GameObjectToEntity, _ReferencedEntities, _IsReferencedEntitiesDirty);
        }

        private void ResetComponentAdditionalEntityCount(int authoringInstanceId, Entity entity)
        {
            _ComponentToAdditionalEntityCounter[authoringInstanceId] = 0;
            if (entity != Entity.Null && _EntityManager.HasBuffer<AdditionalEntitiesBakingData>(entity))
            {
                var buffer = _EntityManager.GetBuffer<AdditionalEntitiesBakingData>(entity);

                // Find the additional entities relative to the authoringID and remove them
                for (int index = buffer.Length - 1; index >= 0; --index)
                {
                    if (buffer[index].AuthoringComponentID == authoringInstanceId)
                    {
                        buffer.RemoveAt(index);
                    }
                }
            }
        }
    }
}
