#if !UNITY_DISABLE_MANAGED_COMPONENTS
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Hybrid.Tests;
using Unity.Mathematics;
using Unity.Transforms;

namespace Unity.Entities.Tests
{
    public class MonoBehaviourComponentConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            Entities.ForEach((ConversionTestHybridComponent component) =>
            {
                AddHybridComponent(component);
            });
        }
    }

    class HybridComponentsTests : HybridComponentTestFixture
    {
        [Test]
        public void HybridComponent_TransformSync_OutOfOrder()
        {
            // Convert to create companions
            for (int i = 0; i < 3; i++)
            {
                var gameObject = CreateGameObject();
                gameObject.AddComponent<ConversionTestHybridComponent>().SomeValue = 123;
                GameObjectConversionUtility.ConvertGameObjectHierarchy(gameObject, MakeDefaultSettings().WithExtraSystem<MonoBehaviourComponentConversionSystem>());
            }

            // Verify we have created the correct number of companions
            var query = m_Manager.CreateEntityQuery(typeof(CompanionLink));
            Assert.AreEqual(3, query.CalculateEntityCount());

            var entities = query.ToEntityArray(Allocator.Persistent);
            m_World.EntityManager.SetComponentData(entities[0], new Translation{Value=new float3(0.0f, 1, 0.0f)});
            m_World.EntityManager.SetComponentData(entities[1], new Translation{Value=new float3(0.0f, 2, 0.0f)});
            m_World.EntityManager.SetComponentData(entities[2], new Translation{Value=new float3(0.0f, 3, 0.0f)});

            var companionGameObjectUpdateTransformSystem = m_World.GetExistingSystem<CompanionGameObjectUpdateTransformSystem>();

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
        public void HybridComponent_OnDisabledGameObject_IsConvertedAndDisabled()
        {
            var gameObject = CreateGameObject();
            gameObject.AddComponent<ConversionTestHybridComponent>().SomeValue = 123;
            gameObject.SetActive(false);
            var entity = GameObjectConversionUtility.ConvertGameObjectHierarchy(gameObject, MakeDefaultSettings().WithExtraSystem<MonoBehaviourComponentConversionSystem>());
            var hybridComponent = m_World.EntityManager.GetComponentObject<ConversionTestHybridComponent>(entity);
            Assert.AreEqual(123, hybridComponent.SomeValue);

            // give the hybrid component system a chance to activate this object, and check it did not in fact do it.
            m_World.Update();

            var companion = m_World.EntityManager.GetComponentObject<CompanionLink>(entity);
            Assert.IsFalse(companion.Companion.activeSelf);
        }
    }
}
#endif
