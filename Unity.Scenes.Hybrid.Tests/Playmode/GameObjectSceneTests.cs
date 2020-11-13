#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Tests;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Hash128 = Unity.Entities.Hash128;


namespace Unity.Scenes.Hybrid.Tests
{
    // These tests only work as Editor Playmode tests, due to lack of support in the test runner for build configs
    // TODO: https://unity3d.atlassian.net/browse/DOTS-3361
    public class GameObjectSceneTests
    {
        Hash128 m_TestSceneWithSubSceneGUID;
        string m_TestSceneWithSubScenePath;

        Hash128 m_TestSceneWithAutoLoadOffGUID;
        string m_TestSceneWithAutoLoadOffPath;

        [OneTimeSetUp]
        public void SetUpOnce()
        {
            m_TestSceneWithSubScenePath = "Packages/com.unity.entities/Unity.Scenes.Hybrid.Tests/TestSceneWithSubScene.unity";
            m_TestSceneWithSubSceneGUID = new GUID(AssetDatabase.AssetPathToGUID(m_TestSceneWithSubScenePath));

            m_TestSceneWithAutoLoadOffPath = "Packages/com.unity.entities/Unity.Scenes.Hybrid.Tests/TestSceneWithSubSceneAutoLoadFalse.unity";
            m_TestSceneWithAutoLoadOffGUID = new GUID(AssetDatabase.AssetPathToGUID(m_TestSceneWithAutoLoadOffPath));
        }

        // Steps
        // 1- Create WorldA
        // 2- Load GO Scene into WorldA by GUID
        // 3- Validate SubScene contents were not loaded
        [UnityTest]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public IEnumerator LoadSceneWithSubScene_AutoLoadOff()
        {
            using (var worldA = TestWorldSetup.CreateEntityWorld("World A", false))
            {
                var sceneSystemA = worldA.GetExistingSystem<SceneSystem>();
                Assert.IsTrue(m_TestSceneWithAutoLoadOffGUID.IsValid, "Scene guid is invalid");

                var worldAScene = sceneSystemA.LoadSceneAsync(m_TestSceneWithAutoLoadOffGUID, new SceneSystem.LoadParameters{Flags = SceneLoadFlags.LoadAsGOScene});
                Assert.IsFalse(sceneSystemA.IsSceneLoaded(worldAScene), "Scene is apparently immediately loaded");

                while (!sceneSystemA.IsSceneLoaded(worldAScene))
                {
                    worldA.Update();
                    yield return null;
                }

                var unitySceneRef = worldA.EntityManager.GetSharedComponentData<GameObjectSceneData>(worldAScene);
                Assert.IsTrue(unitySceneRef.Scene.IsValid(), "GameObject Scene is not valid");
                Assert.IsTrue(unitySceneRef.Scene.isLoaded, "GameObject Scene is not loaded");

                var worldAQuery = worldA.EntityManager.CreateEntityQuery(typeof(SharedWithMaterial));
                Assert.AreEqual(0, worldAQuery.CalculateEntityCount());

                foreach (var subScene in SubScene.AllSubScenes)
                {
                    Assert.IsFalse(subScene.AutoLoadScene);
                }
            }
        }

