using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using Hash128 = Unity.Entities.Hash128;
using System.Collections.Generic;
using UnityEditor.Experimental;
using System.IO;
using Unity.Entities.Build;
using Unity.Entities.Conversion;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Unity.Build.Classic.Private;

namespace Unity.Scenes.Editor
{
    internal class EntitySceneBuildPlayerProcessor : BuildPlayerProcessor
    {
        private static readonly List<string> m_FilesCopiedToStreamingAssetFolder = new List<string>();
        private static bool m_CreatedStreamingAssetFolder;
        private static bool m_CreatedEntitySceneFolder;

        class PostProcessBuild : IPostprocessBuildWithReport
        {
            public int callbackOrder => int.MaxValue;

            public void OnPostprocessBuild(BuildReport report)
            {
                if (m_CreatedStreamingAssetFolder)
                {
                    Directory.Delete(Application.streamingAssetsPath, true);
                    File.Delete(Application.streamingAssetsPath + ".meta");
                }
                else
                {
                    if (m_CreatedEntitySceneFolder)
                    {
                        Directory.Delete(Application.streamingAssetsPath + "/" + EntityScenesPaths.k_EntitySceneSubDir, true);
                        File.Delete(Application.streamingAssetsPath + "/" + EntityScenesPaths.k_EntitySceneSubDir + ".meta");
                    }

                    //Delete remaining files
                    foreach (var file in m_FilesCopiedToStreamingAssetFolder)
                    {
                        if(File.Exists(file))
                            File.Delete(file);
                        //Delete meta file
                        if(File.Exists(file + ".meta"))
                            File.Delete(file + ".meta");
                    }
                }
            }
        }

        void RegisterAdditionalFileToDeploy(string from, string to)
        {
            var parent = Path.GetDirectoryName(to);
            Directory.CreateDirectory(parent);
            File.Copy(from, to, true);
            m_FilesCopiedToStreamingAssetFolder.Add(to);
        }

        [InitializeOnLoadMethod]
        public static void Init()
        {
            BuildPlayerWindow.RegisterGetBuildPlayerOptionsHandler(HandleGetBuild);
        }

        static BuildPlayerOptions HandleGetBuild(BuildPlayerOptions opts)
        {
            if (!LiveConversionSettings.IsBuiltinBuildsEnabled)
                throw new BuildFailedException("Can't build from the Unity build settings window. Make sure to enable Preferences -> Entities -> Builtin Builds Enabled.");

            opts = BuildPlayerWindow.DefaultBuildMethods.GetBuildPlayerOptions(opts);
            var instance = DotsGlobalSettings.Instance;

            if (instance.GetPlayerType() == DotsGlobalSettings.PlayerType.Server)
            {
                opts.extraScriptingDefines = instance.ServerProvider.GetExtraScriptingDefines();
                // Adding EnableHeadlessMode as an option will switch the platform to dedicated server that defines UNITY_SERVER in the Editor as well.
                // We may want to switch back to the original platform at the end of the build to prevent it if we don't support switching to dedicated server. Currently the Editor fails to compile after switching to the dedicated server subtarget.
                opts.options |= instance.ServerProvider.GetExtraBuildOptions();
            }
            else
            {
                opts.extraScriptingDefines = instance.ClientProvider.GetExtraScriptingDefines();
                opts.options |= instance.ClientProvider.GetExtraBuildOptions();
            }
            return opts;
        }

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            m_FilesCopiedToStreamingAssetFolder.Clear();
            m_CreatedStreamingAssetFolder = false;
            m_CreatedEntitySceneFolder = false;

            if (BuildPlayerStep.BuildFromBuildConfiguration)
                return;

            if (!LiveConversionSettings.IsBakingEnabled || !LiveConversionSettings.IsBuiltinBuildsEnabled)
                return;

            // Retrieve list of subscenes to import from the root scenes added to the player settings
            var rootSceneInfos = new List<ResourceCatalogBuildCode.RootSceneInfo>();
            for (int i = 0; i < buildPlayerContext.BuildPlayerOptions.scenes.Length; i++)
            {
                var rootScenePath = buildPlayerContext.BuildPlayerOptions.scenes[i];
                rootSceneInfos.Add(new ResourceCatalogBuildCode.RootSceneInfo()
                {
                    AutoLoad = i == 0, //only first one will be loaded at start
                    Path = rootScenePath,
                    Guid = AssetDatabase.GUIDFromAssetPath(rootScenePath)
                });
            }
            var subSceneGuids = new HashSet<Hash128>(rootSceneInfos.SelectMany(rootScene => EditorEntityScenes.GetSubScenes(rootScene.Guid)));

            // Import subscenes and deploy entity scene files and bundles
            var artifactKeys = new Dictionary<Hash128, ArtifactKey>();
            var binaryFiles = new EntitySectionBundlesInBuild();

            var instance = DotsGlobalSettings.Instance;
            var playerGuid = instance.GetPlayerType() == DotsGlobalSettings.PlayerType.Client? instance.GetClientGUID() : instance.GetServerGUID();
            if(!playerGuid.IsValid)
                throw new BuildFailedException("Invalid Player GUID");

            EntitySceneBuildUtility.PrepareEntityBinaryArtifacts(playerGuid, subSceneGuids, artifactKeys);
            binaryFiles.Add(artifactKeys.Keys, artifactKeys.Values);

            var target = EditorUserBuildSettings.activeBuildTarget;
            if (!Directory.Exists(Application.streamingAssetsPath))
                m_CreatedStreamingAssetFolder = true;
            if (!Directory.Exists(Application.streamingAssetsPath + "/" +EntityScenesPaths.k_EntitySceneSubDir))
                m_CreatedEntitySceneFolder = true;

            EntitySceneBuildUtility.PrepareAdditionalFiles(binaryFiles.SceneGUIDs.ToArray(), binaryFiles.ArtifactKeys.ToArray(), target, RegisterAdditionalFileToDeploy, Application.streamingAssetsPath);

            // Create and deploy resource catalog containing data of gameobject scenes that needs to be loaded in SceneSystem.Create
            AddResourceCatalog(rootSceneInfos.ToArray(), RegisterAdditionalFileToDeploy);
        }

        void AddResourceCatalog(ResourceCatalogBuildCode.RootSceneInfo[] sceneInfos, Action<string, string> registerAdditionalFileToDeploy)
        {
            var workingDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, $"../Library/BuildWorkingDir/{PlayerSettings.productName}"));
            Directory.CreateDirectory(workingDirectory);
            var tempFile = System.IO.Path.Combine(workingDirectory, EntityScenesPaths.k_SceneInfoFileName);
            ResourceCatalogBuildCode.WriteCatalogFile(sceneInfos, tempFile);
            registerAdditionalFileToDeploy(tempFile, System.IO.Path.Combine(Application.streamingAssetsPath, EntityScenesPaths.k_SceneInfoFileName));
        }
    }
}
