using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    class EntityScenesPaths
    {
        public static Type SubSceneImporterType = null;
        
        public enum PathType
        {
            EntitiesUnityObjectReferences,
            EntitiesUnityObjectRefGuids,
            EntitiesUnitObjectReferencesBundle,
            EntitiesBinary,
            EntitiesConversionLog,
            EntitiesHeader
        }

        public static string GetExtension(PathType pathType)
        {
            switch (pathType)
            {
                // these must all be lowercase
                case PathType.EntitiesUnityObjectReferences: return "asset";
                case PathType.EntitiesUnityObjectRefGuids: return "refguids";
                case PathType.EntitiesUnitObjectReferencesBundle: return "bundle";
                case PathType.EntitiesBinary : return "entities";
                case PathType.EntitiesHeader : return "entityheader";
                case PathType.EntitiesConversionLog : return "conversionlog";
            }

            throw new System.ArgumentException("Unknown PathType");
        }
        
#if UNITY_EDITOR
        public struct SceneWithBuildConfigurationGUIDs
        {
            public Hash128 SceneGUID;
            public Hash128 BuildConfiguration;
        }

        static HashSet<Hash128> s_BuildConfigurationCreated = new HashSet<Hash128>();
        static Dictionary<Hash128, string> s_HashToString = new Dictionary<Hash128, string>();

        public static unsafe Hash128 CreateBuildConfigurationSceneFile(Hash128 sceneGUID, Hash128 buildConfigurationGUID)
        {
            var guid = ComputeBuildConfigurationGUID(sceneGUID, buildConfigurationGUID);
            var guids = new SceneWithBuildConfigurationGUIDs { SceneGUID = sceneGUID, BuildConfiguration = buildConfigurationGUID};
            var dir = "Assets/SceneDependencyCache";
            var fileName = $"{dir}/{guid}.sceneWithBuildConfiguration";
            if (!File.Exists(fileName))
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                using(var writer = new Entities.Serialization.StreamBinaryWriter(fileName))
                {
                    writer.WriteBytes(&guids, sizeof(SceneWithBuildConfigurationGUIDs));
                }
                File.WriteAllText(fileName + ".meta",
                    $"fileFormatVersion: 2\nguid: {guid}\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n");
            
                // Refresh is necessary because it appears the asset pipeline
                // can't depend on an asset on disk that has not yet been refreshed.
                AssetDatabase.Refresh();
            }
            return guid;
        }

        static unsafe Hash128 ComputeBuildConfigurationGUID(Hash128 sceneGUID, Hash128 buildConfigurationGUID)
        {
            var guids = new SceneWithBuildConfigurationGUIDs { SceneGUID = sceneGUID, BuildConfiguration = buildConfigurationGUID};
            Hash128 guid;
            guid.Value.x = math.hash(&guids, sizeof(SceneWithBuildConfigurationGUIDs));
            guid.Value.y = math.hash(&guids, sizeof(SceneWithBuildConfigurationGUIDs), 0x96a755e2);
            guid.Value.z = math.hash(&guids, sizeof(SceneWithBuildConfigurationGUIDs), 0x4e936206);
            guid.Value.w = math.hash(&guids, sizeof(SceneWithBuildConfigurationGUIDs), 0xac602639);
            return guid;
        }

        public static Hash128 GetSubSceneArtifactHash(Hash128 sceneGUID, Hash128 buildConfigurationGUID, UnityEditor.Experimental.AssetDatabaseExperimental.ImportSyncMode syncMode)
        {
            var guid = ComputeBuildConfigurationGUID(sceneGUID, buildConfigurationGUID);
            if (s_BuildConfigurationCreated.Add(guid))
            {
                CreateBuildConfigurationSceneFile(sceneGUID, buildConfigurationGUID);
            }
            if (!s_HashToString.TryGetValue(guid, out var guidString))
                guidString = s_HashToString[guid] = guid.ToString();
            var res = UnityEditor.Experimental.AssetDatabaseExperimental.GetArtifactHash(guidString, SubSceneImporterType, syncMode);
            return res;
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
            var extension = GetExtension(type);
            if (type == PathType.EntitiesBinary)
                return $"{Application.streamingAssetsPath}/SubScenes/{sceneGUID}.{sectionIndex}.{extension}";
            else if (type == PathType.EntitiesHeader)
                return $"{Application.streamingAssetsPath}/SubScenes/{sceneGUID}.{extension}";
            else if (type == PathType.EntitiesUnityObjectReferences)
                return $"{Application.streamingAssetsPath}/SubScenes/{sceneGUID}.{sectionIndex}.bundle";
            else
                return "";
        }

        public static string GetLiveLinkCachePath(UnityEngine.Hash128 targetHash, PathType type, int sectionIndex)
        {
            var extension = GetExtension(type);
            if (type == PathType.EntitiesBinary)
                return $"{Application.persistentDataPath}/{targetHash}.{sectionIndex}.{extension}";
            else if (type == PathType.EntitiesHeader)
                return $"{Application.persistentDataPath}/{targetHash}.{extension}";
            else if (type == PathType.EntitiesUnityObjectRefGuids)
                return $"{Application.persistentDataPath}/{targetHash}.{sectionIndex}.{extension}";
            else if (type == PathType.EntitiesUnityObjectReferences)
                return $"{Application.persistentDataPath}/{targetHash}.{sectionIndex}.{extension}";
            else if (type == PathType.EntitiesUnitObjectReferencesBundle)
                return $"{Application.persistentDataPath}/{targetHash}.{sectionIndex}.{extension}";
            else
                return "";
        }

        public static string ComposeLiveLinkCachePath(string fileName)
        {
            return $"{Application.persistentDataPath}/{fileName}";
        }

        public static int GetSectionIndexFromPath(string path)
        {
            var components = Path.GetFileNameWithoutExtension(path).Split('.');
            if (components.Length == 1)
                return 0;
            return int.Parse(components[1]);
        }
    }
}
