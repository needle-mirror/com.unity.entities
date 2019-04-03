using NUnit.Framework;
using Unity.Collections;
using System;
using Unity.Entities;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Serialization;

namespace Unity.Entities.Tests
{
    public unsafe class TestBinaryReader : BinaryReader
    {
        NativeList<byte> content;
        int position = 0;
        public TestBinaryReader(TestBinaryWriter writer)
        {
            content = writer.content;
            writer.content = new NativeList<byte>();

        }

        public void Dispose()
        {
            content.Dispose();
        }

        public void ReadBytes(void* data, int bytes)
        {
            UnsafeUtility.MemCpy(data, (byte*)content.GetUnsafePtr() + position, bytes);
            position += bytes;
        }
    }

    public unsafe class TestBinaryWriter : BinaryWriter
    {
        internal NativeList<byte> content = new NativeList<byte>(Allocator.Temp);

        public void Dispose()
        {
            content.Dispose();
        }

        public void WriteBytes(void* data, int bytes)
        {
            int length = content.Length;
            content.ResizeUninitialized(length + bytes);
            UnsafeUtility.MemCpy((byte*)content.GetUnsafePtr() + length, data, bytes);
        }

    }


    public class SerializeTests : ECSTestsFixture
    {
        public struct TestComponentData1 : IComponentData
        {
            public int value;
            public Entity referencedEntity;
        }

        public struct TestComponentData2 : IComponentData
        {
            public int value;
            public Entity referencedEntity;
        }

        [Test]
        public void SerializeEntities()
        {
            var dummyEntity = CreateEntityWithDefaultData(0); //To ensure entity indices are offset
            var e1 = CreateEntityWithDefaultData(1);
            var e2 = CreateEntityWithDefaultData(2);
            var e3 = CreateEntityWithDefaultData(3);
            m_Manager.AddComponentData(e1, new TestComponentData1{ value = 10, referencedEntity = e2 });
            m_Manager.AddComponentData(e2, new TestComponentData2{ value = 20, referencedEntity = e1 });
            m_Manager.AddComponentData(e3, new TestComponentData1{ value = 30, referencedEntity = Entity.Null });
            m_Manager.AddComponentData(e3, new TestComponentData2{ value = 40, referencedEntity = Entity.Null });
            m_Manager.RemoveComponent<EcsTestData2>(e3);

            m_Manager.DestroyEntity(dummyEntity);

            var writer = new TestBinaryWriter();

            int[] sharedData;
            SerializeUtility.SerializeWorld(m_Manager, writer, out sharedData);
            var reader = new TestBinaryReader(writer);

            var deserializedWorld = new World("SerializeEntities Test World 3");
            var entityManager = deserializedWorld.GetOrCreateManager<EntityManager>();

            SerializeUtility.DeserializeWorld(entityManager.BeginExclusiveEntityTransaction(), reader);
            entityManager.EndExclusiveEntityTransaction();

            try
            {
                var allEntities = entityManager.GetAllEntities(Allocator.Temp);
                var count = allEntities.Length;
                allEntities.Dispose();

                Assert.AreEqual(3, count);

                var group1 = entityManager.CreateComponentGroup(typeof(EcsTestData), typeof(EcsTestData2),
                    typeof(TestComponentData1));
                var group2 = entityManager.CreateComponentGroup(typeof(EcsTestData), typeof(EcsTestData2),
                    typeof(TestComponentData2));
                var group3 = entityManager.CreateComponentGroup(typeof(EcsTestData),
                    typeof(TestComponentData1), typeof(TestComponentData2));

                Assert.AreEqual(1, group1.CalculateLength());
                Assert.AreEqual(1, group2.CalculateLength());
                Assert.AreEqual(1, group3.CalculateLength());

                var new_e1 = group1.GetEntityArray()[0];
                var new_e2 = group2.GetEntityArray()[0];
                var new_e3 = group3.GetEntityArray()[0];

                Assert.AreEqual(1, entityManager.GetComponentData<EcsTestData>(new_e1).value);
                Assert.AreEqual(-1, entityManager.GetComponentData<EcsTestData2>(new_e1).value0);
                Assert.AreEqual(-1, entityManager.GetComponentData<EcsTestData2>(new_e1).value1);
                Assert.AreEqual(10, entityManager.GetComponentData<TestComponentData1>(new_e1).value);

                Assert.AreEqual(2, entityManager.GetComponentData<EcsTestData>(new_e2).value);
                Assert.AreEqual(-2, entityManager.GetComponentData<EcsTestData2>(new_e2).value0);
                Assert.AreEqual(-2, entityManager.GetComponentData<EcsTestData2>(new_e2).value1);
                Assert.AreEqual(20, entityManager.GetComponentData<TestComponentData2>(new_e2).value);

                Assert.AreEqual(3, entityManager.GetComponentData<EcsTestData>(new_e3).value);
                Assert.AreEqual(30, entityManager.GetComponentData<TestComponentData1>(new_e3).value);
                Assert.AreEqual(40, entityManager.GetComponentData<TestComponentData2>(new_e3).value);

                Assert.IsTrue(entityManager.Exists(entityManager.GetComponentData<TestComponentData1>(new_e1).referencedEntity));
                Assert.IsTrue(entityManager.Exists(entityManager.GetComponentData<TestComponentData2>(new_e2).referencedEntity));
                Assert.AreEqual(new_e2 , entityManager.GetComponentData<TestComponentData1>(new_e1).referencedEntity);
                Assert.AreEqual(new_e1 , entityManager.GetComponentData<TestComponentData2>(new_e2).referencedEntity);
            }
            finally
            {
                deserializedWorld.Dispose();
                reader.Dispose();
            }
        }
    }
}
