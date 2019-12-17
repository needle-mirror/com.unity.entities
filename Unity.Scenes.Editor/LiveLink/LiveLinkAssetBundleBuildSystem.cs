using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEditor;
using UnityEditor.Experimental;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine.Networking.PlayerConnection;
using Hash128 = UnityEngine.Hash128;

namespace Unity.Scenes.Editor
{
    class LiveLinkAssetBundleBuildSystem : ScriptableSingleton<LiveLinkAssetBundleBuildSystem>
    {
        readonly Dictionary<GUID, Hash128> _UsedAssetsTargetHash = new Dictionary<GUID, Hash128>();
        readonly Dictionary<SubSceneGUID, Hash128> _UsedSubSceneTargetHash = new Dictionary<SubSceneGUID, Hash128>();

        public const string LiveLinkAssetBundleCache = "Library/LiveLinkAssetBundleCache/";
        public const string AssetObjectManifestPath = "Temp/Temp-AssetObjectManifest";

        public void ClearUsedAssetsTargetHash()
        {
            _UsedAssetsTargetHash.Clear();
        }

        void ReceiveBuildRequest(MessageEventArgs args)
        {
            var guid = args.Receive<GUID>();
            LiveLinkMsg.LogReceived($"AssetBundleBuild request: '{guid}' -> '{AssetDatabase.GUIDToAssetPath(guid.ToString())}'");

            SendAssetBundle(guid, args.playerId);
        }

        public void RequestSubSceneForGUID(MessageEventArgs args)
        {
            var subSceneID = args.Receive<ResolvedSubSceneID>();
            LiveLinkMsg.LogInfo($"RequestSubSceneForGUID => {subSceneID.SubSceneGUID}");

            SendSubScene(subSceneID.SubSceneGUID.Guid, subSceneID.SubSceneGUID.BuildSettingsGuid, args.playerId);
        }

        public void RequestSubSceneTargetHash(MessageEventArgs args)
        {
            using (var subScenes = args.ReceiveArray<SubSceneGUID>())
            {
                var resolvedAssets = new HashSet<ResolvedSubSceneID>();
                foreach (var subScene in subScenes)
                {
                    LiveLinkMsg.LogInfo($"RequestSubSceneTargetHash => {subScene.Guid}, {subScene.BuildSettingsGuid}");

                    var targetHash = EntityScenesPaths.GetSubSceneArtifactHash(subScene.Guid, subScene.BuildSettingsGuid, UnityEditor.Experimental.AssetDatabaseExperimental.ImportSyncMode.Queue);
                    _UsedSubSceneTargetHash[subScene] = targetHash;
                    if(targetHash.IsValid)
                        resolvedAssets.Add(new ResolvedSubSceneID {SubSceneGUID = subScene, TargetHash = targetHash});
                }

                TimeBasedCallbackInvoker.SetCallback(DetectChangedAssets);

                if (resolvedAssets.Count == 0)
                    return;

                var resolved = new NativeArray<ResolvedSubSceneID>(resolvedAssets.Count, Allocator.Temp);
                int i = 0;
                foreach (var id in resolvedAssets)
                    resolved[i++] = id;

                SendSubSceneTargetHash(resolved, args.playerId);
            }
        }

        void RequestAssetBundleTargetHash(MessageEventArgs args)
        {
            //@TODO: should be based on connection / BuildSetting
            var buildTarget = EditorUserBuildSettings.activeBuildTarget;

            using (var assets = args.ReceiveArray<GUID>())
            {
                var resolvedAssets = new HashSet<ResolvedAssetID>();
                foreach(var asset in assets)
                {
                    LiveLinkMsg.LogReceived($"AssetBundleTargetHash request => {asset}");

                    var targetHash = LiveLinkBuildPipeline.CalculateTargetHash(asset, buildTarget);
                    resolvedAssets.Add(new ResolvedAssetID { GUID = asset, TargetHash = targetHash });

                    LiveLinkBuildPipeline.CalculateTargetDependencies(asset, buildTarget, out ResolvedAssetID[] dependencies);
                    resolvedAssets.UnionWith(dependencies);
                }

                var resolved = new NativeArray<ResolvedAssetID>(resolvedAssets.Count, Allocator.Temp);
                int j = 0;
                foreach (var id in resolvedAssets)
                    resolved[j++] = id;

                SendAssetBundleTargetHash(resolved, args.playerId);
            }
        }

        void OnEnable()
        {
            EditorConnection.instance.Register(LiveLinkMsg.RequestAssetBundleForGUID, ReceiveBuildRequest);
            EditorConnection.instance.Register(LiveLinkMsg.RequestAssetBundleTargetHash, RequestAssetBundleTargetHash);
            EditorConnection.instance.Register(LiveLinkMsg.RequestSubSceneTargetHash, RequestSubSceneTargetHash);
            EditorConnection.instance.Register(LiveLinkMsg.RequestSubSceneForGUID, RequestSubSceneForGUID);
        }