        // Steps
        // 1- Create WorldA
        // 2- Load GO Scene into WorldA by GUID
        // 3- Validate both GO Scene and SubScene are loaded
        // 4- Unload GO Scene
        // 5- Validate both have unloaded
        [UnityTest]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public IEnumerator LoadAndUnloadScene()
        {
            using (var worldA = TestWorldSetup.CreateEntityWorld("World A", false))
            {
                var sceneSystemA = worldA.GetExistingSystem<SceneSystem>();
                Assert.IsTrue(m_TestSceneWithSubSceneGUID.IsValid, "Scene guid is invalid");

                var worldAScene = sceneSystemA.LoadSceneAsync(m_TestSceneWithSubSceneGUID, new SceneSystem.LoadParameters{Flags = SceneLoadFlags.LoadAsGOScene});
                Assert.IsFalse(sceneSystemA.IsSceneLoaded(worldAScene), "Scene is apparently immediately loaded");

                while (!sceneSystemA.IsSceneLoaded(worldAScene))
                {
                    worldA.Update();
                    yield return null;
                }

                var unitySceneRef = worldA.EntityManager.GetSharedComponentData<GameObjectSceneData>(worldAScene);
                Assert.IsTrue(unitySceneRef.Scene.IsValid(), "GameObject Scene is not valid");
                Assert.IsTrue(unitySceneRef.Scene.isLoaded, "GameObject Scene is not loaded");

                var worldAQuery = worldA.EntityManager.CreateEntityQuery(typeof(SharedWithMaterial));
                Assert.AreEqual(1, worldAQuery.CalculateEntityCount());

                // Get Material on RenderMesh
                SharedWithMaterial sharedA;
                using (var sharedEntitiesA = worldAQuery.ToEntityArray(Allocator.TempJob))
                {
                    sharedA = worldA.EntityManager.GetSharedComponentData<SharedWithMaterial>(sharedEntitiesA[0]);
                }

                Assert.IsTrue(sharedA.material != null, "sharedA.material != null");

                sceneSystemA.UnloadScene(worldAScene);
                worldA.Update();

                while (sceneSystemA.IsSceneLoaded(worldAScene))
                {
                    worldA.Update();
                    yield return null;
                }

                // Unload of entity scenes can take a few frames
                worldA.Update();
                worldA.Update();

                Assert.AreEqual(0, worldAQuery.CalculateEntityCount());
                Assert.IsFalse(unitySceneRef.Scene.isLoaded, "GameObject Scene is still loaded");
            }
        }

        // Steps
        // 1- Create WorldA
        // 2- Load GO Scene into WorldA by GUID
        // 3- Validate both GO Scene and SubScene are loaded
        // 4- Unload GO Scene
        // 5- Validate both have unloaded
        // 6- Load GO Scene into WorldA again using the original Scene Entity, not GUID
        // 7- Check again everything is loaded
        [UnityTest]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public IEnumerator LoadAndUnloadScene_LoadAgainWithSceneEntity()
        {
            using (var worldA = TestWorldSetup.CreateEntityWorld("World A", false))
            {
                var sceneSystemA = worldA.GetExistingSystem<SceneSystem>();
                Assert.IsTrue(m_TestSceneWithSubSceneGUID.IsValid, "Scene guid is invalid");

                var worldAScene = sceneSystemA.LoadSceneAsync(m_TestSceneWithSubSceneGUID, new SceneSystem.LoadParameters{Flags = SceneLoadFlags.LoadAsGOScene});
                Assert.IsFalse(sceneSystemA.IsSceneLoaded(worldAScene), "Scene is apparently immediately loaded");

                while (!sceneSystemA.IsSceneLoaded(worldAScene))
                {
                    worldA.Update();
                    yield return null;
                }

                var unitySceneRef = worldA.EntityManager.GetSharedComponentData<GameObjectSceneData>(worldAScene);
                var oldScene = unitySceneRef.Scene;
                Assert.IsTrue(oldScene.IsValid(), "GameObject Scene is not valid");
                Assert.IsTrue(oldScene.isLoaded, "GameObject Scene is not loaded");

                var worldAQuery = worldA.EntityManager.CreateEntityQuery(typeof(SharedWithMaterial));
                Assert.AreEqual(1, worldAQuery.CalculateEntityCount());

                worldA.GetOrCreateSystem<SceneSystem>().UnloadScene(worldAScene);
                worldA.Update();

                while (sceneSystemA.IsSceneLoaded(worldAScene))
                {
                    worldA.Update();
                    yield return null;
                }

                // Unload of entity scenes can take a few frames
                worldA.Update();
                worldA.Update();

                Assert.AreEqual(0, worldAQuery.CalculateEntityCount());
                Assert.IsFalse(oldScene.isLoaded, "GameObject Scene is still loaded");

                // Now load again
                sceneSystemA.LoadSceneAsync(worldAScene, new SceneSystem.LoadParameters{Flags = SceneLoadFlags.LoadAsGOScene});
                Assert.IsFalse(sceneSystemA.IsSceneLoaded(worldAScene), "Scene is apparently immediately loaded");

                while (!sceneSystemA.IsSceneLoaded(worldAScene))
                {
                    worldA.Update();
                    yield return null;
                }

                unitySceneRef = worldA.EntityManager.GetSharedComponentData<GameObjectSceneData>(worldAScene);
                Assert.IsTrue(unitySceneRef.Scene.isLoaded, "GameObject Scene is not loaded");
                Assert.AreEqual(1, worldAQuery.CalculateEntityCount());
            }
        }

