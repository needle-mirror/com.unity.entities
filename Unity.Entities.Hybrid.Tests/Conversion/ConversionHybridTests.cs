using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Conversion;
using Unity.Entities.Hybrid.EndToEnd.Tests;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;

namespace Unity.Entities.Tests
{
    class ConversionTestHybridComponentPrefabReference : UnityEngine.MonoBehaviour, IDeclareReferencedPrefabs
    {
        public UnityEngine.GameObject Prefab;

        public void DeclareReferencedPrefabs(List<UnityEngine.GameObject> referencedPrefabs)
        {
            referencedPrefabs.Add(Prefab);
        }
    }
}

#if !UNITY_DISABLE_MANAGED_COMPONENTS
namespace Unity.Entities.Tests.Conversion
{
    class ConversionHybridTests : ConversionTestFixtureBase
    {
        string m_TempAssetDir;

        [OneTimeSetUp]
        public void SetUp()
        {
            var guid = AssetDatabase.CreateFolder("Assets", Path.GetRandomFileName());
            m_TempAssetDir = AssetDatabase.GUIDToAssetPath(guid);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            AssetDatabase.DeleteAsset(m_TempAssetDir);
        }

        public class MonoBehaviourComponentConversionSystem : GameObjectConversionSystem
        {
            protected override void OnUpdate()
            {
                Entities.ForEach((ConversionTestHybridComponent component) =>
                {
                    AddHybridComponent(component);
                });

                Entities.ForEach((ConversionTestHybridComponentWithEntity component) =>
                {
                    AddHybridComponent(component);
                });

                Entities.ForEach((ConversionTestHybridComponentA component) =>
                {
                    AddHybridComponent(component);
                });

                Entities.ForEach((ConversionTestHybridComponentB component) =>
                {
                    AddHybridComponent(component);
                });

                Entities.ForEach((ConversionTestHybridComponentC component) =>
                {
                    AddHybridComponent(component);
                });
            }
        }

        [Test]
        public void ManagedComponentSimple()
        {
            var gameObject = CreateGameObject();
            gameObject.AddComponent<ConversionTestHybridComponent>().SomeValue = 123;

            var entity = GameObjectConversionUtility.ConvertGameObjectHierarchy(gameObject, MakeDefaultSettings().WithExtraSystem<MonoBehaviourComponentConversionSystem>());

            gameObject.GetComponent<ConversionTestHybridComponent>().SomeValue = 234;
            Assert.AreEqual(123, m_Manager.GetComponentObject<ConversionTestHybridComponent>(entity).SomeValue);

            var instance = m_Manager.Instantiate(entity);

            m_Manager.GetComponentObject<ConversionTestHybridComponent>(entity).SomeValue = 345;
            Assert.AreEqual(123, m_Manager.GetComponentObject<ConversionTestHybridComponent>(instance).SomeValue);

            var instances = new NativeArray<Entity>(2, Allocator.Temp);
            m_Manager.Instantiate(entity, instances);

            Assert.AreEqual(345, m_Manager.GetComponentObject<ConversionTestHybridComponent>(instances[0]).SomeValue);
            Assert.AreEqual(345, m_Manager.GetComponentObject<ConversionTestHybridComponent>(instances[1]).SomeValue);
        }

        class MockMultipleAuthoringComponentsConversionSystem : GameObjectConversionSystem
        {
            protected override void OnUpdate()
            {
                Entities.ForEach((ConversionTestHybridComponent authoring) =>
                {
                    var buffer = DstEntityManager.AddBuffer<MockDynamicBufferData>(GetPrimaryEntity(authoring));
                    foreach (var authoringInstance in authoring.gameObject.GetComponents<ConversionTestHybridComponent>())
                        buffer.Add(new MockDynamicBufferData { Value = authoringInstance.SomeValue });
                });
            }
        }

        [Test]
        public void EntityQueryBuilder_WhenGameObjectHasMultipleAuthoringComponentsOfQueriedType_ReturnsFirstMatch()
        {
            var gameObject = CreateGameObject($"GameObject With 2 {nameof(ConversionTestHybridComponent)}", typeof(ConversionTestHybridComponent), typeof(ConversionTestHybridComponent));
            var authoringComponents = gameObject.GetComponents<ConversionTestHybridComponent>();
            Assume.That(authoringComponents.Length, Is.EqualTo(2));
            var expectedValues = new[] { new MockDynamicBufferData { Value = 123 }, new MockDynamicBufferData { Value = 456 } };
            authoringComponents[0].SomeValue = expectedValues[0].Value;
            authoringComponents[1].SomeValue = expectedValues[1].Value;

            var entity = GameObjectConversionUtility.ConvertGameObjectHierarchy(gameObject, MakeDefaultSettings().WithExtraSystem<MockMultipleAuthoringComponentsConversionSystem>());

            var buffer = m_Manager.GetBuffer<MockDynamicBufferData>(entity);
            Assert.That(buffer.AsNativeArray(), Is.EqualTo(expectedValues));
        }