        void OnDisable()
        {
            EditorConnection.instance.Unregister(LiveLinkMsg.RequestAssetBundleForGUID, ReceiveBuildRequest);
            EditorConnection.instance.Unregister(LiveLinkMsg.RequestAssetBundleTargetHash, RequestAssetBundleTargetHash);
            EditorConnection.instance.Unregister(LiveLinkMsg.RequestSubSceneTargetHash, RequestSubSceneTargetHash);
            EditorConnection.instance.Unregister(LiveLinkMsg.RequestSubSceneForGUID, RequestSubSceneForGUID);
        }

        static string ResolveCachePath(Unity.Entities.Hash128 targethash)
        {
            var path = "Library/LiveLinkAssetBundleCache/" + targethash;
            return path;
        }

        void SendSubSceneTargetHash(NativeArray<ResolvedSubSceneID> resolvedAssets, int playerId)
        {
            foreach (var asset in resolvedAssets)
            {
                _UsedSubSceneTargetHash[asset.SubSceneGUID] = asset.TargetHash;
                LiveLinkMsg.LogInfo($"SendSubSceneTargetHash => {asset.SubSceneGUID} to playerId: {playerId}");
            }

            EditorConnection.instance.SendArray(LiveLinkMsg.ResponseSubSceneTargetHash, resolvedAssets, playerId);
        }

        void SendAssetBundleTargetHash(NativeArray<ResolvedAssetID> resolvedAssets, int playerId)
        {
            foreach (var asset in resolvedAssets)
                LiveLinkMsg.LogSend($"AssetBundleTargetHash response {asset.GUID} | {asset.TargetHash} to playerId: {playerId}");

            EditorConnection.instance.SendArray(LiveLinkMsg.ResponseAssetBundleTargetHash, resolvedAssets, playerId);

            foreach (var asset in resolvedAssets)
                _UsedAssetsTargetHash[asset.GUID] = asset.TargetHash;

            TimeBasedCallbackInvoker.SetCallback(DetectChangedAssets);
        }
		
