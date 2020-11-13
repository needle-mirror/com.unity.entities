using System;
using System.IO;
using Unity.Assertions;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
#if !UNITY_DOTSRUNTIME
using UnityEngine.SceneManagement;
#endif
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    /// <summary>
    /// High level API for loading and unloading scenes
    /// </summary>
    [ExecuteAlways]
    [UpdateInGroup(typeof(SceneSystemGroup))]
    public class SceneSystem : ComponentSystem
    {
        public const string k_SceneInfoFileName = "catalog.bin";
        static internal string GetSceneInfoPath()
        {
            return $"{EntityScenesPaths.StreamingAssetsPath}/{k_SceneInfoFileName}";
        }

        /// <summary>
        /// Parameters for loading scenes.
        /// </summary>
        public struct LoadParameters
        {
            public bool AutoLoad
            {
                get { return (Flags & SceneLoadFlags.DisableAutoLoad) == 0; }
                set => Flags = value ? Flags & ~SceneLoadFlags.DisableAutoLoad : Flags | SceneLoadFlags.DisableAutoLoad;
            }
            public SceneLoadFlags Flags;
#if !UNITY_DOTSRUNTIME
            public int Priority;
#endif
        }

        private EntityQuery _unloadSceneQuery;
        BlobAssetReference<ResourceCatalogData> catalogData;

#if UNITY_DOTSRUNTIME
        internal void SetCatalogData(BlobAssetReference<ResourceCatalogData> newCatalogData)
        {
            catalogData = newCatalogData;
        }
#endif

        protected override void OnCreate()
        {
            _unloadSceneQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadWrite<SceneSectionStreamingSystem.StreamingState>()},
                None = new[] { ComponentType.ReadOnly<SceneEntityReference>() }
            });
            var sceneInfoPath = GetSceneInfoPath();

#if !UNITY_DOTSRUNTIME
            if (File.Exists(sceneInfoPath))
            {
                if (!BlobAssetReference<ResourceCatalogData>.TryRead(sceneInfoPath, ResourceCatalogData.CurrentFileFormatVersion, out catalogData))
                {
                    Debug.LogError($"Unable to read catalog data from {sceneInfoPath}.");
                    return;
                }

                //if running in LiveLink mode, the initial scenes list is sent from the editor.  otherwise use the flags in the scene data.
                if (!LiveLinkUtility.LiveLinkEnabled)
                {
                    for (int i = 1; i < catalogData.Value.resources.Length; i++)
                    {
                        if (catalogData.Value.resources[i].ResourceType == ResourceMetaData.Type.Scene &&
                            (catalogData.Value.resources[i].ResourceFlags & ResourceMetaData.Flags.AutoLoad) == ResourceMetaData.Flags.AutoLoad)
                            LoadSceneAsync(catalogData.Value.resources[i].ResourceId, new LoadParameters() { Flags = SceneLoadFlags.LoadAsGOScene});
                    }
                }
            }
