using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Build;
using Unity.Build.Common;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEditor.Build.Pipeline.Tasks;
using UnityEditor.Build.Pipeline.Utilities;
using UnityEditor.Build.Pipeline.Injector;
using UnityEditor.Build.Pipeline.WriteTypes;
using UnityEditor.Build.Utilities;
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
            var bundleNames = new HashSet<string>();
            var subSceneGuids = scenePathsForBuild.SelectMany(scenePath => SceneMetaDataImporter.GetSubSceneGuids(AssetDatabase.AssetPathToGUID(scenePath))).Distinct().ToList();
            var subScenePaths = new Dictionary<Hash128, string>();
            var dependencyInputData = new Dictionary<SceneSection, SectionDependencyInfo>();
            var refExt = EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesUnityObjectReferences);
            var headerExt = EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesHeader);
            var binaryExt = EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesBinary);

            var group = BuildPipeline.GetBuildTargetGroup(target);
            var parameters = new BundleBuildParameters(target, @group, buildWorkingDirectory)
            {
                BundleCompression = BuildCompression.LZ4Runtime
            };

            var requiresRefresh = false;
            var sceneBuildConfigGuids = new NativeArray<GUID>(subSceneGuids.Count, Allocator.TempJob);
            for (int i = 0; i != sceneBuildConfigGuids.Length; i++)
            {
                sceneBuildConfigGuids[i] = SceneWithBuildConfigurationGUIDs.EnsureExistsFor(subSceneGuids[i], new Hash128(buildConfigurationGuid), out var thisRequiresRefresh);
                requiresRefresh |= thisRequiresRefresh;
            }
            if (requiresRefresh)
                AssetDatabase.Refresh();

            var artifactHashes = new NativeArray<UnityEngine.Hash128>(subSceneGuids.Count, Allocator.TempJob);
            AssetDatabaseCompatibility.ProduceArtifactsRefreshIfNecessary(sceneBuildConfigGuids, typeof(SubSceneImporter), artifactHashes);

            for (int i = 0; i != sceneBuildConfigGuids.Length; i++)
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
                    if (ext == headerExt)
                    {
                        foundEntityHeader = true;

                        if (!string.IsNullOrEmpty(artifactPaths.FirstOrDefault(a => a.EndsWith(refExt))))
                        {
                            subScenePaths[sceneGuid] = artifactPath;
                        }
                        else
                        {
                            //if there are no reference bundles, then deduplication can be skipped
                            var destinationFile = EntityScenesPaths.RelativePathFolderFor(sceneGuid, EntityScenesPaths.PathType.EntitiesHeader, -1);
                            DoCopy(RegisterFileCopy, outputStreamingAssetsDirectory, artifactPath, destinationFile);
                        }
                    }
                    else if (ext == binaryExt)
                    {
                        var destinationFile = EntityScenesPaths.RelativePathFolderFor(sceneGuid, EntityScenesPaths.PathType.EntitiesBinary, EntityScenesPaths.GetSectionIndexFromPath(artifactPath));
                        DoCopy(RegisterFileCopy, outputStreamingAssetsDirectory, artifactPath, destinationFile);
                    }

                    if (ext == refExt)
                    {
                        content.CustomAssets.Add(new CustomContent
                        {
                            Asset = sceneBuildConfigGuid,
                            Processor = (guid, processor) =>
                            {
                                var sectionIndex = EntityScenesPaths.GetSectionIndexFromPath(artifactPath);
                                processor.GetObjectIdentifiersAndTypesForSerializedFile(artifactPath, out ObjectIdentifier[] objectIds, out Type[] types);
                                dependencyInputData[new SceneSection() { SceneGUID = sceneGuid, Section = sectionIndex }] = CreateDependencyInfo(objectIds, target, parameters.ScriptInfo);
                                var bundlePath = EntityScenesPaths.GetLoadPath(sceneGuid, EntityScenesPaths.PathType.EntitiesUnityObjectReferencesBundle, sectionIndex);
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

            var dependencyMapping = new Dictionary<Hash128, Dictionary<SceneSection, List<Hash128>>>();
            var explicitLayout = new BundleExplictObjectLayout();
            ContentPipeline.BuildCallbacks.PostDependencyCallback = (buildParams, dependencyData) => ExtractDuplicateObjects(buildParams, dependencyInputData, explicitLayout, bundleNames, dependencyMapping);
            var status = ContentPipeline.BuildAssetBundles(parameters, content, out IBundleBuildResults result, CreateTaskList(), explicitLayout);
            PostBuildCallback?.Invoke(dependencyMapping);
            foreach (var bundleName in bundleNames)
                DoCopy(RegisterFileCopy, outputStreamingAssetsDirectory, buildWorkingDirectory + "/" + bundleName, "SubScenes/" + bundleName);

            foreach (var ssIter in subScenePaths)
            {
                string headerArtifactPath = ssIter.Value;
                Hash128 sceneGUID = ssIter.Key;

                dependencyMapping.TryGetValue(sceneGUID, out var sceneDependencyData);
                var tempPath = $"{buildWorkingDirectory}/{sceneGUID}.{EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesHeader)}";

                if (!BlobAssetReference<SceneMetaData>.TryRead(headerArtifactPath, SceneMetaDataSerializeUtility.CurrentFileFormatVersion, out var sceneMetaDataRef))
                {
                    Debug.LogError($"Loading Entity Scene failed because the entity header file was an old version or doesn't exist: guid={headerArtifactPath} path={headerArtifactPath}");
                    continue;
                }
                UpdateSceneMetaDataDependencies(ref sceneMetaDataRef, sceneDependencyData, tempPath);
                sceneMetaDataRef.Dispose();
                var headerDestPath = EntityScenesPaths.RelativePathFolderFor(sceneGUID, EntityScenesPaths.PathType.EntitiesHeader, -1);
                DoCopy(RegisterFileCopy, outputStreamingAssetsDirectory, tempPath, headerDestPath);
            }

            var succeeded = status >= ReturnCode.Success;
            if (!succeeded)
                throw new InvalidOperationException($"BuildAssetBundles failed with status '{status}'.");
        }
        public static Action<Dictionary<Hash128, Dictionary<SceneSection, List<Hash128>>>> PostBuildCallback;

        static void UpdateSceneMetaDataDependencies(ref BlobAssetReference<SceneMetaData> sceneMetaData, Dictionary<SceneSection, List<Hash128>> sceneDependencyData, string outPath)
        {
            var blob = new BlobBuilder(Allocator.Temp);
            ref var root = ref blob.ConstructRoot<SceneMetaData>();
            blob.Construct(ref root.Sections, sceneMetaData.Value.Sections.ToArray());
            blob.Construct(ref root.SceneSectionCustomMetadata, sceneMetaData.Value.SceneSectionCustomMetadata.ToArray());
            blob.AllocateString(ref root.SceneName, sceneMetaData.Value.SceneName.ToString());
            BlobBuilderArray<BlobArray<Hash128>> deps = blob.Allocate(ref root.Dependencies, sceneMetaData.Value.Sections.Length);

            if (sceneDependencyData != null)
            {
                for (int i = 0; i < deps.Length; i++)
                {
                    var section = new SceneSection()
                    {
                        SceneGUID = sceneMetaData.Value.Sections[i].SceneGUID,
                        Section = sceneMetaData.Value.Sections[i].SubSectionIndex
                    };

                    if (sceneDependencyData.TryGetValue(section, out var bundleIds))
                        blob.Construct(ref deps[i], bundleIds.ToArray());
                }
            }

            BlobAssetReference<SceneMetaData>.Write(blob, outPath, SceneMetaDataSerializeUtility.CurrentFileFormatVersion);
        }

        static void DoCopy(Action<string, string> RegisterFileCopy, string outputStreamingAssetsFolder, string src, string dst)
        {
            RegisterFileCopy(src, outputStreamingAssetsFolder + "/" + dst);
#if USE_ASSETBUNDLES_IN_EDITOR_PLAY_MODE
            if (!Directory.Exists(UnityEngine.Application.streamingAssetsPath))
                Directory.CreateDirectory(UnityEngine.Application.streamingAssetsPath);
            RegisterFileCopy(src, UnityEngine.Application.streamingAssetsPath + "/" + dst);
#endif
        }

        public static SectionDependencyInfo CreateDependencyInfo(ObjectIdentifier[] objectIds, BuildTarget target, UnityEditor.Build.Player.TypeDB scriptInfo)
        {
            //TODO: cache this dependency data
            var dependencies = ContentBuildInterface.GetPlayerDependenciesForObjects(objectIds, target, scriptInfo);
            var depTypes = ContentBuildInterface.GetTypeForObjects(dependencies);
            var paths = dependencies.Select(i => AssetDatabase.GUIDToAssetPath(i.guid.ToString())).ToArray();
            return new SectionDependencyInfo() { Dependencies = dependencies, Paths = paths, Types = depTypes };
        }

        public struct SectionDependencyInfo
        {
            public ObjectIdentifier[] Dependencies;
            public Type[] Types;
            public string[] Paths;
        }

        static ReturnCode ExtractDuplicateObjects(IBuildParameters parameters, Dictionary<SceneSection, SectionDependencyInfo> dependencyInpuData, BundleExplictObjectLayout layout, HashSet<string> bundleNames, Dictionary<Hash128, Dictionary<SceneSection, List<Hash128>>> result)
        {
            var bundleLayout = new Dictionary<Hash128, List<ObjectIdentifier>>();
            CreateAssetLayoutData(dependencyInpuData, result, bundleLayout);
            ExtractExplicitBundleLayout(bundleLayout, layout, bundleNames);
            return ReturnCode.Success;
        }

        static void ExtractExplicitBundleLayout(Dictionary<Hash128, List<ObjectIdentifier>> bundleLayout, BundleExplictObjectLayout layout, HashSet<string> bundleNames)
        {
            foreach (var sectionIter in bundleLayout)
            {
                var bundleName = $"{sectionIter.Key}.bundle";
                foreach (var i in sectionIter.Value)
                {
                    try
                    { layout.ExplicitObjectLocation.Add(i, bundleName); }
                    catch { Debug.LogError($"Trying to add bundle: '{bundleName}' current value '{layout.ExplicitObjectLocation[i]}' object type '{ContentBuildInterface.GetTypeForObject(i).Name}'"); };
                }
                bundleNames.Add(bundleName);
            }
        }

        /// <summary>
        /// Create bundle layout and depedendency data for subscene bundles
        /// </summary>
        /// <param name="dependencyInputData">Mapping of SceneSection to dependency info for that section.</param>
        /// <param name="dependencyResult">Mapping of subscene id to mapping of section to bundle ids</param>
        /// <param name="bundleLayoutResult">Mapping of bundle ids to included objects</param>
        public static void CreateAssetLayoutData(Dictionary<SceneSection, SectionDependencyInfo> dependencyInputData, Dictionary<Hash128, Dictionary<SceneSection, List<Hash128>>> dependencyResult, Dictionary<Hash128, List<ObjectIdentifier>> bundleLayoutResult)
        {
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            if (!ValidateInput(dependencyInputData, out var error))
            {
                Debug.Log(error);
                return;
            }

            var depToSections = new Dictionary<ObjectIdentifier, List<SceneSection>>();
            //for each subscene, collect all dependencies and map them to the scenes they are referenced by.
            //also create a mapping from the subscene to all of its depedencies
            foreach (var sectionIter in dependencyInputData)
            {
                foreach (var dependency in sectionIter.Value.Dependencies)
                {
                    // Built In Resources we reference directly
                    if (dependency.guid == GUIDHelper.UnityBuiltinResources)
                        continue;

                    if (!depToSections.TryGetValue(dependency, out List<SceneSection> sectionList))
                    {
                        sectionList = new List<SceneSection>();
                        depToSections.Add(dependency, sectionList);
                    }
                    sectionList.Add(sectionIter.Key);
                }
            }

            //convert each list of scenes into a hash
            var objToSectionUsageHash = new Dictionary<ObjectIdentifier, Hash128>();
            foreach (var objIter in depToSections)
            {
                if (objIter.Value.Count <= 1)
                    continue;

                objToSectionUsageHash.Add(objIter.Key, HashingMethods.Calculate(objIter.Value).ToHash128());
            }

            if (objToSectionUsageHash.Count > 0)
            {
                //create mapping from scene hash to included dependencies
                foreach (var objIter in objToSectionUsageHash)
                {
                    if (!bundleLayoutResult.TryGetValue(objIter.Value, out var ids))
                        bundleLayoutResult.Add(objIter.Value, ids = new List<ObjectIdentifier>());
                    ids.Add(objIter.Key);
                }

                foreach (var sectionIter in dependencyInputData)
                {
                    var bundleHashes = new HashSet<Hash128>();
                    foreach (var dep in dependencyInputData[sectionIter.Key].Dependencies)
                        if (objToSectionUsageHash.TryGetValue(dep, out var sceneHash))
                            bundleHashes.Add(sceneHash);
                    if (!dependencyResult.TryGetValue(sectionIter.Key.SceneGUID, out var sectionMap))
                        dependencyResult.Add(sectionIter.Key.SceneGUID, sectionMap = new Dictionary<SceneSection, List<Hash128>>());
                    sectionMap[sectionIter.Key] = bundleHashes.ToList();
                }
            }

            sw.Stop();
            Debug.Log($"CreateAssetLayoutData time: {sw.Elapsed}");
        }

        public static bool ValidateInput(Dictionary<SceneSection, SectionDependencyInfo> dependencyInputData, out string firstError)
        {
            firstError = null;
            if (dependencyInputData == null)
            {
                firstError = "NULL dependencyInputData.";
                return false;
            }
            foreach (var sec in dependencyInputData)
            {
                if (!sec.Key.SceneGUID.IsValid)
                {
                    firstError = "Invalid scene guid for section.";
                    return false;
                }
                if (sec.Key.Section < 0)
                {
                    firstError = $"Scene {sec.Key.SceneGUID} - Invalid section index {sec.Key.Section}.";
                    return false;
                }
                if (sec.Value.Dependencies == null)
                {
                    firstError = $"Scene {sec.Key.SceneGUID} - null Dependencies.";
                    return false;
                }
                if (sec.Value.Paths == null)
                {
                    firstError = $"Scene {sec.Key.SceneGUID} - null Paths.";
                    return false;
                }
                if (sec.Value.Types == null)
                {
                    firstError = $"Scene {sec.Key.SceneGUID} - null Types.";
                    return false;
                }
                if (sec.Value.Dependencies.Length != sec.Value.Paths.Length || sec.Value.Dependencies.Length != sec.Value.Types.Length)
                {
                    firstError = $"Scene {sec.Key.SceneGUID} - Data length mismatch: Dependencies: {sec.Value.Dependencies.Length}, Types: {sec.Value.Types.Length}, Paths: {sec.Value.Paths.Length}.";
                    return false;
                }
                for (int i = 0; i < sec.Value.Dependencies.Length; i++)
                {
                    if (sec.Value.Dependencies[i].guid.Empty())
                    {
                        firstError = $"Scene {sec.Key.SceneGUID} - Dependencies[{i}] has invalid GUID, path='{sec.Value.Paths[i]}'.";
                        return false;
                    }
                    if (sec.Value.Types[i] == null)
                    {
                        firstError = $"Scene {sec.Key.SceneGUID} - Types[{i}] is NULL, path='{sec.Value.Paths[i]}'.";
                        return false;
                    }
                    if (string.IsNullOrEmpty(sec.Value.Paths[i]))
                    {
                        firstError = $"Scene {sec.Key.SceneGUID} - Paths[{i}] is NULL or empty.";
                        return false;
                    }
                }
            }
            return true;
        }

        public class UpdateBundlePacking : IBuildTask
        {
            public int Version { get { return 1; } }

#pragma warning disable 649
            [InjectContext]
            IBundleWriteData m_WriteData;

            [InjectContext(ContextUsage.In, true)]
            IBundleExplictObjectLayout m_Layout;

            [InjectContext(ContextUsage.In)]
            IDeterministicIdentifiers m_PackingMethod;
#pragma warning restore 649

            public ReturnCode Run()
            {
                if (m_Layout != null)
                {
                    var extractedBundlesToFileDependencies = new Dictionary<string, HashSet<string>>();
                    foreach (var pair in m_Layout.ExplicitObjectLocation)
                    {
                        ObjectIdentifier objectID = pair.Key;
                        string bundleName = pair.Value;
                        string internalName = string.Format(CommonStrings.AssetBundleNameFormat, m_PackingMethod.GenerateInternalFileName(bundleName));
                        foreach (var assetFilesPair in m_WriteData.AssetToFiles)
                        {
                            if (assetFilesPair.Value.Contains(internalName))
                            {
                                if (!extractedBundlesToFileDependencies.TryGetValue(internalName, out var dependencies))
                                {
                                    extractedBundlesToFileDependencies.Add(internalName, dependencies = new HashSet<string>());
                                    foreach (var afp in assetFilesPair.Value)
                                        dependencies.Add(afp);
                                }
                            }
                        }
                    }
                    Dictionary<string, WriteCommand> fileToCommand = m_WriteData.WriteOperations.ToDictionary(x => x.Command.internalName, x => x.Command);
                    foreach (var pair in extractedBundlesToFileDependencies)
                    {
                        var refMap = m_WriteData.FileToReferenceMap[pair.Key];
                        foreach (var fileDependency in pair.Value)
                        {
                            var cmd = fileToCommand[fileDependency];
                            refMap.AddMappings(fileDependency, cmd.serializeObjects.ToArray());
                        }
                        var cmd2 = fileToCommand[pair.Key];
                        refMap.AddMappings(pair.Key, cmd2.serializeObjects.ToArray(), true);
                    }
                }
                return ReturnCode.Success;
            }
        }

        static IList<IBuildTask> CreateTaskList()
        {
            var taskList = DefaultBuildTasks.Create(DefaultBuildTasks.Preset.AssetBundleBuiltInShaderExtraction);
            // Remove the shader task to use the DOTS dedupe pass only
            taskList.Remove(taskList.First(x => x is CreateBuiltInShadersBundle));
            // Insert the dedupe dependency resolver task
            taskList.Insert(taskList.IndexOf(taskList.First(x => x is GenerateSubAssetPathMaps)), new UpdateBundlePacking());
            return taskList;
        }
    }
}
