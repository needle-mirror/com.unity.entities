using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Entities;
using Unity.Entities.Hybrid.Tests;
using Unity.Entities.Tests;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;

namespace Unity.Scenes.Editor.Tests
{
    public class SubSceneConversionTests
    {
        TestWithTempAssets m_TempAssets;
        TestWithSubScenes m_SubSceneTest;
        TestWithCustomDefaultGameObjectInjectionWorld m_World;
        private PlayerLoopSystem m_PrevPlayerLoop;
        Texture m_Texture1;
        Texture m_Texture2;

        [OneTimeSetUp]
        public void SetUp()
        {
            m_PrevPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();
            m_TempAssets.SetUp();
            m_SubSceneTest.Setup();
            m_World.Setup();
            DefaultWorldInitialization.DefaultLazyEditModeInitialize();
            m_Texture1 = new Texture2D(64, 64);
            AssetDatabase.CreateAsset(m_Texture1, m_TempAssets.GetNextPath("Texture1.asset"));
            m_Texture2 = new Texture2D(32, 32);
            AssetDatabase.CreateAsset(m_Texture2, m_TempAssets.GetNextPath("Texture2.asset"));
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_World.TearDown();
            m_SubSceneTest.TearDown();
            m_TempAssets.TearDown();
            PlayerLoop.SetPlayerLoop(m_PrevPlayerLoop);
        }

        [TearDown]
        public void TearDown()
        {
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            em.DestroyEntity(em.UniversalQuery);
        }

        [Test]
        public void SubScene_WithDependencyOnAsset_IsInvalidatedWhenAssetChanges()
        {
            var subScene = SubSceneTestsHelper.CreateSubSceneFromObjects(ref m_TempAssets, "SubScene", false, () =>
            {
                var go = new GameObject();
                var authoring = go.AddComponent<DependencyTestAuthoring>();
                authoring.Asset = m_Texture1;
                return new List<GameObject> { go };
            });

            var buildSettings = default(Unity.Entities.Hash128);
            var hash = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, buildSettings, ImportMode.Synchronous);
            Assert.IsTrue(hash.IsValid);

            m_Texture1.wrapMode = m_Texture1.wrapMode == TextureWrapMode.Repeat ? TextureWrapMode.Mirror : TextureWrapMode.Repeat;
            AssetDatabase.SaveAssets();

            var newHash = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, buildSettings, ImportMode.NoImport);
            Assert.AreNotEqual(hash, newHash);
            Assert.IsFalse(newHash.IsValid);
        }

        [Test]
        public void SubScene_WithoutContents_ImportsAndLoadsAndUnloads()
        {
            var subScene = SubSceneTestsHelper.CreateSubSceneFromObjects(ref m_TempAssets, "SubScene", false, null);
            subScene.AutoLoadScene = false;
            subScene.gameObject.SetActive(false);

            var world = World.DefaultGameObjectInjectionWorld;
            var sceneSystem = world.GetOrCreateSystem<SceneSystem>();
            var sceneEntity = sceneSystem.LoadSceneAsync(subScene.SceneGUID, new SceneSystem.LoadParameters
            {
                Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn
            });

            // TODO: Editor doesn't update if it doesn't have focus, so we must explicitly update the world to process the load.
            world.Update();

            Assert.IsTrue(sceneSystem.IsSceneLoaded(sceneEntity), "Failed to load scene");
            sceneSystem.UnloadScene(sceneEntity);

            world.Update();

            Assert.IsFalse(sceneSystem.IsSceneLoaded(sceneEntity), "Failed to unload scene");
        }

        [Test]
        public void SubScene_WithoutContents_DeletingSceneAssetUnloadsScene()
        {
            var subScene = SubSceneTestsHelper.CreateSubSceneFromObjects(ref m_TempAssets, "SubScene", false, null);
            subScene.AutoLoadScene = false;
            subScene.gameObject.SetActive(false);

            var world = World.DefaultGameObjectInjectionWorld;
            var sceneSystem = world.GetOrCreateSystem<SceneSystem>();
            var sceneEntity = sceneSystem.LoadSceneAsync(subScene.SceneGUID, new SceneSystem.LoadParameters
            {
                Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn
            });

            Assert.IsFalse(sceneSystem.IsSceneLoaded(sceneEntity), "Scene should not be loaded yet.");

            // TODO: Editor doesn't update if it doesn't have focus, so we must explicitly update the world to process the load.
            world.Update();
            Assert.IsTrue(sceneSystem.IsSceneLoaded(sceneEntity), "Failed to load scene");

            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(subScene.SceneAsset));

            // Block the import of this subscene so that we can get a single-frame result for this test
            var hash = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, sceneSystem.BuildConfigurationGUID, ImportMode.Synchronous);
            Assert.IsTrue(hash.IsValid, "Failed to import SubScene.");

            LogAssert.Expect(LogType.Error, new Regex("Loading Entity Scene failed.*"));

            // TODO: Editor doesn't update if it doesn't have focus, so we must explicitly update the world to process the load.
            world.Update();

            Assert.IsFalse(sceneSystem.IsSceneLoaded(sceneEntity), "Scene should not be loaded");
        }

