#if !UNITY_DISABLE_MANAGED_COMPONENTS
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Entities.Build;
using Unity.Entities.Content;
using Unity.Entities.Serialization;
using Unity.Entities.Tests;
using Unity.Scenes.Editor;
using Unity.Scenes.Editor.Tests;
using UnityEditor;
using UnityEditor.Experimental;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes.Hybrid.Tests.Editmode.Content
{
    public class RuntimeContentManagerDeterminismTests
    {
        private static string MainScenePath => $"{TestAssetFolder}/RuntimeContentManagerTests.unity";
        private static string GOScenePath => $"{TestAssetFolder}/goScene.unity";
        private static string TestAssetFolder => $"Assets/{TestAssetFolderName}";
        private static string TestAssetFolderName => nameof(RuntimeContentManagerTests);
        private static string TestStreamingAssetsFolderName => $"Assets/StreamingAssets/{nameof(RuntimeContentManagerTests)}";
        private static string GetAssetPath(string assetName)
        {
            return $"{TestAssetFolder}/{assetName}";
        }
        private static string DeleteStreamingAssetsFolder => GetSessionStateKey("DeleteStreamingAssetsFolder");
        private static string RefObjRuntimeId => GetSessionStateKey("RefObjRuntimeId");
        private static string DirectObjRuntimeId => GetSessionStateKey("DirectObjRuntimeId");
        Unity.Entities.Hash128 m_SubsceneGUID;
        private static string GetSessionStateKey(string name)
        {
            return $"{TestAssetFolder}.{name}";
        }

        private static string[] kDirectAssetPaths =
        {
            GetAssetPath("VertexLitDirect0.mat"),
            GetAssetPath("VertexLitDirect1.mat"),
            GetAssetPath("VertexLitDirect2.mat"),
            GetAssetPath("VertexLitDirect3.mat")
        };

        private static string[] kRefAssetPaths =
        {
            GetAssetPath("VertexLitRef0.mat"),
            GetAssetPath("VertexLitRef1.mat"),
            GetAssetPath("VertexLitRef2.mat"),
            GetAssetPath("VertexLitRef3.mat")
        };

        private List<string> BuildContentArchives(
          HashSet<Hash128> subSceneGuids, string outputPath)
        {
            var artifactKeys = new Dictionary<Hash128, ArtifactKey>();
            var settingsGuid = DotsGlobalSettings.Instance.GetClientGUID();

            Directory.CreateDirectory(outputPath);

            EntitySceneBuildUtility.PrepareEntityBinaryArtifacts(
                settingsGuid, subSceneGuids, artifactKeys);

            // Verify artifacts were produced
            if (artifactKeys.Count == 0)
            {
                throw new System.Exception($"Failed to produce artifacts for subscene. The subscene may have import errors.");
            }

            var registeredFiles = new List<string>();
            var buildTarget = EditorUserBuildSettings.activeBuildTarget;

            EntitySceneBuildUtility.PrepareAdditionalFiles(
                default,
                artifactKeys.Keys.ToArray(),
                artifactKeys.Values.ToArray(),
                buildTarget,
                (src, dst) => {
                    var fullDst = Path.Combine(outputPath, dst);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullDst));
                    File.Copy(src, fullDst, true);
                    registeredFiles.Add(fullDst);
                });

            return registeredFiles;
        }

        private void ClearSceneDependencyCache()
        {
            SceneWithBuildConfigurationGUIDs.ClearBuildSettingsCache();
        }

        [SetUp]
        public void SetUp()
        {
            // Force AssetDatabase to refresh and clear any cached GUID conflicts
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            if (!AssetDatabase.IsValidFolder("Assets/StreamingAssets"))
                SessionState.SetString(DeleteStreamingAssetsFolder, "true");
            if (AssetDatabase.IsValidFolder(TestAssetFolder))
                AssetDatabase.DeleteAsset(TestAssetFolder);
            AssetDatabase.CreateFolder("Assets", TestAssetFolderName);

            Shader testShader = Shader.Find("Unlit/Color");

            for (int i = 0; i < kRefAssetPaths.Length; i++)
            {
                Material mat1 = new Material(testShader);
                mat1.color = new Color(1, i / (float)kRefAssetPaths.Length, 0);
                AssetDatabase.CreateAsset(mat1, kRefAssetPaths[i]);
            }
            for (int i = 0; i < kDirectAssetPaths.Length; i++)
            {
                Material mat1 = new Material(testShader);
                mat1.color = new Color(1, i / (float)kDirectAssetPaths.Length, 0);
                AssetDatabase.CreateAsset(mat1, kDirectAssetPaths[i]);
            }
            var goScene = SubSceneTestsHelper.CreateScene(GOScenePath);
            var goSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(GOScenePath);

            var mainScene = SubSceneTestsHelper.CreateScene(MainScenePath);

            // Create SubScene with WeakObjectReference
            SubScene refSubScene = SubSceneTestsHelper.CreateSubSceneInSceneFromObjects("RefSubScene", true, mainScene, () =>
            {
                var gos = new List<GameObject>();
                foreach (var p in kDirectAssetPaths)
                {
                    var go1 = new GameObject("MaterialRefObject");
                    var comp1 = go1.AddComponent<WeakMaterialRefComponentAuthoring>();
                    var matObj1 = AssetDatabase.LoadAssetAtPath<Material>(p);
                    WeakObjectReference<Material> materialRef = new WeakObjectReference<Material>();
                    materialRef.Id = UntypedWeakReferenceId.CreateFromObjectInstance(matObj1);
                    comp1.matRef = materialRef;
                    comp1.sceneRef = new WeakObjectSceneReference { Id = UntypedWeakReferenceId.CreateFromObjectInstance(goSceneAsset) };
                    SessionState.SetString(RefObjRuntimeId, materialRef.Id.ToString());
                    gos.Add(go1);
                }
                return gos;
            });
            m_SubsceneGUID = refSubScene.SceneGUID;
        }

        [TearDown]
        public void TearDown()
        {
            SessionState.EraseString(RefObjRuntimeId);
            SessionState.EraseString(DirectObjRuntimeId);
            if (SessionState.GetString(DeleteStreamingAssetsFolder, "") == "true")
            {
                //if we created Streaming assets folder, delete it
                AssetDatabase.DeleteAsset("Assets/StreamingAssets");
                SessionState.EraseString(DeleteStreamingAssetsFolder);
            }
            else
            {
                //otherwise just delete our subfolder
                AssetDatabase.DeleteAsset(TestStreamingAssetsFolderName);
            }

            AssetDatabase.DeleteAsset(TestAssetFolder);
        }

        [Test]
        [Explicit]
        public void Check_ClearEntityCache_Keeps_RuntimeContentFilePath_Is_Deterministic()
        {
            // Force AssetDatabase refresh
            AssetDatabase.Refresh();

            // Build the subscene
            var subSceneGuids = new HashSet<Hash128>();
            subSceneGuids.Add(m_SubsceneGUID);
            var oldRegisteredFiles = BuildContentArchives(subSceneGuids, Path.Combine(TestStreamingAssetsFolderName, "1"));

            // Clear the entity cache to force a subscene reimport
            ClearSceneDependencyCache();

            // Rebuild the subscene with a fresh HashSet (BuildContentArchives modifies the input set)
            subSceneGuids = new HashSet<Hash128>();
            subSceneGuids.Add(m_SubsceneGUID);
            var newRegisteredFiles = BuildContentArchives(subSceneGuids, Path.Combine(TestStreamingAssetsFolderName, "2"));

            // Get just the filenames (without paths)
            oldRegisteredFiles = oldRegisteredFiles.Select(Path.GetFileName).OrderBy(x => x).ToList();
            newRegisteredFiles = newRegisteredFiles.Select(Path.GetFileName).OrderBy(x => x).ToList();

            //Check the number of files is the same
            Assert.IsTrue(oldRegisteredFiles.Count == newRegisteredFiles.Count,
                $"Expected {oldRegisteredFiles.Count} files, but got {newRegisteredFiles.Count}");

            // Checks the name of the files are the same
            for (int i = 0; i < oldRegisteredFiles.Count; i++)
            {
                Assert.IsTrue(oldRegisteredFiles[i] == newRegisteredFiles[i]);
            }
        }

        [Test]
        [Explicit]
        public void Check_ClearEntityCache_Keeps_RuntimeContentFileContent_Is_Deterministic()
        {
            // Force AssetDatabase refresh
            AssetDatabase.Refresh();

            // Build the subscene
            var subSceneGuids = new HashSet<Hash128>();
            subSceneGuids.Add(m_SubsceneGUID);
            var oldRegisteredFiles = BuildContentArchives(subSceneGuids, Path.Combine(TestStreamingAssetsFolderName, "1"));

            // Clear the entity cache to force a subscene reimport
            ClearSceneDependencyCache();

            // Rebuild the subscene with a fresh HashSet (BuildContentArchives modifies the input set)
            subSceneGuids = new HashSet<Hash128>();
            subSceneGuids.Add(m_SubsceneGUID);
            var newRegisteredFiles = BuildContentArchives(subSceneGuids, Path.Combine(TestStreamingAssetsFolderName, "2"));

            // Check the number of files is the same
            Assert.IsTrue(oldRegisteredFiles.Count == newRegisteredFiles.Count,
                $"Expected {oldRegisteredFiles.Count} files, but got {newRegisteredFiles.Count}");

            // Create a dictionary of filename -> full path for the old files
            var oldFileMap = new Dictionary<string, string>();
            foreach (var filePath in oldRegisteredFiles)
            {
                var fileName = Path.GetFileName(filePath);
                oldFileMap[fileName] = filePath;
            }

            // Compare each new file with its corresponding old file
            foreach (var newFilePath in newRegisteredFiles)
            {
                var fileName = Path.GetFileName(newFilePath);
                Assert.IsTrue(oldFileMap.ContainsKey(fileName), $"File {fileName} should exist in both builds");

                var oldFilePath = oldFileMap[fileName];
                var oldBytes = File.ReadAllBytes(oldFilePath);
                var newBytes = File.ReadAllBytes(newFilePath);

                Assert.IsTrue(oldBytes.Length == newBytes.Length,
                    $"File {fileName} has different size. Old: {oldBytes.Length} bytes, New: {newBytes.Length} bytes");

                Assert.IsTrue(oldBytes.SequenceEqual(newBytes),
                    $"File {fileName} has different content after clearing cache. This indicates non-deterministic build output.");
            }
        }

        [Test]
        [Explicit]
        public void Check_ModifyingAsset_ProducesDifferentRuntimeContent()
        {
            // Force AssetDatabase refresh
            AssetDatabase.Refresh();

            // Build the subscene with original assets
            var subSceneGuids = new HashSet<Hash128>();
            subSceneGuids.Add(m_SubsceneGUID);
            var oldRegisteredFiles = BuildContentArchives(subSceneGuids, Path.Combine(TestStreamingAssetsFolderName, "1"));

            // Modify one of the material assets
            var materialPath = kDirectAssetPaths[0];
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Assert.IsNotNull(material, $"Failed to load material at {materialPath}");

            // Change the material color
            var originalColor = material.color;
            material.color = new Color(0, 1, 0); // Change to green
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Rebuild the subscene with modified asset
            var subSceneGuids2 = new HashSet<Hash128>();
            subSceneGuids2.Add(m_SubsceneGUID);
            var newRegisteredFiles = BuildContentArchives(subSceneGuids2, Path.Combine(TestStreamingAssetsFolderName, "2"));

            // Restore original color for cleanup
            material.color = originalColor;
            AssetDatabase.SaveAssets();

            // Check the number of files is the same
            Assert.IsTrue(oldRegisteredFiles.Count == newRegisteredFiles.Count,
                $"Expected {oldRegisteredFiles.Count} files, but got {newRegisteredFiles.Count}");

            // Create a dictionary of filename -> full path for the old files
            var oldFileMap = new Dictionary<string, string>();
            foreach (var filePath in oldRegisteredFiles)
            {
                var fileName = Path.GetFileName(filePath);
                oldFileMap[fileName] = filePath;
            }

            // Track if we found any differences
            bool foundDifference = false;

            // Compare each new file with its corresponding old file
            foreach (var newFilePath in newRegisteredFiles)
            {
                var fileName = Path.GetFileName(newFilePath);
                Assert.IsTrue(oldFileMap.ContainsKey(fileName), $"File {fileName} should exist in both builds");

                var oldFilePath = oldFileMap[fileName];
                var oldBytes = File.ReadAllBytes(oldFilePath);
                var newBytes = File.ReadAllBytes(newFilePath);

                // Check if this file has different content
                if (!oldBytes.SequenceEqual(newBytes))
                {
                    foundDifference = true;
                    break;
                }
            }

            Assert.IsTrue(foundDifference,
                "Modifying an asset should produce different runtime content archives, but all files were identical.");
        }
    }
}
#endif
