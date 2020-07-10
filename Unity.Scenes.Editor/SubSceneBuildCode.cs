using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using BuildCompression = UnityEngine.BuildCompression;
using BuildPipeline = UnityEditor.BuildPipeline;

namespace Unity.Scenes.Editor
{
    static class SubSceneBuildCode
    {
        // This function is responsible for providing all the subscenes to the build.
        //
        // The way these files get generated is that we have a SceneWithBuildConfiguration file, (which is a bit of a hack to work around the inability for scriptable importers to take arguments, so
        // instead we create a different file that points to the scene we want to import, and points to the buildconfiguration we want to import it for).   The SubsceneImporter will import this file,
        // and it will make 3 (relevant) kind of files:
        // - headerfile
        // - entitybinaryformat file (the actual entities payloads)
        // - a SerializedFile that has an array of UnityEngine.Object PPtrs that are used by this entity file.
        //
        // The first two we deal with very simply: they just need to be copied into the build, and we're done.
        // the third one, we will feed as input to the Scriptable build pipeline (which is actually about creating assetbundles), and create an assetbundle that
        // has all those objects in it that the 3rd file referred to.  We do this with a batch api, first we loop through all subscenes, and register with this batch
        // api which assetbundles we'd like to see produced, and then at the end, we say "okay make them please".  this assetbundle creation api has a caching system
        // that is separate from the assetpipeline caching system, so if all goes well, the call to produce these assetbundles will return very fast and did nothing.
        //
        // The reason for the strange looking api, where a two callbacks get passed in is to make integration of the new incremental buildpipeline easier, as this code
        // needs to be compatible both with the current buildpipeline in the dots-repo, as well as with the incremental buildpipeline.  When that is merged, we can simplify this.
        public static void PrepareAdditionalFiles(string buildConfigurationGuid, string[] scenePathsForBuild, BuildTarget target, Action<string, string> RegisterFileCopy, string outputStreamingAssetsDirectory, string buildWorkingDirectory)
        {
            if (target == BuildTarget.NoTarget)
                throw new InvalidOperationException($"Invalid build target '{target.ToString()}'.");

            if (target != EditorUserBuildSettings.activeBuildTarget)
                throw new InvalidOperationException($"ActiveBuildTarget must be switched before the {nameof(SubSceneBuildCode)} runs.");

            var content = new BundleBuildContent(new AssetBundleBuild[0]);
            var bundleNames = new List<string>();
            var subSceneGuids = scenePathsForBuild.SelectMany(scenePath => SceneMetaDataImporter.GetSubSceneGuids(AssetDatabase.AssetPathToGUID(scenePath))).Distinct().ToList();

            var requiresRefresh = false;
            var sceneBuildConfigGuids = new NativeArray<GUID>(subSceneGuids.Count, Allocator.TempJob);
            for (int i=0;i != sceneBuildConfigGuids.Length;i++)
            {
                sceneBuildConfigGuids[i] = SceneWithBuildConfigurationGUIDs.EnsureExistsFor(subSceneGuids[i], new Hash128(buildConfigurationGuid), out var thisRequiresRefresh);
                requiresRefresh |= thisRequiresRefresh;
            }
            if (requiresRefresh)
                AssetDatabase.Refresh();

            var artifactHashes = new NativeArray<UnityEngine.Hash128>(subSceneGuids.Count, Allocator.TempJob);
            AssetDatabaseCompatibility.ProduceArtifactsRefreshIfNecessary(sceneBuildConfigGuids, typeof(SubSceneImporter), artifactHashes);

            for (int i=0;i != sceneBuildConfigGuids.Length;i++)
            {
                var sceneGuid = subSceneGuids[i];
                var sceneBuildConfigGuid = sceneBuildConfigGuids[i];
                var artifactHash = artifactHashes[i];

                bool foundEntityHeader = false;
                AssetDatabaseCompatibility.GetArtifactPaths(artifactHash, out var artifactPaths);
                foreach (var artifactPath in artifactPaths)
                {
                    //@TODO: This looks like a workaround. Whats going on here?
                    var ext = Path.GetExtension(artifactPath).Replace(".", "");

                    if (ext == EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesHeader))
                    {
                        foundEntityHeader = true;
                        var destinationFile = outputStreamingAssetsDirectory + "/" + EntityScenesPaths.RelativePathInStreamingAssetsFolderFor(sceneGuid, EntityScenesPaths.PathType.EntitiesHeader, -1);
                        RegisterFileCopy(artifactPath, destinationFile);
                    }

                    if (ext == EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesBinary))
                    {
                        var destinationFile = outputStreamingAssetsDirectory + "/" + EntityScenesPaths.RelativePathInStreamingAssetsFolderFor(sceneGuid, EntityScenesPaths.PathType.EntitiesBinary, EntityScenesPaths.GetSectionIndexFromPath(artifactPath));
                        RegisterFileCopy(artifactPath, destinationFile);
                    }

                    if (ext == EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesUnityObjectReferences))
                    {
                        content.CustomAssets.Add(new CustomContent
                        {
                            Asset = sceneBuildConfigGuid,
                            Processor = (guid, processor) =>
                            {
                                var sectionIndex = EntityScenesPaths.GetSectionIndexFromPath(artifactPath);
                                processor.GetObjectIdentifiersAndTypesForSerializedFile(artifactPath, out ObjectIdentifier[] objectIds, out Type[] types);
                                var bundlePath = EntityScenesPaths.GetLoadPath(sceneGuid, EntityScenesPaths.PathType.EntitiesUnityObjectReferences, sectionIndex);
                                var bundleName = Path.GetFileName(bundlePath);
                                processor.CreateAssetEntryForObjectIdentifiers(objectIds, artifactPath, bundleName, bundleName, typeof(ReferencedUnityObjects));
                                bundleNames.Add(bundleName);
                            }
                        });
                    }
                }

                if (!foundEntityHeader)
                {
                    Debug.LogError($"Failed to build EntityScene for '{AssetDatabaseCompatibility.GuidToPath(sceneGuid)}'");
                }
            }

            sceneBuildConfigGuids.Dispose();
            artifactHashes.Dispose();

            if (content.CustomAssets.Count <= 0)
                return;

            var group = BuildPipeline.GetBuildTargetGroup(target);
            var parameters = new BundleBuildParameters(target, @group, buildWorkingDirectory)
            {
                BundleCompression = BuildCompression.Uncompressed
            };

            var status = ContentPipeline.BuildAssetBundles(parameters, content, out IBundleBuildResults result);

            foreach (var bundleName in bundleNames)
                RegisterFileCopy(buildWorkingDirectory + "/" + bundleName, outputStreamingAssetsDirectory + "/SubScenes/" + bundleName);

            var succeeded = status >= ReturnCode.Success;
            if (!succeeded)
                throw new InvalidOperationException($"BuildAssetBundles failed with status '{status}'.");
        }
    }
}
