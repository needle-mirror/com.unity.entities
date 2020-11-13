using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Build;
using Unity.Entities.Conversion;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes.Editor
{
    //@TODO: Live link currently force loads all sections. It should only live link sections that are marked for loading.
    class LiveLinkDiffGenerator : IDisposable
    {
#if !UNITY_2020_2_OR_NEWER
        public int                  LiveLinkDirtyID = -1;
#endif

        World                       _GameObjectWorld;

        World                       _ConvertedWorld;
        EntityQuery                 _MissingRenderDataQuery;
        EntityQuery                 _MissingSceneQuery;
        EntityManagerDiffer         _LiveLinkDiffer;

#if UNITY_2020_2_OR_NEWER
        IncrementalConversionDebug _IncrementalConversionDebug;
#endif
        IncrementalConversionChangeTracker _IncrementalConversionChangeTracker;
        GameObjectConversionMappingSystem _MappingSystem;
        bool                        _RequestCleanConversion;
        bool                        _LiveLinkEnabled;

        Scene                       _Scene;
        GUID                        _buildConfigurationGUID;
        BuildConfiguration          _buildConfiguration;
        readonly Hash128            _SceneGUID;
        readonly BlobAssetStore     _BlobAssetStore = new BlobAssetStore();

#if !UNITY_2020_2_OR_NEWER
        readonly HashSet<GameObject> m_ChangedGameObjects = new HashSet<GameObject>();
#endif

#if UNITY_2020_2_OR_NEWER
        struct IncrementalConversionDebug
        {
            public World World;
            public BlobAssetCache BlobAssets;
            public EntityQuery MissingRenderDataQuery;
            public EntityQuery MissingSceneQuery;
            public BlobAssetStore BlobAssetStore;
            public bool NeedsUpdate;
            public GameObjectConversionUtility.ConversionFlags LastConversionFlags;

            public void Dispose()
            {
                if (World == null)
                    return;
                MissingRenderDataQuery.Dispose();
                MissingSceneQuery.Dispose();
                BlobAssets.Dispose();
                World.Dispose();
                BlobAssetStore.Dispose();
            }
        }
#endif

        internal IncrementalConversionChangeTracker ChangeTracker => _IncrementalConversionChangeTracker;

        internal bool HasAssetDependencies()
        {
            if (_GameObjectWorld == null)
                return false;
            return _MappingSystem.Dependencies.AssetDependencyTracker.HasDependencies();
        }

#if !UNITY_2020_2_OR_NEWER
        internal void MarkChanged(GameObject go)
        {
            m_ChangedGameObjects.Add(go);
        }
#endif

        public void RequestCleanConversion()
        {
            _RequestCleanConversion = true;
        }

        public bool DidRequestUpdate()
        {
#if UNITY_2020_2_OR_NEWER
            return _RequestCleanConversion || _IncrementalConversionChangeTracker.HasAnyChanges();
#else
            return _RequestCleanConversion || _IncrementalConversionChangeTracker.HasAnyChanges() || m_ChangedGameObjects.Count > 0;
#endif
        }

        LiveLinkDiffGenerator(Scene scene, Hash128 sceneGUID, GUID buildConfigGUID, BuildConfiguration buildConfig, bool liveLinkEnabled)
        {
            _SceneGUID = sceneGUID;
            _Scene = scene;
            _buildConfigurationGUID = buildConfigGUID;
            _buildConfiguration = buildConfig;

            _LiveLinkEnabled = liveLinkEnabled;
            var worldFlags = WorldFlags.Editor | WorldFlags.Conversion | WorldFlags.Staging;
            _ConvertedWorld = new World($"Converted Scene: '{_Scene.name}", worldFlags);
            _LiveLinkDiffer = new EntityManagerDiffer(_ConvertedWorld.EntityManager, Allocator.Persistent);
            _IncrementalConversionChangeTracker = new IncrementalConversionChangeTracker();
#if UNITY_2020_2_OR_NEWER
            _IncrementalConversionDebug.World = new World($"Incremental Conversion Debug: '{_Scene.name}'", worldFlags);
            _IncrementalConversionDebug.BlobAssets = new BlobAssetCache(Allocator.Persistent);
            _IncrementalConversionDebug.BlobAssetStore = new BlobAssetStore();
#endif
            _RequestCleanConversion = true;

            var missingRenderDataQueryDesc = new EntityQueryDesc
            {
                All = new ComponentType[] { typeof(SceneTag) },
                None = new ComponentType[] { typeof(EditorRenderData) },
                Options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabled
            };
            var missingSceneDataQueryDesc = new EntityQueryDesc
            {
                None = new ComponentType[] { typeof(SceneTag) },
                Options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabled
            };

            _MissingRenderDataQuery = _ConvertedWorld.EntityManager.CreateEntityQuery(missingRenderDataQueryDesc);
            _MissingSceneQuery = _ConvertedWorld.EntityManager.CreateEntityQuery(missingSceneDataQueryDesc);
#if UNITY_2020_2_OR_NEWER
            _IncrementalConversionDebug.MissingRenderDataQuery = _IncrementalConversionDebug.World.EntityManager.CreateEntityQuery(missingRenderDataQueryDesc);
            _IncrementalConversionDebug.MissingSceneQuery = _IncrementalConversionDebug.World.EntityManager.CreateEntityQuery(missingSceneDataQueryDesc);
#endif
        }

        public void Dispose()
        {
            _BlobAssetStore.Dispose();
            _IncrementalConversionChangeTracker.Dispose();
#if UNITY_2020_2_OR_NEWER
            _IncrementalConversionDebug.Dispose();
#endif

            try
            {
                _LiveLinkDiffer.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            try
            {
                if (_GameObjectWorld != null && _GameObjectWorld.IsCreated)
                    _GameObjectWorld.Dispose();
                _GameObjectWorld = null;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            try
            {
                if (_ConvertedWorld != null && _ConvertedWorld.IsCreated)
                    _ConvertedWorld.Dispose();
                _ConvertedWorld = null;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static readonly ProfilerMarker LiveLinkConvertMarker = new ProfilerMarker("LiveLink.Convert");
        static readonly ProfilerMarker IncrementalConversionMarker = new ProfilerMarker("IncrementalConversion");
        static readonly ProfilerMarker DebugConversionMarker = new ProfilerMarker("DebugConversion");
        static readonly ProfilerMarker CleanConversionMarker = new ProfilerMarker("CleanConversion");

        void Convert(GameObjectConversionUtility.ConversionFlags flags)
        {
            using (LiveLinkConvertMarker.Auto())
            {
                var mode = LiveConversionSettings.Mode;
#if UNITY_2020_2_OR_NEWER
                if (mode == LiveConversionSettings.ConversionMode.AlwaysCleanConvert)
                    _RequestCleanConversion = true;
                _IncrementalConversionDebug.LastConversionFlags = flags;
#endif

                // Try incremental conversion
                if (!_RequestCleanConversion)
                {
                    try
                    {
                        using (IncrementalConversionMarker.Auto())
                        {
#if UNITY_2020_2_OR_NEWER
                            _IncrementalConversionDebug.NeedsUpdate = true;
                            var batch = new IncrementalConversionBatch();
                            _IncrementalConversionChangeTracker.FillBatch(ref batch);
                            GameObjectConversionUtility.ConvertIncremental(_GameObjectWorld, flags, ref batch);
#else
                            GameObjectConversionUtility.ConvertIncremental(_GameObjectWorld, m_ChangedGameObjects, _IncrementalConversionChangeTracker.ChangedAssets, flags);
#endif
                            AddMissingData(_ConvertedWorld, _MissingSceneQuery, _MissingRenderDataQuery);
                        }
                    }
#if UNITY_2020_2_OR_NEWER
                    catch (Exception e)
                    {
                        _RequestCleanConversion = true;
                        if (LiveConversionSettings.TreatIncrementalConversionFailureAsError)
                            throw;
                        if (mode != LiveConversionSettings.ConversionMode.AlwaysCleanConvert)
                            Debug.Log("Incremental conversion failed. Performing full conversion instead\n" + e);
                    }
#else
                    catch (Exception)

                    {

                        _RequestCleanConversion = true;
                    }
#endif
                }

                // If anything failed, fall back to clean conversion
                if (_RequestCleanConversion)
                {
#if UNITY_2020_2_OR_NEWER
                    _IncrementalConversionDebug.NeedsUpdate = false;
#endif
                    using (CleanConversionMarker.Auto())
                    {
                        if (_GameObjectWorld != null && _GameObjectWorld.IsCreated)
                        {
                            _GameObjectWorld.Dispose();
                            _GameObjectWorld = null;
                        }

                        var settings = PrepareConversion(_ConvertedWorld, flags, _buildConfigurationGUID, _buildConfiguration);
                        _GameObjectWorld = GameObjectConversionUtility.InitializeIncrementalConversion(_Scene, settings);
                        _MappingSystem = _GameObjectWorld.GetExistingSystem<GameObjectConversionMappingSystem>();
                        AddMissingData(_ConvertedWorld, _MissingSceneQuery, _MissingRenderDataQuery);
                    }
                }

                _IncrementalConversionChangeTracker.Clear();
                _RequestCleanConversion = false;
            }
        }

#if UNITY_2020_2_OR_NEWER
        internal bool DidRequestDebugConversion() => _IncrementalConversionDebug.NeedsUpdate;

        internal void DebugIncrementalConversion()
        {
            if (!_IncrementalConversionDebug.NeedsUpdate)
                return;
            _IncrementalConversionDebug.NeedsUpdate = false;
            var flags = _IncrementalConversionDebug.LastConversionFlags;
            using (DebugConversionMarker.Auto())
            {
                // use this to compare the results of incremental conversion with the results of a clean conversion.
                var settings = PrepareConversion(_IncrementalConversionDebug.World, flags, _buildConfigurationGUID, _buildConfiguration);
                GameObjectConversionUtility.InitializeIncrementalConversion(_Scene, settings).Dispose();
                AddMissingData(_IncrementalConversionDebug.World, _IncrementalConversionDebug.MissingSceneQuery,
                    _IncrementalConversionDebug.MissingRenderDataQuery);
                const EntityManagerDifferOptions options =
                    EntityManagerDifferOptions.IncludeForwardChangeSet |
                    EntityManagerDifferOptions.ValidateUniqueEntityGuid |
                    EntityManagerDifferOptions.UseReferentialEquality;

                unsafe
                {
                    if (_IncrementalConversionDebug.BlobAssets.BlobAssetBatch != null)
                        _IncrementalConversionDebug.BlobAssets.Dispose();
                }

                _IncrementalConversionDebug.BlobAssets = new BlobAssetCache(Allocator.Persistent);
                EntityDiffer.PrecomputeBlobAssetCache(_ConvertedWorld.EntityManager,
                    EntityManagerDiffer.EntityGuidQueryDesc, _IncrementalConversionDebug.BlobAssets);

                var changes = EntityDiffer.GetChanges(
                    _IncrementalConversionDebug.World.EntityManager,
                    _ConvertedWorld.EntityManager,
                    options,
                    EntityManagerDiffer.EntityGuidQueryDesc,
                    _IncrementalConversionDebug.BlobAssets,
                    Allocator.TempJob
                );
                using (changes)
                {
                    if (!changes.AnyChanges)
                        return;

                    var fwdChanges = changes.ForwardChangeSet;
#if !UNITY_DISABLE_MANAGED_COMPONENTS
                    {
                        // Remove all companion link object changes.
                        // Companion objects will always be different between different conversions, so this is
                        // absolutely expected.
                        // It is unlikely that a diff will ever only consist of changes to hybrid components, and even
                        // in that case pointing out that the companion link changed is not exactly helpful for the user
                        // either.
                        var managedComponents = fwdChanges.SetManagedComponents;
                        int numCompanionLinkObjects = 0;
                        var types = fwdChanges.TypeHashes;
                        var companionLinkIndex = TypeManager.GetTypeIndex<CompanionLink>();
                        int last = managedComponents.Length - 1;
                        for (int i = last; i >= 0; i--)
                        {
                            // We need to go through the type index to correctly handle null Companion Links
                            int packedTypeIdx = managedComponents[i].Component.PackedTypeIndex;
                            var idx = TypeManager.GetTypeIndexFromStableTypeHash(types[packedTypeIdx].StableTypeHash);
                            if (idx == companionLinkIndex)
                            {
                                managedComponents[i] = managedComponents[last - numCompanionLinkObjects];
                                numCompanionLinkObjects += 1;
                            }
                        }

                        if (numCompanionLinkObjects > 0)
                        {
                            // throw away the companion link changes
                            Array.Resize(ref managedComponents, last + 1 - numCompanionLinkObjects);
                            fwdChanges = new EntityChangeSet(fwdChanges.CreatedEntityCount,
                                fwdChanges.DestroyedEntityCount, fwdChanges.Entities, fwdChanges.TypeHashes,
                                fwdChanges.Names, fwdChanges.AddComponents, fwdChanges.RemoveComponents,
                                fwdChanges.SetComponents, fwdChanges.ComponentData, fwdChanges.EntityReferenceChanges,
                                fwdChanges.BlobAssetReferenceChanges,
                                managedComponents, // <- this changes
                                fwdChanges.SetSharedComponents, fwdChanges.LinkedEntityGroupAdditions,
                                fwdChanges.LinkedEntityGroupRemovals, fwdChanges.CreatedBlobAssets,
                                fwdChanges.DestroyedBlobAssets, fwdChanges.BlobAssetData);
                            if (!fwdChanges.HasChanges)
                                return;
                        }
                    }
#endif
                    _RequestCleanConversion = true;
                    var sb = new StringBuilder();
                    fwdChanges.PrintSummary(sb);
                    var errorString =
                        "The result of incrementally converting changes and a clean conversion didn't match, are you missing some dependencies?\n" +
                        "This is what was added/removed/changed by the clean conversion relative to the incremental conversion:\n" +
                        sb;
                    if (LiveConversionSettings.TreatIncrementalConversionFailureAsError)
                        throw new Exception(errorString);
                    Debug.LogWarning(errorString);
                }
            }
        }
#endif

        GameObjectConversionSettings PrepareConversion(World dstWorld, GameObjectConversionUtility.ConversionFlags flags, GUID buildConfigurationGUID, BuildConfiguration buildConfig)
        {
            dstWorld.EntityManager.DestroyEntity(dstWorld.EntityManager.UniversalQuery);
            var conversionSettings = new GameObjectConversionSettings(dstWorld, flags)
            {
                BuildConfigurationGUID = buildConfigurationGUID,
                BuildConfiguration = buildConfig,
                SceneGUID = _SceneGUID,
                DebugConversionName = _Scene.name,
                BlobAssetStore = _BlobAssetStore
            };
            if (LiveConversionSettings.AdditionalConversionSystems.Count != 0)
                conversionSettings.ExtraSystems = LiveConversionSettings.AdditionalConversionSystems.ToArray();
            return conversionSettings;
        }

        static void AddMissingData(World world, EntityQuery missingSceneQuery, EntityQuery missingRenderDataQuery)
        {
            var em = world.EntityManager;
            // We don't know the scene tag of the destination world, so we create a null Scene Tag.
            // In the patching code this will be translated into the final scene entity.
            em.AddSharedComponentData(missingSceneQuery, new SceneTag { SceneEntity = Entity.Null });
            em.AddSharedComponentData(missingRenderDataQuery, new EditorRenderData { SceneCullingMask = UnityEditor.SceneManagement.SceneCullingMasks.GameViewObjects, PickableObject = null });
        }

        public static LiveLinkChangeSet UpdateLiveLink(Scene scene, Hash128 sceneGUID, ref LiveLinkDiffGenerator liveLinkData, int sceneDirtyID, LiveLinkMode mode, GUID configGUID, BuildConfiguration config)
        {
            //Debug.Log("ApplyLiveLink: " + scene.SceneName);

            int framesToRetainBlobAssets = RetainBlobAssetsSetting.GetFramesToRetainBlobAssets(config);

            var liveLinkEnabled = mode != LiveLinkMode.Disabled;
            if (liveLinkData != null && liveLinkData._LiveLinkEnabled != liveLinkEnabled)
            {
                liveLinkData.Dispose();
                liveLinkData = null;
            }

            var unloadAllPreviousEntities = liveLinkData == null;
            if (liveLinkData == null)
                liveLinkData = new LiveLinkDiffGenerator(scene, sceneGUID, configGUID, config, liveLinkEnabled);
            else if (liveLinkData._Scene != scene || !ReferenceEquals(liveLinkData._buildConfiguration, config) || liveLinkData._buildConfigurationGUID != configGUID)
            {
                liveLinkData._Scene = scene;
                liveLinkData._buildConfigurationGUID = configGUID;
                liveLinkData._buildConfiguration = config;
                liveLinkData._RequestCleanConversion = true;
            }

            if (!liveLinkEnabled)
            {
                return new LiveLinkChangeSet
                {
                    UnloadAllPreviousEntities = unloadAllPreviousEntities,
                    SceneName = scene.name,
                    SceneGUID = sceneGUID,
                    FramesToRetainBlobAssets = framesToRetainBlobAssets
                };
            }

            var flags = GameObjectConversionUtility.ConversionFlags.AddEntityGUID | GameObjectConversionUtility.ConversionFlags.AssignName | GameObjectConversionUtility.ConversionFlags.GameViewLiveLink;
            if (mode == LiveLinkMode.LiveConvertSceneView)
                flags |= GameObjectConversionUtility.ConversionFlags.SceneViewLiveLink;
            if (mode == LiveLinkMode.LiveConvertStandalonePlayer)
                flags |= GameObjectConversionUtility.ConversionFlags.IsBuildingForPlayer;

            liveLinkData.Convert(flags);
            const EntityManagerDifferOptions options =
                EntityManagerDifferOptions.IncludeForwardChangeSet |
                EntityManagerDifferOptions.FastForwardShadowWorld |
                EntityManagerDifferOptions.ValidateUniqueEntityGuid |
                EntityManagerDifferOptions.ClearMissingReferences;

            var changes = new LiveLinkChangeSet
            {
                Changes = liveLinkData._LiveLinkDiffer.GetChanges(options, Allocator.TempJob).ForwardChangeSet,
                UnloadAllPreviousEntities = unloadAllPreviousEntities,
                SceneName = scene.name,
                SceneGUID = sceneGUID,
                FramesToRetainBlobAssets = framesToRetainBlobAssets
            };

#if !UNITY_2020_2_OR_NEWER
            liveLinkData.LiveLinkDirtyID = sceneDirtyID;
#endif
            // convertedEntityManager.Debug.CheckInternalConsistency();

            return changes;
        }
    }
}
