using NUnit.Framework;
using Unity.Collections;
using System;
using NUnit.Framework.Interfaces;

namespace Unity.Entities.Tests
{
    [TestFixture]
    public class SystemStateComponentTests : ECSTestsFixture
    {
        [Test]
        public void SSC_DeleteWhenEmpty()
        {
            var entity = m_Manager.CreateEntity(
                typeof(EcsTestData),
                typeof(EcsTestSharedComp),
                typeof(EcsState1)
            );

            m_Manager.SetComponentData(entity, new EcsTestData(1));
            m_Manager.SetComponentData(entity, new EcsState1(2));
            m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp(3));

            {
                var chunks = m_Manager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // any
                    Array.Empty<ComponentType>(), // none
                    new ComponentType[] {typeof(EcsTestData)}, // all
                    Allocator.Temp);
                Assert.AreEqual(1, chunks.EntityCount);
                chunks.Dispose();
            }

            m_Manager.DestroyEntity(entity);

            {
                var chunks = m_Manager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // any
                    Array.Empty<ComponentType>(), // none
                    new ComponentType[] {typeof(EcsTestData)}, // all
                    Allocator.Temp);
                Assert.AreEqual(0, chunks.EntityCount);
                chunks.Dispose();
            }

            {
                var chunks = m_Manager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // any
                    Array.Empty<ComponentType>(), // none
                    new ComponentType[] {typeof(EcsState1)}, // all
                    Allocator.Temp);
                Assert.AreEqual(1, chunks.EntityCount);
                chunks.Dispose();
            }

            m_Manager.RemoveComponent<EcsState1>(entity);

            {
                var chunks = m_Manager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // any
                    Array.Empty<ComponentType>(), // none
                    new ComponentType[] {typeof(EcsState1)}, // all
                    Allocator.Temp);
                Assert.AreEqual(0, chunks.EntityCount);
                chunks.Dispose();
            }

            Assert.IsFalse(m_Manager.Exists(entity));
        }

        [Test]
        public void SSC_DeleteWhenEmptyArray()
        {
            var entities = new Entity[512];

            for (var i = 0; i < 512; i++)
            {
                var entity = m_Manager.CreateEntity(
                    typeof(EcsTestData),
                    typeof(EcsTestSharedComp),
                    typeof(EcsState1)
                );
                entities[i] = entity;

                m_Manager.SetComponentData(entity, new EcsTestData(i));
                m_Manager.SetComponentData(entity, new EcsState1(i));
                m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp(i % 7));
            }

            {
                var chunks = m_Manager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // any
                    Array.Empty<ComponentType>(), // none
                    new ComponentType[] {typeof(EcsTestData)}, // all
                    Allocator.Temp);
                Assert.AreEqual(512, chunks.EntityCount);
                chunks.Dispose();
            }

            for (var i = 0; i < 512; i += 2)
            {
                var entity = entities[i];
                m_Manager.DestroyEntity(entity);
            }

            {
                var chunks = m_Manager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // any
                    Array.Empty<ComponentType>(), // none
                    new ComponentType[] {typeof(EcsTestData)}, // all
                    Allocator.Temp);
                Assert.AreEqual(256, chunks.EntityCount);
                chunks.Dispose();
            }

            {
                var chunks = m_Manager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // any
                    new ComponentType[] {typeof(EcsTestData)}, // none
                    new ComponentType[] {typeof(EcsState1)}, // all
                    Allocator.Temp);
                Assert.AreEqual(256, chunks.EntityCount);
                chunks.Dispose();
            }

            for (var i = 0; i < 512; i += 2)
            {
                var entity = entities[i];
                m_Manager.RemoveComponent<EcsState1>(entity);
            }

            {
                var chunks = m_Manager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // none
                    Array.Empty<ComponentType>(), // none
                    new ComponentType[] {typeof(EcsState1)}, // all
                    Allocator.Temp);
                Assert.AreEqual(256, chunks.EntityCount);
                chunks.Dispose();
            }

            for (var i = 0; i < 512; i += 2)
            {
                var entity = entities[i];
                Assert.IsFalse(m_Manager.Exists(entity));
            }

            for (var i = 1; i < 512; i += 2)
            {
                var entity = entities[i];
                Assert.IsTrue(m_Manager.Exists(entity));
            }
        }
        
        [Test]
        public void SSC_DeleteWhenEmptyArray2()
        {
            var entities = new Entity[512];

            for (var i = 0; i < 512; i++)
            {
                var entity = m_Manager.CreateEntity(
                    typeof(EcsTestData),
                    typeof(EcsTestSharedComp),
                    typeof(EcsState1)
                );
                entities[i] = entity;

                m_Manager.SetComponentData(entity, new EcsTestData(i));
                m_Manager.SetComponentData(entity, new EcsState1(i));
                m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp(i % 7));
            }

            {
                var chunks = m_Manager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // any
                    Array.Empty<ComponentType>(), // none
                    new ComponentType[] {typeof(EcsTestData)}, // all
                    Allocator.Temp);
                Assert.AreEqual(512, chunks.EntityCount);
                chunks.Dispose();
            }

            for (var i = 0; i < 256; i++)
            {
                var entity = entities[i];
                m_Manager.DestroyEntity(entity);
            }

            {
                var chunks = m_Manager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // any
                    Array.Empty<ComponentType>(), // none
                    new ComponentType[] {typeof(EcsTestData)}, // all
                    Allocator.Temp);
                Assert.AreEqual(256, chunks.EntityCount);
                chunks.Dispose();
            }

            {
                var chunks = m_Manager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // any
                    new ComponentType[] {typeof(EcsTestData)}, // none
                    new ComponentType[] {typeof(EcsState1)}, // all
                    Allocator.Temp);
                Assert.AreEqual(256, chunks.EntityCount);
                chunks.Dispose();
            }

            for (var i = 0; i < 256; i++)
            {
                var entity = entities[i];
                m_Manager.RemoveComponent<EcsState1>(entity);
            }

            {
                var chunks = m_Manager.CreateArchetypeChunkArray(
                    Array.Empty<ComponentType>(), // none
                    Array.Empty<ComponentType>(), // none
                    new ComponentType[] {typeof(EcsState1)}, // all
                    Allocator.Temp);
                Assert.AreEqual(256, chunks.EntityCount);
                chunks.Dispose();
            }

            for (var i = 0; i < 256; i++)
            {
                var entity = entities[i];
                Assert.IsFalse(m_Manager.Exists(entity));
            }

            for (var i = 256; i < 512; i++)
            {
                var entity = entities[i];
                Assert.IsTrue(m_Manager.Exists(entity));
            }
        }
    }
}
