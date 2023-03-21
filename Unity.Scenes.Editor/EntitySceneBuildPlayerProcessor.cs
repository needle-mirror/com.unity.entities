using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using Hash128 = Unity.Entities.Hash128;
using System.Collections.Generic;
using UnityEditor.Experimental;
using System.IO;
using Unity.Entities.Build;
using UnityEditor.Build.Content;
using UnityEngine;
#if USING_PLATFORMS_PACKAGE
using Unity.Build.Classic.Private;
#endif
using UnityEditor.SceneManagement;

namespace Unity.Scenes.Editor
{
    internal sealed class EntitySectionBundlesInBuild
    {
        internal List<Hash128> SceneGUIDs = new List<Hash128>();
        internal List<ArtifactKey> ArtifactKeys = new List<ArtifactKey>();

        public void Add(Hash128 sceneGUID, ArtifactKey artifactKey)
        {
            SceneGUIDs.Add(sceneGUID);
            ArtifactKeys.Add(artifactKey);
        }

        public void Add(IEnumerable<Hash128> sceneGUIDs, IEnumerable<ArtifactKey> artifactKeys)
        {
            SceneGUIDs.AddRange(sceneGUIDs);
            ArtifactKeys.AddRange(artifactKeys);
        }
    }

    internal struct RootSceneInfo
    {
        public string Path;
        public Hash128 Guid;
    }

    internal interface IEntitySceneBuildCustomizer
    {
        public void RegisterAdditionalFilesToDeploy(Hash128[] rootSceneGUIDs, Hash128[] entitySceneGUIDs, EntitySectionBundlesInBuild additionalImports, Action<string, string> addAdditionalFile);
    }

    internal class EntitySceneBuildPlayerProcessor : BuildPlayerProcessor
    {
        private string m_BuildWorkingDir = $"../Library/BuildWorkingDir/{PlayerSettings.productName}";
        private BuildPlayerContext m_BuildPlayerContext;

        void RegisterAdditionalFileToDeploy(string from, string to)
        {
            var realPath = Path.GetFullPath(from);
            m_BuildPlayerContext.AddAdditionalPathToStreamingAssets(realPath, to);
        }

        [InitializeOnLoadMethod]
        public static void Init()
        {
            BuildPlayerWindow.RegisterGetBuildPlayerOptionsHandler(HandleGetBuild);
        }

        static BuildPlayerOptions HandleGetBuild(BuildPlayerOptions opts)
        {
            opts = BuildPlayerWindow.DefaultBuildMethods.GetBuildPlayerOptions(opts);
            var instance = DotsGlobalSettings.Instance;

            if (instance.GetPlayerType() == DotsGlobalSettings.PlayerType.Server)
            {
                //If a server provider is not present use at least the default setting for the client build.
                var provider = instance.ServerProvider;
                if (provider == null)
                {
                    LogMissingServerProvider();
                    provider = instance.ClientProvider;
                }
                opts.extraScriptingDefines = provider.GetExtraScriptingDefines();
                // Adding EnableHeadlessMode as an option will switch the platform to dedicated server that defines UNITY_SERVER in the Editor as well.
                // We may want to switch back to the original platform at the end of the build to prevent it if we don't support switching to dedicated server. Currently the Editor fails to compile after switching to the dedicated server subtarget.
                opts.options |= provider.GetExtraBuildOptions();
            }
            else
            {
                opts.extraScriptingDefines = instance.ClientProvider.GetExtraScriptingDefines();
                opts.options |= instance.ClientProvider.GetExtraBuildOptions();
            }
            return opts;
        }

        static void LogMissingServerProvider()
        {
            UnityEngine.Debug.LogWarning(
                $"No available DotsPlayerSettingsProvider for the current platform ({EditorUserBuildSettings.activeBuildTarget}). Using the client settings instead.");
        }

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
#if USING_PLATFORMS_PACKAGE
            if (BuildPlayerStep.BuildFromBuildConfiguration)
                return;
#endif

            if(Directory.Exists(m_BuildWorkingDir))
                Directory.Delete(m_BuildWorkingDir, true);

            m_BuildPlayerContext = buildPlayerContext;

            // Retrieve list of subscenes to import from the root scenes added to the player settings
            var rootSceneInfos = new List<RootSceneInfo>();
            var rootSceneGUIDs = new List<Hash128>();
            var subSceneGuids = new HashSet<Hash128>();

