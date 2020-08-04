using System;
using Unity.Assertions;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
#if UNITY_DOTSRUNTIME
using Unity.Tiny.IO;
#endif

namespace Unity.Scenes
{
    public static class ResolveSceneSectionUtility
    {
#if UNITY_DOTSRUNTIME
        internal static void RequestLoadAndPollSceneMetaData(EntityManager EntityManager, Entity sceneEntity, Hash128 sceneGUID)
        {
            var sceneHeaderPath = EntityScenesPaths.GetLoadPath(sceneGUID, EntityScenesPaths.PathType.EntitiesHeader, -1);

            RequestSceneHeader requestSceneHeader;
            if (!EntityManager.HasComponent<RequestSceneHeader>(sceneEntity))
            {
                requestSceneHeader.IOHandle = IOService.RequestAsyncRead(sceneHeaderPath).m_Handle;
                EntityManager.AddComponentData(sceneEntity, requestSceneHeader);
            }
            else
                requestSceneHeader = EntityManager.GetComponentData< RequestSceneHeader>(sceneEntity);

            var asyncOp = new AsyncOp() { m_Handle = requestSceneHeader.IOHandle };
            var sceneHeaderStatus = asyncOp.GetStatus();
            if (sceneHeaderStatus <= AsyncOp.Status.InProgress)
                return;

            if (sceneHeaderStatus != AsyncOp.Status.Success)
            {
                Debug.LogError($"Loading Entity Scene failed because the entity header file could not be read: guid={sceneGUID}.");
            }

            // Even if the file doesn't exist we want to stop continously trying to load the scene metadata
            EntityManager.AddComponentData(sceneEntity, new SceneMetaDataLoaded() { Success = sceneHeaderStatus == AsyncOp.Status.Success });
        }
#endif
        public unsafe static bool ResolveSceneSections(EntityManager EntityManager, Entity sceneEntity, Hash128 sceneGUID, RequestSceneLoaded requestSceneLoaded, Hash128 artifactHash)
        {
            // Resolve first (Even if the file doesn't exist we want to stop continously trying to load the section)
            EntityManager.AddBuffer<ResolvedSectionEntity>(sceneEntity);
            var sceneHeaderPath = "";
#if UNITY_EDITOR
            string[] paths = null;
#endif

            bool useStreamingAssetPath = true;
#if !UNITY_DOTSRUNTIME
            useStreamingAssetPath = SceneBundleHandle.UseAssetBundles;
#endif
            if (useStreamingAssetPath)
            {
                sceneHeaderPath = EntityScenesPaths.GetLoadPath(sceneGUID, EntityScenesPaths.PathType.EntitiesHeader, -1);
            }
            else
            {
#if UNITY_EDITOR
                AssetDatabaseCompatibility.GetArtifactPaths(artifactHash, out paths);
                sceneHeaderPath = EntityScenesPaths.GetLoadPathFromArtifactPaths(paths, EntityScenesPaths.PathType.EntitiesHeader);
#endif
            }

            EntityManager.AddComponentData(sceneEntity, new ResolvedSceneHash { ArtifactHash = artifactHash });
            EntityManager.AddBuffer<LinkedEntityGroup>(sceneEntity).Add(sceneEntity);

            // @TODO: AsyncReadManager currently crashes with empty path.
            //        It should be possible to remove this after that is fixed.
            if (String.IsNullOrEmpty(sceneHeaderPath))
            {
#if UNITY_EDITOR
                var scenePath = AssetDatabaseCompatibility.GuidToPath(sceneGUID);
                var logPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Logs"));
                Debug.LogError($"Loading Entity Scene failed because the entity header file couldn't be resolved. This might be caused by a failed import of the entity scene. Please take a look at the SubScene MonoBehaviour that references this scene or at the asset import worker log in {logPath}. scenePath={scenePath} guid={sceneGUID}");
#else
                Debug.LogError($"Loading Entity Scene failed because the entity header file couldn't be resolved: guid={sceneGUID}.");
#endif
                return false;
            }

#if !UNITY_DOTSRUNTIME
            if (!BlobAssetReference<SceneMetaData>.TryRead(sceneHeaderPath, SceneMetaDataSerializeUtility.CurrentFileFormatVersion, out var sceneMetaDataRef))
            {
#if UNITY_EDITOR
                Debug.LogError($"Loading Entity Scene failed because the entity header file was an old version or doesn't exist: guid={sceneGUID} path={sceneHeaderPath}");
#else
                Debug.LogError($"Loading Entity Scene failed because the entity header file was an old version or doesn't exist: {sceneGUID}\nNOTE: In order to load SubScenes in the player you have to use the new BuildConfiguration asset based workflow to build & run your player.\n{sceneHeaderPath}");
#endif
                return false;
            }
#else
            Assert.IsTrue(EntityManager.HasComponent<RequestSceneHeader>(sceneEntity), "You may only resolve a scene if the entity has a RequestSceneHeader component");
            Assert.IsTrue(EntityManager.HasComponent<SceneMetaDataLoaded>(sceneEntity), "You may only resolve a scene if the entity has a SceneMetaDataLoaded component");
            var sceneMetaDataLoaded = EntityManager.GetComponentData<SceneMetaDataLoaded>(sceneEntity);
            if (!sceneMetaDataLoaded.Success)
                return false;

            var requestSceneHeader = EntityManager.GetComponentData<RequestSceneHeader>(sceneEntity);
            var sceneMetaDataRef = default(BlobAssetReference<SceneMetaData>);
            var asyncOp = new AsyncOp() { m_Handle = requestSceneHeader.IOHandle };
            using (asyncOp)
            {
                unsafe
                {
                    asyncOp.GetData(out var sceneData, out var sceneDataSize);

                    if (!BlobAssetReference<SceneMetaData>.TryRead(sceneData, SceneMetaDataSerializeUtility.CurrentFileFormatVersion, out sceneMetaDataRef))
                    {
                        Debug.LogError($"Loading Entity Scene failed because the entity header file was an old version or doesn't exist: {sceneGUID}\nNOTE: In order to load SubScenes in the player you have to use the new BuildConfiguration asset based workflow to build & run your player.\n{sceneHeaderPath}");
                        return false;
                    }
                }
            }
#endif

            ref var sceneMetaData = ref sceneMetaDataRef.Value;

#if UNITY_EDITOR
            var sceneName = sceneMetaData.SceneName.ToString();
            EntityManager.SetName(sceneEntity, $"Scene: {sceneName}");
#endif

            // If auto-load is enabled
            var loadSections = (requestSceneLoaded.LoadFlags & SceneLoadFlags.DisableAutoLoad) == 0;

            for (int i = 0; i != sceneMetaData.Sections.Length; i++)
            {
                var sectionEntity = EntityManager.CreateEntity();
                var sectionIndex = sceneMetaData.Sections[i].SubSectionIndex;
#if UNITY_EDITOR
                EntityManager.SetName(sectionEntity, $"SceneSection: {sceneName} ({sectionIndex})");
#endif

                if (loadSections)
                {
                    EntityManager.AddComponentData(sectionEntity, requestSceneLoaded);
                }

                EntityManager.AddComponentData(sectionEntity, sceneMetaData.Sections[i]);
                EntityManager.AddComponentData(sectionEntity, new SceneBoundingVolume { Value = sceneMetaData.Sections[i].BoundingVolume });
                EntityManager.AddComponentData(sectionEntity, new SceneEntityReference { SceneEntity = sceneEntity });

                var hybridPath = "";
                var scenePath = "";
                var sectionPath = new ResolvedSectionPath();

                if (useStreamingAssetPath)
                {
                    hybridPath = EntityScenesPaths.GetLoadPath(sceneGUID, EntityScenesPaths.PathType.EntitiesUnityObjectReferencesBundle, sectionIndex);
                    scenePath = EntityScenesPaths.GetLoadPath(sceneGUID, EntityScenesPaths.PathType.EntitiesBinary, sectionIndex);
                }
                else
                {
#if UNITY_EDITOR
                    scenePath = EntityScenesPaths.GetLoadPathFromArtifactPaths(paths, EntityScenesPaths.PathType.EntitiesBinary, sectionIndex);
                    hybridPath = EntityScenesPaths.GetLoadPathFromArtifactPaths(paths, EntityScenesPaths.PathType.EntitiesUnityObjectReferences, sectionIndex);
#endif
                }

                sectionPath.ScenePath.SetString(scenePath);
                if (hybridPath != null)
                    sectionPath.HybridPath.SetString(hybridPath);

                EntityManager.AddComponentData(sectionEntity, sectionPath);

#if UNITY_EDITOR
                if (EntityManager.HasComponent<SubScene>(sceneEntity))
                    EntityManager.AddComponentObject(sectionEntity, EntityManager.GetComponentObject<SubScene>(sceneEntity));
#endif

                AddSectionMetadataComponents(sectionEntity, ref sceneMetaData.SceneSectionCustomMetadata[i], EntityManager);

                var buffer = EntityManager.GetBuffer<ResolvedSectionEntity>(sceneEntity);
                buffer.Add(new ResolvedSectionEntity { SectionEntity = sectionEntity });
                if (sceneMetaData.Dependencies.Length > 0)
                {
                    ref var deps = ref sceneMetaData.Dependencies[i];
                    if (deps.Length > 0)
                    {
                        var bundleSet = EntityManager.AddBuffer<BundleElementData>(sectionEntity);
                        bundleSet.ResizeUninitialized(deps.Length);
                        UnsafeUtility.MemCpy(bundleSet.GetUnsafePtr(), deps.GetUnsafePtr(), sizeof(Hash128) * deps.Length);
                    }
                }
                var linkedEntityGroup = EntityManager.GetBuffer<LinkedEntityGroup>(sceneEntity);
                linkedEntityGroup.Add(new LinkedEntityGroup { Value = sectionEntity });
            }
            sceneMetaDataRef.Dispose();

            return true;
        }

        internal static unsafe void AddSectionMetadataComponents(Entity sectionEntity, ref BlobArray<SceneSectionCustomMetadata> sectionMetaDataArray, EntityManager entityManager)
        {
            // Deserialize the SceneSection custom metadata
            for (var i = 0; i < sectionMetaDataArray.Length; i++)
            {
                ref var metadata = ref sectionMetaDataArray[i];
                var customTypeIndex = TypeManager.GetTypeIndexFromStableTypeHash(metadata.StableTypeHash);

                // Couldn't find the type...
                if (customTypeIndex == -1)
                {
                    UnityEngine.Debug.LogError(
                        $"Couldn't import SceneSection metadata, couldn't find the type to deserialize with stable hash {metadata.StableTypeHash}");
                    continue;
                }

                entityManager.AddComponent(sectionEntity, ComponentType.FromTypeIndex(customTypeIndex));

                if (TypeManager.IsZeroSized(customTypeIndex))
                    continue;

                void* componentPtr = entityManager.GetComponentDataRawRW(sectionEntity, customTypeIndex);
                UnsafeUtility.MemCpy(componentPtr, metadata.Data.GetUnsafePtr(), metadata.Data.Length);
            }
        }
    }
}
