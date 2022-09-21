#if !UNITY_DOTSRUNTIME
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine.SceneManagement;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    /// <summary>
    /// Utility class for handling authoring scenes.
    /// </summary>
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

        /// <summary>
        /// Adds all current GameObject Scenes loaded in the Editor to the GameObjectScene system.
        /// </summary>
        /// <remarks>
        /// You can use this in custom Entity bootstrap code. This makes sure that currently loaded GameObject scenes are added as references to the GameObjectSceneSystem
        /// in cases where the scenes were loaded without the GameObjectSceneSystem, for example when you enter Play mode in the Editor. This effectively adds a ref count.
        /// </remarks>
        public static unsafe void AddGameObjectSceneReferences()
        {
            foreach (var world in World.All)
            {
                var sys = world.Unmanaged.GetExistingUnmanagedSystem<SceneSystem>();
                var statePtr = world.Unmanaged.ResolveSystemState(sys);
                if (statePtr != null)
                {
                    ref var state = ref *statePtr;

                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        // TODO: DOTS-3329
                        var scene = SceneManager.GetSceneAt(i);
                        var guid = SceneSystem.GetSceneGUID(ref state, scene.path);
                        if (guid.IsValid)
                        {
                            // This does not actually load the scene, as it will detect the scene is loaded
                            // and just add a reference
                            LoadGameObjectSceneAsync(world.Unmanaged, guid, default);
                        }
                    }
                }
            }
        }

        internal static void FinalizeGameObjectSceneEntity(WorldUnmanaged world, EntityManager entityManager, Entity sceneEntity, Scene gameObjectScene)
        {
            // Create handle for ref counting
            unsafe
            {
                var gameObjectSceneReference = new GameObjectSceneData
                {
                    Scene = gameObjectScene,
                    gameObjectSceneHandle = GameObjectSceneRefCount.CreateOrRetainScene(gameObjectScene),
                };

                entityManager.AddSharedComponentManaged(sceneEntity, gameObjectSceneReference);
            }

            var fs = new FixedString64Bytes();
            FixedStringMethods.CopyFromTruncated(ref fs, $"GameObject Scene: {gameObjectScene.name}");
            entityManager.SetName(sceneEntity, fs);

            var subSceneDataEnum = GetSubScenes(gameObjectScene);
            entityManager.AddBuffer<GameObjectSceneSubScene>(sceneEntity);

            while(subSceneDataEnum.MoveNext())
            {
                var subSceneData = subSceneDataEnum.Current;
                var subSceneLoadParameters = new SceneSystem.LoadParameters {AutoLoad = subSceneData.AutoLoad };
                var subSceneEntity = SceneSystem.GetSceneEntity(world, subSceneData.SceneGUID);
                if (subSceneEntity == Entity.Null)
                {
                    subSceneEntity = SceneSystem.LoadSceneAsync(world, subSceneData.SceneGUID, subSceneLoadParameters);
                }

                var subSceneBuffer = entityManager.GetBuffer<GameObjectSceneSubScene>(sceneEntity);
                subSceneBuffer.Add(new GameObjectSceneSubScene
                {
                    SceneEntity = subSceneEntity,
                });
            }
        }

        internal static bool IsGameObjectSceneLoaded(WorldUnmanaged world, Entity entity)
        {
            if (!world.EntityManager.HasComponent<GameObjectSceneData>(entity))
                return false;

            var unitySceneReference = world.EntityManager.GetSharedComponentManaged<GameObjectSceneData>(entity);

            if (!unitySceneReference.Scene.IsValid() || !unitySceneReference.Scene.isLoaded)
                return false;

            if (!world.EntityManager.HasComponent<GameObjectSceneSubScene>(entity))
            {
                return false;
            }

            var subSceneEntities = world.EntityManager.GetBuffer<GameObjectSceneSubScene>(entity);

            foreach (var subScene in subSceneEntities)
            {
                // If a SubScene has AutoLoad DISABLED then it will return false from IsSceneLoaded. So in this case, it's not enough
                // to just check if a SubScene of this GameObject Scene is loaded, we also need to check if the NOT loaded SubScene
                // is actually AutoLoad DISABLED, in which case that is not a reason to indicate this GameObject Scene is not loaded.
                if (world.EntityManager.Exists(subScene.SceneEntity) && !SceneSystem.IsSceneLoaded(world, subScene.SceneEntity))
                {
                    if (world.EntityManager.HasComponent<RequestSceneLoaded>(subScene.SceneEntity))
                    {
                        var request = world.EntityManager.GetComponentData<RequestSceneLoaded>(subScene.SceneEntity);
                        if (request.LoadFlags.HasFlag(SceneLoadFlags.DisableAutoLoad))
                            continue;
                    }
                    return false;
                }
            }

            return true;
        }

        private static Entity CreateGameObjectSceneEntity(WorldUnmanaged world, Hash128 sceneGUID)
        {
            var sceneEntity = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(sceneEntity, new GameObjectReference {SceneGUID = sceneGUID});
            return sceneEntity;
        }

        internal static Entity LoadGameObjectSceneAsync(WorldUnmanaged world, Hash128 sceneGUID, SceneSystem.LoadParameters parameters)
        {
            var sceneEntity = CreateGameObjectSceneEntity(world, sceneGUID);
            LoadGameObjectSceneAsync(world, sceneEntity, parameters);
            return sceneEntity;
        }

        internal static void LoadGameObjectSceneAsync(WorldUnmanaged world, Entity sceneEntity, SceneSystem.LoadParameters parameters)
        {
            var requestSceneLoaded = new RequestSceneLoaded { LoadFlags = parameters.Flags};
            world.EntityManager.AddComponentData(sceneEntity, requestSceneLoaded);

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
            world.EntityManager.AddComponentData(sceneEntity, sceneLoadRequest);
        }

        internal static unsafe void UnloadGameObjectScene(WorldUnmanaged world, Entity sceneEntity, SceneSystem.UnloadParameters unloadParams)
        {
            bool removeRequest = (unloadParams & SceneSystem.UnloadParameters.DontRemoveRequestSceneLoaded) == 0;
            bool destroySceneProxyEntity = (unloadParams & SceneSystem.UnloadParameters.DestroySceneProxyEntity) != 0;
            bool destroySectionProxyEntities = (unloadParams & SceneSystem.UnloadParameters.DestroySectionProxyEntities) != 0;
            bool destroySubSceneProxyEntities = (unloadParams & SceneSystem.UnloadParameters.DestroySubSceneProxyEntities) != 0;

            if (destroySceneProxyEntity && !destroySectionProxyEntities)
                throw new ArgumentException("When unloading a scene it's not possible to destroy the scene entity without also destroying the section entities. Please also add the UnloadParameters.DestroySectionProxyEntities flag");

            var em = world.EntityManager;
            if (removeRequest)
            {
                em.RemoveComponent<RequestGameObjectSceneLoaded>(sceneEntity);
                em.RemoveComponent<RequestSceneLoaded>(sceneEntity);
            }

            if (em.HasComponent<GameObjectSceneSubScene>(sceneEntity))
            {
                var gameObjectSceneSubScenes = em.GetBuffer<GameObjectSceneSubScene>(sceneEntity).ToNativeArray(Allocator.Temp);
                for(int i = gameObjectSceneSubScenes.Length-1; i >= 0; i--)
                {
                    var subScene = gameObjectSceneSubScenes[i];

                    // Prune destroy Scene Entities
                    if (!em.Exists(subScene.SceneEntity))
                    {
                        var buf = em.GetBuffer<GameObjectSceneSubScene>(sceneEntity);
                        buf.RemoveAt(i);
                        continue;
                    }

                    var subSceneUnloadParams = unloadParams;

                    if (destroySubSceneProxyEntities)
                        subSceneUnloadParams |= SceneSystem.UnloadParameters.DestroySceneProxyEntity;

                    SceneSystem.UnloadScene(world, subScene.SceneEntity, subSceneUnloadParams);
                }
                gameObjectSceneSubScenes.Dispose();

                if(destroySubSceneProxyEntities)
                    em.RemoveComponent<GameObjectSceneSubScene>(sceneEntity);
            }

            em.RemoveComponent<GameObjectSceneData>(sceneEntity);

            if(destroySceneProxyEntity)
                em.DestroyEntity(sceneEntity);
        }
    }
}
#endif