        //TODO: There is too much code duplication here, refactor this to send general artifacts to the editor
        unsafe void SendSubScene(Unity.Entities.Hash128 subSceneGuid, Unity.Entities.Hash128 buildSettingsGuid, int playerId)
        {
            LiveLinkMsg.LogInfo($"Sending SubScene: 'GUID: {subSceneGuid}' with 'BuildSettings: {buildSettingsGuid}' to playerId: {playerId}");
            
            var hash = EntityScenesPaths.GetSubSceneArtifactHash(subSceneGuid, buildSettingsGuid, AssetDatabaseExperimental.ImportSyncMode.Block);
            AssetDatabaseExperimental.GetArtifactPaths(hash, out var paths);
            var sceneHeaderPath = EntityScenesPaths.GetLoadPathFromArtifactPaths(paths, EntityScenesPaths.PathType.EntitiesHeader);
            
            if (!File.Exists(sceneHeaderPath))
            {
                Debug.LogError("Send Entity Scene failed because the entity header file could not be found: " + sceneHeaderPath);
                return;
            }
            
            if (!BlobAssetReference<SceneMetaData>.TryRead(sceneHeaderPath, SceneMetaDataSerializeUtility.CurrentFileFormatVersion, out var sceneMetaDataRef))
            {
                Debug.LogError("Send Entity Scene failed because the entity header file was an old version: " + sceneHeaderPath);
                return;
            }
            
            ref var sceneMetaData = ref sceneMetaDataRef.Value;
            
            for (int j = 0; j != sceneMetaData.Sections.Length; j++)
            {
                var sectionIndex = sceneMetaData.Sections[j].SubSectionIndex;
                var binaryPath = EntityScenesPaths.GetLoadPathFromArtifactPaths(paths, EntityScenesPaths.PathType.EntitiesBinary, sectionIndex);
                var refObjsPath = EntityScenesPaths.GetLoadPathFromArtifactPaths(paths, EntityScenesPaths.PathType.EntitiesUnityObjectReferences, sectionIndex);
                
                using (FileStream fs = new FileStream(binaryPath, FileMode.Open, FileAccess.Read))
                {
                    var ebfLength = fs.Length;
                    var bufferSize = fs.Length + sizeof(Hash128)*3 + sizeof(int);
                    if (bufferSize > int.MaxValue)
                    {
                        Debug.LogError($"EBF {binaryPath} can't be sent to the player because it exceeds the 2GB size limit");
                        return;
                    }
                    
                    var ebfAndHeader = new byte[bufferSize];
                    fixed (byte* data = ebfAndHeader)
                    {
                        var writer = new UnsafeAppendBuffer(data, ebfAndHeader.Length);
                        writer.Add(subSceneGuid);
                        writer.Add(buildSettingsGuid);
                        writer.Add(hash);
                        writer.Add(sectionIndex);
                        
                        int numBytesToRead = (int)ebfLength;
                        int numBytesRead = writer.Size;
                        while (numBytesToRead > 0)
                        {
                            int n = fs.Read(ebfAndHeader, numBytesRead, numBytesToRead);

                            if (n == 0)
                                break;

                            numBytesRead += n;
                            numBytesToRead -= n;
                        }
                    }
                    
                    EditorConnection.instance.Send(LiveLinkMsg.ResponseSubSceneForGUIDEntityBinaryFile, ebfAndHeader, playerId);
                }

                // Send Obj Refs as GUIDs
                if(sceneMetaData.Sections[j].ObjectReferenceCount != 0)
                {
                    var resourceRequests = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(refObjsPath);
                    var referencedUnityObjects = (ReferencedUnityObjects)resourceRequests[0];
                    
                    var globalObjectIds = new GlobalObjectId[referencedUnityObjects.Array.Length];  
                    GlobalObjectId.GetGlobalObjectIdsSlow(referencedUnityObjects.Array, globalObjectIds);

                    var refObjsSize = globalObjectIds.Length * sizeof(RuntimeGlobalObjectId);
                    // Size of array, subscene GUID, subscene Hash, section index, array count
                    var bufferSize = refObjsSize + sizeof(Hash128)*3 + sizeof(int) + sizeof(int);
                    if (bufferSize > int.MaxValue)
                    {
                        Debug.LogError($"refObjs {refObjsPath} can't be sent to the player because it exceeds the 2GB size limit");
                        return;
                    }
                    
                    var runtimeGlobalObjIDs = new NativeArray<RuntimeGlobalObjectId>(globalObjectIds.Length, Allocator.Temp);
                    for (int i = 0; i != globalObjectIds.Length;i++)
                    {
                        var globalObjectId = globalObjectIds[i];

                        //@TODO: HACK (Object is a scene object)
                        if (globalObjectId.identifierType == 2)
                        {
                            Debug.LogWarning($"{referencedUnityObjects.Array[i]} is part of a scene, LiveLink can't transfer scene objects. (Note: LiveConvertSceneView currently triggers this)");
                            globalObjectId = new GlobalObjectId();
                        }

                        if (globalObjectId.assetGUID == new GUID())
                        {
                            //@TODO: How do we handle this
                            Debug.LogWarning($"{referencedUnityObjects.Array[i]} has no valid GUID. LiveLink currently does not support built-in assets.");
                            globalObjectId = new GlobalObjectId();
                        }

                        runtimeGlobalObjIDs[i] = System.Runtime.CompilerServices.Unsafe.AsRef<RuntimeGlobalObjectId>(&globalObjectId);
                    }
                    
                    var refObjsAndHeader = new byte[bufferSize];
                    fixed (byte* data = refObjsAndHeader)
                    {
                        var writer = new UnsafeAppendBuffer(data, refObjsAndHeader.Length);
                        writer.Add(subSceneGuid);
                        writer.Add(buildSettingsGuid);
                        writer.Add(hash);
                        writer.Add(sectionIndex);
                        writer.Add(runtimeGlobalObjIDs);
                    }
                    
                    EditorConnection.instance.Send(LiveLinkMsg.ResponseSubSceneForGUIDRefs, refObjsAndHeader, playerId);
                }
            }

            // Send Header
            using (FileStream fs = new FileStream(sceneHeaderPath, FileMode.Open, FileAccess.Read))
            {
                var headerLength = fs.Length;
                var bufferSize = fs.Length + sizeof(Hash128)*3 + sizeof(int);
                if (bufferSize > int.MaxValue)
                {
                    Debug.LogError($"SubScene Header {sceneHeaderPath} can't be sent to the player because it exceeds the 2GB size limit");
                    return;
                }

                var subSceneHeaderAndMsgHeader = new byte[bufferSize];
                fixed (byte* data = subSceneHeaderAndMsgHeader)
                {
                    var writer = new UnsafeAppendBuffer(data, subSceneHeaderAndMsgHeader.Length);
                    writer.Add(subSceneGuid);
                    writer.Add(buildSettingsGuid);
                    writer.Add(hash);
                    writer.Add(sceneMetaData.Sections.Length);

                    int numBytesToRead = (int)headerLength;
                    int numBytesRead = writer.Size;
                    while (numBytesToRead > 0)
                    {
                        int n = fs.Read(subSceneHeaderAndMsgHeader, numBytesRead, numBytesToRead);

                        if (n == 0)
                            break;

                        numBytesRead += n;
                        numBytesToRead -= n;
                    }
                }

                EditorConnection.instance.Send(LiveLinkMsg.ResponseSubSceneForGUIDHeader, subSceneHeaderAndMsgHeader, playerId);
            }
        }		

