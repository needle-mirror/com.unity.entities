#if !UNITY_DOTSRUNTIME
using System;
using Unity.Assertions;
using Unity.Entities;
using UnityEngine.SceneManagement;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    // This system manages GameObject Scene loading/unloading (much like ResolveSceneSystem and SceneStreamingSystem combined)
    [UnityEngine.ExecuteAlways]
    [AlwaysUpdateSystem]
    [UpdateInGroup(typeof(SceneSystemGroup))]
    [UpdateAfter(typeof(SceneSystem))]
    internal class GameObjectSceneSystem : ComponentSystem
    {
        EntityQuery m_LiveLinkDependency;
        EntityQuery m_LoadScenes;
        EntityQuery m_LoadedScenes;

        SceneSystem m_SceneSystem;

        protected override void OnCreate()
        {
            m_SceneSystem = World.GetOrCreateSystem<SceneSystem>();

            m_LiveLinkDependency = GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<RequestSceneLoaded>(),
                        ComponentType.ReadOnly<GameObjectReference>(),
                    },
                    None = new[]
                    {
                        ComponentType.ReadOnly<GameObjectSceneDependency>(),
                        ComponentType.ReadOnly<DisableSceneResolveAndLoad>(),
                    }
                });

            m_LoadScenes = GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<RequestGameObjectSceneLoaded>(),
                        ComponentType.ReadOnly<GameObjectReference>(),
                    },
                    None = new[]
                    {
                        ComponentType.ReadOnly<GameObjectSceneData>(),
                        ComponentType.ReadOnly<DisableSceneResolveAndLoad>(),
                    }
                });

            m_LoadedScenes = GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<GameObjectReference>(),
                        ComponentType.ReadOnly<GameObjectSceneData>(),
                    },
                });
        }

        Scene GetExistingScene(Hash128 sceneGUID)
        {
            string scenePath = World.GetExistingSystem<SceneSystem>().GetScenePath(sceneGUID);

            // Try to find out scene amongst loaded scenes
            // TODO: https://unity3d.atlassian.net/browse/DOTS-3329
            Scene gameObjectScene = default;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var currentScene = SceneManager.GetSceneAt(i);
                if (currentScene.path == scenePath)
                {
                    gameObjectScene = currentScene;
                    break;
                }
            }

            return gameObjectScene;
        }

        Scene LoadGameObjectScene(Hash128 sceneGUID, in RequestGameObjectSceneLoaded req, bool async)
        {
#if UNITY_EDITOR
            var scenePath = AssetDatabaseCompatibility.GuidToPath(sceneGUID);
            Scene gameObjectScene;

            //TODO: LoadSceneAsync() should work by GUID and should have GetSceneByGUID()
            //TODO: https://unity3d.atlassian.net/browse/DOTS-3329
            if (async)
            {
                var sceneLoadOperation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, req.loadParameters);
                sceneLoadOperation.allowSceneActivation = req.activateOnLoad;
                sceneLoadOperation.priority = req.priority;
                gameObjectScene = SceneManager.GetSceneAt(SceneManager.sceneCount-1);
            }
            else
            {
                gameObjectScene = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scenePath, req.loadParameters);
            }

            Assert.IsTrue(gameObjectScene.IsValid(), "Unity Scene is not valid even after loading.");

            return gameObjectScene;
