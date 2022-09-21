using System;
using System.Collections.Generic;
using Unity.Build.Classic;
using Unity.Entities;
using UnityEditor;
using UnityEditor.Experimental;

namespace Unity.Scenes.Editor
{
    /// <summary>
    /// The set of Entity sections that will be included in the build.
    /// </summary>
    /// <remarks>
    /// EntitySectionBundlesInBuild is attached to the BuildContext using BuildContext.GetOrCreateValue&lt;EntitySectionBundlesInBuild&gt;().
    /// </remarks>
    public sealed class EntitySectionBundlesInBuild
    {
        /// <summary>
        /// EntityScenes may only be added from the ClassicBuildPipelineCustomizer.OnBeforeRegisterAdditionalFilesToDeploy callback.
        /// </summary>
        internal List<Hash128> SceneGUIDs = new List<Hash128>();
        internal List<ArtifactKey> ArtifactKeys = new List<ArtifactKey>();

        /// <summary>
        /// Adds a scene to be included in the build
        /// </summary>
        /// <param name="sceneGUID">The GUID of the scene</param>
        /// <param name="artifactKey">The artifact associated with the scene</param>
        public void Add(Hash128 sceneGUID, ArtifactKey artifactKey)
        {
            SceneGUIDs.Add(sceneGUID);
            ArtifactKeys.Add(artifactKey);
        }

        /// <summary>
        /// Adds a number of scenes to be included in the build
        /// </summary>
        /// <param name="sceneGUIDs">The GUIDs of the scenes</param>
        /// <param name="artifactKeys">The artifacts associated with the scene</param>
        public void Add(IEnumerable<Hash128> sceneGUIDs, IEnumerable<ArtifactKey> artifactKeys)
        {
            SceneGUIDs.AddRange(sceneGUIDs);
            ArtifactKeys.AddRange(artifactKeys);
        }
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

            EntitySceneBuildUtility.PrepareAdditionalFiles(binaryFiles.SceneGUIDs.ToArray(), binaryFiles.ArtifactKeys.ToArray(), BuildTarget, registerAdditionalFileToDeploy, StreamingAssetsDirectory);
        }
    }
}
