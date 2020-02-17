using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;
using UnityEngine.Experimental.AssetBundlePatching;
using UnityEngine.Networking.PlayerConnection;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    struct ResolvedAssetID : IEquatable<ResolvedAssetID>
    {
        public Hash128 GUID;
        public Hash128 TargetHash;

        public bool Equals(ResolvedAssetID other)
        {
            return GUID == other.GUID && TargetHash == other.TargetHash;
        }
    }
    
    struct ResolvedSubSceneID : IEquatable<ResolvedSubSceneID>
    {
        public SubSceneGUID SubSceneGUID;
        public Hash128 TargetHash;

        public bool Equals(ResolvedSubSceneID other)
        {
            return SubSceneGUID == other.SubSceneGUID && TargetHash == other.TargetHash;
        }
    }
    
    struct WaitingSubScene
    {
        public Hash128 TargetHash;
        public NativeArray<RuntimeGlobalObjectId> RuntimeGlobalObjectIds;
    }


#if UNITY_EDITOR
    [DisableAutoCreation]
#endif
    [AlwaysUpdateSystem]
    [UpdateInGroup(typeof(LiveLinkRuntimeSystemGroup))]
    class LiveLinkPlayerAssetRefreshSystem : ComponentSystem
    {
        public static GlobalAssetObjectResolver _GlobalAssetObjectResolver = new GlobalAssetObjectResolver();

        private Dictionary<Hash128, Hash128>            _WaitingForAssets = new Dictionary<Hash128, Hash128>();
        private Dictionary<SubSceneGUID, WaitingSubScene>    _WaitingForSubScenes = new Dictionary<SubSceneGUID, WaitingSubScene>();
        public Dictionary<SubSceneGUID, Hash128>            _TrackedSubScenes = new Dictionary<SubSceneGUID, Hash128>();

        private EntityQuery                     _ResourceRequests;
        private EntityQuery                     _SubSceneAssetRequests;

        // The resource has been requested from the editor but not necessarily been loaded yet.
        public struct ResourceRequested : IComponentData {}
        public struct SubSceneRequested : IComponentData {}

        public static GlobalAssetObjectResolver GlobalAssetObjectResolver => _GlobalAssetObjectResolver;


        protected override void OnStartRunning()
        {
            PlayerConnection.instance.Register(LiveLinkMsg.ResponseAssetBundleForGUID, ReceiveAssetBundle);
            PlayerConnection.instance.Register(LiveLinkMsg.ResponseAssetBundleTargetHash, ReceiveResponseAssetBundleTargetHash);
            
            PlayerConnection.instance.Register(LiveLinkMsg.ResponseSubSceneTargetHash, ReceiveResponseSubSceneTargetHash);
            PlayerConnection.instance.Register(LiveLinkMsg.ResponseSubSceneForGUID, ReceiveSubScene);
            
            PlayerConnection.instance.Register(LiveLinkMsg.SendBuildArtifact, ReceiveBuildArtifact);

            _ResourceRequests = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ResourceGUID>() },
                None = new[] { ComponentType.ReadOnly<ResourceRequested>(), ComponentType.ReadOnly<ResourceLoaded>() }
            });
            
            _SubSceneAssetRequests = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<SubSceneGUID>() },
                None = new[] { ComponentType.ReadOnly<SubSceneRequested>() }
            });
        }

        protected override void OnStopRunning()
        {
            PlayerConnection.instance.Unregister(LiveLinkMsg.ResponseAssetBundleForGUID, ReceiveAssetBundle);
            PlayerConnection.instance.Unregister(LiveLinkMsg.ResponseAssetBundleTargetHash, ReceiveResponseAssetBundleTargetHash);
            
            PlayerConnection.instance.Unregister(LiveLinkMsg.ResponseSubSceneTargetHash, ReceiveResponseSubSceneTargetHash);
            PlayerConnection.instance.Unregister(LiveLinkMsg.ResponseSubSceneForGUID, ReceiveSubScene);
            
            PlayerConnection.instance.Unregister(LiveLinkMsg.SendBuildArtifact, ReceiveBuildArtifact);
        }

        string GetCachePath(Hash128 targetHash)
        {
            return $"{Application.persistentDataPath}/{targetHash}";
        }

        string GetTempCachePath()
        {
            return $"{Application.persistentDataPath}/{Path.GetRandomFileName()}";
        }

        public Hash128 GetTrackedSubSceneTargetHash(SubSceneGUID subSceneGUID)
        {
            if (!_TrackedSubScenes.TryGetValue(subSceneGUID, out var targetHash))
            {
                Debug.Log($"Failed to find scubScene in TrackedSubScenes: {subSceneGUID}");
                targetHash = new Hash128();
            }

            return targetHash;
        }

        unsafe void ReceiveBuildArtifact(MessageEventArgs args)
        {
            fixed (byte* ptr = args.data)
            {
                LiveLinkMsg.LogInfo($"ReceiveBuildArtifact => Buffer Size: {args.data.Length}");
                var reader = new UnsafeAppendBuffer.Reader(ptr, args.data.Length);
                reader.ReadNext(out string artifactFileName);
                string artifactPath = EntityScenesPaths.ComposeLiveLinkCachePath(artifactFileName);

                if (!File.Exists(artifactPath))
                {
                    LiveLinkMsg.LogInfo($"ReceiveBuildArtifact => {artifactPath}");

                    var tempCachePath = GetTempCachePath();
                    
                    try
                    {
                        var stream = File.OpenWrite(tempCachePath);
                        stream.Write(args.data, reader.Offset, args.data.Length - reader.Offset);
                        stream.Close();
                        stream.Dispose();
                    
                        File.Move(tempCachePath, artifactPath);
                        
                        LiveLinkMsg.LogInfo($"ReceiveBuildArtifact => Successfully written to disc.");
                    }
                    catch (Exception e)
                    {
                        if (File.Exists(tempCachePath))
                        {
                            File.Delete(tempCachePath);
                        }
                        
                        if (!File.Exists(artifactPath))
                        {
                            Debug.LogError($"Failed to move temporary file. Exception: {e.Message}");
                        }
                    }
                }
            }
        }

        unsafe void ReceiveSubScene(MessageEventArgs args)
        {
            fixed (byte* ptr = args.data)
            {
                var reader = new UnsafeAppendBuffer.Reader(ptr, args.data.Length);
                reader.ReadNext(out ResolvedSubSceneID subSceneId);
                reader.ReadNext(out NativeArray<RuntimeGlobalObjectId> runtimeGlobalObjectIds, Allocator.Persistent);
                
                LiveLinkMsg.LogInfo($"ReceiveSubScene => SubScene received {subSceneId} | Asset Dependencies {runtimeGlobalObjectIds.Length}");
                
                if (!IsSubSceneAvailable(subSceneId))
                {
                    Debug.LogError("SubScene is missing artifacts!");
                    return;
                }
                
                AddWaitForSubScene(subSceneId, runtimeGlobalObjectIds);
            }
        }

        //@TODO: Support some sort of transaction like API so we can reload all changed things in batch.
        unsafe void ReceiveAssetBundle(MessageEventArgs args)
        {
            LiveLinkMsg.LogReceived($"AssetBundle: '{args.data.Length}' bytes");

            fixed (byte* ptr = args.data)
            {
                var reader = new UnsafeAppendBuffer.Reader(ptr, args.data.Length);
                var asset = reader.ReadNext<ResolvedAssetID>();
                var assetBundleCachePath = GetCachePath(asset.TargetHash);
                
                // Not printing error because this can happen when running the same player multiple times on the same machine
                if (File.Exists(assetBundleCachePath))
                {
                    LiveLinkMsg.LogInfo($"Received {asset.GUID} | {asset.TargetHash} but it already exists on disk");
                }
                else
                {
                    // cache: look up asset by target hash to see if the version we want is already on the target device
                    //if we already have the asset bundle revision we want, then just put that in the resolver as the active revision of the asset
                    // cache: if not in cache, write actual file to Application.persistentDatapath
                    var tempCachePath = GetTempCachePath();
                    LiveLinkMsg.LogInfo($"ReceiveAssetBundle => {asset.GUID} | {asset.TargetHash}, '{tempCachePath}' => '{assetBundleCachePath}'");
                    
                    var stream = File.OpenWrite(tempCachePath);
                    stream.Write(args.data, reader.Offset, args.data.Length - reader.Offset);
                    stream.Close();
                    stream.Dispose();

                    try
                    {
                        File.Move(tempCachePath, assetBundleCachePath);
                    }
                    catch (Exception e)
                    {
                        File.Delete(tempCachePath);
                        if (!File.Exists(assetBundleCachePath))
                        {
                            Debug.LogError($"Failed to move temporary file. Exception: {e.Message}");
                            LiveLinkMsg.LogInfo($"Failed to move temporary file. Exception: {e.Message}");
                        }
                    }
                }

                if (!_WaitingForAssets.ContainsKey(asset.GUID))
                {
                    LiveLinkMsg.LogInfo($"Received {asset.GUID} | {asset.TargetHash} without requesting it");
                }

                _WaitingForAssets[asset.GUID] = asset.TargetHash;
            }
        }

        void LoadAssetBundles(NativeArray<ResolvedAssetID> assets)
        {
            LiveLinkMsg.LogInfo("--- Begin Load asset bundles");

            var patchAssetBundles = new List<AssetBundle>();
            var patchAssetBundlesPath = new List<string>();
            var newAssetBundles = new List<Hash128>();
            var assetBundleToValidate = new List<Hash128>();


            foreach (var asset in assets)
            {
                var assetGUID = asset.GUID;
                var targetHash = asset.TargetHash;
                var assetBundleCachePath = GetCachePath(targetHash);

                //if we already loaded an asset bundle and we just need a refresh
                var oldAssetBundle = _GlobalAssetObjectResolver.GetAssetBundle(assetGUID);
                if (oldAssetBundle != null)
                {
                    if (oldAssetBundle.isStreamedSceneAssetBundle)
                    {
                        LiveLinkMsg.LogInfo($"Unloading scene bundle: {assetGUID}");
                        var sceneSystem = World.GetExistingSystem<SceneSystem>();
                        if (sceneSystem != null)
                            sceneSystem.ReloadScenesWithHash(assetGUID, targetHash);
                        _GlobalAssetObjectResolver.UnloadAsset(assetGUID);
                        continue;
                    }
                    else
                    {
                        LiveLinkMsg.LogInfo($"patching asset bundle: {assetGUID}");

                        patchAssetBundles.Add(oldAssetBundle);
                        patchAssetBundlesPath.Add(assetBundleCachePath);

                        _GlobalAssetObjectResolver.UpdateTargetHash(assetGUID, targetHash);
                        newAssetBundles.Add(assetGUID);
                    }
                }
                else
                {
                    LiveLinkMsg.LogInfo($"Loaded asset bundle: {assetGUID}");

                    var loadedAssetBundle = AssetBundle.LoadFromFile(assetBundleCachePath);
                    _GlobalAssetObjectResolver.AddAsset(assetGUID, targetHash, null, loadedAssetBundle);
                    newAssetBundles.Add(assetGUID);
                }

                assetBundleToValidate.Add(assetGUID);

                //@TODO: Keep a hashtable of guid -> entity?
                Entities.ForEach((Entity entity, ref ResourceGUID guid) =>
                {
                    if (guid.Guid == assetGUID)
                        EntityManager.AddComponentData(entity, new ResourceLoaded());
                });
            }


            AssetBundleUtility.PatchAssetBundles(patchAssetBundles.ToArray(), patchAssetBundlesPath.ToArray());

            foreach (var assetGUID in newAssetBundles)
            {
                var assetBundle = _GlobalAssetObjectResolver.GetAssetBundle(assetGUID);
                if (assetBundle == null)
                {
                    Debug.LogError($"Could not load requested asset bundle.'");
                    return;
                }

                if (!assetBundle.isStreamedSceneAssetBundle)
                {
                    var loadedManifest = assetBundle.LoadAsset<AssetObjectManifest>(assetGUID.ToString());
                    if (loadedManifest == null)
                    {
                        Debug.LogError($"Loaded {assetGUID} failed to load ObjectManifest");
                        return;
                    }

                    _GlobalAssetObjectResolver.UpdateObjectManifest(assetGUID, loadedManifest);
                }
            }

            foreach(var assetGUID in assetBundleToValidate)
                _GlobalAssetObjectResolver.Validate(assetGUID);

            LiveLinkMsg.LogInfo("--- End Load asset bundles");
        }

        unsafe void ReceiveResponseSubSceneTargetHash(MessageEventArgs args)
        {
            using (var subSceneAssets = args.ReceiveArray<ResolvedSubSceneID>())
            {
                foreach(var subSceneAsset in subSceneAssets)
                {
                    if (_WaitingForSubScenes.ContainsKey(subSceneAsset.SubSceneGUID))
                        return;

                    // If subscene exists locally already, just load it
                    var assetDependencies = new HashSet<RuntimeGlobalObjectId>();
                    if (IsSubSceneAvailable(subSceneAsset, assetDependencies))
                    {
                        LiveLinkMsg.LogInfo($"ReceiveResponseSubSceneTargetHash => {subSceneAsset.SubSceneGUID} | {subSceneAsset.TargetHash}, File.Exists => 'True'");
                        
                        //TODO: This is a hack to make sure assets are managed by asset manifest when loading from cache for first run
                        AddWaitForSubScene(subSceneAsset, assetDependencies);
                    }
                    else
                    {
                        LiveLinkMsg.LogInfo($"ReceiveResponseSubSceneTargetHash => {subSceneAsset.SubSceneGUID} | {subSceneAsset.TargetHash}, File.Exists => 'False'");

                        PlayerConnection.instance.Send(LiveLinkMsg.RequestSubSceneForGUID, subSceneAsset);
                    }
                }
            }
        }

        unsafe void ReceiveResponseAssetBundleTargetHash(MessageEventArgs args)
        {
            using (var resolvedAssets = args.ReceiveArray<ResolvedAssetID>())
            {
                foreach(var asset in resolvedAssets)
                {
                    //TODO: Should we compare against already loaded assets here?
                    if (File.Exists(GetCachePath(asset.TargetHash)))
                    {
                        LiveLinkMsg.LogReceived($"AssetBundleTargetHash => {asset.GUID} | {asset.TargetHash}, File.Exists => 'True'");
                        _WaitingForAssets[asset.GUID] = asset.TargetHash;
                    }
                    else
                    {
                        LiveLinkMsg.LogReceived($"AssetBundleTargetHash => {asset.GUID} | {asset.TargetHash}, File.Exists => 'False'");
                        _WaitingForAssets[asset.GUID] = new Hash128();

                        LiveLinkMsg.LogSend($"AssetBundleBuild request '{asset.GUID}'");
                        PlayerConnection.instance.Send(LiveLinkMsg.RequestAssetBundleForGUID, asset.GUID);
                    }
                }
            }
        }

        public bool IsSubSceneReady(SubSceneGUID subSceneGUID)
        {
            if (!_TrackedSubScenes.TryGetValue(subSceneGUID, out var targetHash))
            {
                return false;
            }
            
            return (targetHash != new Hash128());
        }

        unsafe bool IsSubSceneAvailable(in ResolvedSubSceneID subSceneId, HashSet<RuntimeGlobalObjectId> assetDependencies = null)
        {
            var headerPath = EntityScenesPaths.GetLiveLinkCachePath(subSceneId.TargetHash, EntityScenesPaths.PathType.EntitiesHeader, -1);
            if (!File.Exists(headerPath))
            {
                LiveLinkMsg.LogInfo($"Missing SubScene header! {headerPath}");
                return false;
            }
        
            if (!BlobAssetReference<SceneMetaData>.TryRead(headerPath, SceneMetaDataSerializeUtility.CurrentFileFormatVersion, out var sceneMetaDataRef))
            {
                Debug.LogError("Loading Entity Scene failed because the entity header file was an old version: " + subSceneId.SubSceneGUID);
                return false;
            }

            ref SceneMetaData sceneMetaData = ref sceneMetaDataRef.Value;
            for (int i = 0; i < sceneMetaData.Sections.Length; i++)
            {
                var ebfPath = EntityScenesPaths.GetLiveLinkCachePath(subSceneId.TargetHash, EntityScenesPaths.PathType.EntitiesBinary, i);
                if (!File.Exists(headerPath))
                {
                    LiveLinkMsg.LogInfo($"Missing Entity binary file! {ebfPath}");
                    return false;
                }
                
                if (sceneMetaData.Sections[i].ObjectReferenceCount != 0)
                {
                    var refObjGuidsPath = EntityScenesPaths.GetLiveLinkCachePath(subSceneId.TargetHash, EntityScenesPaths.PathType.EntitiesUnityObjectRefGuids, i);
                    if (!File.Exists(refObjGuidsPath))
                    {
                        LiveLinkMsg.LogInfo($"Missing Entity refObjGuids file! {refObjGuidsPath}");
                        return false;
                    }
                    
                    var assetBundlePath = EntityScenesPaths.GetLiveLinkCachePath(subSceneId.TargetHash, EntityScenesPaths.PathType.EntitiesUnitObjectReferencesBundle, i);
                    if (!File.Exists(assetBundlePath))
                    {
                        LiveLinkMsg.LogInfo($"Missing Entity AssetBundle file! {assetBundlePath}");
                        return false;
                    }

                    if (assetDependencies != null)
                    {
                        using(var data = new NativeArray<byte>(File.ReadAllBytes(refObjGuidsPath), Allocator.Temp))
                        using (var reader = new MemoryBinaryReader((byte*)data.GetUnsafePtr()))
                        {
                            var numObjRefGUIDs = reader.ReadInt();
                            var objRefGUIDs = new NativeArray<RuntimeGlobalObjectId>(numObjRefGUIDs, Allocator.Temp);
                            reader.ReadArray(objRefGUIDs, numObjRefGUIDs);

                            foreach (var runtimeGlobalObjectId in objRefGUIDs)
                            {
                                assetDependencies.Add(runtimeGlobalObjectId);
                            }
                        }
                    }
                }
            }
            
            return true;
        }

        void AddWaitForSubScene(in ResolvedSubSceneID subSceneId, HashSet<RuntimeGlobalObjectId> assetDependencies)
        {
            var runtimeGlobalObjectIds = new NativeArray<RuntimeGlobalObjectId>(assetDependencies.Count, Allocator.Persistent);
            int j = 0;
            foreach (var asset in assetDependencies)
                runtimeGlobalObjectIds[j++] = asset;
            
            AddWaitForSubScene(subSceneId, runtimeGlobalObjectIds);
        }

        void AddWaitForSubScene(in ResolvedSubSceneID subSceneId, NativeArray<RuntimeGlobalObjectId> assetDependencies)
        {
            if (_WaitingForSubScenes.ContainsKey(subSceneId.SubSceneGUID))
            {
                Debug.LogError("Adding SubScene to waiting that we are already waiting for!");
                return;
            }

            var waitingSubScene = new WaitingSubScene {TargetHash = subSceneId.TargetHash, RuntimeGlobalObjectIds = assetDependencies};

            _WaitingForSubScenes[subSceneId.SubSceneGUID] = waitingSubScene;
            LiveLinkMsg.LogInfo($"AddWaitForSubScene => SubScene added to waiting list. {subSceneId.TargetHash}");
        }

        
        protected override void OnUpdate()
        {
            // Request any new guids that we haven't seen yet from the editor
            using (var requestedGuids = _ResourceRequests.ToComponentDataArray<ResourceGUID>(Allocator.TempJob))
            {
                if (requestedGuids.Length > 0)
                {
                    EntityManager.AddComponent(_ResourceRequests, typeof(ResourceRequested));
                    LiveLinkMsg.LogSend($"AssetBundleTargetHash request {requestedGuids.Reinterpret<Hash128>().ToDebugString()}");
                    PlayerConnection.instance.SendArray(LiveLinkMsg.RequestAssetBundleTargetHash, requestedGuids);
                }
            }
            
            // Request any new subscenes that we haven't seen yet from the editor
            using (var requestedSubScenes = _SubSceneAssetRequests.ToComponentDataArray<SubSceneGUID>(Allocator.TempJob))
            {
                if (requestedSubScenes.Length > 0)
                {
                    EntityManager.AddComponent(_SubSceneAssetRequests, typeof(SubSceneRequested));
                    PlayerConnection.instance.SendArray(LiveLinkMsg.RequestSubSceneTargetHash, requestedSubScenes);
                }
            }

            // * Ensure all assets we are waiting for have arrived.
            // * LoadAll asset bundles in one go when everything is ready
            if (_WaitingForAssets.Count != 0)
            {
                bool hasAllAssets = true;
                var assets = new NativeArray<ResolvedAssetID>(_WaitingForAssets.Count, Allocator.TempJob);
                int o = 0;
                foreach (var asset in _WaitingForAssets)
                {
                    if (asset.Value == new Hash128())
                        hasAllAssets = false;
                    assets[o++] = new ResolvedAssetID { GUID = asset.Key, TargetHash = asset.Value };
                }

                if (hasAllAssets)
                {
                    LoadAssetBundles(assets);
                    _WaitingForAssets.Clear();
                }

                assets.Dispose();
            }

            if (_WaitingForSubScenes.Count != 0)
            {
                bool hasAllSubScenes = true;
                foreach (var subScene in _WaitingForSubScenes)
                {
                    bool hasSubScene = subScene.Value.TargetHash.IsValid;
                    
                    if(!World.GetExistingSystem<LiveLinkPlayerSystem>().IsResourceReady(subScene.Value.RuntimeGlobalObjectIds))
                    {
                        hasSubScene = false;
                    }

                    if (!hasSubScene)
                    {
                        hasAllSubScenes = false;
                        break;
                    }

                    _TrackedSubScenes[subScene.Key] = subScene.Value.TargetHash;
                }

                if (hasAllSubScenes)
                {
                    foreach (var subScene in _WaitingForSubScenes)
                        subScene.Value.RuntimeGlobalObjectIds.Dispose();

                    _WaitingForSubScenes.Clear();
                }
            }
        }

        public static void Reset()
        {
            _GlobalAssetObjectResolver.DisposeAssetBundles();
            _GlobalAssetObjectResolver = new GlobalAssetObjectResolver();

            foreach (var world in World.AllWorlds)
            {
                var system = world.GetExistingSystem<LiveLinkPlayerAssetRefreshSystem>();
                if (system != null)
                {
                    system._WaitingForAssets.Clear();
                    system._WaitingForSubScenes.Clear();
                    system._TrackedSubScenes.Clear();
                }
            }
        }
    }
}
