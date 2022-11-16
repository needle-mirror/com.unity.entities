#if USING_PLATFORMS_PACKAGE
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Build.Classic;
using Unity.Entities;
using UnityEditor;
using UnityEditor.Experimental;

namespace Unity.Scenes.Editor
{
    class EntitySectionBundlesBuildCustomizer : ClassicBuildPipelineCustomizer
    {
        private Action<string, string> m_RegisterAdditionalFilesToDeploy;
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

            m_RegisterAdditionalFilesToDeploy = registerAdditionalFileToDeploy;

            var buildTargetGUID = new Hash128(Context.BuildConfigurationAssetGUID);
            EntitySceneBuildUtility.PrepareAdditionalFiles(buildTargetGUID, binaryFiles.SceneGUIDs.ToArray(), binaryFiles.ArtifactKeys.ToArray(), BuildTarget, InternalRegisterAdditionalFilesToDeploy);
        }

        private void InternalRegisterAdditionalFilesToDeploy(string from, string to)
        {
            var finalTo = $"{StreamingAssetsDirectory}/{to}";
            m_RegisterAdditionalFilesToDeploy(from, finalTo);
        }
    }
}
#endif
