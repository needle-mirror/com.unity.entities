using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Scenes;
using Unity.Scenes.Editor;
using UnityEditor.Experimental;
using UnityEngine;
using UnityEditor;
using System.Linq;
using Unity.Entities.Build;
using System.IO.Hashing;

namespace Unity.Entities.Content
{
    /// <summary>
    /// Utility class for creating remote content data.
    /// </summary>
    public static partial class RemoteContentCatalogBuildUtility
    {
        [MenuItem("Assets/Publish/Existing Build")]
        static void ExistingBuildMenuItem()
        {
            var buildFolder = EditorUtility.OpenFolderPanel("Select Build To Publish", Path.GetDirectoryName(Application.dataPath), "Builds");
            if (!string.IsNullOrEmpty(buildFolder))
            {
                var streamingAssetsPath = $"{buildFolder}/{PlayerSettings.productName}_Data/StreamingAssets";
                PublishContent(streamingAssetsPath, $"{buildFolder}-RemoteContent", f => new string[] { "all" });
            }
        }

        [MenuItem("Assets/Publish/Content Update")]
        static void ContentUpdateMenuItem()
        {
            var buildFolder = EditorUtility.OpenFolderPanel("Select Build To Publish", Path.GetDirectoryName(Application.dataPath), "Builds");
            if (!string.IsNullOrEmpty(buildFolder))
            {
                var buildTarget = EditorUserBuildSettings.activeBuildTarget;
                var tmpBuildFolder = Path.Combine(Path.GetDirectoryName(Application.dataPath), $"Library/ContentUpdateBuildDir/{PlayerSettings.productName}");

                var instance = DotsGlobalSettings.Instance;
                var playerGuid = instance.GetPlayerType() == DotsGlobalSettings.PlayerType.Client ? instance.GetClientGUID() : instance.GetServerGUID();
                if (!playerGuid.IsValid)
                    throw new Exception("Invalid Player GUID");

                var subSceneGuids = new HashSet<Hash128>();
                for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
                {
                    var ssGuids = EditorEntityScenes.GetSubScenes(EditorBuildSettings.scenes[i].guid);
                    foreach (var ss in ssGuids)
                        subSceneGuids.Add(ss);
                }

                BuildContent(subSceneGuids, playerGuid, buildTarget, tmpBuildFolder);
                var catalogPath = Path.Combine(tmpBuildFolder, RuntimeContentManager.RelativeCatalogPath);
                RuntimeContentCatalog catalog = new RuntimeContentCatalog();
                var archivePaths = new List<string>();
                catalog.LoadCatalogData(catalogPath, s => { if (!s.Contains("000000000000000000")) archivePaths.Add(Path.Combine(tmpBuildFolder, RuntimeContentManager.k_ContentArchiveDirectory, s)); return s; }, s => s);
                archivePaths.AddRange(Directory.GetFiles(Path.Combine(tmpBuildFolder, EntityScenesPaths.k_EntitySceneSubDir), "*.*", SearchOption.TopDirectoryOnly));
                var publishFolder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", $"{buildFolder}-RemoteContent");
                archivePaths.Sort();
                PublishContent(archivePaths, tmpBuildFolder, publishFolder, f => new string[] { "all" });
            }
        }

