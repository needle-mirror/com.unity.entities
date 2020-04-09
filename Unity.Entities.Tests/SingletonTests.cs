using System;
using NUnit.Framework;

namespace Unity.Entities.Tests
{
    class SingletonTests : ECSTestsFixture
    {
        [Test]
        public void GetSetSingleton()
        {
            m_Manager.CreateEntity(typeof(EcsTestData));

            EmptySystem.SetSingleton(new EcsTestData(10));
            Assert.AreEqual(10, EmptySystem.GetSingleton<EcsTestData>().value);
        }
        
        [Test]
        public void GetSetSingletonMultipleComponents()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData3), typeof(EcsTestData), typeof(EcsTestData2));

            m_Manager.SetComponentData(entity, new EcsTestData(10));
            Assert.AreEqual(10, EmptySystem.GetSingleton<EcsTestData>().value);
                
            EmptySystem.SetSingleton(new EcsTestData2(100));
            Assert.AreEqual(100, m_Manager.GetComponentData<EcsTestData2>(entity).value0);
        }

        [Test]
        public void GetSetSingletonZeroThrows()
        {
            Assert.Throws<InvalidOperationException>(() => EmptySystem.SetSingleton(new EcsTestData()));
            Assert.Throws<InvalidOperationException>(() => EmptySystem.GetSingleton<EcsTestData>());
        }
        
        [Test]
        public void GetSetSingletonMultipleThrows()
        {
            m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.CreateEntity(typeof(EcsTestData));

            Assert.Throws<InvalidOperationException>(() => EmptySystem.SetSingleton(new EcsTestData()));
            Assert.Throws<InvalidOperationException>(() => EmptySystem.GetSingleton<EcsTestData>());
        }
        
        [Test]
        public void RequireSingletonWorks()
        {
            EmptySystem.RequireSingletonForUpdate<EcsTestData>();
            EmptySystem.GetEntityQuery(typeof(EcsTestData2));
            
            m_Manager.CreateEntity(typeof(EcsTestData2));
            Assert.IsFalse(EmptySystem.ShouldRunSystem());
            m_Manager.CreateEntity(typeof(EcsTestData));
            Assert.IsTrue(EmptySystem.ShouldRunSystem());
        }

        [AlwaysUpdateSystem]
        class TestAlwaysUpdateSystem : ComponentSystem
        {
            protected override void OnUpdate()
            {
            }
        }

        [Test]
        public void RequireSingletonWithAlwaysUpdateThrows()
        {
            var system = World.CreateSystem<TestAlwaysUpdateSystem>();
            Assert.Throws<InvalidOperationException>(() => system.RequireSingletonForUpdate<EcsTestData>());
        }

        [Test]
        public void HasSingletonWorks()
        {
            Assert.IsFalse(EmptySystem.HasSingleton<EcsTestData>());
            m_Manager.CreateEntity(typeof(EcsTestData));
            Assert.IsTrue(EmptySystem.HasSingleton<EcsTestData>());
        }

        [Test]
        public void HasSingleton_ReturnsTrueWithEntityWithOnlyComponent()
        {
            Assert.IsFalse(EmptySystem.HasSingleton<EcsTestData>());
                
            m_Manager.CreateEntity(typeof(EcsTestData));
            Assert.IsTrue(EmptySystem.HasSingleton<EcsTestData>());
                
            m_Manager.CreateEntity(typeof(EcsTestData));
            Assert.IsFalse(EmptySystem.HasSingleton<EcsTestData>());
        }
        
        [Test]
        public void GetSingletonEntityWorks()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));

            var singletonEntity = EmptySystem.GetSingletonEntity<EcsTestData>();
            Assert.AreEqual(entity, singletonEntity);
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void GetSetSingleton_ManagedComponents()
        {
            m_Manager.CreateEntity(typeof(EcsTestManagedComponent));

            const string kTestVal = "SomeString";
            EmptySystem.SetSingleton(new EcsTestManagedComponent() { value = kTestVal });
            Assert.AreEqual(kTestVal, EmptySystem.GetSingleton<EcsTestManagedComponent>().value);
        }

        [Test]
        public void HasSingletonWorks_ManagedComponents()
        {
            Assert.IsFalse(EmptySystem.HasSingleton<EcsTestManagedComponent>());
            m_Manager.CreateEntity(typeof(EcsTestManagedComponent));
            Assert.IsTrue(EmptySystem.HasSingleton<EcsTestManagedComponent>());
        }
#endif
    }
}
