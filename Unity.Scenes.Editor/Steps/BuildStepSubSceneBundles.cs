using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using Unity.Platforms;
using Unity.Build;
using Unity.Build.Classic;
using Unity.Build.Common;
using Unity.Build.Internals;
using UnityEditor;

namespace Unity.Scenes.Editor
{
    [BuildStep(Name = "Build SubScene Bundles", Description = "Building SubScene Bundles", Category = "Hybrid")]
    sealed class BuildStepSubSceneBundles : BuildStep
    {
        TemporaryFileTracker m_TemporaryFileTracker;

        public override Type[] RequiredComponents => new[]
        {
            typeof(ClassicBuildProfile),
            typeof(SceneList)
        };

        public override BuildStepResult RunBuildStep(BuildContext context)
        {
            m_TemporaryFileTracker = new TemporaryFileTracker();

            var profile = GetRequiredComponent<ClassicBuildProfile>(context);
            if (profile.Target == UnityEditor.BuildTarget.NoTarget)
                return Failure($"Invalid build target '{profile.Target.ToString()}'.");
            if (profile.Target != EditorUserBuildSettings.activeBuildTarget)
                return Failure($"ActiveBuildTarget must be switched before the {nameof(BuildStepSubSceneBundles)} step.");

            var buildConfigurationGuid = new Hash128(BuildContextInternals.GetBuildConfigurationGUID(context));
            var content = new UnityEditor.Build.Pipeline.BundleBuildContent(new AssetBundleBuild[0]);
            var sceneList = GetRequiredComponent<SceneList>(context);
            var visited = new HashSet<Hash128>();
            foreach (var scenePath in sceneList.GetScenePathsForBuild())
            {
                var sceneGuid = AssetDatabase.AssetPathToGUID(scenePath);
                var subSceneGuids = SceneMetaDataImporter.GetSubSceneGuids(sceneGuid);
                foreach (var subSceneGuid in subSceneGuids)
                {
                    if (!visited.Add(subSceneGuid))
                        continue;

                    var hash128Guid = EntityScenesPaths.CreateBuildConfigurationSceneFile(subSceneGuid, buildConfigurationGuid);
                    content.CustomAssets.Add(new UnityEditor.Build.Pipeline.Interfaces.CustomContent
                    {
                        Asset = hash128Guid,
                        Processor = SubSceneImporter.ConvertToBuild
                    });
                }
            }

            if (content.CustomAssets.Count == 0)
            {
                return Success();
            }

            var buildPath = Path.GetDirectoryName(EntityScenesPaths.GetLoadPath(new Hash128(), EntityScenesPaths.PathType.EntitiesUnityObjectReferences, 0));

            // Delete SubScenes build folder defensively (Eg. if unity crashes during build)
            FileUtil.DeleteFileOrDirectory(buildPath);

            m_TemporaryFileTracker.CreateDirectory(buildPath);

            var group = UnityEditor.BuildPipeline.GetBuildTargetGroup(profile.Target);
            var parameters = new UnityEditor.Build.Pipeline.BundleBuildParameters(profile.Target, group, buildPath);
            parameters.BundleCompression = UnityEngine.BuildCompression.Uncompressed;

            var status = UnityEditor.Build.Pipeline.ContentPipeline.BuildAssetBundles(parameters, content, out UnityEditor.Build.Pipeline.Interfaces.IBundleBuildResults result);
            context.SetValue(result);

            var succeeded = status >= UnityEditor.Build.Pipeline.ReturnCode.Success;
            return succeeded ? Success() : Failure($"BuildAssetBundles failed with status '{status}'.");
        }

        public override BuildStepResult CleanupBuildStep(BuildContext context)
        {
            m_TemporaryFileTracker.Dispose();
            return Success();
        }
    }
}
