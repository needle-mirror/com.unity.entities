using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;
using Unity.Entities.Streaming;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Experimental.PlayerLoop;
using Object = UnityEngine.Object;

namespace Unity.Scenes.Editor
{
    [ExecuteAlways]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateBefore(typeof(SubSceneStreamingSystem))]
    class SubSceneLiveLinkSystem : ComponentSystem
    {
        class GameObjectPrefabLiveLinkSceneTracker : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
            {
                foreach (var asset in importedAssets)
                {
                    if (asset.EndsWith(".prefab", true, System.Globalization.CultureInfo.InvariantCulture))
                        GlobalDirtyLiveLink();
                }
            }
        }
        
        static int GlobalDirtyID = 0;
        static int PreviousGlobalDirtyID = 0;
    
        UInt64 m_LiveLinkEditSceneCullingMask = 1UL << 60;
        UInt64 m_GameObjectSceneCullingMask = 1UL << 58;
        UInt64 m_EntitySceneCullingMask = 1UL << 59;

        static void AddUnique(List<SubScene> list, SubScene scene)
        {
            if (!list.Contains(scene))
                list.Add(scene);
        }

        protected override void OnUpdate()
        {
            var needLiveLinkSync = new List<SubScene>();
            var cleanupScene = new List<SubScene>();
            var markSceneLoadedFromLiveLink = new List<SubScene>();
            var removeSceneLoadedFromLiveLink = new List<SubScene>();

            // By default all scenes need to have m_GameObjectSceneCullingMask, otherwise they won't show up in game view
            for (int i = 0; i != EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (EditorSceneManager.GetSceneCullingMask(scene) == EditorSceneManager.DefaultSceneCullingMask)
                {
                    EditorSceneManager.SetSceneCullingMask(scene, EditorSceneManager.DefaultSceneCullingMask | m_GameObjectSceneCullingMask);
                }
            }
    
            if (PreviousGlobalDirtyID != GlobalDirtyID)
            {
                ForEach((SubScene subScene) => subScene.LiveLinkDirtyID = -1);
                PreviousGlobalDirtyID = GlobalDirtyID;
            }
    
            ForEach((SubScene subScene) =>
            {
                // We are editing with live link. Ensure it is active & up to date
                if (subScene.IsLoaded && SubSceneInspectorUtility.LiveLinkEnabled)
                {
                    if (subScene.LiveLinkDirtyID != GetSceneDirtyID(subScene.LoadedScene) || subScene.LiveLinkShadowWorld == null)
                        AddUnique(needLiveLinkSync, subScene);
                }
                // We are editing without live link.
                // We should have no entity representation loaded for the scene.
                else if (subScene.IsLoaded && !SubSceneInspectorUtility.LiveLinkEnabled)
                {
                    var hasAnythingLoaded = false;
                    foreach (var s in subScene._SceneEntities)
                        hasAnythingLoaded |= EntityManager.HasComponent<SubSceneStreamingSystem.StreamingState>(s) || !EntityManager.HasComponent<SubSceneStreamingSystem.IgnoreTag>(s);

                    if (hasAnythingLoaded)
                    {
                        AddUnique(cleanupScene, subScene);
                        AddUnique(markSceneLoadedFromLiveLink, subScene);
                    }
                }
                // Scene is not being edited, thus should not be live linked.
                else
                {
                    var isDrivenByLiveLink = false;
                    foreach (var s in subScene._SceneEntities)
                        isDrivenByLiveLink |= EntityManager.HasComponent<SubSceneStreamingSystem.IgnoreTag>(s);

                    if (isDrivenByLiveLink || subScene.LiveLinkShadowWorld != null)
                    {
                        AddUnique(cleanupScene, subScene);
                        AddUnique(removeSceneLoadedFromLiveLink, subScene);
                    }
                }

                if (subScene.LoadedScene.isLoaded)
                {
                    if (SubSceneInspectorUtility.LiveLinkEnabled)
                       EditorSceneManager.SetSceneCullingMask(subScene.LoadedScene, m_LiveLinkEditSceneCullingMask | EditorSceneManager.DefaultSceneCullingMask);
                    else
                        EditorSceneManager.SetSceneCullingMask(subScene.LoadedScene, EditorSceneManager.DefaultSceneCullingMask | m_GameObjectSceneCullingMask);
                }
            });
    
            foreach (var camera in Camera.allCameras)
            {
                if (camera.cameraType == CameraType.Game)
                    camera.overrideSceneCullingMask = m_EntitySceneCullingMask | m_GameObjectSceneCullingMask;
            }
    
            
            // Live link changes to entity world
            foreach (var scene in needLiveLinkSync)
            {
                // Prevent live link updating during drag operation
                // (Currently performance is not good enough to do it completely live)
                if (!IsHotControlActive())
                    ApplyLiveLink(scene);
                else
                    EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
            }
            
            // Live link changes to entity world
            foreach (var scene in cleanupScene)
            {
                CleanupScene(scene);
            }
            
            foreach (var scene in markSceneLoadedFromLiveLink)
            {
                foreach (var sceneEntity in scene._SceneEntities)
                {
                    if (!EntityManager.HasComponent<SubSceneStreamingSystem.IgnoreTag>(sceneEntity))
                        EntityManager.AddComponentData(sceneEntity, new SubSceneStreamingSystem.IgnoreTag());
                }
            }
            
            foreach (var scene in removeSceneLoadedFromLiveLink)
            {
                foreach (var sceneEntity in scene._SceneEntities)
                {
                    EntityManager.RemoveComponent<SubSceneStreamingSystem.IgnoreTag>(sceneEntity);
                }
            }
        }

        void CleanupScene(SubScene scene)
        {
            // Debug.Log("CleanupScene: " + scene.SceneName);
            scene.CleanupLiveLink();
                
            var streamingSystem = World.GetExistingManager<SubSceneStreamingSystem>();
    
            foreach (var sceneEntity in scene._SceneEntities)
            {
                streamingSystem.UnloadSceneImmediate(sceneEntity);
                EntityManager.DestroyEntity(sceneEntity);
            }
            scene._SceneEntities = new List<Entity>();

            scene.UpdateSceneEntities();
        }

        void ApplyLiveLink(SubScene scene)
        {
            //Debug.Log("ApplyLiveLink: " + scene.SceneName);
            
            var streamingSystem = World.GetExistingManager<SubSceneStreamingSystem>();

            var isFirstTime = scene.LiveLinkShadowWorld == null;
            if (scene.LiveLinkShadowWorld == null)
                scene.LiveLinkShadowWorld = new World("LiveLink");
    
                  
            using (var cleanConvertedEntityWorld = new World("Clean Entity Conversion World"))
            {
                // Unload scene 
                //@TODO: We optimally shouldn't be unloading the scene here. We should simply prime the shadow world with the scene that we originally loaded into the player (Including Entity GUIDs)
                //       This way we can continue the live link, compared to exactly what we loaded into the player.
                if (isFirstTime)
                {
                    foreach (var s in scene._SceneEntities)
                    {
                        streamingSystem.UnloadSceneImmediate(s);
                        EntityManager.DestroyEntity(s);
                    }
                    
                    var sceneEntity = EntityManager.CreateEntity();
                    EntityManager.SetName(sceneEntity, "Scene (LiveLink): " + scene.SceneName);
                    EntityManager.AddComponentObject(sceneEntity, scene);
                    EntityManager.AddComponentData(sceneEntity, new SubSceneStreamingSystem.StreamingState { Status = SubSceneStreamingSystem.StreamingStatus.Loaded});
                    EntityManager.AddComponentData(sceneEntity, new SubSceneStreamingSystem.IgnoreTag( ));
                    
                    scene._SceneEntities = new List<Entity>();
                    scene._SceneEntities.Add(sceneEntity);
                }
                
                // Convert scene
                GameObjectConversionUtility.ConvertScene(scene.LoadedScene, scene.SceneGUID, cleanConvertedEntityWorld, GameObjectConversionUtility.ConversionFlags.AddEntityGUID | GameObjectConversionUtility.ConversionFlags.AssignName);

                var convertedEntityManager = cleanConvertedEntityWorld.GetOrCreateManager<EntityManager>();

                var liveLinkSceneEntity = scene._SceneEntities[0];
                
                /// We want to let the live linked scene be able to reference the already existing Scene Entity (Specifically SceneTag should point to the scene Entity after live link completes) 
                // Add Scene tag to all entities using the convertedSceneEntity that will map to the already existing scene entity.
                convertedEntityManager.AddSharedComponentData(convertedEntityManager.UniversalGroup, new SceneTag { SceneEntity = liveLinkSceneEntity });

                WorldDiffer.DiffAndApply(cleanConvertedEntityWorld, scene.LiveLinkShadowWorld, World);

                convertedEntityManager.Debug.CheckInternalConsistency();
                scene.LiveLinkShadowWorld.GetOrCreateManager<EntityManager>().Debug.CheckInternalConsistency();

                var group = EntityManager.CreateComponentGroup(typeof(SceneTag), ComponentType.Exclude<EditorRenderData>());
                group.SetFilter(new SceneTag {SceneEntity = liveLinkSceneEntity});
                
                EntityManager.AddSharedComponentData(group, new EditorRenderData() { SceneCullingMask = m_EntitySceneCullingMask, PickableObject = scene.gameObject });

                group.Dispose();
                
                scene.LiveLinkDirtyID = GetSceneDirtyID(scene.LoadedScene);
                EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
            }
        }
    
    
        static GameObject GetGameObjectFromAny(Object target)
        {
            Component component = target as Component;
            if (component != null)
                return component.gameObject;
            return target as GameObject;
        }
        
        internal static bool IsHotControlActive()
        {
            return GUIUtility.hotControl != 0;
        }
    
    
        UndoPropertyModification[] PostprocessModifications(UndoPropertyModification[] modifications)
        {
            foreach (var mod in modifications)
            {
                var target = GetGameObjectFromAny(mod.currentValue.target);
                if (target)
                {
                    var targetScene = target.scene;
                    ForEach((SubScene scene) =>
                    {
                        if (scene.IsLoaded && scene.LoadedScene == targetScene)
                        {
                            scene.LiveLinkDirtyID = -1;    
                        }
                    });
                }
            }
    
            return modifications;
        }
        
        static int GetSceneDirtyID(Scene scene)
        {
            if (scene.IsValid())
            {
                var method = typeof(Scene).GetProperty("dirtyID", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetMethod;
                return (int)method.Invoke(scene, null);
            }
            else
                return -1;
        }
    
        static void GlobalDirtyLiveLink()
        {
            GlobalDirtyID++;
        }
    
        protected override void OnCreateManager()
        {        
            Undo.postprocessModifications += PostprocessModifications;
            Undo.undoRedoPerformed += SubSceneLiveLinkSystem.GlobalDirtyLiveLink;
        }
    
        protected override void OnDestroyManager()
        {        
            Undo.postprocessModifications -= PostprocessModifications;
            Undo.undoRedoPerformed -= SubSceneLiveLinkSystem.GlobalDirtyLiveLink;
    
        }
    }
}

