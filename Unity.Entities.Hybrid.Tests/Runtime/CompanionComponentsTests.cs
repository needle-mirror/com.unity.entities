#if !UNITY_DISABLE_MANAGED_COMPONENTS
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Hybrid.Tests;
using Unity.Entities.Tests.Conversion;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Unity.Entities.Tests
{
    class CompanionComponentsRuntimeTests_Baking : CompanionComponentTestFixture
    {
        protected BakingSettings MakeDefaultBakingSettings() => new BakingSettings
        {
            BakingFlags = BakingUtility.BakingFlags.AssignName
        };

        [Test]
        public void CompanionComponent_TransformSync_OutOfOrder()
        {
            BakingUtility.AddAdditionalCompanionComponentType(typeof(ConversionTestCompanionComponent));

            // Convert to create companions
            var gameObjects = new GameObject[3];
            for (int i = 0; i < gameObjects.Length; i++)
            {
                var gameObject = CreateGameObject();
                gameObject.AddComponent<ConversionTestCompanionComponent>().SomeValue = 123;
                gameObjects[i] = gameObject;
            }

            using var blobAssetStore = new BlobAssetStore(128);
            var bakingSettings = MakeDefaultBakingSettings();
            bakingSettings.BlobAssetStore = blobAssetStore;
            BakingUtility.BakeGameObjects(m_World, gameObjects, bakingSettings);

            // Verify we have created the correct number of companions
            var query = m_Manager.CreateEntityQuery(typeof(CompanionLink));
            Assert.AreEqual(3, query.CalculateEntityCount());

            var entities = query.ToEntityArray(Allocator.Persistent);
#if !ENABLE_TRANSFORM_V1
            m_World.EntityManager.SetComponentData(entities[0], LocalTransform.FromPosition(0.0f, 1, 0.0f));
            m_World.EntityManager.SetComponentData(entities[1], LocalTransform.FromPosition(0.0f, 2, 0.0f));
            m_World.EntityManager.SetComponentData(entities[2], LocalTransform.FromPosition(0.0f, 3, 0.0f));
#else
            m_World.EntityManager.SetComponentData(entities[0], new Translation{Value=new float3(0.0f, 1, 0.0f)});
            m_World.EntityManager.SetComponentData(entities[1], new Translation{Value=new float3(0.0f, 2, 0.0f)});
            m_World.EntityManager.SetComponentData(entities[2], new Translation{Value=new float3(0.0f, 3, 0.0f)});
#endif

            var companionGameObjectUpdateTransformSystem = m_World.GetExistingSystemManaged<CompanionGameObjectUpdateTransformSystem>();

            // Validate positions not moved
            for (int i = 0; i < 3; i++)
            {
                var companionLink = m_World.EntityManager.GetComponentObject<CompanionLink>(entities[i]);
                Assert.AreEqual(0f, companionLink.Companion.transform.localPosition.y);
            }

            // Re-order the entities
            m_World.EntityManager.AddComponent<EcsTestData>(entities[1]);

            m_World.Update();
            companionGameObjectUpdateTransformSystem.CompleteDependencyInternal();

            // Validate things moved and to the correct place
            var companionLink0 = m_World.EntityManager.GetComponentObject<CompanionLink>(entities[0]);
            var companionLink1 = m_World.EntityManager.GetComponentObject<CompanionLink>(entities[1]);
            var companionLink2 = m_World.EntityManager.GetComponentObject<CompanionLink>(entities[2]);

            Assert.AreEqual(1, companionLink0.Companion.transform.localPosition.y);
            Assert.AreEqual(2, companionLink1.Companion.transform.localPosition.y);
            Assert.AreEqual(3, companionLink2.Companion.transform.localPosition.y);

            entities.Dispose();
        }

        [Test]
        public void CompanionComponent_OnDisabledGameObject_IsConvertedAndDisabled()
        {
            BakingUtility.AddAdditionalCompanionComponentType(typeof(ConversionTestCompanionComponent));
            var gameObject = CreateGameObject();
            gameObject.AddComponent<ConversionTestCompanionComponent>().SomeValue = 123;
            gameObject.SetActive(false);

            using var blobAssetStore = new BlobAssetStore(128);
            var bakingSettings = MakeDefaultBakingSettings();
            bakingSettings.BlobAssetStore = blobAssetStore;
            BakingUtility.BakeGameObjects(m_World, new[] {gameObject}, bakingSettings);
            var query = m_World.EntityManager.CreateEntityQuery(new EntityQueryDesc{ All = new ComponentType[]{typeof(ConversionTestCompanionComponent)}, Options = EntityQueryOptions.IncludeDisabledEntities});
            var entities = query.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(1, entities.Length);

            var companionComponent = m_World.EntityManager.GetComponentObject<ConversionTestCompanionComponent>(entities[0]);
            Assert.AreEqual(123, companionComponent.SomeValue);

            // give the hybrid component system a chance to activate this object, and check it did not in fact do it.
            m_World.Update();

            var companion = m_World.EntityManager.GetComponentObject<CompanionLink>(entities[0]);
            Assert.IsFalse(companion.Companion.activeSelf);

            entities.Dispose();
        }
    }
}
#endif
