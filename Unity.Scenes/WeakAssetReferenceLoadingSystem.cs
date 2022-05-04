using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;

namespace Unity.Scenes
{
    public struct PrefabLoadResult : IComponentData
    {
        public Entity PrefabRoot;
    }

    internal struct PrefabAssetReference : ISystemStateComponentData
    {
        internal EntityPrefabReference      _ReferenceId;
    }

    public struct RequestEntityPrefabLoaded : IComponentData
    {
        public EntityPrefabReference Prefab;
    }

    internal struct WeakAssetPrefabLoadRequest : IComponentData
    {
        internal EntityPrefabReference WeakReferenceId;
    }

    [ExecuteAlways]
    [UpdateInGroup(typeof(SceneSystemGroup))]
    [UpdateBefore(typeof(ResolveSceneReferenceSystem))]
    public partial class WeakAssetReferenceLoadingSystem : SystemBase
    {
        internal struct LoadedPrefab
        {
            public int RefCount;
            public Entity SceneEntity;
            public Entity PrefabRoot;
        }

        internal NativeParallelMultiHashMap<EntityPrefabReference, Entity> InProgressLoads;
        internal NativeParallelHashMap<EntityPrefabReference, LoadedPrefab> LoadedPrefabs;

        protected override void OnCreate()
        {
            InProgressLoads = new NativeParallelMultiHashMap<EntityPrefabReference, Entity>(1, Allocator.Persistent);
            LoadedPrefabs = new NativeParallelHashMap<EntityPrefabReference, LoadedPrefab>(1, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            InProgressLoads.Dispose();
            LoadedPrefabs.Dispose();
        }

        protected override void OnUpdate()
        {
            Entities.WithoutBurst().WithStructuralChanges()
                .WithNone<PrefabAssetReference>()
                .ForEach((Entity e, ref RequestEntityPrefabLoaded loadRequest) =>
                {
                    StartLoadRequest(e, loadRequest.Prefab);
                }).Run();

            Entities.WithoutBurst().WithStructuralChanges()
                .WithNone<RequestEntityPrefabLoaded>()
                .ForEach((Entity e, ref PrefabAssetReference prefabReference) =>
                {
                    if (LoadedPrefabs.TryGetValue(prefabReference._ReferenceId, out var loadedPrefab))
                    {
                        LoadedPrefabs.Remove(prefabReference._ReferenceId);
                        if (loadedPrefab.RefCount > 1)
                        {
                            loadedPrefab.RefCount--;
                            LoadedPrefabs.Add(prefabReference._ReferenceId, loadedPrefab);
                        }
                        else
                        {
                            World.GetExistingSystem<SceneSystem>().UnloadScene(loadedPrefab.SceneEntity,
                                SceneSystem.UnloadParameters.DestroySceneProxyEntity | SceneSystem.UnloadParameters.DestroySectionProxyEntities);
                        }
                    }
                    else
                    {
                        InProgressLoads.Remove(prefabReference._ReferenceId, e);
                    }
                    EntityManager.RemoveComponent<PrefabAssetReference>(e);
                }).Run();
        }

        void StartLoadRequest(Entity entity, EntityPrefabReference loadRequestWeakReferenceId)
        {
            if (LoadedPrefabs.TryGetValue(loadRequestWeakReferenceId, out var loadedPrefab))
            {
                EntityManager.AddComponentData(entity, new PrefabAssetReference { _ReferenceId = loadRequestWeakReferenceId});
                EntityManager.AddComponentData(entity, new PrefabLoadResult { PrefabRoot = loadedPrefab.PrefabRoot});
                loadedPrefab.RefCount++;
                LoadedPrefabs.Remove(loadRequestWeakReferenceId);
                LoadedPrefabs.Add(loadRequestWeakReferenceId, loadedPrefab);
                return;
            }

            EntityManager.AddComponentData(entity, new PrefabAssetReference { _ReferenceId = loadRequestWeakReferenceId });

            if (!InProgressLoads.ContainsKey(loadRequestWeakReferenceId))
            {
                var loadParameters = new SceneSystem.LoadParameters { Flags = SceneLoadFlags.NewInstance };
                var sceneEntity = World.GetExistingSystem<SceneSystem>().LoadPrefabAsync(loadRequestWeakReferenceId, loadParameters);
                EntityManager.AddComponentData(sceneEntity, new WeakAssetPrefabLoadRequest { WeakReferenceId = loadRequestWeakReferenceId });
            }

            InProgressLoads.Add(loadRequestWeakReferenceId, entity);
        }

        public void CompleteLoad(Entity sceneEntity, Entity prefabRoot, EntityPrefabReference weakReferenceId)
        {

            if (!InProgressLoads.TryGetFirstValue(weakReferenceId, out var entity, out var it))
            {
#if UNITY_EDITOR
                if (LoadedPrefabs.TryGetValue(weakReferenceId, out var loadedPrefab))
                {
                    // Prefab was reloaded, patch all references to point to the new prefab root
                    loadedPrefab.PrefabRoot = prefabRoot;
                    loadedPrefab.SceneEntity = sceneEntity;
                    LoadedPrefabs[weakReferenceId] = loadedPrefab;
                    Entities.ForEach((ref PrefabLoadResult loadedPrefab, in RequestEntityPrefabLoaded prefabRequest) =>
                    {
                        if (prefabRequest.Prefab == weakReferenceId)
                            loadedPrefab.PrefabRoot = prefabRoot;
                    }).Run();
                    return;
                }
#endif
                //No one was waiting for this load, unload it immediately
                World.GetExistingSystem<SceneSystem>().UnloadScene(sceneEntity,
                    SceneSystem.UnloadParameters.DestroySceneProxyEntity | SceneSystem.UnloadParameters.DestroySectionProxyEntities);
                return;
            }

            EntityManager.AddComponentData(prefabRoot, new RequestEntityPrefabLoaded() {Prefab =  weakReferenceId});
            int count = 0;
            do
            {
                EntityManager.AddComponentData(entity, new PrefabAssetReference { _ReferenceId = weakReferenceId});
                EntityManager.AddComponentData(entity, new PrefabLoadResult { PrefabRoot =  prefabRoot});
                count++;
            } while (InProgressLoads.TryGetNextValue(out entity, ref it));
            InProgressLoads.Remove(weakReferenceId);
            LoadedPrefabs.Add(weakReferenceId, new LoadedPrefab{SceneEntity = sceneEntity, PrefabRoot = prefabRoot, RefCount = count});
        }
    }
}
