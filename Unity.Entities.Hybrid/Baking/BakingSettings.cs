using System;
using System.Collections.Generic;
using Unity.Entities.Hybrid;
using UnityEngine;
#if UNITY_EDITOR
using Unity.Build;
using UnityEditor;
using Unity.Entities.Build;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif
#endif
using UnityObject = UnityEngine.Object;

namespace Unity.Entities
{
    internal class BakingSettings
    {
        public Hash128                  SceneGUID;
        public BakingUtility.BakingFlags          BakingFlags;
        public WorldSystemFilterFlags FilterFlags = WorldSystemFilterFlags.BakingSystem;
        public List<Type>               ExtraSystems = new List<Type>();
        public List<Type>               Systems;
#if UNITY_EDITOR
        public DotsPlayerSettings DotsSettings;
        public BuildConfiguration BuildConfiguration;
        public AssetImportContext AssetImportContext;
        public GameObject PrefabRoot;
        public bool IsBuiltInBuildsEnabled;
#endif
        internal byte                     NamespaceID; // this must be internal

        public BlobAssetStore BlobAssetStore { get; protected internal set; }

        public BakingSettings() {}

        public BakingSettings(BakingUtility.BakingFlags bakingFlags, BlobAssetStore blobAssetStore)
        {
            BakingFlags = bakingFlags;
            BlobAssetStore = blobAssetStore;
        }
    }
}
