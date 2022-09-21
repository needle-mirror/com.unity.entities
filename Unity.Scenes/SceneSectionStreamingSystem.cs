using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Assertions;

namespace Unity.Scenes
{
    /// <summary>
    /// The group of systems that runs after a scene is loaded
    /// This allows for custom post processing of loaded SubScenes
    /// ie scene offsetting
    /// </summary>
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.ProcessAfterLoad)]
    public class ProcessAfterLoadGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// The tag component is added to scene entities when they are loaded.
    /// </summary>
    public struct IsSectionLoaded : IComponentData
    {}

    internal struct SceneSectionStreamingData : IComponentData
    {
        internal EntityQuery m_NestedScenes;
        internal EntityQuery m_SceneFilter;
    }

    /// <summary>
    /// System that controls streaming scene sections.
    /// </summary>
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(SceneSystemGroup))]
    [UpdateAfter(typeof(ResolveSceneReferenceSystem))]
    [BurstCompile]
    public partial class SceneSectionStreamingSystem : SystemBase
    {
        internal enum StreamingStatus
        {
            NotYetProcessed,
            Loaded,
            Loading,
            FailedToLoad
        }

        internal struct StreamingState : ICleanupComponentData
        {
            public StreamingStatus Status;
            public int            ActiveStreamIndex;
        }

        struct Stream
        {
            public World                   World;
            public Entity                  SectionEntity;
            public AsyncLoadSceneOperation Operation;
        }

        int m_MaximumWorldsMovedPerUpdate = 1;
        int m_MaximumSectionsUnloadedPerUpdate = -1;

        World m_SynchronousSceneLoadWorld;

        const int k_InitialConcurrentSectionStreamCount = 4;
        int m_ConcurrentSectionStreamCount;
        Stream[] m_Streams = Array.Empty<Stream>();

        EntityQuery m_PendingStreamRequests;
        EntityQuery m_UnloadStreamRequests;
        EntityQuery m_NestedScenesPending;
        EntityQuery m_PublicRefFilter;
        EntityQuery m_SectionData;

        readonly ProfilerMarker s_MoveEntitiesFrom = new ProfilerMarker("SceneStreaming.MoveEntitiesFrom");
        readonly ProfilerMarker s_ExtractEntityRemapRefs = new ProfilerMarker("SceneStreaming.ExtractEntityRemapRefs");
        readonly ProfilerMarker s_AddSceneSharedComponents = new ProfilerMarker("SceneStreaming.AddSceneSharedComponents");
#if !UNITY_DOTSRUNTIME
        static readonly ProfilerMarker s_UnloadSectionImmediate = new ProfilerMarker("SceneStreaming." + nameof(UnloadSectionImmediate));
#endif
        readonly ProfilerMarker s_MoveEntities = new ProfilerMarker("SceneStreaming." + nameof(MoveEntities));
        readonly ProfilerMarker s_UpdateLoadOperation = new ProfilerMarker("SceneStreaming." + nameof(UpdateLoadOperation));

        /// <summary>
        /// The maximum amount of sections that will be streamed in concurrently.
        /// This defaults to 4.
        /// </summary>
        public int ConcurrentSectionStreamCount
        {
            get => m_ConcurrentSectionStreamCount;
            set
            {
                if (value > m_Streams.Length)
                {
                    Array.Resize(ref m_Streams, value);
                    for (int i = m_ConcurrentSectionStreamCount; i < value; ++i)
                    {
                        if (m_Streams[i].World == null)
                            CreateStreamWorld(i);
                    }
                }
                else
                {
                    for (int i = value; i < m_Streams.Length; ++i)
                    {
                        if ((m_Streams[i].Operation == null) && (m_Streams[i].World != null))
                            DestroyStreamWorld(i);
                    }
                }
                m_ConcurrentSectionStreamCount = value;
            }
        }

        /// <summary>
        /// The maximum amount of streaming worlds that will be moved into the main world per update.
        /// This defaults to 1.
        /// </summary>
        public int MaximumWorldsMovedPerUpdate
        {
            get => m_MaximumWorldsMovedPerUpdate;
            set => m_MaximumWorldsMovedPerUpdate = value;
        }

        /// <summary>
        /// The maximum number of scene sections that will be unloaded per update tick. A value equal to or below zero
        /// indicates that the number of sections to unload per update is unlimited.
        /// This defaults to a negative value.
        /// </summary>
        public int MaximumSectionsUnloadedPerUpdate
        {
            get => m_MaximumSectionsUnloadedPerUpdate;
            set => m_MaximumSectionsUnloadedPerUpdate = value;
        }

        // Exposed for testing
        internal int StreamArrayLength => m_Streams.Length;
        internal bool AllStreamsComplete
        {
            get
            {
                for (int i = 0; i < m_Streams.Length; ++i) {
                    if (m_Streams[i].Operation != null && !m_Streams[i].Operation.IsCompleted)
                        return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Called when this system is created.
        /// </summary>
        protected override void OnCreate()
        {
            using var marker = new ProfilerMarker("SceneSectionStreamingSystem.OnCreate").Auto();

            ConcurrentSectionStreamCount = k_InitialConcurrentSectionStreamCount;

            m_SynchronousSceneLoadWorld = new World("LoadingWorld (synchronous)", WorldFlags.Streaming);

            m_PendingStreamRequests = GetEntityQuery(new EntityQueryDesc()
            {
                All = new[] { ComponentType.ReadWrite<RequestSceneLoaded>(), ComponentType.ReadWrite<SceneSectionData>(), ComponentType.ReadWrite<ResolvedSectionPath>() },
                None = new[] { ComponentType.ReadWrite<StreamingState>(), ComponentType.ReadWrite<DisableSceneResolveAndLoad>() }
            });

            m_UnloadStreamRequests = GetEntityQuery(new EntityQueryDesc()
            {
                All = new[] { ComponentType.ReadWrite<StreamingState>() },
                None = new[] { ComponentType.ReadWrite<RequestSceneLoaded>(), ComponentType.ReadWrite<DisableSceneResolveAndLoad>() }
            });

            m_NestedScenesPending = GetEntityQuery(new EntityQueryDesc()
            {
                All = new[] { ComponentType.ReadWrite<RequestSceneLoaded>(), ComponentType.ReadWrite<SceneTag>() },
                None = new[] { ComponentType.ReadWrite<StreamingState>(), ComponentType.ReadWrite<DisableSceneResolveAndLoad>() }
            });

            EntityManager.AddComponentData(SystemHandle, new SceneSectionStreamingData
            {
                m_NestedScenes = GetEntityQuery(new EntityQueryDesc()
                {
                    All = new[] { ComponentType.ReadWrite<RequestSceneLoaded>(), ComponentType.ReadWrite<SceneTag>() },
                }),
                m_SceneFilter = GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadWrite<SceneTag>() },
                    Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
                }),
            });

            m_PublicRefFilter = GetEntityQuery
                (
                ComponentType.ReadWrite<SceneTag>(),
                ComponentType.ReadWrite<PublicEntityRef>()
                );

            m_SectionData = GetEntityQuery
                (
                ComponentType.ReadWrite<SceneSectionData>(),
                ComponentType.ReadWrite<SceneEntityReference>()
                );
        }

        /// <summary>
        /// Called when this system is destroyed.
        /// </summary>
        protected override void OnDestroy()
        {
            for (int i = 0; i != m_Streams.Length; i++)
            {
                m_Streams[i].Operation?.Dispose();
                if(m_Streams[i].World != null)
                    DestroyStreamWorld(i);
            }

            m_SynchronousSceneLoadWorld.Dispose();
        }

        void DestroyStreamWorld(int index)
        {
            m_Streams[index].World.Dispose();
            m_Streams[index].World = null;
            m_Streams[index].Operation = null;
        }

        void CreateStreamWorld(int index)
        {
            m_Streams[index].World = new World("LoadingWorld" + index, WorldFlags.Streaming);
        }

        struct ProcessAfterLoadRootGroups : DefaultWorldInitialization.IIdentifyRootGroups
        {
            public bool IsRootGroup(Type type) => type == typeof(ProcessAfterLoadGroup);
        }

        static internal void AddStreamingWorldSystems(World world)
        {
            using var marker = new ProfilerMarker("AddSystems").Auto();

            var group = world.GetOrCreateSystemManaged<ProcessAfterLoadGroup>();
            var systemTypes = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.ProcessAfterLoad);
            DefaultWorldInitialization.AddSystemToRootLevelSystemGroupsInternal(world, systemTypes, group, new ProcessAfterLoadRootGroups());
            group.SortSystems();
        }

        static unsafe NativeArray<Entity> GetExternalRefEntities(EntityManager manager, Allocator allocator)
        {
            var type = ComponentType.ReadOnly<ExternalEntityRefInfo>();
            using (var group = manager.CreateEntityQuery(&type, 1))
            {
                return group.ToEntityArray(allocator);
            }
        }

        static NativeArray<SceneTag> ExternalRefToSceneTag(NativeArray<ExternalEntityRefInfo> externalEntityRefInfos, Entity sceneEntity, EntityQuery sectionDataQuery, Allocator allocator)
        {
            var sceneTags = new NativeArray<SceneTag>(externalEntityRefInfos.Length, allocator);

            using (var sectionDataEntities = sectionDataQuery.ToEntityArray(Allocator.TempJob))
            using (var sectionData = sectionDataQuery.ToComponentDataArray<SceneSectionData>(Allocator.TempJob))
            using (var sceneEntityReference = sectionDataQuery.ToComponentDataArray<SceneEntityReference>(Allocator.TempJob))
            {
                for (int i = 0; i < sectionData.Length; ++i)
                {
                    for (int j = 0; j < externalEntityRefInfos.Length; ++j)
                    {
                        if (
                            sceneEntity == sceneEntityReference[i].SceneEntity &&
                            externalEntityRefInfos[j].SceneGUID == sectionData[i].SceneGUID &&
                            externalEntityRefInfos[j].SubSectionIndex == sectionData[i].SubSectionIndex
                        )
                        {
                            sceneTags[j] = new SceneTag { SceneEntity = sectionDataEntities[i] };
                            break;
                        }
                    }
                }
            }

            return sceneTags;
        }

        struct EntityRemapArgs
        {
            public EntityManager EntityManager;
            public EntityManager SrcManager;
            public Entity SceneEntity;
            public EntityQuery SectionData;
            public EntityQuery PublicRefFilter;

            public NativeArray<EntityRemapUtility.EntityRemapInfo> OutEntityRemapping;
        }

        bool MoveEntities(EntityManager srcManager, Entity sectionEntity, ref Entity prefabRoot)
        {
            using var marker = s_MoveEntities.Auto();
            var sceneEntity = GetComponent<SceneEntityReference>(sectionEntity).SceneEntity;
            Assert.AreNotEqual(Entity.Null, sceneEntity);

            NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping;
            using (s_ExtractEntityRemapRefs.Auto())
            {
                var remapArgs = new EntityRemapArgs
                {
                    EntityManager = EntityManager,
                    SectionData = m_SectionData,
                    PublicRefFilter = m_PublicRefFilter,
                    SrcManager = srcManager,
                    SceneEntity = sceneEntity,
                };
                if (!ExtractEntityRemapRefs(ref remapArgs))
                    return false;
                entityRemapping = remapArgs.OutEntityRemapping;
            }

            var startCapacity = srcManager.EntityCapacity;
#if UNITY_EDITOR
            using (s_AddSceneSharedComponents.Auto())
            {
                var data = new EditorRenderData()
                {
                    SceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.DefaultSceneCullingMask | (1UL << 59),
                    PickableObject = EntityManager.HasComponent<SubScene>(sectionEntity) ? EntityManager.GetComponentObject<SubScene>(sectionEntity).gameObject : null
                };
                srcManager.AddSharedComponentManaged(srcManager.UniversalQuery, data);
            }
#endif
            var endCapacity = srcManager.EntityCapacity;

            // ExtractEntityRemapRefs gathers entityRemapping based on Entities Capacity.
            // MoveEntitiesFrom below assumes that AddSharedComponentData on srcManager.UniversalQuery does not affect capacity.
            Assert.AreEqual(startCapacity, endCapacity);

            using (s_MoveEntitiesFrom.Auto())
            {
                using (var builder = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<SystemInstance>())
                {
                    using (var query = srcManager.CreateEntityQuery(builder))
                    {
                        if (query.CalculateEntityCount() != 0)
                        {
                            throw new InvalidOperationException("System entities can not exist in the streaming world");
                        }
                    }

                    EntityManager.MoveEntitiesFrom(srcManager, entityRemapping);
                }
            }

            var sharedComponentFilter = new SceneTag {SceneEntity = sectionEntity};
            Entities.WithSharedComponentFilter(sharedComponentFilter).ForEach((ref ExternalEntityRefInfo sceneRef) =>
            {
                sceneRef.SceneRef = sceneEntity;
            }).Run();

            if (prefabRoot != Entity.Null)
                prefabRoot = EntityRemapUtility.RemapEntity(ref entityRemapping, prefabRoot);

            entityRemapping.Dispose();
            srcManager.PrepareForDeserialize();

            return true;
        }

        [BurstCompile]
        static bool ExtractEntityRemapRefs(ref EntityRemapArgs args)
        {
            // External entity references are "virtual" entities. If we don't have any, only real entities need remapping
            int remapTableSize = args.SrcManager.EntityCapacity;

            using (var externalRefEntities = GetExternalRefEntities(args.SrcManager, Allocator.TempJob))
            {
                // We can potentially have several external entity reference arrays, each one pointing to a different scene
                var externalEntityRefInfos = new NativeArray<ExternalEntityRefInfo>(externalRefEntities.Length, Allocator.Temp);
                for (int i = 0; i < externalRefEntities.Length; ++i)
                {
                    // External references point to indices beyond the range used by the entities in this scene
                    // The highest index used by all those references defines how big the remap table has to be
                    externalEntityRefInfos[i] = args.SrcManager.GetComponentData<ExternalEntityRefInfo>(externalRefEntities[i]);
                    var extRefs = args.SrcManager.GetBuffer<ExternalEntityRef>(externalRefEntities[i]);
                    remapTableSize = math.max(remapTableSize, externalEntityRefInfos[i].EntityIndexStart + extRefs.Length);
                }

                // Within a scene, external scenes are identified by some ID
                // In the destination world, scenes are identified by an entity
                // Every entity coming from a scene needs to have a SceneTag that references the scene entity
                using (var sceneTags = ExternalRefToSceneTag(externalEntityRefInfos, args.SceneEntity, args.SectionData, Allocator.TempJob))
                {
                    args.OutEntityRemapping = new NativeArray<EntityRemapUtility.EntityRemapInfo>(remapTableSize, Allocator.TempJob);

                    for (int i = 0; i < externalRefEntities.Length; ++i)
                    {
                        var extRefs = args.SrcManager.GetBuffer<ExternalEntityRef>(externalRefEntities[i]);
                        var extRefInfo = args.SrcManager.GetComponentData<ExternalEntityRefInfo>(externalRefEntities[i]);

                        // A scene that external references point to is expected to have a single public reference array
                        args.PublicRefFilter.SetSharedComponentFilter(sceneTags[i]);
                        using (var pubRefEntities = args.PublicRefFilter.ToEntityArray(Allocator.TempJob))
                        {
                            if (pubRefEntities.Length == 0)
                            {
                                // If the array is missing, the external scene isn't loaded, we have to wait.
                                args.OutEntityRemapping.Dispose();
                                return false;
                            }

                            var pubRefs = args.EntityManager.GetBuffer<PublicEntityRef>(pubRefEntities[0]);

                            // Proper mapping from external reference in section to entity in main world
                            for (int k = 0; k < extRefs.Length; ++k)
                            {
                                var srcIdx = extRefInfo.EntityIndexStart + k;
                                var target = pubRefs[extRefs[k].entityIndex].targetEntity;

                                // External references always have a version number of 1
                                args.OutEntityRemapping[srcIdx] = new EntityRemapUtility.EntityRemapInfo
                                {
                                    SourceVersion = 1,
                                    Target = target
                                };
                            }
                        }

                        args.PublicRefFilter.ResetFilter();
                    }
                }
            }

            return true;
        }

        bool ProcessActiveStreams(ref SystemState state)
        {
            bool needsMoreProcessing = false;
            int moveEntitiesFromProcessed = 0;

            int lastOperationIndex = -1;

            for (int i = 0; i != m_Streams.Length; i++)
            {
                if (m_Streams[i].Operation != null)
                {
                    needsMoreProcessing = true;

                    bool moveEntities = moveEntitiesFromProcessed < m_MaximumWorldsMovedPerUpdate;
                    switch (UpdateLoadOperation(ref state, m_Streams[i].Operation, m_Streams[i].World, m_Streams[i].SectionEntity, moveEntities))
                    {
                        case UpdateLoadOperationResult.Completed:
                        {
                            moveEntitiesFromProcessed += 1;
                            m_Streams[i].Operation.Dispose();
                            m_Streams[i].Operation = null;
                            break;
                        }
                        case UpdateLoadOperationResult.Aborted:
                        {
                            m_Streams[i].Operation.Dispose();
                            m_Streams[i].Operation = null;
                            break;
                        }
                        case UpdateLoadOperationResult.Error:
                        {
                            DestroyStreamWorld(i);
                            if (i < m_ConcurrentSectionStreamCount)
                                CreateStreamWorld(i);
                            break;
                        }
                        case UpdateLoadOperationResult.Busy:
                        {
                            lastOperationIndex = i;
                            // carry on
                            break;
                        }
                        default:
                        {
                            throw new ArgumentOutOfRangeException();
                        }
                    }
                }
            }

            if((m_Streams.Length > m_ConcurrentSectionStreamCount) && (lastOperationIndex < m_ConcurrentSectionStreamCount))
            {
                for (int i = m_ConcurrentSectionStreamCount; i < m_Streams.Length; ++i)
                {
                    m_Streams[i].World?.Dispose();
                }
                Array.Resize(ref m_Streams, m_ConcurrentSectionStreamCount);
            }

            return needsMoreProcessing;
        }

        enum UpdateLoadOperationResult
        {
            Busy,
            Aborted,
            Completed,
            Error,
        }

        unsafe UpdateLoadOperationResult UpdateLoadOperation(
            ref SystemState systemState,
            AsyncLoadSceneOperation operation,
            World streamingWorld,
            Entity sectionEntity,
            bool moveEntities)
        {
            using var marker = s_UpdateLoadOperation.Auto();
            operation.Update();

            var streamingManager = streamingWorld.EntityManager;

            if (operation.IsCompleted)
            {
                try
                {
                    // Loading failed, EntityManager is in unknown state. Just wipe it out and create a clean one.
                    if (operation.ErrorStatus == null)
                    {
                        streamingManager.EndExclusiveEntityTransaction();

                        Entity prefabRoot = operation.DeserializationResult.PrefabRoot;

                        if (EntityManager.HasComponent<RequestSceneLoaded>(sectionEntity))
                        {
                            if(!moveEntities)
                                return UpdateLoadOperationResult.Busy;

                            if (MoveEntities(streamingManager, sectionEntity, ref prefabRoot))
                            {
#if !UNITY_DOTSRUNTIME
                                var bundles = operation.StealBundles();
#endif

                                if (EntityManager.HasComponent<StreamingState>(sectionEntity))
                                {
                                    var state = EntityManager.GetComponentData<StreamingState>(sectionEntity);
                                    state.Status = StreamingStatus.Loaded;
                                    EntityManager.SetComponentData(sectionEntity, state);
                                    EntityManager.AddComponentData(sectionEntity, default(IsSectionLoaded));
                                }

#if !UNITY_DOTSRUNTIME
                                if (bundles != null)
                                {
                                    EntityManager.AddSharedComponentManaged(sectionEntity, new SceneSectionBundle(bundles));
                                    foreach (var b in bundles)
                                        b.Release();
                                }
#endif
                                if (prefabRoot != Entity.Null)
                                {
                                    var sceneEntity = EntityManager.GetComponentData<SceneEntityReference>(sectionEntity).SceneEntity;
                                    EntityManager.AddComponentData(sceneEntity, new PrefabRoot {Root = prefabRoot});
                                    if (EntityManager.HasComponent<WeakAssetPrefabLoadRequest>(sceneEntity))
                                    {
                                        WeakAssetReferenceLoadingSystem.CompleteLoad(ref systemState,
                                            sceneEntity,
                                            prefabRoot,
                                            EntityManager.GetComponentData<WeakAssetPrefabLoadRequest>(sceneEntity)
                                                .WeakReferenceId);
                                    }
                                }

                                // if this section was loaded with block on import, propagate those flags to immediate children
                                RequestSceneLoaded parentRequestSceneLoaded = EntityManager.GetComponentData<RequestSceneLoaded>(sectionEntity);
                                if ((parentRequestSceneLoaded.LoadFlags & (SceneLoadFlags.BlockOnImport|SceneLoadFlags.BlockOnStreamIn)) != 0)
                                {
                                    m_NestedScenesPending.SetSharedComponentFilter(new SceneTag { SceneEntity = sectionEntity });
                                    using (NativeArray<Entity> entities = m_NestedScenesPending.ToEntityArray(Allocator.Temp))
                                    {
                                        for (int i = 0; i < entities.Length; ++i)
                                        {
                                            Entity entity = entities[i];
                                            RequestSceneLoaded requestSceneLoaded = EntityManager.GetComponentData<RequestSceneLoaded>(entity);
                                            requestSceneLoaded.LoadFlags |= parentRequestSceneLoaded.LoadFlags & (SceneLoadFlags.BlockOnImport|SceneLoadFlags.BlockOnStreamIn);
                                            EntityManager.SetComponentData<RequestSceneLoaded>(entity, requestSceneLoaded);
                                        }
                                    }
                                }

                                return UpdateLoadOperationResult.Completed;
                            }
                            else
                            {
                                // Debug.Log("MoveEntities on hold, waiting for main section");
                            }
                        }
                        // The SubScene is no longer being requested for load
                        else
                        {
                            streamingManager.DestroyEntity(streamingManager.UniversalQuery);
                            // RetainBlobAssets is a cleanup component and must thus be explicitly removed.
                            // Blob assets have at this point not yet been increased with refcount, so no leak should occurr
                            streamingManager.RemoveComponent<RetainBlobAssets>(streamingManager.UniversalQuery);
                            streamingManager.PrepareForDeserialize();

                            EntityManager.RemoveComponent<IsSectionLoaded>(sectionEntity);
                            // Do this last just in case there are exceptions.
                            // So that SetLoadFailureOnEntity can set the state to failure
                            if (EntityManager.HasComponent<StreamingState>(sectionEntity))
                                EntityManager.RemoveComponent<StreamingState>(sectionEntity);

                            return UpdateLoadOperationResult.Aborted;
                        }
                    }
                    else
                    {
                        if (operation.Exception != null)
                            Debug.LogException(operation.Exception);
                        Debug.LogWarning($"Error when processing '{operation}': {operation.ErrorStatus}");
                        SetLoadFailureOnEntity(sectionEntity);

                        return UpdateLoadOperationResult.Error;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Error when loading '{operation}': {e}");
                    SetLoadFailureOnEntity(sectionEntity);

                    return UpdateLoadOperationResult.Error;
                }
            }

            return UpdateLoadOperationResult.Busy;
        }

        void SetLoadFailureOnEntity(Entity sceneEntity)
        {
            // If load fails, don't try to load the requestScene again.
            if (EntityManager.HasComponent<StreamingState>(sceneEntity))
            {
                var state = EntityManager.GetComponentData<StreamingState>(sceneEntity);
                state.Status = StreamingStatus.FailedToLoad;
                EntityManager.SetComponentData(sceneEntity, state);
                EntityManager.RemoveComponent<IsSectionLoaded>(sceneEntity);
            }
        }

        bool SceneSectionRequiresSynchronousLoading(Entity entity) =>
            (EntityManager.GetComponentData<RequestSceneLoaded>(entity).LoadFlags & SceneLoadFlags.BlockOnStreamIn) != 0;

        /// <summary>
        /// Called when this system is updated.
        /// </summary>
        protected override unsafe void OnUpdate()
        {
            // Sections > 0 need the external references from sections 0 and will wait for it to be loaded.
            // So we have to ensure sections 0 are loaded first, otherwise there's a risk of starving loading streams.
            if (!m_PendingStreamRequests.IsEmptyIgnoreFilter) {
                using (var entities = m_PendingStreamRequests.ToEntityArray(Allocator.TempJob))
                {
                    var priorityList = new NativeList<Entity>(Allocator.Temp);
                    var priorities = new NativeArray<int>(entities.Length, Allocator.Temp);
                    var sceneDataFromEntity = GetComponentLookup<SceneSectionData>();

                    for (int i = 0; i < entities.Length; ++i)
                    {
                        var entity = entities[i];

                        if (SceneSectionRequiresSynchronousLoading(entity))
                        {
                            var streamingState = new StreamingState
                                {ActiveStreamIndex = -1, Status = StreamingStatus.NotYetProcessed};
                            EntityManager.AddComponentData(entity, streamingState);

                            priorities[i] = 0;
                            var operation = CreateAsyncLoadSceneOperation(m_SynchronousSceneLoadWorld.EntityManager,
                                entity, true);
                            var result = UpdateLoadOperation(ref *m_StatePtr, operation, m_SynchronousSceneLoadWorld, entity, true);
                            operation.Dispose();

                            if (result == UpdateLoadOperationResult.Error)
                            {
                                m_SynchronousSceneLoadWorld.Dispose();
                                m_SynchronousSceneLoadWorld =
                                    new World("LoadingWorld (synchronous)", WorldFlags.Streaming);
                            }
                            Assert.AreNotEqual(UpdateLoadOperationResult.Aborted, result);
                        }
                        else if (sceneDataFromEntity[entity].SubSectionIndex == 0)
                            priorities[i] = 1;
                        else
                            priorities[i] = 2;
                    }

                    for (int priority = 1; priority <= 2; ++priority)
                    {
                        for (int i = 0; i < entities.Length; ++i)
                        {
                            if (priorityList.Length == m_ConcurrentSectionStreamCount)
                            {
                                break;
                            }

                            if (priorities[i] == priority)
                                priorityList.Add(entities[i]);
                        }
                    }

                    var priorityArray = priorityList.AsArray();

                    foreach (var entity in priorityArray)
                    {
                        var streamIndex = CreateAsyncLoadScene(entity, false);
                        if (streamIndex != -1)
                        {
                            var streamingState = new StreamingState
                                {ActiveStreamIndex = streamIndex, Status = StreamingStatus.NotYetProcessed};
                            EntityManager.AddComponentData(entity, streamingState);
                        }
                    }
                }
            }

            if (!m_UnloadStreamRequests.IsEmptyIgnoreFilter)
            {
                var destroySubScenes = m_UnloadStreamRequests.ToEntityArray(Allocator.Temp);

                var count = destroySubScenes.Length;
                if (count > m_MaximumSectionsUnloadedPerUpdate && m_MaximumSectionsUnloadedPerUpdate > 0)
                    count = m_MaximumSectionsUnloadedPerUpdate;
                for (int i = 0; i < count; i++)
                    UnloadSectionImmediate(World.Unmanaged, destroySubScenes[i]);
            }

            if (ProcessActiveStreams(ref *m_StatePtr))
                EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();

#if !UNITY_DOTSRUNTIME
            // Process unloading bundles
            SceneBundleHandle.ProcessUnloadingBundles();
#endif
        }

        internal static void UnloadSectionImmediate(WorldUnmanaged world, Entity scene)
        {
#if !UNITY_DOTSRUNTIME
            using var marker = s_UnloadSectionImmediate.Auto();
#endif
            ref var singleton =
                ref world.GetExistingSystemState<SceneSectionStreamingSystem>().GetSingletonEntityQueryInternal(
                    ComponentType.ReadWrite<SceneSectionStreamingData>())
                .GetSingletonRW<SceneSectionStreamingData>().ValueRW;

            if (world.EntityManager.HasComponent<StreamingState>(scene))
            {
                // unload nested scenes first.
                singleton.m_NestedScenes.SetSharedComponentFilter(new SceneTag { SceneEntity = scene });
                if (!singleton.m_NestedScenes.IsEmptyIgnoreFilter)
                {
                    using (var nestedSubscenes = singleton.m_NestedScenes.ToEntityArray(Allocator.Temp))
                    {
                        SceneSystem.UnloadParameters nestedSceneUnloadParams = new SceneSystem.UnloadParameters();
                        nestedSceneUnloadParams |= SceneSystem.UnloadParameters.DestroySectionProxyEntities;

                        for (int i=0; i<nestedSubscenes.Length; ++i)
                        {
                            SceneSystem.UnloadScene(world, nestedSubscenes[i], nestedSceneUnloadParams);
                        }
                    }
                }

                singleton.m_SceneFilter.SetSharedComponentFilter(new SceneTag { SceneEntity = scene });

                world.EntityManager.DestroyEntity(singleton.m_SceneFilter);

                singleton.m_SceneFilter.ResetFilter();

                world.EntityManager.RemoveComponent<IsSectionLoaded>(scene);
                world.EntityManager.RemoveComponent<StreamingState>(scene);
            }
#if !UNITY_DOTSRUNTIME
            if (world.EntityManager.HasComponent<SceneSectionBundle>(scene))
                world.EntityManager.RemoveComponent<SceneSectionBundle>(scene);
#endif
        }

        int CreateAsyncLoadScene(Entity entity, bool blockUntilFullyLoaded)
        {
            for (int i = 0; i != m_ConcurrentSectionStreamCount; i++)
            {
                if (m_Streams[i].Operation != null)
                    continue;

                var dstManager = m_Streams[i].World.EntityManager;
                m_Streams[i].Operation = CreateAsyncLoadSceneOperation(dstManager, entity, blockUntilFullyLoaded);
                m_Streams[i].SectionEntity = entity;
                return i;
            }

            return -1;
        }

        AsyncLoadSceneOperation CreateAsyncLoadSceneOperation(EntityManager dstManager, Entity entity, bool blockUntilFullyLoaded)
        {
            var sceneData = EntityManager.GetComponentData<SceneSectionData>(entity);
            var blobHeaderOwner = EntityManager.GetSharedComponent<BlobAssetOwner>(entity);
            blobHeaderOwner.Retain();
            var sectionData = EntityManager.GetComponentData<ResolvedSectionPath>(entity);

            var entitiesBinaryPath = sectionData.ScenePath.ToString();
            var resourcesPath = sectionData.HybridPath.ToString();
            NativeArray<Entities.Hash128> dependencies = default;
            if (EntityManager.HasComponent<BundleElementData>(entity))
            {
                var depBuffer = EntityManager.GetBuffer<BundleElementData>(entity);
                dependencies = new NativeArray<Entities.Hash128>(depBuffer.AsNativeArray().Reinterpret<Entities.Hash128>(), Allocator.Persistent);
            }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
            PostLoadCommandBuffer postLoadCommandBuffer = null;
            if (EntityManager.HasComponent<PostLoadCommandBuffer>(entity))
            {
                postLoadCommandBuffer = EntityManager.GetComponentData<PostLoadCommandBuffer>(entity);
            }
            else if (EntityManager.HasComponent<SceneEntityReference>(entity))
            {
                var sceneEntity = EntityManager.GetComponentData<SceneEntityReference>(entity).SceneEntity;
                if (EntityManager.HasComponent<PostLoadCommandBuffer>(sceneEntity))
                {
                    postLoadCommandBuffer = EntityManager.GetComponentData<PostLoadCommandBuffer>(sceneEntity);
                }
            }

            if (postLoadCommandBuffer != null)
                postLoadCommandBuffer = (PostLoadCommandBuffer)postLoadCommandBuffer.Clone();
#endif
            return new AsyncLoadSceneOperation(new AsyncLoadSceneData
            {
                ScenePath = entitiesBinaryPath,
                SceneSize = sceneData.DecompressedFileSize,
                CompressedSceneSize = sceneData.FileSize,
                Codec = sceneData.Codec,
                ExpectedObjectReferenceCount = sceneData.ObjectReferenceCount,
                ResourcesPathObjRefs = resourcesPath,
                EntityManager = dstManager,
                BlockUntilFullyLoaded = blockUntilFullyLoaded,
                Dependencies = dependencies,
                BlobHeader = sceneData.BlobHeader,
                BlobHeaderOwner = blobHeaderOwner,
                SceneSectionEntity = entity,
#if !UNITY_DISABLE_MANAGED_COMPONENTS
                PostLoadCommandBuffer = postLoadCommandBuffer
#endif
            });
        }
    }
}
