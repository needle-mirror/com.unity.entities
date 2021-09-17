using System;
using System.Collections.Generic;
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
            binaryFiles.SceneGUIDs = artifactKeys.Keys.ToArray();
            binaryFiles.ArtifactKeys = artifactKeys.Values.ToArray();
        }

        public override void RegisterAdditionalFilesToDeploy(Action<string, string> registerAdditionalFileToDeploy)
        {
            var sceneList = Context.GetComponentOrDefault<SceneList>();
            var tempFile = System.IO.Path.Combine(WorkingDirectory, EntityScenesPaths.k_SceneInfoFileName);
            ResourceCatalogBuildCode.WriteCatalogFile(sceneList, tempFile);
            registerAdditionalFileToDeploy(tempFile, System.IO.Path.Combine(StreamingAssetsDirectory, EntityScenesPaths.k_SceneInfoFileName));
        }
    }
}