        unsafe void SendAssetBundle(GUID guid, int playerId)
        {
            Hash128 targetHash;
            string path = BuildAssetBundleIfNotCached(guid, out targetHash);
            if (path == null)
                return;

            var stream = File.OpenRead(path);
            var assetBundleFileLength = stream.Length;
            var bufferSize = stream.Length + sizeof(Hash128) + sizeof(Hash128); 
            
            if (bufferSize > int.MaxValue)
            {
                Debug.LogError($"AssetBundle {guid} can't be sent to the player because it exceeds the 2GB size limit");
                return;
            }

            var bundleAndHeader = new byte[bufferSize];
            fixed (byte* data = bundleAndHeader)
            {
                var writer = new UnsafeAppendBuffer(data, bundleAndHeader.Length);
                writer.Add(guid);
                writer.Add(targetHash);
                stream.Read(bundleAndHeader, writer.Size, (int)assetBundleFileLength);
            }

            stream.Close();
            stream.Dispose();

            LiveLinkMsg.LogSend($"AssetBundle: '{AssetDatabase.GUIDToAssetPath(guid.ToString())}' ({guid}), size: {assetBundleFileLength}, hash: {targetHash} to playerId: {playerId}");
            EditorConnection.instance.Send(LiveLinkMsg.ResponseAssetBundleForGUID, bundleAndHeader, playerId);
        }

        public string BuildAssetBundleIfNotCached(GUID guid, out Hash128 targetHash)
        {
            //@TODO Get build target from player requesting it...
            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            targetHash = LiveLinkBuildPipeline.CalculateTargetHash(guid, buildTarget);

            var bundlePath = LiveLinkBuildImporter.GetBundlePath(guid.ToString(), buildTarget);
            if (string.IsNullOrEmpty(bundlePath) || !File.Exists(bundlePath))
            {
                Debug.LogError($"Failed to build asset bundle: '{guid}'");
                return null;
            }
            return bundlePath;
        }

        void DetectChangedAssets()
        {
            if (_UsedAssetsTargetHash.Count == 0 && _UsedSubSceneTargetHash.Count == 0)
            {
                TimeBasedCallbackInvoker.ClearCallback(DetectChangedAssets);
                return;
            }

            using (var changedAssets = new NativeList<ResolvedAssetID>(Allocator.Temp))
            {
                var buildTarget = EditorUserBuildSettings.activeBuildTarget;
                foreach (var asset in _UsedAssetsTargetHash)
                {
                    //@TODO: Artifact hash API should give error message when used on V1 pipeline (currently does not).

                    var targetHash = LiveLinkBuildPipeline.CalculateTargetHash(asset.Key, buildTarget);

                    if (asset.Value != targetHash)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(asset.Key.ToString());
                        LiveLinkMsg.LogInfo("Detected asset change: " + path);
                        changedAssets.Add(new ResolvedAssetID { GUID = asset.Key, TargetHash = targetHash });

                        LiveLinkBuildPipeline.CalculateTargetDependencies(asset.Key, buildTarget, out ResolvedAssetID[] dependencies);
                        foreach (var dependency in dependencies)
                        {
                            if (_UsedAssetsTargetHash.ContainsKey(dependency.GUID))
                                continue;

                            // New Dependency
                            var dependencyHash = LiveLinkBuildPipeline.CalculateTargetHash(dependency.GUID, buildTarget);
                            changedAssets.Add(new ResolvedAssetID { GUID = dependency.GUID, TargetHash = dependencyHash });
                        }
                    }
                }
                if (changedAssets.Length != 0)
                    SendAssetBundleTargetHash(changedAssets, 0);
            }

            using(var changedSubScenes = new NativeList<ResolvedSubSceneID>(Allocator.Temp))
            {
                foreach (var subScene in _UsedSubSceneTargetHash)
                {
                    var targetHash = EntityScenesPaths.GetSubSceneArtifactHash(subScene.Key.Guid,
                        subScene.Key.BuildSettingsGuid,
                        UnityEditor.Experimental.AssetDatabaseExperimental.ImportSyncMode.Queue);
                    if (targetHash.IsValid && (subScene.Value != (Hash128) targetHash))
                    {
                        LiveLinkMsg.LogInfo("Detected subscene change: " + subScene.Key);
                        changedSubScenes.Add(new ResolvedSubSceneID
                            {SubSceneGUID = subScene.Key, TargetHash = targetHash});
                    }
                }

                if(changedSubScenes.Length > 0)
                    SendSubSceneTargetHash(changedSubScenes, 0);
            }
        }
    }
}