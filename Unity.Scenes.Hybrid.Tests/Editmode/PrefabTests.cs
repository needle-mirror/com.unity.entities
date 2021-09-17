using System.Collections;
using System.Linq;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.TestTools;
#endif
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Entities.Tests;


namespace Unity.Scenes.Hybrid.Tests
{
    public class PrefabTests : SubSceneTestFixture
    {
        public PrefabTests() : base("Packages/com.unity.entities/Unity.Scenes.Hybrid.Tests/TestSceneWithSubScene/TestPrefab.prefab")
        {
        }

        [UnityTest]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public IEnumerator CanLoadPrefabAsScene()
        {
            using (var world = TestWorldSetup.CreateEntityWorld("World", false))
            {
                var em = world.EntityManager;
                var sceneSystem = world.GetExistingSystem<SceneSystem>();
                var sceneSectionStreamingSystem = world.GetExistingSystem<SceneSectionStreamingSystem>();

                var loadParams = new SceneSystem.LoadParameters
                {
                    Flags = SceneLoadFlags.BlockOnImport
                };

                Assert.IsTrue(SceneGUID.IsValid);
                var prefabSceneEntity = sceneSystem.LoadSceneAsync(SceneGUID, loadParams);
                world.Update();
                while (!sceneSectionStreamingSystem.AllStreamsComplete)
                {
                    world.Update();
                    yield return null;
                }

                var ecsTestDataQuery = em.CreateEntityQuery(typeof(SubSceneSectionTestData));
                var ecsTestDataInPrefabQuery = em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] {ComponentType.ReadOnly<SubSceneSectionTestData>()},
                    Options = EntityQueryOptions.IncludePrefab
                });
                Assert.AreEqual(0, ecsTestDataQuery.CalculateEntityCount());
                Assert.AreEqual(2, ecsTestDataInPrefabQuery.CalculateEntityCount());
                Assert.IsTrue(em.HasComponent<PrefabRoot>(prefabSceneEntity));
                var prefabRoot = em.GetComponentData<PrefabRoot>(prefabSceneEntity).Root;
                Assert.AreNotEqual(prefabRoot, Entity.Null);
                Assert.IsTrue(em.HasComponent<LinkedEntityGroup>(prefabRoot));
                var instance = em.Instantiate(prefabRoot);
                Assert.AreEqual(2, ecsTestDataQuery.CalculateEntityCount());
                Assert.AreEqual(4, ecsTestDataInPrefabQuery.CalculateEntityCount());
            }
        }

        [UnityTest]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public IEnumerator CanLoadPrefabWithWeakAssetReference()
        {
            using (var world = TestWorldSetup.CreateEntityWorld("World", false))
            {
                Assert.IsTrue(SceneGUID.IsValid);
                var prefab = new EntityPrefabReference(SceneGUID);
                var em = world.EntityManager;

                var requestEntity = em.CreateEntity();
                em.AddComponentData(requestEntity, new RequestEntityPrefabLoaded {Prefab = prefab});

                world.Update();
                Assert.IsTrue(em.HasComponent<PrefabAssetReference>(requestEntity));

                while (!em.HasComponent<PrefabLoadResult>(requestEntity))
                {
                    world.Update();
                    yield return null;
                }

                var ecsTestDataQuery = em.CreateEntityQuery(typeof(SubSceneSectionTestData));
                var ecsTestDataInPrefabQuery = em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] {ComponentType.ReadOnly<SubSceneSectionTestData>()},
                    Options = EntityQueryOptions.IncludePrefab
                });
                Assert.AreEqual(0, ecsTestDataQuery.CalculateEntityCount());
                Assert.AreEqual(2, ecsTestDataInPrefabQuery.CalculateEntityCount());
                var prefabRoot = em.GetComponentData<PrefabLoadResult>(requestEntity).PrefabRoot;
                Assert.AreNotEqual(prefabRoot, Entity.Null);
                Assert.IsTrue(em.HasComponent<LinkedEntityGroup>(prefabRoot));
                var instance = em.Instantiate(prefabRoot);
                Assert.AreEqual(2, ecsTestDataQuery.CalculateEntityCount());
                Assert.AreEqual(4, ecsTestDataInPrefabQuery.CalculateEntityCount());

                // delete request entity and the prefab instance to release reference and unload prefab
                em.DestroyEntity(instance);
                world.Update();
                Assert.AreEqual(0, ecsTestDataQuery.CalculateEntityCount());
                Assert.AreEqual(2, ecsTestDataInPrefabQuery.CalculateEntityCount());

                em.DestroyEntity(requestEntity);
                world.Update();
                Assert.AreEqual(0, ecsTestDataQuery.CalculateEntityCount());
                Assert.AreEqual(0, ecsTestDataInPrefabQuery.CalculateEntityCount());
            }
        }

        [UnityTest]
        [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public IEnumerator TwoLoadRequestsLoadSamePrefab([Values] bool waitForLoadBetweenRequests)
        {
            using (var world = TestWorldSetup.CreateEntityWorld("World", false))
            {
                Assert.IsTrue(SceneGUID.IsValid);
                var prefab = new EntityPrefabReference(SceneGUID);
                var em = world.EntityManager;

                var requestEntity1 = em.CreateEntity();
                em.AddComponentData(requestEntity1, new RequestEntityPrefabLoaded {Prefab = prefab});

                world.Update();
                if (waitForLoadBetweenRequests)
                {
                    while (!em.HasComponent<PrefabLoadResult>(requestEntity1))
                    {
                        world.Update();
                        yield return null;
                    }
                }

                var requestEntity2 = em.CreateEntity();
                em.AddComponentData(requestEntity2, new RequestEntityPrefabLoaded {Prefab = prefab});

                Assert.IsTrue(em.HasComponent<PrefabAssetReference>(requestEntity1));
                world.Update();
                Assert.IsTrue(em.HasComponent<PrefabAssetReference>(requestEntity2));

                if (!waitForLoadBetweenRequests)
                {
                    while (!em.HasComponent<PrefabLoadResult>(requestEntity1))
                    {
                        world.Update();
                        yield return null;
                    }
                }

                Assert.AreEqual(em.GetComponentData<PrefabLoadResult>(requestEntity1).PrefabRoot,
                    em.GetComponentData<PrefabLoadResult>(requestEntity2).PrefabRoot);

                var ecsTestDataQuery = em.CreateEntityQuery(typeof(SubSceneSectionTestData));
                var ecsTestDataInPrefabQuery = em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] {ComponentType.ReadOnly<SubSceneSectionTestData>()},
                    Options = EntityQueryOptions.IncludePrefab
                });
                Assert.AreEqual(0, ecsTestDataQuery.CalculateEntityCount());
                Assert.AreEqual(2, ecsTestDataInPrefabQuery.CalculateEntityCount());

                // delete request entity to release reference and unload prefab
                em.DestroyEntity(requestEntity1);
                world.Update();
                Assert.AreEqual(2, ecsTestDataInPrefabQuery.CalculateEntityCount());
                em.DestroyEntity(requestEntity2);
                world.Update();
                Assert.AreEqual(0, ecsTestDataInPrefabQuery.CalculateEntityCount());
            }
        }
	}
}