#else
            Scene gameObjectScene = default;

            if (!LiveLinkUtility.LiveLinkEnabled)
            {
                var scenePath = World.GetExistingSystem<SceneSystem>().GetScenePath(sceneGUID);

                if (async)
                {
                    var sceneLoadOperation = SceneManager.LoadSceneAsync(scenePath, req.loadParameters);
                    sceneLoadOperation.allowSceneActivation = req.activateOnLoad;
                    sceneLoadOperation.priority = req.priority;
                    gameObjectScene = SceneManager.GetSceneAt(SceneManager.sceneCount-1);
                }
                else
                {
                    gameObjectScene = SceneManager.LoadScene(scenePath, req.loadParameters);
                }
            }
            else
            {
                var assetResolver = LiveLinkPlayerAssetRefreshSystem.GlobalAssetObjectResolver;

                if (assetResolver.HasAsset(sceneGUID))
                {
                    var firstBundle = assetResolver.GetAssetBundle(sceneGUID);
                    var scenePath = firstBundle.GetAllScenePaths()[0];
                    LiveLinkMsg.LogInfo($"Loading GameObject Scene with path {scenePath}.");

                    if (async)
                    {
                        var sceneLoadOperation = SceneManager.LoadSceneAsync(scenePath, req.loadParameters);
                        sceneLoadOperation.allowSceneActivation = req.activateOnLoad;
                        sceneLoadOperation.priority = req.priority;
                        gameObjectScene = SceneManager.GetSceneAt(SceneManager.sceneCount-1);
                    }
                    else
                    {
                        gameObjectScene = SceneManager.LoadScene(scenePath, req.loadParameters);
                    }
                }
            }

            return gameObjectScene;
#endif
        }

        protected override void OnUpdate()
        {
            if (LiveLinkUtility.LiveLinkEnabled && !m_LiveLinkDependency.IsEmptyIgnoreFilter)
            {
                Entities.With(m_LiveLinkDependency).ForEach((Entity sceneEntity, ref GameObjectReference sceneReference) =>
                {
                    var dependency = EntityManager.CreateEntity(typeof(ResourceGUID));
                    EntityManager.SetComponentData(dependency, new ResourceGUID() {Guid = sceneReference.SceneGUID});

                    EntityManager.AddComponentData(sceneEntity, new GameObjectSceneDependency {Value = dependency});
                });
            }

            if (!m_LoadScenes.IsEmptyIgnoreFilter)
            {
                Entities.With(m_LoadScenes).ForEach((Entity sceneEntity, ref RequestGameObjectSceneLoaded requestGameObjectSceneLoaded, ref GameObjectReference sceneReference, ref RequestSceneLoaded requestSceneLoaded) =>
                {
                    var async = (requestSceneLoaded.LoadFlags & SceneLoadFlags.BlockOnImport) == 0;

                    var gameObjectScene = GetExistingScene(sceneReference.SceneGUID);
                    if (gameObjectScene == default)
                    {
                        // No existing scene found, so load a new one
                        gameObjectScene = LoadGameObjectScene(sceneReference.SceneGUID, requestGameObjectSceneLoaded, async);

                        // We are in LiveLink and couldn't load the scene, this means we are waiting for it to be sent
                        if (LiveLinkUtility.LiveLinkEnabled && gameObjectScene == default)
                            return;
                    }
                    // TODO: Exposing Scene.loadingState in Root Scene Conversion PR
                    // TODO: https://unity3d.atlassian.net/browse/DOTS-3329
                    else if (!gameObjectScene.IsValid() || !gameObjectScene.isLoaded)
                    {
                        // Scene was found but is in an unloading state, so we essentially wait for it to finish so we can load it again
                        // This happens when you unload a GO scene async, it will still exist in the scene manager as invalid until it is unloaded completely
                        return;
                    }

                    // Scene is still loading, so it won't have any SubScenes yet
                    if (gameObjectScene.IsValid() && !gameObjectScene.isLoaded)
                        return;

                    if (!gameObjectScene.IsValid())
                        throw new InvalidOperationException($"Unity Scene is invalid. Path={gameObjectScene.path} Name={gameObjectScene.name} BuildIndex={gameObjectScene.buildIndex}");

                    GameObjectSceneUtility.FinalizeGameObjectSceneEntity(EntityManager, sceneEntity, gameObjectScene, m_SceneSystem);
                });
            }
        }

        internal void UnloadAllScenes()
        {
            LiveLinkMsg.LogInfo($"Unloading all GameObject Scenes.");
            var sceneSystem = World.GetExistingSystem<SceneSystem>();
            Entities.With(m_LoadedScenes).ForEach((Entity entity, ref GameObjectReference sceneReference) =>
            {
                sceneSystem.UnloadScene(entity);
            });
        }
    }
}
#endif
