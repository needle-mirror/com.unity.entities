//#define LOG_RESOLVING

using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    public struct ResolvedSectionEntity : IBufferElementData
    {
        public Entity SectionEntity;
    }

    struct BundleElementData : IBufferElementData
    {
        public Hash128 BundleId;
    }

    struct SceneEntityReference : IComponentData
    {
        public Entity SceneEntity;
    }

    struct ResolvedSceneHash : IComponentData
    {
        public Hash128 ArtifactHash;
    }

    struct ResolvedSectionPath : IComponentData
    {
        //@TODO: Switch back to NativeString512 once bugs are fixed
        public Words ScenePath;
        public Words HybridPath;
    }

    struct SceneSectionCustomMetadata
    {
        public ulong StableTypeHash;
        public BlobArray<byte> Data;
    }

    struct SceneMetaData
    {
        public BlobArray<SceneSectionData> Sections;
        public BlobString                  SceneName;
        public BlobArray<BlobArray<Hash128>> Dependencies;
        public BlobArray<BlobArray<SceneSectionCustomMetadata>> SceneSectionCustomMetadata;
    }

    public struct DisableSceneResolveAndLoad : IComponentData
    {
    }

    static class SceneMetaDataSerializeUtility
    {
        public static readonly int CurrentFileFormatVersion = 3;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Scenes are made out of sections, but to find out how many sections there are and extract their data like bounding volume or file size.
    /// The meta data for the scene has to be loaded first.
    /// ResolveSceneReferenceSystem creates section entities for each scene by loading the scenesection's metadata from disk.
    /// </summary>
    [UnityEngine.ExecuteAlways]
    [AlwaysUpdateSystem]
    [UpdateInGroup(typeof(SceneSystemGroup))]
    [UpdateAfter(typeof(SceneSystem))]
    class ResolveSceneReferenceSystem : SystemBase
    {
        struct AssetDependencyTrackerState : ISystemStateComponentData
        {
            public UnityEditor.GUID SceneAndBuildConfigGUID;
        }

        EntityQuery m_AddScenes;
        EntityQuery m_RemoveScenes;
        EntityQueryMask m_ValidSceneMask;

        AssetDependencyTracker<Entity> _AssetDependencyTracker;
        NativeList<AssetDependencyTracker<Entity>.Completed> _Changed;

        [Conditional("LOG_RESOLVING")]
        void LogResolving(string type, Hash128 sceneGUID)
        {
            Debug.Log(type + ": " + sceneGUID);
        }

        [Conditional("LOG_RESOLVING")]
        void LogResolving(string log)
        {
            Debug.Log(log);
        }

        protected override void OnUpdate()
        {
            SceneWithBuildConfigurationGUIDs.ValidateBuildSettingsCache();

            var sceneSystem = World.GetExistingSystem<SceneSystem>();
            var buildConfigurationGUID = sceneSystem.BuildConfigurationGUID;

            // Add scene entities that haven't been encountered yet
            if (!m_AddScenes.IsEmptyIgnoreFilter)
            {
                //@TODO: Should use Entities.ForEach but we are missing
                // 1. Entities.ForEach support with execute always (ILPP compilation not taking effect on first domain reload)
                // 2. Entities.ForEach not supporting explicit queries

                using (var addScenes = m_AddScenes.ToEntityArray(Allocator.TempJob))
                {
                    var trackerStates = new NativeArray<AssetDependencyTrackerState>(addScenes.Length, Allocator.Temp);
                    for (int i = 0; i != addScenes.Length; i++)
                    {
                        var sceneEntity = addScenes[i];
                        var scene = EntityManager.GetComponentData<SceneReference>(sceneEntity);
                        var requestSceneLoaded = EntityManager.GetComponentData<RequestSceneLoaded>(sceneEntity);

                        var guid = SceneWithBuildConfigurationGUIDs.EnsureExistsFor(scene.SceneGUID,
                            buildConfigurationGUID, out var requireRefresh);
                        var async = (requestSceneLoaded.LoadFlags & SceneLoadFlags.BlockOnImport) == 0;

                        LogResolving(async ? "Adding Async" : "Adding Sync", guid);

                        _AssetDependencyTracker.Add(guid, sceneEntity, async);
                        if (requireRefresh)
                            _AssetDependencyTracker.RequestRefresh();

                        trackerStates[i] = new AssetDependencyTrackerState {SceneAndBuildConfigGUID = guid};
                    }

                    EntityManager.AddComponentData(m_AddScenes, trackerStates);
                    trackerStates.Dispose();
                }
            }

            // Remove scene entities that were added and should no longer be tracked
            if (!m_RemoveScenes.IsEmptyIgnoreFilter)
            {
                using (var removeEntities = m_RemoveScenes.ToEntityArray(Allocator.TempJob))
                using (var removeGuids =
                    m_RemoveScenes.ToComponentDataArray<AssetDependencyTrackerState>(Allocator.TempJob))
                {
                    for (int i = 0; i != removeEntities.Length; i++)
                    {
                        LogResolving("Removing", removeGuids[i].SceneAndBuildConfigGUID);
                        _AssetDependencyTracker.Remove(removeGuids[i].SceneAndBuildConfigGUID, removeEntities[i]);
                    }
                }

                EntityManager.RemoveComponent<AssetDependencyTrackerState>(m_RemoveScenes);
            }

            // Process any scenes that have completed their asset import
            var isDone = _AssetDependencyTracker.GetCompleted(_Changed);
            foreach (var change in _Changed)
            {
                var sceneEntity = change.UserKey;
                LogResolving($"Resolving: {change.Asset} -> {change.ArtifactID}");

                if (!m_ValidSceneMask.Matches(sceneEntity))
                    throw new InvalidOperationException("entity should have been removed from tracker already");

                // Unload any previous state
                var unloadFlags = SceneSystem.UnloadParameters.DestroySectionProxyEntities |
                                  SceneSystem.UnloadParameters.DontRemoveRequestSceneLoaded;
                sceneSystem.UnloadScene(sceneEntity, unloadFlags);

                // Resolve new state
                var scene = EntityManager.GetComponentData<SceneReference>(change.UserKey);
                var request = EntityManager.GetComponentData<RequestSceneLoaded>(change.UserKey);
                if (change.ArtifactID != default)
                    ResolveSceneSectionUtility.ResolveSceneSections(EntityManager, change.UserKey, scene.SceneGUID,
                        request, change.ArtifactID);
                else
                    Debug.LogError(
                        $"Failed to import entity scene because the automatically generated SceneAndBuildConfigGUID asset was not present: '{AssetDatabaseCompatibility.GuidToPath(scene.SceneGUID)}' -> '{AssetDatabaseCompatibility.GuidToPath(change.Asset)}'");
            }

            if (!isDone)
                EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
        }

        protected override void OnCreate()
        {
            _AssetDependencyTracker =
                new AssetDependencyTracker<Entity>(EntityScenesPaths.SubSceneImporterType, "Import EntityScene");
            _Changed = new NativeList<AssetDependencyTracker<Entity>.Completed>(32, Allocator.Persistent);

            m_ValidSceneMask = EntityManager.GetEntityQueryMask(
                GetEntityQuery(new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<SceneReference>(), ComponentType.ReadOnly<RequestSceneLoaded>(), ComponentType.ReadOnly<AssetDependencyTrackerState>()
                    },
                    None = new[]
                    {
                        ComponentType.ReadOnly<DisableSceneResolveAndLoad>(),
                    }
                }));

            m_AddScenes = GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<SceneReference>(), ComponentType.ReadOnly<RequestSceneLoaded>()
                    },
                    None = new[]
                    {
                        ComponentType.ReadOnly<DisableSceneResolveAndLoad>(),
                        ComponentType.ReadOnly<AssetDependencyTrackerState>()
                    }
                });

            //@TODO: This syntax is horrible. We need a reactive query syntax that lets me simply invert the m_AddScenes EntityQueryDesc
            m_RemoveScenes = GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[] {ComponentType.ReadOnly<AssetDependencyTrackerState>()},
                    None = new[]
                        {ComponentType.ReadOnly<SceneReference>(), ComponentType.ReadOnly<RequestSceneLoaded>()}
                },
                new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<AssetDependencyTrackerState>(),
                        ComponentType.ReadOnly<DisableSceneResolveAndLoad>()
                    }
                },
                new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<AssetDependencyTrackerState>(), ComponentType.ReadOnly<Disabled>()
                    }
                }
            );
        }

        protected override void OnDestroy()
        {
            _AssetDependencyTracker.Dispose();
            _Changed.Dispose();
        }
    }

