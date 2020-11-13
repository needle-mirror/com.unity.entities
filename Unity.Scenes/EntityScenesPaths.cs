using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Core;
#if !NET_DOTS
using System.Linq;
#endif
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    internal class EntityScenesPaths
    {
        public static Type SubSceneImporterType = null;

        internal static readonly string PersistentDataPath;
        internal static readonly string StreamingAssetsPath;

        static EntityScenesPaths()
        {
#if UNITY_DOTSRUNTIME
            PersistentDataPath = "Data";
            StreamingAssetsPath = "Data";
#elif PLATFORM_SWITCH
            // Temporary hack around using Application.persistentDataPath during static initialisation which fails on switch and crashes
            // WHY does it fail on switch? Shouldn't this just silently fail on Switch?
            // We need to find a way to not use PersistentDataPath during LiveLink. This means LiveLink doesn't work on Switch right now.
            PersistentDataPath = "DOESNOTWORK";
            StreamingAssetsPath = Application.streamingAssetsPath;
#else
            PersistentDataPath = Application.persistentDataPath;
            StreamingAssetsPath = Application.streamingAssetsPath;
#endif
        }

        public enum PathType
        {
            EntitiesUnityObjectReferences,
            EntitiesUnityObjectReferencesBundle,
            EntitiesAssetDependencyGUIDs,
            EntitiesBinary,
            EntitiesConversionLog,
            EntitiesHeader,
            EntitiesSharedReferencesBundle
        }

        public static string GetExtension(PathType pathType)
        {
            switch (pathType)
            {
                // these must all be lowercase
                case PathType.EntitiesUnityObjectReferences: return "asset";
                case PathType.EntitiesBinary: return "entities";
                case PathType.EntitiesUnityObjectReferencesBundle: return "bundle";
                case PathType.EntitiesHeader: return "entityheader";
                case PathType.EntitiesConversionLog: return "conversionlog";
                case PathType.EntitiesSharedReferencesBundle: return "bundle";
                case PathType.EntitiesAssetDependencyGUIDs: return "dependencies";
            }

            throw new ArgumentException($"Unknown PathType {pathType}");
        }

#if UNITY_EDITOR

        static Dictionary<Hash128, string> s_HashToString = new Dictionary<Hash128, string>();


        public static Hash128 GetSubSceneArtifactHash(Hash128 sceneGUID, Hash128 buildConfigurationGUID, bool isBuildingForEditor, ImportMode importMode)
        {
            var guid = SceneWithBuildConfigurationGUIDs.EnsureExistsFor(sceneGUID, buildConfigurationGUID, isBuildingForEditor, out var mustRequestRefresh);
            if (mustRequestRefresh)
                UnityEditor.AssetDatabase.Refresh();

            if (!s_HashToString.TryGetValue(guid, out var guidString))
                guidString = s_HashToString[guid] = guid.ToString();
            return AssetDatabaseCompatibility.GetArtifactHash(guidString, SubSceneImporterType, importMode);
        }

        public static string GetLoadPathFromArtifactPaths(string[] paths, PathType type, int? sectionIndex = null)
        {
            var extension = GetExtension(type);
            if (sectionIndex != null)
                extension = $"{sectionIndex}.{extension}";

            return paths.FirstOrDefault(p => p.EndsWith(extension));
        }

#endif // UNITY_EDITOR

        public static string GetLoadPath(Hash128 sceneGUID, PathType type, int sectionIndex)
        {
            return StreamingAssetsPath + "/" + RelativePathFolderFor(sceneGUID, type, sectionIndex);
        }

        public static string RelativePathFolderFor(Hash128 sceneGUID, PathType type, int sectionIndex)
        {
            var extension = GetExtension(type);
            switch (type)
            {
                case PathType.EntitiesBinary:
                    return $"SubScenes/{sceneGUID}.{sectionIndex}.{extension}";
                case PathType.EntitiesHeader:
                case PathType.EntitiesConversionLog:
                    return $"SubScenes/{sceneGUID}.{extension}";
                case PathType.EntitiesUnityObjectReferences:
                case PathType.EntitiesUnityObjectReferencesBundle:
                    return $"SubScenes/{sceneGUID}.{sectionIndex}.{extension}";
                case PathType.EntitiesSharedReferencesBundle:
                    return $"SubScenes/{sceneGUID}.{extension}";
                default:
                    throw new ArgumentException();
            }
        }

        public static string GetLiveLinkCachePath(UnityEngine.Hash128 targetHash, PathType type, int sectionIndex)
        {
            var extension = GetExtension(type);
            switch (type)
            {
                case PathType.EntitiesHeader:
                    return $"{PersistentDataPath}/{k_LiveLinkCacheDir}/{targetHash}.{extension}";
                case PathType.EntitiesBinary:
                case PathType.EntitiesUnityObjectReferences:
                case PathType.EntitiesUnityObjectReferencesBundle:
                    return $"{PersistentDataPath}/{k_LiveLinkCacheDir}/{targetHash}.{sectionIndex}.{extension}";
                default:
                    return "";
            }
        }

        const string k_LiveLinkCacheDir = "LiveLinkCache";

        public static string GetCachePath(Hash128 targetHash)
        {
            return $"{PersistentDataPath}/{k_LiveLinkCacheDir}/{targetHash}";
        }

        public static string GetTempCachePath()
        {
            return $"{PersistentDataPath}/{k_LiveLinkCacheDir}/{MakeRandomFileName()}";
        }

        static unsafe string MakeRandomFileName()
        {
            const int kFilenameLen = 16;
            var filenameBuffer = stackalloc char[kFilenameLen];
            var rng = new Mathematics.Random((uint) Time.realtimeSinceStartup);

            for(int i = 0; i < kFilenameLen; ++i)
                filenameBuffer[i] = rng.NextBool() ? (char) rng.NextInt('a', 'z') : (char) rng.NextInt('0', '9');

            return new string(filenameBuffer, 0, kFilenameLen);
        }

        public static string ComposeLiveLinkCachePath(string fileName)
        {
            return $"{PersistentDataPath}/{k_LiveLinkCacheDir}/{fileName}";
        }

        public static string GetLiveLinkCacheDirPath()
        {
            return $"{PersistentDataPath}/{k_LiveLinkCacheDir}";
        }

        public static int GetSectionIndexFromPath(string path)
        {
            var dot = new FixedString32(".");
            var localStr = new FixedString512(path);

            // Find the extension '.'
            var index = localStr.LastIndexOf(dot);
            if (index < 0) // no '.' characters so return default '0'
                return 0;

            // Found the extension dot, so null it and search for a section number '.' delimiter
            localStr[index] = 0;
            index = localStr.LastIndexOf(dot, index);
            if(index < 0)
                return 0;

            index++;
            int parsedInt = 0;
            localStr.Parse(ref index, ref parsedInt);

            return parsedInt;
        }
    }
}
