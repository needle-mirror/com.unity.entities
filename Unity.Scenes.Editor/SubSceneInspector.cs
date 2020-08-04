using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes.Editor
{
    [CustomEditor(typeof(SubScene))]
    [CanEditMultipleObjects]
    class SubSceneInspector : UnityEditor.Editor
    {
        Dictionary<Hash128, bool> m_ConversionLogLoaded = new Dictionary<Hash128, bool>();
        string m_ConversionLog = "";

        SceneAsset[] m_PreviousSceneAssets;
        private SubScene[] _selectedSubscenes;

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            _selectedSubscenes = new SubScene[targets.Length];
            var targetsArray = targets;
            targetsArray.CopyTo(_selectedSubscenes, 0);
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        void OnUndoRedoPerformed()
        {
            // The referenced Scene Asset can have changed when undo/redo happens so we ensure to
            // reload the Hierarchy which depends on the current SubScene state.
            SceneHierarchyHooks.ReloadAllSceneHierarchies();
        }

        void CachePreviousSceneAssetReferences()
        {
            var numTargets = targets.Length;
            if (m_PreviousSceneAssets == null || m_PreviousSceneAssets.Length != numTargets)
            {
                m_PreviousSceneAssets = new SceneAsset[numTargets];
            }
            for (int i = 0; i < numTargets; ++i)
            {
                var subScene = (SubScene)targets[i];
                m_PreviousSceneAssets[i] = subScene.SceneAsset;
            }
        }

        void HandleChangedSceneAssetReferences()
        {
            bool needsHierarchyReload = false;
            var numTargets = targets.Length;
            for (int i = 0; i < numTargets; ++i)
            {
                var subScene = (SubScene)targets[i];
                var prevSceneAsset = m_PreviousSceneAssets[i];
                if (prevSceneAsset != subScene.SceneAsset)
                {
                    if (!needsHierarchyReload)
                    {
                        // First time we see there's a change in Scene Asset,
                        // check if new scene is already loaded but not as a Sub Scene.
                        var scene = subScene.EditingScene;
                        if (scene.IsValid() && !scene.isSubScene)
                        {
                            if (EditorUtility.DisplayDialog("Convert to Sub Scene?", "The Scene is already loaded as a root Scene. Do you want to convert it to a Sub Scene?", "Convert", "Cancel"))
                            {
                                // Make loaded scene a Sub Scene. Only needs to be done once,
                                // since even with multi-editing, user can only have assigned one Scene.
                                scene.isSubScene = true;
                            }
                            else
                            {
                                // Cancel assigning new Scene Asset (after the fact).
                                Undo.PerformUndo();
                                break;
                            }
                        }
                    }

                    needsHierarchyReload = true;
                    if (prevSceneAsset != null)
                    {
                        Scene prevScene = SceneManager.GetSceneByPath(AssetDatabase.GetAssetPath(prevSceneAsset));
                        if (prevScene.isLoaded && prevScene.isSubScene)
                        {
                            if (prevScene.isDirty)
                                EditorSceneManager.SaveModifiedScenesIfUserWantsTo(new[] { prevScene });

                            // We need to close the Scene if it is loaded to prevent having scenes loaded
                            // that are not visualized in the Hierarhcy.
                            EditorSceneManager.CloseScene(prevScene, true);
                        }
                    }
                }
            }
            if (needsHierarchyReload)
                SceneHierarchyHooks.ReloadAllSceneHierarchies();
        }

        static readonly GUIContent s_TmpContent = new GUIContent();

        static Rect DrawButtonGridLabelAndGetFirstButtonRect(GUIContent label, int numButtons, out float spacing)
        {
            Rect controlRect;
            if (EditorGUIUtility.wideMode)
            {
                controlRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                controlRect = EditorGUI.PrefixLabel(controlRect, label);
            }
            else
            {
                EditorGUILayout.PrefixLabel(label);
                ++EditorGUI.indentLevel;
                controlRect = EditorGUI.IndentedRect(
                    EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight)
                );
                --EditorGUI.indentLevel;
            }
            spacing = GUI.skin.button.margin.horizontal / 2f;
            controlRect.width = (controlRect.width - spacing * (numButtons - 1)) / numButtons;
            return controlRect;
        }

        private static void DrawOpenSubScenes(SubScene[] subScenes)
        {
            int numOpen = 0;
            for (int i = 0; i < subScenes.Length; i++)
            {
                if (subScenes[i].IsLoaded)
                {
                    numOpen++;
                }
            }

            if (numOpen > 0)
            {
                GUILayout.Space(EditorGUIUtility.singleLineHeight);
                GUILayout.BeginHorizontal();
                GUILayout.Label(Content.OpenSubScenesLabel, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{numOpen}");
                GUILayout.EndHorizontal();

                for (int i = 0; i < subScenes.Length; i++)
                {
                    var scene = subScenes[i];
                    if (!scene.IsLoaded)
                        continue;

                    s_TmpContent.text = scene.SceneName;
                    s_TmpContent.tooltip = scene.EditableScenePath;
                    var buttonRect = DrawButtonGridLabelAndGetFirstButtonRect(s_TmpContent, 3, out var spacing);

                    // add empty space space so buttons are right-aligned
                    buttonRect.x += buttonRect.width + spacing;
                    using (new EditorGUI.DisabledScope(!scene.EditingScene.isDirty))
                    {
                        if (GUI.Button(buttonRect, Content.SaveLabel))
                        {
                            SubSceneInspectorUtility.SaveScene(scene);
                        }
                    }
                    buttonRect.x += buttonRect.width + spacing;
                    if (GUI.Button(buttonRect, Content.CloseLabel))
                    {
                        SubSceneInspectorUtility.CloseAndAskSaveIfUserWantsTo(scene);
                    }
                }
            }
        }

        private static bool DrawClosedSubScenes(SubSceneInspectorUtility.LoadableScene[] loadableScenes, SubScene[] subscenes)
        {
            if (World.DefaultGameObjectInjectionWorld != null && loadableScenes.Length != 0)
            {
                GUILayout.Space(EditorGUIUtility.singleLineHeight);
                var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

                {
                    int numScenesLoaded = 0;
                    int numScenesImported = 0;
                    foreach (var scene in loadableScenes)
                    {
                        if (entityManager.HasComponent<RequestSceneLoaded>(scene.Scene))
                            numScenesLoaded++;
                        if (IsSubsceneImported(scene.SubScene))
                            numScenesImported++;
                    }

                    if (EditorGUIUtility.wideMode)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(Content.ClosedSubScenesLabel, EditorStyles.boldLabel);
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(string.Format(Content.ClosedStatusString, numScenesLoaded, loadableScenes.Length, numScenesImported));
                        GUILayout.EndHorizontal();
                    }
                    else
                    {
                        GUILayout.Label(Content.ClosedSubScenesLabel, EditorStyles.boldLabel);
                        GUILayout.BeginHorizontal();
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(string.Format(Content.ClosedStatusString, numScenesLoaded, loadableScenes.Length, numScenesImported));
                        GUILayout.EndHorizontal();
                    }
                }

                if (loadableScenes.Length > 1)
                {
                    GUILayout.BeginHorizontal();
                    bool reimportRequested = GUILayout.Button(Content.ReimportAllLabel);

                    if (GUILayout.Button(Content.LoadAllLabel))
                    {
                        foreach (var scene in loadableScenes)
                        {
                            if (!entityManager.HasComponent<RequestSceneLoaded>(scene.Scene))
                                entityManager.AddComponentData(scene.Scene, new RequestSceneLoaded());
                        }

                        EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
                    }

                    if (GUILayout.Button(Content.UnloadAllLabel))
                    {
                        foreach (var scene in loadableScenes)
                        {
                            if (entityManager.HasComponent<RequestSceneLoaded>(scene.Scene))
                                entityManager.RemoveComponent<RequestSceneLoaded>(scene.Scene);
                        }
                        EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
                    }

                    GUILayout.EndHorizontal();

                    if (reimportRequested && EditorUtility.DisplayDialog(Content.ReimportAllSubScenes, Content.ReimportAllSubScenesDetails, Content.Yes, Content.No))
                        SubSceneInspectorUtility.ForceReimport(subscenes);

                    GUILayout.Space(EditorGUIUtility.standardVerticalSpacing);
                }

                bool needsRepaint = false;
                foreach (var scene in loadableScenes)
                {
                    s_TmpContent.text = scene.Name;
                    s_TmpContent.tooltip = scene.SubScene.EditableScenePath;

                    var buttonRect = DrawButtonGridLabelAndGetFirstButtonRect(s_TmpContent, 3, out var spacing);

                    if (!IsSubsceneImported(scene.SubScene))
                    {
                        GUI.Label(buttonRect, Content.ImportingLabel);
                        needsRepaint = true;
                    }
                    else if (GUI.Button(buttonRect, Content.ReimportLabel))
                    {
                        SubSceneInspectorUtility.ForceReimport(scene.SubScene);
                    }

                    buttonRect.x += buttonRect.width + spacing;
                    if (!entityManager.HasComponent<RequestSceneLoaded>(scene.Scene))
                    {
                        if (GUI.Button(buttonRect, Content.LoadLabel))
                        {
                            entityManager.AddComponentData(scene.Scene, new RequestSceneLoaded());
                            EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
                        }
                    }
                    else
                    {
                        if (GUI.Button(buttonRect, Content.UnloadLabel))
                        {
                            entityManager.RemoveComponent<RequestSceneLoaded>(scene.Scene);
                            EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
                        }
                    }

                    buttonRect.x += buttonRect.width + spacing;
                    using (new EditorGUI.DisabledScope(!SubSceneInspectorUtility.CanEditScene(scene.SubScene)))
                    {
                        if (GUI.Button(buttonRect, Content.OpenLabel))
                        {
                            SubSceneInspectorUtility.EditScene(scene.SubScene);
                        }
                    }
                }

                return needsRepaint;
            }

            return false;
        }

        public override void OnInspectorGUI()
        {
            var subScene = target as SubScene;

            if (!subScene.IsInMainStage())
            {
                // In Prefab Mode and when selecting a Prefab Asset in the Project Browser we only show the inspector of data of the
                // SubScene, and not the load/unload/edit/close buttons.
                base.OnInspectorGUI();

                EditorGUILayout.HelpBox($"Only Sub Scenes in the Main Stage can be loaded and unloaded.", MessageType.Info, true);
                EditorGUILayout.Space();
                return;
            }

            var prevColor = subScene.HierarchyColor;
            CachePreviousSceneAssetReferences();

            base.OnInspectorGUI();

            HandleChangedSceneAssetReferences();

            if (subScene.HierarchyColor != prevColor)
                SceneHierarchyHooks.ReloadAllSceneHierarchies();

            DrawOpenSubScenes(_selectedSubscenes);
            var loadableScenes = SubSceneInspectorUtility.GetLoadableScenes(_selectedSubscenes);
            if (DrawClosedSubScenes(loadableScenes, _selectedSubscenes))
            {
                Repaint();
            }

#if false
            // @TODO: TEMP for debugging
            if (GUILayout.Button("ClearWorld"))
            {
                World.DisposeAllWorlds();
                DefaultWorldInitialization.Initialize("Default World", !Application.isPlaying);

                var scenes = FindObjectsOfType<SubScene>();
                foreach (var scene in scenes)
                {
                    var oldEnabled = scene.enabled;
                    scene.enabled = false;
                    scene.enabled = oldEnabled;
                }

                EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
            }
    #endif

            bool hasDuplicates = subScene.SceneAsset != null && (SubScene.AllSubScenes.Count(s => (s.SceneAsset == subScene.SceneAsset)) > 1);
            if (hasDuplicates)
            {
                EditorGUILayout.HelpBox($"The Scene Asset '{subScene.EditableScenePath}' is used mutiple times and this is not supported. Clear the reference.", MessageType.Warning, true);
                if (GUILayout.Button("Clear"))
                {
                    subScene.SceneAsset = null;
                    SceneHierarchyHooks.ReloadAllSceneHierarchies();
                }
                EditorGUILayout.Space();
            }

            var uncleanHierarchyObject = SubSceneInspectorUtility.GetUncleanHierarchyObject(_selectedSubscenes);
            if (uncleanHierarchyObject != null)
            {
                EditorGUILayout.HelpBox($"Scene transform values are not applied to scenes child transforms. But {uncleanHierarchyObject.name} has an offset Transform.", MessageType.Warning, true);
                if (GUILayout.Button("Clear"))
                {
                    foreach (var scene in _selectedSubscenes)
                    {
                        scene.transform.localPosition = Vector3.zero;
                        scene.transform.localRotation = Quaternion.identity;
                        scene.transform.localScale = Vector3.one;
                    }
                }
                EditorGUILayout.Space();
            }
            if (SubSceneInspectorUtility.HasChildren(_selectedSubscenes))
            {
                EditorGUILayout.HelpBox($"SubScenes can not have child game objects. Close the scene and delete the child game objects.", MessageType.Warning, true);
            }

            if (targets.Length == 1)
            {
                GUILayout.Space(EditorGUIUtility.singleLineHeight);
                if (CheckConversionLog(subScene))
                {
                    GUILayout.Label("Importing...");
                    Repaint();
                }

                if (m_ConversionLog.Length != 0)
                {
                    GUILayout.Space(EditorGUIUtility.singleLineHeight);

                    GUILayout.Label("Conversion Log", EditorStyles.boldLabel);
                    GUILayout.TextArea(m_ConversionLog);
                }
            }
        }

        // Invoked by Unity magically for FrameSelect command.
        // Frames the whole sub scene in scene view
        bool HasFrameBounds()
        {
            return !SubSceneInspectorUtility.GetActiveWorldMinMax(World.DefaultGameObjectInjectionWorld, targets).Equals(MinMaxAABB.Empty);
        }

        Bounds OnGetFrameBounds()
        {
            AABB aabb = SubSceneInspectorUtility.GetActiveWorldMinMax(World.DefaultGameObjectInjectionWorld, targets);
            return new Bounds(aabb.Center, aabb.Size);
        }

        // Visualize SubScene using bounding volume when it is selected.
        [DrawGizmo(GizmoType.Selected)]
        static void DrawSubsceneBounds(SubScene scene, GizmoType gizmoType)
        {
            SubSceneInspectorUtility.DrawSubsceneBounds(scene);
        }

        static bool IsSubsceneImported(SubScene subScene)
        {
            foreach (var world in World.All)
            {
                var sceneSystem = world.GetExistingSystem<SceneSystem>();
                if (sceneSystem is null)
                    continue;

                var hash = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, sceneSystem.BuildConfigurationGUID, ImportMode.NoImport);
                if (!hash.IsValid)
                    return false;
            }

            return true;
        }

        bool CheckConversionLog(SubScene subScene)
        {
            var pendingWork = false;

            foreach (var world in World.All)
            {
                var sceneSystem = world.GetExistingSystem<SceneSystem>();
                if (sceneSystem is null)
                    continue;

                if (!m_ConversionLogLoaded.TryGetValue(sceneSystem.BuildConfigurationGUID, out var loaded))
                    m_ConversionLogLoaded.Add(sceneSystem.BuildConfigurationGUID, false);
                else if (loaded)
                    continue;

                var hash = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, sceneSystem.BuildConfigurationGUID, ImportMode.Asynchronous);
                if (!hash.IsValid)
                {
                    pendingWork = true;
                    continue;
                }

                m_ConversionLogLoaded[sceneSystem.BuildConfigurationGUID] = true;

                AssetDatabaseCompatibility.GetArtifactPaths(hash, out var paths);
                var logPath = EntityScenesPaths.GetLoadPathFromArtifactPaths(paths, EntityScenesPaths.PathType.EntitiesConversionLog);
                if (logPath == null)
                    continue;

                var log = File.ReadAllText(logPath);
                if (log.Trim().Length != 0)
                {
                    if (m_ConversionLog.Length != 0)
                        m_ConversionLog += "\n\n";
                    m_ConversionLog += log;
                }
            }

            return pendingWork;
        }

        static class Content
        {
            public static readonly GUIContent OpenSubScenesLabel = EditorGUIUtility.TrTextContent("Open SubScenes");
            public static readonly GUIContent ClosedSubScenesLabel = EditorGUIUtility.TrTextContent("Closed SubScenes");
            public static readonly GUIContent CloseLabel = EditorGUIUtility.TrTextContent("Close");
            public static readonly GUIContent SaveLabel = EditorGUIUtility.TrTextContent("Save");
            public static readonly GUIContent LoadAllLabel = EditorGUIUtility.TrTextContent("Load All");
            public static readonly GUIContent UnloadAllLabel = EditorGUIUtility.TrTextContent("Unload All");
            public static readonly GUIContent ReimportAllLabel = EditorGUIUtility.TrTextContent("Reimport All");
            public static readonly GUIContent LoadLabel = EditorGUIUtility.TrTextContent("Load");
            public static readonly GUIContent UnloadLabel = EditorGUIUtility.TrTextContent("Unload");
            public static readonly GUIContent ReimportLabel = EditorGUIUtility.TrTextContent("Reimport");
            public static readonly GUIContent ImportingLabel = EditorGUIUtility.TrTextContent("Importing...");
            public static readonly GUIContent OpenLabel = EditorGUIUtility.TrTextContent("Open");
            public static readonly string ClosedStatusString = L10n.Tr("{0} / {1} loaded, {2} / {1} imported");
            public static readonly string ReimportAllSubScenes = L10n.Tr("Reimport All Selected SubScenes?");
            public static readonly string ReimportAllSubScenesDetails =
                L10n.Tr(
                    "Reimporting all SubScenes could take a considerable amount of time. Do you really want to trigger a reimport?");
            public static readonly string Yes = L10n.Tr("Yes");
            public static readonly string No = L10n.Tr("No");
        }
    }
}
