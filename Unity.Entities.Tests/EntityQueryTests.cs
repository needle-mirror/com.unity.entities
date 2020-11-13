#if !NET_DOTS
// https://unity3d.atlassian.net/browse/DOTSR-1432

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using System.Linq;
using Unity.Burst;

namespace Unity.Entities.Tests
{
    [TestFixture]
    class EntityQueryTests : ECSTestsFixture
    {
        public enum EntityQueryJobMode
        {
            Immediate,
            Async,
            AsyncComplete
        };

        ArchetypeChunk[] CreateEntitiesAndReturnChunks(EntityArchetype archetype, int entityCount, Action<Entity> action = null)
        {
            var entities = new NativeArray<Entity>(entityCount, Allocator.Temp);
            m_Manager.CreateEntity(archetype, entities);
#if UNITY_DOTSRUNTIME
            var managedEntities = new Entity[entities.Length];
            for (int i = 0; i < entities.Length; i++)
            {
                managedEntities[i] = entities[i];
            }
#else
            var managedEntities = entities.ToArray();
#endif
            entities.Dispose();

            if (action != null)
                foreach (var e in managedEntities)
                    action(e);

            return managedEntities.Select(e => m_Manager.GetChunk(e)).Distinct().ToArray();
        }

        private unsafe EntityTypeHandle GetEntityTypeHandle(EntityQuery query)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safetyHandles = query._GetImpl()->SafetyHandles;
            var entityType = new EntityTypeHandle(safetyHandles->GetSafetyHandleForEntityTypeHandle());
#else
            var entityType = new EntityTypeHandle();
#endif
            return entityType;
        }