            // If Scenes is empty, the default behaviour is to build the current scene, so we cannot use the GetSubScenes call as it is an importer
            if (buildPlayerContext.BuildPlayerOptions.scenes == null ||
                buildPlayerContext.BuildPlayerOptions.scenes.Length == 0)
            {
                var subScenes = SubScene.AllSubScenes;
                subSceneGuids = new HashSet<Hash128>(subScenes.Where(x => x.SceneGUID.IsValid).Select(x => x.SceneGUID).Distinct());

                var rootScenePath = EditorSceneManager.GetActiveScene().path;

                if(string.IsNullOrEmpty(rootScenePath))
                    throw new SystemException("Entities currently cannot build a Scene that has not yet been saved for the first time.");

                var rootSceneGUID = AssetDatabase.GUIDFromAssetPath(rootScenePath);
                rootSceneInfos.Add(new RootSceneInfo()
                {
                    Path = rootScenePath,
                    Guid = rootSceneGUID
                });
            }
            else
            {
                for (int i = 0; i < buildPlayerContext.BuildPlayerOptions.scenes.Length; i++)
                {
                    var rootScenePath = buildPlayerContext.BuildPlayerOptions.scenes[i];
                    var rootSceneGUID = AssetDatabase.GUIDFromAssetPath(rootScenePath);

                    rootSceneGUIDs.Add(rootSceneGUID);
                    rootSceneInfos.Add(new RootSceneInfo()
                    {
                        Path = rootScenePath,
                        Guid = rootSceneGUID
                    });
                }
                subSceneGuids = new HashSet<Hash128>(rootSceneInfos.SelectMany(rootScene => EditorEntityScenes.GetSubScenes(rootScene.Guid)));
            }

            // Import subscenes and deploy entity scene files and bundles
            var artifactKeys = new Dictionary<Hash128, ArtifactKey>();
            var binaryFiles = new EntitySectionBundlesInBuild();

            var instance = DotsGlobalSettings.Instance;
            var playerGuid = instance.GetClientGUID();

            if (instance.GetPlayerType() == DotsGlobalSettings.PlayerType.Server)
            {
                playerGuid = instance.GetServerGUID();
                if (!playerGuid.IsValid)
                {
                    LogMissingServerProvider();
                    playerGuid = instance.GetClientGUID();
                }
            }

            if(!playerGuid.IsValid)
                throw new BuildFailedException("Invalid Player GUID");

            EntitySceneBuildUtility.PrepareEntityBinaryArtifacts(playerGuid, subSceneGuids, artifactKeys);
            binaryFiles.Add(artifactKeys.Keys, artifactKeys.Values);

            var target = EditorUserBuildSettings.activeBuildTarget;

            var entitySceneGUIDs = binaryFiles.SceneGUIDs.ToArray();

            // Add any other from customizers
            var types = TypeCache.GetTypesDerivedFrom<IEntitySceneBuildCustomizer>();
            foreach (var type in types)
            {
                var buildCustomizer = (IEntitySceneBuildCustomizer)Activator.CreateInstance(type);
                buildCustomizer.RegisterAdditionalFilesToDeploy(rootSceneGUIDs.ToArray(), entitySceneGUIDs, binaryFiles, RegisterAdditionalFileToDeploy);
            }

            EntitySceneBuildUtility.PrepareAdditionalFiles(playerGuid, binaryFiles.SceneGUIDs.ToArray(), binaryFiles.ArtifactKeys.ToArray(), target, RegisterAdditionalFileToDeploy);

            // Create and deploy resource catalog containing data of gameobject scenes that needs to be loaded in SceneSystem.Create
            AddResourceCatalog(rootSceneInfos.ToArray(), RegisterAdditionalFileToDeploy);
        }

        void AddResourceCatalog(RootSceneInfo[] sceneInfos, Action<string, string> registerAdditionalFileToDeploy)
        {
            var tempFile = Path.GetFullPath(Path.Combine(Application.dataPath, m_BuildWorkingDir, EntityScenesPaths.RelativePathForSceneInfoFile));
            ResourceCatalogBuildCode.WriteCatalogFile(sceneInfos, tempFile);
            registerAdditionalFileToDeploy(tempFile, EntityScenesPaths.RelativePathForSceneInfoFile);
        }
    }
}
