using System;
using System.IO;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;
#if !UNITY_DOTSRUNTIME
using UnityEngine.SceneManagement;
#endif
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    /// <summary>
    /// Component for public data interface with <see cref="SceneSystem"/>.
    /// </summary>
    public struct SceneSystemData : IComponentData
    {
        /// <summary>
        /// The GUID of the <see cref="Unity.Build.BuildConfiguration"/> applied on the <see cref="World"/>.
        /// </summary>
        public Hash128 BuildConfigurationGUID;
    }

    /// <summary>
    /// High level API for loading and unloading scenes
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(SceneSystemGroup))]
    [BurstCompile]
    public partial struct SceneSystem : ISystem, ISystemStartStop
    {
        /// <summary>
        /// Parameters for loading scenes.
        /// </summary>
        public struct LoadParameters
        {
            /// <summary>
            /// True if the <see cref="SceneLoadFlags.DisableAutoLoad"/> flag is set, otherwise false.
            /// </summary>
            public bool AutoLoad
            {
                get { return (Flags & SceneLoadFlags.DisableAutoLoad) == 0; }
                set => Flags = value ? Flags & ~SceneLoadFlags.DisableAutoLoad : Flags | SceneLoadFlags.DisableAutoLoad;
            }
            /// <summary>
            /// The flags applied when loading the scene.
            /// </summary>
            public SceneLoadFlags Flags;
#if !UNITY_DOTSRUNTIME
            /// <summary>
            /// The priority of the load operation.
            /// </summary>
            public int Priority;
#endif
        }

        static internal RequestSceneLoaded CreateRequestSceneLoaded(LoadParameters loadParameters)
        {
            var requestSceneLoaded = new RequestSceneLoaded { LoadFlags = loadParameters.Flags};

            return requestSceneLoaded;
        }

        private EntityQuery _unloadSceneQuery;
        BlobAssetReference<ResourceCatalogData> catalogData;

#if UNITY_DOTSRUNTIME
        internal void SetCatalogData(BlobAssetReference<ResourceCatalogData> newCatalogData)
        {
            catalogData = newCatalogData;
        }
#endif

        static internal string SceneLoadDir =>
#if UNITY_DOTSRUNTIME
            "Data";
#else
            Application.streamingAssetsPath;
#endif

        /// <summary>
        /// Callback invoked when the system starts running.
        /// </summary>
        /// <param name="state">The entity system state.</param>
        public void OnStartRunning(ref SystemState state)
        {
#if !UNITY_EDITOR
            LoadCatalogData(ref state);
#endif
        }

        /// <summary>
        /// Callback invoked when the system stops running.
        /// </summary>
        /// <param name="state">The entity system state.</param>
        public void OnStopRunning(ref SystemState state)
        {
        
        }


        /// <summary>
        /// Callback invoked when the system is created.
        /// </summary>
        /// <param name="state">The entity system state.</param>
        public void OnCreate(ref SystemState state)
        {
            _unloadSceneQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadWrite<SceneSectionStreamingSystem.StreamingState>()},
                None = new[] { ComponentType.ReadOnly<SceneEntityReference>() }
            });

            state.EntityManager.AddComponentData(state.SystemHandle, new SceneSystemData());
        }

        void LoadCatalogData(ref SystemState state)
        {
#if !UNITY_DOTSRUNTIME
            var fullSceneInfoPath = EntityScenesPaths.FullPathForFile(SceneLoadDir, EntityScenesPaths.RelativePathForSceneInfoFile);
            if (FileUtilityHybrid.FileExists(fullSceneInfoPath))
            {
                if (!BlobAssetReference<ResourceCatalogData>.TryRead(fullSceneInfoPath, ResourceCatalogData.CurrentFileFormatVersion, out catalogData))
                {
                    Debug.LogError($"Unable to read catalog data from {fullSceneInfoPath}.");
                    return;
                }
            }
#endif
        }

        /// <summary>
        /// Callback invoked when the system is destroyed.
        /// </summary>
        /// <param name="state">The entity system state.</param>
        public void OnDestroy(ref SystemState state)
        {
        }

        /// <summary>
        /// Get the guid for a scene path or name.  This is a slow method - it is best to use the guid directly.
        /// </summary>
        /// <param name="state">The entity system state.</param>
        /// <param name="scenePath">The scene path or name.</param>
        /// <returns>True if the scene guid exists.</returns>
        public static unsafe Hash128 GetSceneGUID(ref SystemState state, string scenePath)
        {
            // In future this should come from the active build configuration, or something along those lines
            // How will active build configuration work with Client AND Server in same process?
#if UNITY_EDITOR
            return AssetDatabaseCompatibility.PathToGUID(scenePath);
#else
            ref var cdata = ref ((SceneSystem*)state.m_SystemPtr)->catalogData;
            Assert.IsTrue(cdata.IsCreated, "The scene catalog has not been loaded yet");
            return (cdata.Value.GetGUIDFromPath(scenePath));
#endif
        }

        // TODO: Remove this when we have API to access scenes by GUID (Root Scene Conversion)
        // TODO: DOTS-3329
        internal static unsafe string GetScenePath(ref SystemState state, Hash128 sceneGUID)
        {
#if UNITY_EDITOR
            return AssetDatabaseCompatibility.GuidToPath(sceneGUID);
#else
            ref var cdata = ref ((SceneSystem*)state.m_SystemPtr)->catalogData;

            Assert.IsTrue(cdata.IsCreated, "The scene catalog has not been loaded yet");
            return cdata.Value.GetPathFromGUID(sceneGUID);
#endif
        }

        /// <summary>
        /// Check if a scene or subscene is loaded.
        /// </summary>
        /// <param name="world">The <see cref="World"/> in which the scene is loaded.</param>
        /// <param name="entity">The entity with the loading component data.  This is the entity returned by LoadSceneAsync.</param>
        /// <returns>True if the scene is loaded.</returns>
        public static bool IsSceneLoaded(WorldUnmanaged world, Entity entity)
        {
            if (!world.EntityManager.HasComponent<SceneReference>(entity))
                return false;

            if (!world.EntityManager.HasComponent<ResolvedSectionEntity>(entity))
                return false;

            var resolvedSectionEntities = world.EntityManager.GetBuffer<ResolvedSectionEntity>(entity);

            if (resolvedSectionEntities.Length == 0)
                return false;

            foreach (var s in resolvedSectionEntities)
            {
                if (!IsSectionLoaded(world, s.SectionEntity))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Check if a section of a subscene is loaded.
        /// </summary>
        /// <param name="world">The <see cref="World"/> in which the section is loaded.</param>
        /// <param name="sectionEntity">The section entity representing the scene section. The section entities can be found in the ResolvedSectionEntity buffer on the scene entity.</param>
        /// <returns>True if the scene section is loaded.</returns>
        public static bool IsSectionLoaded(WorldUnmanaged world, Entity sectionEntity)
        {
            if (!world.EntityManager.HasComponent<SceneSectionStreamingSystem.StreamingState>(sectionEntity))
                return false;

            return (SceneSectionStreamingSystem.StreamingStatus.Loaded ==
                    world.EntityManager.GetComponentData<SceneSectionStreamingSystem.StreamingState>(sectionEntity).Status);
        }

        /// <summary>
        /// Load a scene by its weak reference id.
        /// </summary>
        /// <param name="world">The <see cref="World"/> in which the scene is loaded.</param>
        /// <param name="sceneReferenceId">The weak asset reference to the scene.</param>
        /// <param name="parameters">The load parameters for the scene.</param>
        /// <returns>An entity representing the loading state of the scene.</returns>
        public static Entity LoadSceneAsync(WorldUnmanaged world, EntitySceneReference sceneReferenceId, LoadParameters parameters = default)
        {
            return LoadSceneAsync(world, sceneReferenceId.SceneId.GlobalId.AssetGUID, parameters);
        }

        /// <summary>
        /// Load a prefab by its weak reference id.
        /// A PrefabRoot component is added to the returned entity when the load completes.
        /// </summary>
        /// <param name="world">The <see cref="World"/> in which the prefab is loaded.</param>
        /// <param name="prefabReferenceId">The weak asset reference to the prefab.</param>
        /// <param name="parameters">The load parameters for the prefab.</param>
        /// <returns>An entity representing the loading state of the prefab.</returns>
        public static Entity LoadPrefabAsync(WorldUnmanaged world, EntityPrefabReference prefabReferenceId, LoadParameters parameters = default)
        {
            return LoadSceneAsync(world, prefabReferenceId.PrefabId.GlobalId.AssetGUID, parameters);
        }

        /// <summary>
        /// Load a scene or prefab by its asset GUID.
        /// When loading a prefab a PrefabRoot component is added to the scene entity when the load completes.
        /// </summary>
        /// <param name="world">The <see cref="World"/> in which the prefab is loaded.</param>
        /// <param name="sceneGUID">The guid of the scene or prefab.</param>
        /// <param name="parameters">The load parameters for the scene or prefab.</param>
        /// <returns>An entity representing the loading state of the scene or prefab.</returns>
        public static Entity LoadSceneAsync(WorldUnmanaged world, Hash128 sceneGUID, LoadParameters parameters = default)
        {
            if (!sceneGUID.IsValid)
            {
                Debug.LogError($"LoadSceneAsync - Invalid sceneGUID.");
                return Entity.Null;
            }

            return LoadEntitySceneAsync(world, sceneGUID, parameters);
        }

        private static Entity LoadEntitySceneAsync(WorldUnmanaged world, Hash128 sceneGUID, LoadParameters parameters)
        {
            var sceneEntity = Entity.Null;
            if ((parameters.Flags & SceneLoadFlags.NewInstance) == 0)
            {
                var builder = new EntityQueryBuilder(Allocator.Temp).WithAll<SceneReference>();
                using var q = world.EntityManager.CreateEntityQuery(builder);
                foreach (var e in q.ToEntityArray(Allocator.Temp))
                {
                    var r = world.EntityManager.GetComponentData<SceneReference>(e);
                    if (r.SceneGUID == sceneGUID)
                        sceneEntity = e;
                }
            }

            if (sceneEntity == Entity.Null)
                return CreateSceneEntity(world, sceneGUID, parameters);

            LoadEntitySceneAsync(world, sceneEntity, parameters);
            return sceneEntity;
        }

        static void LoadEntitySceneAsync(WorldUnmanaged world, Entity sceneEntity, LoadParameters parameters)
        {
            var requestSceneLoaded = CreateRequestSceneLoaded(parameters);

            world.EntityManager.AddComponentData(sceneEntity, requestSceneLoaded);

            if (parameters.AutoLoad && world.EntityManager.HasComponent<ResolvedSectionEntity>(sceneEntity))
            {
                var resolvedSectionEntities = world.EntityManager.GetBuffer<ResolvedSectionEntity>(sceneEntity).ToNativeArray(Allocator.Temp);
                for (int i = 0; i < resolvedSectionEntities.Length; i++)
                    world.EntityManager.AddComponentData(resolvedSectionEntities[i].SectionEntity, requestSceneLoaded);
            }
        }

        /// <summary>
        /// Loads a scene.
        /// </summary>
        /// <param name="world">The <see cref="World"/> in which the scene is loaded.</param>
        /// <param name="sceneEntity">The entity representing the loading state of the scene.</param>
        /// <param name="parameters">The load parameters for the scene or prefab.</param>
        public static void LoadSceneAsync(WorldUnmanaged world, Entity sceneEntity, LoadParameters parameters = default)
        {
            LoadEntitySceneAsync(world, sceneEntity, parameters);
        }

        static Entity CreateSceneEntity(WorldUnmanaged world, Hash128 sceneGUID, LoadParameters parameters = default)
        {
            var requestSceneLoaded = CreateRequestSceneLoaded(parameters);
            var arr = new NativeArray<ComponentType>(2, Allocator.Temp);
            arr[0] = ComponentType.ReadWrite<SceneReference>();
            arr[1] = ComponentType.ReadWrite<RequestSceneLoaded>();
            var sceneEntity = world.EntityManager.CreateEntity(world.EntityManager.CreateArchetype(arr));
            world.EntityManager.SetComponentData(sceneEntity, new SceneReference {SceneGUID = sceneGUID});
            world.EntityManager.SetComponentData(sceneEntity, requestSceneLoaded);
            return sceneEntity;
        }

        /// <summary>
        /// Flags controlling the unload process for SubScenes.
        /// </summary>
        [Flags]
        public enum UnloadParameters
        {
            /// <summary>
            /// Options for the default unloading behavior.
            /// </summary>
            /// <remarks>Destroys the request scene loaded entity, but preserves the section and scene entities when the unload completes.</remarks>
            Default = 0,
            /// <summary>
            /// Destroys the section proxy entities when unloading the scene.
            /// </summary>
            DestroySectionProxyEntities = 1 << 1,
            /// <summary>
            /// Destroys the scene proxy entity when unloading the scene.
            /// </summary>
            /// <remarks>You can't destroy the scene entity proxy without also destroying the section entity proxies.
            /// Therefore, when you set this flag, you must also set <see cref="DestroySectionProxyEntities"/>.</remarks>
            DestroySceneProxyEntity = 1 << 2,
            /// <summary>
            /// Retains the request components in the entity that represents the scene load state.
            /// </summary>
            DontRemoveRequestSceneLoaded = 1 << 3,
        }

        /// <summary>
        /// Unload the scene.
        /// </summary>
        /// <param name="world">The world from which to unload the scene.</param>
        /// <param name="sceneEntity">The entity for the scene.</param>
        /// <param name="unloadParams">Parameters controlling the unload process.  These are ignored for GameObject scenes.</param>
        public static void UnloadScene(WorldUnmanaged world, Entity sceneEntity, UnloadParameters unloadParams = UnloadParameters.Default)
        {
            bool removeRequest = (unloadParams & UnloadParameters.DontRemoveRequestSceneLoaded) == 0;
            bool destroySceneProxyEntity = (unloadParams & UnloadParameters.DestroySceneProxyEntity) != 0;
            bool destroySectionProxyEntities = (unloadParams & UnloadParameters.DestroySectionProxyEntities) != 0;

            if (destroySceneProxyEntity && !destroySectionProxyEntities)
                throw new ArgumentException("When unloading a scene it's not possible to destroy the scene entity without also destroying the section entities. Please also add the UnloadParameters.DestroySectionProxyEntities flag");

            if (world.EntityManager.HasComponent<ResolvedSectionEntity>(sceneEntity))
            {
                using (var sections = world.EntityManager.GetBuffer<ResolvedSectionEntity>(sceneEntity).ToNativeArray(Allocator.Temp))
                {
                    foreach (var section in sections)
                    {
                        //@TODO: Should this really be in SubSceneStreamingSystem?
                        SceneSectionStreamingSystem.UnloadSectionImmediate(world, section.SectionEntity);

                        if (destroySectionProxyEntities)
                            world.EntityManager.DestroyEntity(section.SectionEntity);
                        else if (removeRequest)
                            world.EntityManager.RemoveComponent<RequestSceneLoaded>(section.SectionEntity);
                    }
                }
            }

            if (destroySceneProxyEntity)
            {
                world.EntityManager.RemoveComponent<LinkedEntityGroup>(sceneEntity);
                world.EntityManager.DestroyEntity(sceneEntity);
            }
            else
            {
                if (destroySectionProxyEntities)
                {
                    world.EntityManager.RemoveComponent<ResolvedSectionEntity>(sceneEntity);
                    world.EntityManager.RemoveComponent<LinkedEntityGroup>(sceneEntity);
                    world.EntityManager.RemoveComponent<ResolvedSceneHash>(sceneEntity);
                }

                if (removeRequest)
                    world.EntityManager.RemoveComponent<RequestSceneLoaded>(sceneEntity);
            }
        }

        /// <summary>
        /// Unload a SubScene by GUID.  This will only unload the first matching scene.
        /// </summary>
        /// <param name="world">The <see cref="World"/> in which the scene is loaded.</param>
        /// <param name="sceneGUID">The guid of the scene.</param>
        /// <param name="unloadParams">Parameters controlling the unload process.  These are ignored for GameObject scenes.</param>
        public static void UnloadScene(
            WorldUnmanaged world,
            Hash128 sceneGUID,
            UnloadParameters unloadParams = UnloadParameters.Default)
        {
            var sceneEntity = GetSceneEntity(world, sceneGUID);
            if (sceneEntity != Entity.Null)
                UnloadScene(world, sceneEntity, unloadParams);
        }

        /// <summary>
        /// Find the scene given a guid.  This will only return the first matching scene.
        /// </summary>
        /// <param name="world">The <see cref="World"/> in which the scene is loaded.</param>
        /// <param name="sceneGUID">The guid of the scene.</param>
        /// <returns>The entity for the scene.</returns>su
        public static Entity GetSceneEntity(WorldUnmanaged world, Hash128 sceneGUID)
        {
            Entity sceneEntity = Entity.Null;

            using var q = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<SceneReference>());
            foreach (var e in q.ToEntityArray(Allocator.Temp))
            {
                var r = world.EntityManager.GetComponentData<SceneReference>(e);
                if (r.SceneGUID == sceneGUID)
                    sceneEntity = e;
            }

            return sceneEntity;
        }

#if !UNITY_DOTSRUNTIME
        /// <summary>
        /// Find the scene given a guid and no DisableLiveConversion component.  This will only return the first matching scene.
        /// </summary>
        /// <param name="state">The entity system state.</param>
        /// <param name="sceneGUID">The guid of the scene.</param>
        /// <returns>The entity for the scene.</returns>
        internal static Entity GetLiveConvertedSceneEntity(ref SystemState state, Hash128 sceneGUID)
        {
            Entity sceneEntity = Entity.Null;

            var query = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<SceneReference>()
                },
                None = new ComponentType[]
                {
                    ComponentType.ReadWrite<DisableLiveConversion>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
            });
            foreach (var e in query.ToEntityArray(Allocator.Temp))
            {
                var r = state.EntityManager.GetComponentData<SceneReference>(e);
                if (r.SceneGUID == sceneGUID)
                    sceneEntity = e;
            }

            return sceneEntity;
        }
#endif

        /// <summary>
        /// Callback invoked when the system is updated.
        /// </summary>
        /// <param name="state">The entity system state.</param>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // Cleanup all Scenes that were destroyed explicitly
            if (!_unloadSceneQuery.IsEmptyIgnoreFilter)
            {
                foreach (var e in _unloadSceneQuery.ToEntityArray(Allocator.Temp))
                {
                    SceneSectionStreamingSystem.UnloadSectionImmediate(state.WorldUnmanaged, e);
                }
            }
        }

    }
}