        private unsafe ComponentTypeHandle<T> GetComponentTypeHandle<T>(EntityQuery query)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var impl = query._GetImpl();
            var safetyHandles = impl->SafetyHandles;
            var componentType = new ComponentTypeHandle<T>(
                safetyHandles->GetSafetyHandleForComponentTypeHandle(TypeManager.GetTypeIndex<T>(), true),
                true, impl->_Access->EntityComponentStore->GlobalSystemVersion);
#else
            componentType = new ComponentTypeHandle<T>();
#endif
            return componentType;
        }

        private unsafe UnsafeMatchingArchetypePtrList GetMatchingArchetypes(EntityQuery query)
        {
            var impl = query._GetImpl();
            return impl->_QueryData->MatchingArchetypes;
        }

        [Test]
        public void CreateArchetypeChunkArray()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestData2));
            var archetype12 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));

            var createdChunks1 = CreateEntitiesAndReturnChunks(archetype1, 5000);
            var createdChunks2 = CreateEntitiesAndReturnChunks(archetype2, 5000);
            var createdChunks12 = CreateEntitiesAndReturnChunks(archetype12, 5000);

            var allCreatedChunks = createdChunks1.Concat(createdChunks2).Concat(createdChunks12);

            var query1 = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            var query12 = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2));

            var queriedChunks1 = query1.CreateArchetypeChunkArray(Allocator.TempJob);
            var queriedChunks12 = query12.CreateArchetypeChunkArray(Allocator.TempJob);
            var queriedChunksAll = m_Manager.GetAllChunks(Allocator.TempJob);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks12), queriedChunks1);
            CollectionAssert.AreEquivalent(createdChunks12, queriedChunks12);
            CollectionAssert.AreEquivalent(allCreatedChunks, queriedChunksAll);

            queriedChunks1.Dispose();
            queriedChunks12.Dispose();
            queriedChunksAll.Dispose();
        }

        void SetShared(Entity e, int i)
        {
            m_Manager.SetSharedComponentData(e, new EcsTestSharedComp(i));
        }

        [Test]
        public void CreateArchetypeChunkArray_FiltersSharedComponents()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestData2), typeof(EcsTestSharedComp));

            var createdChunks1 = CreateEntitiesAndReturnChunks(archetype1, 5000, e => SetShared(e, 1));
            var createdChunks2 = CreateEntitiesAndReturnChunks(archetype2, 5000, e => SetShared(e, 1));
            var createdChunks3 = CreateEntitiesAndReturnChunks(archetype1, 5000, e => SetShared(e, 2));
            var createdChunks4 = CreateEntitiesAndReturnChunks(archetype2, 5000, e => SetShared(e, 2));

            var query = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp));

            query.SetSharedComponentFilter(new EcsTestSharedComp(1));

            var queriedChunks1 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetSharedComponentFilter(new EcsTestSharedComp(2));

            var queriedChunks2 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2), queriedChunks1);
            CollectionAssert.AreEquivalent(createdChunks3.Concat(createdChunks4), queriedChunks2);

            query.Dispose();
            queriedChunks1.Dispose();
            queriedChunks2.Dispose();
        }

        void SetShared(Entity e, int i, int j)
        {
            m_Manager.SetSharedComponentData(e, new EcsTestSharedComp(i));
            m_Manager.SetSharedComponentData(e, new EcsTestSharedComp2(j));
        }

        [Test]
        public void CreateArchetypeChunkArray_FiltersTwoSharedComponents()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp), typeof(EcsTestSharedComp2));
            var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestData2), typeof(EcsTestSharedComp), typeof(EcsTestSharedComp2));

            var createdChunks1 = CreateEntitiesAndReturnChunks(archetype1, 5000, e => SetShared(e, 1, 7));
            var createdChunks2 = CreateEntitiesAndReturnChunks(archetype2, 5000, e => SetShared(e, 1, 7));
            var createdChunks3 = CreateEntitiesAndReturnChunks(archetype1, 5000, e => SetShared(e, 2, 7));
            var createdChunks4 = CreateEntitiesAndReturnChunks(archetype2, 5000, e => SetShared(e, 2, 7));
            var createdChunks5 = CreateEntitiesAndReturnChunks(archetype1, 5000, e => SetShared(e, 1, 8));
            var createdChunks6 = CreateEntitiesAndReturnChunks(archetype2, 5000, e => SetShared(e, 1, 8));
            var createdChunks7 = CreateEntitiesAndReturnChunks(archetype1, 5000, e => SetShared(e, 2, 8));
            var createdChunks8 = CreateEntitiesAndReturnChunks(archetype2, 5000, e => SetShared(e, 2, 8));

            var query = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp), typeof(EcsTestSharedComp2));

            query.SetSharedComponentFilter(new EcsTestSharedComp(1), new EcsTestSharedComp2(7));
            var queriedChunks1 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetSharedComponentFilter(new EcsTestSharedComp(2), new EcsTestSharedComp2(7));
            var queriedChunks2 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetSharedComponentFilter(new EcsTestSharedComp(1), new EcsTestSharedComp2(8));
            var queriedChunks3 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetSharedComponentFilter(new EcsTestSharedComp(2), new EcsTestSharedComp2(8));
            var queriedChunks4 =  query.CreateArchetypeChunkArray(Allocator.TempJob);


            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2), queriedChunks1);
            CollectionAssert.AreEquivalent(createdChunks3.Concat(createdChunks4), queriedChunks2);
            CollectionAssert.AreEquivalent(createdChunks5.Concat(createdChunks6), queriedChunks3);
            CollectionAssert.AreEquivalent(createdChunks7.Concat(createdChunks8), queriedChunks4);

            query.Dispose();
            queriedChunks1.Dispose();
            queriedChunks2.Dispose();
            queriedChunks3.Dispose();
            queriedChunks4.Dispose();
        }

        void SetData(Entity e, int i)
        {
            m_Manager.SetComponentData(e, new EcsTestData(i));
        }

        [Test]
        public void CreateArchetypeChunkArray_FiltersChangeVersions()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var archetype3 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData3));

            m_ManagerDebug.SetGlobalSystemVersion(20);
            var createdChunks1 = CreateEntitiesAndReturnChunks(archetype1, 5000, e => SetData(e, 1));
            m_ManagerDebug.SetGlobalSystemVersion(30);
            var createdChunks2 = CreateEntitiesAndReturnChunks(archetype2, 5000, e => SetData(e, 2));
            m_ManagerDebug.SetGlobalSystemVersion(40);
            var createdChunks3 = CreateEntitiesAndReturnChunks(archetype3, 5000, e => SetData(e, 3));

            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            query.SetChangedVersionFilter(typeof(EcsTestData));

            query.SetChangedFilterRequiredVersion(10);
            var queriedChunks1 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetChangedFilterRequiredVersion(20);
            var queriedChunks2 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetChangedFilterRequiredVersion(30);
            var queriedChunks3 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetChangedFilterRequiredVersion(40);
            var queriedChunks4 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2).Concat(createdChunks3), queriedChunks1);
            CollectionAssert.AreEquivalent(createdChunks2.Concat(createdChunks3), queriedChunks2);
            CollectionAssert.AreEquivalent(createdChunks3, queriedChunks3);

            Assert.AreEqual(0, queriedChunks4.Length);

            query.Dispose();
            queriedChunks1.Dispose();
            queriedChunks2.Dispose();
            queriedChunks3.Dispose();
            queriedChunks4.Dispose();
        }

        void SetData(Entity e, int i, int j)
        {
            m_Manager.SetComponentData(e, new EcsTestData(i));
            m_Manager.SetComponentData(e, new EcsTestData2(j));
        }

        [Test]
        public void CreateArchetypeChunkArray_FiltersTwoChangeVersions()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3));
            var archetype3 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData4));

            m_ManagerDebug.SetGlobalSystemVersion(20);
            var createdChunks1 = CreateEntitiesAndReturnChunks(archetype1, 5000, e => SetData(e, 1, 4));
            m_ManagerDebug.SetGlobalSystemVersion(30);
            var createdChunks2 = CreateEntitiesAndReturnChunks(archetype2, 5000, e => SetData(e, 2, 5));
            m_ManagerDebug.SetGlobalSystemVersion(40);
            var createdChunks3 = CreateEntitiesAndReturnChunks(archetype3, 5000, e => SetData(e, 3, 6));

            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2));

            query.SetChangedVersionFilter(new ComponentType[] {typeof(EcsTestData), typeof(EcsTestData2)});

            query.SetChangedFilterRequiredVersion(30);

            var testType1 = m_Manager.GetComponentTypeHandle<EcsTestData>(false);
            var testType2 = m_Manager.GetComponentTypeHandle<EcsTestData2>(false);

            var queriedChunks1 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            foreach (var chunk in createdChunks1)
            {
                var array = chunk.GetNativeArray(testType1);
                array[0] = new EcsTestData(7);
            }

            var queriedChunks2 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            foreach (var chunk in createdChunks2)
            {
                var array = chunk.GetNativeArray(testType2);
                array[0] = new EcsTestData2(7);
            }

            var queriedChunks3 =  query.CreateArchetypeChunkArray(Allocator.TempJob);


            CollectionAssert.AreEquivalent(createdChunks3, queriedChunks1);
            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks3), queriedChunks2);

            query.Dispose();
            queriedChunks1.Dispose();
            queriedChunks2.Dispose();
            queriedChunks3.Dispose();
        }

        [Test]
        public void CreateArchetypeChunkArray_FiltersOrderVersions()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var archetype3 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData3));

            m_ManagerDebug.SetGlobalSystemVersion(20);
            var createdChunks1 = CreateEntitiesAndReturnChunks(archetype1, 5000, e => SetData(e, 1));
            m_ManagerDebug.SetGlobalSystemVersion(30);
            var createdChunks2 = CreateEntitiesAndReturnChunks(archetype2, 5000, e => SetData(e, 2));
            m_ManagerDebug.SetGlobalSystemVersion(40);
            var createdChunks3 = CreateEntitiesAndReturnChunks(archetype3, 5000, e => SetData(e, 3));

            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            query.SetOrderVersionFilter();

            query.SetChangedFilterRequiredVersion(10);
            var queriedChunks1 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetChangedFilterRequiredVersion(20);
            var queriedChunks2 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetChangedFilterRequiredVersion(30);
            var queriedChunks3 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetChangedFilterRequiredVersion(40);
            var queriedChunks4 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2).Concat(createdChunks3), queriedChunks1);
            CollectionAssert.AreEquivalent(createdChunks2.Concat(createdChunks3), queriedChunks2);
            CollectionAssert.AreEquivalent(createdChunks3, queriedChunks3);

            Assert.AreEqual(0, queriedChunks4.Length);

            query.Dispose();
            queriedChunks1.Dispose();
            queriedChunks2.Dispose();
            queriedChunks3.Dispose();
            queriedChunks4.Dispose();
        }

        [Test]
        public void CreateArchetypeChunkArray_FiltersOrderAndChangedVersions()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var archetype3 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData3));

            m_ManagerDebug.SetGlobalSystemVersion(20);
            var createdChunks1 = CreateEntitiesAndReturnChunks(archetype1, 5000, e => SetData(e, 1));
            m_ManagerDebug.SetGlobalSystemVersion(30);
            var createdChunks2 = CreateEntitiesAndReturnChunks(archetype2, 5000, e => SetData(e, 2));
            m_ManagerDebug.SetGlobalSystemVersion(40);
            var createdChunks3 = CreateEntitiesAndReturnChunks(archetype3, 5000, e => SetData(e, 3));

            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            query.SetOrderVersionFilter();
            query.SetChangedVersionFilter(typeof(EcsTestData));

            query.SetChangedFilterRequiredVersion(10);
            var queriedChunks1 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetChangedFilterRequiredVersion(20);
            var queriedChunks2 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetChangedFilterRequiredVersion(30);
            var queriedChunks3 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetChangedFilterRequiredVersion(40);
            var queriedChunks4 =  query.CreateArchetypeChunkArray(Allocator.TempJob);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2).Concat(createdChunks3), queriedChunks1);
            CollectionAssert.AreEquivalent(createdChunks2.Concat(createdChunks3), queriedChunks2);
            CollectionAssert.AreEquivalent(createdChunks3, queriedChunks3);

            Assert.AreEqual(0, queriedChunks4.Length);

            query.Dispose();
            queriedChunks1.Dispose();
            queriedChunks2.Dispose();
            queriedChunks3.Dispose();
            queriedChunks4.Dispose();
        }

        void SetDataAndShared(Entity e, int data, int shared)
        {
            SetData(e, data);
            SetShared(e, shared);
        }

        [Test]
        public void CreateArchetypeChunkArray_FiltersOneSharedOneChangeVersion()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));
            var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));

            // 9 chunks
            // 3 of archetype1 with 1 shared value
            // 3 of archetype2 with 1 shared value
            // 3 of archetype1 with 2 shared value
            m_ManagerDebug.SetGlobalSystemVersion(10);
            var createdChunks1 = CreateEntitiesAndReturnChunks(archetype1, archetype1.ChunkCapacity * 3, e => SetDataAndShared(e, 1, 1));
            var createdChunks2 = CreateEntitiesAndReturnChunks(archetype2, archetype2.ChunkCapacity * 3, e => SetDataAndShared(e, 2, 1));
            var createdChunks3 = CreateEntitiesAndReturnChunks(archetype1, archetype1.ChunkCapacity * 3, e => SetDataAndShared(e, 3, 2));

            // query matches all three
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp));

            query.AddChangedVersionFilter(typeof(EcsTestData));
            query.AddSharedComponentFilter(new EcsTestSharedComp {value = 1});

            var queriedChunks1 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetChangedFilterRequiredVersion(10);
            var queriedChunks2 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            // bumps the version number for TestData1 for createdChunks1
            m_ManagerDebug.SetGlobalSystemVersion(20);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                var array = createdChunks1[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData>());
                array[0] = new EcsTestData {value = 10};
            }
            var queriedChunks3 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            // bumps the version number for TestData2
            query.SetChangedFilterRequiredVersion(20);
            m_ManagerDebug.SetGlobalSystemVersion(30);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                var array = createdChunks1[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData2>());
                array[0] = new EcsTestData2 {value1 = 10, value0 = 10};
            }
            var queriedChunks4 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2), queriedChunks1); // query 1 = created 1,2
            Assert.AreEqual(0, queriedChunks2.Length); // query 2 is empty
            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks3); // query 3 = created 1 (version # was bumped)
            Assert.AreEqual(0, queriedChunks4.Length); // query 4 is empty (version # of type we're not change tracking was bumped)

            query.Dispose();
            queriedChunks1.Dispose();
            queriedChunks2.Dispose();
            queriedChunks3.Dispose();
            queriedChunks4.Dispose();
        }

        [Test]
        public void CreateArchetypeChunkArray_FiltersOneSharedOrderVersion()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));
            var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));

            // 9 chunks
            // 3 of archetype1 with 1 shared value
            // 3 of archetype2 with 1 shared value
            // 3 of archetype1 with 2 shared value
            m_ManagerDebug.SetGlobalSystemVersion(10);
            var createdChunks1 = CreateEntitiesAndReturnChunks(archetype1, archetype1.ChunkCapacity * 3, e => SetDataAndShared(e, 1, 1));
            var createdChunks2 = CreateEntitiesAndReturnChunks(archetype2, archetype2.ChunkCapacity * 3, e => SetDataAndShared(e, 2, 1));
            var createdChunks3 = CreateEntitiesAndReturnChunks(archetype1, archetype1.ChunkCapacity * 3, e => SetDataAndShared(e, 3, 2));

            // query matches all three
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp));

            query.AddOrderVersionFilter();
            query.AddSharedComponentFilter(new EcsTestSharedComp {value = 1});

            var queriedChunks1 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetChangedFilterRequiredVersion(10);
            var queriedChunks2 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            // bumps the order version number for createdChunks1
            m_ManagerDebug.SetGlobalSystemVersion(20);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                var array = createdChunks1[i].GetNativeArray(EmptySystem.GetEntityTypeHandle());
                m_Manager.AddComponent(array, typeof(EcsTestTag));
            }
            var queriedChunks3 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2), queriedChunks1); // query 1 = created 1,2
            Assert.AreEqual(0, queriedChunks2.Length); // query 2 is empty
            Assert.AreEqual(createdChunks1.Length, queriedChunks3.Length); // query 3 = created 1 (version # was bumped) (not collection equivalent because it is a new archetype so the chunk *has* changed)

            query.Dispose();
            queriedChunks1.Dispose();
            queriedChunks2.Dispose();
            queriedChunks3.Dispose();
        }

        [Test]
        public void CreateArchetypeChunkArray_FiltersOneSharedTwoChangeVersion()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3), typeof(EcsTestSharedComp));
            var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));

            // 9 chunks
            // 3 of archetype1 with 1 shared value
            // 3 of archetype2 with 1 shared value
            // 3 of archetype1 with 2 shared value
            m_ManagerDebug.SetGlobalSystemVersion(10);
            var createdChunks1 = CreateEntitiesAndReturnChunks(archetype1, archetype1.ChunkCapacity * 3, e => SetDataAndShared(e, 1, 1));
            var createdChunks2 = CreateEntitiesAndReturnChunks(archetype2, archetype2.ChunkCapacity * 3, e => SetDataAndShared(e, 2, 1));
            var createdChunks3 = CreateEntitiesAndReturnChunks(archetype1, archetype1.ChunkCapacity * 3, e => SetDataAndShared(e, 3, 2));

            // query matches all three
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));

            query.AddChangedVersionFilter(typeof(EcsTestData));
            query.AddChangedVersionFilter(typeof(EcsTestData2));
            query.AddSharedComponentFilter(new EcsTestSharedComp {value = 1});

            var queriedChunks1 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetChangedFilterRequiredVersion(10);
            var queriedChunks2 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            // bumps the version number for TestData1 for createdChunks1
            m_ManagerDebug.SetGlobalSystemVersion(20);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                var array = createdChunks1[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData>());
                array[0] = new EcsTestData {value = 10};
            }
            var queriedChunks3 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            // bumps the version number for TestData2
            query.SetChangedFilterRequiredVersion(20);
            m_ManagerDebug.SetGlobalSystemVersion(30);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                var array = createdChunks1[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData2>());
                array[0] = new EcsTestData2 {value1 = 10, value0 = 10};
            }
            var queriedChunks4 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2), queriedChunks1); // query 1 = created 1,2
            Assert.AreEqual(0, queriedChunks2.Length); // query 2 is empty
            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks3); // query 3 = created 1 (version # of type1 was bumped)
            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks4); // query 4 = created 1 (version # of type2 was bumped)

            query.Dispose();
            queriedChunks1.Dispose();
            queriedChunks2.Dispose();
            queriedChunks3.Dispose();
            queriedChunks4.Dispose();
        }

        void SetDataAndShared(Entity e, int data, int shared1, int shared2)
        {
            SetData(e, data);
            SetShared(e, shared1, shared2);
        }

        [Test]
        public void CreateArchetypeChunkArray_FiltersTwoSharedOneChangeVersion()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp), typeof(EcsTestSharedComp2));
            var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp), typeof(EcsTestSharedComp2));

            // 9 chunks
            // 3 of archetype1 with 1 shared value1, 3,3 shared value2
            // 3 of archetype2 with 1 shared value1, 4,4 shared value2
            // 3 of archetype1 with 2 shared value1, 3,3 shared value2
            m_ManagerDebug.SetGlobalSystemVersion(10);
            var createdChunks1 = CreateEntitiesAndReturnChunks(archetype1, archetype1.ChunkCapacity * 3, e => SetDataAndShared(e, 1, 1, 3));
            var createdChunks2 = CreateEntitiesAndReturnChunks(archetype2, archetype2.ChunkCapacity * 3, e => SetDataAndShared(e, 2, 1, 4));
            var createdChunks3 = CreateEntitiesAndReturnChunks(archetype1, archetype1.ChunkCapacity * 3, e => SetDataAndShared(e, 3, 2, 3));

            // query matches all three
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp), typeof(EcsTestSharedComp2));

            query.AddChangedVersionFilter(typeof(EcsTestData));
            query.AddSharedComponentFilter(new EcsTestSharedComp {value = 1});
            query.AddSharedComponentFilter(new EcsTestSharedComp2 {value0 = 3, value1 = 3});

            var queriedChunks1 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetChangedFilterRequiredVersion(10);
            var queriedChunks2 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            // bumps the version number for TestData1 for createdChunks1 and createdChunks2
            m_ManagerDebug.SetGlobalSystemVersion(20);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                {
                    var array = createdChunks1[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData>());
                    array[0] = new EcsTestData {value = 10};
                }
                {
                    var array = createdChunks3[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData>());
                    array[0] = new EcsTestData {value = 10};
                }
            }
            var queriedChunks3 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            // bumps the version number for TestData2 for createdChunks1
            query.SetChangedFilterRequiredVersion(20);
            m_ManagerDebug.SetGlobalSystemVersion(30);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                var array = createdChunks1[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData2>());
                array[0] = new EcsTestData2 {value1 = 10, value0 = 10};
            }
            var queriedChunks4 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks1); // query 1 = created 1
            Assert.AreEqual(0, queriedChunks2.Length); // query 2 is empty
            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks3); // query 3 = created 1 (version # was bumped and we're filtering out created2)
            Assert.AreEqual(0, queriedChunks4.Length); // query 4 is empty (version # of type we're not change tracking was bumped)

            query.Dispose();
            queriedChunks1.Dispose();
            queriedChunks2.Dispose();
            queriedChunks3.Dispose();
            queriedChunks4.Dispose();
        }

        [Test]
        public void CreateArchetypeChunkArray_FiltersTwoSharedTwoChangeVersion()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3), typeof(EcsTestSharedComp), typeof(EcsTestSharedComp2));
            var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp), typeof(EcsTestSharedComp2));

            // 9 chunks
            // 3 of archetype1 with 1 shared value1, 3,3 shared value2
            // 3 of archetype2 with 1 shared value1, 4,4 shared value2
            // 3 of archetype1 with 2 shared value1, 3,3 shared value2
            m_ManagerDebug.SetGlobalSystemVersion(10);
            var createdChunks1 = CreateEntitiesAndReturnChunks(archetype1, archetype1.ChunkCapacity * 3, e => SetDataAndShared(e, 1, 1, 3));
            var createdChunks2 = CreateEntitiesAndReturnChunks(archetype2, archetype2.ChunkCapacity * 3, e => SetDataAndShared(e, 2, 1, 4));
            var createdChunks3 = CreateEntitiesAndReturnChunks(archetype1, archetype1.ChunkCapacity * 3, e => SetDataAndShared(e, 3, 2, 3));

            // query matches all three
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp), typeof(EcsTestSharedComp2));

            query.AddChangedVersionFilter(typeof(EcsTestData));
            query.AddChangedVersionFilter(typeof(EcsTestData2));
            query.AddSharedComponentFilter(new EcsTestSharedComp {value = 1});
            query.AddSharedComponentFilter(new EcsTestSharedComp2 {value0 = 3, value1 = 3});

            var queriedChunks1 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            query.SetChangedFilterRequiredVersion(10);
            var queriedChunks2 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            // bumps the version number for TestData1 for createdChunks1 and createdChunks2
            m_ManagerDebug.SetGlobalSystemVersion(20);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                {
                    var array = createdChunks1[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData>());
                    array[0] = new EcsTestData {value = 10};
                }
                {
                    var array = createdChunks3[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData>());
                    array[0] = new EcsTestData {value = 10};
                }
            }
            var queriedChunks3 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            // bumps the version number for TestData2 for createdChunks1
            query.SetChangedFilterRequiredVersion(20);
            m_ManagerDebug.SetGlobalSystemVersion(30);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                var array = createdChunks1[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData2>());
                array[0] = new EcsTestData2 {value1 = 10, value0 = 10};
            }
            var queriedChunks4 = query.CreateArchetypeChunkArray(Allocator.TempJob);

            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks1); // query 1 = created 1
            Assert.AreEqual(0, queriedChunks2.Length); // query 2 is empty
            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks3); // query 3 = created 1 (version # was bumped and we're filtering out created2)
            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks4); // query 4 = created 1 (version # of type2 was bumped)

            query.Dispose();
            queriedChunks1.Dispose();
            queriedChunks2.Dispose();
            queriedChunks3.Dispose();
            queriedChunks4.Dispose();
        }

        [Test]
        public void FiltersOrderVersion()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));

            m_ManagerDebug.SetGlobalSystemVersion(10);
            var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity, Allocator.Temp);

            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            Assert.AreEqual(1, query.CalculateChunkCount());

            query.SetChangedFilterRequiredVersion(10);
            query.SetOrderVersionFilter();

            Assert.AreEqual(0, query.CalculateChunkCount());

            // "Other System runs"
            m_ManagerDebug.SetGlobalSystemVersion(11);
            for (int i = 0; i < archetype.ChunkCapacity; i += 2)
            {
                m_Manager.AddComponent(entities[i], typeof(EcsTestTag));
            }

            Assert.AreEqual(2, query.CalculateChunkCount());

            query.Dispose();
        }

        [Test]
        public void ChangeVersionFollowsEntity()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            query.SetChangedVersionFilter(typeof(EcsTestData));
            query.SetChangedFilterRequiredVersion(10);

            // "Entity is created"
            m_ManagerDebug.SetGlobalSystemVersion(10);
            var entity = m_Manager.CreateEntity(archetype);

            Assert.AreEqual(0, query.CalculateEntityCount());

            // "System runs"
            m_ManagerDebug.SetGlobalSystemVersion(11);
            m_Manager.SetComponentData(entity, new EcsTestData {value = 50});

            Assert.AreEqual(1, query.CalculateEntityCount());

            // "System runs"
            m_ManagerDebug.SetGlobalSystemVersion(12);
            m_Manager.SetComponentData(entity, new EcsTestData {value = 50});
            m_Manager.AddComponent(entity, typeof(EcsTestData2));

            Assert.AreEqual(1, query.CalculateEntityCount());

            query.Dispose();
        }

        // https://github.com/Unity-Technologies/dots/issues/1098
        [Test]
        public void TestIssue1098()
        {
            m_Manager.CreateEntity(typeof(EcsTestData));

            using
            (
                var query = m_Manager.CreateEntityQuery
                    (
                        new EntityQueryDesc
                        {
                            All = new ComponentType[] {typeof(EcsTestData)}
                        }
                    )
            )
                // NB: EcsTestData != EcsTestData2
                Assert.Throws<InvalidOperationException>(() => query.ToComponentDataArray<EcsTestData2>(Allocator.TempJob));
        }

