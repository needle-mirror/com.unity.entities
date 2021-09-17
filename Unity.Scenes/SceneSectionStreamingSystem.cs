using System;
using Unity.Collections;
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

    [ExecuteAlways]
    [UpdateInGroup(typeof(SceneSystemGroup))]
    [UpdateAfter(typeof(ResolveSceneReferenceSystem))]
    public partial class SceneSectionStreamingSystem : SystemBase
    {
        internal enum StreamingStatus
        {
            NotYetProcessed,
            Loaded,
            Loading,
            FailedToLoad
        }

        internal struct StreamingState : ISystemStateComponentData
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

        World m_SynchronousSceneLoadWorld;

        const int k_InitialConcurrentSectionStreamCount = 4;
        int m_ConcurrentSectionStreamCount;
        Stream[] m_Streams = Array.Empty<Stream>();

        WeakAssetReferenceLoadingSystem _WeakAssetReferenceLoadingSystem;

        EntityQuery m_PendingStreamRequests;
        EntityQuery m_UnloadStreamRequests;
        EntityQuery m_SceneFilter;
        EntityQuery m_PublicRefFilter;
        EntityQuery m_SectionData;

        ProfilerMarker m_MoveEntitiesFrom = new ProfilerMarker("SceneStreaming.MoveEntitiesFrom");
        ProfilerMarker m_ExtractEntityRemapRefs = new ProfilerMarker("SceneStreaming.ExtractEntityRemapRefs");
        ProfilerMarker m_AddSceneSharedComponents = new ProfilerMarker("SceneStreaming.AddSceneSharedComponents");

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

        protected override void OnCreate()
        {
            ConcurrentSectionStreamCount = k_InitialConcurrentSectionStreamCount;
            _WeakAssetReferenceLoadingSystem = World.GetExistingSystem<WeakAssetReferenceLoadingSystem>();

            m_SynchronousSceneLoadWorld = new World("LoadingWorld (synchronous)", WorldFlags.Streaming);
            AddStreamingWorldSystems(m_SynchronousSceneLoadWorld);

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

            m_SceneFilter = GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadWrite<SceneTag>() },
                    Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab
                }
            );
        }

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
            AddStreamingWorldSystems(m_Streams[index].World);
        }

        struct ProcessAfterLoadRootGroups : DefaultWorldInitialization.IIdentifyRootGroups
        {
            public bool IsRootGroup(Type type) => type == typeof(ProcessAfterLoadGroup);
        }

        static void AddStreamingWorldSystems(World world)
        {
            var group = world.GetOrCreateSystem<ProcessAfterLoadGroup>();
            var systemTypes = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.ProcessAfterLoad);
            DefaultWorldInitialization.AddSystemToRootLevelSystemGroupsInternal(world, systemTypes, group, new ProcessAfterLoadRootGroups());
            group.SortSystems();
        }

        static NativeArray<Entity> GetExternalRefEntities(EntityManager manager, Allocator allocator)
        {
            using (var group = manager.CreateEntityQuery(typeof(ExternalEntityRefInfo)))
            {
                return group.ToEntityArray(allocator);
            }
        }

        NativeArray<SceneTag> ExternalRefToSceneTag(NativeArray<ExternalEntityRefInfo> externalEntityRefInfos, Entity sceneEntity, Allocator allocator)
        {
            var sceneTags = new NativeArray<SceneTag>(externalEntityRefInfos.Length, allocator);

            using (var sectionDataEntities = m_SectionData.ToEntityArray(Allocator.TempJob))
            using (var sectionData = m_SectionData.ToComponentDataArray<SceneSectionData>(Allocator.TempJob))
            using (var sceneEntityReference = m_SectionData.ToComponentDataArray<SceneEntityReference>(Allocator.TempJob))
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

        bool MoveEntities(EntityManager srcManager, Entity sectionEntity, ref Entity prefabRoot)
        {
            var sceneEntity = GetComponent<SceneEntityReference>(sectionEntity).SceneEntity;
            Assert.AreNotEqual(Entity.Null, sceneEntity);

            NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping;
            using (m_ExtractEntityRemapRefs.Auto())
            {
                if (!ExtractEntityRemapRefs(srcManager, sceneEntity, out entityRemapping))
                    return false;
            }

            var startCapacity = srcManager.EntityCapacity;
            using (m_AddSceneSharedComponents.Auto())
            {
#if UNITY_EDITOR
                var data = new EditorRenderData()
                {
                    SceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.DefaultSceneCullingMask | (1UL << 59),
                    PickableObject = EntityManager.HasComponent<SubScene>(sectionEntity) ? EntityManager.GetComponentObject<SubScene>(sectionEntity).gameObject : null
                };
                srcManager.AddSharedComponentData(srcManager.UniversalQuery, data);
#endif

                srcManager.AddSharedComponentData(srcManager.UniversalQuery, new SceneTag { SceneEntity = sectionEntity });
            }
            var endCapacity = srcManager.EntityCapacity;

            // ExtractEntityRemapRefs gathers entityRemapping based on Entities Capacity.
            // MoveEntitiesFrom below assumes that AddSharedComponentData on srcManager.UniversalQuery does not affect capacity.
            Assert.AreEqual(startCapacity, endCapacity);

            using (m_MoveEntitiesFrom.Auto())
            {
                EntityManager.MoveEntitiesFrom(srcManager, entityRemapping);
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

        bool ExtractEntityRemapRefs(EntityManager srcManager, Entity sceneEntity, out NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapping)
        {
            // External entity references are "virtual" entities. If we don't have any, only real entities need remapping
            int remapTableSize = srcManager.EntityCapacity;

            using (var externalRefEntities = GetExternalRefEntities(srcManager, Allocator.TempJob))
            {
                // We can potentially have several external entity reference arrays, each one pointing to a different scene
                var externalEntityRefInfos = new NativeArray<ExternalEntityRefInfo>(externalRefEntities.Length, Allocator.Temp);
                for (int i = 0; i < externalRefEntities.Length; ++i)
                {
                    // External references point to indices beyond the range used by the entities in this scene
                    // The highest index used by all those references defines how big the remap table has to be
                    externalEntityRefInfos[i] = srcManager.GetComponentData<ExternalEntityRefInfo>(externalRefEntities[i]);
                    var extRefs = srcManager.GetBuffer<ExternalEntityRef>(externalRefEntities[i]);
                    remapTableSize = math.max(remapTableSize, externalEntityRefInfos[i].EntityIndexStart + extRefs.Length);
                }

                // Within a scene, external scenes are identified by some ID
                // In the destination world, scenes are identified by an entity
                // Every entity coming from a scene needs to have a SceneTag that references the scene entity
                using (var sceneTags = ExternalRefToSceneTag(externalEntityRefInfos, sceneEntity, Allocator.TempJob))
                {
                    entityRemapping = new NativeArray<EntityRemapUtility.EntityRemapInfo>(remapTableSize, Allocator.TempJob);

                    for (int i = 0; i < externalRefEntities.Length; ++i)
                    {
                        var extRefs = srcManager.GetBuffer<ExternalEntityRef>(externalRefEntities[i]);
                        var extRefInfo = srcManager.GetComponentData<ExternalEntityRefInfo>(externalRefEntities[i]);

                        // A scene that external references point to is expected to have a single public reference array
                        m_PublicRefFilter.SetSharedComponentFilter(sceneTags[i]);
                        using (var pubRefEntities = m_PublicRefFilter.ToEntityArray(Allocator.TempJob))
                        {
                            if (pubRefEntities.Length == 0)
                            {
                                // If the array is missing, the external scene isn't loaded, we have to wait.
                                entityRemapping.Dispose();
                                return false;
                            }

                            var pubRefs = EntityManager.GetBuffer<PublicEntityRef>(pubRefEntities[0]);

                            // Proper mapping from external reference in section to entity in main world
                            for (int k = 0; k < extRefs.Length; ++k)
                            {
                                var srcIdx = extRefInfo.EntityIndexStart + k;
                                var target = pubRefs[extRefs[k].entityIndex].targetEntity;

                                // External references always have a version number of 1
                                entityRemapping[srcIdx] = new EntityRemapUtility.EntityRemapInfo
                                {
                                    SourceVersion = 1,
                                    Target = target
                                };
                            }
                        }

                        m_PublicRefFilter.ResetFilter();
                    }
                }
            }

            return true;
        }

        bool ProcessActiveStreams()
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
                    switch (UpdateLoadOperation(m_Streams[i].Operation, m_Streams[i].World, m_Streams[i].SectionEntity, moveEntities))
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

        UpdateLoadOperationResult UpdateLoadOperation(AsyncLoadSceneOperation operation, World streamingWorld, Entity sectionEntity, bool moveEntities)
        {
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
                                }

#if !UNITY_DOTSRUNTIME
                                if (bundles != null)
                                {
                                    EntityManager.AddSharedComponentData(sectionEntity, new SceneSectionBundle(bundles));
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
                                        _WeakAssetReferenceLoadingSystem.
                                            CompleteLoad(sceneEntity, prefabRoot, EntityManager.GetComponentData<WeakAssetPrefabLoadRequest>(sceneEntity).WeakReferenceId);
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
                            // RetainBlobAssets is a system state component and must thus be explicitly removed.
                            // Blob assets have at this point not yet been increased with refcount, so no leak should occurr
                            streamingManager.RemoveComponent<RetainBlobAssets>(streamingManager.UniversalQuery);
                            streamingManager.PrepareForDeserialize();

                            // Do this last just in case there are exceptions.
                            // So that SetLoadFailureOnEntity can set the state to failure
                            if (EntityManager.HasComponent<StreamingState>(sectionEntity))
                                EntityManager.RemoveComponent<StreamingState>(sectionEntity);

                            return UpdateLoadOperationResult.Aborted;
                        }
                    }
                    else
                    {
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
            }
        }

        bool SceneSectionRequiresSynchronousLoading(Entity entity) =>
            (EntityManager.GetComponentData<RequestSceneLoaded>(entity).LoadFlags & SceneLoadFlags.BlockOnStreamIn) != 0;

        protected override void OnUpdate()
        {
            // Sections > 0 need the external references from sections 0 and will wait for it to be loaded.
            // So we have to ensure sections 0 are loaded first, otherwise there's a risk of starving loading streams.
            if (!m_PendingStreamRequests.IsEmptyIgnoreFilter) {
                using (var entities = m_PendingStreamRequests.ToEntityArray(Allocator.TempJob))
                {
                    var priorityList = new NativeList<Entity>(Allocator.Temp);
                    var priorities = new NativeArray<int>(entities.Length, Allocator.Temp);
                    var sceneDataFromEntity = GetComponentDataFromEntity<SceneSectionData>();

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
                            var result = UpdateLoadOperation(operation, m_SynchronousSceneLoadWorld, entity, true);
                            operation.Dispose();

                            if (result == UpdateLoadOperationResult.Error)
                            {
                                m_SynchronousSceneLoadWorld.Dispose();
                                m_SynchronousSceneLoadWorld =
                                    new World("LoadingWorld (synchronous)", WorldFlags.Streaming);
                                AddStreamingWorldSystems(m_SynchronousSceneLoadWorld);
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
                foreach (var destroyScene in destroySubScenes)
                    UnloadSectionImmediate(destroyScene);
            }

            if (ProcessActiveStreams())
                EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();

#if !UNITY_DOTSRUNTIME
            // Process unloading bundles
            SceneBundleHandle.ProcessUnloadingBundles();
#endif
        }

        internal void UnloadSectionImmediate(Entity scene)
        {
            if (EntityManager.HasComponent<StreamingState>(scene))
            {
                m_SceneFilter.SetSharedComponentFilter(new SceneTag { SceneEntity = scene });

                EntityManager.DestroyEntity(m_SceneFilter);

                m_SceneFilter.ResetFilter();

                EntityManager.RemoveComponent<StreamingState>(scene);
            }
#if !UNITY_DOTSRUNTIME
            if (EntityManager.HasComponent<SceneSectionBundle>(scene))
                EntityManager.RemoveComponent<SceneSectionBundle>(scene);
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
            var blobHeaderOwner = EntityManager.GetSharedComponentData<BlobAssetOwner>(entity);
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
#if !UNITY_DISABLE_MANAGED_COMPONENTS
                PostLoadCommandBuffer = postLoadCommandBuffer
#endif
            });
        }
    }
}
