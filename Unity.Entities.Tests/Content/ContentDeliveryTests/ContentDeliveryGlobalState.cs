/// <summary>
/// These tests are currently disabled because the `ENABLE_CONTENT_DELIVERY` define is not set in the project settings.
/// TODO: Re-enable these tests once the issue is resolved.
/// Bug report: https://jira.unity3d.com/browse/DOTS-10674
/// </summary>
#if UNITY_EDITOR && ENABLE_CONTENT_DELIVERY
using NUnit.Framework;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Entities.Content;
using UnityEditor.SceneManagement;
using UnityEditor;
using System.Collections.Generic;
using Unity.Entities.Build;
using UnityEditor.Build;
using UnityEngine.TestTools;

namespace Unity.Entities.Tests.Content
{
    [TestFixture]
    public class ContentDeliveryTestsWithCatalog
    {
        private string _buildPath;
        private string _cachePath;
        private string _tmpBuildFolder;
        private string _tmpSceneFolder = "Assets/Scenes";
        private Scene _testScene;
        private bool _contentDownloaded;
        private float _timeOutInSeconds = 10f;

        private NamedBuildTarget NamedBuildTarget => NamedBuildTarget.Standalone;
        private string ScenePath => Path.Combine(_tmpSceneFolder, "TestScene.unity");

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string projectFolder = Directory.GetParent(Application.dataPath).FullName;
            _buildPath = Path.Combine(projectFolder, "Builds");
            _cachePath = Path.Combine(projectFolder, "ContentCache");
            _tmpBuildFolder = Path.Combine(projectFolder, $"Library/ContentUpdateBuildDir/{PlayerSettings.productName}");

            if (!Directory.Exists(_tmpBuildFolder))
            {
                Directory.CreateDirectory(_tmpBuildFolder);
            }

            if (!Directory.Exists(_cachePath))
            {
                Directory.CreateDirectory(_cachePath);
            }

            if (!Directory.Exists(_buildPath))
            {
                Directory.CreateDirectory(_buildPath);
            }

            _testScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            EditorSceneManager.SaveScene(_testScene, ScenePath);

            // Ensure that DotsGlobalSettings is properly initialized
            var instance = DotsGlobalSettings.Instance;
            var playerGuid = instance.GetPlayerType() == DotsGlobalSettings.PlayerType.Client
                ? instance.GetClientGUID()
                : instance.GetServerGUID();
            Assert.IsTrue(playerGuid.IsValid, "Invalid Player GUID");

            var sceneGuids = new HashSet<Hash128>();
            var level = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            var guid = AssetDatabase.GUIDFromAssetPath(ScenePath);
            Assert.IsFalse(guid.Empty(), $"Scene {level.name} has an empty GUID");
            sceneGuids.Add(guid);

            RemoteContentCatalogBuildUtility.BuildContent(sceneGuids, playerGuid, EditorUserBuildSettings.activeBuildTarget, _tmpBuildFolder);

            var hasBeenPublished = RemoteContentCatalogBuildUtility.PublishContent(_tmpBuildFolder, _buildPath, f => new string[] { "all" });
            Assert.IsTrue(hasBeenPublished, "Failed to publish the content catalog.");
        }

        [UnityTest]
        public IEnumerator LoadContentCatalogAndVerifyDownload_AtPlaymode()
        {
            _contentDownloaded = false;
            var localPath = SetupPathFromBuildPathLocation();
            yield return StartDownloading(localPath);
            Assert.IsTrue(_contentDownloaded, $"Content was not successfully downloaded within the time limit. Sources: {localPath}");
        }

        [UnityTest]
        public IEnumerator DoNotLoadContentCatalog_AtEditor()
        {
            _contentDownloaded = false;
            var localPath = SetupPathFromBuildPathLocation();
            yield return StartDownloadingInEditMode(localPath);
            Assert.IsFalse(_contentDownloaded, $"Content should not be successfully downloaded in Edit mode from: {localPath}");
        }

        [UnityTest]
        public IEnumerator LoadContentCatalogAndVerifyDownloadFromStreamingAssets_AtPlaymode()
        {
            _contentDownloaded = false;
            var localPath = SetupPathFromStreamingAssets();
            yield return StartDownloading("");
            Assert.IsTrue(_contentDownloaded, $"Content was not successfully downloaded within the time limit. Sources: {localPath}");
        }