#if !UNITY_DOTSRUNTIME // IJobForEach is deprecated

        [AlwaysUpdateSystem]
        public class WriteEcsTestDataSystem : JobComponentSystem
        {
#pragma warning disable 618
            private struct WriteJob : IJobForEach<EcsTestData>
            {
                public void Execute(ref EcsTestData c0) {}
            }
#pragma warning restore 618

            protected override JobHandle OnUpdate(JobHandle input)
            {
                var job = new WriteJob() {};
                return job.Schedule(this, input);
            }
        }

        [Test]
        public unsafe void CreateArchetypeChunkArray_SyncsChangeFilterTypes()
        {
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            query.SetChangedVersionFilter(typeof(EcsTestData));
            var ws1 = World.GetOrCreateSystem<WriteEcsTestDataSystem>();
            ws1.Update();
            var safetyHandle = m_Manager.GetCheckedEntityDataAccess()->DependencyManager->Safety.GetSafetyHandle(TypeManager.GetTypeIndex<EcsTestData>(), false);

            Assert.Throws<InvalidOperationException>(() => AtomicSafetyHandle.CheckWriteAndThrow(safetyHandle));
            var chunks =  query.CreateArchetypeChunkArray(Allocator.TempJob);
            AtomicSafetyHandle.CheckWriteAndThrow(safetyHandle);

            chunks.Dispose();
            query.Dispose();
        }

        [Test]
        public unsafe void CalculateEntityCount_SyncsChangeFilterTypes()
        {
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            query.SetChangedVersionFilter(typeof(EcsTestData));
            var ws1 = World.GetOrCreateSystem<WriteEcsTestDataSystem>();
            ws1.Update();
            var safetyHandle = m_Manager.GetCheckedEntityDataAccess()->DependencyManager->Safety.GetSafetyHandle(TypeManager.GetTypeIndex<EcsTestData>(), false);

            Assert.Throws<InvalidOperationException>(() => AtomicSafetyHandle.CheckWriteAndThrow(safetyHandle));
            query.CalculateEntityCount();
            AtomicSafetyHandle.CheckWriteAndThrow(safetyHandle);

            query.Dispose();
        }

