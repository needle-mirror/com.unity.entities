using System;
using NUnit.Framework;

namespace Unity.Entities.Tests
{
    class SingletonTests : ECSTestsFixture
    {
        [Test]
        [StandaloneFixme]
        public void GetSetSingleton()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));

            EmptySystem.SetSingleton(new EcsTestData(10));
            Assert.AreEqual(10, EmptySystem.GetSingleton<EcsTestData>().value);
        }
        
        [Test]
        [StandaloneFixme]
        public void GetSetSingletonZeroThrows()
        {
            Assert.Throws<InvalidOperationException>(() => EmptySystem.SetSingleton(new EcsTestData()));
            Assert.Throws<InvalidOperationException>(() => EmptySystem.GetSingleton<EcsTestData>());
        }
        
        [Test]
        [StandaloneFixme]
        public void GetSetSingletonMultipleThrows()
        {
            m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.CreateEntity(typeof(EcsTestData));

            Assert.Throws<InvalidOperationException>(() => EmptySystem.SetSingleton(new EcsTestData()));
            Assert.Throws<InvalidOperationException>(() => EmptySystem.GetSingleton<EcsTestData>());
        }
        
        [Test]
        [StandaloneFixme]
        public void RequireSingletonWorks()
        {
            EmptySystem.RequireSingletonForUpdate<EcsTestData>();
            EmptySystem.GetComponentGroup(typeof(EcsTestData2));
            
            m_Manager.CreateEntity(typeof(EcsTestData2));
            Assert.IsFalse(EmptySystem.ShouldRunSystem());
            m_Manager.CreateEntity(typeof(EcsTestData));
            Assert.IsTrue(EmptySystem.ShouldRunSystem());
        }

        [Test]
        [StandaloneFixme]
        public void HasSingletonWorks()
        {
            Assert.IsFalse(EmptySystem.HasSingleton<EcsTestData>());
            m_Manager.CreateEntity(typeof(EcsTestData));
            Assert.IsTrue(EmptySystem.HasSingleton<EcsTestData>());
        }        
    }
}
