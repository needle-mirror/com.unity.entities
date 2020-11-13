using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Build;
using Unity.Build.Common;
using Unity.Entities.Conversion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Assert = Unity.Assertions.Assert;
using Hash128 = Unity.Entities.Hash128;
using Object = UnityEngine.Object;

namespace Unity.Scenes.Editor
{
    //@TODO: LiveLinkConnection is starting to be a relatively complex statemachine. Lets have unit tests for it in isolation...

    // A connection to a Player or Editor with a specific build configuration.
    // Each destination world in each player/editor, has it's own LiveLinkConnection so we can generate different data for different worlds.
    // For example server world vs client world.
    class LiveLinkConnection
    {
        static int                                 GlobalDirtyID = 0;

#if !UNITY_2020_2_OR_NEWER
        static readonly MethodInfo                 _GetDirtyIDMethod = typeof(Scene).GetProperty("dirtyID", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetMethod;
#endif

        HashSet<Hash128>                           _LoadedScenes = new HashSet<Hash128>();
        HashSet<Hash128>                           _SentLoadScenes = new HashSet<Hash128>();
        NativeList<Hash128>                        _RemovedScenes;
        Dictionary<Hash128, LiveLinkDiffGenerator> _SceneGUIDToLiveLink = new Dictionary<Hash128, LiveLinkDiffGenerator>();
        int                                        _PreviousGlobalDirtyID;
        Dictionary<Hash128, Scene>                 _GUIDToEditScene = new Dictionary<Hash128, Scene>();

        internal readonly Hash128                  _BuildConfigurationGUID;
        BuildConfiguration                         _BuildConfiguration;
        UnityEngine.Hash128                        _BuildConfigurationArtifactHash;

        internal bool                              _IsEnabled = true;

        static readonly List<LiveLinkConnection>   k_AllConnections = new List<LiveLinkConnection>();

        public LiveLinkConnection(Hash128 buildConfigurationGuid)
        {
            _BuildConfigurationGUID = buildConfigurationGuid;
            if (buildConfigurationGuid != default)
            {
                _BuildConfiguration = BuildConfiguration.LoadAsset(buildConfigurationGuid);
                if (_BuildConfiguration == null)
                    Debug.LogError($"Unable to load build configuration asset from guid {buildConfigurationGuid}.");
            }

#if UNITY_2020_2_OR_NEWER
            ObjectChangeEvents.changesPublished += OnEditorChangeEvents;
            EditorSceneManager.sceneOpened += SceneOpened;
            EditorSceneManager.sceneClosed += SceneClosed;
#else
            Undo.postprocessModifications += PostprocessModifications;
            Undo.undoRedoPerformed += OnUndoPerformed;
#endif

            _RemovedScenes = new NativeList<Hash128>(Allocator.Persistent);
            k_AllConnections.Add(this);
        }

        public void Dispose()
        {
            k_AllConnections.Remove(this);
#if UNITY_2020_2_OR_NEWER
            EditorSceneManager.sceneOpened -= SceneOpened;
            EditorSceneManager.sceneClosed -= SceneClosed;
            ObjectChangeEvents.changesPublished -= OnEditorChangeEvents;
#else
            Undo.undoRedoPerformed -= OnUndoPerformed;
            Undo.postprocessModifications -= PostprocessModifications;
#endif
            foreach (var livelink in _SceneGUIDToLiveLink.Values)
                livelink.Dispose();
            _SceneGUIDToLiveLink.Clear();
            _SceneGUIDToLiveLink = null;
            _RemovedScenes.Dispose();
        }

        public NativeArray<Hash128> GetInitialScenes(int playerId, Allocator allocator)
        {
            var sceneList = _BuildConfiguration.GetComponent<SceneList>();
            var nonEmbeddedStartupScenes = new List<string>();
            foreach (var path in sceneList.GetScenePathsToLoad())
            {
                if (SceneImporterData.CanLiveLinkScene(path))
                    nonEmbeddedStartupScenes.Add(path);
            }

            if (nonEmbeddedStartupScenes.Count > 0)
            {
                var sceneIds = new NativeArray<Hash128>(nonEmbeddedStartupScenes.Count, allocator);
                for (int i = 0; i < nonEmbeddedStartupScenes.Count; i++)
                    sceneIds[i] = AssetDatabaseCompatibility.PathToGUID(nonEmbeddedStartupScenes[i]);
                return sceneIds;
            }
            return new NativeArray<Hash128>(0, allocator);
        }

#if UNITY_2020_2_OR_NEWER

        void SceneOpened(Scene scene, OpenSceneMode mode)
        {
            {
                // @TODO: This is a temporary workaround until ObjectChangeEventStream handles this
                // When a scene is re-loaded, we need to re-convert it. This happens for example when the changes in a scene
                // are discarded.
                GetLiveLink(scene)?.RequestCleanConversion();
            }
            EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
        }

        void SceneClosed(Scene scene)
        {
            EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
        }

        void OnEditorChangeEvents(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                var type = stream.GetEventType(i);
                switch (type)
                {
                    case ObjectChangeKind.None:
                        break;
                    case ObjectChangeKind.ChangeScene:
                    {
                        stream.GetChangeSceneEvent(i, out var evt);
                        if (evt.scene.IsValid())
                            GetLiveLink(evt.scene)?.RequestCleanConversion();
                        break;
                    }
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                    {
                        stream.GetCreateGameObjectHierarchyEvent(i, out var evt);
                        GetLiveLink(evt.scene)?.ChangeTracker.MarkReconvertHierarchy(evt.instanceId);
                        break;
                    }
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                    {
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out var evt);
                        GetLiveLink(evt.scene)?.ChangeTracker.MarkReconvertHierarchy(evt.instanceId);
                        break;
                    }
                    case ObjectChangeKind.ChangeGameObjectStructure:
                    {
                        stream.GetChangeGameObjectStructureEvent(i, out var evt);
                        GetLiveLink(evt.scene)?.ChangeTracker.MarkChanged(evt.instanceId);
                        break;
                    }
                    case ObjectChangeKind.ChangeGameObjectParent:
                    {
                        stream.GetChangeGameObjectParentEvent(i, out var evt);
                        if (evt.newScene != evt.previousScene)
                        {
                            GetLiveLink(evt.newScene)?.ChangeTracker.MarkReconvertHierarchy(evt.instanceId);
                            GetLiveLink(evt.previousScene)?.ChangeTracker.MarkRemoved(evt.instanceId);
                        }
                        else
                            GetLiveLink(evt.newScene)?.ChangeTracker.MarkParentChanged(evt.instanceId, evt.newParentInstanceId);
                        break;
                    }
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                    {
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var evt);
                        var target = EditorUtility.InstanceIDToObject(evt.instanceId);
                        if (target is Component c)
                            GetLiveLink(evt.scene)?.ChangeTracker.MarkComponentChanged(c);
                        else
                            GetLiveLink(evt.scene)?.ChangeTracker.MarkChanged(evt.instanceId);
                        break;
                    }
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                    {
                        stream.GetDestroyGameObjectHierarchyEvent(i, out var evt);
                        GetLiveLink(evt.scene)?.ChangeTracker.MarkRemoved(evt.instanceId);
                        break;
                    }
                    case ObjectChangeKind.CreateAssetObject:
                    {
                        stream.GetCreateAssetObjectEvent(i, out var evt);
                        MarkAssetChanged(evt.instanceId, evt.scene);
                        break;
                    }
                    case ObjectChangeKind.DestroyAssetObject:
                    {
                        stream.GetDestroyAssetObjectEvent(i, out var evt);
                        MarkAssetChanged(evt.instanceId, evt.scene);
                        break;
                    }
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                    {
                        stream.GetChangeAssetObjectPropertiesEvent(i, out var evt);
                        MarkAssetChanged(evt.instanceId, evt.scene);
                        break;
                    }
                    case ObjectChangeKind.UpdatePrefabInstances:
                    {
                        stream.GetUpdatePrefabInstancesEvent(i, out var evt);
                        var diffGenerator = GetLiveLink(evt.scene);
                        if (diffGenerator != null)
                        {
                            for (int k = 0; k < evt.instanceIds.Length; k++)
                                diffGenerator.ChangeTracker.MarkReconvertHierarchy(evt.instanceIds[k]);
                        }
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
        }

        void MarkAssetChanged(int assetInstanceId, Scene scene)
        {
            if (scene.IsValid())
                GetLiveLink(scene)?.ChangeTracker.MarkAssetChanged(assetInstanceId);
            else
            {
                foreach (var diffGenerator in _SceneGUIDToLiveLink.Values)
                    diffGenerator.ChangeTracker.MarkAssetChanged(assetInstanceId);
            }
        }
#else
        void OnUndoPerformed()
        {
            GlobalDirtyLiveLink();
        }

        UndoPropertyModification[] PostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (LiveConversionSettings.IsFullyIncremental)
                return modifications;

            foreach (var mod in modifications)
            {
                var target = GetGameObjectFromAny(mod.currentValue.target);
                if (target)
                {
                    var liveLink = GetLiveLink(target.scene);
                    if (liveLink != null)
                    {
                        liveLink.MarkChanged(target);
                        EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
                    }
                }
            }

            if (HasAssetDependencies())
            {
                foreach (var mod in modifications)
                {
                    foreach (var kvp in _SceneGUIDToLiveLink)
                        kvp.Value.ChangeTracker.MarkAssetChanged(mod.currentValue.target.GetInstanceID());
                }
            }
            return modifications;
        }

