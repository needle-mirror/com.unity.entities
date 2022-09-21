using System.Collections.Generic;
using System.IO;
using UnityEditor;
using NUnit.Framework;
using Unity.Entities.Build;
using Unity.Entities.Conversion;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Scenes.Editor.Tests
{
    public class DotsGlobalSettingsTests
    {
        private bool m_PreviousBuiltInEnabledOption;
        TestWithTempAssets m_Assets;

        [SetUp]
        public void Setup()
        {
            m_PreviousBuiltInEnabledOption = LiveConversionSettings.IsBuiltinBuildsEnabled;
            LiveConversionSettings.IsBuiltinBuildsEnabled = true;
            m_Assets.SetUp();
        }

        [TearDown]
        public void Teardown()
        {
            LiveConversionSettings.IsBuiltinBuildsEnabled = m_PreviousBuiltInEnabledOption;
            m_Assets.TearDown();
        }

        [Test]
        public void SuccessfulPlayerBuildTest()
        {
            var scene = SubSceneTestsHelper.CreateScene(m_Assets.GetNextPath() + ".unity");
            SceneManager.SetActiveScene(scene);

            var subScene = SubSceneTestsHelper.CreateSubSceneInSceneFromObjects("SubScene", true, scene, () =>
            {
                var go = new GameObject();
                return new List<GameObject> {go};
            });

            var buildOptions = new BuildPlayerOptions
            {
                target = EditorUserBuildSettings.activeBuildTarget,
                subtarget = 0,
                scenes = new[] {scene.path}
            };

            var uniqueTempPath = FileUtil.GetUniqueTempPathInProject();
            buildOptions.locationPathName = uniqueTempPath + "/Test.exe";
            bool isOSXEditor = Application.platform == RuntimePlatform.OSXEditor;
            if(isOSXEditor)
                buildOptions.locationPathName = uniqueTempPath + "/Test.app";
            buildOptions.options = BuildOptions.Development;
            DotsGlobalSettings.Instance.SetPlayerType(DotsGlobalSettings.PlayerType.Client);

            var report = BuildPipeline.BuildPlayer(buildOptions);

            var locationPath = Application.dataPath + "/../" + uniqueTempPath;
            var streamingAssetPath = locationPath + "/Test_Data/StreamingAssets/";
            if(isOSXEditor)
                streamingAssetPath = locationPath  + $"/Test.app/Contents/Resources/Data/StreamingAssets/";
            var subSceneGuid = subScene.SceneGUID;

            Assert.IsTrue(File.Exists(streamingAssetPath + EntityScenesPaths.k_SceneInfoFileName));
            Assert.IsTrue(File.Exists(streamingAssetPath + "EntityScenes/" + subSceneGuid + ".entityheader"));
            Assert.IsTrue(File.Exists(streamingAssetPath + "EntityScenes/" + subSceneGuid + ".0.entities"));

            //Test the Editor asset folder doesn't contain any entities file after build
            Assert.IsTrue(!File.Exists(Application.streamingAssetsPath + EntityScenesPaths.k_SceneInfoFileName));
            Assert.IsTrue(!File.Exists(Application.streamingAssetsPath + "EntityScenes/" + subSceneGuid + ".entityheader"));
            Assert.IsTrue(!File.Exists(Application.streamingAssetsPath + "EntityScenes/" + subSceneGuid + ".0.entities"));

            Assert.IsTrue(report.summary.result == BuildResult.Succeeded);
        }
    }
}
