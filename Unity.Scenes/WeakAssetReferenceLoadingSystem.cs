using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;

namespace Unity.Scenes
{
    /// <summary>
    /// Component to reference the root entity of a converted prefab.
    /// </summary>
    public struct PrefabLoadResult : IComponentData
    {
        /// <summary>
        /// The root entity of the converted prefab.
        /// </summary>
        public Entity PrefabRoot;
    }

    internal struct PrefabAssetReference : ICleanupComponentData
    {
        internal EntityPrefabReference      _ReferenceId;
    }

    /// <summary>
    /// Component to signal the request to convert the referenced prefab.
    /// </summary>
    public struct RequestEntityPrefabLoaded : IComponentData
    {
        /// <summary>
        /// The reference of the prefab to be converted.
        /// </summary>
        public EntityPrefabReference Prefab;
    }

    internal struct WeakAssetPrefabLoadRequest : IComponentData
    {
        internal EntityPrefabReference WeakReferenceId;
    }
    internal readonly partial struct RequestPrefabLoadedAspect : IAspect
    {
        public readonly Entity Self;
        public readonly RefRO <RequestEntityPrefabLoaded> RequestEntityPrefabLoaded;
    }

    internal readonly partial struct PrefabAssetReferenceAspect : IAspect
    {
        public readonly Entity Self;
        internal readonly RefRO<PrefabAssetReference> PrefabAssetReference;
    }

    /// <summary>
    /// Aspect for accessing the prefab load result for a given prefab load request.
    /// </summary>
    public readonly partial struct PrefabRequestAndResultAspect : IAspect
    {
        /// <summary>
        /// The reference to the <see cref="PrefabLoadResult"/> component.
        /// </summary>
        public readonly RefRW<PrefabLoadResult> LoadedPrefab;
        internal readonly RefRO<RequestEntityPrefabLoaded> PrefabRequest;
    }

    struct WeakAssetReferenceLoadingData : IComponentData
    {
        public struct LoadedPrefab
        {
            public int RefCount;
            public Entity SceneEntity;
            public Entity PrefabRoot;
        }

        public NativeMultiHashMap<EntityPrefabReference, Entity> InProgressLoads;
        public NativeParallelHashMap<EntityPrefabReference, LoadedPrefab> LoadedPrefabs;
    }