#else
#if UNITY_DOTSRUNTIME
    internal struct RequestSceneHeader : ISystemStateComponentData
    {
        // DOTS Runtime IO handle. When the AsyncReadManager provides a mechanism to read files without knowing the size
        // this type can be changed to a common type.
        internal int IOHandle;
    }

    internal struct SceneMetaDataLoaded : ISystemStateComponentData
    {
        public bool Success;
    }
#endif

    /// <summary>
    /// Scenes are made out of sections, but to find out how many sections there are and extract their data like bounding volume or file size.
    /// The meta data for the scene has to be loaded first.
    /// ResolveSceneReferenceSystem creates section entities for each scene by loading the scenesection's metadata from disk.
    /// </summary>
    [AlwaysUpdateSystem]
    [UpdateInGroup(typeof(SceneSystemGroup))]
    [UpdateAfter(typeof(SceneSystem))]
    class ResolveSceneReferenceSystem : SystemBase
    {
        protected override void OnUpdate()
        {
#if !UNITY_DOTSRUNTIME
            Enabled = !LiveLinkUtility.LiveLinkEnabled;
            if (!Enabled)
                return;

            Entities.WithStructuralChanges().WithNone<DisableSceneResolveAndLoad, ResolvedSectionEntity>().
                ForEach((Entity sceneEntity, ref SceneReference scene, ref RequestSceneLoaded requestSceneLoaded) =>
                {
                    ResolveSceneSectionUtility.ResolveSceneSections(EntityManager, sceneEntity, scene.SceneGUID, requestSceneLoaded, default);
                }).Run();
#else
            // Asynchronously load the SceneMetaData from the SceneHeader
            Entities.WithStructuralChanges().WithNone<DisableSceneResolveAndLoad, ResolvedSectionEntity, SceneMetaDataLoaded>().
                ForEach((Entity sceneEntity, ref SceneReference scene) =>
                {
                    ResolveSceneSectionUtility.RequestLoadAndPollSceneMetaData(EntityManager, sceneEntity, scene.SceneGUID);
                }).Run();

            // Once the SceneHeader is loaded, resolve the sections from the scene header
            Entities.WithStructuralChanges().WithNone<DisableSceneResolveAndLoad, ResolvedSectionEntity>().WithAll<SceneMetaDataLoaded>().
                ForEach((Entity sceneEntity, ref SceneReference scene, ref RequestSceneLoaded requestSceneLoaded) =>
                {
                    ResolveSceneSectionUtility.ResolveSceneSections(EntityManager, sceneEntity, scene.SceneGUID, requestSceneLoaded, default);
                }).Run();
#endif
        }
    }
#endif

}

