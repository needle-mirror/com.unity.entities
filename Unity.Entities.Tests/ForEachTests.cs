using System;
using NUnit.Framework;

namespace Unity.Entities.Tests
{
    class ForEachBasicTests : EntityQueryBuilderTestFixture
    {
        [SetUp]
        public void CreateTestEntities()
        {
            m_Manager.AddComponentData(m_Manager.CreateEntity(), new EcsTestData(5));
            m_Manager.AddSharedComponentData(m_Manager.CreateEntity(), new SharedData1(7));
            m_Manager.CreateEntity(typeof(EcsIntElement));
        }

        [Test]
        public void All()
        {
            var counter = 0;
            TestSystem.Entities.ForEach(entity =>
            {
                Assert.IsTrue(m_Manager.Exists(entity));
                counter++;
            });
            Assert.AreEqual(3, counter);
        }

        [Test]
        public void ComponentData()
        {
            {
                var counter = 0;
                TestSystem.Entities.ForEach((ref EcsTestData testData) =>
                {
                    Assert.AreEqual(5, testData.value);
                    testData.value++;
                    counter++;
                });
                Assert.AreEqual(1, counter);
            }

            {
                var counter = 0;
                TestSystem.Entities.ForEach((Entity entity, ref EcsTestData testData) =>
                {
                    Assert.AreEqual(6, testData.value);
                    testData.value++;

                    Assert.AreEqual(7, m_Manager.GetComponentData<EcsTestData>(entity).value);

                    counter++;
                });
                Assert.AreEqual(1, counter);
            }
        }

        [Test]
        public void SharedComponentData()
        {
            var counter = 0;
            TestSystem.Entities.ForEach((SharedData1 testData) =>
            {
                Assert.AreEqual(7, testData.value);
                counter++;
            });
            Assert.AreEqual(1, counter);
        }

        [Test]
        public void DynamicBuffer()
        {
            var counter = 0;
            TestSystem.Entities.ForEach((DynamicBuffer<EcsIntElement> testData) =>
            {
                testData.Add(0);
                testData.Add(1);
                counter++;
            });
            Assert.AreEqual(1, counter);
        }
    }

    class ForEachTests : EntityQueryBuilderTestFixture
    {
        [Test]
        public void Many()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponentData(entity, new EcsTestData(0));
            m_Manager.AddComponentData(entity, new EcsTestData2(1));
            m_Manager.AddComponentData(entity, new EcsTestData3(2));
            m_Manager.AddComponentData(entity, new EcsTestData4(3));
            m_Manager.AddComponentData(entity, new EcsTestData5(4));

            var counter = 0;
            TestSystem.Entities.ForEach((Entity e, ref EcsTestData t0, ref EcsTestData2 t1, ref EcsTestData3 t2, ref EcsTestData4 t3, ref EcsTestData5 t4) =>
            {
                Assert.AreEqual(entity, e);
                Assert.AreEqual(0, t0.value);
                Assert.AreEqual(1, t1.value0);
                Assert.AreEqual(2, t2.value0);
                Assert.AreEqual(3, t3.value0);
                Assert.AreEqual(4, t4.value0);
                counter++;
            });
            Assert.AreEqual(1, counter);
        }

        [Test]
        public void Safety()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponentData(entity, new EcsTestData(0));

            var counter = 0;
            TestSystem.Entities.ForEach((Entity e, ref EcsTestData t0) =>
            {
                Assert.Throws<InvalidOperationException>(() => m_Manager.CreateEntity());
                Assert.Throws<InvalidOperationException>(() => m_Manager.DestroyEntity(e));
                Assert.Throws<InvalidOperationException>(() => m_Manager.AddComponent(e, typeof(EcsTestData2)));
                Assert.Throws<InvalidOperationException>(() => m_Manager.RemoveComponent<EcsTestData>(e));
                counter++;
            });
            Assert.AreEqual(1, counter);

            Assert.Throws<ArgumentException>(() =>
            {
                TestSystem.Entities.ForEach((Entity e, ref EcsTestData t0) => throw new ArgumentException());
            });

            Assert.IsFalse(m_Manager.IsInsideForEach);
        }

        //@TODO: Class iterator test coverage...
    }
}
