using System.Collections.Generic;
using NUnit.Framework;
using Unity.Build;
using Unity.Build.Common;
using UnityEditor;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes.Hybrid.Tests
{
    public abstract class SubSceneTestFixture
    {
        Hash128 m_SceneGUID;

        public Hash128 SceneGUID => m_SceneGUID;

#if UNITY_EDITOR
        string m_SubScenePath;
        GUID m_BuildConfigurationGUID;
        string m_SceneWithBuildSettingsPath;

        static string m_TempPath = "Assets/Temp";
        static string m_BuildConfigPath = $"{m_TempPath}/BuildConfig.buildconfiguration";
#endif

        public SubSceneTestFixture(string subScenePath)
        {
            m_SubScenePath = subScenePath;
        }

        [OneTimeSetUp]
        public void SetUpOnce()
        {
#if UNITY_EDITOR
            try
            {
                BuildConfiguration.CreateAsset(m_BuildConfigPath, config =>
                {
                    config.SetComponent(new SceneList
                    {
                        SceneInfos = new List<SceneList.SceneInfo>
                        {
                            new SceneList.SceneInfo
                            {
                                Scene = GlobalObjectId.GetGlobalObjectIdSlow(
                                    AssetDatabase.LoadAssetAtPath<SceneAsset>(m_SubScenePath))
                            }
                        }
                    });
                });
                m_BuildConfigurationGUID = new GUID(AssetDatabase.AssetPathToGUID(m_BuildConfigPath));
                m_SceneGUID = new GUID(AssetDatabase.AssetPathToGUID(m_SubScenePath));

                var guid = SceneWithBuildConfigurationGUIDs.EnsureExistsFor(m_SceneGUID, m_BuildConfigurationGUID,
                    out var requestRefresh);
                if (requestRefresh)
                    AssetDatabase.Refresh();
                m_SceneWithBuildSettingsPath = SceneWithBuildConfigurationGUIDs.GetSceneWithBuildSettingsPath(ref guid);
                EntityScenesPaths.GetSubSceneArtifactHash(m_SceneGUID, m_BuildConfigurationGUID,
                    ImportMode.Synchronous);
            }
            catch
            {
                AssetDatabase.DeleteAsset(m_TempPath);
                AssetDatabase.DeleteAsset(m_SceneWithBuildSettingsPath);
                throw;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
#else
            //TODO: Playmode test not supported yet
            m_SceneGUID = new Unity.Entities.Hash128();
#endif
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
#if UNITY_EDITOR
            AssetDatabase.DeleteAsset(m_TempPath);
            AssetDatabase.DeleteAsset(m_SceneWithBuildSettingsPath);
#endif
        }
    }
}
