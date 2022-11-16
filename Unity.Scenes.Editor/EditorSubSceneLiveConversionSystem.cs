using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Conversion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes.Editor
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.ThinClientSimulation)]
    [UpdateInGroup(typeof(LiveConversionEditorSystemGroup))]
    partial class EditorSubSceneLiveConversionSystem : SystemBase
    {
        LiveConversionConnection         _EditorLiveConversion;
        LiveConversionPatcher            _Patcher;
        LiveConversionSceneChangeTracker _SceneChangeTracker;

        // Temp data cached to reduce gc allocations
        List<LiveConversionChangeSet>    _ChangeSets;
        NativeList<Hash128>        _UnloadScenes;
        NativeList<Hash128>        _LoadScenes;

        ulong m_GizmoSceneCullingMask = 1UL << 59;

        System.Diagnostics.Stopwatch m_Watch;
        internal double MillisecondsTakenByUpdate { get; set; }

        internal World GetConvertedWorldForEntity(Entity entity)
        {
            if (!EntityManager.HasComponent<SceneSection>(entity))
                return null;
            var section = EntityManager.GetSharedComponent<SceneSection>(entity);
            return _EditorLiveConversion.GetConvertedWorldForScene(section.SceneGUID);
        }

        internal World GetConvertedWorldForScene(Hash128 sceneGUID)
        {
            return _EditorLiveConversion.GetConvertedWorldForScene(sceneGUID);
        }

        protected override void OnUpdate()
        {
            m_Watch.Restart();
            try
            {
                // We can't initialize live link in OnCreate because other systems might configure BuildConfigurationGUID from OnCreate
                if (_EditorLiveConversion == null)
                    _EditorLiveConversion = new LiveConversionConnection(EntityManager.GetComponentData<SceneSystemData>(World.GetExistingSystem<SceneSystem>()).BuildConfigurationGUID);

                try
                {
                    if (_SceneChangeTracker.GetSceneMessage(out var msg))
                    {
                        using (msg)
                        {
                            _EditorLiveConversion.ApplyLiveConversionSceneMsg(msg);
                        }
                    }

                    _EditorLiveConversion.Update(_ChangeSets, _LoadScenes, _UnloadScenes, LiveConversionEditorSettings.LiveConversionMode);

                    // Unload scenes that are no longer being edited / need to be reloaded etc
                    foreach (var change in _UnloadScenes)
                    {
                        _Patcher.UnloadScene(change);
                    }

                    // Apply changes to scenes that are being edited
                    foreach (var change in _ChangeSets)
                    {
                        try
                        {
                            _Patcher.ApplyPatch(change);
                        }
                        catch (System.Exception exc)
                        {
                            Debug.LogException(exc);
                        }
                    }
                }
                finally
                {
                    _LoadScenes.Clear();

                    foreach (var change in _ChangeSets)
                    {
                        change.Dispose();
                    }
                    _ChangeSets.Clear();

                    _UnloadScenes.Clear();
                }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
                CompanionGameObjectUtility.UpdateLiveConversionCulling(LiveConversionEditorSettings.LiveConversionMode);
#endif

                if (_EditorLiveConversion.HasLoadedScenes())
                {
                    // Configure scene culling masks so that game objects & entities are rendered exlusively to each other
                    for (int i = 0; i != EditorSceneManager.sceneCount; i++)
                    {
                        var scene = EditorSceneManager.GetSceneAt(i);

                        //this is to avoid trying to get the guid of a scene loaded from a content archive during play mode
                        if (!scene.path.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase) &&
                            !scene.path.StartsWith("Packages/", System.StringComparison.OrdinalIgnoreCase))
                            continue;

                        var sceneGUID = AssetDatabaseCompatibility.PathToGUID(scene.path);
                        if (_EditorLiveConversion.HasScene(sceneGUID))
                        {
                            if (LiveConversionEditorSettings.LiveConversionMode == LiveConversionMode.SceneViewShowsAuthoring)
                                EditorSceneManager.SetSceneCullingMask(scene, SceneCullingMasks.MainStageSceneViewObjects);
                            else if (LiveConversionEditorSettings.LiveConversionMode == LiveConversionMode.SceneViewShowsRuntime)
                                EditorSceneManager.SetSceneCullingMask(scene, m_GizmoSceneCullingMask);
                            else
                                EditorSceneManager.SetSceneCullingMask(scene, EditorSceneManager.DefaultSceneCullingMask);
                        }
                    }
                }
            }
            finally
            {
                m_Watch.Stop();
                MillisecondsTakenByUpdate += m_Watch.Elapsed.TotalMilliseconds;
            }
        }

        protected override void OnCreate()
        {
            Camera.onPreCull += OnPreCull;
            RenderPipelineManager.beginContextRendering += OnPreCull;
            SceneView.duringSceneGui += SceneViewOnBeforeSceneGui;
            m_Watch = new Stopwatch();

            _SceneChangeTracker = new LiveConversionSceneChangeTracker(EntityManager);

            _Patcher = new LiveConversionPatcher(World);
            _UnloadScenes = new NativeList<Hash128>(Allocator.Persistent);
            _LoadScenes = new NativeList<Hash128>(Allocator.Persistent);
            _ChangeSets = new List<LiveConversionChangeSet>();
        }

        protected override void OnDestroy()
        {
            Camera.onPreCull -= OnPreCull;
            RenderPipelineManager.beginContextRendering -= OnPreCull;
            SceneView.duringSceneGui -= SceneViewOnBeforeSceneGui;

            if (_EditorLiveConversion != null)
                _EditorLiveConversion.Dispose();
            _SceneChangeTracker.Dispose();
            _Patcher.Dispose();
            _UnloadScenes.Dispose();
            _LoadScenes.Dispose();
        }

        //@TODO:
        // * This is a gross hack to show the Transform gizmo even though the game objects used for editing are hidden and thus the tool gizmo is not shown
        // * Also we are not rendering the selection (Selection must be drawn around live linked object, not the editing object)
        void SceneViewOnBeforeSceneGui(SceneView sceneView)
        {
            if (LiveConversionEditorSettings.LiveConversionMode == LiveConversionMode.SceneViewShowsRuntime)
            {
                Camera camera = sceneView.camera;
                bool sceneViewIsRenderingCustomScene = camera.scene.IsValid();
                if (!sceneViewIsRenderingCustomScene)
                {
                    // Add our gizmo hack bit before gizmo rendering so the SubScene GameObjects are considered visible
                    ulong newmask = camera.overrideSceneCullingMask | m_GizmoSceneCullingMask;
                    camera.overrideSceneCullingMask = newmask;
                }
            }
        }

        void OnPreCull(ScriptableRenderContext src, Camera[] cameras)
        {
            foreach (Camera camera in cameras)
            {
                OnPreCull(camera);
            }
        }

        void OnPreCull(ScriptableRenderContext src, List<Camera> cameras)
        {
            foreach (Camera camera in cameras)
            {
                OnPreCull(camera);
            }
        }

        void OnPreCull(Camera camera)
        {
            if (camera.cameraType == CameraType.SceneView)
            {
                // Ensure to remove our gizmo hack bit before rendering
                ulong newmask = camera.overrideSceneCullingMask & ~m_GizmoSceneCullingMask;
                camera.overrideSceneCullingMask = newmask;
            }
        }
    }
}