#endif

        [Test]
        public void ToEntityArrayFiltered([Values] EntityQueryJobMode jobMode)
        {
            // Note - test is setup so that each entity is in its own chunk, this checks that entity indices are correct
            var a = m_Manager.CreateEntity(typeof(EcsTestSharedComp), typeof(EcsTestData));
            var b = m_Manager.CreateEntity(typeof(EcsTestSharedComp), typeof(EcsTestData2));
            var c = m_Manager.CreateEntity(typeof(EcsTestSharedComp), typeof(EcsTestData3));

            m_Manager.SetSharedComponentData(a, new EcsTestSharedComp {value = 123});
            m_Manager.SetSharedComponentData(b, new EcsTestSharedComp {value = 456});
            m_Manager.SetSharedComponentData(c, new EcsTestSharedComp {value = 123});

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp)))
            {
                query.SetSharedComponentFilter(new EcsTestSharedComp {value = 123});
                NativeArray<Entity> entities;


                switch (jobMode)
                {
                    case EntityQueryJobMode.Async:
                        entities = query.ToEntityArrayAsync(Allocator.TempJob, out JobHandle jobHandle);
                        jobHandle.Complete();
                        break;
                    case EntityQueryJobMode.AsyncComplete:
                        entities = ChunkIterationUtility.CreateEntityArrayAsyncComplete(GetMatchingArchetypes(query),
                            Allocator.TempJob,
                            GetEntityTypeHandle(query), query, query.CalculateEntityCount(),query.GetDependency());
                        break;
                    default: //EntityQueryMethodType.Immediate
                        entities = ChunkIterationUtility.CreateEntityArray(GetMatchingArchetypes(query),
                            Allocator.TempJob,
                            GetEntityTypeHandle(query), query, query.CalculateEntityCount());
                        break;
                }


                CollectionAssert.AreEqual(new[] {a, c}, entities);
                entities.Dispose();

                query.SetSharedComponentFilter(new EcsTestSharedComp {value = 456});

                switch (jobMode)
                {
                    case EntityQueryJobMode.Async:
                    entities = query.ToEntityArrayAsync(Allocator.TempJob, out JobHandle jobHandle);
                    jobHandle.Complete();
                    break;
                    case EntityQueryJobMode.AsyncComplete:
                    entities = ChunkIterationUtility.CreateEntityArrayAsyncComplete(GetMatchingArchetypes(query),
                        Allocator.TempJob,
                        GetEntityTypeHandle(query), query, query.CalculateEntityCount(),query.GetDependency());
                    break;
                    default: //EntityQueryMethodType.Immediate
                    entities = ChunkIterationUtility.CreateEntityArray(GetMatchingArchetypes(query),
                        Allocator.TempJob,
                        GetEntityTypeHandle(query), query, query.CalculateEntityCount());
                    break;
                }

                CollectionAssert.AreEqual(new[] {b}, entities);
                entities.Dispose();
            }
        }

        [Test]
        public void ToEntityArrayUnfiltered([Values] EntityQueryJobMode jobMode)
        {
            int count = 1000;
            for (int i = 0; i < count ; i++)
            {
                var entity = m_Manager.CreateEntity(typeof(EcsTestData3));
                if (i % 2 == 0)
                {
                    m_Manager.AddComponentData(entity, new EcsTestData
                    {
                        value = i
                    });
                }
            }


            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData),typeof(EcsTestData3)))
            {
                NativeArray<Entity> entities;
                switch (jobMode)
                {
                    case EntityQueryJobMode.Async:
                    entities = query.ToEntityArrayAsync(Allocator.TempJob, out JobHandle jobHandle);
                    jobHandle.Complete();
                    break;
                    case EntityQueryJobMode.AsyncComplete:
                    entities = ChunkIterationUtility.CreateEntityArrayAsyncComplete(GetMatchingArchetypes(query),
                        Allocator.TempJob,
                        GetEntityTypeHandle(query), query, query.CalculateEntityCount(),query.GetDependency());
                    break;
                    default: //EntityQueryMethodType.Immediate
                    entities = ChunkIterationUtility.CreateEntityArray(GetMatchingArchetypes(query),
                        Allocator.TempJob,
                        GetEntityTypeHandle(query), query, query.CalculateEntityCount());
                    break;
                }
                Assert.AreEqual(count / 2, entities.Length);
                for (int i = 0; i < count / 2 ; i++)
                {
                    Assert.IsTrue(m_Manager.HasComponent<EcsTestData>(entities[i]));
                    var value = m_Manager.GetComponentData<EcsTestData>(entities[i]).value;
                    Assert.AreEqual(i * 2,value);
                }
                entities.Dispose();
            }
        }


        [Test]
        public void ToComponentDataArrayFiltered([Values] EntityQueryJobMode jobMode)
        {
            var ecsTestData1 = new EcsTestData
            {
                value = 1
            };
            var ecsTestData2 = new EcsTestData
            {
                value = 2
            };
            var ecsTestData3 = new EcsTestData
            {
                value = 3
            };

            var a = m_Manager.CreateEntity(typeof(EcsTestSharedComp), typeof(EcsTestData));
            m_Manager.SetComponentData(a,ecsTestData1);
            var b = m_Manager.CreateEntity(typeof(EcsTestSharedComp), typeof(EcsTestData));
            m_Manager.SetComponentData(b,ecsTestData2);
            var c = m_Manager.CreateEntity(typeof(EcsTestSharedComp), typeof(EcsTestData));
            m_Manager.SetComponentData(c,ecsTestData3);

            m_Manager.SetSharedComponentData(a, new EcsTestSharedComp {value = 123});
            m_Manager.SetSharedComponentData(b, new EcsTestSharedComp {value = 456});
            m_Manager.SetSharedComponentData(c, new EcsTestSharedComp {value = 123});

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp),typeof(EcsTestData)))
            {
                NativeArray<EcsTestData> components;

                switch (jobMode)
                {
                    case EntityQueryJobMode.Async:
                        components = query.ToComponentDataArrayAsync<EcsTestData>(Allocator.TempJob, out JobHandle jobHandle);
                        jobHandle.Complete();
                        break;
                    case EntityQueryJobMode.AsyncComplete:
                        components = ChunkIterationUtility.CreateComponentDataArrayAsyncComplete(Allocator.TempJob,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(),
                            query,query.GetDependency());
                        break;
                    default: //EntityQueryMethodType.Immediate
                        components = ChunkIterationUtility.CreateComponentDataArray(Allocator.TempJob,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(), query);
                        break;
                }

                CollectionAssert.AreEquivalent(new[] {ecsTestData1,ecsTestData2,ecsTestData3}, components);
                components.Dispose();

                query.SetSharedComponentFilter(new EcsTestSharedComp {value = 123});

                switch (jobMode)
                {
                    case EntityQueryJobMode.Async:
                        components = query.ToComponentDataArrayAsync<EcsTestData>(Allocator.TempJob, out JobHandle jobHandle);
                        jobHandle.Complete();
                        break;
                    case EntityQueryJobMode.AsyncComplete:
                        components = ChunkIterationUtility.CreateComponentDataArrayAsyncComplete(Allocator.TempJob,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(),
                            query,query.GetDependency());
                        break;
                    default: //EntityQueryMethodType.Immediate
                        components = ChunkIterationUtility.CreateComponentDataArray(Allocator.TempJob,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(), query);
                        break;
                }

                CollectionAssert.AreEqual(new[] {ecsTestData1,ecsTestData3}, components);
                components.Dispose();

                query.SetSharedComponentFilter(new EcsTestSharedComp {value = 456});

                switch (jobMode)
                {
                    case EntityQueryJobMode.Async:
                        components = query.ToComponentDataArrayAsync<EcsTestData>(Allocator.TempJob, out JobHandle jobHandle);
                        jobHandle.Complete();
                        break;
                    case EntityQueryJobMode.AsyncComplete:
                        components = ChunkIterationUtility.CreateComponentDataArrayAsyncComplete(Allocator.TempJob,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(),
                            query,query.GetDependency());
                        break;
                    default: //EntityQueryMethodType.Immediate
                        components = ChunkIterationUtility.CreateComponentDataArray(Allocator.TempJob,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(), query);
                        break;
                }

                CollectionAssert.AreEqual(new[] {ecsTestData2}, components);
                components.Dispose();
            }
        }

        [Test]
        public void ToComponentDataArrayUnfiltered([Values] EntityQueryJobMode jobMode)
        {
            var ecsData20 = new EcsTestData
            {
                value = 20
            };
            var ecsData40 = new EcsTestData
            {
                value = 40
            };
            var ecsData60 = new EcsTestData
            {
                value = 60
            };

            var ecsData2_20_40 = new EcsTestData2
            {
                value0 = 20,
                value1 = 40
            };

            var ecsData2_60_80 = new EcsTestData2
            {
                value0 = 60,
                value1 = 80
            };

            var ecsData3_20_40_60 = new EcsTestData3
            {
                value0 = 20,
                value1 = 40,
                value2 = 60,
            };

            var ecsData3_80_100_120 = new EcsTestData3
            {
                value0 = 80,
                value1 = 100,
                value2 = 120,
            };

            var a = m_Manager.CreateEntity( typeof(EcsTestData));
            m_Manager.SetComponentData(a,ecsData20);
            var b = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));
            m_Manager.SetComponentData(b,ecsData2_20_40);
            m_Manager.SetComponentData(b,ecsData40);
            var c = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData3));
            m_Manager.SetComponentData(c,ecsData3_20_40_60);
            m_Manager.SetComponentData(c,ecsData60);

            var d = m_Manager.CreateEntity(typeof(EcsTestData2));
            m_Manager.SetComponentData(d,ecsData2_60_80);
            var e = m_Manager.CreateEntity(typeof(EcsTestData3));
            m_Manager.SetComponentData(e,ecsData3_80_100_120);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                NativeArray<EcsTestData> components;
                switch (jobMode)
                {
                    case EntityQueryJobMode.Async:
                        components = query.ToComponentDataArrayAsync<EcsTestData>(Allocator.TempJob, out JobHandle jobHandle);
                        jobHandle.Complete();
                        break;
                    case EntityQueryJobMode.AsyncComplete:
                        components = ChunkIterationUtility.CreateComponentDataArrayAsyncComplete(Allocator.TempJob,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(),
                            query,query.GetDependency());
                        break;
                    default: //EntityQueryJobMode.Immediate
                        components = ChunkIterationUtility.CreateComponentDataArray(Allocator.TempJob,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(), query);
                        break;
                }
                CollectionAssert.AreEqual(new[] {ecsData20,ecsData40,ecsData60}, components);
                components.Dispose();
            }

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData2)))
            {
                NativeArray<EcsTestData2> components2;
                switch (jobMode)
                {
                    case EntityQueryJobMode.Async:
                        components2 = query.ToComponentDataArrayAsync<EcsTestData2>(Allocator.TempJob, out JobHandle jobHandle);
                        jobHandle.Complete();
                        break;
                    case EntityQueryJobMode.AsyncComplete:
                        components2 = ChunkIterationUtility.CreateComponentDataArrayAsyncComplete(Allocator.TempJob,
                            GetComponentTypeHandle<EcsTestData2>(query),query.CalculateEntityCount(),
                            query,query.GetDependency());
                        break;
                    default: //EntityQueryJobMode.Immediate
                        components2 = ChunkIterationUtility.CreateComponentDataArray(Allocator.TempJob,
                            GetComponentTypeHandle<EcsTestData2>(query),query.CalculateEntityCount(), query);
                        break;
                }
                CollectionAssert.AreEqual(new[] {ecsData2_20_40, ecsData2_60_80}, components2);
                components2.Dispose();
            }

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData3)))
            {
                NativeArray<EcsTestData3> components3;
                switch (jobMode)
                {
                    case EntityQueryJobMode.Async:
                        components3 = query.ToComponentDataArrayAsync<EcsTestData3>(Allocator.TempJob, out JobHandle jobHandle);
                        jobHandle.Complete();
                        break;
                    case EntityQueryJobMode.AsyncComplete:
                        components3 = ChunkIterationUtility.CreateComponentDataArrayAsyncComplete(Allocator.TempJob,
                            GetComponentTypeHandle<EcsTestData3>(query),query.CalculateEntityCount(),
                            query,query.GetDependency());
                        break;
                    default: //EntityQueryJobMode.Immediate
                        components3 = ChunkIterationUtility.CreateComponentDataArray(Allocator.TempJob,
                            GetComponentTypeHandle<EcsTestData3>(query),query.CalculateEntityCount(), query);
                        break;
                }
                CollectionAssert.AreEqual(new[] {ecsData3_20_40_60, ecsData3_80_100_120}, components3);

                components3.Dispose();

            }
        }

        [Test]
        public void CalculateEntityCount()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var entityA = m_Manager.CreateEntity(archetype);
            var entityB = m_Manager.CreateEntity(archetype);

            m_Manager.SetSharedComponentData(entityA, new EcsTestSharedComp {value = 10});

            var query = EmptySystem.GetEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp));

            var entityCountBeforeFilter = query.CalculateChunkCount();

            query.SetSharedComponentFilter(new EcsTestSharedComp {value = 10});
            var entityCountAfterSetFilter = query.CalculateChunkCount();

            var entityCountUnfilteredAfterSetFilter = query.CalculateChunkCountWithoutFiltering();

            Assert.AreEqual(2, entityCountBeforeFilter);
            Assert.AreEqual(1, entityCountAfterSetFilter);
            Assert.AreEqual(2, entityCountUnfilteredAfterSetFilter);
        }

        [Test]
        public void CalculateChunkCount()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var entityA = m_Manager.CreateEntity(archetype);
            var entityB = m_Manager.CreateEntity(archetype);

            m_Manager.SetSharedComponentData(entityA, new EcsTestSharedComp {value = 10});

            var query = EmptySystem.GetEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp));

            var chunkCountBeforeFilter = query.CalculateChunkCount();

            query.SetSharedComponentFilter(new EcsTestSharedComp {value = 10});
            var chunkCountAfterSetFilter = query.CalculateChunkCount();

            var chunkCountUnfilteredAfterSetFilter = query.CalculateChunkCountWithoutFiltering();

            Assert.AreEqual(2, chunkCountBeforeFilter);
            Assert.AreEqual(1, chunkCountAfterSetFilter);
            Assert.AreEqual(2, chunkCountUnfilteredAfterSetFilter);
        }

        [Test]
        public void IsEmpty()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity, Allocator.Temp);
            for (int i = 0; i < entities.Length; i += 2)
            {
                m_Manager.SetSharedComponentData(entities[i], new EcsTestSharedComp {value = 10});
            }

            var query = EmptySystem.GetEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp));

            Assert.IsFalse(query.IsEmpty);
            Assert.IsFalse(query.IsEmptyIgnoreFilter);

            query.SetSharedComponentFilter(new EcsTestSharedComp {value = 10});
            Assert.IsFalse(query.IsEmpty);
            Assert.IsFalse(query.IsEmptyIgnoreFilter);

            query.SetSharedComponentFilter(new EcsTestSharedComp {value = 50});
            Assert.IsTrue(query.IsEmpty);
            Assert.IsFalse(query.IsEmptyIgnoreFilter);
        }

        private struct TestTag0 : IComponentData {}
        private struct TestTag1 : IComponentData {}
        private struct TestTag2 : IComponentData {}
        private struct TestTag3 : IComponentData {}
        private struct TestTag4 : IComponentData {}
        private struct TestTag5 : IComponentData {}
        private struct TestTag6 : IComponentData {}
        private struct TestTag7 : IComponentData {}
        private struct TestTag8 : IComponentData {}
        private struct TestTag9 : IComponentData {}
        private struct TestTag10 : IComponentData {}
        private struct TestTag11 : IComponentData {}
        private struct TestTag12 : IComponentData {}
        private struct TestTag13 : IComponentData {}
        private struct TestTag14 : IComponentData {}
        private struct TestTag15 : IComponentData {}
        private struct TestTag16 : IComponentData {}
        private struct TestTag17 : IComponentData {}

        private struct TestDefaultData : IComponentData
        {
            private int value;
        }

        private void MakeExtraQueries(int size)
        {
            var TagTypes = new Type[]
            {
                typeof(TestTag0),
                typeof(TestTag1),
                typeof(TestTag2),
                typeof(TestTag3),
                typeof(TestTag4),
                typeof(TestTag5),
                typeof(TestTag6),
                typeof(TestTag7),
                typeof(TestTag8),
                typeof(TestTag9),
                typeof(TestTag10),
                typeof(TestTag11),
                typeof(TestTag12),
                typeof(TestTag13),
                typeof(TestTag14),
                typeof(TestTag15),
                typeof(TestTag16),
                typeof(TestTag17)
            };

            for (int i = 0; i < size; i++)
            {
                var typeCount = CollectionHelper.Log2Ceil(i);
                var typeList = new List<ComponentType>();
                for (int typeIndex = 0; typeIndex < typeCount; typeIndex++)
                {
                    if ((i & (1 << typeIndex)) != 0)
                        typeList.Add(TagTypes[typeIndex]);
                }

                typeList.Add(typeof(TestDefaultData));

                var types = typeList.ToArray();
                var archetype = m_Manager.CreateArchetype(types);

                m_Manager.CreateEntity(archetype);
                var query = EmptySystem.GetEntityQuery(types);
                m_Manager.GetEntityQueryMask(query);
            }
        }