    /// <summary>
    /// System for loading assets into a runtime world.
    /// </summary>
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(SceneSystemGroup))]
    [UpdateBefore(typeof(ResolveSceneReferenceSystem))]
    [BurstCompile]
    public partial struct WeakAssetReferenceLoadingSystem : ISystem
    {
        /// <summary>
        /// Callback invoked when the system is created.
        /// </summary>
        /// <param name="state">The entity system state.</param>
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.EntityManager.AddComponentData(state.SystemHandle, new WeakAssetReferenceLoadingData
            {
                InProgressLoads = new NativeMultiHashMap<EntityPrefabReference, Entity>(1, Allocator.Persistent),
                LoadedPrefabs = new NativeParallelHashMap<EntityPrefabReference, WeakAssetReferenceLoadingData.LoadedPrefab>(1, Allocator.Persistent),
            });
        }

        /// <summary>
        /// Callback invoked when the system is destroyed.
        /// </summary>
        /// <param name="state">The entity system state.</param>
        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            ref var data = ref state.EntityManager.GetComponentDataRW<WeakAssetReferenceLoadingData>(state.SystemHandle).ValueRW;
            data.InProgressLoads.Dispose();
            data.LoadedPrefabs.Dispose();
        }

        /// <summary>
        /// Callback invoked when the system is updated.
        /// </summary>
        /// <param name="state">The entity system state.</param>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach(var req in SystemAPI.Query<RequestPrefabLoadedAspect>().WithNone<PrefabAssetReference>())
            {
                StartLoadRequest(ref state, req.Self, req.RequestEntityPrefabLoaded.ValueRO.Prefab, ref ecb);
            }
            ecb.Playback(state.EntityManager);

            var sceneEntitiesToUnload = new NativeList<Entity>(Allocator.Temp);
            var prefabIdsToRemove = new NativeList<EntityPrefabReference>(Allocator.Temp);
            var inProgressLoadsToRemove = new NativeParallelHashMap<EntityPrefabReference, Entity>(16, Allocator.Temp);
            var PARSToRemove = new NativeList<Entity>(Allocator.Temp);
            ref var loadingData = ref state.EntityManager.GetComponentDataRW<WeakAssetReferenceLoadingData>(state.SystemHandle).ValueRW;
            foreach (var req in SystemAPI.Query<PrefabAssetReferenceAspect>().WithNone<RequestEntityPrefabLoaded>())
            {
                var prefabReference = req.PrefabAssetReference.ValueRO;
                var e = req.Self;
                {
                    if (loadingData.LoadedPrefabs.TryGetValue(prefabReference._ReferenceId, out var loadedPrefab))
                    {
                        if (loadedPrefab.RefCount > 1)
                        {
                            loadedPrefab.RefCount--;
                            loadingData.LoadedPrefabs[prefabReference._ReferenceId] = loadedPrefab;
                        }
                        else
                        {
                            prefabIdsToRemove.Add(prefabReference._ReferenceId);

                            sceneEntitiesToUnload.Add(loadedPrefab.SceneEntity);
                        }
                    }
                    else
                    {
                        inProgressLoadsToRemove.Add(prefabReference._ReferenceId, e);
                    }
                    PARSToRemove.Add(e);
                }
            }

            for (int i = 0; i < sceneEntitiesToUnload.Length; i++)
            {
                SceneSystem.UnloadScene(state.WorldUnmanaged,
                    sceneEntitiesToUnload[i],
                    SceneSystem.UnloadParameters.DestroySceneProxyEntity |
                    SceneSystem.UnloadParameters.DestroySectionProxyEntities);
            }

            for (int i = 0; i < prefabIdsToRemove.Length; i++)
            {
                loadingData.LoadedPrefabs.Remove(prefabIdsToRemove[i]);
            }

            foreach (var k in inProgressLoadsToRemove.GetKeyArray(Allocator.Temp))
            {
                loadingData.InProgressLoads.Remove(k, inProgressLoadsToRemove[k]);
            }

            for (int i = 0; i < PARSToRemove.Length; i++)
            {
                state.EntityManager.RemoveComponent<PrefabAssetReference>(PARSToRemove[i]);
            }
        }

        void StartLoadRequest(ref SystemState state, Entity entity, EntityPrefabReference loadRequestWeakReferenceId, ref EntityCommandBuffer ecb)
        {
            ref var loadingData = ref state.EntityManager.GetComponentDataRW<WeakAssetReferenceLoadingData>(state.SystemHandle).ValueRW;
            if (loadingData.LoadedPrefabs.TryGetValue(loadRequestWeakReferenceId, out var loadedPrefab))
            {
                ecb.AddComponent(entity, new PrefabAssetReference { _ReferenceId = loadRequestWeakReferenceId});
                ecb.AddComponent(entity, new PrefabLoadResult { PrefabRoot = loadedPrefab.PrefabRoot});
                loadedPrefab.RefCount++;
                loadingData.LoadedPrefabs.Remove(loadRequestWeakReferenceId);
                loadingData.LoadedPrefabs.Add(loadRequestWeakReferenceId, loadedPrefab);
                return;
            }

            ecb.AddComponent(entity, new PrefabAssetReference { _ReferenceId = loadRequestWeakReferenceId });

            if (!loadingData.InProgressLoads.ContainsKey(loadRequestWeakReferenceId))
            {
                var loadParameters = new SceneSystem.LoadParameters { Flags = SceneLoadFlags.NewInstance };
                var sceneEntity = SceneSystem.LoadPrefabAsync(state.WorldUnmanaged, loadRequestWeakReferenceId, loadParameters);
                ecb.AddComponent(sceneEntity, new WeakAssetPrefabLoadRequest { WeakReferenceId = loadRequestWeakReferenceId });
            }

            loadingData.InProgressLoads.Add(loadRequestWeakReferenceId, entity);
        }

        /// <summary>
        /// Marks a prefab as loaded and cleans up the in progress state.
        /// </summary>
        /// <param name="state">The entity system state.</param>
        /// <param name="sceneEntity">The entity representing the loading state of the scene.</param>
        /// <param name="prefabRoot">The root entity of a converted prefab.</param>
        /// <param name="weakReferenceId">The prefab reference used to initiate the load.</param>
        public static void CompleteLoad(ref SystemState state, Entity sceneEntity, Entity prefabRoot, EntityPrefabReference weakReferenceId)
        {
            ref var loadingData = ref state.EntityManager.GetComponentDataRW<WeakAssetReferenceLoadingData>(state.WorldUnmanaged.GetExistingUnmanagedSystem<WeakAssetReferenceLoadingSystem>()).ValueRW;
            if (!loadingData.InProgressLoads.TryGetFirstValue(weakReferenceId, out var entity, out var it))
            {
#if UNITY_EDITOR
                if (loadingData.LoadedPrefabs.TryGetValue(weakReferenceId, out var loadedPrefab))
                {
                    // Prefab was reloaded, patch all references to point to the new prefab root
                    loadedPrefab.PrefabRoot = prefabRoot;
                    loadedPrefab.SceneEntity = sceneEntity;
                    loadingData.LoadedPrefabs[weakReferenceId] = loadedPrefab;
                    var query = state.GetEntityQuery(ComponentType.ReadOnly<RequestEntityPrefabLoaded>());
                    foreach (var req in query.ToComponentDataArray<RequestEntityPrefabLoaded>(Allocator.Temp))
                    {
                        if (req.Prefab == weakReferenceId)
                            loadedPrefab.PrefabRoot = prefabRoot;
                    }
                    return;
                }
#endif
                //No one was waiting for this load, unload it immediately
                SceneSystem.UnloadScene(state.WorldUnmanaged,
                    sceneEntity,
                    SceneSystem.UnloadParameters.DestroySceneProxyEntity |
                    SceneSystem.UnloadParameters.DestroySectionProxyEntities);
                    return;
            }

            state.EntityManager.AddComponentData(prefabRoot, new RequestEntityPrefabLoaded() {Prefab =  weakReferenceId});
            int count = 0;
            do
            {
                state.EntityManager.AddComponentData(entity, new PrefabAssetReference { _ReferenceId = weakReferenceId});
                state.EntityManager.AddComponentData(entity, new PrefabLoadResult { PrefabRoot =  prefabRoot});
                count++;
            } while (loadingData.InProgressLoads.TryGetNextValue(out entity, ref it));
            loadingData.InProgressLoads.Remove(weakReferenceId);
            loadingData.LoadedPrefabs.Add(weakReferenceId, new WeakAssetReferenceLoadingData.LoadedPrefab { SceneEntity = sceneEntity, PrefabRoot = prefabRoot, RefCount = count});
        }
    }
}
