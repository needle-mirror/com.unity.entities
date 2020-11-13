#if !UNITY_DOTSRUNTIME
using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.SceneManagement;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    public class GameObjectSceneUtility
    {
        private readonly struct SubSceneData : IEquatable<SubSceneData>
        {
            public readonly Hash128 SceneGUID;
            public readonly bool AutoLoad;

            public SubSceneData(Hash128 sceneGuid, bool autoLoad)
            {
                SceneGUID = sceneGuid;
                AutoLoad = autoLoad;
            }

            public bool Equals(SubSceneData other)
            {
                return SceneGUID.Equals(other.SceneGUID);
            }

            public override int GetHashCode()
            {
                return SceneGUID.GetHashCode();
            }
        }
        private static NativeMultiHashMap<int, SubSceneData> SubSceneLookup;

        private static NativeMultiHashMap<int, SubSceneData>.Enumerator GetSubScenes(Scene gameObjectScene)
        {
            if (!SubSceneLookup.IsCreated)
                CreateSubSceneLookup();
            return SubSceneLookup.GetValuesForKey(gameObjectScene.handle);
        }

        private static int GetSubSceneCount(Scene gameObjectScene)
        {
            if (!SubSceneLookup.IsCreated)
                return 0;
            return SubSceneLookup.CountValuesForKey(gameObjectScene.handle);
        }

        internal static void RegisterSubScene(Scene gameObjectScene, SubScene subScene)
        {
            if (!SubSceneLookup.IsCreated)
                CreateSubSceneLookup();
            SubSceneLookup.Add(gameObjectScene.handle, new SubSceneData(subScene.SceneGUID, subScene.AutoLoadScene));
        }

        internal static void UnregisterSubScene(Scene gameObjectScene, SubScene subScene)
        {
            if (!SubSceneLookup.IsCreated)
                return;
            SubSceneLookup.Remove(gameObjectScene.handle, new SubSceneData(subScene.SceneGUID, subScene.AutoLoadScene));
        }

        private static void CreateSubSceneLookup()
        {
            SubSceneLookup = new NativeMultiHashMap<int, SubSceneData>(0, Allocator.Persistent);

            #if UNITY_EDITOR
            AppDomain.CurrentDomain.DomainUnload += OnDomainUnload;
            #endif
        }

        #if UNITY_EDITOR
        private static void OnDomainUnload(object sender, EventArgs e)
        {
            SubSceneLookup.Dispose();
        }
        #endif

        // Adds all current GameObject Scenes loaded in the Editor to the GameObjectScene System (effectively adds a ref count)
        // This can be used in custom Entity Bootstrap code to ensure currently loaded Game Object Scenes are added as references to the GameObjectSceneSystem
        // for cases where these scenes were loaded without the GameObjectSceneSystem (eg in the Editor and pressing Play).
        public static void AddGameObjectSceneReferences()
        {
            foreach (var world in World.All)
            {
                var sys = world.GetExistingSystem<SceneSystem>();
                if (sys != null)
                {
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        // TODO: https://unity3d.atlassian.net/browse/DOTS-3329
                        var scene = SceneManager.GetSceneAt(i);
                        var guid = sys.GetSceneGUID(scene.path);
                        if (guid.IsValid)
                        {
                            // This does not actually load the scene, as it will detect the scene is loaded
                            // and just add a reference
                            LoadGameObjectSceneAsync(sys, guid, default);
                        }
                    }
                }
            }
        }

        internal static void FinalizeGameObjectSceneEntity(EntityManager entityManager, Entity sceneEntity, Scene gameObjectScene, SceneSystem sceneSystem)
        {
            // Create handle for ref counting
            unsafe
            {
                var gameObjectSceneReference = new GameObjectSceneData
                {
                    Scene = gameObjectScene,
                    gameObjectSceneHandle = GameObjectSceneRefCount.CreateOrRetainScene(gameObjectScene),
                };

                entityManager.AddSharedComponentData(sceneEntity, gameObjectSceneReference);
            }

            #if UNITY_EDITOR
            entityManager.SetName(sceneEntity, $"GameObject Scene: {gameObjectScene.name}");
            #endif

            var subSceneDataEnum = GetSubScenes(gameObjectScene);
            entityManager.AddBuffer<GameObjectSceneSubScene>(sceneEntity);

            while(subSceneDataEnum.MoveNext())
            {
                var subSceneData = subSceneDataEnum.Current;
                var subSceneLoadParameters = new SceneSystem.LoadParameters {AutoLoad = subSceneData.AutoLoad };
                var subSceneEntity = sceneSystem.GetSceneEntity(subSceneData.SceneGUID);
                if (subSceneEntity == Entity.Null)
                {
                    subSceneEntity = sceneSystem.LoadSceneAsync(subSceneData.SceneGUID, subSceneLoadParameters);
                }

                var subSceneBuffer = entityManager.GetBuffer<GameObjectSceneSubScene>(sceneEntity);
                subSceneBuffer.Add(new GameObjectSceneSubScene
                {
                    SceneEntity = subSceneEntity,
                });
            }
        }

        internal static bool IsGameObjectSceneLoaded(SceneSystem sys, Entity entity)
        {
            if (!sys.EntityManager.HasComponent<GameObjectSceneData>(entity))
                return false;

            var unitySceneReference = sys.EntityManager.GetSharedComponentData<GameObjectSceneData>(entity);

            if (!unitySceneReference.Scene.IsValid() || !unitySceneReference.Scene.isLoaded)
                return false;

            if (!sys.EntityManager.HasComponent<GameObjectSceneSubScene>(entity))
            {
                return false;
            }

            var subSceneEntities = sys.EntityManager.GetBuffer<GameObjectSceneSubScene>(entity);
            foreach (var subScene in subSceneEntities)
            {
                // If a SubScene has AutoLoad DISABLED then it will return false from IsSceneLoaded. So in this case, it's not enough
                // to just check if a SubScene of this GameObject Scene is loaded, we also need to check if the NOT loaded SubScene
                // is actually AutoLoad DISABLED, in which case that is not a reason to indicate this GameObject Scene is not loaded.
                if (sys.EntityManager.Exists(subScene.SceneEntity) && !sys.IsSceneLoaded(subScene.SceneEntity))
                {
                    if (sys.EntityManager.HasComponent<RequestSceneLoaded>(subScene.SceneEntity))
                    {
                        var request = sys.EntityManager.GetComponentData<RequestSceneLoaded>(subScene.SceneEntity);
                        if (request.LoadFlags.HasFlag(SceneLoadFlags.DisableAutoLoad))
                            continue;
                    }
                    return false;
                }
            }

            return true;
        }

        private static Entity CreateGameObjectSceneEntity(SceneSystem sys, Hash128 sceneGUID)
        {
            var sceneEntity = sys.EntityManager.CreateEntity();
            sys.EntityManager.AddComponentData(sceneEntity, new GameObjectReference {SceneGUID = sceneGUID});
            return sceneEntity;
        }

        internal static Entity LoadGameObjectSceneAsync(SceneSystem sys, Hash128 sceneGUID, SceneSystem.LoadParameters parameters)
        {
            var sceneEntity = CreateGameObjectSceneEntity(sys, sceneGUID);
            LoadGameObjectSceneAsync(sys, sceneEntity, parameters);
            return sceneEntity;
        }

        internal static void LoadGameObjectSceneAsync(SceneSystem sys, Entity sceneEntity, SceneSystem.LoadParameters parameters)
        {
            var requestSceneLoaded = new RequestSceneLoaded { LoadFlags = parameters.Flags};
            sys.EntityManager.AddComponentData(sceneEntity, requestSceneLoaded);

            if ((parameters.Flags & SceneLoadFlags.LoadAdditive) == SceneLoadFlags.LoadAdditive)
            {
                Debug.LogWarning("Deprecation Warning - SceneLoadFlags.LoadAdditive is deprecated. Scenes loaded through the SceneSystem are always loaded Additively. (RemovedAfter 2021-02-05)");
            }

            var loadSceneParameters = new LoadSceneParameters(LoadSceneMode.Additive);
            var activeOnLoad = (parameters.Flags & SceneLoadFlags.DisableAutoLoad) != SceneLoadFlags.DisableAutoLoad;

            var sceneLoadRequest = new RequestGameObjectSceneLoaded
            {
                loadParameters = loadSceneParameters,
                activateOnLoad = activeOnLoad,
                priority = parameters.Priority
            };
            sys.EntityManager.AddComponentData(sceneEntity, sceneLoadRequest);
        }

        internal static void UnloadGameObjectScene(SceneSystem sys, Entity sceneEntity, SceneSystem.UnloadParameters unloadParams)
        {
            bool removeRequest = (unloadParams & SceneSystem.UnloadParameters.DontRemoveRequestSceneLoaded) == 0;
            bool destroySceneProxyEntity = (unloadParams & SceneSystem.UnloadParameters.DestroySceneProxyEntity) != 0;
            bool destroySectionProxyEntities = (unloadParams & SceneSystem.UnloadParameters.DestroySectionProxyEntities) != 0;
            bool destroySubSceneProxyEntities = (unloadParams & SceneSystem.UnloadParameters.DestroySubSceneProxyEntities) != 0;

            if (destroySceneProxyEntity && !destroySectionProxyEntities)
                throw new ArgumentException("When unloading a scene it's not possible to destroy the scene entity without also destroying the section entities. Please also add the UnloadParameters.DestroySectionProxyEntities flag");

            if (sys.EntityManager.HasComponent<GameObjectSceneDependency>(sceneEntity))
            {
                var dependency = sys.EntityManager.GetComponentData<GameObjectSceneDependency>(sceneEntity);
                sys.EntityManager.DestroyEntity(dependency.Value);
                sys.EntityManager.RemoveComponent<GameObjectSceneDependency>(sceneEntity);
            }

            if (removeRequest)
            {
                sys.EntityManager.RemoveComponent<RequestGameObjectSceneLoaded>(sceneEntity);
                sys.EntityManager.RemoveComponent<RequestSceneLoaded>(sceneEntity);
            }

            if (sys.EntityManager.HasComponent<GameObjectSceneSubScene>(sceneEntity))
            {
                var gameObjectSceneSubScenes = sys.EntityManager.GetBuffer<GameObjectSceneSubScene>(sceneEntity).ToNativeArray(Allocator.Temp);
                for(int i = gameObjectSceneSubScenes.Length-1; i >= 0; i--)
                {
                    var subScene = gameObjectSceneSubScenes[i];

                    // Prune destroy Scene Entities
                    if (!sys.EntityManager.Exists(subScene.SceneEntity))
                    {
                        var buf = sys.EntityManager.GetBuffer<GameObjectSceneSubScene>(sceneEntity);
                        buf.RemoveAt(i);
                        continue;
                    }

                    var subSceneUnloadParams = unloadParams;

                    if (destroySubSceneProxyEntities)
                        subSceneUnloadParams |= SceneSystem.UnloadParameters.DestroySceneProxyEntity;

                    sys.UnloadScene(subScene.SceneEntity, subSceneUnloadParams);
                }
                gameObjectSceneSubScenes.Dispose();

                if(destroySubSceneProxyEntities)
                    sys.EntityManager.RemoveComponent<GameObjectSceneSubScene>(sceneEntity);
            }

            sys.EntityManager.RemoveComponent<GameObjectSceneData>(sceneEntity);

            if(destroySceneProxyEntity)
                sys.EntityManager.DestroyEntity(sceneEntity);
        }
    }
}
#endif
