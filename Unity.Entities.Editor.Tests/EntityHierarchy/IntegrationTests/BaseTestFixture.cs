using System;
using System.IO;
using NUnit.Framework;
using Unity.Scenes;
using Unity.Scenes.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.Entities.Editor.Tests
{
    abstract class BaseTestFixture
    {
        const string k_AssetsFolderRoot = "Assets";
        protected const string k_SceneExtension = "unity";

        bool m_PreviousLiveLinkState;

        protected string TestAssetsDirectory { get; private set; }

        protected virtual string SceneName { get; } = "TestScene";
        protected virtual string SubSceneName { get; } = "SubScene";
        protected GameObject SubSceneRoot { get; private set; }
        protected Scene Scene { get; private set; }
        protected SubScene SubScene { get; private set; }

        [OneTimeSetUp]
        public virtual void OneTimeSetUp()
        {
            string path;
            do
            {
                path = Path.GetRandomFileName();
            } while (AssetDatabase.IsValidFolder(Path.Combine(k_AssetsFolderRoot, path)));

            m_PreviousLiveLinkState = SubSceneInspectorUtility.LiveConversionEnabled;
            SubSceneInspectorUtility.LiveConversionEnabled = true;

            var guid = AssetDatabase.CreateFolder(k_AssetsFolderRoot, path);
            TestAssetsDirectory = AssetDatabase.GUIDToAssetPath(guid);

            Scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var mainScenePath = Path.Combine(TestAssetsDirectory, $"{SceneName}.{k_SceneExtension}");
            EditorSceneManager.SaveScene(Scene, mainScenePath);
            SceneManager.SetActiveScene(Scene);

            // Temp context GameObject, necessary to create an empty subscene
            var targetGO = new GameObject(SubSceneName);

            var subsceneArgs = new SubSceneContextMenu.NewSubSceneArgs(targetGO, Scene, SubSceneContextMenu.NewSubSceneMode.EmptyScene);
            SubScene = SubSceneContextMenu.CreateNewSubScene(targetGO.name, subsceneArgs, InteractionMode.AutomatedAction);

            SubSceneRoot = SubScene.gameObject;

            Object.DestroyImmediate(targetGO);
            EditorSceneManager.SaveScene(Scene);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Object.DestroyImmediate(SubSceneRoot);
            AssetDatabase.DeleteAsset(TestAssetsDirectory);
            SceneWithBuildConfigurationGUIDs.ClearBuildSettingsCache();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);

            SubSceneInspectorUtility.LiveConversionEnabled = m_PreviousLiveLinkState;
        }
    }
}
