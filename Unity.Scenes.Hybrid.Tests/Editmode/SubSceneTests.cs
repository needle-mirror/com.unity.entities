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
using Unity.Scenes.Editor.Tests;


namespace Unity.Scenes.Hybrid.Tests
{
    public class SubSceneTestsConversion : SubSceneTests
    {
        private TestLiveConversionSettings m_Settings;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            m_Settings.Setup(false);
            base.SetUpOnce();
        }

        [OneTimeTearDown]
        public void OneTimeTeardown()
        {
            base.TearDownOnce();
            m_Settings.TearDown();
        }
    }

    public class SubSceneTestsBaking : SubSceneTests
    {
        private TestLiveConversionSettings m_Settings;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            m_Settings.Setup(true);
            base.SetUpOnce();
        }

        [OneTimeTearDown]
        public void OneTimeTeardown()
        {
            base.TearDownOnce();
            m_Settings.TearDown();
        }
    }

    public abstract class SubSceneTests : SubSceneTestFixture
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

                var worldAScene = SceneSystem.LoadSceneAsync(worldA.Unmanaged, SceneGUID);
                var worldBScene = SceneSystem.LoadSceneAsync(worldB.Unmanaged, SceneGUID);

                Assert.IsFalse(SceneSystem.IsSceneLoaded(worldA.Unmanaged, worldAScene));
                Assert.IsFalse(SceneSystem.IsSceneLoaded(worldB.Unmanaged, worldBScene));

                while (!SceneSystem.IsSceneLoaded(worldA.Unmanaged, worldAScene) || 
                       !SceneSystem.IsSceneLoaded(worldB.Unmanaged, worldBScene))
                {
                    worldA.Update();
                    worldB.Update();
                    yield return null;
                }

                var worldAEntities = worldA.EntityManager.GetAllEntities(worldA.UpdateAllocator.ToAllocator);
                var worldBEntities = worldB.EntityManager.GetAllEntities(worldB.UpdateAllocator.ToAllocator);
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
                var sharedEntitiesA = worldAQuery.ToEntityArray(worldA.UpdateAllocator.ToAllocator);
                var sharedEntitiesB = worldBQuery.ToEntityArray(worldB.UpdateAllocator.ToAllocator);

                SharedWithMaterial sharedA;
                SharedWithMaterial sharedB;
                using (sharedEntitiesA)
                using (sharedEntitiesB)
                {
                    sharedA = worldA.EntityManager.GetSharedComponentManaged<SharedWithMaterial>(sharedEntitiesA[0]);
                    sharedB = worldB.EntityManager.GetSharedComponentManaged<SharedWithMaterial>(sharedEntitiesB[0]);
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

                SceneSystem.UnloadScene(worldA.Unmanaged, worldAScene);
                worldA.Update();

                SceneSystem.UnloadScene(worldB.Unmanaged, worldBScene);
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

                var worldAScene = SceneSystem.LoadSceneAsync(worldA.Unmanaged, SceneGUID, loadParams);
                var worldBScene = SceneSystem.LoadSceneAsync(worldB.Unmanaged, SceneGUID, loadParams);
                Assert.IsFalse(SceneSystem.IsSceneLoaded(worldA.Unmanaged, worldAScene));
                Assert.IsFalse(SceneSystem.IsSceneLoaded(worldB.Unmanaged, worldBScene));

                worldA.Update();
                worldB.Update();

                Assert.IsTrue(SceneSystem.IsSceneLoaded(worldA.Unmanaged, worldAScene));
                Assert.IsTrue(SceneSystem.IsSceneLoaded(worldB.Unmanaged, worldBScene));

                var worldAEntities = worldA.EntityManager.GetAllEntities(worldA.UpdateAllocator.ToAllocator);
                var worldBEntities = worldB.EntityManager.GetAllEntities(worldB.UpdateAllocator.ToAllocator);
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
                var sharedEntitiesA = worldAQuery.ToEntityArray(worldA.UpdateAllocator.ToAllocator);
                var sharedEntitiesB = worldBQuery.ToEntityArray(worldB.UpdateAllocator.ToAllocator);

                SharedWithMaterial sharedA;
                SharedWithMaterial sharedB;
                using (sharedEntitiesA)
                using (sharedEntitiesB)
                {
                    sharedA = worldA.EntityManager.GetSharedComponentManaged<SharedWithMaterial>(sharedEntitiesA[0]);
                    sharedB = worldB.EntityManager.GetSharedComponentManaged<SharedWithMaterial>(sharedEntitiesB[0]);
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

                SceneSystem.UnloadScene(worldA.Unmanaged, worldAScene);
                worldA.Update();

                SceneSystem.UnloadScene(worldB.Unmanaged, worldBScene);
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
                if (addCommandBufferToSection)
                {
                    var resolveParams = new SceneSystem.LoadParameters
                    {
                        Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.DisableAutoLoad
                    };
                    SceneSystem.LoadSceneAsync(world.Unmanaged, SceneGUID, resolveParams);
                    world.Update();
                    var section = world.EntityManager.CreateEntityQuery(typeof(SceneSectionData)).GetSingletonEntity();
                    world.EntityManager.AddComponentData(section, postLoadCommandBuffer);
                }

                var loadParams = new SceneSystem.LoadParameters
                {
                    Flags = loadAsync ? 0 : SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn
                };

                Assert.IsTrue(SceneGUID.IsValid);

                var scene = SceneSystem.LoadSceneAsync(world.Unmanaged, SceneGUID, loadParams);
                if (!addCommandBufferToSection)
                    world.EntityManager.AddComponentData(scene, postLoadCommandBuffer);

                if (loadAsync)
                {
                    while (!SceneSystem.IsSceneLoaded(world.Unmanaged, scene))
                    {
                        world.Update();
                        yield return null;
                    }
                }
                else
                {
                    world.Update();
                    Assert.IsTrue(SceneSystem.IsSceneLoaded(world.Unmanaged, scene));
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
                var resolvedScene = SceneSystem.LoadSceneAsync(world.Unmanaged, SceneGUID, new SceneSystem.LoadParameters {AutoLoad = false, Flags = SceneLoadFlags.BlockOnImport});
                world.Update();

                Assert.IsTrue(world.EntityManager.HasComponent<ResolvedSectionEntity>(resolvedScene));

                var loadParams = new SceneSystem.LoadParameters
                {
                    Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn
                };

                Assert.IsTrue(SceneGUID.IsValid);

                var scene1 = world.EntityManager.Instantiate(resolvedScene);
                world.EntityManager.AddComponentData(scene1, postLoadCommandBuffer1);
                SceneSystem.LoadSceneAsync(world.Unmanaged, scene1, loadParams);


                var scene2 = world.EntityManager.Instantiate(resolvedScene);
                world.EntityManager.AddComponentData(scene2, postLoadCommandBuffer2);
                SceneSystem.LoadSceneAsync(world.Unmanaged, scene2, loadParams);

                world.Update();

                var ecsTestDataQuery = world.EntityManager.CreateEntityQuery(typeof(TestProcessAfterLoadData));
                using (var ecsTestDataArray = ecsTestDataQuery.ToComponentDataArray<TestProcessAfterLoadData>(world.UpdateAllocator.ToAllocator))
                {
                    CollectionAssert.AreEquivalent(new[] {8, 43}, ecsTestDataArray.ToArray().Select(e => e.Value));
                }

                world.EntityManager.DestroyEntity(scene1);
                world.Update();
                using (var ecsTestDataArray = ecsTestDataQuery.ToComponentDataArray<TestProcessAfterLoadData>(world.UpdateAllocator.ToAllocator))
                {
                    CollectionAssert.AreEquivalent(new[] {8}, ecsTestDataArray.ToArray().Select(e => e.Value));
                }

                world.EntityManager.DestroyEntity(scene2);
                world.Update();
                using (var ecsTestDataArray = ecsTestDataQuery.ToComponentDataArray<TestProcessAfterLoadData>(world.UpdateAllocator.ToAllocator))
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
                var loadParams = new SceneSystem.LoadParameters
                {
                    Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn | SceneLoadFlags.NewInstance
                };

                Assert.IsTrue(SceneGUID.IsValid);

                var scene1 = SceneSystem.LoadSceneAsync(world.Unmanaged, SceneGUID, loadParams);
                world.EntityManager.AddComponentData(scene1, postLoadCommandBuffer1);

                var scene2 = SceneSystem.LoadSceneAsync(world.Unmanaged, SceneGUID, loadParams);
                world.EntityManager.AddComponentData(scene2, postLoadCommandBuffer2);

                world.Update();

                var ecsTestDataQuery = world.EntityManager.CreateEntityQuery(typeof(TestProcessAfterLoadData));
                using (var ecsTestDataArray = ecsTestDataQuery.ToComponentDataArray<TestProcessAfterLoadData>(world.UpdateAllocator.ToAllocator))
                {
                    CollectionAssert.AreEquivalent(new[] {8, 43}, ecsTestDataArray.ToArray().Select(e => e.Value));
                }
            }
        }

        [WorldSystemFilter(WorldSystemFilterFlags.ProcessAfterLoad)]
        private class Group1 : ComponentSystemGroup {}

        [UpdateBefore(typeof(Group1))]
        [WorldSystemFilter(WorldSystemFilterFlags.ProcessAfterLoad)]
        private class Group2 : ComponentSystemGroup {}

        [UpdateInGroup(typeof(Group1))]
        [WorldSystemFilter(WorldSystemFilterFlags.ProcessAfterLoad)]
        private partial class System1 : SystemBase
        {
            public static int CounterRead;
            protected override void OnUpdate()
            {
                CounterRead = s_Counter++;
            }
        }

        [UpdateInGroup(typeof(Group2))]
        [WorldSystemFilter(WorldSystemFilterFlags.ProcessAfterLoad)]
        private partial class System2 : SystemBase
        {
            public static int CounterRead;
            protected override void OnUpdate()
            {
                CounterRead = s_Counter++;
            }
        }

        private static int s_Counter = 0;

        [Test]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public void PostProcessAfterLoadGroup_SupportsSystemGroups()
        {
            using (var world = TestWorldSetup.CreateEntityWorld("World", false))
            {
                var loadParams = new SceneSystem.LoadParameters
                {
                    Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn | SceneLoadFlags.NewInstance
                };
                Assert.IsTrue(SceneGUID.IsValid);

                SceneSystem.LoadSceneAsync(world.Unmanaged, SceneGUID, loadParams);
                world.Update();
                Assert.Greater(System1.CounterRead, System2.CounterRead);
            }
        }

        [UnityTest]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public IEnumerator SubscenesCompleteLoading_When_ConcurrentSectionStreamCountIsSetTo0()
        {
            var postLoadCommandBuffers =
                Enumerable.Range(1, 10).Select(i => CreateTestProcessAfterLoadDataCommandBuffer(i)).ToArray();

            using (var world = TestWorldSetup.CreateEntityWorld("World", false))
            {
                var sceneSectionStreamingSystem = world.GetExistingSystemManaged<SceneSectionStreamingSystem>();

                var loadParams = new SceneSystem.LoadParameters
                {
                    Flags = SceneLoadFlags.NewInstance | SceneLoadFlags.BlockOnImport
                };

                Assert.IsTrue(SceneGUID.IsValid);

                sceneSectionStreamingSystem.MaximumWorldsMovedPerUpdate = 0;

                var scenes = postLoadCommandBuffers.Select(cb =>
                {
                    var scene = SceneSystem.LoadSceneAsync(world.Unmanaged, SceneGUID, loadParams);
                    world.EntityManager.AddComponentData(scene, cb);
                    return scene;
                }).ToArray();

                // Increase ConcurrentSectionStreamCount to 10 so all streams are started in the first update
                sceneSectionStreamingSystem.ConcurrentSectionStreamCount = 10;
                world.Update();
                world.GetExistingSystemManaged<SceneSectionStreamingSystem>().ConcurrentSectionStreamCount = 0;
                //All streams should still be in progress
                Assert.AreEqual(10, sceneSectionStreamingSystem.StreamArrayLength);

                while (!sceneSectionStreamingSystem.AllStreamsComplete)
                {
                    world.Update();
                    yield return null;
                }

                var ecsTestDataQuery = world.EntityManager.CreateEntityQuery(typeof(TestProcessAfterLoadData));
                // No scenes should have been moved yet since MaximumMoveEntitiesFromPerFrame is 0
                Assert.AreEqual(0, ecsTestDataQuery.CalculateEntityCount());

                // Move all scenes in one update
                sceneSectionStreamingSystem.MaximumWorldsMovedPerUpdate = 10;
                world.Update();

                //All streams are completed so the stream array should have been resized to ConcurrentSectionStreamCount (0)
                Assert.AreEqual(0, sceneSectionStreamingSystem.StreamArrayLength);
                using (var ecsTestDataArray = ecsTestDataQuery.ToComponentDataArray<TestProcessAfterLoadData>(world.UpdateAllocator.ToAllocator))
                {
                    CollectionAssert.AreEquivalent(Enumerable.Range(2, 10), ecsTestDataArray.ToArray().Select(e => e.Value));
                }
                sceneSectionStreamingSystem.ConcurrentSectionStreamCount = 5;
                Assert.AreEqual(5, sceneSectionStreamingSystem.StreamArrayLength);
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
