using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEngine.TestTools;
#endif
using Unity.Entities;
using Unity.Entities.Tests;
using Unity.Scenes.Editor.Tests;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes.Hybrid.Tests.Editor
{
    [Serializable]
    [TestFixture]
    public class GameObjectSceneTests
    {
        [SerializeField] TestWithTempAssets m_TempAssets;

        [SerializeField] Hash128 m_SceneGUID;
        [SerializeField] string m_ScenePath;

        [OneTimeSetUp]
        public void SetUpOnce()
        {
            if (m_TempAssets.TempAssetDir != null)
            {
                return;
            }

            m_TempAssets.SetUp();

            var tempScene = SubSceneTestsHelper.CreateTmpScene(ref m_TempAssets);
            SubSceneTestsHelper.CreateSubSceneInSceneFromObjects("SubScene", false, tempScene, () =>
            {
                var go = new GameObject();
                var authoring = go.AddComponent<AuthoringWithUnmanaged>();
                authoring.Value = 42;
                return new List<GameObject> { go };
            });

            m_ScenePath = tempScene.path;
            m_SceneGUID = new GUID(AssetDatabase.AssetPathToGUID(m_ScenePath));
        }

        [OneTimeTearDown]
        public void TearDownOnce()
        {
            m_TempAssets.TearDown();
            SceneWithBuildConfigurationGUIDs.ClearBuildSettingsCache();
        }

        // This test handles the case where the user presses Play in Editor with a Scene with SubScenes.
        // In this case, we automatically create a reference in the default world to this Scene
        // It is totally fine to load this again, it is a way to create a reference if it doesn't exist and won't load twice
        // Steps
        // 1- Ensure DefaultWorld is created and we enter PlayMode with a scene containing a subscene
        // 2- Create WorldB
        // 3- Load GO Scene into WorldA and WorldB by GUID
        // 4- Validate both GO Scene and SubScene are loaded in WorldA and WorldB
        // 5- Unload GO Scene from WorldB
        // 6- Validate SubScene unloaded from WorldB but still loaded in WorldA
        // 7- Validate GO Scene is still loaded
        [UnityTest]
        public IEnumerator LoadSameSceneIntoTwoWorlds_ThenUnloadInOne_SceneStaysLoadedInOtherWorld()
        {
            yield return new EnterPlayMode();

            var worldA = World.DefaultGameObjectInjectionWorld;

            using (var worldB = TestWorldSetup.CreateEntityWorld("World B", false))
            {
                var sceneSystemA = worldA.GetExistingSystem<SceneSystem>();
                var sceneSystemB = worldB.GetExistingSystem<SceneSystem>();
                Assert.IsTrue(m_SceneGUID.IsValid, "Scene guid is invalid");

                var worldAScene = sceneSystemA.LoadSceneAsync(m_SceneGUID, new SceneSystem.LoadParameters{Flags = SceneLoadFlags.LoadAsGOScene});
                var worldBScene = sceneSystemB.LoadSceneAsync(m_SceneGUID, new SceneSystem.LoadParameters{Flags = SceneLoadFlags.LoadAsGOScene});
                Assert.IsFalse(sceneSystemA.IsSceneLoaded(worldAScene), "Scene is apparently immediately loaded");
                Assert.IsFalse(sceneSystemB.IsSceneLoaded(worldBScene), "Scene is apparently immediately loaded");

                while (!sceneSystemA.IsSceneLoaded(worldAScene) || !sceneSystemB.IsSceneLoaded(worldBScene))
                {
                    worldA.Update();
                    worldB.Update();
                    yield return null;
                }

                var worldAQuery = worldA.EntityManager.CreateEntityQuery(typeof(RuntimeUnmanaged));
                Assert.AreEqual(1, worldAQuery.CalculateEntityCount());

                var worldBQuery = worldB.EntityManager.CreateEntityQuery(typeof(RuntimeUnmanaged));
                Assert.AreEqual(1, worldBQuery.CalculateEntityCount());

                var unitySceneRef = worldA.EntityManager.GetSharedComponentData<GameObjectSceneData>(worldAScene);
                Assert.IsTrue(unitySceneRef.Scene.IsValid(), "GameObject Scene is not valid");
                Assert.IsTrue(unitySceneRef.Scene.isLoaded, "GameObject Scene is not loaded");

                sceneSystemB.UnloadScene(worldBScene);
                worldA.Update();
                worldB.Update();

                while (sceneSystemB.IsSceneLoaded(worldBScene))
                {
                    worldA.Update();
                    worldB.Update();
                    yield return null;
                }

                // Unload of entity scenes can take a few frames
                worldA.Update();
                worldB.Update();

                worldA.Update();
                worldB.Update();

                Assert.AreEqual(1, worldAQuery.CalculateEntityCount());
                Assert.AreEqual(0, worldBQuery.CalculateEntityCount());
                Assert.IsTrue(unitySceneRef.Scene.isLoaded, "GameObject Scene is not loaded");
                Assert.IsTrue(unitySceneRef.Scene.IsValid(), "GameObject Scene is not valid");
            }
        }

        // This test validates the AutoLoad on a SubScene is respected when entering and exiting PlayMode in the Editor
        // Steps
        // 1- Open our scene
        // 2- Enter PlayMode
        // 3- Validate our SubScene loaded the Entities we expect
        // 4- Exit PlayMode
        // 5- Disable Autoload on SubScenes in the Scene
        // 6- Enter PlayMode
        // 7- Validate NONE of the Entities we expect were loaded
        [UnityTest]
        public IEnumerator LoadSceneWithSubSceneAutoLoad_OnAndOff()
        {
            EditorSceneManager.OpenScene(m_ScenePath);

            yield return new EnterPlayMode();

            var world = World.DefaultGameObjectInjectionWorld;
            var worldQuery = world.EntityManager.CreateEntityQuery(typeof(RuntimeUnmanaged));
            Assert.AreEqual(1, worldQuery.CalculateEntityCount());

            // Now exit and disable auto-load
            yield return new ExitPlayMode();

            foreach (var subScene in SubScene.AllSubScenes)
            {
                subScene.AutoLoadScene = false;
            }

            yield return new EnterPlayMode();

            world = World.DefaultGameObjectInjectionWorld;
            worldQuery = world.EntityManager.CreateEntityQuery(typeof(RuntimeUnmanaged));
            Assert.AreEqual(0, worldQuery.CalculateEntityCount());
        }
    }
}
