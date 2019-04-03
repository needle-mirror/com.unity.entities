using System;
using System.IO;
using System.Text;
using Unity.Collections;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace Unity.Entities.Tests
{
    public struct Issue149Data : IComponentData
    {
        public int a;
        public int b;
    }

    public class BatchDestroyTests : ECSTestsFixture
    {
        private EntityArchetype m_Archetype;

        private const int kBatchCount = 512;

        private class EntityBag
        {
            public NativeArray<Entity> Entities;
            public int ValidVersion;
        }

        private EntityBag[] Bags = new EntityBag[3];

        [Test]
        public void TestIssue149()
        {
            m_Archetype = m_Manager.CreateArchetype(typeof(Issue149Data));

            for (int i = 0; i < Bags.Length; ++i)
            {
                Bags[i] = new EntityBag();
            }

            var a = Bags[0];
            var b = Bags[1];
            var c = Bags[2];

            try
            {
                RecycleEntities(a);
                RecycleEntities(b);
                RecycleEntities(c);
                RecycleEntities(a);
                RecycleEntities(b);
                RecycleEntities(a);
                RecycleEntities(c);
                RecycleEntities(a);
                RecycleEntities(a);
                RecycleEntities(b);
                RecycleEntities(a);
                RecycleEntities(c);
                RecycleEntities(a);
            }
            finally
            {
                // To get rid of leak errors in the log when the test fails.
                a.Entities.Dispose();
                b.Entities.Dispose();
                c.Entities.Dispose();
            }
        }

        private void RecycleEntities(EntityBag bag)
        {
            if (bag.Entities.Length > 0)
            {
                m_Manager.DestroyEntity(bag.Entities);
                bag.Entities.Dispose();
            }

            bag.ValidVersion++;

            // Sanity check all arrays.
            SanityCheckVersions();

            bag.Entities = new NativeArray<Entity>(kBatchCount, Allocator.Persistent);

            for (int i = 0; i < bag.Entities.Length; ++i)
            {
                bag.Entities[i] = m_Manager.CreateEntity(m_Archetype);
            }
        }

        private static readonly ComponentType[] s_OurTypes = new ComponentType[] {
            typeof(Issue149Data)
        };

        // Walk all accessible entity data and check that the versions match what we
        // believe the generation numbers should be.
        private void SanityCheckVersions()
        {
            ArchetypeChunkArray chunks = m_Manager.CreateArchetypeChunkArray(
                Array.Empty<ComponentType>(), // none
                Array.Empty<ComponentType>(), // none
                s_OurTypes, // all
                Allocator.Temp);

            ArchetypeChunkEntityType entityType = m_Manager.GetArchetypeChunkEntityType(false);

            for (int i = 0; i < chunks.Length; ++i)
            {
                ArchetypeChunk chunk = chunks[i];
                var entitiesInChunk = chunk.GetNativeArray(entityType);

                for (int k = 0; k < chunk.Count; ++k)
                {
                    Entity e = entitiesInChunk[k];
                    int index = e.Index;
                    int version = e.Version;

                    int ourArray = index / kBatchCount;
                    int ourVersion = Bags[ourArray].ValidVersion;

                    Assert.IsTrue(ourVersion == version);
                }
            }
        }
    }
}