#endif
        }

        /// <summary>
        /// Get the guid for a scene path or name.  This is a slow method - it is best to use the guid directly.
        /// </summary>
        /// <param name="scenePath">The scene path or name.</param>
        /// <returns>True if the scene guid exists.</returns>
        public Hash128 GetSceneGUID(string scenePath)
        {
            // In future this should come from the active build configuration, or something along those lines
            // How will active build configuration work with Client AND Server in same process?
#if UNITY_EDITOR
            return AssetDatabaseCompatibility.PathToGUID(scenePath);
#else
            Assert.IsTrue(catalogData.IsCreated, "The scene catalog has not been loaded yet");
            return catalogData.Value.GetGUIDFromPath(scenePath);
#endif
        }

        // TODO: Remove this when we have API to access scenes by GUID (Root Scene Conversion)
        // TODO: https://unity3d.atlassian.net/browse/DOTS-3329
        internal string GetScenePath(Hash128 sceneGUID)
        {
#if UNITY_EDITOR
            return AssetDatabaseCompatibility.GuidToPath(sceneGUID);
#else
            Assert.IsTrue(catalogData.IsCreated, "The scene catalog has not been loaded yet");
            return catalogData.Value.GetPathFromGUID(sceneGUID);
#endif
        }

        /// <summary>
        /// Check if a scene or subscene is loaded.
        /// </summary>
        /// <param name="entity">The entity with the loading component data.  This is the entity returned by LoadSceneAsync.</param>
        /// <returns>True if the scene is loaded.</returns>
        public bool IsSceneLoaded(Entity entity)
        {
#if !UNITY_DOTSRUNTIME
            if (EntityManager.HasComponent<GameObjectReference>(entity))
            {
                return GameObjectSceneUtility.IsGameObjectSceneLoaded(this, entity);
            }
#endif

            if (!EntityManager.HasComponent<SceneReference>(entity))
                return false;

            if (!EntityManager.HasComponent<ResolvedSectionEntity>(entity))
                return false;

            var resolvedSectionEntities = EntityManager.GetBuffer<ResolvedSectionEntity>(entity);

            if (resolvedSectionEntities.Length == 0)
                return false;

            foreach (var s in resolvedSectionEntities)
            {
                if (!IsSectionLoaded(s.SectionEntity))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Check if a section of a subscene is loaded.
        /// </summary>
        /// <param name="sectionEntity">The section entity representing the scene section. The section entities can be found in the ResolvedSectionEntity buffer on the scene entity.</param>
        /// <returns>True if the scene section is loaded.</returns>
        public bool IsSectionLoaded(Entity sectionEntity)
        {
            if (!EntityManager.HasComponent<SceneSectionStreamingSystem.StreamingState>(sectionEntity))
                return false;

            return (SceneSectionStreamingSystem.StreamingStatus.Loaded ==
                    EntityManager.GetComponentData<SceneSectionStreamingSystem.StreamingState>(sectionEntity).Status);
        }

        public Hash128 BuildConfigurationGUID { get; set; }

        /// <summary>
        /// Load a scene by its asset GUID.
        /// </summary>
        /// <param name="sceneGUID">The guid of the scene.</param>
        /// <param name="parameters">The load parameters for the scene.</param>
        /// <returns>An entity representing the loading state of the scene.</returns>
        public Entity LoadSceneAsync(Hash128 sceneGUID, LoadParameters parameters = default)
        {
            if (!sceneGUID.IsValid)
            {
                Debug.LogError($"LoadSceneAsync - Invalid sceneGUID.");
                return Entity.Null;
            }

#if !UNITY_DOTSRUNTIME
            if ((parameters.Flags & SceneLoadFlags.LoadAsGOScene) == SceneLoadFlags.LoadAsGOScene)
            {
                return GameObjectSceneUtility.LoadGameObjectSceneAsync(this, sceneGUID, parameters);
            }
#endif
            return LoadEntitySceneAsync(sceneGUID, parameters);
        }

        private Entity LoadEntitySceneAsync(Hash128 sceneGUID, LoadParameters parameters)
        {
            var sceneEntity = Entity.Null;
            if ((parameters.Flags & SceneLoadFlags.NewInstance) == 0)
                Entities.ForEach((Entity entity, ref SceneReference scene) =>
                {
                    if (scene.SceneGUID == sceneGUID)
                        sceneEntity = entity;
                });

            if (sceneEntity == Entity.Null)
                return CreateSceneEntity(sceneGUID, parameters);

            LoadEntitySceneAsync(sceneEntity, parameters);
            return sceneEntity;
        }

        void LoadEntitySceneAsync(Entity sceneEntity, LoadParameters parameters)
        {
            var requestSceneLoaded = new RequestSceneLoaded { LoadFlags = parameters.Flags};
            EntityManager.AddComponentData(sceneEntity, requestSceneLoaded);

            if (parameters.AutoLoad && EntityManager.HasComponent<ResolvedSectionEntity>(sceneEntity))
            {
                foreach (var s in EntityManager.GetBuffer<ResolvedSectionEntity>(sceneEntity))
                    EntityManager.AddComponentData(s.SectionEntity, requestSceneLoaded);
            }
        }

        public void LoadSceneAsync(Entity sceneEntity, LoadParameters parameters = default)
        {
#if !UNITY_DOTSRUNTIME
            if (EntityManager.HasComponent<GameObjectReference>(sceneEntity))
            {
                GameObjectSceneUtility.LoadGameObjectSceneAsync(this, sceneEntity, parameters);
                return;
            }
#endif
            LoadEntitySceneAsync(sceneEntity, parameters);
        }

        Entity CreateSceneEntity(Hash128 sceneGUID, LoadParameters parameters = default)
        {
            var requestSceneLoaded = new RequestSceneLoaded { LoadFlags = parameters.Flags};
            var sceneEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(sceneEntity, new SceneReference {SceneGUID = sceneGUID});
            EntityManager.AddComponentData(sceneEntity, requestSceneLoaded);
            return sceneEntity;
        }

        /// <summary>
        /// Flags controlling the unload process for SubScenes.
        /// </summary>
        [Flags]
        public enum UnloadParameters
        {
            Default = 0,
            DestroySectionProxyEntities = 1 << 1,
            DestroySceneProxyEntity = 1 << 2,
            DontRemoveRequestSceneLoaded = 1 << 3,
            DestroySubSceneProxyEntities = 1 << 4,
        }

        /// <summary>
        /// Unload the scene.
        /// </summary>
        /// <param name="sceneEntity">The entity for the scene.</param>
        /// <param name="unloadParams">Parameters controlling the unload process.  These are ignored for GameObject scenes.</param>
        public void UnloadScene(Entity sceneEntity, UnloadParameters unloadParams = UnloadParameters.Default)
        {
#if !UNITY_DOTSRUNTIME
            if (EntityManager.HasComponent<GameObjectReference>(sceneEntity))
            {
                GameObjectSceneUtility.UnloadGameObjectScene(this, sceneEntity, unloadParams);
                return;
            }
#endif
            bool removeRequest = (unloadParams & UnloadParameters.DontRemoveRequestSceneLoaded) == 0;
            bool destroySceneProxyEntity = (unloadParams & UnloadParameters.DestroySceneProxyEntity) != 0;
            bool destroySectionProxyEntities = (unloadParams & UnloadParameters.DestroySectionProxyEntities) != 0;

            if (destroySceneProxyEntity && !destroySectionProxyEntities)
                throw new ArgumentException("When unloading a scene it's not possible to destroy the scene entity without also destroying the section entities. Please also add the UnloadParameters.DestroySectionProxyEntities flag");

            var streamingSystem = World.GetExistingSystem<SceneSectionStreamingSystem>();

            if (EntityManager.HasComponent<ResolvedSectionEntity>(sceneEntity))
            {
                using (var sections = EntityManager.GetBuffer<ResolvedSectionEntity>(sceneEntity).ToNativeArray(Allocator.Temp))
                {
                    foreach (var section in sections)
                    {
                        //@TODO: Should this really be in SubSceneStreamingSystem?
                        streamingSystem.UnloadSectionImmediate(section.SectionEntity);

                        if (destroySectionProxyEntities)
                            EntityManager.DestroyEntity(section.SectionEntity);
                        else if (removeRequest)
                            EntityManager.RemoveComponent<RequestSceneLoaded>(section.SectionEntity);
                    }
                }
            }

            if (destroySceneProxyEntity)
            {
                EntityManager.RemoveComponent<LinkedEntityGroup>(sceneEntity);
                EntityManager.DestroyEntity(sceneEntity);
            }
            else
            {
                if (destroySectionProxyEntities)
                {
                    EntityManager.RemoveComponent<ResolvedSectionEntity>(sceneEntity);
                    EntityManager.RemoveComponent<LinkedEntityGroup>(sceneEntity);
                    EntityManager.RemoveComponent<ResolvedSceneHash>(sceneEntity);
                }

                if (removeRequest)
                    EntityManager.RemoveComponent<RequestSceneLoaded>(sceneEntity);
            }
        }

        /// <summary>
        /// Unload a SubScene by GUID.  This will only unload the first matching scene.
        /// </summary>
        /// <param name="sceneGUID">The guid of the scene.</param>
        /// <param name="unloadParams">Parameters controlling the unload process.  These are ignored for GameObject scenes.</param>
        public void UnloadScene(Hash128 sceneGUID, UnloadParameters unloadParams = UnloadParameters.Default)
        {
            var sceneEntity = GetSceneEntity(sceneGUID);
            if (sceneEntity != Entity.Null)
                UnloadScene(sceneEntity, unloadParams);
        }

        /// <summary>
        /// Find the scene given a guid.  This will only return the first matching scene.
        /// </summary>
        /// <param name="sceneGUID">The guid of the scene.</param>
        /// <returns>The entity for the scene.</returns>su
        public Entity GetSceneEntity(Hash128 sceneGUID)
        {
            Entity sceneEntity = Entity.Null;
            Entities.ForEach((Entity entity, ref SceneReference scene) =>
            {
                if (scene.SceneGUID == sceneGUID)
                    sceneEntity = entity;
            });

            return sceneEntity;
        }

#if !UNITY_DOTSRUNTIME
        /// <summary>
        /// Find the scene given a guid and no DisableLiveLink component.  This will only return the first matching scene.
        /// </summary>
        /// <param name="sceneGUID">The guid of the scene.</param>
        /// <returns>The entity for the scene.</returns>su
        internal Entity GetLiveLinkedSceneEntity(Hash128 sceneGUID)
        {
            Entity sceneEntity = Entity.Null;
            Entities.WithNone<DisableLiveLink>().ForEach((Entity entity, ref SceneReference scene) =>
            {
                if (scene.SceneGUID == sceneGUID)
                    sceneEntity = entity;
            });

            return sceneEntity;
        }
#endif

        protected override void OnUpdate()
        {
            // Cleanup all Scenes that were destroyed explicitly
            if (!_unloadSceneQuery.IsEmptyIgnoreFilter)
            {
                var streamingSystem = World.GetExistingSystem<SceneSectionStreamingSystem>();
                Entities.With(_unloadSceneQuery).ForEach((Entity sectionEntity) =>
                {
                    streamingSystem.UnloadSectionImmediate(sectionEntity);
                });
            }
        }

        // Used by LiveLink -- enable once supported
#if !UNITY_DOTSRUNTIME
#endif
    }
}
