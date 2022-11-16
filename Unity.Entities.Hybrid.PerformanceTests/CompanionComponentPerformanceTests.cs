#if !UNITY_DISABLE_MANAGED_COMPONENTS
using System;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Conversion;
using Unity.Entities.Hybrid.Tests;
using Unity.Entities.Tests;
using Unity.Entities.Tests.Conversion;
using Unity.Mathematics;
using Unity.PerformanceTesting;
using Unity.Scenes.Editor.Tests;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Entities.Hybrid.PerformanceTests
{
    class ConversionTestCompanionComponentBaker : Baker<ConversionTestCompanionComponent>
    {
        public override void Bake(ConversionTestCompanionComponent authoring)
        {
            AddComponentObject(authoring);
        }
    }

    [Serializable]
    [TestFixture]
    public class CompanionComponentPerformanceTests_Baking
    {
        [SerializeField] TestWithCustomDefaultGameObjectInjectionWorld m_DefaultWorld;
        [SerializeField] TestWithObjects m_Objects;
        [SerializeField] TestWithTempAssets m_Assets;

        [SetUp]
        public void SetUp()
        {
            m_DefaultWorld.Setup(true);
            m_Objects.SetUp();
            m_Assets.SetUp();
        }

        [TearDown]
        public void TearDown()
        {
            m_DefaultWorld.TearDown();
            m_Objects.TearDown();
            m_Assets.TearDown();
        }

        internal BakingSettings MakeDefaultSettings() => new BakingSettings
        {
            BakingFlags = BakingUtility.BakingFlags.AssignName |
                              BakingUtility.BakingFlags.AddEntityGUID
        };

        [Ignore("DOTS-6907 Physics systems are editor systems as well as baking systems, but after a World.Update (re?)moves entities, physics singletons are no longer available, and the test fails if is is present")]
        [Test, Performance]
        public void CompanionComponent_TransformSync([Values(1, 10, 100, 1000, 9000)] int companionCount)
        {
            BakingUtility.AddAdditionalCompanionComponentType(typeof(ConversionTestCompanionComponent));

            // Convert to create companions
            var gameObjects = new GameObject[companionCount];
            for (int i = 0; i < companionCount; i++)
            {
                var gameObject = m_Objects.CreateGameObject();
                gameObject.AddComponent<ConversionTestCompanionComponent>().SomeValue = 123;
                gameObjects[i] = gameObject;
            }

            using var blobAssetStore = new BlobAssetStore(128);
            var bakingSettings = MakeDefaultSettings();
            bakingSettings.BlobAssetStore = blobAssetStore;
            BakingUtility.BakeGameObjects(m_DefaultWorld.World, gameObjects, bakingSettings);

            // Verify we have created the correct number of companions
            var query = m_DefaultWorld.EntityManager.CreateEntityQuery(typeof(CompanionLink));
            Assert.AreEqual(companionCount, query.CalculateEntityCount());

            var entities = query.ToEntityArray(Allocator.Persistent);
            for (int i = 0; i < entities.Length; i++)
            {
                m_DefaultWorld.World.EntityManager.SetComponentData(entities[i],
#if !ENABLE_TRANSFORM_V1
                    LocalTransform.FromPosition(0.0f, 42f, 0.0f));
#else
                    new Translation {Value = new float3(0.0f, 42f, 0.0f)});
#endif
            }

            var companionGameObjectUpdateTransformSystem =
                m_DefaultWorld.World.GetExistingSystemManaged<CompanionGameObjectUpdateTransformSystem>();
            Measure.ProfilerMarkers(companionGameObjectUpdateTransformSystem.GetProfilerMarkerName());

            // Validate positions not moved
            for (int i = 0; i < entities.Length; i++)
            {
                var companionLink = m_DefaultWorld.World.EntityManager.GetComponentObject<CompanionLink>(entities[i]);
                Assert.AreEqual(0f, companionLink.Companion.transform.localPosition.y);
            }

            m_DefaultWorld.World.Update();
            companionGameObjectUpdateTransformSystem.CompleteDependencyInternal();

            // Validate things moved
            for (int i = 0; i < entities.Length; i++)
            {
                var companionLink = m_DefaultWorld.World.EntityManager.GetComponentObject<CompanionLink>(entities[i]);
                Assert.AreEqual(42f, companionLink.Companion.transform.localPosition.y);
            }

            entities.Dispose();
        }

        [Test, Performance]
        public void CompanionComponent_ConvertScene([Values(1, 10, 100, 1000, 10000)] int numObjects)
        {
            BakingUtility.AddAdditionalCompanionComponentType(typeof(ConversionTestCompanionComponent));

            var scene = SubSceneTestsHelper.CreateScene(m_Assets.GetNextPath() + ".unity");
            for (int i = 0; i < numObjects; i++)
            {
                var go = m_Objects.CreateGameObject("", typeof(ConversionTestCompanionComponent));
                SceneManager.MoveGameObjectToScene(go, scene);
            }

            using (var bakingWorld = new World("FullBake"))
            using (var blobAssetStore = new BlobAssetStore(128))
            {
                var bakingSettings = MakeDefaultSettings();
                bakingSettings.BlobAssetStore = blobAssetStore;
                bakingSettings.BakingFlags =
                    BakingUtility.BakingFlags.GameViewLiveConversion |
                    BakingUtility.BakingFlags.AddEntityGUID;

                Measure.Method(() => { BakingUtility.BakeScene(bakingWorld, scene, bakingSettings, false, null); })
                    .WarmupCount(2)
                    .MeasurementCount(10)
                    .Run();
            }
        }
    }
}
#endif