#if !UNITY_PORTABLE_TEST_RUNNER
        // https://unity3d.atlassian.net/browse/DOTSR-1432
        // TODO: IL2CPP_TEST_RUNNER can't handle Assert.That combined with Throws

        [Test]
        public void GetEntityQueryMaskThrowsOnOverflow()
        {
            Assert.That(() => MakeExtraQueries(1200),
                Throws.Exception.With.Message.Matches("You have reached the limit of 1024 unique EntityQueryMasks, and cannot generate any more."));
        }

#endif

        [Test]
        public unsafe void GetEntityQueryMaskReturnsCachedMask()
        {
            var queryMatches = EmptySystem.GetEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));
            var queryMaskMatches = m_Manager.GetEntityQueryMask(queryMatches);

            var queryMaskMatches2 = m_Manager.GetEntityQueryMask(queryMatches);

            Assert.True(queryMaskMatches.Mask == queryMaskMatches2.Mask &&
                queryMaskMatches.Index == queryMaskMatches2.Index &&
                queryMaskMatches.EntityComponentStore == queryMaskMatches2.EntityComponentStore);
        }

        [Test]
        public void GetEntityQueryMaskIgnoresFilter()
        {
            var queryMatches = EmptySystem.GetEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));
            queryMatches.SetSharedComponentFilter(new EcsTestSharedComp(42));

            var matching = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));
            var different = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));

            Assert.IsTrue(m_Manager.GetEntityQueryMask(queryMatches).Matches(matching));
            Assert.IsFalse(m_Manager.GetEntityQueryMask(queryMatches).Matches(different));
        }


        [Test]
        public void Matches()
        {
            var archetypeMatches = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));
            var archetypeDoesntMatch = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData3), typeof(EcsTestSharedComp));

            var entity = m_Manager.CreateEntity(archetypeMatches);
            var entityOnlyNeededToPopulateArchetype = m_Manager.CreateEntity(archetypeDoesntMatch);

            var queryMatches = EmptySystem.GetEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));
            var queryDoesntMatch = EmptySystem.GetEntityQuery(typeof(EcsTestData), typeof(EcsTestData3), typeof(EcsTestSharedComp));

            var queryMaskMatches = m_Manager.GetEntityQueryMask(queryMatches);

            var queryMaskDoesntMatch = m_Manager.GetEntityQueryMask(queryDoesntMatch);

            Assert.True(queryMaskMatches.Matches(entity));
            Assert.False(queryMaskDoesntMatch.Matches(entity));
        }

        [Test]
        public void MatchesEntity()
        {
            var archetypeMatches = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));
            var archetypeDoesntMatch = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData3), typeof(EcsTestSharedComp));

            var entity = m_Manager.CreateEntity(archetypeMatches);
            var entityDoesntMatch = m_Manager.CreateEntity(archetypeDoesntMatch);

            var query = EmptySystem.GetEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));

            Assert.True(query.Matches(entity));
            Assert.False(query.Matches(entityDoesntMatch));
        }

        [Test]
        public void MatchesEntityFiltered()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));

            var entity = m_Manager.CreateEntity(archetype);
            m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp{value = 10});

            var query = EmptySystem.GetEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));

            Assert.True(query.Matches(entity));

            query.SetSharedComponentFilter(new EcsTestSharedComp(5));
            Assert.False(query.Matches(entity));

            query.SetSharedComponentFilter(new EcsTestSharedComp(10));
            Assert.True(query.Matches(entity));
        }

        [Test]
        public void MatchesArchetypeAddedAfterMaskCreation()
        {
            var archetypeBefore = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var query = EmptySystem.GetEntityQuery(typeof(EcsTestData));
            var queryMask = m_Manager.GetEntityQueryMask(query);

            var archetypeAfter = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData3));
            var entity = m_Manager.CreateEntity(archetypeAfter);

            Assert.True(queryMask.Matches(entity));
        }

        [Test]
        public void ChunkListCaching_CheckCacheConsistency_FailsForInvalidCache()
        {
            // new archetype, no query
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));

            // new query, exisiting archetype, no entities
            var queryA = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            // new chunk of matching archetype
            m_Manager.CreateEntity(archetypeA, archetypeA.ChunkCapacity);

            Assert.IsFalse(queryA.IsCacheValid);
            Assert.IsFalse(queryA.CheckChunkListCacheConsistency());
        }

        [Test]
        public void ChunkListCaching_CreateEntity()
        {
            // new archetype, no query
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));

            // new query, exisiting archetype, no entities
            var queryA = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            // new chunk of matching archetype
            var entitiesA = m_Manager.CreateEntity(archetypeA, archetypeA.ChunkCapacity, Allocator.Temp);

            Assert.IsFalse(queryA.IsCacheValid);
        }

        [Test]
        public void ChunkListCaching_CreateEntity_NoNewChunk()
        {
            // new archetype, no query
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));

            // new query, exisiting archetype, no entities
            var queryA = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            // new chunk of matching archetype
            var entityA = m_Manager.CreateEntity(archetypeA);

            // Update the cache
            queryA.UpdateCache();
            Assert.IsTrue(queryA.IsCacheValid);
            Assert.IsTrue(queryA.CheckChunkListCacheConsistency());

            // no new chunk of matching archetype
            var entityB = m_Manager.CreateEntity(archetypeA);

            // cache should still be valid
            Assert.IsTrue(queryA.IsCacheValid);
            Assert.IsTrue(queryA.CheckChunkListCacheConsistency());
        }

        [Test]
        public void ChunkListCaching_DestroyEntity()
        {
            // new archetype, no query
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));

            // new query, exisiting archetype, no entities
            var queryA = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            // new chunk of matching archetype
            using(var entitiesA = m_Manager.CreateEntity(archetypeA, archetypeA.ChunkCapacity, Allocator.TempJob))
            using(var entitiesB = m_Manager.CreateEntity(archetypeA, archetypeA.ChunkCapacity, Allocator.TempJob))
            {
                queryA.UpdateCache();
                Assert.IsTrue(queryA.IsCacheValid);
                Assert.IsTrue(queryA.CheckChunkListCacheConsistency());

                // destroy entities
                m_Manager.DestroyEntity(entitiesA);
                Assert.IsFalse(queryA.IsCacheValid);
            }
        }

        [Test]
        public void ChunkListCaching_AddComponent()
        {
            // new archetype, no query
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));

            // new query, exisiting archetype, no entities
            var queryA = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            // new chunk of matching archetype
            var entities = m_Manager.CreateEntity(archetypeA, archetypeA.ChunkCapacity, Allocator.Temp);

            queryA.UpdateCache();
            Assert.IsTrue(queryA.IsCacheValid);
            Assert.IsTrue(queryA.CheckChunkListCacheConsistency());

            for (int i = 0; i < entities.Length; ++i)
            {
                m_Manager.AddComponent(entities[i], ComponentType.ReadWrite<EcsTestData2>());
            }

            Assert.IsFalse(queryA.IsCacheValid);
        }

        [Test]
        public void ChunkListCaching_RemoveComponent()
        {
            // new archetype, no query
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData), ComponentType.ReadWrite<EcsTestData2>());

            var queryA = m_Manager.CreateEntityQuery( new EntityQueryDesc
            {
                All = new[]{ComponentType.ReadWrite<EcsTestData>()},
                None = new[]{ComponentType.ReadWrite<EcsTestData2>()}
            });

            var entities = m_Manager.CreateEntity(archetypeA, archetypeA.ChunkCapacity, Allocator.Temp);

            queryA.UpdateCache();
            Assert.IsTrue(queryA.IsCacheValid);
            Assert.IsTrue(queryA.CheckChunkListCacheConsistency());

            for (int i = 0; i < entities.Length; ++i)
            {
                m_Manager.RemoveComponent(entities[i], ComponentType.ReadWrite<EcsTestData2>());
            }

            Assert.IsFalse(queryA.IsCacheValid);
        }

        [Test]
        public void ChunkListCaching_MoveEntitiesFrom()
        {
            var queryC = m_Manager.CreateEntityQuery(typeof(EcsTestData3));
            Assert.IsFalse(queryC.IsCacheValid);
            queryC.UpdateCache();
            Assert.IsTrue(queryC.IsCacheValid);
            Assert.IsTrue(queryC.CheckChunkListCacheConsistency());

            // move from another world
            using(var newWorld = new World("testworld"))
            {
                var archetype = newWorld.EntityManager.CreateArchetype(typeof(EcsTestData3));
                newWorld.EntityManager.CreateEntity(archetype);
                var query = newWorld.EntityManager.CreateEntityQuery(typeof(EcsTestData3));

                newWorld.EntityManager.UniversalQuery.UpdateCache();
                Assert.IsTrue(newWorld.EntityManager.UniversalQuery.IsCacheValid);
                Assert.IsTrue(newWorld.EntityManager.UniversalQuery.CheckChunkListCacheConsistency());

                m_Manager.MoveEntitiesFrom(newWorld.EntityManager);

                Assert.IsFalse(newWorld.EntityManager.UniversalQuery.IsCacheValid);
            }

            Assert.IsFalse(queryC.IsCacheValid);
        }

        [Test]
        public void ChunkListCaching_CopyAndReplaceEntitiesFrom()
        {
            var queryC = m_Manager.CreateEntityQuery(typeof(EcsTestData3));
            queryC.UpdateCache();
            Assert.IsTrue(queryC.IsCacheValid);
            Assert.IsTrue(queryC.CheckChunkListCacheConsistency());

            // move from another world
            using(var newWorld = new World("testworld"))
            {
                var archetype = newWorld.EntityManager.CreateArchetype(typeof(EcsTestData3));
                newWorld.EntityManager.CreateEntity(archetype);

                newWorld.EntityManager.UniversalQuery.UpdateCache();
                Assert.IsTrue(newWorld.EntityManager.UniversalQuery.IsCacheValid);
                Assert.IsTrue(newWorld.EntityManager.UniversalQuery.CheckChunkListCacheConsistency());

                m_Manager.CopyAndReplaceEntitiesFrom(newWorld.EntityManager);

                Assert.IsTrue(newWorld.EntityManager.UniversalQuery.IsCacheValid);
                Assert.IsTrue(newWorld.EntityManager.UniversalQuery.CheckChunkListCacheConsistency());
            }

            Assert.IsFalse(queryC.IsCacheValid);
        }

        [Test]
        public void ChunkListCaching_Instantiate()
        {
            // new archetype, no query
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));

            // new query, exisiting archetype, no entities
            var queryA = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            // new chunk of matching archetype
            var entityA = m_Manager.CreateEntity(archetypeA);

            queryA.UpdateCache();
            Assert.IsTrue(queryA.IsCacheValid);
            Assert.IsTrue(queryA.CheckChunkListCacheConsistency());

            m_Manager.Instantiate(entityA, archetypeA.ChunkCapacity, Allocator.Temp);

            Assert.IsFalse(queryA.IsCacheValid);
        }

        [Test]
        public void ChunkListCaching_AddSharedComponent()
        {
            // new archetype, no query
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));

            // new query, exisiting archetype, no entities
            var queryA = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            // new chunk of matching archetype
            var entities = m_Manager.CreateEntity(archetypeA, archetypeA.ChunkCapacity, Allocator.Temp);

            queryA.UpdateCache();
            Assert.IsTrue(queryA.IsCacheValid);
            Assert.IsTrue(queryA.CheckChunkListCacheConsistency());

            for (int i = 0; i < entities.Length; ++i)
            {
                m_Manager.AddSharedComponentData(entities[i], new EcsTestSharedComp{value = 10});
            }

            Assert.IsFalse(queryA.IsCacheValid);
        }

        [Test]
        public void ChunkListCaching_SetSharedComponent()
        {
            // new archetype, no query
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));

            // new query, exisiting archetype, no entities
            var queryA = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            // new chunk of matching archetype
            var entities = m_Manager.CreateEntity(archetypeA, archetypeA.ChunkCapacity, Allocator.Temp);

            queryA.UpdateCache();
            Assert.IsTrue(queryA.IsCacheValid);
            Assert.IsTrue(queryA.CheckChunkListCacheConsistency());

            for (int i = 0; i < entities.Length; ++i)
            {
                m_Manager.SetSharedComponentData(entities[i], new EcsTestSharedComp{value = 10});
            }

            Assert.IsFalse(queryA.IsCacheValid);
        }

        [Test]
        public void ChunkListCaching_DestroyEntity_Query()
        {
            // new archetype, no query
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));

            // new query, exisiting archetype, no entities
            var queryA = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            // new chunk of matching archetype
            using(var entitiesA = m_Manager.CreateEntity(archetypeA, archetypeA.ChunkCapacity, Allocator.TempJob))
            using(var entitiesB = m_Manager.CreateEntity(archetypeA, archetypeA.ChunkCapacity, Allocator.TempJob))
            {
                queryA.UpdateCache();
                Assert.IsTrue(queryA.IsCacheValid);
                Assert.IsTrue(queryA.CheckChunkListCacheConsistency());

                // destroy entities
                m_Manager.DestroyEntity(queryA);
                Assert.IsFalse(queryA.IsCacheValid);
            }
        }

        [Test]
        public void CalculateEntityCountWithEntityList()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var allEntitiesA = m_Manager.CreateEntity(archetypeA, 10, Allocator.TempJob))
            using (var allEntitiesB = m_Manager.CreateEntity(archetypeB, 10, Allocator.TempJob))
            {
                var entities = new NativeArray<Entity>(10, Allocator.TempJob);
                for (int i = 0; i < 10; ++i)
                {
                    if (i % 2 == 0)
                        entities[i] = allEntitiesA[i];
                    else
                        entities[i] = allEntitiesB[i];
                }

                Assert.AreEqual(5, query.CalculateEntityCount(entities));

                entities.Dispose();
            }
        }

        [Test]
        public void ToEntityArrayWithEntityList()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var allEntitiesA = m_Manager.CreateEntity(archetypeA, 10, Allocator.TempJob))
            using (var allEntitiesB = m_Manager.CreateEntity(archetypeB, 10, Allocator.TempJob))
            {
                var entities = new NativeArray<Entity>(10, Allocator.TempJob);
                for (int i = 0; i < 10; ++i)
                {
                    if (i % 2 == 0)
                        entities[i] = allEntitiesA[i];
                    else
                        entities[i] = allEntitiesB[i];
                }

                var res = query.ToEntityArray(entities, Allocator.TempJob);
                Assert.AreEqual(5, res.Length);
                for (int i = 0; i < res.Length; ++i)
                {
                    Assert.AreEqual(allEntitiesA[i * 2], res[i]);
                }

                entities.Dispose();
                res.Dispose();
            }
        }

        [Test]
        public void ToComponentDataArrayWithEntityList()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var allEntitiesA = m_Manager.CreateEntity(archetypeA, 10, Allocator.TempJob))
            using (var allEntitiesB = m_Manager.CreateEntity(archetypeB, 10, Allocator.TempJob))
            {
                var entities = new NativeArray<Entity>(10, Allocator.TempJob);
                for (int i = 0; i < 10; ++i)
                {
                    if (i % 2 == 0)
                    {
                        entities[i] = allEntitiesA[i];
                        m_Manager.SetComponentData(allEntitiesA[i], new EcsTestData(i));
                    }
                    else
                        entities[i] = allEntitiesB[i];
                }

                var res = query.ToComponentDataArray<EcsTestData>(entities, Allocator.TempJob);
                Assert.AreEqual(5, res.Length);
                for (int i = 0; i < res.Length; ++i)
                {
                    Assert.AreEqual(m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i * 2]).value, res[i].value);
                }

                entities.Dispose();
                res.Dispose();
            }
        }

        [Test]
        public void MatchesInList()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var allEntitiesA = m_Manager.CreateEntity(archetypeA, 10, Allocator.TempJob))
            using (var allEntitiesB = m_Manager.CreateEntity(archetypeB, 10, Allocator.TempJob))
            {
                var entities = new NativeArray<Entity>(10, Allocator.TempJob);
                for (int i = 0; i < 10; ++i)
                {
                    entities[i] = allEntitiesA[i];
                }

                Assert.IsTrue(query.MatchesAny(entities));

                for (int i = 0; i < 10; ++i)
                {
                    entities[i] = allEntitiesB[i];
                }

                Assert.IsFalse(query.MatchesAny(entities));

                entities.Dispose();
            }
        }

        unsafe struct ReadOnlyArrayContainer : IDisposable
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            internal AtomicSafetyHandle m_Safety;
            static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<ReadOnlyArrayContainer>();

            [BurstDiscard]
            static void CreateStaticSafetyId()
            {
                s_staticSafetyId.Data = AtomicSafetyHandle.NewStaticSafetyId<ReadOnlyArrayContainer>();
            }

            [NativeSetClassTypeToNullOnSchedule]
            DisposeSentinel m_DisposeSentinel;
