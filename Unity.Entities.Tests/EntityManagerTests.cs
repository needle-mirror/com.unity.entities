using System;
using Unity.Collections;
using NUnit.Framework;

namespace Unity.Entities.Tests
{
    interface IEcsFooInterface
    {
        int value { get; set; }

    }
    public struct EcsFooTest : IComponentData, IEcsFooInterface
    {
        public int value { get; set; }

        public EcsFooTest(int inValue) { value = inValue; }
    }

    interface IEcsNotUsedInterface
    {
        int value { get; set; }

    }
    class EntityManagerTests : ECSTestsFixture
    {
#if UNITY_EDITOR
        [Test]
        public void NameEntities()
        {
            WordStorage.Setup();
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var count = 1024;
            var array = new NativeArray<Entity>(count, Allocator.Temp);
            m_Manager.CreateEntity (archetype, array);
            for (int i = 0; i < count; i++)
            {
                m_Manager.SetName(array[i], "Name" + i);
            }
            for (int i = 0; i < count; ++i)
            {
                Assert.AreEqual(m_Manager.GetName(array[i]), "Name" + i);
            }
            // even though we've made 1024 entities, the string table should contain only two entries:
            // "", and "Name"
            Assert.IsTrue(WordStorage.Instance.Entries == 2);
            array.Dispose();
        }

        [Test]
        public void InstantiateKeepsName()
        {
            WordStorage.Setup();
            var entity = m_Manager.CreateEntity ();
            m_Manager.SetName(entity, "Blah");

            var instance = m_Manager.Instantiate(entity);
            Assert.AreEqual("Blah", m_Manager.GetName(instance));
        }
#endif

        [Test]
        public void IncreaseEntityCapacity()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var count = 1024;
            var array = new NativeArray<Entity>(count, Allocator.Temp);
            m_Manager.CreateEntity (archetype, array);
            for (int i = 0; i < count; i++)
            {
                Assert.AreEqual(i, array[i].Index);
            }
            array.Dispose();
        }

        [Test]
        public void FoundComponentInterface()
        {
            var fooTypes = m_Manager.GetAssignableComponentTypes(typeof(IEcsFooInterface));
            Assert.AreEqual(1,fooTypes.Count);
            Assert.AreEqual(typeof(EcsFooTest),fooTypes[0]);

            var barTypes = m_Manager.GetAssignableComponentTypes(typeof(IEcsNotUsedInterface));
            Assert.AreEqual(0,barTypes.Count);
        }

        [Test]
        public void VersionIsConsistent()
        {
            Assert.AreEqual(0, m_Manager.Version);

            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            Assert.AreEqual(1, m_Manager.Version);

            m_Manager.AddComponentData(entity, new EcsTestData2(0));
            Assert.AreEqual(2, m_Manager.Version);

            m_Manager.SetComponentData(entity, new EcsTestData2(5));
            Assert.AreEqual(2, m_Manager.Version); // Shouldn't change when just setting data

            m_Manager.RemoveComponent<EcsTestData2>(entity);
            Assert.AreEqual(3, m_Manager.Version);

            m_Manager.DestroyEntity(entity);
            Assert.AreEqual(4, m_Manager.Version);
        }

        [Test]
        public void GetChunkVersions_ReflectsChange()
        {
            m_Manager.Debug.SetGlobalSystemVersion(1);

            var entity = m_Manager.CreateEntity(typeof(EcsTestData));

            m_Manager.Debug.SetGlobalSystemVersion(2);

            var version = m_Manager.GetChunkVersionHash(entity);

            m_Manager.SetComponentData(entity, new EcsTestData());

            var version2 = m_Manager.GetChunkVersionHash(entity);

            Assert.AreNotEqual(version, version2);
        }

        [Test]
        [Ignore("NOT IMPLEMENTED")]
        public void UsingComponentGroupOrArchetypeorEntityFromDifferentEntityManagerGivesExceptions()
        {
        }

        [Test]
        public unsafe void ComponentsWithBool()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestComponentWithBool));
            var count = 128;
            var array = new NativeArray<Entity>(count, Allocator.Temp);
            m_Manager.CreateEntity(archetype, array);

            var hash = new NativeHashMap<Entity, bool>(count, Allocator.Temp);

            var cg = m_Manager.CreateComponentGroup(ComponentType.ReadWrite<EcsTestComponentWithBool>());
            using (var chunks = cg.CreateArchetypeChunkArray(Allocator.TempJob))
            {
                var boolsType = m_Manager.GetArchetypeChunkComponentType<EcsTestComponentWithBool>(false);
                var entsType = m_Manager.GetArchetypeChunkEntityType();

                foreach (var chunk in chunks)
                {
                    var bools = chunk.GetNativeArray(boolsType);
                    var entities = chunk.GetNativeArray(entsType);

                    for (var i = 0; i < chunk.Count; ++i)
                    {
                        bools[i] = new EcsTestComponentWithBool {value = (entities[i].Index & 1) == 1};
                        Assert.IsTrue(hash.TryAdd(entities[i], bools[i].value));
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                var data = m_Manager.GetComponentData<EcsTestComponentWithBool>(array[i]);
                Assert.AreEqual((array[i].Index & 1) == 1, data.value);
            }

            array.Dispose();
            hash.Dispose();
        }
    }
}
