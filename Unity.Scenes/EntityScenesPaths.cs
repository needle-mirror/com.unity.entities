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
        internal static Type SubSceneImporterType = null;

        internal const string k_SceneInfoFileName = "catalog.bin";
        internal const string k_EntitySceneSubDir = "EntityScenes";

        internal static string GetSceneInfoPath(string sceneLoadDir)
        {
            return $"{sceneLoadDir}/{k_SceneInfoFileName}";
        }

        internal enum PathType
        {
            EntitiesUnityObjectReferences,
            EntitiesUnityObjectReferencesBundle,
            EntitiesAssetDependencyGUIDs,
            EntitiesBinary,
            EntitiesConversionLog,
            EntitiesHeader,
            EntitiesSharedReferencesBundle,
            EntitiesWeakAssetRefs,
            EntitiesGlobalUsage
        }

        internal static string GetExtension(PathType pathType)
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
                case PathType.EntitiesWeakAssetRefs: return "weakassetrefs";
                case PathType.EntitiesGlobalUsage: return "globalusage";
            }

            throw new ArgumentException($"Unknown PathType {pathType}");
        }

#if UNITY_EDITOR

        static Dictionary<Hash128, string> s_HashToString = new Dictionary<Hash128, string>();


        internal static Hash128 GetSubSceneArtifactHash(Hash128 sceneGUID, Hash128 buildConfigurationGUID, bool isBuildingForEditor, ImportMode importMode)
        {
            var guid = SceneWithBuildConfigurationGUIDs.EnsureExistsFor(sceneGUID, buildConfigurationGUID, isBuildingForEditor, out var mustRequestRefresh);
            if (mustRequestRefresh)
                UnityEditor.AssetDatabase.Refresh();

            if (!s_HashToString.TryGetValue(guid, out var guidString))
                guidString = s_HashToString[guid] = guid.ToString();
            return AssetDatabaseCompatibility.GetArtifactHash(guidString, SubSceneImporterType, importMode);
        }

        // This specialization is faster than calling GetLoadPathFromArtifactPaths with a null section index
        internal static string GetHeaderPathFromArtifactPaths(string[] paths)
        {
            int length = paths.Length;

            for (int i = 0; i < length; ++i)
            {
                var path = paths[i];
                if (path.EndsWith(".entityheader", StringComparison.Ordinal))
                {
                    return path;
                }
            }
            return null;
        }

        internal static string GetLoadPathFromArtifactPaths(string[] paths, PathType type)
        {
            var extension = GetExtension(type);
            return paths.FirstOrDefault(p => p.EndsWith(extension, StringComparison.Ordinal));
        }

        internal static string GetLoadPathFromArtifactPaths(string[] paths, PathType type, int sectionIndex)
        {
            var extension = $"{sectionIndex}.{GetExtension(type)}";
            return paths.FirstOrDefault(p => p.EndsWith(extension, StringComparison.Ordinal));
        }


#endif // UNITY_EDITOR

        internal static string GetLoadPath(Hash128 sceneGUID, PathType type, int sectionIndex, string sceneLoadDir)
        {
            return $"{sceneLoadDir}/{RelativePathFolderFor(sceneGUID, type, sectionIndex)}";
        }

        internal static string GetFileName(Hash128 sceneGUID, PathType type, int sectionIndex)
        {
            var extension = GetExtension(type);
            switch (type)
            {
                case PathType.EntitiesBinary:
                    return $"{sceneGUID}.{sectionIndex}.{extension}";
                case PathType.EntitiesHeader:
                case PathType.EntitiesConversionLog:
                    return $"{sceneGUID}.{extension}";
                case PathType.EntitiesUnityObjectReferences:
                case PathType.EntitiesUnityObjectReferencesBundle:
                    return $"{sceneGUID}.{sectionIndex}.{extension}";
                case PathType.EntitiesSharedReferencesBundle:
                    return $"{sceneGUID}.{extension}";
                default:
                    throw new ArgumentException();
            }
        }

        internal static string RelativePathFolderFor(Hash128 sceneGUID, PathType type, int sectionIndex)
        {
            return $"{k_EntitySceneSubDir}/{GetFileName(sceneGUID, type, sectionIndex)}";
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

        internal static int GetSectionIndexFromPath(string path)
        {
            var dot = new FixedString32Bytes(".");
            var localStr = new FixedString512Bytes(path);

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