#endif

            [NativeDisableUnsafePtrRestriction]
            private UnsafeList* m_Data;

            private readonly Allocator m_Allocator;

            public ReadOnlyArrayContainer(int initialCapacity, Allocator allocator)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 2, allocator);
                if (s_staticSafetyId.Data == 0)
                {
                    CreateStaticSafetyId();
                }
                AtomicSafetyHandle.SetStaticSafetyId(ref m_Safety, s_staticSafetyId.Data);
#endif

                m_Data = UnsafeList.Create(UnsafeUtility.SizeOf<Entity>(), UnsafeUtility.AlignOf<Entity>(), initialCapacity, allocator);
                m_Allocator = allocator;
            }

            public void AddRange(NativeArray<Entity> entities)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
                m_Data->AddRange<Entity>(entities.GetUnsafeReadOnlyPtr(), entities.Length);
            }

            public NativeArray<Entity> GetValueArray()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckGetSecondaryDataPointerAndThrow(m_Safety);
                var arraySafety = m_Safety;
                AtomicSafetyHandle.UseSecondaryVersion(ref arraySafety);
#endif

                var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(m_Data->Ptr, m_Data->Length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.SetAllowSecondaryVersionWriting(arraySafety, false);
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, arraySafety);
#endif
                return array;
            }

            public void Dispose()
            {
                if (m_Data != null)
                {
                    UnsafeList.Destroy(m_Data);
                    m_Data = null;
                }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
            }
        }

        [Test]
        public void EntityQuery_ToEntityArray_With_ReadOnlyFilter_DoesNotThrow()
        {
            using (var customContainer = new ReadOnlyArrayContainer(10, Allocator.TempJob))
            using (var array = new NativeArray<Entity>(10, Allocator.TempJob))
            {
                var archetype = m_Manager.CreateArchetype(ComponentType.ReadWrite<EcsTestData>(),
                    ComponentType.ReadWrite<EcsTestData2>());

                m_Manager.CreateEntity(archetype, array);
                customContainer.AddRange(array);

                var query = EmptySystem.GetEntityQuery(typeof(EcsTestData));

                Assert.DoesNotThrow(() =>
                {
                    using (var _ = query.ToEntityArray(customContainer.GetValueArray(), Allocator.Temp))
                    { }
                });
            }
        }

        [Test]
        public void EntityQuery_ToComponentDataArray_With_ReadOnlyFilter_DoesNotThrow()
        {
            using (var customContainer = new ReadOnlyArrayContainer(10, Allocator.TempJob))
            using (var array = new NativeArray<Entity>(10, Allocator.TempJob))
            {
                var archetype = m_Manager.CreateArchetype(ComponentType.ReadWrite<EcsTestData>(),
                    ComponentType.ReadWrite<EcsTestData2>());

                m_Manager.CreateEntity(archetype, array);
                customContainer.AddRange(array);

                var query = EmptySystem.GetEntityQuery(typeof(EcsTestData));

                Assert.DoesNotThrow(() =>
                {
                    using (var _ = query.ToComponentDataArray<EcsTestData>(customContainer.GetValueArray(), Allocator.Temp))
                    { }
                });
            }
        }

        [Test]
        public void EntityQuery_CalculateEntityCount_With_ReadOnlyFilter_DoesNotThrow()
        {
            using (var customContainer = new ReadOnlyArrayContainer(10, Allocator.TempJob))
            using (var array = new NativeArray<Entity>(10, Allocator.TempJob))
            {
                var archetype = m_Manager.CreateArchetype(ComponentType.ReadWrite<EcsTestData>(),
                    ComponentType.ReadWrite<EcsTestData2>());

                m_Manager.CreateEntity(archetype, array);
                customContainer.AddRange(array);

                var query = EmptySystem.GetEntityQuery(typeof(EcsTestData));

                Assert.DoesNotThrow(() =>
                {
                    var _ = query.CalculateEntityCount(customContainer.GetValueArray());
                });
            }
        }

        [Test]
        public void EntityQuery_MatchesInList_With_ReadOnlyFilter_DoesNotThrow()
        {
            using (var customContainer = new ReadOnlyArrayContainer(10, Allocator.TempJob))
            using (var array = new NativeArray<Entity>(10, Allocator.TempJob))
            {
                var archetype = m_Manager.CreateArchetype(ComponentType.ReadWrite<EcsTestData>(),
                    ComponentType.ReadWrite<EcsTestData2>());

                m_Manager.CreateEntity(archetype, array);
                customContainer.AddRange(array);

                var query = EmptySystem.GetEntityQuery(typeof(EcsTestData));

                Assert.DoesNotThrow(() =>
                {
                    var _ = query.MatchesAny(customContainer.GetValueArray());
                });
            }
        }