        [Test]
        public void CompanionGameObjectTranform_WithScale_IsSetFromLocalToWorld()
        {
            var gameObject = CreateGameObject("source", typeof(ConversionTestHybridComponent));
            gameObject.transform.localPosition = new UnityEngine.Vector3(1, 2, 3);
            gameObject.transform.localRotation = UnityEngine.Quaternion.Euler(10, 20, 30);
            gameObject.transform.localScale = new UnityEngine.Vector3(4, 5, 6);
            var reference = gameObject.transform.localToWorldMatrix;

            var conversionSettings = MakeDefaultSettings().WithExtraSystem<MonoBehaviourComponentConversionSystem>();
            var entity = GameObjectConversionUtility.ConvertGameObjectHierarchy(gameObject, conversionSettings);

            TestUtilities.RegisterSystems(World, TestUtilities.SystemCategories.HybridComponents);

            var companion = m_Manager.GetComponentData<CompanionLink>(entity).Companion;

            Assert.AreNotEqual(gameObject, companion);
            AssertEqual(reference, companion.transform.localToWorldMatrix);

            World.Update();

            AssertEqual(reference, companion.transform.localToWorldMatrix);

            var matrix = float4x4.TRS(new float3(2, 3, 4), quaternion.Euler(3, 4, 5), new float3(4, 5, 6));
            m_Manager.SetComponentData(entity, new LocalToWorld {Value = matrix});

            World.Update();

            AssertEqual(matrix, companion.transform.localToWorldMatrix);

            void AssertEqual(UnityEngine.Matrix4x4 a, UnityEngine.Matrix4x4 b)
            {
                for (int i = 0; i < 16; ++i)
                {
                    Assert.AreEqual(a[i], b[i], 0.001f);
                }
            }
        }

        [Test]
        public void CompanionGameObject_ActivatesIfNotPrefabOrDisabled()
        {
            // Create a prefab asset with an Hybrid Component
            var prefab = CreateGameObject("prefab", typeof(ConversionTestHybridComponent));
            var prefabPath = m_TempAssetDir + "/TestPrefab.prefab";
            Assert.IsFalse(prefab.IsPrefab());
            prefab = PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath, out var success);
            Assert.IsTrue(success && prefab.IsPrefab());

            // Create a GameObject that references the prefab, in order to trigger the conversion of the prefab
            var gameObject = CreateGameObject("prefab_ref", typeof(ConversionTestHybridComponentPrefabReference));
            gameObject.GetComponent<ConversionTestHybridComponentPrefabReference>().Prefab = prefab;

            // Run the actual conversion, we only care about the prefab so we destroy the other entity
            var settings = MakeDefaultSettings().WithExtraSystem<MonoBehaviourComponentConversionSystem>();
            var dummy = GameObjectConversionUtility.ConvertGameObjectHierarchy(gameObject, settings);
            m_Manager.DestroyEntity(dummy);
            EntitiesAssert.ContainsOnly(m_Manager, EntityMatch.Exact<CompanionLink, ConversionTestHybridComponent, Prefab, LinkedEntityGroup>(k_CommonComponents));