        /// <summary>
        /// Builds content for the player.  This can be used in conjunction with PublishContent to prepare a content update.
        /// </summary>
        /// <param name="subScenes">The subscenes to include in the build.</param>
        /// <param name="playerGUID">The player guid.  This can be provided from <seealso cref="DotsGlobalSettings"/> by calling GetClientGUID() or GetServerGUID().</param>
        /// <param name="target">The build target. <seealso cref="EditorUserBuildSettings.activeBuildTarget"/> can be used.</param>
        /// <param name="buildFolder">The folder to build the content into.</param>
        public static void BuildContent(HashSet<Hash128> subScenes, Hash128 playerGUID, BuildTarget target, string buildFolder)
        {
            if (subScenes.Count == 0)
            {
                Debug.LogError($"BuildContent - The hashSet subScenes is empty.");
                return;
            }
            if (!playerGUID.IsValid)
            {
                Debug.LogError($"BuildContent - invalid playerGUID.");
                return;

            }

            Directory.CreateDirectory(buildFolder);

            var artifactKeys = new Dictionary<Hash128, ArtifactKey>();
            EntitySceneBuildUtility.PrepareEntityBinaryArtifacts(playerGUID, subScenes, artifactKeys);
            //sorting ensures that all items are passed in a deterministic order
            var keys = artifactKeys.Keys.ToArray();
            Array.Sort(keys);
            var values = new ArtifactKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                values[i] = artifactKeys[keys[i]];
            EntitySceneBuildUtility.PrepareAdditionalFiles(playerGUID, keys, values, target, (s, d) => DoCopy(s, Path.Combine(buildFolder, d)));
        }

