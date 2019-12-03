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
        public int SubSectionCount;
        public List<NativeArray<RuntimeGlobalObjectId>> SubSections;
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
            PlayerConnection.instance.Register(LiveLinkMsg.ResponseSubSceneForGUIDHeader, ReceiveSubSceneHeader);
            PlayerConnection.instance.Register(LiveLinkMsg.ResponseSubSceneForGUIDEntityBinaryFile, ReceiveEntityBinaryFile);
            PlayerConnection.instance.Register(LiveLinkMsg.ResponseSubSceneForGUIDRefs, ReceiveSubSceneRefGUIDs);

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
            PlayerConnection.instance.Unregister(LiveLinkMsg.ResponseSubSceneForGUIDHeader, ReceiveSubSceneHeader);
            PlayerConnection.instance.Unregister(LiveLinkMsg.ResponseSubSceneForGUIDEntityBinaryFile, ReceiveEntityBinaryFile);
            PlayerConnection.instance.Unregister(LiveLinkMsg.ResponseSubSceneForGUIDRefs, ReceiveSubSceneRefGUIDs);
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

        //TODO: There is too much code duplication here, refactor this to receive general artifacts from the editor
        unsafe void ReceiveSubSceneRefGUIDs(MessageEventArgs args)
        {
            fixed (byte* ptr = args.data)
            {
                var reader = new UnsafeAppendBuffer.Reader(ptr, args.data.Length);
                var subSceneAsset = reader.ReadNext<ResolvedSubSceneID>();
                var sectionIndex = reader.ReadNext<int>();
                reader.ReadNext(out NativeArray<RuntimeGlobalObjectId> objRefGUIDs, Allocator.Persistent);
                var refObjGUIDsPath = EntityScenesPaths.GetLiveLinkCachePath(subSceneAsset.TargetHash, EntityScenesPaths.PathType.EntitiesUnityObjectReferences, sectionIndex);
                
                // Not printing error because this can happen when running the same player multiple times on the same machine
                if (File.Exists(refObjGUIDsPath))
                {
                    LiveLinkMsg.LogInfo($"Received {subSceneAsset.SubSceneGUID} | {subSceneAsset.TargetHash} but it already exists on disk");
                }
                else
                {
                    LiveLinkMsg.LogInfo($"ReceieveSubSceneRefGUIDs => {subSceneAsset.SubSceneGUID} | {subSceneAsset.TargetHash},");
                    
                    var tempCachePath = GetTempCachePath();
                    using (var writer = new StreamBinaryWriter(tempCachePath))
                    {
                        writer.Write(objRefGUIDs.Length);
                        writer.WriteArray(objRefGUIDs);
                    }
                    
                    try
                    {
                        File.Move(tempCachePath, refObjGUIDsPath);
                    }
                    catch (Exception e)
                    {
                        File.Delete(tempCachePath);
                        if (!File.Exists(refObjGUIDsPath))
                        {
                            Debug.LogError($"Failed to move temporary file. Exception: {e.Message}");
                            LiveLinkMsg.LogInfo($"Failed to move temporary file. Exception: {e.Message}");
                        }
                    }
                }
                
                if (!_WaitingForSubScenes.ContainsKey(subSceneAsset.SubSceneGUID))
                {
                    Debug.LogError($"Received {subSceneAsset.SubSceneGUID} | {subSceneAsset.TargetHash} without requesting it");
                    LiveLinkMsg.LogInfo($"Received {subSceneAsset.SubSceneGUID} | {subSceneAsset.TargetHash} without requesting it");
                    return;
                }

                var waitingSubScene = _WaitingForSubScenes[subSceneAsset.SubSceneGUID];
                waitingSubScene.SubSections.Add(objRefGUIDs);
                _WaitingForSubScenes[subSceneAsset.SubSceneGUID] = waitingSubScene;
            }
        }
        
        unsafe void ReceiveSubSceneHeader(MessageEventArgs args)
        {
            fixed (byte* ptr = args.data)
            {
                var reader = new UnsafeAppendBuffer.Reader(ptr, args.data.Length);
                var subSceneAsset = reader.ReadNext<ResolvedSubSceneID>();
                var subSectionCount = reader.ReadNext<int>();
                var headerPath = EntityScenesPaths.GetLiveLinkCachePath(subSceneAsset.TargetHash, EntityScenesPaths.PathType.EntitiesHeader, -1);
                
                // Not printing error because this can happen when running the same player multiple times on the same machine
                if (File.Exists(headerPath))
                {
                    LiveLinkMsg.LogInfo($"Received {subSceneAsset.SubSceneGUID} | {subSceneAsset.TargetHash} but it already exists on disk");
                }
                else
                {
                    var tempCachePath = GetTempCachePath();
                    LiveLinkMsg.LogInfo($"ReceiveSubSceneHeader => {subSceneAsset.SubSceneGUID} | {subSceneAsset.TargetHash}, '{tempCachePath}' => '{headerPath}'");
                    
                    var stream = File.OpenWrite(tempCachePath);
                    stream.Write(args.data, reader.Offset, args.data.Length - reader.Offset);
                    stream.Close();
                    stream.Dispose();
                    
                    try
                    {
                        File.Move(tempCachePath, headerPath);
                    }
                    catch (Exception e)
                    {
                        File.Delete(tempCachePath);
                        if (!File.Exists(headerPath))
                        {
                            Debug.LogError($"Failed to move temporary file. Exception: {e.Message}");
                            LiveLinkMsg.LogInfo($"Failed to move temporary file. Exception: {e.Message}");
                        }
                    }
                }
                
                if (!_WaitingForSubScenes.ContainsKey(subSceneAsset.SubSceneGUID))
                {
                    Debug.LogError($"Received {subSceneAsset.SubSceneGUID} | {subSceneAsset.TargetHash} without requesting it");
                    LiveLinkMsg.LogInfo($"Received {subSceneAsset.SubSceneGUID} | {subSceneAsset.TargetHash} without requesting it");
                    return;
                }

                var waitingSubScene = _WaitingForSubScenes[subSceneAsset.SubSceneGUID];
                waitingSubScene.TargetHash = subSceneAsset.TargetHash;
                waitingSubScene.SubSectionCount = subSectionCount;
                _WaitingForSubScenes[subSceneAsset.SubSceneGUID] = waitingSubScene;
            }
        }
        
        unsafe void ReceiveEntityBinaryFile(MessageEventArgs args)
        {
            fixed (byte* ptr = args.data)
            {
                var reader = new UnsafeAppendBuffer.Reader(ptr, args.data.Length);
                var subSceneAsset = reader.ReadNext<ResolvedSubSceneID>();
                var sectionIndex = reader.ReadNext<int>();
                var ebfPath = EntityScenesPaths.GetLiveLinkCachePath(subSceneAsset.TargetHash, EntityScenesPaths.PathType.EntitiesBinary, sectionIndex);
                
                // Not printing error because this can happen when running the same player multiple times on the same machine
                if (File.Exists(ebfPath))
                {
                    LiveLinkMsg.LogInfo($"Received {subSceneAsset.SubSceneGUID} | {sectionIndex} | {subSceneAsset.TargetHash} but it already exists on disk");
                }
                else
                {
                    var tempCachePath = GetTempCachePath();
                    LiveLinkMsg.LogInfo($"ReceiveEntityBinaryFile => {subSceneAsset.SubSceneGUID} | {sectionIndex} | {subSceneAsset.TargetHash}, '{tempCachePath}' => '{ebfPath}'");
                    
                    var stream = File.OpenWrite(tempCachePath);
                    stream.Write(args.data, reader.Offset, args.data.Length - reader.Offset);
                    stream.Close();
                    stream.Dispose();
                    
                    try
                    {
                        File.Move(tempCachePath, ebfPath);
                    }
                    catch (Exception e)
                    {
                        File.Delete(tempCachePath);
                        if (!File.Exists(ebfPath))
                        {
                            Debug.LogError($"Failed to move temporary file. Exception: {e.Message}");
                            LiveLinkMsg.LogInfo($"Failed to move temporary file. Exception: {e.Message}");
                        }
                    }
                }
                
                if (!_WaitingForSubScenes.ContainsKey(subSceneAsset.SubSceneGUID))
                {
                    Debug.LogError($"Received {subSceneAsset.SubSceneGUID} | {sectionIndex} | {subSceneAsset.TargetHash} without requesting it");
                    LiveLinkMsg.LogInfo($"Received {subSceneAsset.SubSceneGUID} | {sectionIndex} | {subSceneAsset.TargetHash} without requesting it");
                }
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
                    Debug.LogError($"Received {asset.GUID} | {asset.TargetHash} without requesting it");
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
                    LiveLinkMsg.LogInfo($"patching asset bundle: {assetGUID}");

                    patchAssetBundles.Add(oldAssetBundle);
                    patchAssetBundlesPath.Add(assetBundleCachePath);

                    _GlobalAssetObjectResolver.UpdateTargetHash(assetGUID, targetHash);
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

                var loadedManifest = assetBundle.LoadAsset<AssetObjectManifest>(assetGUID.ToString());
                if (loadedManifest == null)
                {
                    Debug.LogError($"Loaded {assetGUID} failed to load ObjectManifest");
                    return;
                }

                _GlobalAssetObjectResolver.UpdateObjectManifest(assetGUID, loadedManifest);
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

                    var headerPath = EntityScenesPaths.GetLiveLinkCachePath(subSceneAsset.TargetHash, EntityScenesPaths.PathType.EntitiesHeader, -1);
                    
                    if (File.Exists(headerPath))
                    {
                        LiveLinkMsg.LogInfo($"ReceiveResponseSubSceneTargetHash => {subSceneAsset.SubSceneGUID} | {subSceneAsset.TargetHash}, File.Exists => 'True', {Path.GetFullPath(headerPath)}");
                        
                        //TODO: This is a hack to make sure assets are managed by asset manifest when loading from cache for first run
                        if (!BlobAssetReference<SceneMetaData>.TryRead(headerPath, SceneMetaDataSerializeUtility.CurrentFileFormatVersion, out var sceneMetaDataRef))
                        {
                            Debug.LogError("Loading Entity Scene failed because the entity header file was an old version: " + subSceneAsset.SubSceneGUID);
                            return;
                        }
                        
                        ref var sceneMetaData = ref sceneMetaDataRef.Value;
                        
                        var waitingSubScene = new WaitingSubScene
                        {
                            TargetHash = subSceneAsset.TargetHash,
                            SubSections = new List<NativeArray<RuntimeGlobalObjectId>>(),
                            SubSectionCount = sceneMetaData.Sections.Length
                        };
                        
                        for (int i = 0; i < sceneMetaData.Sections.Length; i++)
                        {
                            if (sceneMetaData.Sections[i].ObjectReferenceCount != 0)
                            {
                                var refObjGUIDsPath = EntityScenesPaths.GetLiveLinkCachePath(subSceneAsset.TargetHash, EntityScenesPaths.PathType.EntitiesUnityObjectReferences, i);
                                using (var reader = new StreamBinaryReader(refObjGUIDsPath))
                                {
                                    var numObjRefGUIDs = reader.ReadInt();
                                    NativeArray<RuntimeGlobalObjectId> objRefGUIDs = new NativeArray<RuntimeGlobalObjectId>(numObjRefGUIDs, Allocator.Persistent);
                                    reader.ReadArray(objRefGUIDs, numObjRefGUIDs);
                                    waitingSubScene.SubSections.Add(objRefGUIDs);
                                }
                            }
                            else
                            {
                                waitingSubScene.SubSections.Add(new NativeArray<RuntimeGlobalObjectId>(0, Allocator.Persistent));
                            }
                        }

                        _WaitingForSubScenes[subSceneAsset.SubSceneGUID] = waitingSubScene;
                    }
                    else
                    {
                        LiveLinkMsg.LogInfo($"ReceiveResponseSubSceneTargetHash => {subSceneAsset.SubSceneGUID} | {subSceneAsset.TargetHash}, File.Exists => 'False', {Path.GetFullPath(headerPath)}");

                        _WaitingForSubScenes[subSceneAsset.SubSceneGUID] = new WaitingSubScene
                        {
                            TargetHash = new Hash128(),
                            SubSections = new List<NativeArray<RuntimeGlobalObjectId>>(),
                            SubSectionCount = 0
                        };

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

                    if (subScene.Value.SubSectionCount != subScene.Value.SubSections.Count)
                        hasSubScene = false;

                    foreach (var subSection in subScene.Value.SubSections)
                    {
                        if(!World.GetExistingSystem<LiveLinkPlayerSystem>().IsResourceReady(subSection))
                        {
                            hasSubScene = false;
                            break;
                        }
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
                        foreach (var subSection in subScene.Value.SubSections)
                            subSection.Dispose();

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