        static GameObject GetGameObjectFromAny(Object target)
        {
            Component component = target as Component;
            if (component != null)
                return component.gameObject;
            return target as GameObject;
        }

        int GetSceneDirtyID(Scene scene)
        {
            if (scene.IsValid())
            {
                return (int)_GetDirtyIDMethod.Invoke(scene, null);
            }
            else
                return -1;
        }
#endif

        bool HasAssetDependencies()
        {
            foreach (var kvp in _SceneGUIDToLiveLink)
            {
                if (kvp.Value.HasAssetDependencies())
                    return true;
            }

            return false;
        }

        class GameObjectPrefabLiveLinkSceneTracker : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets,
                string[] movedFromAssetPaths)
            {
#if !UNITY_2020_2_OR_NEWER
                foreach (var asset in importedAssets)
                {
                    if (asset.EndsWith(".prefab", true, System.Globalization.CultureInfo.InvariantCulture))
                    {
                        GlobalDirtyLiveLink();
                        return;
                    }
                }
#endif

                var connections = k_AllConnections;
                if (connections.Count == 0)
                    return;
                {
                    bool hasDependencies = false;
                    foreach (var c in connections)
                        hasDependencies |= c.HasAssetDependencies();
                    if (!hasDependencies)
                        return;
                }

                foreach (var assetPath in importedAssets)
                {
                    var instanceId = AssetDatabase.LoadAssetAtPath<Object>(assetPath).GetInstanceID();
                    foreach (var connection in connections)
                    {
                        foreach (var diff in connection._SceneGUIDToLiveLink)
                            diff.Value.ChangeTracker.MarkAssetChanged(instanceId);
                    }
                }
            }
        }

        public static void GlobalDirtyLiveLink()
        {
            GlobalDirtyID++;
            EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
        }

        static bool IsHotControlActive()
        {
            return GUIUtility.hotControl != 0;
        }

        LiveLinkDiffGenerator GetLiveLink(Hash128 sceneGUID)
        {
            _SceneGUIDToLiveLink.TryGetValue(sceneGUID, out var liveLink);
            return liveLink;
        }

        LiveLinkDiffGenerator GetLiveLink(Scene scene)
        {
            //@TODO: Cache _SceneToLiveLink ???
            var guid = new GUID(AssetDatabase.AssetPathToGUID(scene.path));
            return GetLiveLink(guid);
        }

        public void ApplyLiveLinkSceneMsg(LiveLinkSceneMsg msg)
        {
            SetLoadedScenes(msg.LoadedScenes);
            QueueRemovedScenes(msg.RemovedScenes);
        }

        void SetLoadedScenes(NativeArray<Hash128> loadedScenes)
        {
            _LoadedScenes.Clear();
            foreach (var scene in loadedScenes)
            {
                if (scene != default)
                    _LoadedScenes.Add(scene);
            }
        }

        void QueueRemovedScenes(NativeArray<Hash128> removedScenes)
        {
            _RemovedScenes.AddRange(removedScenes);
        }

        public bool HasScene(Hash128 sceneGuid)
        {
            return _LoadedScenes.Contains(sceneGuid);
        }

        public bool HasLoadedScenes()
        {
            return _LoadedScenes.Count > 0;
        }

        void RequestCleanConversion()
        {
            foreach (var liveLink in _SceneGUIDToLiveLink.Values)
                liveLink.RequestCleanConversion();
        }

        public void Update(List<LiveLinkChangeSet> changeSets, NativeList<Hash128> loadScenes, NativeList<Hash128> unloadScenes, LiveLinkMode mode)
        {
            if (_LoadedScenes.Count == 0 && _SceneGUIDToLiveLink.Count == 0 && _RemovedScenes.Length == 0)
                return;

            // If build configuration changed, we need to trigger a full conversion
            if (_BuildConfigurationGUID != default)
            {
                var buildConfigurationDependencyHash = AssetDatabaseCompatibility.GetAssetDependencyHash(_BuildConfigurationGUID);
                if (_BuildConfigurationArtifactHash != buildConfigurationDependencyHash)
                {
                    _BuildConfigurationArtifactHash = buildConfigurationDependencyHash;
                    RequestCleanConversion();
                }
            }

            if (_PreviousGlobalDirtyID != GlobalDirtyID)
            {
                RequestCleanConversion();
                _PreviousGlobalDirtyID = GlobalDirtyID;
            }

            // By default all scenes need to have m_GameObjectSceneCullingMask, otherwise they won't show up in game view
            _GUIDToEditScene.Clear();
            for (int i = 0; i != EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                var sceneGUID = AssetDatabaseCompatibility.PathToGUID(scene.path);

                if (_LoadedScenes.Contains(sceneGUID))
                {
                    if (scene.isLoaded && sceneGUID != default(GUID))
                        _GUIDToEditScene.Add(sceneGUID, scene);
                }
            }

            foreach (var scene in _SceneGUIDToLiveLink)
            {
                if (!_GUIDToEditScene.ContainsKey(scene.Key))
                    unloadScenes.Add(scene.Key);
            }

            // Process scenes that are no longer loaded
            foreach (var scene in unloadScenes)
            {
                var liveLink = _SceneGUIDToLiveLink[scene];
                liveLink.Dispose();
                _SceneGUIDToLiveLink.Remove(scene);
                _SentLoadScenes.Remove(scene);
            }
            foreach (var scene in _RemovedScenes)
            {
                if (_SceneGUIDToLiveLink.TryGetValue(scene, out var liveLink))
                {
                    liveLink.Dispose();
                    _SceneGUIDToLiveLink.Remove(scene);
                }

                unloadScenes.Add(scene);
                _SentLoadScenes.Remove(scene);
            }
            _RemovedScenes.Clear();

            _SentLoadScenes.RemoveWhere(scene => !_LoadedScenes.Contains(scene));

            // Process all scenes that the player needs
            var conversionMode = LiveConversionSettings.Mode;
            foreach (var sceneGuid in _LoadedScenes)
            {
                var isLoaded = _GUIDToEditScene.TryGetValue(sceneGuid, out var scene);

                // We are editing with live link. Ensure it is active & up to date
                if (isLoaded)
                {
                    var liveLink = GetLiveLink(sceneGuid);

                    if (liveLink == null || liveLink.DidRequestUpdate())
                        AddLiveLinkChangeSet(ref liveLink, sceneGuid, changeSets, mode);
#if !UNITY_2020_2_OR_NEWER
                    else if (liveLink.LiveLinkDirtyID != GetSceneDirtyID(scene))
                        AddLiveLinkChangeSet(ref liveLink, sceneGuid, changeSets, mode);
#endif

#if UNITY_2020_2_OR_NEWER
                    if (conversionMode == LiveConversionSettings.ConversionMode.IncrementalConversionWithDebug)
                    {
                        if (liveLink != null && liveLink.DidRequestDebugConversion())
                        {
                            if (IsHotControlActive())
                                EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
                            else
                                liveLink.DebugIncrementalConversion();
                        }
                    }
#endif
                }
                else
                {
                    if (_SentLoadScenes.Add(sceneGuid))
                        loadScenes.Add(sceneGuid);
                }
            }
        }

        void AddLiveLinkChangeSet(ref LiveLinkDiffGenerator liveLink, Hash128 sceneGUID, List<LiveLinkChangeSet> changeSets, LiveLinkMode mode)
        {
            var editScene = _GUIDToEditScene[sceneGUID];

            int sceneDirtyID = 0;
#if !UNITY_2020_2_OR_NEWER
            // The current behaviour is that we do incremental conversion until we release the hot control
            // This is to avoid any unexpected stalls
            if (IsHotControlActive())
            {
                if (liveLink == null)
                {
                    EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
                    return;
                }
                sceneDirtyID = liveLink.LiveLinkDirtyID;
            }
            else
            {
                sceneDirtyID = GetSceneDirtyID(editScene);
                if (liveLink != null && liveLink.LiveLinkDirtyID != sceneDirtyID)
                    liveLink.RequestCleanConversion();
            }
#endif

            //@TODO: need one place that LiveLinkDiffGenerators are managed. UpdateLiveLink does a Dispose()
            // but this must be paired with membership in _SceneGUIDToLiveLink. not good to have multiple places
            // doing ownership management.
            //
            // also: when implementing an improvement to this, be sure to deal with exceptions, which can occur
            // during conversion.

            if (liveLink != null)
                _SceneGUIDToLiveLink.Remove(sceneGUID);

            try
            {
                changeSets.Add(LiveLinkDiffGenerator.UpdateLiveLink(editScene, sceneGUID, ref liveLink, sceneDirtyID, mode, _BuildConfigurationGUID, _BuildConfiguration));
            }
            finally
            {
                if (liveLink != null)
                    _SceneGUIDToLiveLink.Add(sceneGUID, liveLink);
            }
        }
    }
}
