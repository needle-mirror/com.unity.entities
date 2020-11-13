using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace Unity.Entities.Tests.Conversion
{
    class AuthoringObjectForEntityTests : ConversionTestFixtureBase
    {
        GameObjectConversionSettings MakeWithEntityGUID()
        {
            var settings = MakeDefaultSettings();
            settings.ConversionFlags |= GameObjectConversionUtility.ConversionFlags.AddEntityGUID;
            return settings;
        }

        class CreatePrimaryEntitySystem : GameObjectConversionSystem
        {
            protected override void OnUpdate()
            {
                Entities.ForEach((Transform t) =>
                {
                    var e = GetPrimaryEntity(t);
                    DstEntityManager.AddComponent<EcsTestData>(e);
                });
            }
        }

        class CreateAdditionalEntitySystem : GameObjectConversionSystem
        {
            protected override void OnUpdate()
            {
                Entities.ForEach((Transform t) =>
                {
                    var e = CreateAdditionalEntity(t);
                    DstEntityManager.AddComponent<EcsTestData>(e);
                });
            }
        }

        [Test]
        public void GetAuthoringObjectForEntityWorks()
        {
            var go = CreateGameObject();
            var child = CreateGameObject();
            child.transform.parent = go.transform;

            var entity = GameObjectConversionUtility.ConvertGameObjectHierarchy(go, MakeWithEntityGUID().WithExtraSystem<CreatePrimaryEntitySystem>());
            Assert.AreEqual(2, m_Manager.GetBuffer<LinkedEntityGroup>(entity).Length);
            var childEntity = m_Manager.GetBuffer<LinkedEntityGroup>(entity)[1].Value;

            Assert.AreEqual(go, m_Manager.Debug.GetAuthoringObjectForEntity(entity));
            Assert.AreEqual(child, m_Manager.Debug.GetAuthoringObjectForEntity(childEntity));

            using (var entities = new NativeList<Entity>(Allocator.TempJob))
            {
                m_Manager.Debug.GetEntitiesForAuthoringObject(go, entities);
                Assert.AreEqual(new []{entity}, entities.ToArray());

                m_Manager.Debug.GetEntitiesForAuthoringObject(child, entities);
                Assert.AreEqual(new []{childEntity}, entities.ToArray());
            }
        }

        [Test]
        public void GetAuthoringObjectForMultipleEntityWorks()
        {
            var go = CreateGameObject();

            var rootEntity = GameObjectConversionUtility.ConvertGameObjectHierarchy(go, MakeWithEntityGUID().WithExtraSystems<CreateAdditionalEntitySystem, CreatePrimaryEntitySystem>());
            Assert.AreEqual(2, m_Manager.GetBuffer<LinkedEntityGroup>(rootEntity).Length);
            var linkedEntities = m_Manager.GetBuffer<LinkedEntityGroup>(rootEntity).Reinterpret<Entity>().AsNativeArray().ToArray();

            foreach (var e in linkedEntities)
                Assert.AreEqual(go, m_Manager.Debug.GetAuthoringObjectForEntity(e));

            using (var entities = new NativeList<Entity>(Allocator.TempJob))
            {
                m_Manager.Debug.GetEntitiesForAuthoringObject(go, entities);
                Assert.AreEqual(linkedEntities, entities.ToArray());
            }
        }

        [Test]
        public void GetAuthoringObjectForUnknownEntityWorks()
        {
            var go = CreateGameObject();
            var entity = m_Manager.CreateEntity();

            using (var entities = new NativeList<Entity>(Allocator.TempJob))
            {
                m_Manager.Debug.GetEntitiesForAuthoringObject(go, entities);

                Assert.AreEqual(0, entities.Length);
                Assert.AreEqual(null, m_Manager.Debug.GetAuthoringObjectForEntity(entity));
            }
        }
    }
}
