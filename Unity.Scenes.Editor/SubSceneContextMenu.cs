using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;
using System.Reflection;

namespace Unity.Scenes.Editor
{
    class SubSceneContextMenu
    {
        public static void AddExtraGameObjectContextMenuItems(GenericMenu menu, GameObject target)
        {
            menu.AddSeparator("");
            var addSubSceneContent = EditorGUIUtility.TrTextContent("New SubScene From Selection");
            if (!EditorApplication.isPlaying && GetValidSelectedGameObjectsForSubSceneCreation(target) != null)
                menu.AddItem(addSubSceneContent, false, CreateSubSceneAndAddSelection, target);
            else
                menu.AddDisabledItem(addSubSceneContent);
        }
    
        static GameObject[] GetValidSelectedGameObjectsForSubSceneCreation(GameObject target)
        {
            if (target == null)
                return null;
            if (!target.scene.IsValid())
                return null;
            if (string.IsNullOrEmpty(target.scene.path))
                return null;
    
            var selection = Selection.GetFiltered<GameObject>(SelectionMode.TopLevel);
            if (selection.Any(x => EditorUtility.IsPersistent(x)))
                return null;

            if (!selection.Contains(target))
                return null;
            return selection;
        }
    
        internal static void CreateSubSceneAndAddSelection(object target)
        {
            CreateSubSceneAndAddSelection((GameObject)target, InteractionMode.UserAction);
        }

        internal static SubScene CreateSubSceneAndAddSelection(GameObject gameObjectTarget, InteractionMode interactionMode)
        {
            var validSelection = GetValidSelectedGameObjectsForSubSceneCreation(gameObjectTarget);
            if (validSelection == null)
                return null;

            int siblingIndex = gameObjectTarget.transform.GetSiblingIndex();
            string errorMessage;
            SubScene subScene = CreateSubSceneAndMoveObjectInside(gameObjectTarget.scene, gameObjectTarget.transform.parent, validSelection, gameObjectTarget.name, interactionMode, out errorMessage);
            if (subScene != null)
            {
                subScene.gameObject.transform.SetSiblingIndex(siblingIndex);
            }
            else
            {
                if (interactionMode == InteractionMode.UserAction)
                    EditorUtility.DisplayDialog("Could not create Sub Scene", errorMessage, "OK");
            }
            return subScene;
        }

        static bool IsValidSubSceneFileName(string fileName, string destinationPath, out string errorDialogMessage)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                errorDialogMessage = "The provided name for the Sub Scene is empty. This is not allowed since the name is used when creating the Scene asset file.";
                return false;
            }

            var invalidIndex = fileName.IndexOfAny(Path.GetInvalidFileNameChars());
            if (invalidIndex >= 0)
            {
                char invalidChar = fileName[invalidIndex];
                errorDialogMessage = $"The name '{fileName}' contains the invalid character: '{invalidChar}'. This is not allowed since the name is used when creating the Scene asset file.";
                return false;
            }

            if (File.Exists(destinationPath))
            {
                errorDialogMessage = $"A Scene already exists at '{destinationPath}'. Rename '{fileName}' to prevent overwriting the existing Scene file.";
                return false;
            }

            errorDialogMessage = string.Empty;
            return true;
        }

        static string GetActualPathName(string path)
        {
            //@TODO: GetActualPathName is expected to become public in 2020.1
            var getActualPathName = typeof(FileUtil).GetMethod("GetActualPathName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return (string)getActualPathName?.Invoke(null, new object[] { path });
        }

        static bool HasAnyUntitledScene()
        {
            return SceneManager.GetSceneByPath(string.Empty).IsValid();
        }

        static SubScene CreateSubSceneAndMoveObjectInside(Scene parentScene, Transform parent, GameObject[] topLevelObjects, string name, InteractionMode interactionMode, out string errorMessage)
        {
            name = name.Trim();
            var srcPath = parentScene.path;
            var dstDirectory = Path.Combine(Path.GetDirectoryName(srcPath), Path.GetFileNameWithoutExtension(parentScene.path));
            var dstPath = GetActualPathName(Path.Combine(dstDirectory, name + ".unity"));
            dstDirectory = Path.GetDirectoryName(dstPath);
 
            if (!IsValidSubSceneFileName(name, dstPath, out errorMessage))
            {
                return null;
            }

            if (HasAnyUntitledScene())
            {
                errorMessage = "Save the Untitled Scene before creating a Sub Scene.";
                return null;
            }

            if (topLevelObjects.Any(x => PrefabUtility.IsPartOfAnyPrefab(x) && !PrefabUtility.IsOutermostPrefabInstanceRoot(x)))
            {
                errorMessage = "Cannot create a Sub Scene from a part of a Prefab instance. Select the outermost Prefab root.";
                return null;
            }

            Directory.CreateDirectory(dstDirectory);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            scene.isSubScene = true;

            try
            {
                var undoName = "Create Sub Scene";

                switch (interactionMode)
                {
                    case InteractionMode.AutomatedAction:
                        foreach (var go in topLevelObjects)
                        {
                            go.transform.SetParent(null, true);
                            SceneManager.MoveGameObjectToScene(go, scene);
                        }
                        break;
                    case InteractionMode.UserAction:
                        foreach (var go in topLevelObjects)
                        {
                            Undo.SetTransformParent(go.transform, null, undoName);
                            Undo.MoveGameObjectToScene(go, scene, undoName);
                        }
                        break;
                    default:
                        Debug.LogError("Enum not handled"); 
                        break;
                }

                EditorSceneManager.SaveScene(scene, dstPath);

                var gameObject = new GameObject(name, typeof(SubScene));
                gameObject.SetActive(false);
                var subSceneComponent = gameObject.GetComponent<SubScene>();
                var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(dstPath);
                subSceneComponent.SceneAsset = sceneAsset;

                if (parent)
                    gameObject.transform.parent = parent;
                else
                    SceneManager.MoveGameObjectToScene(gameObject, parentScene);

                gameObject.SetActive(true);

                if (interactionMode == InteractionMode.UserAction)
                    Undo.RegisterCreatedObjectUndo(gameObject, undoName);

                Selection.activeObject = gameObject;

                EditorSceneManager.MarkSceneDirty(parentScene);
                return subSceneComponent;
            }
            catch
            {
                EditorSceneManager.CloseScene(scene, true);
                throw;
            }
        }
    }
}
