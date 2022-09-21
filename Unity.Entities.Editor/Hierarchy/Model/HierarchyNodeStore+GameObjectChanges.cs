using System.Collections.Generic;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using GameObjectChangeTrackerEventType = Unity.Editor.Bridge.GameObjectChangeTrackerEventType;

namespace Unity.Entities.Editor
{
    partial struct HierarchyNodeStore
    {
        static int GetSceneIndex(Scene scene)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i) == scene)
                    return i;
            }

            return 0;
        }

        internal void IntegrateGameObjectChanges(HierarchyGameObjectChanges changes)
        {
            foreach (var scene in changes.UnloadedScenes)
            {
                if (!scene.isRemoved)
                    continue;

                var sceneNode = scene.isSubScene ? HierarchyNodeHandle.FromSubScene(m_SubSceneNodeMapping, scene) : HierarchyNodeHandle.FromScene(scene);
                if (Exists(sceneNode))
                    RemoveNode(sceneNode);
            }

            var rootGameObjects = new List<GameObject>();
            foreach (var scene in changes.LoadedScenes)
            {
                var sceneNode = scene.isSubScene ? HierarchyNodeHandle.FromSubScene(m_SubSceneNodeMapping, scene) : HierarchyNodeHandle.FromScene(scene);

                if (!Exists(sceneNode))
                    AddNode(sceneNode);

                if (!scene.isSubScene)
                    SetSortIndex(sceneNode, GetSceneIndex(scene));

                if (!scene.isLoaded)
                    continue;

                scene.GetRootGameObjects(rootGameObjects);
                foreach (var gameObject in rootGameObjects)
                    RecursivelyAddNodes(gameObject, sceneNode);
            }

            if (changes.GameObjectChangeTrackerEvents.Length == 0)
                return;

            var events = changes.GameObjectChangeTrackerEvents.AsArray();
            for (var i = 0; i < events.Length; i++)
            {
                var changeTrackerEvent = events[i];
                var gameObject = EditorUtility.InstanceIDToObject(changeTrackerEvent.InstanceId) as GameObject;

                if ((changeTrackerEvent.EventType & GameObjectChangeTrackerEventType.SceneOrderChanged) != 0)
                {
                    for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
                    {
                        var scene = SceneManager.GetSceneAt(sceneIndex);
                        if (scene.isSubScene)
                            continue;
                        var sceneNode = HierarchyNodeHandle.FromScene(scene);
                        if (!Exists(sceneNode))
                            continue;

                        SetSortIndex(sceneNode, sceneIndex);
                    }
                }

                if ((changeTrackerEvent.EventType & GameObjectChangeTrackerEventType.Destroyed) != 0)
                {
                    var deletedHandle = HierarchyNodeHandle.FromGameObject(changeTrackerEvent.InstanceId);
                    if (Exists(deletedHandle))
                        RemoveNode(deletedHandle);

                    continue;
                }

                if (!gameObject
// Invalid scenes should be ignored by default, with the option to fail when encountered.
#if !DOTS_HIERARCHY_FAIL_ON_INVALID_SCENES
                    || !gameObject.scene.IsValid()
#endif
                    )
                    continue;

                var parent = GetParentNodeHandle(gameObject);
                if (!Exists(parent))
                {
                    // Check in the rest of the buffer if the parent is created
                    for (var j = i; j < events.Length; j++)
                    {
                        var evt = events[j];
                        if (evt.InstanceId == parent.Index && (evt.EventType & GameObjectChangeTrackerEventType.Destroyed) == 0)
                        {
                            // replace the current event with the one found
                            events[i] = evt;
                            // put the current event later in the buffer
                            events[j] = changeTrackerEvent;
                            // rewind
                            i--;
                            break;
                        }
                    }

                    continue;
                }

                var handle = GetNodeHandle(gameObject);
                if ((changeTrackerEvent.EventType & GameObjectChangeTrackerEventType.CreatedOrChanged) != 0)
                {
                    if (!Exists(handle))
                    {
                        // the node doesn't exist
                        // if the handle is a subscene, we try to create a go node to check if it exists
                        // this would mean a go has been converted to a subscene
                        if (handle.Kind == NodeKind.SubScene)
                        {
                            var goHandle = HierarchyNodeHandle.FromGameObject(gameObject);
                            if (Exists(goHandle))
                                RemoveNode(goHandle);
                        }

                        AddNode(handle, parent);
                        SetSortIndex(handle, gameObject.transform.GetSiblingIndex());
                    }
                }

                if (((changeTrackerEvent.EventType & GameObjectChangeTrackerEventType.ChangedScene) != 0 || ((changeTrackerEvent.EventType & GameObjectChangeTrackerEventType.ChangedParent) != 0)))
                {
                    if (!Exists(parent))
                    {
                        Debug.Log($"[{changeTrackerEvent.EventType}]: Ignoring GameObject {gameObject.name} ({gameObject.GetInstanceID()}), expected parent {parent} does not exist in the hierarchy");
                    }
                    else
                    {
                        SetParent(handle, parent);
                        SetSortIndex(handle, gameObject.transform.GetSiblingIndex());
                    }
                }
                else
                {
                    if ((changeTrackerEvent.EventType & GameObjectChangeTrackerEventType.OrderChanged) != 0)
                    {
                        SetSortIndex(handle, gameObject.transform.GetSiblingIndex());
                    }
                }
            }
        }

        HierarchyNodeHandle GetNodeHandle(GameObject gameObject)
        {
            if (gameObject.TryGetComponent<SubScene>(out var subscene))
                return HierarchyNodeHandle.FromSubScene(m_SubSceneNodeMapping, subscene);

            return HierarchyNodeHandle.FromGameObject(gameObject);
        }

        HierarchyNodeHandle GetParentNodeHandle(GameObject gameObject)
        {
            if (!gameObject.transform.parent)
            {
// Invalid scenes should be ignored by default, with the option to fail when encountered.
#if DOTS_HIERARCHY_FAIL_ON_INVALID_SCENES
                if (!gameObject.scene.IsValid())
                    throw new System.InvalidOperationException($"GameObject {gameObject.name} ({gameObject.GetInstanceID()}) is at root of a scene marked as not valid");
#endif
                // No GO parent, parent must be a scene
                // Note that we know we have a valid scene at this point
                return gameObject.scene.isSubScene
                    ? HierarchyNodeHandle.FromSubScene(m_SubSceneNodeMapping, gameObject.scene)
                    : HierarchyNodeHandle.FromScene(gameObject.scene);
            }

            return GetNodeHandle(gameObject.transform.parent.gameObject);
        }

        void RecursivelyAddNodes(GameObject gameObject, HierarchyNodeHandle parentHandle)
        {
            var handle = GetNodeHandle(gameObject);

            if (handle.Kind == NodeKind.SubScene)
            {
                // SubScene nodes are allowed to already exist
                // they can have been created by the Entity change integration
                if (!Exists(handle))
                {
                    AddNode(handle, parentHandle);
                }
                SetSortIndex(handle, gameObject.transform.GetSiblingIndex());

                var subscene = gameObject.GetComponent<SubScene>();
                if(!subscene.IsLoaded)
                    return;

                var rootGameObjects = new List<GameObject>();
                subscene.EditingScene.GetRootGameObjects(rootGameObjects);
                foreach (var rootGameObject in rootGameObjects)
                {
                    RecursivelyAddNodes(rootGameObject, handle);
                }
            }
            else
            {
                // Other nodes cannot be detected twice
                if (Exists(handle))
                    return;

                AddNode(handle, parentHandle);
                SetSortIndex(handle, gameObject.transform.GetSiblingIndex());

                foreach (Transform child in gameObject.transform)
                {
                    RecursivelyAddNodes(child.gameObject, handle);
                }
            }
        }
    }
}
