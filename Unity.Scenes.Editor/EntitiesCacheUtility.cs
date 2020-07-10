using System.IO;
using Unity.Scenes.Editor;
using UnityEditor;
using UnityEngine;

namespace Unity.Scenes
{
#if UNITY_EDITOR
    internal static class EntitiesCacheUtility
    {
        internal const string globalEntitiesDependencyDir = "Assets/GlobalEntitiesDependencies/";
        internal const string globalEntitySceneDependencyPath = globalEntitiesDependencyDir + "GlobalEntitySceneDependency.asset";
        internal const string globalLiveLinkAssetDependencyPath = globalEntitiesDependencyDir + "GlobalLiveLinkAssetDependency.asset";

        internal static GlobalEntitiesDependency entitySceneDependency;
        internal static GlobalEntitiesDependency liveLinkAssetDependency;

        internal static void UpdateEntitySceneGlobalDependency()
        {
            if (entitySceneDependency == null)
                LoadOrCreateGlobalEntitySceneDependency();

            entitySceneDependency.cacheGUID = GUID.Generate();
            EditorUtility.SetDirty(entitySceneDependency);
            AssetDatabase.SaveAssets();
        }

        static void LoadOrCreateGlobalEntitySceneDependency()
        {
            entitySceneDependency = AssetDatabase.LoadAssetAtPath<GlobalEntitiesDependency>(globalEntitySceneDependencyPath);

            if (entitySceneDependency == null)
            {
                entitySceneDependency = ScriptableObject.CreateInstance<GlobalEntitiesDependency>();
                Directory.CreateDirectory(globalEntitiesDependencyDir);
                AssetDatabase.CreateAsset(entitySceneDependency, globalEntitySceneDependencyPath);
            }
        }

        internal static void UpdateLiveLinkAssetGlobalDependency()
        {
            if (liveLinkAssetDependency == null)
                LoadOrCreateGlobalLiveLinkAssetDependency();

            liveLinkAssetDependency.cacheGUID = GUID.Generate();
            EditorUtility.SetDirty(liveLinkAssetDependency);
            AssetDatabase.SaveAssets();
        }

        static void LoadOrCreateGlobalLiveLinkAssetDependency()
        {
            liveLinkAssetDependency = AssetDatabase.LoadAssetAtPath<GlobalEntitiesDependency>(globalLiveLinkAssetDependencyPath);

            if (liveLinkAssetDependency == null)
            {
                liveLinkAssetDependency = ScriptableObject.CreateInstance<GlobalEntitiesDependency>();
                Directory.CreateDirectory(globalEntitiesDependencyDir);
                AssetDatabase.CreateAsset(liveLinkAssetDependency, globalLiveLinkAssetDependencyPath);
            }
        }

        [MenuItem("DOTS/Clear Entities Cache(s)", false, 1000)]
        static void ClearEntitiesCache()
        {
            ClearEntitiesCacheWindow.OpenWindow();
        }
    }
    #endif
}
