#if !UNITY_DOTSRUNTIME
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Unity.Collections;
using UnityEngine;

namespace Unity.Scenes
{
    public class SceneBundleHandle
    {
        private int _refCount;
        private AssetBundleCreateRequest _assetBundleCreateRequest;
        private AssetBundle _assetBundle;
        private readonly string _bundlePath;

        public static bool UseAssetBundles
        {
            get
            {
#if UNITY_EDITOR
#if USE_ASSETBUNDLES_IN_EDITOR_PLAY_MODE
                if (Application.isPlaying)
                    return true;
#endif
                return false;
#else
                return true;
#endif
            }
        }

        internal AssetBundle AssetBundle
        {
            get
            {
                if (_assetBundle == null)
                {
                    _assetBundle = _assetBundleCreateRequest.assetBundle;
                    _assetBundleCreateRequest = null;
                }

                return _assetBundle;
            }
        }

        private SceneBundleHandle(string bundlePath)
        {
            _refCount = 0;
            _bundlePath = bundlePath;
            _assetBundleCreateRequest = AssetBundle.LoadFromFileAsync(_bundlePath);
        }

        internal bool IsReady()
        {
            if (_assetBundleCreateRequest != null)
            {
                if (!_assetBundleCreateRequest.isDone)
                    return false;

                _assetBundle = _assetBundleCreateRequest.assetBundle;
                _assetBundleCreateRequest = null;
            }

            return true;
        }

        internal void Release()
        {
            var refCount = Interlocked.Decrement(ref _refCount);

            if (refCount <= 0)
            {
                if (refCount < 0)
                    throw new InvalidOperationException($"SceneBundleHandle refcount is less than zero. It has been corrupted.");

                ReleaseBundle(this);
            }
        }
        //used by tests
        internal static string[] GetLoadedBundlesPaths()
        {
            return LoadedBundles.Keys.ToArray();
        }

        internal void Retain()
        {
            Interlocked.Increment(ref _refCount);
        }
        private static readonly Dictionary<string, SceneBundleHandle> LoadedBundles = new Dictionary<string, SceneBundleHandle>();
        private static readonly ConcurrentDictionary<string, SceneBundleHandle> UnloadingBundles = new ConcurrentDictionary<string, SceneBundleHandle>();
        internal static List<SceneBundleHandle> LoadSceneBundles(string mainBundlePath, NativeArray<Entities.Hash128> sharedBundles, bool blocking)
        {
            var bundles = new List<SceneBundleHandle>();

            if (sharedBundles.IsCreated)
            {
                for (int i = 0; i < sharedBundles.Length; i++)
                {
                    var path = $"{Application.streamingAssetsPath}/{EntityScenesPaths.RelativePathFolderFor(sharedBundles[i], EntityScenesPaths.PathType.EntitiesSharedReferencesBundle, -1)}";
                    bundles.Add(CreateOrRetainBundle(path));
                }
                sharedBundles.Dispose();
            }

            if (!string.IsNullOrEmpty(mainBundlePath))
            {
                bundles.Insert(0, CreateOrRetainBundle(mainBundlePath));
            }

            if (blocking)
            {
                foreach (var b in bundles)
                {
                    var forceLoad = b.AssetBundle;
                    if (forceLoad == null)
                    {
                        Debug.LogWarning($"Failed to load asset bundle at path {b._bundlePath}");
                    }
                }
            }
            return bundles;
        }

        internal static bool CheckLoadingStatus(List<SceneBundleHandle> bundles, ref string error)
        {
            if (bundles == null)
                return true;

            foreach (var b in bundles)
            {
                if (!b.IsReady())
                    return false;
                if (b.AssetBundle == null)
                {
                    error = $"Failed to load asset bundle at path {b._bundlePath}";
                    return true;
                }
            }
            return true;
        }

        static SceneBundleHandle CreateOrRetainBundle(string bundlePath)
        {
            if (bundlePath == null)
                throw new InvalidOperationException("Bundle Path is null!");

            // First Check if we have it loaded
            if (!LoadedBundles.TryGetValue(bundlePath, out var assetBundleHandle))
            {
                // Check if it's about to be unloaded
                if (!UnloadingBundles.TryRemove(bundlePath, out assetBundleHandle))
                    assetBundleHandle = new SceneBundleHandle(bundlePath);

                LoadedBundles[bundlePath] = assetBundleHandle;
            }

            assetBundleHandle.Retain();

            return assetBundleHandle;
        }

        private static void ReleaseBundle(SceneBundleHandle sceneBundleHandle)
        {
            var bundlePath = sceneBundleHandle._bundlePath;

            if (UnloadingBundles.ContainsKey(bundlePath))
                throw new InvalidOperationException($"Attempting to release a bundle that is already unloading! {bundlePath}");

            if (!LoadedBundles.ContainsKey(bundlePath))
                throw new InvalidOperationException($"Attempting to release a bundle is not contained within LoadedBundles! {bundlePath}");

            LoadedBundles.Remove(bundlePath);
            UnloadingBundles[bundlePath] = sceneBundleHandle;
        }

        internal static void ProcessUnloadingBundles()
        {
            foreach (var sceneBundleHandle in UnloadingBundles)
            {
                if (sceneBundleHandle.Value.IsReady())
                {
                    sceneBundleHandle.Value.AssetBundle?.Unload(true);

                    UnloadingBundles.TryRemove(sceneBundleHandle.Key, out _);
                }
            }
        }

        internal static int GetLoadedCount()
        {
            return LoadedBundles.Count;
        }

        internal static int GetUnloadingCount()
        {
            return UnloadingBundles.Count;
        }
    }
}
#endif
