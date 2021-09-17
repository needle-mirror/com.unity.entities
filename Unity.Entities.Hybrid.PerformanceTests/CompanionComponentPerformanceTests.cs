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
    public class MonoBehaviourComponentConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            AddTypeToCompanionWhiteList(typeof(ConversionTestCompanionComponent));

            Entities.ForEach((ConversionTestCompanionComponent component) =>
            {
                var entity = GetPrimaryEntity(component);
                DstEntityManager.AddComponentObject(entity, component);
            });
        }
    }

    [Serializable]
    [TestFixture]
    public class CompanionComponentPerformanceTests
    {
        [SerializeField] TestWithCustomDefaultGameObjectInjectionWorld m_DefaultWorld;
        [SerializeField] TestWithObjects m_Objects;
        [SerializeField] TestWithTempAssets m_Assets;

        protected GameObjectConversionSettings MakeDefaultSettings(World world) => new GameObjectConversionSettings
        {
            DestinationWorld = world,
            ConversionFlags = GameObjectConversionUtility.ConversionFlags.AssignName,
            Systems = TestWorldSetup.GetDefaultInitSystemsFromEntitiesPackage(WorldSystemFilterFlags.GameObjectConversion).ToList()
        };

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

        [Test, Performance]
        public void CompanionComponent_Companion_TransformSync([Values(1, 10, 100, 1000, 9000)] int companionCount)
        {
            // Convert to create companions
            for (int i = 0; i < companionCount; i++)
            {
                var gameObject = m_Objects.CreateGameObject();
                gameObject.AddComponent<ConversionTestCompanionComponent>().SomeValue = 123;
                GameObjectConversionUtility.ConvertGameObjectHierarchy(gameObject, MakeDefaultSettings(m_DefaultWorld.World).WithExtraSystem<MonoBehaviourComponentConversionSystem>());
            }

            // Verify we have created the correct number of companions
            var query = m_DefaultWorld.EntityManager.CreateEntityQuery(typeof(CompanionLink));
            Assert.AreEqual(companionCount, query.CalculateEntityCount());

            var entities = query.ToEntityArray(Allocator.Persistent);
            for (int i = 0; i < entities.Length; i++)
            {
                m_DefaultWorld.World.EntityManager.SetComponentData(entities[i], new Translation{Value=new float3(0.0f, 42f, 0.0f)});
            }

            var companionGameObjectUpdateTransformSystem = m_DefaultWorld.World.GetExistingSystem<CompanionGameObjectUpdateTransformSystem>();
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
        public void CompanionComponent_Conversion_ConvertScene([Values(1, 10, 100, 1000, 10000)]int numObjects)
        {
            var scene = SubSceneTestsHelper.CreateScene(m_Assets.GetNextPath() + ".unity");
            for (int i = 0; i < numObjects; i++)
            {
                var go = m_Objects.CreateGameObject("", typeof(ConversionTestCompanionComponent));
                SceneManager.MoveGameObjectToScene(go, scene);
            }

            using (var fullConversionWorld = new World("FullConversion"))
            using (var blobAssetStore = new BlobAssetStore())
            {
                var conversionSettings = GameObjectConversionSettings.FromWorld(fullConversionWorld, blobAssetStore);
                conversionSettings.ConversionFlags =
                    GameObjectConversionUtility.ConversionFlags.GameViewLiveConversion |
                    GameObjectConversionUtility.ConversionFlags.AddEntityGUID;

                Measure.Method(() =>
                {
                    GameObjectConversionUtility.ConvertScene(scene, conversionSettings);
                }).MeasurementCount(100);
            }
        }
    }
}
#endif