#if !UNITY_DOTSRUNTIME // IJobForEach is deprecated
        [AlwaysUpdateSystem]
        public class CachedSystemQueryTestSystem : JobComponentSystem
        {
#pragma warning disable 618
            // Creates implicit query (All = {EcsTestData}, None = {}, Any = {}
            private struct ImplicitQueryCreator : IJobForEach<EcsTestData>
            {
                public void Execute(ref EcsTestData c0)
                {
                    c0.value = 10;
                }
            }
#pragma warning restore 618

            protected override void OnCreate()
            {
                // Caches a query in the system.
                // This occurs before the implicit query is created and will be first in the cached list.
                GetEntityQuery(new EntityQueryDesc
                {
                    All = new[] {ComponentType.ReadWrite<EcsTestData>()},
                    None = new[] {ComponentType.ReadOnly<EcsTestTag>()}
                });
            }

            protected override JobHandle OnUpdate(JobHandle input)
            {
                var job = new ImplicitQueryCreator() {};
                return job.Schedule(this, input);
            }
        }
        [Test]
        public void CachedSystemQueryReturnsOnlyExactQuery()
        {
            var entityA = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestTag));
            var entityB = m_Manager.CreateEntity(typeof(EcsTestData));

            var testSystem = World.GetOrCreateSystem<CachedSystemQueryTestSystem>();
            testSystem.Update();

            Assert.AreEqual(2, testSystem.EntityQueries.Length);
            Assert.AreEqual(10, m_Manager.GetComponentData<EcsTestData>(entityA).value);
            Assert.AreEqual(10, m_Manager.GetComponentData<EcsTestData>(entityB).value);

            var queryA = testSystem.GetEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadWrite<EcsTestData>()},
                None = new[] {ComponentType.ReadOnly<EcsTestTag>()}
            });

            var queryB = testSystem.GetEntityQuery(ComponentType.ReadWrite<EcsTestData>(), ComponentType.Exclude<EcsTestTag>());

            Assert.AreEqual(queryA, queryB);
        }

#endif // !UNITY_DOTSRUNTIME

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        private class ManagedComponent : IComponentData
        {
            public int Value;
        }

        [Test]
        public void Managed_ToComponentDataArray_Respects_Filter()
        {
            const int kShared1 = 5;
            const int kShared2 = 7;
            const int kShared3 = 11;

            for (int i = 0; i < kShared1; ++i)
            {
                var entity = m_Manager.CreateEntity(typeof(ManagedComponent), typeof(EcsTestSharedComp));
                m_Manager.SetComponentData(entity, new ManagedComponent() { Value = 1 });
                m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp() { value = 1 });
            }
            for (int i = 0; i < kShared2; ++i)
            {
                var entity = m_Manager.CreateEntity(typeof(ManagedComponent), typeof(EcsTestSharedComp));
                m_Manager.SetComponentData(entity, new ManagedComponent() { Value = 2 });
                m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp() { value = 2 });
            }
            for (int i = 0; i < kShared3; ++i)
            {
                var entity = m_Manager.CreateEntity(typeof(ManagedComponent), typeof(EcsTestSharedComp));
                m_Manager.SetComponentData(entity, new ManagedComponent() { Value = 3 });
                m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp() { value = 3 });
            }

            var allSharedComponents = new List<EcsTestSharedComp>();
            m_Manager.GetAllUniqueSharedComponentData(allSharedComponents);

            var query = m_Manager.CreateEntityQuery(typeof(ManagedComponent), typeof(EcsTestSharedComp));
            foreach (var shared in allSharedComponents)
            {
                query.SetSharedComponentFilter(shared);
                var comps = query.ToComponentDataArray<ManagedComponent>();

                if (shared.value == 1)
                    Assert.AreEqual(kShared1, comps.Length);
                else if (shared.value == 2)
                    Assert.AreEqual(kShared2, comps.Length);
                else if (shared.value == 3)
                    Assert.AreEqual(kShared3, comps.Length);

                foreach (var comp in comps)
                    Assert.AreEqual(shared.value, comp.Value);
            }
            query.ResetFilter();
        }

