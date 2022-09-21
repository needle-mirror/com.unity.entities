//#define LOG_BUNDLES

#if !UNITY_DOTSRUNTIME
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Unity.Collections;
using UnityEngine;

namespace Unity.Scenes
{
    internal class SceneBundleHandle
    {
        private int _refCount;
        private AssetBundleCreateRequest _assetBundleCreateRequest;
        private AssetBundleUnloadOperation _assetBundleUnloadOperation;
        private AssetBundle _assetBundle;
        private readonly string _bundlePath;

        internal AssetBundle AssetBundle
        {
            get
            {
                if (_assetBundle == null)
                {
                    if (_assetBundleUnloadOperation != null)
                    {
                        _assetBundleUnloadOperation.WaitForCompletion();
                        LoadAssetBundle();
                    }

                    if (_assetBundleCreateRequest != null)
                    {
                        _assetBundle = _assetBundleCreateRequest.assetBundle;
                        _assetBundleCreateRequest = null;
                    }
                }

                return _assetBundle;
            }
        }

        private void LoadAssetBundle()
        {
            _assetBundleCreateRequest = AssetBundle.LoadFromFileAsync(_bundlePath);
            if (_assetBundleCreateRequest == null)
                LogBundle($"Failed AssetBundle.LoadFromFileAsync {_bundlePath}");
            else
                LogBundle($"AssetBundle.LoadFromFileAsync {_bundlePath}");
        }

        private SceneBundleHandle(string bundlePath)
        {
            _refCount = 0;
            _bundlePath = bundlePath;
            LoadAssetBundle();
        }

        private bool IsReady()
        {
            if (_assetBundleCreateRequest != null)
            {
                if (!_assetBundleCreateRequest.isDone)
                    return false;

                _assetBundle = _assetBundleCreateRequest.assetBundle;
                _assetBundleCreateRequest = null;
            }

            if (_assetBundleUnloadOperation != null)
                return false;

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
        /// <summary>
        /// The bundles that are currently loaded or still in the process of loading.
        /// </summary>
        private static readonly Dictionary<string, SceneBundleHandle> LoadedBundles = new Dictionary<string, SceneBundleHandle>();
        /// <summary>
        /// The bundles that need to be unloaded.
        /// </summary>
        private static readonly ConcurrentDictionary<string, SceneBundleHandle> BundlesToUnload = new ConcurrentDictionary<string, SceneBundleHandle>();
        private static int s_NumBundlesToUnload;
        /// <summary>
        /// The bundles we're currently unloading.
        /// </summary>
        private static readonly Dictionary<string, SceneBundleHandle> UnloadingBundlesInProcess = new Dictionary<string, SceneBundleHandle>();
        internal static SceneBundleHandle[] LoadSceneBundles(string mainBundlePath, NativeArray<Entities.Hash128> sharedBundles, bool blocking)
        {
            var hasMainBundle = !string.IsNullOrEmpty(mainBundlePath);
            var bundles = new SceneBundleHandle[sharedBundles.Length + (hasMainBundle ? 1 : 0)];
            if (hasMainBundle)
                LogBundle($"Request main bundle {mainBundlePath}");

            if (sharedBundles.IsCreated)
            {
                for (int i = 0; i < sharedBundles.Length; i++)
                {
                    var path = $"{Application.streamingAssetsPath}/{EntityScenesPaths.RelativePathFolderFor(sharedBundles[i], EntityScenesPaths.PathType.EntitiesSharedReferencesBundle, -1)}";
                    LogBundle($"Request dependency {mainBundlePath}");
                    bundles[i + 1] = CreateOrRetainBundle(path);
                }
            }

            if (hasMainBundle)
                bundles[0] = CreateOrRetainBundle(mainBundlePath);

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

        internal static bool CheckLoadingStatus(SceneBundleHandle[] bundles, ref string error)
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
                if (!BundlesToUnload.TryRemove(bundlePath, out assetBundleHandle))
                {
                    // Check if it's already unloading
                    if (!UnloadingBundlesInProcess.TryGetValue(bundlePath, out assetBundleHandle))
                        assetBundleHandle = new SceneBundleHandle(bundlePath);
                }
                else
                    Interlocked.Decrement(ref s_NumBundlesToUnload);

                LoadedBundles[bundlePath] = assetBundleHandle;
            }

            assetBundleHandle.Retain();

            return assetBundleHandle;
        }

        private static void ReleaseBundle(SceneBundleHandle sceneBundleHandle)
        {
            var bundlePath = sceneBundleHandle._bundlePath;

            if (UnloadingBundlesInProcess.ContainsKey(bundlePath))
            {
                if (!LoadedBundles.Remove(bundlePath))
                    throw new InvalidOperationException($"Attempting to release a bundle is not contained within LoadedBundles! {bundlePath}");
                return;
            }

            if (!BundlesToUnload.TryAdd(bundlePath, sceneBundleHandle))
                throw new InvalidOperationException($"Attempting to release a bundle that is already unloading! {bundlePath}");
            Interlocked.Increment(ref s_NumBundlesToUnload);

            if (!LoadedBundles.Remove(bundlePath))
                throw new InvalidOperationException($"Attempting to release a bundle is not contained within LoadedBundles! {bundlePath}");
        }

        private static readonly List<string> s_BundlesFinishedUnloading = new List<string>();
        internal static void ProcessUnloadingBundles()
        {
            foreach (var kvp in UnloadingBundlesInProcess) {
                if (kvp.Value._assetBundleUnloadOperation.isDone)
                    s_BundlesFinishedUnloading.Add(kvp.Key);
            }

            if (s_BundlesFinishedUnloading.Count > 0)
            {
                foreach (var path in s_BundlesFinishedUnloading)
                {
                    UnloadingBundlesInProcess.Remove(path, out var handle);
                    // It may happen that while we were unloading this bundle, someone else requested this to be loaded.
                    // They will already have added it to the LoadedBundles dictionary
                    handle._assetBundleUnloadOperation = null;
                    if (handle._refCount > 0)
                        handle.LoadAssetBundle();
                }
                s_BundlesFinishedUnloading.Clear();
            }

            if (s_NumBundlesToUnload == 0)
                return;
            foreach (var kvp in BundlesToUnload)
            {
                SceneBundleHandle sceneBundleHandle = kvp.Value;
                if (sceneBundleHandle.IsReady())
                {
                    LogBundle($"Unload {kvp.Value}");

                    if (sceneBundleHandle.AssetBundle != null)
                    {
                        sceneBundleHandle._assetBundleUnloadOperation = sceneBundleHandle.AssetBundle.UnloadAsync(true);
                        UnloadingBundlesInProcess.Add(kvp.Key, kvp.Value);
                    }

                    if (BundlesToUnload.TryRemove(kvp.Key, out _))
                        Interlocked.Decrement(ref s_NumBundlesToUnload);
                }
            }
        }

        [Conditional("LOG_BUNDLES")]
        private static void LogBundle(string s)
        {
            Console.WriteLine(s);
        }

        internal static int GetLoadedCount()
        {
            return LoadedBundles.Count;
        }

        internal static int GetUnloadingCount()
        {
            return BundlesToUnload.Count;
        }
    }
}
#endif
