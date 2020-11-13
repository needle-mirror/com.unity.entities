using System;
using System.Linq;
using Unity.Build.Classic;
using Unity.Build.Common;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEditor.Experimental;

namespace Unity.Scenes.Editor
{
    class SubSceneFilesProvider : ClassicBuildPipelineCustomizer
    {
        public override Type[] UsedComponents { get; } =
        {
            typeof(SceneList),
            typeof(ClassicBuildProfile)
        };

        static void AddEntityBinaryFiles(Hash128 buildConfigurationGuid, string[] scenePathsForBuild, EntitySectionBundlesInBuild sectionBundlesInBuild)
        {
            var subSceneGuids = scenePathsForBuild.SelectMany(scenePath => EditorEntityScenes.GetSubScenes(AssetDatabaseCompatibility.PathToGUID(scenePath))).Distinct().ToList();

            var requiresRefresh = false;
            var sceneBuildConfigGuids = new NativeArray<GUID>(subSceneGuids.Count, Allocator.TempJob);
            for (int i = 0; i != sceneBuildConfigGuids.Length; i++)
            {
                sceneBuildConfigGuids[i] = SceneWithBuildConfigurationGUIDs.EnsureExistsFor(subSceneGuids[i], buildConfigurationGuid, false, out var thisRequiresRefresh);
                requiresRefresh |= thisRequiresRefresh;

                sectionBundlesInBuild.Add(subSceneGuids[i], sceneBuildConfigGuids[i], typeof(SubSceneImporter));
            }
            if (requiresRefresh)
                AssetDatabase.Refresh();

            AssetDatabaseCompatibility.ProduceArtifactsAsync(sceneBuildConfigGuids, typeof(SubSceneImporter));
            sceneBuildConfigGuids.Dispose();
        }

        public override void OnBeforeRegisterAdditionalFilesToDeploy()
        {
            if (BuildTarget == BuildTarget.NoTarget)
                throw new InvalidOperationException($"Invalid build target '{BuildTarget.ToString()}'.");

            if (BuildTarget != EditorUserBuildSettings.activeBuildTarget)
                throw new InvalidOperationException($"ActiveBuildTarget must be switched before the {nameof(SubSceneBuildCode)} runs.");

            var sceneList = Context.GetComponentOrDefault<SceneList>();
            var binaryFiles = Context.GetOrCreateValue<EntitySectionBundlesInBuild>();
            var buildTargetGUID = new Hash128(Context.BuildConfigurationAssetGUID);
            AddEntityBinaryFiles(buildTargetGUID, sceneList.GetScenePathsForBuild(), binaryFiles);
        }

        public override void RegisterAdditionalFilesToDeploy(Action<string, string> registerAdditionalFileToDeploy)
        {
            var sceneList = Context.GetComponentOrDefault<SceneList>();
            var tempFile = System.IO.Path.Combine(WorkingDirectory, SceneSystem.k_SceneInfoFileName);
            ResourceCatalogBuildCode.WriteCatalogFile(sceneList, tempFile);
            registerAdditionalFileToDeploy(tempFile, System.IO.Path.Combine(StreamingAssetsDirectory, SceneSystem.k_SceneInfoFileName));
        }
    }
}