#endif

        [Test]
        public void QueryFromWrongWorldThrows()
        {
            using (var world = new World("temp"))
            {
                Assert.Throws<InvalidOperationException>(() => m_Manager.AddComponent(world.EntityManager.UniversalQuery, typeof(EcsTestData)));
                Assert.Throws<InvalidOperationException>(() => m_Manager.AddSharedComponentData(world.EntityManager.UniversalQuery, new EcsTestSharedComp()));
                Assert.Throws<InvalidOperationException>(() => m_Manager.DestroyEntity(world.EntityManager.UniversalQuery));
                Assert.Throws<InvalidOperationException>(() => m_Manager.RemoveComponent<EcsTestData>(world.EntityManager.UniversalQuery));

                // TODO: Leaks string allocs in Burst; Can be re-enabled when DOTS-1963 is closed
                /*
                using (var cmd = new EntityCommandBuffer(Allocator.TempJob))
                {
                    cmd.AddComponent(world.EntityManager.UniversalQuery, typeof(EcsTestData));
                    Assert.Throws<InvalidOperationException>(() => cmd.Playback(m_Manager));
                }
                */
            }
        }

        [Test]
        public void ToEntityArrayTempMemoryThrows()
        {
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            Assert.Throws<ArgumentException>(() =>
            {
                query.ToEntityArrayAsync(Allocator.Temp, out JobHandle jobhandle);
            });

            query.Dispose();
        }

        [Test]
        public void ToEntityArrayTempMemoryDoesNotThrow()
        {
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            Assert.DoesNotThrow(() =>
            {
                query.ToEntityArray(Allocator.Temp).Dispose();
            });

            query.Dispose();

            //create very large Query to test AsyncComplete path
            for (int i = 0; i < 100000; ++i)
            {
                m_Manager.CreateEntity(typeof(EcsTestData));
            }

            query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            Assert.DoesNotThrow(() =>
            {
                query.ToEntityArray(Allocator.Temp).Dispose();
            });

            query.Dispose();

        }

        [Test]
        public void ToComponentDataArrayTempMemoryThrows()
        {
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            Assert.Throws<ArgumentException>(() =>
            {
                query.ToComponentDataArrayAsync<EcsTestData>(Allocator.Temp, out JobHandle jobhandle);
            });

            query.Dispose();

        }

        [Test]
        public void ToComponentDataArrayTempMemoryDoesNotThrow()
        {
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));


            Assert.DoesNotThrow(() =>
            {
                query.ToComponentDataArray<EcsTestData>(Allocator.Temp).Dispose();
            });

            query.Dispose();

            //create very large Query to test AsyncComplete path
            for (int i = 0; i < 100000; ++i)
            {
                m_Manager.CreateEntity(typeof(EcsTestData));
            }

            query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            Assert.DoesNotThrow(() =>
            {
                query.ToComponentDataArray<EcsTestData>(Allocator.Temp).Dispose();
            });

            query.Dispose();

        }

        [Test]
        public void CopyFromComponentDataArrayTempMemoryThrows()
        {
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            var components = query.ToComponentDataArray<EcsTestData>(Allocator.Temp);

            Assert.DoesNotThrow(() =>
            {
                query.CopyFromComponentDataArray(components);
            });
            Assert.Throws<ArgumentException>(() =>
            {
                query.CopyFromComponentDataArrayAsync(components, out JobHandle jobhandle);
            });

            query.Dispose();
            components.Dispose();

            //create very large Query to test AsyncComplete path
            for (int i = 0; i < 100000; ++i)
            {
                m_Manager.CreateEntity(typeof(EcsTestData));
            }

            query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            components = query.ToComponentDataArray<EcsTestData>(Allocator.Temp);


            Assert.DoesNotThrow(() =>
            {
                query.CopyFromComponentDataArray(components);
            });

            query.Dispose();
            components.Dispose();

        }

        [Test]
        public void ToComponentDataArrayWithUnrelatedQueryThrows()
        {
            var query = EmptySystem.GetEntityQuery(typeof(EcsTestData));

            JobHandle jobHandle;
            Assert.Throws<InvalidOperationException>(() =>
            {
                query.ToComponentDataArrayAsync<EcsTestData2>(Allocator.Persistent, out jobHandle);
            });
            Assert.Throws<InvalidOperationException>(() =>
            {
                query.ToComponentDataArray<EcsTestData2>(Allocator.Persistent);
            });
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            Assert.Throws<InvalidOperationException>(() =>
            {
                query.ToComponentDataArray<EcsTestManagedComponent>();
            });
#endif
        }

        [Test]
        public unsafe void CopyFromComponentDataArray_Works([Values] EntityQueryJobMode jobMode)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));

            var values = new NativeArray<EcsTestData>(archetype.ChunkCapacity * 2, Allocator.TempJob);
            for (int i = 0; i < archetype.ChunkCapacity * 2; ++i)
            {
                values[i] = new EcsTestData{value = i};
            }

            m_Manager.CreateEntity(archetype, archetype.ChunkCapacity * 2, Allocator.Temp);
            var query = EmptySystem.GetEntityQuery(typeof(EcsTestData));
            switch (jobMode)
            {
                case EntityQueryJobMode.Async:
                    query.CopyFromComponentDataArrayAsync(values, out JobHandle jobHandle);
                    jobHandle.Complete();
                    break;
                case EntityQueryJobMode.AsyncComplete:
                    ChunkIterationUtility.CopyFromComponentDataArrayAsyncComplete(GetMatchingArchetypes(query),
                        values, GetComponentTypeHandle<EcsTestData>(query),query,ref query._GetImpl()->_Filter,
                        query.GetDependency());
                    break;
                default: //EntityQueryJobMode.Immediate
                    ChunkIterationUtility.CopyFromComponentDataArray(values,
                        GetComponentTypeHandle<EcsTestData>(query), query);
                    break;
            }

            var dataArray = query.ToComponentDataArray<EcsTestData>(Allocator.TempJob);
            CollectionAssert.AreEquivalent(values, dataArray);

            dataArray.Dispose();
            values.Dispose();
        }

        [Test]
        public void CopyFromComponentDataArrayWithUnrelatedQueryThrows()
        {
            var query = EmptySystem.GetEntityQuery(typeof(EcsTestData));

            JobHandle jobHandle;
            Assert.Throws<InvalidOperationException>(() =>
            {
                using (var array = new NativeArray<EcsTestData2>(0, Allocator.Persistent))
                {
                    query.CopyFromComponentDataArray(array);
                }
            });
            Assert.Throws<InvalidOperationException>(() =>
            {
                using (var array = new NativeArray<EcsTestData2>(0, Allocator.Persistent))
                {
                    query.CopyFromComponentDataArrayAsync(array, out jobHandle);
                }
            });
        }

        [Test]
        public void UseDisposedQueryThrows()
        {
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            query.Dispose();
#if UNITY_2020_2_OR_NEWER
            Assert.Throws<ObjectDisposedException>(
#else
            Assert.Throws<InvalidOperationException>(
#endif
                () => m_Manager.AddComponent(query, typeof(EcsTestData2)));
        }

        [Test]
        public void ArchetypesCreatedInExclusiveEntityTransaction()
        {
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            var transaction = m_Manager.BeginExclusiveEntityTransaction();
            transaction.CreateEntity(typeof(EcsTestData));
            m_Manager.EndExclusiveEntityTransaction();

            Assert.AreEqual(1, query.CalculateEntityCount());
        }

        [Test]
        public unsafe void QueryDescAndEntityQueryHaveEqualAccessPermissions()
        {
            var queryA = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsTestData>(), ComponentType.ReadWrite<EcsTestData2>());
            var queryB = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<EcsTestData>(), ComponentType.ReadWrite<EcsTestData2>()}
            });

            var queryDataA = queryA._GetImpl()->_QueryData;
            var queryDataB = queryB._GetImpl()->_QueryData;
            Assert.AreEqual(queryDataA->RequiredComponentsCount, queryDataB->RequiredComponentsCount);
            Assert.IsTrue(UnsafeUtility.MemCmp(queryDataA->RequiredComponents, queryDataB->RequiredComponents, sizeof(ComponentType) * queryDataA->RequiredComponentsCount) == 0);

            queryA.Dispose();
            queryB.Dispose();
        }

        [Test]
        public unsafe void CachingWorks()
        {
            var q1 = EmptySystem.GetEntityQuery(typeof(EcsTestData));
            var q2 = EmptySystem.GetEntityQuery(typeof(EcsTestData2));
            var q3 = EmptySystem.GetEntityQuery(typeof(EcsTestData));

            Assert.AreEqual((IntPtr)q1.__impl, (IntPtr)q3.__impl);
            Assert.AreNotEqual((IntPtr)q2.__impl, (IntPtr)q3.__impl);
            Assert.AreEqual(EntityDataAccess.kBuiltinEntityQueryCount + 2, m_Manager.GetCheckedEntityDataAccess()->AliveEntityQueries.Count());
        }

        [Test]
        public unsafe void LivePointerTrackingWorks()
        {
            var q1 = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            var q2 = m_Manager.CreateEntityQuery(typeof(EcsTestData2));

            Assert.AreEqual(EntityDataAccess.kBuiltinEntityQueryCount + 2, m_Manager.GetCheckedEntityDataAccess()->AliveEntityQueries.Count());

            q1.Dispose();

            Assert.AreEqual(EntityDataAccess.kBuiltinEntityQueryCount + 1, m_Manager.GetCheckedEntityDataAccess()->AliveEntityQueries.Count());

            q2.Dispose();

            Assert.AreEqual(EntityDataAccess.kBuiltinEntityQueryCount, m_Manager.GetCheckedEntityDataAccess()->AliveEntityQueries.Count());
        }
    }
}
#endif // NET_DOTS
