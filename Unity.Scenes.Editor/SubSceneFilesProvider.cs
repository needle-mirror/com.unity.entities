#if USING_PLATFORMS_PACKAGE
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Build.Classic;
using Unity.Build.Common;
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

        public override void OnBeforeRegisterAdditionalFilesToDeploy()
        {
            if (BuildTarget == BuildTarget.NoTarget)
                throw new InvalidOperationException($"Invalid build target '{BuildTarget.ToString()}'.");

            if (BuildTarget != EditorUserBuildSettings.activeBuildTarget)
                throw new InvalidOperationException($"ActiveBuildTarget must be switched before the {nameof(EntitySceneBuildUtility)} runs.");

            var sceneList = Context.GetComponentOrDefault<SceneList>();
            var binaryFiles = Context.GetOrCreateValue<EntitySectionBundlesInBuild>();
            var buildTargetGUID = new Hash128(Context.BuildConfigurationAssetGUID);
            var scenePaths = sceneList.GetScenePathsForBuild();
            var subSceneGuids = new HashSet<Hash128>(scenePaths.SelectMany(scenePath => EditorEntityScenes.GetSubScenes(AssetDatabaseCompatibility.PathToGUID(scenePath))));
            var artifactKeys = new Dictionary<Hash128, ArtifactKey>();

            EntitySceneBuildUtility.PrepareEntityBinaryArtifacts(buildTargetGUID, subSceneGuids, artifactKeys);

            // Put in component to pass data further along in build
            binaryFiles.Add(artifactKeys.Keys, artifactKeys.Values);
        }

        public override void RegisterAdditionalFilesToDeploy(Action<string, string> registerAdditionalFileToDeploy)
        {
            var tempFile = Path.GetFullPath(Path.Combine(WorkingDirectory, EntityScenesPaths.RelativePathForSceneInfoFile));
            ResourceCatalogBuildCode.WriteCatalogFile(Context.GetComponentOrDefault<SceneList>(), tempFile);
            registerAdditionalFileToDeploy(tempFile, EntityScenesPaths.FullPathForFile(StreamingAssetsDirectory, EntityScenesPaths.RelativePathForSceneInfoFile));
        }
    }
}
#endif
