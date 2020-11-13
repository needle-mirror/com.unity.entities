using System.Collections;
using System.Linq;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.TestTools;
#endif
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Tests;


namespace Unity.Scenes.Hybrid.Tests
{
    public class SubSceneTests : SubSceneTestFixture
    {
        public SubSceneTests() : base("Packages/com.unity.entities/Unity.Scenes.Hybrid.Tests/TestSceneWithSubScene/TestSubScene.unity")
        {
        }

        [UnityTest]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public IEnumerator LoadMultipleSubscenes_Async_WithAssetBundles()
        {
            using (var worldA = TestWorldSetup.CreateEntityWorld("World A", false))
            using (var worldB = TestWorldSetup.CreateEntityWorld("World B", false))
            {
                var sceneSystemA = worldA.GetExistingSystem<SceneSystem>();
                var sceneSystemB = worldB.GetExistingSystem<SceneSystem>();
                Assert.IsTrue(SceneGUID.IsValid);

                var worldAScene = sceneSystemA.LoadSceneAsync(SceneGUID);
                var worldBScene = sceneSystemB.LoadSceneAsync(SceneGUID);

                Assert.IsFalse(sceneSystemA.IsSceneLoaded(worldAScene));
                Assert.IsFalse(sceneSystemB.IsSceneLoaded(worldBScene));

                while (!sceneSystemA.IsSceneLoaded(worldAScene) || !sceneSystemB.IsSceneLoaded(worldBScene))
                {
                    worldA.Update();
                    worldB.Update();
                    yield return null;
                }

                var worldAEntities = worldA.EntityManager.GetAllEntities(Allocator.TempJob);
                var worldBEntities = worldB.EntityManager.GetAllEntities(Allocator.TempJob);
                using (worldAEntities)
                using (worldBEntities)
                {
                    Assert.AreEqual(worldAEntities.Length, worldBEntities.Length);
                }

                var worldAQuery = worldA.EntityManager.CreateEntityQuery(typeof(SharedWithMaterial));
                var worldBQuery = worldB.EntityManager.CreateEntityQuery(typeof(SharedWithMaterial));
                Assert.AreEqual(worldAQuery.CalculateEntityCount(), worldBQuery.CalculateEntityCount());
                Assert.AreEqual(1, worldAQuery.CalculateEntityCount());

                // Get Material on RenderMesh
                var sharedEntitiesA = worldAQuery.ToEntityArray(Allocator.TempJob);
                var sharedEntitiesB = worldBQuery.ToEntityArray(Allocator.TempJob);

                SharedWithMaterial sharedA;
                SharedWithMaterial sharedB;
                using (sharedEntitiesA)
                using (sharedEntitiesB)
                {
                    sharedA = worldA.EntityManager.GetSharedComponentData<SharedWithMaterial>(sharedEntitiesA[0]);
                    sharedB = worldB.EntityManager.GetSharedComponentData<SharedWithMaterial>(sharedEntitiesB[0]);
                }

                Assert.AreSame(sharedA.material, sharedB.material);
                Assert.IsTrue(sharedA.material != null, "sharedA.material != null");

                var material = sharedA.material;

#if !UNITY_EDITOR
                Assert.AreEqual(1, SceneBundleHandle.GetLoadedCount());
#else
                Assert.AreEqual(0, SceneBundleHandle.GetLoadedCount());
#endif
                Assert.AreEqual(0, SceneBundleHandle.GetUnloadingCount());

                worldA.GetOrCreateSystem<SceneSystem>().UnloadScene(worldAScene);
                worldA.Update();

                worldB.GetOrCreateSystem<SceneSystem>().UnloadScene(worldBScene);
                worldB.Update();

                Assert.AreEqual(0, SceneBundleHandle.GetLoadedCount());
                Assert.AreEqual(0, SceneBundleHandle.GetUnloadingCount());
#if !UNITY_EDITOR
                Assert.IsTrue(material == null);
#endif
            }
        }

