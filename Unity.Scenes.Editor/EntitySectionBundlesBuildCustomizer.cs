using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Build.Classic;
using UnityEditor;
using UnityEditor.Experimental;

namespace Unity.Scenes.Editor
{
    /// <summary>
    /// The set of Entity sections that will be included in the build.
    /// EntitySectionBundlesInBuild is attached to the BuildContext using BuildContext.GetOrCreateValue<EntitySectionBundlesInBuild>();
    /// </summary>
    sealed public class EntitySectionBundlesInBuild
    {
        /// <summary>
        /// Adds an Entity Scene that will be produced by a ScriptedImporter from any data source.
        /// EntityScenes may only be added from the ClassicBuildPipelineCustomizer.OnBeforeRegisterAdditionalFilesToDeploy callback.
        /// </summary>
        public void Add(GUID sceneGUID, GUID assetGUID, Type importerType)
        {
            SceneGUID.Add(sceneGUID);
            ArtifactKeys.Add(new ArtifactKey(assetGUID, importerType));
        }

        internal List<GUID>        SceneGUID = new List<GUID>();
        internal List<ArtifactKey> ArtifactKeys = new List<ArtifactKey>();
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

            SubSceneBuildCode.PrepareAdditionalFiles(
                binaryFiles.SceneGUID.ToArray(), binaryFiles.ArtifactKeys.ToArray(), BuildTarget, registerAdditionalFileToDeploy, StreamingAssetsDirectory, $"Library/SubsceneBundles");
        }
    }
}
