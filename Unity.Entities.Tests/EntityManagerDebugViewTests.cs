#if !NET_DOTS
using System;
using NUnit.Framework;

namespace Unity.Entities.Tests
{
    class EntityManagerDebugViewTests : ECSTestsFixture
    {
        [Test]
        public void IncludesMetaChunkEntities()
        {
            m_Manager.CreateEntity(ComponentType.ChunkComponent<EcsTestData>());
            var debugView = new EntityManagerDebugView(m_Manager);
            Assert.AreEqual(2, debugView.Entities.Length);
        }

        [Test]
        public void IncludesSharedComponents()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestSharedComp));
            var components = new EntityDebugProxy(entity).Components;
            Assert.IsTrue(Array.Exists(components, component => component is EcsTestSharedComp));
        }
    }
}
#endif
