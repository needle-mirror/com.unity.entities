using System;
using System.Collections.Generic;
using System.IO;
using Unity.Build;
using Unity.Build.Common;
using Unity.Build.Internals;
using Unity.Entities;
using UnityEditor;

namespace Unity.Scenes.Editor
{
    [BuildStep(description = k_Description, category = "Hybrid")]
    sealed class BuildStepSubSceneBundles : BuildStep
    {
        const string k_Description = "Build SubScene Bundles";
        TemporaryFileTracker m_TemporaryFileTracker;

        public override string Description => k_Description;

        public override Type[] RequiredComponents => new[]
        {
            typeof(ClassicBuildProfile),
            typeof(SceneList)
        };

        public override BuildStepResult RunBuildStep(BuildContext context)
        {
            m_TemporaryFileTracker = new TemporaryFileTracker();

            var profile = GetRequiredComponent<ClassicBuildProfile>(context);
            if (profile.Target == BuildTarget.NoTarget)
                return Failure($"Invalid build target '{profile.Target.ToString()}'.");
            if (profile.Target != EditorUserBuildSettings.activeBuildTarget)
                return Failure($"ActiveBuildTarget must be switched before the {nameof(BuildStepSubSceneBundles)} step.");

            var buildSettingsGuid = new Hash128(BuildContextInternals.GetBuildSettingsGUID(context));
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

                    var hash128Guid = EntityScenesPaths.CreateBuildSettingSceneFile(subSceneGuid, buildSettingsGuid);
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
