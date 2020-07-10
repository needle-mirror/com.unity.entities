using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.Entities.Tests
{
    //@TODO: Test for prevent adding proxy component to type system...

    class GameObjectEntityTests : HybridRuntimeTestFixture
    {
        [Test]
        public void GameObjectEntityNotAdded()
        {
            var go = new GameObject("test", typeof(GameObjectEntity));
            MarkForAutoDestructionAfterTest(go);
            var entity = GameObjectEntity.AddToEntityManager(m_Manager, go);

            var x = Assert.Throws<ArgumentException>(() => { m_Manager.HasComponent<GameObjectEntity>(entity); });
            Assert.That(x.Message, Contains.Substring("All ComponentType must be known at compile time"));
        }

        [Test]
        public void ComponentsAddedOrNotBasedOnEnabledFlag()
        {
            var ge = new GameObject("with enabled");
            ge.AddComponent<MeshRenderer>().enabled = true;
            var gd = new GameObject("with disabled");
            gd.AddComponent<MeshRenderer>().enabled = false;

            var ee = GameObjectEntity.AddToEntityManager(m_Manager, ge);
            var ed = GameObjectEntity.AddToEntityManager(m_Manager, gd);

            Assert.That(m_Manager.HasComponent<MeshRenderer>(ee), Is.True);
            Assert.That(m_Manager.HasComponent<MeshRenderer>(ed), Is.False);
        }

        [Test]
        public void AddRemoveGetComponentObject()
        {
            var go = new GameObject("test", typeof(Rigidbody));
            var rb = go.GetComponent<Rigidbody>();

            var entity = m_Manager.CreateEntity();

            m_Manager.AddComponentObject(entity, go.GetComponent<Rigidbody>());

            Assert.AreEqual(rb, m_Manager.GetComponentObject<Rigidbody>(entity));;

            m_Manager.RemoveComponent<Rigidbody>(entity);

            Assert.Throws<ArgumentException>(() => m_Manager.GetComponentObject<Rigidbody>(entity));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void AddNullObjectThrows()
        {
            var entity = m_Manager.CreateEntity();
            Assert.Throws<ArgumentNullException>(() => m_Manager.AddComponentObject(entity, null));
        }
    }
}
