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
    partial class EntityQueryTests : ECSTestsFixture
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
            var managedEntities = entities.ToArray();
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

            var queriedChunks1 = query1.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);
            var queriedChunks12 = query12.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);
            var queriedChunksAll = m_Manager.GetAllChunks(World.UpdateAllocator.ToAllocator);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks12), queriedChunks1);
            CollectionAssert.AreEquivalent(createdChunks12, queriedChunks12);
            CollectionAssert.AreEquivalent(allCreatedChunks, queriedChunksAll);
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

            var queriedChunks1 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetSharedComponentFilter(new EcsTestSharedComp(2));

            var queriedChunks2 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2), queriedChunks1);
            CollectionAssert.AreEquivalent(createdChunks3.Concat(createdChunks4), queriedChunks2);

            query.Dispose();
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
            var queriedChunks1 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetSharedComponentFilter(new EcsTestSharedComp(2), new EcsTestSharedComp2(7));
            var queriedChunks2 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetSharedComponentFilter(new EcsTestSharedComp(1), new EcsTestSharedComp2(8));
            var queriedChunks3 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetSharedComponentFilter(new EcsTestSharedComp(2), new EcsTestSharedComp2(8));
            var queriedChunks4 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);


            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2), queriedChunks1);
            CollectionAssert.AreEquivalent(createdChunks3.Concat(createdChunks4), queriedChunks2);
            CollectionAssert.AreEquivalent(createdChunks5.Concat(createdChunks6), queriedChunks3);
            CollectionAssert.AreEquivalent(createdChunks7.Concat(createdChunks8), queriedChunks4);

            query.Dispose();
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
            var queriedChunks1 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetChangedFilterRequiredVersion(20);
            var queriedChunks2 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetChangedFilterRequiredVersion(30);
            var queriedChunks3 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetChangedFilterRequiredVersion(40);
            var queriedChunks4 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2).Concat(createdChunks3), queriedChunks1);
            CollectionAssert.AreEquivalent(createdChunks2.Concat(createdChunks3), queriedChunks2);
            CollectionAssert.AreEquivalent(createdChunks3, queriedChunks3);

            Assert.AreEqual(0, queriedChunks4.Length);

            query.Dispose();
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

            var queriedChunks1 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            foreach (var chunk in createdChunks1)
            {
                var array = chunk.GetNativeArray(testType1);
                array[0] = new EcsTestData(7);
            }

            var queriedChunks2 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            foreach (var chunk in createdChunks2)
            {
                var array = chunk.GetNativeArray(testType2);
                array[0] = new EcsTestData2(7);
            }

            var queriedChunks3 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);


            CollectionAssert.AreEquivalent(createdChunks3, queriedChunks1);
            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks3), queriedChunks2);

            query.Dispose();
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
            var queriedChunks1 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetChangedFilterRequiredVersion(20);
            var queriedChunks2 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetChangedFilterRequiredVersion(30);
            var queriedChunks3 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetChangedFilterRequiredVersion(40);
            var queriedChunks4 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2).Concat(createdChunks3), queriedChunks1);
            CollectionAssert.AreEquivalent(createdChunks2.Concat(createdChunks3), queriedChunks2);
            CollectionAssert.AreEquivalent(createdChunks3, queriedChunks3);

            Assert.AreEqual(0, queriedChunks4.Length);

            query.Dispose();
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
            var queriedChunks1 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetChangedFilterRequiredVersion(20);
            var queriedChunks2 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetChangedFilterRequiredVersion(30);
            var queriedChunks3 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetChangedFilterRequiredVersion(40);
            var queriedChunks4 =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2).Concat(createdChunks3), queriedChunks1);
            CollectionAssert.AreEquivalent(createdChunks2.Concat(createdChunks3), queriedChunks2);
            CollectionAssert.AreEquivalent(createdChunks3, queriedChunks3);

            Assert.AreEqual(0, queriedChunks4.Length);

            query.Dispose();
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

            var queriedChunks1 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetChangedFilterRequiredVersion(10);
            var queriedChunks2 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            // bumps the version number for TestData1 for createdChunks1
            m_ManagerDebug.SetGlobalSystemVersion(20);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                var array = createdChunks1[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData>());
                array[0] = new EcsTestData {value = 10};
            }
            var queriedChunks3 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            // bumps the version number for TestData2
            query.SetChangedFilterRequiredVersion(20);
            m_ManagerDebug.SetGlobalSystemVersion(30);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                var array = createdChunks1[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData2>());
                array[0] = new EcsTestData2 {value1 = 10, value0 = 10};
            }
            var queriedChunks4 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2), queriedChunks1); // query 1 = created 1,2
            Assert.AreEqual(0, queriedChunks2.Length); // query 2 is empty
            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks3); // query 3 = created 1 (version # was bumped)
            Assert.AreEqual(0, queriedChunks4.Length); // query 4 is empty (version # of type we're not change tracking was bumped)

            query.Dispose();
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

            var queriedChunks1 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetChangedFilterRequiredVersion(10);
            var queriedChunks2 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            // bumps the order version number for createdChunks1
            m_ManagerDebug.SetGlobalSystemVersion(20);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                var array = createdChunks1[i].GetNativeArray(EmptySystem.GetEntityTypeHandle());
                m_Manager.AddComponent(array, typeof(EcsTestTag));
            }
            var queriedChunks3 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2), queriedChunks1); // query 1 = created 1,2
            Assert.AreEqual(0, queriedChunks2.Length); // query 2 is empty
            Assert.AreEqual(createdChunks1.Length, queriedChunks3.Length); // query 3 = created 1 (version # was bumped) (not collection equivalent because it is a new archetype so the chunk *has* changed)

            query.Dispose();
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

            var queriedChunks1 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetChangedFilterRequiredVersion(10);
            var queriedChunks2 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            // bumps the version number for TestData1 for createdChunks1
            m_ManagerDebug.SetGlobalSystemVersion(20);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                var array = createdChunks1[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData>());
                array[0] = new EcsTestData {value = 10};
            }
            var queriedChunks3 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            // bumps the version number for TestData2
            query.SetChangedFilterRequiredVersion(20);
            m_ManagerDebug.SetGlobalSystemVersion(30);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                var array = createdChunks1[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData2>());
                array[0] = new EcsTestData2 {value1 = 10, value0 = 10};
            }
            var queriedChunks4 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            CollectionAssert.AreEquivalent(createdChunks1.Concat(createdChunks2), queriedChunks1); // query 1 = created 1,2
            Assert.AreEqual(0, queriedChunks2.Length); // query 2 is empty
            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks3); // query 3 = created 1 (version # of type1 was bumped)
            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks4); // query 4 = created 1 (version # of type2 was bumped)

            query.Dispose();
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

            var queriedChunks1 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetChangedFilterRequiredVersion(10);
            var queriedChunks2 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

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
            var queriedChunks3 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            // bumps the version number for TestData2 for createdChunks1
            query.SetChangedFilterRequiredVersion(20);
            m_ManagerDebug.SetGlobalSystemVersion(30);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                var array = createdChunks1[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData2>());
                array[0] = new EcsTestData2 {value1 = 10, value0 = 10};
            }
            var queriedChunks4 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks1); // query 1 = created 1
            Assert.AreEqual(0, queriedChunks2.Length); // query 2 is empty
            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks3); // query 3 = created 1 (version # was bumped and we're filtering out created2)
            Assert.AreEqual(0, queriedChunks4.Length); // query 4 is empty (version # of type we're not change tracking was bumped)

            query.Dispose();
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

            var queriedChunks1 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            query.SetChangedFilterRequiredVersion(10);
            var queriedChunks2 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

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
            var queriedChunks3 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            // bumps the version number for TestData2 for createdChunks1
            query.SetChangedFilterRequiredVersion(20);
            m_ManagerDebug.SetGlobalSystemVersion(30);
            for (int i = 0; i < createdChunks1.Length; ++i)
            {
                var array = createdChunks1[i].GetNativeArray(EmptySystem.GetComponentTypeHandle<EcsTestData2>());
                array[0] = new EcsTestData2 {value1 = 10, value0 = 10};
            }
            var queriedChunks4 = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);

            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks1); // query 1 = created 1
            Assert.AreEqual(0, queriedChunks2.Length); // query 2 is empty
            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks3); // query 3 = created 1 (version # was bumped and we're filtering out created2)
            CollectionAssert.AreEquivalent(createdChunks1, queriedChunks4); // query 4 = created 1 (version # of type2 was bumped)

            query.Dispose();
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
                Assert.Throws<InvalidOperationException>(() => query.ToComponentDataArray<EcsTestData2>(World.UpdateAllocator.ToAllocator));
        }

        [AlwaysUpdateSystem]
        public partial class WriteEcsTestDataSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities.ForEach((ref EcsTestData data ) => {  }).Schedule();
            }
        }

        [Test]
        public unsafe void CreateArchetypeChunkArray_SyncsChangeFilterTypes()
        {
            m_Manager.CreateEntity(typeof(EcsTestData));

            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            query.SetChangedVersionFilter(typeof(EcsTestData));
            var ws1 = World.GetOrCreateSystem<WriteEcsTestDataSystem>();
            ws1.Update();
            var safetyHandle = m_Manager.GetCheckedEntityDataAccess()->DependencyManager->Safety.GetSafetyHandle(TypeManager.GetTypeIndex<EcsTestData>(), false);

            Assert.Throws<InvalidOperationException>(() => AtomicSafetyHandle.CheckWriteAndThrow(safetyHandle));
            var chunks =  query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);
            AtomicSafetyHandle.CheckWriteAndThrow(safetyHandle);

            query.Dispose();
        }

        [Test]
        public unsafe void CalculateEntityCount_SyncsChangeFilterTypes()
        {
            m_Manager.CreateEntity(typeof(EcsTestData));

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

        [Test]
        public unsafe void IsEmpty_SyncsChangeFilterTypes()
        {
            m_Manager.CreateEntity(typeof(EcsTestData));

            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            query.SetChangedVersionFilter(typeof(EcsTestData));
            var ws1 = World.GetOrCreateSystem<WriteEcsTestDataSystem>();
            ws1.Update();
            var safetyHandle = m_Manager.GetCheckedEntityDataAccess()->DependencyManager->Safety.GetSafetyHandle(TypeManager.GetTypeIndex<EcsTestData>(), false);

            Assert.Throws<InvalidOperationException>(() => AtomicSafetyHandle.CheckWriteAndThrow(safetyHandle));
            var dummy = query.IsEmpty;
            AtomicSafetyHandle.CheckWriteAndThrow(safetyHandle);

            query.Dispose();
        }

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
                        entities = query.ToEntityArrayAsync(World.UpdateAllocator.ToAllocator, out JobHandle jobHandle);
                        jobHandle.Complete();
                        break;
                    case EntityQueryJobMode.AsyncComplete:
                        entities = ChunkIterationUtility.CreateEntityArrayAsyncComplete(GetMatchingArchetypes(query),
                            World.UpdateAllocator.ToAllocator,
                            GetEntityTypeHandle(query), query, query.CalculateEntityCount(),query.GetDependency());
                        break;
                    default: //EntityQueryMethodType.Immediate
                        entities = ChunkIterationUtility.CreateEntityArray(GetMatchingArchetypes(query),
                            World.UpdateAllocator.ToAllocator,
                            GetEntityTypeHandle(query), query, query.CalculateEntityCount());
                        break;
                }


                CollectionAssert.AreEqual(new[] {a, c}, entities);

                query.SetSharedComponentFilter(new EcsTestSharedComp {value = 456});

                switch (jobMode)
                {
                    case EntityQueryJobMode.Async:
                    entities = query.ToEntityArrayAsync(World.UpdateAllocator.ToAllocator, out JobHandle jobHandle);
                    jobHandle.Complete();
                    break;
                    case EntityQueryJobMode.AsyncComplete:
                    entities = ChunkIterationUtility.CreateEntityArrayAsyncComplete(GetMatchingArchetypes(query),
                        World.UpdateAllocator.ToAllocator,
                        GetEntityTypeHandle(query), query, query.CalculateEntityCount(),query.GetDependency());
                    break;
                    default: //EntityQueryMethodType.Immediate
                    entities = ChunkIterationUtility.CreateEntityArray(GetMatchingArchetypes(query),
                        World.UpdateAllocator.ToAllocator,
                        GetEntityTypeHandle(query), query, query.CalculateEntityCount());
                    break;
                }

                CollectionAssert.AreEqual(new[] {b}, entities);
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
                    entities = query.ToEntityArrayAsync(World.UpdateAllocator.ToAllocator, out JobHandle jobHandle);
                    jobHandle.Complete();
                    break;
                    case EntityQueryJobMode.AsyncComplete:
                    entities = ChunkIterationUtility.CreateEntityArrayAsyncComplete(GetMatchingArchetypes(query),
                        World.UpdateAllocator.ToAllocator,
                        GetEntityTypeHandle(query), query, query.CalculateEntityCount(),query.GetDependency());
                    break;
                    default: //EntityQueryMethodType.Immediate
                    entities = ChunkIterationUtility.CreateEntityArray(GetMatchingArchetypes(query),
                        World.UpdateAllocator.ToAllocator,
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
                        components = query.ToComponentDataArrayAsync<EcsTestData>(World.UpdateAllocator.ToAllocator, out JobHandle jobHandle);
                        jobHandle.Complete();
                        break;
                    case EntityQueryJobMode.AsyncComplete:
                        components = ChunkIterationUtility.CreateComponentDataArrayAsyncComplete(World.UpdateAllocator.ToAllocator,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(),
                            query,query.GetDependency());
                        break;
                    default: //EntityQueryMethodType.Immediate
                        components = ChunkIterationUtility.CreateComponentDataArray(World.UpdateAllocator.ToAllocator,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(), query);
                        break;
                }

                CollectionAssert.AreEquivalent(new[] {ecsTestData1,ecsTestData2,ecsTestData3}, components);

                query.SetSharedComponentFilter(new EcsTestSharedComp {value = 123});

                switch (jobMode)
                {
                    case EntityQueryJobMode.Async:
                        components = query.ToComponentDataArrayAsync<EcsTestData>(World.UpdateAllocator.ToAllocator, out JobHandle jobHandle);
                        jobHandle.Complete();
                        break;
                    case EntityQueryJobMode.AsyncComplete:
                        components = ChunkIterationUtility.CreateComponentDataArrayAsyncComplete(World.UpdateAllocator.ToAllocator,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(),
                            query,query.GetDependency());
                        break;
                    default: //EntityQueryMethodType.Immediate
                        components = ChunkIterationUtility.CreateComponentDataArray(World.UpdateAllocator.ToAllocator,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(), query);
                        break;
                }

                CollectionAssert.AreEqual(new[] {ecsTestData1,ecsTestData3}, components);

                query.SetSharedComponentFilter(new EcsTestSharedComp {value = 456});

                switch (jobMode)
                {
                    case EntityQueryJobMode.Async:
                        components = query.ToComponentDataArrayAsync<EcsTestData>(World.UpdateAllocator.ToAllocator, out JobHandle jobHandle);
                        jobHandle.Complete();
                        break;
                    case EntityQueryJobMode.AsyncComplete:
                        components = ChunkIterationUtility.CreateComponentDataArrayAsyncComplete(World.UpdateAllocator.ToAllocator,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(),
                            query,query.GetDependency());
                        break;
                    default: //EntityQueryMethodType.Immediate
                        components = ChunkIterationUtility.CreateComponentDataArray(World.UpdateAllocator.ToAllocator,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(), query);
                        break;
                }

                CollectionAssert.AreEqual(new[] {ecsTestData2}, components);
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
                        components = query.ToComponentDataArrayAsync<EcsTestData>(World.UpdateAllocator.ToAllocator, out JobHandle jobHandle);
                        jobHandle.Complete();
                        break;
                    case EntityQueryJobMode.AsyncComplete:
                        components = ChunkIterationUtility.CreateComponentDataArrayAsyncComplete(World.UpdateAllocator.ToAllocator,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(),
                            query,query.GetDependency());
                        break;
                    default: //EntityQueryJobMode.Immediate
                        components = ChunkIterationUtility.CreateComponentDataArray(World.UpdateAllocator.ToAllocator,
                            GetComponentTypeHandle<EcsTestData>(query),query.CalculateEntityCount(), query);
                        break;
                }
                CollectionAssert.AreEqual(new[] {ecsData20,ecsData40,ecsData60}, components);
            }

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData2)))
            {
                NativeArray<EcsTestData2> components2;
                switch (jobMode)
                {
                    case EntityQueryJobMode.Async:
                        components2 = query.ToComponentDataArrayAsync<EcsTestData2>(World.UpdateAllocator.ToAllocator, out JobHandle jobHandle);
                        jobHandle.Complete();
                        break;
                    case EntityQueryJobMode.AsyncComplete:
                        components2 = ChunkIterationUtility.CreateComponentDataArrayAsyncComplete(World.UpdateAllocator.ToAllocator,
                            GetComponentTypeHandle<EcsTestData2>(query),query.CalculateEntityCount(),
                            query,query.GetDependency());
                        break;
                    default: //EntityQueryJobMode.Immediate
                        components2 = ChunkIterationUtility.CreateComponentDataArray(World.UpdateAllocator.ToAllocator,
                            GetComponentTypeHandle<EcsTestData2>(query),query.CalculateEntityCount(), query);
                        break;
                }
                CollectionAssert.AreEqual(new[] {ecsData2_20_40, ecsData2_60_80}, components2);
            }

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData3)))
            {
                NativeArray<EcsTestData3> components3;
                switch (jobMode)
                {
                    case EntityQueryJobMode.Async:
                        components3 = query.ToComponentDataArrayAsync<EcsTestData3>(World.UpdateAllocator.ToAllocator, out JobHandle jobHandle);
                        jobHandle.Complete();
                        break;
                    case EntityQueryJobMode.AsyncComplete:
                        components3 = ChunkIterationUtility.CreateComponentDataArrayAsyncComplete(World.UpdateAllocator.ToAllocator,
                            GetComponentTypeHandle<EcsTestData3>(query),query.CalculateEntityCount(),
                            query,query.GetDependency());
                        break;
                    default: //EntityQueryJobMode.Immediate
                        components3 = ChunkIterationUtility.CreateComponentDataArray(World.UpdateAllocator.ToAllocator,
                            GetComponentTypeHandle<EcsTestData3>(query),query.CalculateEntityCount(), query);
                        break;
                }
                CollectionAssert.AreEqual(new[] {ecsData3_20_40_60, ecsData3_80_100_120}, components3);
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
        public void ChunkListCaching_CheckCacheConsistency_PassesForNewEmptyQuery()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            Assert.AreEqual(0, archetype1.ChunkCount);
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                // Freshly-created empty queries which don't match any chunks yet are invalid, but not inconsistent
                Assert.IsFalse(query.IsCacheValid, "cached chunk list is valid on freshly-created empty query");
                Assert.IsTrue(query.CheckChunkListCacheConsistency(), "cached chunk list is inconsistent on freshly-created empty query");
                // After updating the cache (called automatically when the cache is accessed with GetMatchingChunkCache),
                // it should be consistent.
                query.UpdateCache();
                Assert.IsTrue(query.IsCacheValid, "cached chunk list is invalid after UpdateCache() call");
                Assert.IsTrue(query.CheckChunkListCacheConsistency(), "cached chunk list is inconsistent after UpdateCache() call");
            }
        }

        [Test]
        public void ChunkListCaching_CheckCacheConsistency_FailsForNewNonEmptyQuery()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var ent1 = m_Manager.CreateEntity(archetype1);
            Assert.AreEqual(1, archetype1.ChunkCount);
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                // Freshly-created queries are both invalid and inconsistent if they match any chunks
                Assert.IsFalse(query.IsCacheValid, "cached chunk list is valid on freshly-created query");
                Assert.IsFalse(query.CheckChunkListCacheConsistency(), "cached chunk list is consistent on freshly-created query");
                // After updating the cache (called automatically when the cache is accessed with GetMatchingChunkCache),
                // it should be consistent.
                query.UpdateCache();
                Assert.IsTrue(query.IsCacheValid, "cached chunk list is invalid after UpdateCache() call");
                Assert.IsTrue(query.CheckChunkListCacheConsistency(), "cached chunk list is inconsistent after UpdateCache() call");
            }
        }

        [Test]
        public void ChunkListCaching_CheckCacheConsistency_FailsAfterAddingAndRemovingChunks()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var ent1 = m_Manager.CreateEntity(archetype1);
            Assert.AreEqual(1, archetype1.ChunkCount);
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                // Start with a valid, consistent cache
                query.UpdateCache();
                Assert.IsTrue(query.IsCacheValid, "cached chunk list is invalid after UpdateCache() call");
                Assert.IsTrue(query.CheckChunkListCacheConsistency(),
                    "cached chunk list is inconsistent after UpdateCache() call");
                // Adding a new chunk AND removing an existing chunk to/from one of the matching archetypes should
                // invalidate the cache AND render it inconsistent.
                var ent2 = m_Manager.CreateEntity(archetype1);
                m_Manager.SetSharedComponentData(ent2, new EcsTestSharedComp {value = 17});
                Assert.AreEqual(2, archetype1.ChunkCount);
                m_Manager.DestroyEntity(ent1);
                Assert.AreEqual(1, archetype1.ChunkCount);
                Assert.IsFalse(query.IsCacheValid, "cached chunk list is valid after adding and removing chunk");
                Assert.IsFalse(query.CheckChunkListCacheConsistency(), "cached chunk list is consistent after adding and removing chunk");
                // Updating the cache should leave it valid and consistent.
                query.UpdateCache();
                Assert.IsTrue(query.IsCacheValid, "cached chunk list is invalid after UpdateCache() call");
                Assert.IsTrue(query.CheckChunkListCacheConsistency(), "cached chunk list is inconsistent after UpdateCache() call");
            }
        }

        [Test]
        public void ChunkListCaching_CheckCacheConsistency_FailsAfterAddingChunks()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var ent1 = m_Manager.CreateEntity(archetype1);
            Assert.AreEqual(1, archetype1.ChunkCount);
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                // Start with a valid, consistent cache
                query.UpdateCache();
                Assert.IsTrue(query.IsCacheValid, "cached chunk list is invalid after UpdateCache() call");
                Assert.IsTrue(query.CheckChunkListCacheConsistency(),
                    "cached chunk list is inconsistent after UpdateCache() call");
                // Adding a new chunk to one of the matching archetypes should invalidate the cache AND render it inconsistent.
                var ent2 = m_Manager.CreateEntity(archetype1);
                m_Manager.SetSharedComponentData(ent2, new EcsTestSharedComp {value = 17});
                Assert.AreEqual(2, archetype1.ChunkCount);
                Assert.IsFalse(query.IsCacheValid, "cached chunk list is valid after adding chunk");
                Assert.IsFalse(query.CheckChunkListCacheConsistency(), "cached chunk list is consistent after adding chunk");
                // Updating the cache should leave it valid and consistent.
                query.UpdateCache();
                Assert.IsTrue(query.IsCacheValid, "cached chunk list is invalid after UpdateCache() call");
                Assert.IsTrue(query.CheckChunkListCacheConsistency(), "cached chunk list is inconsistent after UpdateCache() call");
            }
        }

        [Test]
        public void ChunkListCaching_CheckCacheConsistency_FailsAfterRemovingChunks()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var ent1 = m_Manager.CreateEntity(archetype1);
            Assert.AreEqual(1, archetype1.ChunkCount);
            var ent2 = m_Manager.CreateEntity(archetype1);
            m_Manager.SetSharedComponentData(ent2, new EcsTestSharedComp {value = 17});
            Assert.AreEqual(2, archetype1.ChunkCount);
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                // Start with a valid, consistent cache
                query.UpdateCache();
                Assert.IsTrue(query.IsCacheValid, "cached chunk list is invalid after UpdateCache() call");
                Assert.IsTrue(query.CheckChunkListCacheConsistency(),
                    "cached chunk list is inconsistent after UpdateCache() call");
                // Removing a chunk from one of the matching archetypes should invalidate the cache AND render it inconsistent.
                m_Manager.DestroyEntity(ent2);
                Assert.AreEqual(1, archetype1.ChunkCount);
                Assert.IsFalse(query.IsCacheValid, "cached chunk list is valid after removing chunk");
                Assert.IsFalse(query.CheckChunkListCacheConsistency(), "cached chunk list is consistent after removing chunk");
                // Updating the cache should leave it valid and consistent.
                query.UpdateCache();
                Assert.IsTrue(query.IsCacheValid, "cached chunk list is invalid after UpdateCache() call");
                Assert.IsTrue(query.CheckChunkListCacheConsistency(), "cached chunk list is inconsistent after UpdateCache() call");
            }
        }

        [Test]
        public void ChunkListCaching_CheckCacheConsistency_PassesAfterAddingNewArchetype()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var ent1 = m_Manager.CreateEntity(archetype1);
            Assert.AreEqual(1, archetype1.ChunkCount);
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                // Start with a valid, consistent cache
                query.UpdateCache();
                Assert.IsTrue(query.IsCacheValid, "cached chunk list is invalid after UpdateCache() call");
                Assert.IsTrue(query.CheckChunkListCacheConsistency(), "cached chunk list is inconsistent after UpdateCache() call");
                // Adding a new archetype that matches the query should NOT immediately invalidate the cache or render it inconsistent,
                // as the new archetype has no chunks to match.
                var archetype2 = m_Manager.CreateArchetype(typeof(EcsTestData),
                    typeof(EcsTestData2), typeof(EcsTestSharedComp));
                Assert.IsTrue(query.IsCacheValid, "cached chunk list is invalid after creating new empty archetype");
                Assert.IsTrue(query.CheckChunkListCacheConsistency(), "cached chunk list is inconsistent after creating new empty archetype");
            }
        }

        [Test]
        public void ChunkListCaching_CheckCacheConsistency_PassesAfterAddingEntityToExistingChunk()
        {
            var archetype1 = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var ent1 = m_Manager.CreateEntity(archetype1);
            Assert.AreEqual(1, archetype1.ChunkCount);
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                // Start with a valid, consistent cache
                query.UpdateCache();
                Assert.IsTrue(query.IsCacheValid, "cached chunk list is invalid after UpdateCache() call");
                Assert.IsTrue(query.CheckChunkListCacheConsistency(), "cached chunk list is inconsistent after UpdateCache() call");
                // Structural changes which don't add/remove new chunks should neither invalidate the cache nor render it inconsistent;
                // no chunks were added/removed/changed.
                var ent2 = m_Manager.CreateEntity(archetype1);
                Assert.AreEqual(1, archetype1.ChunkCount);
                Assert.IsTrue(query.IsCacheValid, "cached chunk list is invalid after adding new entity to existing chunk");
                Assert.IsTrue(query.CheckChunkListCacheConsistency(), "cached chunk list is inconsistent after adding new entity to existing chunk");
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
            Assert.IsFalse(queryA.CheckChunkListCacheConsistency());
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
            Assert.IsFalse(queryA.CheckChunkListCacheConsistency());
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
            Assert.IsFalse(queryC.CheckChunkListCacheConsistency());
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
            Assert.IsFalse(queryC.CheckChunkListCacheConsistency());

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
            Assert.IsFalse(queryA.CheckChunkListCacheConsistency());
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
            Assert.IsFalse(queryA.CheckChunkListCacheConsistency());
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
            Assert.IsFalse(queryA.CheckChunkListCacheConsistency());
        }

        [Test]
        public void ChunkListCaching_DestroyEntity_Query()
        {
            // new archetype, no query
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));

            // new query, exisiting archetype, no entities
            var queryA = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            // new chunk of matching archetype
            using(var entitiesA = m_Manager.CreateEntity(archetypeA, archetypeA.ChunkCapacity, World.UpdateAllocator.ToAllocator))
            using(var entitiesB = m_Manager.CreateEntity(archetypeA, archetypeA.ChunkCapacity, World.UpdateAllocator.ToAllocator))
            {
                queryA.UpdateCache();
                Assert.IsTrue(queryA.IsCacheValid);
                Assert.IsTrue(queryA.CheckChunkListCacheConsistency());

                // destroy entities
                m_Manager.DestroyEntity(queryA);
                Assert.IsFalse(queryA.IsCacheValid);
                Assert.IsFalse(queryA.CheckChunkListCacheConsistency());
            }
        }

        [Test]
        public void CalculateEntityCountWithEntityList()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var allEntitiesA = m_Manager.CreateEntity(archetypeA, 10, World.UpdateAllocator.ToAllocator))
            using (var allEntitiesB = m_Manager.CreateEntity(archetypeB, 10, World.UpdateAllocator.ToAllocator))
            {
                var entities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(10, ref World.UpdateAllocator);
                for (int i = 0; i < 10; ++i)
                {
                    if (i % 2 == 0)
                        entities[i] = allEntitiesA[i];
                    else
                        entities[i] = allEntitiesB[i];
                }

                Assert.AreEqual(5, query.CalculateEntityCount(entities));
            }
        }

        [Test]
        public void ToEntityArrayWithEntityList()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var allEntitiesA = m_Manager.CreateEntity(archetypeA, 10, World.UpdateAllocator.ToAllocator))
            using (var allEntitiesB = m_Manager.CreateEntity(archetypeB, 10, World.UpdateAllocator.ToAllocator))
            {
                var entities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(10, ref World.UpdateAllocator);
                for (int i = 0; i < 10; ++i)
                {
                    if (i % 2 == 0)
                        entities[i] = allEntitiesA[i];
                    else
                        entities[i] = allEntitiesB[i];
                }

                var res = query.ToEntityArray(entities, World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(5, res.Length);
                for (int i = 0; i < res.Length; ++i)
                {
                    Assert.AreEqual(allEntitiesA[i * 2], res[i]);
                }
            }
        }

        [Test]
        public void ToComponentDataArrayWithEntityList()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var allEntitiesA = m_Manager.CreateEntity(archetypeA, 10, World.UpdateAllocator.ToAllocator))
            using (var allEntitiesB = m_Manager.CreateEntity(archetypeB, 10, World.UpdateAllocator.ToAllocator))
            {
                var entities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(10, ref World.UpdateAllocator);
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

                var res = query.ToComponentDataArray<EcsTestData>(entities, World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(5, res.Length);
                for (int i = 0; i < res.Length; ++i)
                {
                    Assert.AreEqual(m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i * 2]).value, res[i].value);
                }
            }
        }

        [Test]
        public void MatchesInList()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var allEntitiesA = m_Manager.CreateEntity(archetypeA, 10, World.UpdateAllocator.ToAllocator))
            using (var allEntitiesB = m_Manager.CreateEntity(archetypeB, 10, World.UpdateAllocator.ToAllocator))
            {
                var entities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(10, ref World.UpdateAllocator);
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
            private UnsafeList<Entity>* m_Data;

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

                m_Data = UnsafeList<Entity>.Create(initialCapacity, allocator);
                m_Allocator = allocator;
            }

            public void AddRange(NativeArray<Entity> entities)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
                m_Data->AddRange(entities.GetUnsafeReadOnlyPtr(), entities.Length);
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
                    UnsafeList<Entity>.Destroy(m_Data);
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
            using (var customContainer = new ReadOnlyArrayContainer(10, World.UpdateAllocator.ToAllocator))
            using (var array = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(10, ref World.UpdateAllocator))
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
            using (var customContainer = new ReadOnlyArrayContainer(10, World.UpdateAllocator.ToAllocator))
            using (var array = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(10, ref World.UpdateAllocator))
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
            using (var customContainer = new ReadOnlyArrayContainer(10, World.UpdateAllocator.ToAllocator))
            using (var array = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(10, ref World.UpdateAllocator))
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
            using (var customContainer = new ReadOnlyArrayContainer(10, World.UpdateAllocator.ToAllocator))
            using (var array = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(10, ref World.UpdateAllocator))
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

        struct ReadFromEntityJob : IJob
        {
            [ReadOnly] public ComponentTypeHandle<Entity> TypeHandle;
            public void Execute()
            {
            }
        }

        struct WriteToEntityJob : IJob
        {
            public ComponentTypeHandle<Entity> TypeHandle;
            public void Execute()
            {
            }
        }

        [Test]
        public void EntityQuery_ToEntityArray_WithRunningJob_NoDependency_Throws()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var entities = m_Manager.CreateEntity(archetype, 10000, World.UpdateAllocator.ToAllocator);
            NativeArray<Entity> output = default;

            // Test with a job that reads from entity data; this should work fine.
            var typeHandleRO = m_Manager.GetComponentTypeHandle<Entity>(true);
            var jobHandleRO = new ReadFromEntityJob { TypeHandle = typeHandleRO }.Schedule();
            //query.AddDependency(jobHandleRO); //This test makes sure we detect the race condition if this dependency is NOT established.
            Assert.DoesNotThrow(() => { output = query.ToEntityArray(World.UpdateAllocator.ToAllocator); });
            jobHandleRO.Complete();
            CollectionAssert.AreEqual(entities.ToArray(), output.ToArray());
            if (output.IsCreated)
                output.Dispose();


            // It's highly unusual for a job to have write access to the Entity component, but for symmetry with the subsequent
            // tests we'll test it here anyway.
            var typeHandleRW = m_Manager.GetComponentTypeHandle<Entity>(false);
            var jobHandleRW = new WriteToEntityJob { TypeHandle = typeHandleRW }.Schedule();
            //query.AddDependency(jobHandleRW); //This test makes sure we detect the race condition if this dependency is NOT established.
            Assert.Throws<InvalidOperationException>(() => { output = query.ToEntityArray(World.UpdateAllocator.ToAllocator); });
            jobHandleRW.Complete();
        }

        [Test]
        public void EntityQuery_ToEntityArray_WithRunningJob_WithDependency_Works()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var entities = m_Manager.CreateEntity(archetype, 10000, World.UpdateAllocator.ToAllocator);
            NativeArray<Entity> output = default;

            // Test with a job that reads from entity data; this should work fine.
            var typeHandleRO = m_Manager.GetComponentTypeHandle<Entity>(true);
            var jobHandleRO = new ReadFromEntityJob { TypeHandle = typeHandleRO }.Schedule();
            query.AddDependency(jobHandleRO);
            Assert.DoesNotThrow(() => { output = query.ToEntityArray(World.UpdateAllocator.ToAllocator); });
            jobHandleRO.Complete();
            CollectionAssert.AreEqual(entities.ToArray(), output.ToArray());
            if (output.IsCreated)
                output.Dispose();

            // It's highly unusual for a job to have write access to the Entity component, but for symmetry with the subsequent
            // tests we'll test it here anyway.
            var typeHandleRW = m_Manager.GetComponentTypeHandle<Entity>(false);
            var jobHandleRW = new WriteToEntityJob { TypeHandle = typeHandleRW }.Schedule();
            query.AddDependency(jobHandleRW);
            Assert.DoesNotThrow(() => { output = query.ToEntityArray(World.UpdateAllocator.ToAllocator); });
            jobHandleRW.Complete();
            CollectionAssert.AreEqual(entities.ToArray(), output.ToArray());
            if (output.IsCreated)
                output.Dispose();
        }

        struct ReadFromComponentJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<EcsTestData> TypeHandle;
            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
            }
        }

        struct WriteToComponentJob : IJobEntityBatch
        {
            public ComponentTypeHandle<EcsTestData> TypeHandle;
            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
            }
        }

        [Test]
        public void EntityQuery_ToComponentDataArray_WithRunningJob_NoDependency_Throws()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var entities = m_Manager.CreateEntity(archetype, 1000, World.UpdateAllocator.ToAllocator);

            NativeArray<EcsTestData> newValues =
                CollectionHelper.CreateNativeArray<EcsTestData>(entities.Length, World.UpdateAllocator.ToAllocator);
            for (int i = 0; i < entities.Length; ++i)
            {
                m_Manager.SetComponentData(entities[i], new EcsTestData(i));
                newValues[i] = new EcsTestData(2*i);
            }

            // test with running job reading from component data. This should work fine.
            NativeArray<EcsTestData> testValues = default;
            using var readValues = query.ToComponentDataArrayAsync<EcsTestData>(World.UpdateAllocator.ToAllocator, out var readJobHandle);
            //query.AddDependency(readJobHandle); //This test makes sure we detect the race condition if this dependency is NOT established.
            Assert.DoesNotThrow(() => { testValues = query.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator); });
            readJobHandle.Complete();
            CollectionAssert.AreEqual(readValues.ToArray(), testValues.ToArray());
            if (testValues.IsCreated)
                testValues.Dispose();

            // test with running job writing to component data. This is a race condition.
            var typeHandleRW = m_Manager.GetComponentTypeHandle<EcsTestData>(false);
            var writeJobHandle = new WriteToComponentJob { TypeHandle = typeHandleRW }.ScheduleParallel(query);
            Assert.Throws<InvalidOperationException>(() => { testValues = query.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator); });
            writeJobHandle.Complete();
            if (testValues.IsCreated)
                testValues.Dispose();
        }

        [Test]
        public void EntityQuery_ToComponentDataArray_WithRunningJob_WithDependency_Works()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var entities = m_Manager.CreateEntity(archetype, 1000, World.UpdateAllocator.ToAllocator);

            NativeArray<EcsTestData> newValues =
                CollectionHelper.CreateNativeArray<EcsTestData>(entities.Length, World.UpdateAllocator.ToAllocator);
            for (int i = 0; i < entities.Length; ++i)
            {
                m_Manager.SetComponentData(entities[i], new EcsTestData(i));
                newValues[i] = new EcsTestData(2*i);
            }

            // test with running job reading from component data
            NativeArray<EcsTestData> testValues = default;
            using var readValues = query.ToComponentDataArrayAsync<EcsTestData>(World.UpdateAllocator.ToAllocator, out var readJobHandle);
            query.AddDependency(readJobHandle); // ensure the following query operation sees this job when it completes its dependencies
            Assert.DoesNotThrow(() => { testValues = query.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator); });
            CollectionAssert.AreEqual(readValues.ToArray(), testValues.ToArray());
            testValues.Dispose();

            // test with running job writing to component data
            query.CopyFromComponentDataArrayAsync(newValues, out var writeJobHandle);
            query.AddDependency(writeJobHandle); // ensure the following query operation sees this job when it completes its dependencies
            Assert.DoesNotThrow(() => { testValues = query.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator); });
            CollectionAssert.AreEqual(newValues.ToArray(), testValues.ToArray());
            testValues.Dispose();

            newValues.Dispose();
        }

        [Test]
        public void EntityQuery_CopyFromComponentDataArray_WithRunningJob_NoDependency_Throws()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var entities = m_Manager.CreateEntity(archetype, 1000, World.UpdateAllocator.ToAllocator);

            NativeArray<EcsTestData> newValues =
                CollectionHelper.CreateNativeArray<EcsTestData>(entities.Length, World.UpdateAllocator.ToAllocator);
            for (int i = 0; i < entities.Length; ++i)
            {
                m_Manager.SetComponentData(entities[i], new EcsTestData(i));
                newValues[i] = new EcsTestData(2*i);
            }

            // test with running job reading from component data. This is a race condition.
            var typeHandleRO = m_Manager.GetComponentTypeHandle<EcsTestData>(true);
            var readJobHandle = new ReadFromComponentJob { TypeHandle = typeHandleRO }.ScheduleParallel(query);
            Assert.Throws<InvalidOperationException>(() => { query.CopyFromComponentDataArray(newValues); });
            readJobHandle.Complete();

            // test with running job writing to component data. This is a race condition.
            var typeHandleRW = m_Manager.GetComponentTypeHandle<EcsTestData>(false);
            var writeJobHandle = new WriteToComponentJob { TypeHandle = typeHandleRW }.ScheduleParallel(query);
            Assert.Throws<InvalidOperationException>(() => { query.CopyFromComponentDataArray(newValues); });
            writeJobHandle.Complete();

            newValues.Dispose();
        }

        [Test]
        public void EntityQuery_CopyFromComponentDataArray_WithRunningJob_WithDependency_Works()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            using var entities = m_Manager.CreateEntity(archetype, 1000, World.UpdateAllocator.ToAllocator);

            NativeArray<EcsTestData> newValues =
                CollectionHelper.CreateNativeArray<EcsTestData>(entities.Length, World.UpdateAllocator.ToAllocator);
            for (int i = 0; i < entities.Length; ++i)
            {
                m_Manager.SetComponentData(entities[i], new EcsTestData(i));
                newValues[i] = new EcsTestData(2*i);
            }

            // test with running job reading from component data
            using var readValues = query.ToComponentDataArrayAsync<EcsTestData>(World.UpdateAllocator.ToAllocator, out var readJobHandle);
            query.AddDependency(readJobHandle); // ensure the following query operation sees this job when it completes its dependencies
            Assert.DoesNotThrow(() => { query.CopyFromComponentDataArray(newValues); });
            for (int i = 0; i < entities.Length; ++i)
            {
                Assert.AreEqual(i, readValues[i].value);
                Assert.AreEqual(newValues[i].value, m_Manager.GetComponentData<EcsTestData>(entities[i]).value);
            }

            // test with running job writing to component data
            query.CopyFromComponentDataArrayAsync(readValues, out var writeJobHandle);
            query.AddDependency(writeJobHandle); // ensure the following query operation sees this job when it completes its dependencies
            Assert.DoesNotThrow(() => { query.CopyFromComponentDataArray(newValues); });
            for (int i = 0; i < entities.Length; ++i)
            {
                Assert.AreEqual(newValues[i].value, m_Manager.GetComponentData<EcsTestData>(entities[i]).value);
            }
        }

        partial class EntityQueryDescBuilderTestSystem : SystemBase
        {
            public EntityQuery Query;
            public JobHandle JobDependency => Dependency;

            protected override void OnCreate()
            {
                var builder = new EntityQueryDescBuilder(Allocator.Temp);
                builder.AddAll(typeof(EcsTestData));
                builder.AddAll(ComponentType.ReadOnly<EcsTestData2>());
                builder.AddNone(typeof(EcsTestTag));
                builder.FinalizeQuery();
                Query = GetEntityQuery(builder);
            }

            protected override void OnUpdate()
            {
                Entities.WithNone<EcsTestTag>().ForEach((ref EcsTestData sum, in EcsTestData2 addends) =>
                {
                    sum.value = addends.value0 + addends.value1;
                }).Schedule();
            }
        }

        [Test]
        public void EntityQuery_SystemBase_GetEntityQueryFromEntityQueryDescBuilder_Works()
        {
            var entityWithTag = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestTag));
            var entityWithoutTag = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));
            m_Manager.SetComponentData(entityWithoutTag, new EcsTestData2{ value0 = 27, value1 = 15 });

            var sys = World.GetOrCreateSystem<EntityQueryDescBuilderTestSystem>();
            sys.Update();

            Assert.AreEqual(1, sys.Query.CalculateEntityCount());

            sys.JobDependency.Complete();
            var arr = sys.Query.ToComponentDataArray<EcsTestData>(Allocator.Temp);
            Assert.AreEqual(42, arr[0].value);
        }


