#if !UNITY_DOTSRUNTIME
using System;
using Unity.Assertions;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.SceneManagement;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    // This system manages GameObject Scene loading/unloading (much like ResolveSceneSystem and SceneStreamingSystem combined)
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(SceneSystemGroup))]
    [UpdateAfter(typeof(SceneSystem))]
    internal partial class GameObjectSceneSystem : SystemBase
    {
        EntityQuery m_LoadScenes;

        protected override void OnCreate()
        {
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
        }

        Scene GetExistingScene(Hash128 sceneGUID)
        {
            string scenePath = SceneSystem.GetScenePath(ref World.Unmanaged.GetExistingSystemState<SceneSystem>(), sceneGUID);

            // Try to find out scene amongst loaded scenes
            // TODO: DOTS-3329
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
            //TODO: DOTS-3329
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

            var scenePath = SceneSystem.GetScenePath(ref World.Unmanaged.GetExistingSystemState<SceneSystem>(), sceneGUID);

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

            return gameObjectScene;
#endif
        }

        protected override unsafe void OnUpdate()
        {
            if (!m_LoadScenes.IsEmptyIgnoreFilter)
            {
                Entities
                    .WithNone<GameObjectSceneData, DisableSceneResolveAndLoad>()
                    .WithStoreEntityQueryInField(ref m_LoadScenes)
                    .ForEach((Entity sceneEntity,
                        ref RequestGameObjectSceneLoaded requestGameObjectSceneLoaded,
                        ref GameObjectReference sceneReference,
                        ref RequestSceneLoaded requestSceneLoaded) =>
                    {
                        var async = (requestSceneLoaded.LoadFlags & SceneLoadFlags.BlockOnImport) == 0;

                        var gameObjectScene = GetExistingScene(sceneReference.SceneGUID);
                        if (gameObjectScene == default)
                        {
                            // No existing scene found, so load a new one
                            gameObjectScene = LoadGameObjectScene(sceneReference.SceneGUID, requestGameObjectSceneLoaded, async);
                        }
                        // TODO: Exposing Scene.loadingState in Root Scene Conversion PR
                        // TODO: DOTS-3329
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

                        GameObjectSceneUtility.FinalizeGameObjectSceneEntity(World.Unmanaged, EntityManager, sceneEntity, gameObjectScene);
                    }).WithStructuralChanges().Run();
            }
        }

        internal void UnloadAllScenes(ref SystemState state)
        {
            foreach (var e in GetEntityQuery(
                         new EntityQueryDesc
                         {
                             All = new[]
                             {
                                 ComponentType.ReadOnly<GameObjectReference>(),
                                 ComponentType.ReadOnly<GameObjectSceneData>(),
                             },
                         }).ToEntityArray(Allocator.Temp))
            {
                SceneSystem.UnloadScene(state.WorldUnmanaged, e);
            }
        }
    }
}
#endif