#if !NET_DOTS && !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test, Ignore("DOTS-1422 - Currently fails, but this is an edge case")]
        public void SubScene_WithNullAsset_ImportsAndLoads()
            => SubScene_WithAsset_ImportsAndLoads(null);

        [Test]
        public void SubScene_WithTextureAsset_ImportsAndLoads()
            => SubScene_WithAsset_ImportsAndLoads(m_Texture1);

        void SubScene_WithAsset_ImportsAndLoads(UnityEngine.Object asset)
        {
            var subScene = SubSceneTestsHelper.CreateSubSceneFromObjects(ref m_TempAssets, "SubScene", false, () =>
            {
                var go = new GameObject("GO1");
                var managed = go.AddComponent<SubSceneLoadTestAssetAuthoring>();
                managed.Asset = asset;
                return new List<GameObject> { go };
            });
            subScene.AutoLoadScene = false;
            subScene.gameObject.SetActive(false);

            var world = World.DefaultGameObjectInjectionWorld;
            var sceneSystem = world.GetOrCreateSystem<SceneSystem>();
            var sceneEntity = sceneSystem.LoadSceneAsync(subScene.SceneGUID, new SceneSystem.LoadParameters
            {
                Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn
            });

            // TODO: Editor doesn't update if it doesn't have focus, so we must explicitly update the world to process the load.
            world.Update();

            Assert.IsTrue(sceneSystem.IsSceneLoaded(sceneEntity), "Failed to load scene");

            {
                var entities = DebugEntity.GetAllEntities(world.EntityManager);
                var entity = entities.FirstOrDefault(de => de.HasComponent<SubSceneLoadTestAssetAuthoring.Component>());
                Assert.IsNotNull(entity, "Failed to find converted GameObject");

                var component = world.EntityManager.GetComponentData<SubSceneLoadTestAssetAuthoring.Component>(entity.Entity);
                Assert.AreEqual(asset, component.Asset);
            }
        }