        // This Test deals with the fact that Unity GO Scenes can still be loaded and managed from the Unity SceneManager
        // Steps
        // 1- Load GO Scene with Unity SceneManager
        // 2- Create WorldA
        // 3- Load GO Scene into WorldA by GUID
        // 4- Validate both GO Scene and SubScene are loaded into WorldA
        // 5- Unload GO Scene from WorldA
        // 6- Validate SubScene unloaded from WorldA but GO Scene is still loaded in Unity
        [UnityTest]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public IEnumerator LoadAndUnloadScene_SceneThatWasAlreadyLoaded()
        {
            var asyncOperation = EditorSceneManager.LoadSceneAsyncInPlayMode(m_TestSceneWithSubScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return asyncOperation;

            var scene = EditorSceneManager.GetSceneByPath(m_TestSceneWithSubScenePath);

            Assert.IsTrue(scene.IsValid(), "GameObject Scene was not valid");
            Assert.IsTrue(scene.isLoaded, "GameObject Scene was not loaded");

            using (var worldA = TestWorldSetup.CreateEntityWorld("World A", false))
            {
                var sceneSystemA = worldA.GetExistingSystem<SceneSystem>();
                Assert.IsTrue(m_TestSceneWithSubSceneGUID.IsValid, "Scene guid is invalid");

                var worldAScene = sceneSystemA.LoadSceneAsync(m_TestSceneWithSubSceneGUID, new SceneSystem.LoadParameters{Flags = SceneLoadFlags.LoadAsGOScene});
                Assert.IsFalse(sceneSystemA.IsSceneLoaded(worldAScene), "Scene is apparently immediately loaded");

                while (!sceneSystemA.IsSceneLoaded(worldAScene))
                {
                    worldA.Update();
                    yield return null;
                }

                var worldAQuery = worldA.EntityManager.CreateEntityQuery(typeof(SharedWithMaterial));
                Assert.AreEqual(1, worldAQuery.CalculateEntityCount());

                sceneSystemA.UnloadScene(worldAScene);
                worldA.Update();

                while (sceneSystemA.IsSceneLoaded(worldAScene))
                {
                    worldA.Update();
                    yield return null;
                }

                // Unload of entity scenes can take a few frames
                worldA.Update();
                worldA.Update();

                Assert.AreEqual(0, worldAQuery.CalculateEntityCount());
                Assert.IsFalse(scene.isLoaded, "GameObject Scene was still loaded");
            }
        }

        // Steps
        // 1- Create WorldA and WorldB
        // 2- Load GO Scene into WorldA and WorldB by GUID
        // 3- Validate both GO Scene and SubScene are loaded into WorldA and B
        // 4- Unload GO Scene from WorldB
        // 5- Validate SubScene is Unloaded from WorldB but still loaded in WorldA
        // 6- Validate GO Scene is still loaded in Unity
        [UnityTest]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public IEnumerator LoadUnloadScene_TwoWorlds_StillLoadedInOne()
        {
            using (var worldA = TestWorldSetup.CreateEntityWorld("World A", false))
            using (var worldB = TestWorldSetup.CreateEntityWorld("World B", false))
            {
                var sceneSystemA = worldA.GetExistingSystem<SceneSystem>();
                var sceneSystemB = worldB.GetExistingSystem<SceneSystem>();
                Assert.IsTrue(m_TestSceneWithSubSceneGUID.IsValid, "Scene guid is invalid");

                var worldAScene = sceneSystemA.LoadSceneAsync(m_TestSceneWithSubSceneGUID, new SceneSystem.LoadParameters{Flags = SceneLoadFlags.LoadAsGOScene});
                var worldBScene = sceneSystemB.LoadSceneAsync(m_TestSceneWithSubSceneGUID, new SceneSystem.LoadParameters{Flags = SceneLoadFlags.LoadAsGOScene});
                Assert.IsFalse(sceneSystemA.IsSceneLoaded(worldAScene), "Scene is apparently immediately loaded");
                Assert.IsFalse(sceneSystemB.IsSceneLoaded(worldBScene), "Scene is apparently immediately loaded");

                while (!sceneSystemA.IsSceneLoaded(worldAScene) || !sceneSystemB.IsSceneLoaded(worldBScene))
                {
                    worldA.Update();
                    worldB.Update();
                    yield return null;
                }

                var worldAQuery = worldA.EntityManager.CreateEntityQuery(typeof(SharedWithMaterial));
                Assert.AreEqual(1, worldAQuery.CalculateEntityCount());

                var worldBQuery = worldB.EntityManager.CreateEntityQuery(typeof(SharedWithMaterial));
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

        // This test makes sure that ref counting is working and that GO Scenes only unload from Unity when no world/entity is referencing them
        // Steps
        // 1- Create WorldA and WorldB
        // 2- Load GO Scene into WorldA and WorldB by GUID
        // 3- Validate both GO Scene and SubScene are loaded into WorldA and B
        // 4- Dispose WorldB
        // 5- Validate SubScene is still loaded in WorldA and GO Scene is still loaded in Unity
        // 6- Dispose WorldA
        // 7- Validate GO Scene is unloaded from Unity
        [UnityTest]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public IEnumerator LoadScene_UnloadOnLastWorldDispose()
        {
            var worldA = TestWorldSetup.CreateEntityWorld("World A", false);
            var worldB = TestWorldSetup.CreateEntityWorld("World B", false);
            var sceneSystemA = worldA.GetExistingSystem<SceneSystem>();
            var sceneSystemB = worldB.GetExistingSystem<SceneSystem>();
            Assert.IsTrue(m_TestSceneWithSubSceneGUID.IsValid, "Scene guid is invalid");

            var worldAScene = sceneSystemA.LoadSceneAsync(m_TestSceneWithSubSceneGUID, new SceneSystem.LoadParameters{Flags = SceneLoadFlags.LoadAsGOScene});
            var worldBScene = sceneSystemB.LoadSceneAsync(m_TestSceneWithSubSceneGUID, new SceneSystem.LoadParameters{Flags = SceneLoadFlags.LoadAsGOScene});
            Assert.IsFalse(sceneSystemA.IsSceneLoaded(worldAScene), "Scene is apparently immediately loaded");
            Assert.IsFalse(sceneSystemB.IsSceneLoaded(worldBScene), "Scene is apparently immediately loaded");

            while (!sceneSystemA.IsSceneLoaded(worldAScene) || !sceneSystemB.IsSceneLoaded(worldBScene))
            {
                worldA.Update();
                worldB.Update();
                yield return null;
            }

            var worldAQuery = worldA.EntityManager.CreateEntityQuery(typeof(SharedWithMaterial));
            Assert.AreEqual(1, worldAQuery.CalculateEntityCount());

            var worldBQuery = worldB.EntityManager.CreateEntityQuery(typeof(SharedWithMaterial));
            Assert.AreEqual(1, worldBQuery.CalculateEntityCount());

            var unitySceneRef = worldA.EntityManager.GetSharedComponentData<GameObjectSceneData>(worldAScene);
            Assert.IsTrue(unitySceneRef.Scene.IsValid(), "GameObject Scene is not valid");
            Assert.IsTrue(unitySceneRef.Scene.isLoaded, "GameObject Scene is not loaded");

            worldB.Dispose();
            Assert.IsTrue(unitySceneRef.Scene.IsValid(), "GameObject Scene is not valid");
            Assert.IsTrue(unitySceneRef.Scene.isLoaded, "GameObject Scene is not loaded");
            Assert.AreEqual(1, worldAQuery.CalculateEntityCount());

            worldA.Dispose();
            Assert.IsFalse(unitySceneRef.Scene.isLoaded, "GameObject Scene is still loaded");
        }
    }
}
#endif
