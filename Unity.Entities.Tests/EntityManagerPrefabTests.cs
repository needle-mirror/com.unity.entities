using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities.Tests
{
    class LinkedGroupInstantiateTests : ECSTestsFixture
    {
        public Entity PrepareLinkedGroup(Entity external, int count = 4)
        {
            // Scramble allocation order intentionally
            var e2 = m_Manager.CreateEntity(typeof(EcsTestDataEntity), typeof(Prefab));
            var e0 = m_Manager.CreateEntity(typeof(EcsTestDataEntity), typeof(Prefab), typeof(LinkedEntityGroup));
            var e1 = m_Manager.CreateEntity(typeof(EcsTestDataEntity), typeof(Prefab));

            var linkedBuf = m_Manager.GetBuffer<LinkedEntityGroup>(e0);
            linkedBuf.Add(e0);
            linkedBuf.Add(e1);
            linkedBuf.Add(e2);
            m_Manager.SetComponentData(e0, new EcsTestDataEntity { value0 = 0, value1 = external});
            m_Manager.SetComponentData(e1, new EcsTestDataEntity { value0 = 1, value1 = Entity.Null});
            m_Manager.SetComponentData(e2, new EcsTestDataEntity { value0 = 2, value1 = e1});

            for (int i = 3; i < count; i++)
            {
                var e = m_Manager.CreateEntity(typeof(EcsTestDataEntity), typeof(Prefab));
                linkedBuf = m_Manager.GetBuffer<LinkedEntityGroup>(e0);

                m_Manager.SetComponentData(e, new EcsTestDataEntity { value0 = i, value1 = linkedBuf[i - 1].Value});

                linkedBuf.Add(e);
            }

            return e0;
        }

        public void CheckLinkedGroup(Entity clone, NativeArray<Entity> srcLinked, Entity external, int count = 4)
        {
            var output = m_Manager.GetBuffer<LinkedEntityGroup>(clone).Reinterpret<Entity>().AsNativeArray();

            FastAssert.AreEqual(clone, output[0]);
            FastAssert.AreEqual(count, output.Length);

            FastAssert.AreNotEqual(srcLinked[0], output[0]);
            FastAssert.AreEqual(0, m_Manager.GetComponentData<EcsTestDataEntity>(output[0]).value0);
            FastAssert.AreEqual(external, m_Manager.GetComponentData<EcsTestDataEntity>(output[0]).value1);

            FastAssert.AreNotEqual(srcLinked[1], output[1]);
            FastAssert.AreEqual(1, m_Manager.GetComponentData<EcsTestDataEntity>(output[1]).value0);
            FastAssert.AreEqual(Entity.Null, m_Manager.GetComponentData<EcsTestDataEntity>(output[1]).value1);

            for (int i = 2; i < count; i++)
            {
                FastAssert.AreNotEqual(srcLinked[i], output[i]);
                var component = m_Manager.GetComponentData<EcsTestDataEntity>(output[i]);
                FastAssert.AreEqual(i, component.value0);
                FastAssert.AreEqual(output[i - 1], component.value1);
            }
        }

        [Test]
        public void InstantiateLinkedGroup()
        {
            var external = m_Manager.CreateEntity();

            var srcRoot = PrepareLinkedGroup(external);
            var srcLinked = new NativeArray<Entity>(m_Manager.GetBuffer<LinkedEntityGroup>(srcRoot).Reinterpret<Entity>().AsNativeArray(), Allocator.Persistent);

            var clone = m_Manager.Instantiate(srcRoot);

            CheckLinkedGroup(clone, srcLinked, external);

            // Make sure that instantiated objects are found by component group (They are no longer prefabs)
            Assert.AreEqual(4, m_Manager.CreateEntityQuery(typeof(EcsTestDataEntity)).CalculateEntityCount());

            srcLinked.Dispose();
        }

        [Test]
        public void InstantiateLinkedGroupStressTest([Values(1, 1023)] int count)
        {
            var external = m_Manager.CreateEntity();
            var srcRoot = PrepareLinkedGroup(external);
            var srcLinked = new NativeArray<Entity>(m_Manager.GetBuffer<LinkedEntityGroup>(srcRoot).Reinterpret<Entity>().AsNativeArray(), Allocator.Persistent);

            var clones = new NativeArray<Entity>(count, Allocator.Persistent);
            for (int iter = 0; iter != 3; iter++)
            {
                m_Manager.Instantiate(srcRoot, clones);
                for (int i = 0; i != clones.Length; i++)
                    CheckLinkedGroup(clones[i], srcLinked, external);
            }

            // Make sure that instantiated objects are found by component group (They are no longer prefabs)
            Assert.AreEqual(3 * 4 * count, m_Manager.CreateEntityQuery(typeof(EcsTestDataEntity)).CalculateEntityCount());

            clones.Dispose();
            srcLinked.Dispose();
        }

        [Test]
        public void DestroyLinkedGroupStressTest()
        {
            var external = m_Manager.CreateEntity();

            var roots = new NativeArray<Entity>(20, Allocator.Temp);
            for (int i = 0; i < 10; i++)
            {
                roots[i * 2] = PrepareLinkedGroup(external, 5 + i * 3);
                roots[i * 2 + 1] = PrepareLinkedGroup(external, 5 + i * 3);
            }

            m_Manager.DestroyEntity(roots);

            Assert.AreEqual(1, m_Manager.Debug.EntityCount);
            Assert.IsTrue(m_Manager.Exists(external));
        }

        [Test]
        public void DestroyEmptyLinkedEntityGroup()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddBuffer<LinkedEntityGroup>(entity);
            m_Manager.DestroyEntity(entity);
            Assert.IsFalse(m_Manager.Exists(entity));
        }

        [Test]
        public void CopyExplicitEntitySet()
        {
            var external = m_Manager.CreateEntity();
            var a = m_Manager.CreateEntity();
            var b = m_Manager.CreateEntity(typeof(Prefab));
            m_Manager.AddComponentData(a, new EcsTestDataEntity { value0 = 0, value1 = b});
            m_Manager.AddComponentData(b, new EcsTestDataEntity { value0 = 1, value1 = external});

            using (var inputs = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(new[] { a, b }, ref World.UpdateAllocator))
            using (var outputs = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(2, ref World.UpdateAllocator))
            {
                m_Manager.CopyEntitiesInternal(inputs, outputs);
                Assert.IsTrue(m_Manager.HasComponent<Prefab>(outputs[1]));

                Assert.AreEqual(outputs[1], m_Manager.GetComponentData<EcsTestDataEntity>(outputs[0]).value1);
                Assert.AreEqual(external, m_Manager.GetComponentData<EcsTestDataEntity>(outputs[1]).value1);
                Assert.AreEqual(1, m_Manager.GetComponentData<EcsTestDataEntity>(outputs[1]).value0);
                Assert.AreNotEqual(a, outputs[0]);
                Assert.AreNotEqual(a, outputs[1]);
            }
            Assert.AreEqual(4, m_Manager.CreateEntityQuery(
                new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadWrite<EcsTestDataEntity>() },
                    Options = EntityQueryOptions.IncludePrefab
                }).CalculateEntityCount());
        }

        [Test]
        public void OmitLinkedEntityGroupTest()
        {
            using var prefabChildQuery = m_Manager.CreateEntityQuery(typeof(EcsTestTag));
            var prefab = m_Manager.CreateEntity(typeof(Prefab), typeof(LinkedEntityGroup));
            var prefabChild = m_Manager.CreateEntity(typeof(Prefab), typeof(EcsTestTag)); // Use EcsTestTag to compute the number of prefab child entities.
            m_Manager.GetBuffer<LinkedEntityGroup>(prefab).Add(prefab);
            m_Manager.GetBuffer<LinkedEntityGroup>(prefab).Add(prefabChild);
            Assert.AreEqual(0, prefabChildQuery.CalculateEntityCount()); // Prefab is not included by entity queries by default

            // Instantiate should retain LEG and remove Prefab tag.
            var prefabInstance = m_Manager.Instantiate(prefab);
            Assert.IsFalse(m_Manager.HasComponent<Prefab>(prefabInstance));
            Assert.IsTrue(m_Manager.HasBuffer<LinkedEntityGroup>(prefabInstance));
            Assert.AreEqual(prefabInstance, m_Manager.GetBuffer<LinkedEntityGroup>(prefabInstance)[0].Value);
            Assert.AreNotEqual(prefabChild, m_Manager.GetBuffer<LinkedEntityGroup>(prefabInstance)[1].Value);
            Assert.IsFalse(m_Manager.HasComponent<Prefab>(m_Manager.GetBuffer<LinkedEntityGroup>(prefabInstance)[1].Value));
            Assert.AreEqual(1, prefabChildQuery.CalculateEntityCount());

            // CopyEntities however retains LEG and keeps Prefab
            var srcEntities = new NativeArray<Entity>(1, Allocator.Temp);
            var dstEntities = new NativeArray<Entity>(1, Allocator.Temp);
            srcEntities[0] = prefab;
            m_Manager.CopyEntitiesInternal(srcEntities, dstEntities);
            var copyInstance = dstEntities[0];
            srcEntities.Dispose();
            dstEntities.Dispose();
            Assert.IsTrue(m_Manager.HasComponent<Prefab>(copyInstance));
            Assert.IsTrue(m_Manager.HasBuffer<LinkedEntityGroup>(copyInstance));
            // CopyEntities remaps the LEG[0] because it points to itself, but keeps LEG[1] not remapped because technically
            // it ignores the LEG.
            Assert.AreEqual(copyInstance, m_Manager.GetBuffer<LinkedEntityGroup>(copyInstance)[0].Value);
            Assert.AreEqual(prefabChild, m_Manager.GetBuffer<LinkedEntityGroup>(copyInstance)[1].Value);
            Assert.IsTrue(m_Manager.HasComponent<Prefab>(m_Manager.GetBuffer<LinkedEntityGroup>(copyInstance)[1].Value));
            Assert.AreEqual(1, prefabChildQuery.CalculateEntityCount());

            m_Manager.AddComponent<OmitLinkedEntityGroupFromPrefabInstance>(prefab);
            var prefabInstance2 = m_Manager.Instantiate(prefab);
            Assert.IsFalse(m_Manager.HasComponent<Prefab>(prefabInstance2));
            Assert.IsFalse(m_Manager.HasBuffer<LinkedEntityGroup>(prefabInstance2));
            Assert.IsFalse(m_Manager.HasComponent<OmitLinkedEntityGroupFromPrefabInstance>(prefabInstance2));
            Assert.AreEqual(2, prefabChildQuery.CalculateEntityCount());

            m_Manager.RemoveComponent<LinkedEntityGroup>(prefab);
            var prefabInstance3 = m_Manager.Instantiate(prefab);
            Assert.IsFalse(m_Manager.HasComponent<Prefab>(prefabInstance3));
            Assert.IsFalse(m_Manager.HasBuffer<LinkedEntityGroup>(prefabInstance3));
            // OmitLinkedEntityGroupFromPrefabInstance is ignored and kept if LinkedEntityGroup is not present.
            Assert.IsTrue(m_Manager.HasComponent<OmitLinkedEntityGroupFromPrefabInstance>(prefabInstance3));
            Assert.AreEqual(2, prefabChildQuery.CalculateEntityCount());
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        public Entity PrepareLinkedGroup_ManagedComponents(Entity external, int count = 4)
        {
            // Scramble allocation order intentionally
            var e2 = m_Manager.CreateEntity(typeof(EcsTestManagedDataEntity), typeof(Prefab));
            var e0 = m_Manager.CreateEntity(typeof(EcsTestManagedDataEntity), typeof(Prefab), typeof(LinkedEntityGroup));
            var e1 = m_Manager.CreateEntity(typeof(EcsTestManagedDataEntity), typeof(Prefab));

            var linkedBuf = m_Manager.GetBuffer<LinkedEntityGroup>(e0);
            linkedBuf.Add(e0);
            linkedBuf.Add(e1);
            linkedBuf.Add(e2);
            m_Manager.SetComponentData(e0, new EcsTestManagedDataEntity { value0 = 0.ToString(), value1 = external });
            m_Manager.SetComponentData(e1, new EcsTestManagedDataEntity { value0 = 1.ToString(), value1 = Entity.Null });
            m_Manager.SetComponentData(e2, new EcsTestManagedDataEntity { value0 = 2.ToString(), value1 = e1 });

            for (int i = 3; i < count; i++)
            {
                var e = m_Manager.CreateEntity(typeof(EcsTestManagedDataEntity), typeof(Prefab));
                linkedBuf = m_Manager.GetBuffer<LinkedEntityGroup>(e0);

                m_Manager.SetComponentData(e, new EcsTestManagedDataEntity { value0 = i.ToString(), value1 = linkedBuf[i - 1].Value });

                linkedBuf.Add(e);
            }

            return e0;
        }

        public void CheckLinkedGroup_ManagedComponents(Entity clone, NativeArray<Entity> srcLinked, Entity external, int count = 4)
        {
            var output = m_Manager.GetBuffer<LinkedEntityGroup>(clone).Reinterpret<Entity>().AsNativeArray();

            FastAssert.AreEqual(clone, output[0]);
            FastAssert.AreEqual(count, output.Length);

            FastAssert.AreNotEqual(srcLinked[0], output[0]);
            FastAssert.AreEqual("0", m_Manager.GetComponentData<EcsTestManagedDataEntity>(output[0]).value0);
            FastAssert.AreEqual(external, m_Manager.GetComponentData<EcsTestManagedDataEntity>(output[0]).value1);

            FastAssert.AreNotEqual(srcLinked[1], output[1]);
            FastAssert.AreEqual("1", m_Manager.GetComponentData<EcsTestManagedDataEntity>(output[1]).value0);
            FastAssert.AreEqual(Entity.Null, m_Manager.GetComponentData<EcsTestManagedDataEntity>(output[1]).value1);

            for (int i = 2; i < count; i++)
            {
                FastAssert.AreNotEqual(srcLinked[i], output[i]);
                var component = m_Manager.GetComponentData<EcsTestManagedDataEntity>(output[i]);
                FastAssert.AreEqual(i.ToString(), component.value0);
                FastAssert.AreEqual(output[i - 1], component.value1);
            }
        }

        [Test]
        public void InstantiateLinkedGroup_ManagedComponents()
        {
            var external = m_Manager.CreateEntity();

            var srcRoot = PrepareLinkedGroup_ManagedComponents(external);
            var srcLinked = new NativeArray<Entity>(m_Manager.GetBuffer<LinkedEntityGroup>(srcRoot).Reinterpret<Entity>().AsNativeArray(), Allocator.Persistent);

            var clone = m_Manager.Instantiate(srcRoot);

            CheckLinkedGroup_ManagedComponents(clone, srcLinked, external);

            // Make sure that instantiated objects are found by component group (They are no longer prefabs)
            Assert.AreEqual(4, m_Manager.CreateEntityQuery(typeof(EcsTestManagedDataEntity)).CalculateEntityCount());

            srcLinked.Dispose();
        }

        [Test]
        public void InstantiateLinkedGroupStressTest_ManagedComponents([Values(1, 1023)] int count)
        {
            var external = m_Manager.CreateEntity();
            var srcRoot = PrepareLinkedGroup_ManagedComponents(external);
            var srcLinked = new NativeArray<Entity>(m_Manager.GetBuffer<LinkedEntityGroup>(srcRoot).Reinterpret<Entity>().AsNativeArray(), Allocator.Persistent);

            var clones = new NativeArray<Entity>(count, Allocator.Persistent);
            for (int iter = 0; iter != 3; iter++)
            {
                m_Manager.Instantiate(srcRoot, clones);
                for (int i = 0; i != clones.Length; i++)
                    CheckLinkedGroup_ManagedComponents(clones[i], srcLinked, external);
            }

            // Make sure that instantiated objects are found by component group (They are no longer prefabs)
            Assert.AreEqual(3 * 4 * count, m_Manager.CreateEntityQuery(typeof(EcsTestManagedDataEntity)).CalculateEntityCount());

            clones.Dispose();
            srcLinked.Dispose();
        }

        [Test]
        public void DestroyLinkedGroupStressTest_ManagedComponents()
        {
            var external = m_Manager.CreateEntity();

            var roots = new NativeArray<Entity>(20, Allocator.Temp);
            for (int i = 0; i < 10; i++)
            {
                roots[i * 2] = PrepareLinkedGroup_ManagedComponents(external, 5 + i * 3);
                roots[i * 2 + 1] = PrepareLinkedGroup_ManagedComponents(external, 5 + i * 3);
            }

            m_Manager.DestroyEntity(roots);

            Assert.AreEqual(1, m_Manager.Debug.EntityCount);
            Assert.IsTrue(m_Manager.Exists(external));
        }

#endif
    }
}
