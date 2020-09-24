#if !UNITY_DISABLE_MANAGED_COMPONENTS
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Hybrid.Tests;
using Unity.Entities.Tests;
using Unity.Mathematics;
using Unity.PerformanceTesting;
using Unity.Transforms;

namespace Unity.Entities.Hybrid.PerformanceTests
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

    [TestFixture]
    [Category("Performance")]
    public sealed unsafe class HybridComponentPerformanceTests : HybridComponentTestFixture
    {
        [Test, Performance]
        public void HybridComponent_Companion_TransformSync([Values(1, 10, 100, 1000, 10000)] int companionCount)
        {
            // Convert to create companions
            for (int i = 0; i < companionCount; i++)
            {
                var gameObject = CreateGameObject();
                gameObject.AddComponent<ConversionTestHybridComponent>().SomeValue = 123;
                GameObjectConversionUtility.ConvertGameObjectHierarchy(gameObject, MakeDefaultSettings().WithExtraSystem<MonoBehaviourComponentConversionSystem>());
            }

            // Verify we have created the correct number of companions
            var query = m_Manager.CreateEntityQuery(typeof(CompanionLink));
            Assert.AreEqual(companionCount, query.CalculateEntityCount());

            var entities = query.ToEntityArray(Allocator.Persistent);
            for (int i = 0; i < entities.Length; i++)
            {
                m_World.EntityManager.SetComponentData(entities[i], new Translation{Value=new float3(0.0f, 42f, 0.0f)});
            }

            var companionGameObjectUpdateTransformSystem = m_World.GetExistingSystem<CompanionGameObjectUpdateTransformSystem>();
            Measure.ProfilerMarkers(companionGameObjectUpdateTransformSystem.GetProfilerMarkerName());

            // Validate positions not moved
            for (int i = 0; i < entities.Length; i++)
            {
                var companionLink = m_World.EntityManager.GetComponentObject<CompanionLink>(entities[i]);
                Assert.AreEqual(0f, companionLink.Companion.transform.localPosition.y);
            }

            m_World.Update();
            companionGameObjectUpdateTransformSystem.CompleteDependencyInternal();

            // Validate things moved
            for (int i = 0; i < entities.Length; i++)
            {
                var companionLink = m_World.EntityManager.GetComponentObject<CompanionLink>(entities[i]);
                Assert.AreEqual(42f, companionLink.Companion.transform.localPosition.y);
            }

            entities.Dispose();
        }
    }
}
#endif