#endif

        [Test]
        public void SubScene_WithNullBlobAsset_ImportsAndLoads()
            => SubScene_WithBlobAsset_ImportsAndLoads_Impl(false);

        [Test]
        public void SubScene_WithBlobAsset_ImportsAndLoads()
            => SubScene_WithBlobAsset_ImportsAndLoads_Impl(true);

        void SubScene_WithBlobAsset_ImportsAndLoads_Impl(bool useBlobAsset)
        {
            var subScene = SubSceneTestsHelper.CreateSubSceneFromObjects(ref m_TempAssets, "SubScene", false, () =>
            {
                var go = new GameObject("GO1");
                var component = go.AddComponent<SubSceneLoadTestBlobAssetAuthoring>();
                component.Int = 1;
                component.PtrInt = 2;
                component.String = "Test";
                component.Strings = new[] { "A", null, "B" };
                component.UseNullBlobAsset = !useBlobAsset;
                return new List<GameObject> { go };
            });
            subScene.AutoLoadScene = false;
            subScene.gameObject.SetActive(false);

            var world = World.DefaultGameObjectInjectionWorld;
            var sceneSystem = world.GetOrCreateSystem<SceneSystem>();
            var sceneEntity = sceneSystem.LoadSceneAsync(subScene.SceneGUID, new SceneSystem.LoadParameters
            {
                Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn
            });

            // TODO: Editor doesn't update if it doesn't have focus, so we must explicitly update the world to process the load.
            world.Update();

            Assert.IsTrue(sceneSystem.IsSceneLoaded(sceneEntity), "Failed to load scene");

            {
                var entities = DebugEntity.GetAllEntities(world.EntityManager);
                var entity = entities.FirstOrDefault(de => de.HasComponent<SubSceneLoadTestBlobAssetAuthoring.Component>());
                Assert.IsNotNull(entity, "Failed to find converted GameObject");

                var component = world.EntityManager.GetComponentData<SubSceneLoadTestBlobAssetAuthoring.Component>(entity.Entity);
                if (useBlobAsset)
                {
                    Assert.AreEqual(1, component.BlobAsset.Value.Int);
                    Assert.AreEqual(2, component.BlobAsset.Value.Ptr.Value);
                    Assert.AreEqual("Test", component.BlobAsset.Value.String.ToString());
                    Assert.AreEqual(3, component.BlobAsset.Value.Strings.Length);
                    Assert.AreEqual("A", component.BlobAsset.Value.Strings[0].ToString());
                    Assert.AreEqual("", component.BlobAsset.Value.Strings[1].ToString());
                    Assert.AreEqual("B", component.BlobAsset.Value.Strings[2].ToString());
                }
                else
                {
                    // TODO: this sometimes fails
                    Assert.AreEqual(BlobAssetReference<SubSceneLoadTestBlobAsset>.Null, component.BlobAsset, "Blob asset should be null");
                    Assert.IsFalse(component.BlobAsset.IsCreated, "Blob asset should not be created, but it was");
                }
            }
        }

        [Test]
        public void SubScene_WithUnmanagedComponent_ImportsAndLoads()
        {
            var subScene = SubSceneTestsHelper.CreateSubSceneFromObjects(ref m_TempAssets, "SubScene", false, () =>
            {
                var go = new GameObject("GO1");
                var unmanaged = go.AddComponent<SubSceneLoadTestUnmanagedAuthoring>();
                unmanaged.Int = 1;
                unmanaged.Entity = go;
                return new List<GameObject> { go };
            });
            subScene.AutoLoadScene = false;
            subScene.gameObject.SetActive(false);

            var world = World.DefaultGameObjectInjectionWorld;
            var sceneSystem = world.GetOrCreateSystem<SceneSystem>();
            var sceneEntity = sceneSystem.LoadSceneAsync(subScene.SceneGUID, new SceneSystem.LoadParameters
            {
                Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn
            });

            // TODO: Editor doesn't update if it doesn't have focus, so we must explicitly update the world to process the load.
            world.Update();

            Assert.IsTrue(sceneSystem.IsSceneLoaded(sceneEntity), "Failed to load scene");

            {
                var query = world.EntityManager.CreateEntityQuery(typeof(SubSceneLoadTestUnmanagedAuthoring.Component));
                var comp = query.GetSingleton<SubSceneLoadTestUnmanagedAuthoring.Component>();
                var entity = query.GetSingletonEntity();
                Assert.AreEqual(1, comp.Int);
                Assert.AreEqual(entity, comp.Entity);
                VerifyBlobAsset(comp.BlobAsset, 1, "GO1", 1, "GO blob asset");
            }
        }

        [Test]
        public void SubScene_WithSharedComponent_ImportsAndLoads()
        {
            var subScene = SubSceneTestsHelper.CreateSubSceneFromObjects(ref m_TempAssets, "SubScene", false, () =>
            {
                var go = new GameObject("GO1");
                var comp = go.AddComponent<SubSceneLoadTestSharedAuthoring>();
                comp.Int = 1;
                comp.String = "Test";
                comp.Asset = m_Texture1;
                return new List<GameObject> { go };
            });
            subScene.AutoLoadScene = false;
            subScene.gameObject.SetActive(false);

            var world = World.DefaultGameObjectInjectionWorld;
            var sceneSystem = world.GetOrCreateSystem<SceneSystem>();
            var sceneEntity = sceneSystem.LoadSceneAsync(subScene.SceneGUID, new SceneSystem.LoadParameters
            {
                Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn
            });

            // TODO: Editor doesn't update if it doesn't have focus, so we must explicitly update the world to process the load.
            world.Update();

            Assert.IsTrue(sceneSystem.IsSceneLoaded(sceneEntity), "Failed to load scene");

            {
                var query = world.EntityManager.CreateEntityQuery(typeof(SubSceneLoadTestSharedAuthoring.Component));
                var entity = query.GetSingletonEntity();
                var comp = world.EntityManager.GetSharedComponentData<SubSceneLoadTestSharedAuthoring.Component>(entity);
                Assert.AreEqual(1, comp.Int);
                Assert.AreEqual("Test", comp.String);
                Assert.AreEqual(m_Texture1, comp.Asset);
            }
        }