#if !UNITY_DOTSRUNTIME
        [AlwaysUpdateSystem]
        public partial class CachedSystemQueryTestSystem : SystemBase
        {
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

            protected override void OnUpdate()
            {
                 Entities.ForEach((ref EcsTestData data) => { data.value = 10; }).Schedule();
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
            using (var array = new NativeArray<EcsTestData2>(1, Allocator.Persistent))
            {
                Assert.Throws<ArgumentException>(() => m_Manager.AddComponent(world.EntityManager.UniversalQuery, typeof(EcsTestData)));
                Assert.Throws<ArgumentException>(() => m_Manager.AddComponent(world.EntityManager.UniversalQuery, new ComponentTypes(typeof(EcsTestData))));
                Assert.Throws<ArgumentException>(() => m_Manager.AddComponentData(world.EntityManager.UniversalQuery, array));
                Assert.Throws<ArgumentException>(() => m_Manager.AddSharedComponentData(world.EntityManager.UniversalQuery, new EcsTestSharedComp()));
                Assert.Throws<ArgumentException>(() => m_Manager.SetSharedComponentData(world.EntityManager.UniversalQuery, new EcsTestSharedComp(1)));
                Assert.Throws<ArgumentException>(() => m_Manager.DestroyEntity(world.EntityManager.UniversalQuery));
                Assert.Throws<ArgumentException>(() => m_Manager.RemoveComponent<EcsTestData>(world.EntityManager.UniversalQuery));
                Assert.Throws<ArgumentException>(() => m_Manager.RemoveComponent(world.EntityManager.UniversalQuery, new ComponentTypes(typeof(EcsTestData))));
                Assert.Throws<ArgumentException>(() => m_Manager.AddChunkComponentData(world.EntityManager.UniversalQuery, new EcsTestData(1)));
                Assert.Throws<ArgumentException>(() => m_Manager.GetEntityQueryMask(world.EntityManager.UniversalQuery));
#if !UNITY_DISABLE_MANAGED_COMPONENTS
                Assert.Throws<ArgumentException>(() => m_Manager.AddChunkComponentData(world.EntityManager.UniversalQuery, new EcsTestManagedComponent() { value = "SomeString" }));
#endif
            }
        }

        [Test]
        public void QueryAlreadyDisposedThrowsThrows()
        {
            EntityQuery query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            query.Dispose();
            using (var array = new NativeArray<EcsTestData2>(1, Allocator.Persistent))
            {
                Assert.Throws<ArgumentException>(() => m_Manager.AddComponent(query, typeof(EcsTestData)));
                Assert.Throws<ArgumentException>(() => m_Manager.AddComponent(query, new ComponentTypes(typeof(EcsTestData))));
                Assert.Throws<ArgumentException>(() => m_Manager.AddComponentData(query, array));
                Assert.Throws<ArgumentException>(() => m_Manager.AddSharedComponentData(query, new EcsTestSharedComp()));
                Assert.Throws<ArgumentException>(() => m_Manager.SetSharedComponentData(query, new EcsTestSharedComp(1)));
                Assert.Throws<ArgumentException>(() => m_Manager.DestroyEntity(query));
                Assert.Throws<ArgumentException>(() => m_Manager.RemoveComponent<EcsTestData>(query));
                Assert.Throws<ArgumentException>(() => m_Manager.RemoveComponent(query, new ComponentTypes(typeof(EcsTestData))));
                Assert.Throws<ArgumentException>(() => m_Manager.AddChunkComponentData(query, new EcsTestData(1)));
                Assert.Throws<ArgumentException>(() => m_Manager.GetEntityQueryMask(query));
#if !UNITY_DISABLE_MANAGED_COMPONENTS
                Assert.Throws<ArgumentException>(() => m_Manager.AddChunkComponentData(query, new EcsTestManagedComponent() { value = "SomeString" }));
#endif
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

            var values = CollectionHelper.CreateNativeArray<EcsTestData, RewindableAllocator>(archetype.ChunkCapacity * 2, ref World.UpdateAllocator);
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

            var dataArray = query.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);
            CollectionAssert.AreEquivalent(values, dataArray);

            dataArray.Dispose();
            values.Dispose();
        }

        [Test]
        public void CopyFromComponentDataArray_SetsChangeVersion([Values] EntityQueryJobMode jobMode)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            m_Manager.CreateEntity(archetype, archetype.ChunkCapacity * 2);

            var values = CollectionHelper.CreateNativeArray<EcsTestData, RewindableAllocator>(archetype.ChunkCapacity * 2, ref World.UpdateAllocator);
            for (int i = 0; i < archetype.ChunkCapacity * 2; ++i)
            {
                values[i] = new EcsTestData{value = i};
            }

            var typeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(true);
            var query = EmptySystem.GetEntityQuery(typeof(EcsTestData));
            using var chunksOld = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);
            for (int i = 0; i < chunksOld.Length; ++i)
            {
                Assert.AreEqual(typeHandle.GlobalSystemVersion, chunksOld[i].GetChangeVersion(typeHandle));
            }

            uint fakeSystemVersion = 42;
            m_ManagerDebug.SetGlobalSystemVersion(fakeSystemVersion);
            switch (jobMode)
            {
                case EntityQueryJobMode.Async:
                    query.CopyFromComponentDataArrayAsync(values, out JobHandle jobHandle);
                    jobHandle.Complete();
                    break;
                case EntityQueryJobMode.Immediate:
                default:
                    query.CopyFromComponentDataArray(values);
                    break;
            }
            values.Dispose();

            using var chunksNew = query.CreateArchetypeChunkArray(World.UpdateAllocator.ToAllocator);
            for (int i = 0; i < chunksOld.Length; ++i)
            {
                Assert.AreEqual(fakeSystemVersion, chunksNew[i].GetChangeVersion(typeHandle));
            }
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
            Assert.Throws<ArgumentException>(
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

        [Test]
        public void EntityQueryDescBuilder_CreateBuilder()
        {
            ComponentType allType = typeof(EcsTestData);
            ComponentType noneType = typeof(EcsTestData2);

            var builder = new EntityQueryDescBuilder(Allocator.Temp);
            builder.AddAll(allType);
            builder.AddNone(noneType);
            builder.FinalizeQuery();

            Assert.AreEqual(1, builder.m_IndexData.Length);
            Assert.AreEqual(2, builder.m_TypeData.Length);

            var entityQueryData = builder.m_IndexData[0];
            var typeData = builder.m_TypeData;

            Assert.AreEqual(1, entityQueryData.All.Count);
            Assert.AreEqual(1, entityQueryData.None.Count);
            Assert.AreEqual(0, entityQueryData.Any.Count);

            Assert.AreEqual(allType, typeData[entityQueryData.All.Index]);
            Assert.AreEqual(noneType, typeData[entityQueryData.None.Index]);

            builder.Dispose();
        }

        [Test]
        public void EntityQueryDescBuilder_CreateMoreThanOne()
        {
            ComponentType type1 = typeof(EcsTestData);
            ComponentType type2 = typeof(EcsTestData2);

            var builder = new EntityQueryDescBuilder(Allocator.Temp);
            builder.AddAll(type1);
            builder.AddNone(type2);
            builder.FinalizeQuery();

            builder.AddAll(type2);
            builder.AddAny(type1);
            builder.AddAny(type2);
            builder.FinalizeQuery();

            Assert.AreEqual(2, builder.m_IndexData.Length);
            Assert.AreEqual(5, builder.m_TypeData.Length);

            var entityQueryData = builder.m_IndexData[0];
            var typeData = builder.m_TypeData;

            Assert.AreEqual(1, entityQueryData.All.Count);
            Assert.AreEqual(1, entityQueryData.None.Count);
            Assert.AreEqual(0, entityQueryData.Any.Count);

            Assert.AreEqual(type1, typeData[entityQueryData.All.Index]);
            Assert.AreEqual(type2, typeData[entityQueryData.None.Index]);

            var entityQueryData2 = builder.m_IndexData[1];

            Assert.AreEqual(1, entityQueryData2.All.Count);
            Assert.AreEqual(2, entityQueryData2.Any.Count);
            Assert.AreEqual(0, entityQueryData2.None.Count);

            Assert.AreEqual(type2, typeData[entityQueryData2.All.Index]);

            Assert.AreEqual(type1, typeData[entityQueryData2.Any.Index]);
            Assert.AreEqual(type2, typeData[entityQueryData2.Any.Index + 1]);

            builder.Dispose();
        }

        [BurstCompile(CompileSynchronously = true)]
        public partial struct BurstCompiledUnmanagedSystemEntityQueryDescBuilder : ISystem
        {
            private EntityQuery _Query;

            ComponentTypeHandle<EcsTestFloatData3> _RotationTypeHandle;
            ComponentTypeHandle<EcsTestFloatData> _RotationSpeedTypeHandle;

            [BurstCompile(CompileSynchronously = true)]
            unsafe struct MyJob : IJobEntityBatch
            {
                public ComponentTypeHandle<EcsTestFloatData3> RotationTypeHandle;
                [ReadOnly] public ComponentTypeHandle<EcsTestFloatData> RotationSpeedTypeHandle;

                public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
                {
                }
            }

            [BurstCompile(CompileSynchronously = true)]
            public void OnCreate(ref SystemState state)
            {
                var builder = new EntityQueryDescBuilder(Allocator.Temp);
                builder.AddAll(ComponentType.ReadWrite<EcsTestFloatData3>());
                builder.AddAll(ComponentType.ReadOnly<EcsTestFloatData>());
                builder.FinalizeQuery();
                _Query = state.GetEntityQuery(builder);
                builder.Dispose();
                _RotationTypeHandle = state.GetComponentTypeHandle<EcsTestFloatData3>();
                _RotationSpeedTypeHandle = state.GetComponentTypeHandle<EcsTestFloatData>();
            }

            [BurstCompile(CompileSynchronously = true)]
            public void OnDestroy(ref SystemState state)
            {
            }

            [BurstDiscard]
            static void CheckRunningBurst()
            {
                throw new ArgumentException("Not running burst");
            }

            [BurstCompile(CompileSynchronously = true)]
            public void OnUpdate(ref SystemState state)
            {
                CheckRunningBurst();

                _RotationTypeHandle.Update(ref state);
                _RotationSpeedTypeHandle.Update(ref state);

                var job = new MyJob
                {
                    RotationTypeHandle = _RotationTypeHandle,
                    RotationSpeedTypeHandle = _RotationSpeedTypeHandle,
                };
                JobEntityBatchExtensions.RunWithoutJobs(ref job, _Query);
            }
        }

        class TestGroup : ComponentSystemGroup
        {
        }

        [Test]
        public void BurstCompiledUnmanagedSystemEntityQueryDescBuilderWorks()
        {
            var group = World.CreateSystem<TestGroup>();
            var sys = World.AddSystem<BurstCompiledUnmanagedSystemEntityQueryDescBuilder>();
            group.AddSystemToUpdateList(sys.Handle);
            Assert.DoesNotThrow(() => group.Update());
            group.CompleteDependencyInternal();
        }

        [Test]
        public void GetEntityQueryDesc()
        {
            var queryDesc = new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<EcsTestData>(), ComponentType.ReadWrite<EcsTestData2>() },
                Any = new[] { ComponentType.ReadOnly<EcsTestData3>(), ComponentType.ReadWrite<EcsTestData4>() },
                None = new[] { ComponentType.ReadOnly<EcsTestFloatData>(), ComponentType.ReadWrite<EcsTestFloatData2>() },
                Options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabled
            };
            using (var query = m_Manager.CreateEntityQuery(queryDesc))
            {
                Assert.That(query.GetEntityQueryDesc(), Is.EqualTo(queryDesc));
            }
        }

        partial class CalculateEntityCount_WithAny_System : SystemBase
        {
            private EntityQuery _query;
            private EntityQuery _queryFromForEach;
            protected override void OnCreate()
            {
                _query = GetEntityQuery(ComponentType.ReadOnly<TestTag0>(), ComponentType.ReadOnly<TestTag1>());
            }

            protected override void OnUpdate()
            {
                int expectedCount = _query.CalculateEntityCount();
                int actualCount = 0;
                // Should match 20 entities
                Entities
                    .WithAll<TestTag0>()
                    .WithAny<TestTag2,TestTag3>()
                    .ForEach((Entity entity, in TestTag1 bComponent) =>
                    {
                        actualCount++;
                    }).Run();
                // Should match the remaining 10 entities
                Entities
                    .WithAll<TestTag0>()
                    .ForEach((Entity entity, in TestTag4 eComponent) =>
                    {
                        actualCount++;
                    }).Run();
                Assert.AreEqual(30, expectedCount, "Query on common components should match all 30 entities");
                Assert.AreEqual(30, actualCount, "Between the two jobs, all 30 entities should be found once each");
                //Assert.AreEqual(expectedCount, actualCount);
            }
        }

        [Test]
        public void CalculateEntityCount_WithAny_Works()
        {
            var archetype012 = m_Manager.CreateArchetype(typeof(TestTag0), typeof(TestTag1), typeof(TestTag2));
            var archetype013 = m_Manager.CreateArchetype(typeof(TestTag0), typeof(TestTag1), typeof(TestTag3));
            var archetype014 = m_Manager.CreateArchetype(typeof(TestTag0), typeof(TestTag1), typeof(TestTag4));
            m_Manager.CreateEntity(archetype012, 10);
            m_Manager.CreateEntity(archetype013, 10);
            m_Manager.CreateEntity(archetype014, 10);
            var sys = World.CreateSystem<CalculateEntityCount_WithAny_System>();
            sys.Update();
        }
    }
}
#endif // NET_DOTS
