using NUnit.Framework;
using UnityEngine;
#pragma warning disable 649
#pragma warning disable 618

namespace Unity.Entities.Tests
{
    class InjectComponentGroupTestsHybrid : ECSTestsFixture
    {
        [DisableAutoCreation]
        [AlwaysUpdateSystem]
        public class ExcludeSystem : ComponentSystem
        {
            public struct Datas
            {
                public ComponentDataArray<EcsTestData> Data;
                public ExcludeComponent<EcsTestData2> Data2;
                public ExcludeComponent<Rigidbody> Rigidbody;
            }

            [Inject]
            public Datas Group;

            protected override void OnUpdate()
            {
            }
        }

        [Test]
        public void ExcludeComponent()
        {
            var subtractiveSystem = World.GetOrCreateManager<ExcludeSystem> ();

            var entity = m_Manager.CreateEntity (typeof(EcsTestData));

            var go = new GameObject("Test", typeof(EcsTestProxy));

            // Ensure entities without the subtractive components are present
            subtractiveSystem.Update ();
            Assert.AreEqual (2, subtractiveSystem.Group.Data.Length);
            Assert.AreEqual (0, subtractiveSystem.Group.Data[0].value);
            Assert.AreEqual (0, subtractiveSystem.Group.Data[1].value);

            // Ensure adding the subtractive components, removes them from the injection
            m_Manager.AddComponentData (entity, new EcsTestData2());

            // TODO: This should be automatic...
            go.AddComponent<Rigidbody>();
            go.GetComponent<GameObjectEntity>().enabled = false;
            go.GetComponent<GameObjectEntity>().enabled = true;

            subtractiveSystem.Update ();
            Assert.AreEqual (0, subtractiveSystem.Group.Data.Length);

            Object.DestroyImmediate(go);
        }
    }
}