#if !NET_DOTS && !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void SubScene_WithComplexComponents_ImportsAndLoads()
        {
            var subScene = SubSceneTestsHelper.CreateSubSceneFromObjects(ref m_TempAssets, "SubScene", false, () =>
            {
                var go1 = new GameObject("GO1");
                {
                    var unmanaged = go1.AddComponent<SubSceneLoadTestUnmanagedAuthoring>();
                    unmanaged.Int = 1;
                    unmanaged.Entity = go1;

                    var shared = go1.AddComponent<SubSceneLoadTestSharedAuthoring>();
                    shared.Int = 42;
                    shared.String = "Test";
                    shared.Asset = m_Texture2;

                    var buffer = go1.AddComponent<SubSceneLoadTestBufferAuthoring>();
                    buffer.Ints = new List<int>();
                    buffer.Entities = new List<GameObject>();
                }
                go1.AddComponent<TestComponentAuthoring>();

                var go2 = new GameObject("GO2");
                {
                    var unmanaged = go2.AddComponent<SubSceneLoadTestUnmanagedAuthoring>();
                    unmanaged.Int = 2;
                    unmanaged.Entity = go2;

                    var managed = go2.AddComponent<SubSceneLoadTestManagedAuthoring>();
                    managed.Int = 2;
                    managed.String = "Test2";
                    managed.Entity = go2;
                    managed.Asset = m_Texture1;

                    var shared = go2.AddComponent<SubSceneLoadTestSharedAuthoring>();
                    shared.Int = 42;
                    shared.String = "Test";
                    shared.Asset = m_Texture1;
                }

                var go3 = new GameObject("GO3");
                {
                    var managed = go3.AddComponent<SubSceneLoadTestManagedAuthoring>();
                    managed.Int = 3;
                    managed.String = "Test3";
                    managed.Entity = go3;
                    managed.Asset = m_Texture1;

                    var shared = go3.AddComponent<SubSceneLoadTestSharedAuthoring>();
                    shared.Int = 42;
                    shared.String = "Test Different";
                    shared.Asset = m_Texture1;

                    var buffer = go3.AddComponent<SubSceneLoadTestBufferAuthoring>();
                    buffer.Ints = new List<int> { 5 };
                    buffer.Entities = new List<GameObject> { go2 };
                }

                var go4 = new GameObject("GO4");
                {
                    var unmanaged = go4.AddComponent<SubSceneLoadTestUnmanagedAuthoring>();
                    unmanaged.Int = 4;
                    unmanaged.Entity = go4;

                    var managed = go4.AddComponent<SubSceneLoadTestManagedAuthoring>();
                    managed.Int = 4;
                    managed.String = "Test4";
                    managed.Entity = go4;
                    managed.Asset = m_Texture1;

                    var buffer = go4.AddComponent<SubSceneLoadTestBufferAuthoring>();
                    buffer.Ints = new List<int> { 3, 17, 2 };
                    buffer.Entities = new List<GameObject> { go1.gameObject, go2.gameObject, go3.gameObject };
                }

                return new List<GameObject> { go1, go2, go3, go4 };
            });

            subScene.AutoLoadScene = false;
            subScene.gameObject.SetActive(false);

            var world = World.DefaultGameObjectInjectionWorld;
            var sceneSystem = world.GetOrCreateSystem<SceneSystem>();
            var sceneEntity = sceneSystem.LoadSceneAsync(subScene.SceneGUID, new SceneSystem.LoadParameters
            {
                Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn
            });

            // TODO: Editor doesn't update if it doesn't have focus, so we must explicitly update the world to process the load.
            world.Update();

            Assert.IsTrue(sceneSystem.IsSceneLoaded(sceneEntity), "Failed to load scene");

            {
                var entities = DebugEntity.GetAllEntities(world.EntityManager);
                var unmanagedQuery = world.EntityManager.CreateEntityQuery(typeof(SubSceneLoadTestUnmanagedAuthoring.Component));
                var managedQuery = world.EntityManager.CreateEntityQuery(typeof(SubSceneLoadTestManagedAuthoring.Component));
                var sharedQuery = world.EntityManager.CreateEntityQuery(typeof(SubSceneLoadTestSharedAuthoring.Component));
                var bufferQuery = world.EntityManager.CreateEntityQuery(typeof(SubSceneLoadTestBufferAuthoring.Component));

                Assert.AreEqual(3, unmanagedQuery.CalculateEntityCount(), $"Expected exactly 3 entities with {typeof(SubSceneLoadTestUnmanagedAuthoring.Component).FullName}");
                Assert.AreEqual(3, managedQuery.CalculateEntityCount(), $"Expected exactly 3 entities with {typeof(SubSceneLoadTestManagedAuthoring.Component).FullName}");
                Assert.AreEqual(3, sharedQuery.CalculateEntityCount(), $"Expected exactly 3 entities with {typeof(SubSceneLoadTestSharedAuthoring.Component).FullName}");
                Assert.AreEqual(3, bufferQuery.CalculateEntityCount(), $"Expected exactly 3 entities with {typeof(SubSceneLoadTestBufferAuthoring.Component).FullName}");


                var e1 = entities.FirstOrDefault(de => de.HasComponent<SubSceneLoadTestUnmanagedAuthoring.Component>() && de.HasComponent<SubSceneLoadTestSharedAuthoring.Component>() && de.HasComponent<SubSceneLoadTestBufferAuthoring.Component>());
                Assert.IsNotNull(e1, "Could not find entity corresponding to GO1");
                var e2 = entities.FirstOrDefault(de => de.HasComponent<SubSceneLoadTestUnmanagedAuthoring.Component>()
                    && de.HasComponent<SubSceneLoadTestManagedAuthoring.Component>()
                    && de.HasComponent<SubSceneLoadTestSharedAuthoring.Component>());
                Assert.IsNotNull(e2, "Could not find entity corresponding to GO2");
                var e3 = entities.FirstOrDefault(de =>
                    de.HasComponent<SubSceneLoadTestManagedAuthoring.Component>() &&
                    de.HasComponent<SubSceneLoadTestSharedAuthoring.Component>() && de.HasComponent<SubSceneLoadTestBufferAuthoring.Component>());
                Assert.IsNotNull(e3, "Could not find entity corresponding to GO3");
                var e4 = entities.FirstOrDefault(de => de.HasComponent<SubSceneLoadTestUnmanagedAuthoring.Component>()
                    && de.HasComponent<SubSceneLoadTestManagedAuthoring.Component>()
                    && de.HasComponent<SubSceneLoadTestBufferAuthoring.Component>());
                Assert.IsNotNull(e4, "Could not find entity corresponding to GO4");

                {
                    var unmanaged = world.EntityManager.GetComponentData<SubSceneLoadTestUnmanagedAuthoring.Component>(e1.Entity);
                    Assert.AreEqual(e1.Entity, unmanaged.Entity);
                    Assert.AreEqual(1, unmanaged.Int);
                    VerifyBlobAsset(unmanaged.BlobAsset, 1, "GO1", 1, "GO1-unmanaged");

                    var shared = world.EntityManager.GetSharedComponentData<SubSceneLoadTestSharedAuthoring.Component>(e1.Entity);
                    Assert.AreEqual(42, shared.Int);
                    Assert.AreEqual("Test", shared.String);
                    Assert.AreEqual(m_Texture2, shared.Asset);

                    var buffer = world.EntityManager.GetBuffer<SubSceneLoadTestBufferAuthoring.Component>(e1.Entity);
                    Assert.AreEqual(0, buffer.Length);
                }

                {
                    var unmanaged = world.EntityManager.GetComponentData<SubSceneLoadTestUnmanagedAuthoring.Component>(e2.Entity);
                    Assert.AreEqual(e2.Entity, unmanaged.Entity);
                    Assert.AreEqual(2, unmanaged.Int);
                    VerifyBlobAsset(unmanaged.BlobAsset, 2, "GO2", 1, "GO2-unmanaged");

                    var managed = world.EntityManager.GetComponentData<SubSceneLoadTestManagedAuthoring.Component>(e2.Entity);
                    Assert.AreEqual(e2.Entity, managed.Entity);
                    Assert.AreEqual(2, managed.Int);
                    Assert.AreEqual("Test2", managed.String);
                    Assert.AreEqual(m_Texture1, managed.Asset);

                    var shared = world.EntityManager.GetSharedComponentData<SubSceneLoadTestSharedAuthoring.Component>(e2.Entity);
                    Assert.AreEqual(42, shared.Int);
                    Assert.AreEqual("Test", shared.String);
                    Assert.AreEqual(m_Texture1, shared.Asset);
                }

                {
                    var managed = world.EntityManager.GetComponentData<SubSceneLoadTestManagedAuthoring.Component>(e3.Entity);
                    Assert.AreEqual(e3.Entity, managed.Entity);
                    Assert.AreEqual(3, managed.Int);
                    Assert.AreEqual("Test3", managed.String);
                    Assert.AreEqual(m_Texture1, managed.Asset);

                    var shared = world.EntityManager.GetSharedComponentData<SubSceneLoadTestSharedAuthoring.Component>(e3.Entity);
                    Assert.AreEqual(42, shared.Int);
                    Assert.AreEqual("Test Different", shared.String);
                    Assert.AreEqual(m_Texture1, shared.Asset);

                    var buffer = world.EntityManager.GetBuffer<SubSceneLoadTestBufferAuthoring.Component>(e3.Entity);
                    Assert.AreEqual(1, buffer.Length);
                    Assert.AreEqual(e2.Entity, buffer[0].Entity);
                    Assert.AreEqual(5, buffer[0].Int);
                    VerifyBlobAsset(buffer[0].BlobAsset, 5, "GO30", 0, "GO3-buffer0");
                }

                {
                    var unmanaged = world.EntityManager.GetComponentData<SubSceneLoadTestUnmanagedAuthoring.Component>(e4.Entity);
                    Assert.AreEqual(e4.Entity, unmanaged.Entity);
                    Assert.AreEqual(4, unmanaged.Int);
                    VerifyBlobAsset(unmanaged.BlobAsset, 4, "GO4", 1, "GO4-unmanaged");

                    var managed = world.EntityManager.GetComponentData<SubSceneLoadTestManagedAuthoring.Component>(e4.Entity);
                    Assert.AreEqual(e4.Entity, managed.Entity);
                    Assert.AreEqual(4, managed.Int);
                    Assert.AreEqual("Test4", managed.String);
                    Assert.AreEqual(m_Texture1, managed.Asset);

                    var buffer = world.EntityManager.GetBuffer<SubSceneLoadTestBufferAuthoring.Component>(e4.Entity);
                    Assert.AreEqual(3, buffer.Length);

                    Assert.AreEqual(e1.Entity, buffer[0].Entity);
                    Assert.AreEqual(3, buffer[0].Int);
                    VerifyBlobAsset(buffer[0].BlobAsset, 3, "GO40", 0, "GO4-buffer0");

                    Assert.AreEqual(e2.Entity, buffer[1].Entity);
                    Assert.AreEqual(17, buffer[1].Int);
                    VerifyBlobAsset(buffer[1].BlobAsset, 17, "GO41", 1, "GO4-buffer1");

                    Assert.AreEqual(e3.Entity, buffer[2].Entity);
                    Assert.AreEqual(2, buffer[2].Int);
                    VerifyBlobAsset(buffer[2].BlobAsset, 2, "GO42", 2, "GO4-buffer2");
                }
            }

            sceneSystem.UnloadScene(sceneEntity);
            Assert.IsFalse(sceneSystem.IsSceneLoaded(sceneEntity), "Failed to unload scene");
        }

        #endif

        static void VerifyBlobAsset(BlobAssetReference<SubSceneLoadTestBlobAsset> asset, int n, string str, int arrayLength, string assertReference)
        {
            Assert.AreNotEqual(BlobAssetReference<SubSceneLoadTestBlobAsset>.Null, asset, $"The blob asset {assertReference} should not be null");
            Assert.AreEqual(n, asset.Value.Int, $"The int stored on {assertReference} wasn't the expected value");
            Assert.AreEqual(str, asset.Value.String.ToString(), $"The string stored on {assertReference} wasn't the expected value");
            Assert.AreEqual(n + 1, asset.Value.Ptr.Value, $"The int stored on {assertReference} via a pointer wasn't the expected value");
            Assert.AreEqual(arrayLength, asset.Value.Strings.Length, $"The array of strings stored on {assertReference} wasn't of the expected length");
            for (int i = 0; i < arrayLength; i++)
                Assert.AreEqual(i.ToString(), asset.Value.Strings[i].ToString(), $"The {i}th string stored on {assertReference} wasn't the expected value");
        }

        [Test]
        public void SubScene_WithDependencyOnAssetInScene_StillImports()
        {
            var subScene = SubSceneTestsHelper.CreateSubSceneFromObjects(ref m_TempAssets, "SubScene", false, () =>
            {
                var go = new GameObject();
                var authoring = go.AddComponent<DependencyTestAuthoring>();
                var texture = new Texture2D(64, 64);
                authoring.Asset = texture;
                return new List<GameObject> { go };
            });

            var buildSettings = default(Unity.Entities.Hash128);
            var hash = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, buildSettings, ImportMode.Synchronous);
            Assert.IsTrue(hash.IsValid);
        }

        [Test]
        public void SubScene_WithDependencyOnBuiltInAsset_StillImports()
        {
            var subScene = SubSceneTestsHelper.CreateSubSceneFromObjects(ref m_TempAssets, "SubScene", false, () =>
            {
                var go = new GameObject();
                var authoring = go.AddComponent<DependencyTestAuthoring>();
                var sphereHolder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                authoring.Asset = sphereHolder.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(sphereHolder);
                return new List<GameObject> { go };
            });

            var buildSettings = default(Unity.Entities.Hash128);
            var hash = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, buildSettings, ImportMode.Synchronous);
            Assert.IsTrue(hash.IsValid);
        }

        [Test]
        public void SubScene_WithDependencyOnAssetInScene_Reimport_EndToEnd()
        {
            var subScene = SubSceneTestsHelper.CreateSubSceneFromObjects(ref m_TempAssets, "SubScene", false, () =>
            {
                var go = new GameObject();
                var authoring = go.AddComponent<DependencyTestAuthoring>();
                var sphereHolder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                authoring.Asset = sphereHolder.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(sphereHolder);
                return new List<GameObject> { go };
            });

            var buildSettings = default(Unity.Entities.Hash128);
            var originalHash = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, buildSettings, ImportMode.Synchronous);
            Assert.IsTrue(originalHash.IsValid);

            SubSceneInspectorUtility.ForceReimport(new []{subScene});

            var newHashCreated = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, buildSettings, ImportMode.Synchronous);
            Assert.IsTrue(newHashCreated.IsValid);
            Assert.AreNotEqual(originalHash, newHashCreated);

            SubSceneInspectorUtility.ForceReimport(new []{subScene});

            var newHashUpdated = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, buildSettings, ImportMode.Synchronous);
            Assert.IsTrue(newHashUpdated.IsValid);
            Assert.AreNotEqual(newHashCreated, newHashUpdated);
            Assert.AreNotEqual(originalHash, newHashUpdated);
        }

        [Test]
        public void SubScene_WithDependencyOnAssetInScene_ClearCache_EndToEnd()
        {
            var subScene = SubSceneTestsHelper.CreateSubSceneFromObjects(ref m_TempAssets, "SubScene", false, () =>
            {
                var go = new GameObject();
                var authoring = go.AddComponent<DependencyTestAuthoring>();
                var sphereHolder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                authoring.Asset = sphereHolder.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(sphereHolder);
                return new List<GameObject> { go };
            });

            var buildSettings = default(Unity.Entities.Hash128);
            var originalHash = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, buildSettings, ImportMode.Synchronous);
            Assert.IsTrue(originalHash.IsValid);

            // Clear Cache (First time this creates global dependency asset, so we will test both steps)
            EntitiesCacheUtility.UpdateEntitySceneGlobalDependency();

            var newHashCreated = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, buildSettings, ImportMode.Synchronous);
            Assert.IsTrue(newHashCreated.IsValid);
            Assert.AreNotEqual(originalHash, newHashCreated);

            // Clear Cache (This updates existing asset)
            EntitiesCacheUtility.UpdateEntitySceneGlobalDependency();

            var newHashUpdated = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, buildSettings, ImportMode.Synchronous);
            Assert.IsTrue(newHashUpdated.IsValid);
            Assert.AreNotEqual(newHashCreated, newHashUpdated);
            Assert.AreNotEqual(originalHash, newHashUpdated);

            // Delete created dependency, this cleans up test but also we need to verify that the scene returns to the original hash
            AssetDatabase.DeleteAsset(EntitiesCacheUtility.globalEntitiesDependencyDir);
            AssetDatabase.Refresh();

            // With the dependency deleted, the hash should return to the original
            var finalHash = EntityScenesPaths.GetSubSceneArtifactHash(subScene.SceneGUID, buildSettings, ImportMode.Synchronous);
            Assert.AreEqual(originalHash, finalHash);
        }
    }
}