        [UnityTest]
        public IEnumerator DoNotLoadContentCatalogStreamingAssets_AtEditor()
        {
            _contentDownloaded = false;
            var localPath = SetupPathFromStreamingAssets();
            yield return StartDownloadingInEditMode("");
            Assert.IsTrue(_contentDownloaded, $"Content was not successfully downloaded within the time limit. Sources: {localPath}");
        }

        /// <summary>
        /// If the path is empty or null, the system will start loading the content from StreamingAssets
        /// </summary>
        /// <param name="localPath"></param>
        /// <returns></returns>
        private IEnumerator StartDownloading(string localPath)
        {
            //Entering play mode might take some frames
            var enterPlayMode = false;
            while (!EditorApplication.isPlaying)
            {
                if (!enterPlayMode)
                {
                    enterPlayMode = true;
                    EditorApplication.EnterPlaymode();
                }

                yield return null;
            }

            Assert.IsTrue(EditorApplication.isPlaying, $"Is the editor running in play mode? {EditorApplication.isPlaying}");
            DownloadUsingLocalPath(localPath);

            // Downloading the assets takes at least four frames.
            // Sets a timeout or waits for the content to finish downloading, whichever occurs first.
            // This counts for some seconds while content is loaded due to that may take some seconds.
            float timeElapsed = 0f;
            while (!_contentDownloaded && timeElapsed < _timeOutInSeconds)
            {
                yield return null;
                timeElapsed += Time.deltaTime;
            }

            EditorApplication.ExitPlaymode();
        }

        private IEnumerator StartDownloadingInEditMode(string localPath)
        {
            DownloadUsingLocalPath(localPath);

            // Since downloading takes four frames,
            // this ensures that the download cannot be executed in the editor, even if the editor runs for a few seconds.
            // This isn't because the system is stopping and preventing the package from downloading the assets
            var maxTime = EditorApplication.timeSinceStartup + _timeOutInSeconds;
            while (!_contentDownloaded && EditorApplication.timeSinceStartup < maxTime)
            {
                yield return null;
            }
        }

        private string SetupPathFromBuildPathLocation()
        {
            string localPath = $"file://{_buildPath}/";
            Assert.IsTrue(Directory.Exists(_buildPath), $"build path: [{_buildPath}] does not exist");
            Assert.IsTrue(Directory.Exists(_cachePath), $"cache path: [{_cachePath}] does not exist");
            Assert.IsTrue(File.Exists(Path.Combine(_buildPath, "catalogs.bin")), "catalogs.bin does not exist");
            return localPath;
        }

        private string SetupPathFromStreamingAssets()
        {
            string localPath = Application.streamingAssetsPath;

            if (!Directory.Exists(localPath))
            {
                Directory.CreateDirectory(localPath);
            }

            // Copy files from the build folder (_buildPath) to the StreamingAssets folder (localPath)
            if (Directory.Exists(_buildPath))
            {
                foreach (string filePath in Directory.GetFiles(_buildPath))
                {
                    string fileName = Path.GetFileName(filePath);
                    string destFilePath = Path.Combine(localPath, fileName);
                    File.Copy(filePath, destFilePath, true);
                }
            }

            Assert.IsTrue(Directory.Exists(localPath), $"build path: [{localPath}] does not exist");
            Assert.IsTrue(Directory.Exists(_cachePath), $"cache path: [{_cachePath}] does not exist");
            Assert.IsTrue(File.Exists(Path.Combine(localPath, "catalogs.bin")), "catalogs.bin does not exist");
            return localPath;
        }

        private void DownloadUsingLocalPath(string localPath)
        {
            // This starts downloading content from {localPath} and stores it in the cache path: {_cachePath}
            ContentDeliveryGlobalState.Initialize(localPath, _cachePath, "all", s =>
            {
                if (s >= ContentDeliveryGlobalState.ContentUpdateState.ContentReady)
                {
                    _contentDownloaded = true;
                }
            });
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (File.Exists(ScenePath))
            {
                AssetDatabase.DeleteAsset(ScenePath);
            }

            if (Directory.Exists(Application.streamingAssetsPath))
            {
                Directory.Delete(Application.streamingAssetsPath, true);
            }

            if (Directory.Exists(_tmpBuildFolder))
            {
                Directory.Delete(_tmpBuildFolder, true);
            }

            if (Directory.Exists(_buildPath))
            {
                Directory.Delete(_buildPath, true);
            }

            if (Directory.Exists(_cachePath))
            {
                Directory.Delete(_cachePath, true);
            }
        }
    }
}
#endif