            // Accessing the prefab entity and its companion GameObject can't be directly done with GetSingleton because it requires EntityQueryOptions.IncludePrefab
            var companionQuery = EmptySystem.GetEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<CompanionLink>()},
                Options = EntityQueryOptions.IncludePrefab
            });
            var prefabEntity = companionQuery.GetSingletonEntity();
            var prefabCompanion = m_Manager.GetComponentData<CompanionLink>(prefabEntity).Companion;

            // Create an instance, the expectation is that the prefab remains inactive, but the instance activates
            var instanceEntity = m_Manager.Instantiate(prefabEntity);
            var instanceCompanion = m_Manager.GetComponentData<CompanionLink>(instanceEntity).Companion;

            // Activation happens through a system, so before the first update everything is inactive
            Assert.IsFalse(prefabCompanion.activeSelf);
            Assert.IsFalse(instanceCompanion.activeSelf);

            // Register all the Hybrid Component related systems, including the one that deals with activation
            TestUtilities.RegisterSystems(World, TestUtilities.SystemCategories.HybridComponents);

            // After an update, the prefab should remain inactive, but the instance should be active
            World.Update();
            Assert.IsFalse(prefabCompanion.activeSelf);
            Assert.IsTrue(instanceCompanion.activeSelf);

            // Let's reverse the test, demote the prefab to a regular entity, and disable the instance
            m_Manager.RemoveComponent<Prefab>(prefabEntity);
            m_Manager.AddComponent<Disabled>(instanceEntity);

            // After an update, the prefab which isn't one anymore should be active, and the disabled entity should be inactive
            World.Update();
            Assert.IsTrue(prefabCompanion.activeSelf);
            Assert.IsFalse(instanceCompanion.activeSelf);

            // Let's reverse once more and get back to the initial state
            m_Manager.AddComponent<Prefab>(prefabEntity);
            m_Manager.RemoveComponent<Disabled>(instanceEntity);

            // After an update, the prefab should be inactive again, and the instance should be active again
            World.Update();
            Assert.IsFalse(prefabCompanion.activeSelf);
            Assert.IsTrue(instanceCompanion.activeSelf);
        }

        public class ConversionTestHybridComponentWithEntity : UnityEngine.MonoBehaviour, UnityEngine.ISerializationCallbackReceiver
        {
            public static Entity DefaultEntity;
            public Entity SomeEntity;

            public void OnBeforeSerialize()
            {
            }

            public void OnAfterDeserialize()
            {
                SomeEntity = DefaultEntity;
            }
        }

        [Test]
        public void HybridComponents_AreNotBeRemapped_WhenInstantiated()
        {
            // Setup a simple entity with a hybrid component that contains an Entity field
            var gameObjectPrefab = CreateGameObject("prefab", typeof(ConversionTestHybridComponentWithEntity));
            var settings = MakeDefaultSettings().WithExtraSystem<MonoBehaviourComponentConversionSystem>();
            var entityPrefab = GameObjectConversionUtility.ConvertGameObjectHierarchy(gameObjectPrefab, settings);

            // Add a managed and an unmanaged component, each with an entity field pointing to their own entity
            m_Manager.AddComponentData(entityPrefab, new EcsTestDataEntity {value1 = entityPrefab});
            m_Manager.AddComponentData(entityPrefab, new EcsTestManagedDataEntity {value1 = entityPrefab});

            // Since Entity contents aren't serialized, GameObject.Instantiate won't copy the Entity field
            // But we need that field to be set to the prefab entity in order to check if the remapping happens
            // So we're abusing ISerializationCallbackReceiver with a static field to pretend it got cloned
            ConversionTestHybridComponentWithEntity.DefaultEntity = entityPrefab;

            // Create a bunch of instances
            var instances = m_Manager.Instantiate(entityPrefab, 10, Allocator.Temp);

            foreach (var instance in instances)
            {
                var hybrid = m_Manager.GetComponentObject<ConversionTestHybridComponentWithEntity>(instance);
                var unmanaged = m_Manager.GetComponentData<EcsTestDataEntity>(instance);
                var managed = m_Manager.GetComponentData<EcsTestManagedDataEntity>(instance);

                // The hybrid component SHOULD NOT be remapped, so it still contains the prefab entity
                Assert.AreEqual(hybrid.SomeEntity, entityPrefab);

                // The other two components SHOULD be remapped, so they now point to the instanced entity
                Assert.AreEqual(unmanaged.value1, instance);
                Assert.AreEqual(managed.value1, instance);
            }
        }

        public class ConversionTestHybridComponentA : UnityEngine.MonoBehaviour
        {
            public int SomeValue;
        }
        public class ConversionTestHybridComponentB : UnityEngine.MonoBehaviour
        {
            public int SomeValue;
        }
        public class ConversionTestHybridComponentC : UnityEngine.MonoBehaviour
        {
            public int SomeValue;
        }

        [Test]
        public void MultipleHybridComponents_DontGetMixedUp_WhenInstantiated()
        {
            // Setup a simple entity with multiple hybrid components
            var gameObjectPrefab = CreateGameObject("prefab",
                typeof(ConversionTestHybridComponentA),
                typeof(ConversionTestHybridComponentB),
                typeof(ConversionTestHybridComponentC));
            var settings = MakeDefaultSettings().WithExtraSystem<MonoBehaviourComponentConversionSystem>();
            var entityPrefab = GameObjectConversionUtility.ConvertGameObjectHierarchy(gameObjectPrefab, settings);

            // Create a bunch of instances
            var instances = m_Manager.Instantiate(entityPrefab, 10, Allocator.Temp);

            var query = EmptySystem.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ConversionTestHybridComponentA>(),
                    ComponentType.ReadOnly<ConversionTestHybridComponentB>(),
                    ComponentType.ReadOnly<ConversionTestHybridComponentC>(),
                }
            });

            var a = query.ToComponentArray<ConversionTestHybridComponentA>();
            var b = query.ToComponentArray<ConversionTestHybridComponentB>();
            var c = query.ToComponentArray<ConversionTestHybridComponentC>();

            // The source doesn't have the Prefab tag, so it also gets picked up by the query
            Assert.AreEqual(instances.Length + 1, a.Length);
            Assert.AreEqual(instances.Length + 1, b.Length);
            Assert.AreEqual(instances.Length + 1, c.Length);

            CollectionAssert.AllItemsAreUnique(a);
            CollectionAssert.AllItemsAreUnique(b);
            CollectionAssert.AllItemsAreUnique(c);
        }

        [Test]
        public void HybridComponentConversion_DoesNotThrow_WhenHybridIsDestroyed()
        {
            var gameObject = InstantiateGameObject(LoadPrefab("Prefab_MissingMB"));
            gameObject.AddComponent<ConversionTestHybridComponent>().SomeValue = 123;

            Entity entity = default;
            Assert.DoesNotThrow(() =>
            {
                entity = GameObjectConversionUtility.ConvertGameObjectHierarchy(gameObject, MakeDefaultSettings().WithExtraSystem<MonoBehaviourComponentConversionSystem>());
            });
            Assert.That(m_Manager.HasComponent<CompanionLink>(entity), Is.True);
        }
    }
}
#endif
