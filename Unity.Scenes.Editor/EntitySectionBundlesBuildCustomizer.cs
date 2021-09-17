using System;
using Unity.Build.Classic;
using Unity.Entities;
using UnityEditor;
using UnityEditor.Experimental;

namespace Unity.Scenes.Editor
{
    /// <summary>
    /// The set of Entity sections that will be included in the build.
    /// EntitySectionBundlesInBuild is attached to the BuildContext using BuildContext.GetOrCreateValue<EntitySectionBundlesInBuild>();
    /// </summary>
    internal sealed class EntitySectionBundlesInBuild
    {
        /// <summary>
        /// EntityScenes may only be added from the ClassicBuildPipelineCustomizer.OnBeforeRegisterAdditionalFilesToDeploy callback.
        /// </summary>
        internal Hash128[] SceneGUIDs;
        internal ArtifactKey[] ArtifactKeys;
    }

    class EntitySectionBundlesBuildCustomizer : ClassicBuildPipelineCustomizer
    {
        public override void RegisterAdditionalFilesToDeploy(Action<string, string> registerAdditionalFileToDeploy)
        {
            if (!Context.HasValue<EntitySectionBundlesInBuild>())
                return;

            var binaryFiles = Context.GetValue<EntitySectionBundlesInBuild>();

            // Additional pre-checks to detect when the same SceneGUID or artifacts are added multiple times which will fail the builder further on.
#if false
            if (binaryFiles.SceneGUID.Distinct().Count() != binaryFiles.SceneGUID.Count())
                throw new ArgumentException("Some of the EntityScenes guids in build are not unique");
            if (binaryFiles.ArtifactKeys.Distinct().Count() != binaryFiles.ArtifactKeys.Count())
                throw  new ArgumentException("Some of the EntityScenes target resolved guids in build are not unique");
#endif

            if (BuildTarget != EditorUserBuildSettings.activeBuildTarget)
                throw new InvalidOperationException($"ActiveBuildTarget must be switched before the {nameof(EntitySceneBuildUtility)} runs.");

            EntitySceneBuildUtility.PrepareAdditionalFiles(binaryFiles.SceneGUIDs, binaryFiles.ArtifactKeys, BuildTarget, registerAdditionalFileToDeploy, StreamingAssetsDirectory);
        }
    }
}
