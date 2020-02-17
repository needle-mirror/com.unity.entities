using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEditor;
using UnityEditor.Build.Content;
using UnityEditor.Experimental;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine;
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

            SendSubScene(subSceneID, args.playerId);
        }

        public void RequestSubSceneTargetHash(MessageEventArgs args)
        {
            using (var subScenes = args.ReceiveArray<SubSceneGUID>())
            {
                var resolvedScenes = new HashSet<ResolvedSubSceneID>();
                foreach (var subScene in subScenes)
                {
                    LiveLinkMsg.LogInfo($"RequestSubSceneTargetHash => {subScene.Guid}, {subScene.BuildConfigurationGuid}");

                    var targetHash = EntityScenesPaths.GetSubSceneArtifactHash(subScene.Guid, subScene.BuildConfigurationGuid, UnityEditor.Experimental.AssetDatabaseExperimental.ImportSyncMode.Queue);
                    _UsedSubSceneTargetHash[subScene] = targetHash;
                    if(targetHash.IsValid)
                        resolvedScenes.Add(new ResolvedSubSceneID {SubSceneGUID = subScene, TargetHash = targetHash});
                }

                TimeBasedCallbackInvoker.SetCallback(DetectChangedAssets);

                if (resolvedScenes.Count == 0)
                    return;

                var resolved = new NativeArray<ResolvedSubSceneID>(resolvedScenes.Count, Allocator.Temp);
                int i = 0;
                foreach (var id in resolvedScenes)
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

        void SendSubSceneTargetHash(NativeArray<ResolvedSubSceneID> resolvedSubScenes, int playerId)
        {
            foreach (var asset in resolvedSubScenes)
            {
                _UsedSubSceneTargetHash[asset.SubSceneGUID] = asset.TargetHash;
                LiveLinkMsg.LogInfo($"SendSubSceneTargetHash => {asset.SubSceneGUID} to playerId: {playerId}");
            }

            EditorConnection.instance.SendArray(LiveLinkMsg.ResponseSubSceneTargetHash, resolvedSubScenes, playerId);
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

        void SendBuildArtifact(string artifactPath, int playerId)
        {
            string artifactFileName = Path.GetFileName(artifactPath);
            SendBuildArtifact(artifactPath, artifactFileName, playerId);
        }

        unsafe void SendBuildArtifact(string artifactPath, string artifactFileName, int playerId)
        {
            LiveLinkMsg.LogInfo($"SendBuildArtifact => artifactPath={artifactPath}, playerId={playerId}");

            if (!File.Exists(artifactPath))
            {
                Debug.LogError($"Attempting to send file that doesn't exist on editor. {artifactPath}");
                return;
            }

            using (FileStream fs = new FileStream(artifactPath, FileMode.Open, FileAccess.Read))
            {
                // TODO: Any OS/language supports wide chars here? Should be tested
                var bufferSize = fs.Length + artifactFileName.Length * sizeof(char) + sizeof(int);
                if (fs.Length > int.MaxValue)
                {
                    Debug.LogError($"File cannot be sent to the player because it exceeds the 2GB size limit. {artifactPath}");
                    return;
                }
                
                var buffer = new byte[bufferSize];
                fixed (byte* data = buffer)
                {
                    var writer = new UnsafeAppendBuffer(data, (int)bufferSize);
                    writer.Add(artifactFileName);
                    
                    int numBytesToRead = (int)fs.Length;
                    int numBytesRead = writer.Size;
                    while (numBytesToRead > 0)
                    {
                        int n = fs.Read(buffer, numBytesRead, numBytesToRead);

                        if (n == 0)
                            break;

                        numBytesRead += n;
                        numBytesToRead -= n;
                    }
                }
                
                EditorConnection.instance.Send(LiveLinkMsg.SendBuildArtifact, buffer, playerId);
            }
        }

        unsafe void SendResponseSubSceneForGuid(in ResolvedSubSceneID subSceneId, HashSet<Unity.Entities.Hash128> assetDependencies, int playerId)
        {
            var runtimeGlobalObjectIds = new NativeArray<RuntimeGlobalObjectId>(assetDependencies.Count, Allocator.Temp);
            int j = 0;
            foreach (var asset in assetDependencies)
                runtimeGlobalObjectIds[j++] = new RuntimeGlobalObjectId {AssetGUID = asset};

            long bufferSize = sizeof(ResolvedSubSceneID) + sizeof(int) + runtimeGlobalObjectIds.Length * sizeof(RuntimeGlobalObjectId);
            if (bufferSize > int.MaxValue)
            {
                Debug.LogError($"Buffer cannot be sent to the player because it exceeds the 2GB size limit.");
                return;
            }
            
            var buffer = new byte[bufferSize];
            fixed (byte* data = buffer)
            {
                var writer = new UnsafeAppendBuffer(data, (int)bufferSize);
                writer.Add(subSceneId);
                writer.Add(runtimeGlobalObjectIds);
                
                EditorConnection.instance.Send(LiveLinkMsg.ResponseSubSceneForGUID, buffer);
            }
        }
        

        void SendSubScene(ResolvedSubSceneID subSceneId, int playerId)
        {
            LiveLinkMsg.LogInfo($"Sending SubScene: 'GUID: {subSceneId.SubSceneGUID.Guid}' Hash: '{subSceneId.TargetHash}' with 'BuildConfiguration: {subSceneId.SubSceneGUID.BuildConfigurationGuid}' to playerId: {playerId}");
            AssetDatabaseExperimental.GetArtifactPaths(subSceneId.TargetHash, out var paths);
            var sceneHeaderPath = EntityScenesPaths.GetLoadPathFromArtifactPaths(paths, EntityScenesPaths.PathType.EntitiesHeader);

            // Send Header build artifact
            SendBuildArtifact(sceneHeaderPath, playerId);
            
            // Process each scene section, gathering runtime global obj IDs and sending EBFs
            if (!BlobAssetReference<SceneMetaData>.TryRead(sceneHeaderPath, SceneMetaDataSerializeUtility.CurrentFileFormatVersion, out var sceneMetaDataRef))
            {
                Debug.LogError("Send Entity Scene failed because the entity header file was an old version: " + sceneHeaderPath);
                return;
            }

            var assetDependencies = new HashSet<Unity.Entities.Hash128>();
            ref var sceneMetaData = ref sceneMetaDataRef.Value;
            for (int i = 0; i != sceneMetaData.Sections.Length; i++)
            {
                var sectionIndex = sceneMetaData.Sections[i].SubSectionIndex;
                
                var binaryPath = EntityScenesPaths.GetLoadPathFromArtifactPaths(paths, EntityScenesPaths.PathType.EntitiesBinary, sectionIndex);
                var refGuidsPath = EntityScenesPaths.GetLoadPathFromArtifactPaths(paths, EntityScenesPaths.PathType.EntitiesUnityObjectRefGuids, sectionIndex);
                SendBuildArtifact(binaryPath, playerId);
                SendBuildArtifact(refGuidsPath, playerId);
                
                var scriptedObjPath = EntityScenesPaths.GetLoadPathFromArtifactPaths(paths, EntityScenesPaths.PathType.EntitiesUnityObjectReferences, sectionIndex);
                var bundleName = $"{(UnityEngine.Hash128)subSceneId.TargetHash}.{sectionIndex}.{EntityScenesPaths.GetExtension(EntityScenesPaths.PathType.EntitiesUnitObjectReferencesBundle)}";
                var tempPath = Path.GetTempFileName();
                
                AssetBundleTypeCache.RegisterMonoScripts();
                LiveLinkBuildPipeline.BuildSubSceneBundle(scriptedObjPath, bundleName, tempPath,
                    EditorUserBuildSettings.activeBuildTarget, assetDependencies);
                SendBuildArtifact(tempPath, bundleName, playerId);
                File.Delete(tempPath);
            }
            
            SendResponseSubSceneForGuid(subSceneId, assetDependencies, playerId);
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
                        subScene.Key.BuildConfigurationGuid,
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