        static void DoCopy(string src, string dst)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, true);
        }

        /// <summary>
        /// Publish a folder of files.  This will copy or move files from the build folder to the target folder and rename them to the content hash.  Remote catalogs will also be created.
        /// </summary>
        /// <param name="sourceFolder">The folder with the source data.  Files will not be removed during the publish process.  If desired, the entire source folder can be deleted after this process is complete.</param>
        /// <param name="targetFolder">The target folder for the published data.  The structure is the same as the local cache, so this data can be directly installed on device to preload the cache.</param>
        /// <param name="contentSetFunc">This will be called for each file as it is published.  The returned strings will define the content sets that the file will be a part of.  If null is returned, the content will stay in the source folder and will not be published.</param>
        /// <param name="deleteSrcContent">If true, the src content files will be deleted from the build folder.  Ensure that a build is properly backed up before enabling this.</param>
        /// <returns>True if the publish process succeeds.</returns>
        public static bool PublishContent(string sourceFolder, string targetFolder, Func<string, IEnumerable<string>> contentSetFunc, bool deleteSrcContent = false)
        {
            if (!Directory.Exists(sourceFolder))
            {
                Debug.Log($"PublishContent - Source folder {sourceFolder} does not exist.");
                return false;
            }

            var files = Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                Debug.Log($"PublishContent - Source folder {sourceFolder} is empty.");
                return false;
            }
            Array.Sort(files);
            return PublishContent(files, sourceFolder, targetFolder, contentSetFunc, deleteSrcContent);
        }

        /// <summary>
        /// Publish a folder of files.  This will copy or move files from the build folder to the target folder and rename them to the content hash.  Remote catalogs will also be created.
        /// </summary>
        /// <param name="files">The collection of files to publish.  This should be sorted before calling this method to ensure deterministic data.</param>
        /// <param name="sourceFolder">The source folder.  This is used to compute the relative path that is used to find the remote location data at runtime.</param>
        /// <param name="targetFolder">The target folder for the published data.  The structure is the same as the local cache, so this data can be directly installed on device to preload the cache.</param>
        /// <param name="contentSetFunc">This will be called for each file as it is published.  The returned strings will define the content sets that the file will be a part of.  If null is returned, the content will stay in the source folder and will not be published.</param>
        /// <param name="deleteSrcContent">If true, the src content files will be deleted from the build folder.  Ensure that a build is properly backed up before enabling this.</param>
        /// <returns>True if the publish process succeeds.</returns>
        public static bool PublishContent(IEnumerable<string> files, string sourceFolder, string targetFolder, Func<string, IEnumerable<string>> contentSetFunc, bool deleteSrcContent = false)
        {
            try
            {
                sourceFolder = sourceFolder.Replace('\\', '/');

                if (!Directory.Exists(sourceFolder))
                {
                    Debug.Log($"PublishContent - Source folder {sourceFolder} does not exist.");
                    return false;
                }

                var depMap = CreateDependencyMap(sourceFolder);

                Directory.CreateDirectory(targetFolder);
                var entries = new List<(RemoteContentId, RemoteContentLocation, IEnumerable<string>, string)>();
                foreach (var f in files)
                {
                    if (!File.Exists(f))
                    {
                        Debug.LogWarning($"Unable to publish file at path '{f}', skipping.");
                        continue;
                    }

                    var relPath = Path.GetRelativePath(sourceFolder, f).Replace('\\', '/');

                    var contentSets = contentSetFunc.Invoke(relPath);
                    if (contentSets == null)
                        continue;

                    //all .bin files are added to a special group named "local_catalogs" - this group is always delivered even if there is no initial content set specified.
                    //this is to ensure that the RuntimeContentManager initialization has the correct catalog
                    if (Path.GetExtension(relPath) == ".bin")
                        contentSets = contentSets.Concat(new string[] { ContentDeliveryGlobalState.kLocalCatalogsContentSet });

                    //each files is checked to see if it is a dependency of any files or subscenes - if so, it is added to the content set that is named with the object id or subscene guid
                    if (depMap.TryGetValue(relPath, out var referencedBy))
                        contentSets = contentSets.Concat(referencedBy);

                    //content sets are created for directories 
                    contentSets = contentSets.Concat(new string[] { Path.GetDirectoryName(relPath) });

                    var loc = new RemoteContentLocation();
                    using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
                    {
                        var buffer = new byte[fs.Length];
                        fs.Read(buffer, 0, buffer.Length);
                        loc.Size = buffer.LongLength;
                        var crcData = Crc32.Hash(buffer);
                        loc.Crc = Crc32.HashToUInt32(crcData);
                        var hashStr = (loc.Hash = UnityEngine.Hash128.Compute(buffer)).ToString();
                        loc.Path = $"{hashStr[0]}{hashStr[1]}/{hashStr}";
                        relPath = relPath.Replace('\\', '/');
                        var remoteId = new RemoteContentId(relPath);
                        entries.Add((remoteId, loc, contentSets, relPath));
                    }
                    DoCopy(f, Path.Combine(targetFolder, loc.Path.ToString()));
                    if (deleteSrcContent)
                        File.Delete(f);
                }

                var catalogLocationsPath = ContentDeliveryGlobalState.CreateCatalogLocationPath(targetFolder);
                return CreateRemoteContentCatalogData(targetFolder, catalogLocationsPath, entries.Count, i => entries[i]);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        /// <summary>
        /// The full catalog will be created and saved as a cache entry with its content hash.  Another catalog that contains the location for the full catalog will be created
        /// at the specified locations path.  This file should be loaded without caching to check for updated versions of the full catalog and any content.
        /// </summary>
        /// <param name="targetFolder">The target folder to build the catalog data to.</param>
        /// <param name="catalogLocationsPath">The path of the catalog locations file.  This is NOT the full catalog, but a catalog of the full catalog.</param>
        /// <param name="count">The number of catalog entries.</param>
        /// <param name="indexer">The indexer to retrieve the catalog entry at the specified index.</param>
        /// <returns>True if the catalog data was created.</returns>
        static bool CreateRemoteContentCatalogData(string targetFolder, string catalogLocationsPath, int count, Func<int, (RemoteContentId, RemoteContentLocation, IEnumerable<string>, string)> indexer)
        {
            try
            {
                var catalogTempPath = Path.Combine(targetFolder, Path.GetRandomFileName());
                CreateRemoteContentLocationData(catalogTempPath, count, indexer, Path.Combine(targetFolder, "DebugCatalog.txt"));

                var catalogBytes = File.ReadAllBytes(catalogTempPath);
                var catalogSize = catalogBytes.Length;
                var catalogContentHash = UnityEngine.Hash128.Compute(catalogBytes);
                var catalogHashStr = catalogContentHash.ToString();
                var catalogLocationFileName = $"{catalogHashStr[0]}{catalogHashStr[1]}/{catalogHashStr}";

                DoCopy(catalogTempPath, Path.Combine(targetFolder, catalogLocationFileName));

                CreateRemoteContentLocationData(catalogLocationsPath, 1,
                    i => (new RemoteContentId(ContentDeliveryGlobalState.kCatalogLocations), new RemoteContentLocation { Hash = catalogContentHash, Path = catalogLocationFileName, Size = catalogSize, Crc = 0 },
                    new string[] { ContentDeliveryGlobalState.kCatalogLocations }, null), null);

                File.Delete(catalogTempPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        private static void CreateRemoteContentLocationData(string path, int locationCount, Func<int, (RemoteContentId, RemoteContentLocation, IEnumerable<string>, string)> locationIndexer, string debugCatalogPath = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var debugLines = string.IsNullOrEmpty(debugCatalogPath) ? null : new List<string>();
            using (var blobBuilder = new BlobBuilder(Allocator.Temp))
            {
                var setMap = new Dictionary<string, List<int>>();
                ref RemoteContentCatalogData catalog = ref blobBuilder.ConstructRoot<RemoteContentCatalogData>();
                var blobEntries = blobBuilder.Allocate(ref catalog.RemoteContentLocations, locationCount);
                for (int i = 0; i < locationCount; i++)
                {
                    var res = locationIndexer(i);
                    blobEntries[i] = new RemoteContentCatalogData.RemoteContentLocationData { identifier = res.Item1, location = res.Item2 };
                    debugLines?.Add($"Src File:{res.Item4},  RemoteContentId:{res.Item1.Hash} => LocHash:{res.Item2.Hash}, Path:{res.Item2.Path}, Size: {res.Item2.Size}, CRC: {res.Item2.Crc}, Type: {res.Item2.Type}");
                    if (res.Item3 != null)
                    {
                        foreach (var set in res.Item3)
                        {
                            if (!setMap.TryGetValue(set, out var setIndices))
                                setMap.Add(set, setIndices = new List<int>());
                            setIndices.Add(i);
                        }
                    }
                }
                if (setMap.Count > 0)
                {
                    var sets = blobBuilder.Allocate(ref catalog.ContentSets, setMap.Count);
                    var sortedKeys = setMap.Keys.ToArray();
                    Array.Sort(sortedKeys);
                    for (int i = 0; i < sortedKeys.Length; i++)
                    {
                        var setKey = sortedKeys[i];
                        var setVal = setMap[setKey];
                        sets[i] = new RemoteContentCatalogData.RemoteContentSetData { Name = setKey };
                        debugLines?.Add($"Content Set: {setKey}");
                        var ids = blobBuilder.Allocate(ref sets[i].Ids, setVal.Count);
                        for (int j = 0; j < setVal.Count; j++)
                        {
                            ids[j] = setVal[j];
                            if (debugLines != null)
                            {
                                var res = locationIndexer(setVal[j]);
                                debugLines?.Add($"\tRemoteContentId: {res.Item1.Hash}");
                            }
                        }
                    }
                }
                if (debugLines != null)
                    File.WriteAllLines(debugCatalogPath, debugLines);
                BlobAssetReference<RemoteContentCatalogData>.Write(blobBuilder, path, 1);
            }
        }

        //this creates a mapping from files being published to object & subscene ids that depend on them.  the map is used to create content sets of dependencies
        static Dictionary<string, List<string>> CreateDependencyMap(string sourceFolder)
        {
            Func<string, string> remapFunc = (string p) => $"{sourceFolder}/{p}";
            var catalogPath = remapFunc(RuntimeContentManager.RelativeCatalogPath);
            var fileToDependents = new Dictionary<string, List<string>>();
            if (!File.Exists(catalogPath))
            {
                Debug.LogWarning($"ContentArchive catalog not found at path '{catalogPath}'");
                return fileToDependents;
            }

            //collect all entity scene headers so that we can put all dependencies into a single content set with the name of the scene guid
            HashSet<string> entitySceneIds = new HashSet<string>();
            foreach (var e in Directory.GetFiles(sourceFolder, "*.entityheader", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(sourceFolder, e).Replace('\\', '/');
                if (!fileToDependents.TryGetValue(relPath, out var deps))
                    fileToDependents.Add(relPath, deps = new List<string>());
                var objSetName = Path.GetFileNameWithoutExtension(e);
                deps.Add(objSetName);
                entitySceneIds.Add(objSetName);
            }

            //collect all entities section files and put them into the same content set as the header
            foreach (var e in Directory.GetFiles(sourceFolder, "*.entities", SearchOption.AllDirectories))
            {
                var relPath = Path.GetRelativePath(sourceFolder, e).Replace('\\', '/');
                var objSetName = Path.GetFileNameWithoutExtension(e);
                var dotIndex = objSetName.IndexOf('.');
                if (dotIndex < 0)
                    continue;
                objSetName = objSetName.Substring(0, dotIndex);
                if (entitySceneIds.Contains(objSetName))
                {
                    if (!fileToDependents.TryGetValue(relPath, out var deps))
                        fileToDependents.Add(relPath, deps = new List<string>());
                    deps.Add(objSetName);
                }
            }


            var rtcc = new RuntimeContentCatalog();
            rtcc.LoadCatalogData(catalogPath,
                    RuntimeContentManager.DefaultContentFileNameFunc,
                    p => remapFunc(RuntimeContentManager.DefaultArchivePathFunc(p)));

            {//gather object dependencies
                var objLocList = new List<(string, ContentFileId)>();
                foreach (var e in rtcc.ObjectLocations)
                    objLocList.Add((e.Key.ToString(), e.Value.FileId));
                GatherDependencies(ref rtcc, entitySceneIds, sourceFolder, objLocList, fileToDependents, p => remapFunc(RuntimeContentManager.DefaultArchivePathFunc(p)));
            }

            {//gather scene dependencies
                var sceneLocList = new List<(string, ContentFileId)>();
                foreach (var e in rtcc.SceneLocations)
                    sceneLocList.Add((e.Key.ToString(), e.Value.FileId));

                GatherDependencies(ref rtcc, entitySceneIds, sourceFolder, sceneLocList, fileToDependents, p => remapFunc(RuntimeContentManager.DefaultArchivePathFunc(p)));
            }

            return fileToDependents;
        }

        static void GatherDependencies(
            ref RuntimeContentCatalog rtcc,
            HashSet<string> entitySceneIds,
            string sourceFolder,
            IEnumerable<(string, ContentFileId)> locs,
            Dictionary<string, List<string>> fileToDependents,
            Func<string, string> remapFunc)
        {
            foreach (var kvp in locs)
            {
                var objSetName = kvp.Item1;
                var id = objSetName.Substring(0, objSetName.IndexOf(':'));
                //if this object id matches to an entity scene id, change the content set name to match that.  There is no need to have a content set for just the archive.
                if (entitySceneIds.Contains(id))
                    objSetName = id;
                RecurseDependencies(ref rtcc, fileToDependents, sourceFolder, objSetName, kvp.Item2, remapFunc);
            }
        }

        static void RecurseDependencies(ref RuntimeContentCatalog rtcc, Dictionary<string, List<string>> archiveToObjectIds, string sourceFolder, string objSetName, ContentFileId fileId, Func<string, string> remapFunc)
        {
            if (!rtcc.TryGetFileLocation(fileId, out int _, out var deps, out var archiveId, out var _))
                return;
            var archivePath = remapFunc(archiveId.Value.ToString());
            archivePath = Path.GetRelativePath(sourceFolder, archivePath).Replace('\\', '/');
            if (!archiveToObjectIds.TryGetValue(archivePath, out var refs))
                archiveToObjectIds.Add(archivePath, refs = new List<string>());
            refs.Add(objSetName);
            foreach (var d in deps)
                RecurseDependencies(ref rtcc, archiveToObjectIds, sourceFolder, objSetName, d, remapFunc);
        }
    }
}