        // Only works in Editor for now until we can support SubScene building with new build settings in a test
        [Test]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public void LoadMultipleSubscenes_Blocking_WithAssetBundles()
        {
            using (var worldA = TestWorldSetup.CreateEntityWorld("World A", false))
            using (var worldB = TestWorldSetup.CreateEntityWorld("World B", false))
            {
                var loadParams = new SceneSystem.LoadParameters
                {
                    Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn
                };

                Assert.IsTrue(SceneGUID.IsValid);

                var worldAScene = worldA.GetOrCreateSystem<SceneSystem>().LoadSceneAsync(SceneGUID, loadParams);
                var worldBScene = worldB.GetOrCreateSystem<SceneSystem>().LoadSceneAsync(SceneGUID, loadParams);
                Assert.IsFalse(worldA.GetExistingSystem<SceneSystem>().IsSceneLoaded(worldAScene));
                Assert.IsFalse(worldB.GetExistingSystem<SceneSystem>().IsSceneLoaded(worldBScene));

                worldA.Update();
                worldB.Update();

                Assert.IsTrue(worldA.GetExistingSystem<SceneSystem>().IsSceneLoaded(worldAScene));
                Assert.IsTrue(worldB.GetExistingSystem<SceneSystem>().IsSceneLoaded(worldBScene));

                var worldAEntities = worldA.EntityManager.GetAllEntities(Allocator.TempJob);
                var worldBEntities = worldB.EntityManager.GetAllEntities(Allocator.TempJob);
                using (worldAEntities)
                using (worldBEntities)
                {
                    Assert.AreEqual(worldAEntities.Length, worldBEntities.Length);
                }

                var worldAQuery = worldA.EntityManager.CreateEntityQuery(typeof(SharedWithMaterial));
                var worldBQuery = worldB.EntityManager.CreateEntityQuery(typeof(SharedWithMaterial));
                Assert.AreEqual(worldAQuery.CalculateEntityCount(), worldBQuery.CalculateEntityCount());
                Assert.AreEqual(1, worldAQuery.CalculateEntityCount());

                // Get Material on RenderMesh
                var sharedEntitiesA = worldAQuery.ToEntityArray(Allocator.TempJob);
                var sharedEntitiesB = worldBQuery.ToEntityArray(Allocator.TempJob);

                SharedWithMaterial sharedA;
                SharedWithMaterial sharedB;
                using (sharedEntitiesA)
                using (sharedEntitiesB)
                {
                    sharedA = worldA.EntityManager.GetSharedComponentData<SharedWithMaterial>(sharedEntitiesA[0]);
                    sharedB = worldB.EntityManager.GetSharedComponentData<SharedWithMaterial>(sharedEntitiesB[0]);
                }

                Assert.AreSame(sharedA.material, sharedB.material);
                Assert.IsTrue(sharedA.material != null, "sharedA.material != null");

                var material = sharedA.material;

#if !UNITY_EDITOR
                Assert.AreEqual(1, SceneBundleHandle.GetLoadedCount());
#else
                Assert.AreEqual(0, SceneBundleHandle.GetLoadedCount());
#endif
                Assert.AreEqual(0, SceneBundleHandle.GetUnloadingCount());

                worldA.GetOrCreateSystem<SceneSystem>().UnloadScene(worldAScene);
                worldA.Update();

                worldB.GetOrCreateSystem<SceneSystem>().UnloadScene(worldBScene);
                worldB.Update();

                Assert.AreEqual(0, SceneBundleHandle.GetLoadedCount());
                Assert.AreEqual(0, SceneBundleHandle.GetUnloadingCount());
#if !UNITY_EDITOR
                Assert.IsTrue(material == null);
#endif
            }
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS // PostLoadCommandBuffer is a managed component
        private static PostLoadCommandBuffer CreateTestProcessAfterLoadDataCommandBuffer(int value)
        {
            var postLoadCommandBuffer = new PostLoadCommandBuffer();
            postLoadCommandBuffer.CommandBuffer = new EntityCommandBuffer(Allocator.Persistent, PlaybackPolicy.MultiPlayback);
            var postLoadEntity = postLoadCommandBuffer.CommandBuffer.CreateEntity();
            postLoadCommandBuffer.CommandBuffer.AddComponent(postLoadEntity, new TestProcessAfterLoadData {Value = value});
            return postLoadCommandBuffer;
        }

        // Only works in Editor for now until we can support SubScene building with new build settings in a test
        [UnityTest]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public IEnumerator LoadSubscene_With_PostLoadCommandBuffer([Values] bool loadAsync, [Values] bool addCommandBufferToSection)
        {
            var postLoadCommandBuffer = CreateTestProcessAfterLoadDataCommandBuffer(42);

            using (var world = TestWorldSetup.CreateEntityWorld("World", false))
            {
                var sceneSystem = world.GetExistingSystem<SceneSystem>();
                if (addCommandBufferToSection)
                {
                    var resolveParams = new SceneSystem.LoadParameters
                    {
                        Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.DisableAutoLoad
                    };
                    sceneSystem.LoadSceneAsync(SceneGUID, resolveParams);
                    world.Update();
                    var section = world.EntityManager.CreateEntityQuery(typeof(SceneSectionData)).GetSingletonEntity();
                    world.EntityManager.AddComponentData(section, postLoadCommandBuffer);
                }

                var loadParams = new SceneSystem.LoadParameters
                {
                    Flags = loadAsync ? 0 : SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn
                };

                Assert.IsTrue(SceneGUID.IsValid);

                var scene = sceneSystem.LoadSceneAsync(SceneGUID, loadParams);
                if (!addCommandBufferToSection)
                    world.EntityManager.AddComponentData(scene, postLoadCommandBuffer);

                if (loadAsync)
                {
                    while (!sceneSystem.IsSceneLoaded(scene))
                    {
                        world.Update();
                        yield return null;
                    }
                }
                else
                {
                    world.Update();
                    Assert.IsTrue(sceneSystem.IsSceneLoaded(scene));
                }

                var ecsTestDataQuery = world.EntityManager.CreateEntityQuery(typeof(TestProcessAfterLoadData));
                Assert.AreEqual(1, ecsTestDataQuery.CalculateEntityCount());
                Assert.AreEqual(43, ecsTestDataQuery.GetSingleton<TestProcessAfterLoadData>().Value);
            }

            // Check that command buffer has been Disposed
            Assert.IsFalse(postLoadCommandBuffer.CommandBuffer.IsCreated);
        }

        [Test]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public void Load_MultipleInstancesOfSameSubScene_By_Instantiating_ResolvedScene()
        {
            var postLoadCommandBuffer1 = CreateTestProcessAfterLoadDataCommandBuffer(42);
            var postLoadCommandBuffer2 = CreateTestProcessAfterLoadDataCommandBuffer(7);

            using (var world = TestWorldSetup.CreateEntityWorld("World", false))
            {
                var sceneSystem = world.GetExistingSystem<SceneSystem>();
                var resolvedScene = sceneSystem.LoadSceneAsync(SceneGUID, new SceneSystem.LoadParameters {AutoLoad = false, Flags = SceneLoadFlags.BlockOnImport});
                world.Update();

                Assert.IsTrue(world.EntityManager.HasComponent<ResolvedSectionEntity>(resolvedScene));

                var loadParams = new SceneSystem.LoadParameters
                {
                    Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn
                };

                Assert.IsTrue(SceneGUID.IsValid);

                var scene1 = world.EntityManager.Instantiate(resolvedScene);
                world.EntityManager.AddComponentData(scene1, postLoadCommandBuffer1);
                sceneSystem.LoadSceneAsync(scene1, loadParams);


                var scene2 = world.EntityManager.Instantiate(resolvedScene);
                world.EntityManager.AddComponentData(scene2, postLoadCommandBuffer2);
                sceneSystem.LoadSceneAsync(scene2, loadParams);

                world.Update();

                var ecsTestDataQuery = world.EntityManager.CreateEntityQuery(typeof(TestProcessAfterLoadData));
                using (var ecsTestDataArray = ecsTestDataQuery.ToComponentDataArray<TestProcessAfterLoadData>(Allocator.TempJob))
                {
                    CollectionAssert.AreEquivalent(new[] {8, 43}, ecsTestDataArray.ToArray().Select(e => e.Value));
                }

                world.EntityManager.DestroyEntity(scene1);
                world.Update();
                using (var ecsTestDataArray = ecsTestDataQuery.ToComponentDataArray<TestProcessAfterLoadData>(Allocator.TempJob))
                {
                    CollectionAssert.AreEquivalent(new[] {8}, ecsTestDataArray.ToArray().Select(e => e.Value));
                }

                world.EntityManager.DestroyEntity(scene2);
                world.Update();
                using (var ecsTestDataArray = ecsTestDataQuery.ToComponentDataArray<TestProcessAfterLoadData>(Allocator.TempJob))
                {
                    Assert.AreEqual(0, ecsTestDataArray.Length);
                }
            }
        }

        [Test]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public void Load_MultipleInstancesOfSameSubScene_With_NewInstance_Flag()
        {
            var postLoadCommandBuffer1 = CreateTestProcessAfterLoadDataCommandBuffer(42);
            var postLoadCommandBuffer2 = CreateTestProcessAfterLoadDataCommandBuffer(7);

            using (var world = TestWorldSetup.CreateEntityWorld("World", false))
            {
                var sceneSystem = world.GetExistingSystem<SceneSystem>();
                var loadParams = new SceneSystem.LoadParameters
                {
                    Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn | SceneLoadFlags.NewInstance
                };

                Assert.IsTrue(SceneGUID.IsValid);

                var scene1 = sceneSystem.LoadSceneAsync(SceneGUID, loadParams);
                world.EntityManager.AddComponentData(scene1, postLoadCommandBuffer1);

                var scene2 = sceneSystem.LoadSceneAsync(SceneGUID, loadParams);
                world.EntityManager.AddComponentData(scene2, postLoadCommandBuffer2);

                world.Update();

                var ecsTestDataQuery = world.EntityManager.CreateEntityQuery(typeof(TestProcessAfterLoadData));
                using (var ecsTestDataArray = ecsTestDataQuery.ToComponentDataArray<TestProcessAfterLoadData>(Allocator.TempJob))
                {
                    CollectionAssert.AreEquivalent(new[] {8, 43}, ecsTestDataArray.ToArray().Select(e => e.Value));
                }
            }
        }
#endif
    }

    public struct TestProcessAfterLoadData : IComponentData
    {
        public int Value;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ProcessAfterLoad)]
    public partial class IncrementEcsTestDataProcessAfterLoadSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((ref TestProcessAfterLoadData data) =>
            {
                data.Value++;
            }).Run();
        }
    }
}
