using NUnit.Framework;
using Unity.Collections;

namespace Unity.Entities.Tests
{
    class ComponentEnabledBitFeatureTests : ECSTestsFixture
    {
        struct SetValueJob : IJobEntityBatch
        {
            public ComponentTypeHandle<EcsTestDataEnableable> TypeRW;
            public int SetValue;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var data = batchInChunk.GetNativeArray(TypeRW);
                for (int i = 0; i < batchInChunk.Count; ++i)
                {
                    data[i] = new EcsTestDataEnableable(SetValue);
                }
            }
        }

        struct DisableEveryOtherEntityJob : IJobEntityBatch
        {
            public ComponentTypeHandle<EcsTestDataEnableable> TypeRW;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                for (int i = 0; i < batchInChunk.Count; ++i)
                {
                    var enabledValue = i % 2 == 0;
                    batchInChunk.SetComponentEnabled(TypeRW, i, enabledValue);
                }
            }
        }

        struct SetValueToIndexJob : IJobEntityBatchWithIndex
        {
            public ComponentTypeHandle<EcsTestDataEnableable> TypeRW;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex, int indexOfFirstEntityInQuery)
            {
                for (int i = 0; i < batchInChunk.Count; ++i)
                {
                    var data = batchInChunk.GetNativeArray(TypeRW);
                    data[i] = new EcsTestDataEnableable(indexOfFirstEntityInQuery + i);
                }
            }
        }

        [Test]
        public void IJobEntityBatch_GeneratesCorrectBatches()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            using (var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity, Allocator.TempJob))
            using (var query = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestDataEnableable>()))
            {
                m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[10], false);
                m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[63], false);
                m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[64], false);

                var setValue = 10;
                new SetValueJob
                {
                    TypeRW = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false),
                    SetValue = setValue
                }.Run(query);

                for (int i = 0; i < entities.Length; ++i)
                {
                    if (i == 10 || i == 63 || i == 64)
                    {
                        Assert.AreEqual(0, m_Manager.GetComponentData<EcsTestDataEnableable>(entities[i]).value);
                        continue;
                    }

                    Assert.AreEqual(10, m_Manager.GetComponentData<EcsTestDataEnableable>(entities[i]).value);
                }
            }
        }

        [Test]
        public void IJobEntityBatch_ParallelJob_GeneratesExpectedBatches()
        {
            var chunkCount = 10;
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            using (var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity * chunkCount, Allocator.TempJob))
            using (var query = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestDataEnableable>()))
            {
                var jobHandle = new DisableEveryOtherEntityJob
                {
                    TypeRW = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false),
                }.ScheduleParallel(query);

                var setValue = 10;
                jobHandle = new SetValueJob
                {
                    TypeRW = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false),
                    SetValue = setValue
                }.ScheduleParallel(query, jobHandle);

                jobHandle.Complete();

                for (int i = 0; i < entities.Length; ++i)
                {
                    var chunkIndex = i / archetype.ChunkCapacity;
                    var indexInChunk = i - (chunkIndex * archetype.ChunkCapacity);
                    var expectedValue = indexInChunk % 2 == 0 ? 10 : 0;
                    Assert.AreEqual(expectedValue, m_Manager.GetComponentData<EcsTestDataEnableable>(entities[i]).value);
                }
            }
        }

        [Test]
        public void IJobEntityBatch_WithFiltering_ParallelJob_GeneratesExpectedBatches()
        {
            var chunkCount = 10;
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestSharedComp));
            using (var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity * chunkCount, Allocator.TempJob))
            using (var query = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestDataEnableable>(), typeof(EcsTestSharedComp)))
            {
                for (int i = 0; i < entities.Length; ++i)
                {
                    if(i % 2 == 0)
                        m_Manager.SetSharedComponentData(entities[i], new EcsTestSharedComp(10));
                }

                query.SetSharedComponentFilter(new EcsTestSharedComp(10));

                var jobHandle = new DisableEveryOtherEntityJob
                {
                    TypeRW = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false),
                }.ScheduleParallel(query);

                var setValue = 10;
                jobHandle = new SetValueJob
                {
                    TypeRW = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false),
                    SetValue = setValue
                }.ScheduleParallel(query, jobHandle);

                jobHandle.Complete();

                for (int i = 0; i < entities.Length; ++i)
                {
                    if(i % 2 != 0)
                        Assert.AreEqual(0, m_Manager.GetComponentData<EcsTestDataEnableable>(entities[i]).value);
                    else
                    {
                        var indexInFilteredEntities = i / 2;
                        var chunkIndex = indexInFilteredEntities / archetype.ChunkCapacity;
                        var indexInChunk = indexInFilteredEntities - (chunkIndex * archetype.ChunkCapacity);
                        var expectedValue = indexInChunk % 2 == 0 ? 10 : 0;
                        Assert.AreEqual(expectedValue,
                            m_Manager.GetComponentData<EcsTestDataEnableable>(entities[i]).value);
                    }
                }
            }
        }

        [Test]
        public void IJobEntityBatchWithIndex_ParallelJob_GeneratesExpectedBatches()
        {
            var chunkCount = 10;
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            using (var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity * chunkCount, Allocator.TempJob))
            using (var query = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestDataEnableable>()))
            {
                var jobHandle = new DisableEveryOtherEntityJob
                {
                    TypeRW = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false),
                }.ScheduleParallel(query);

                jobHandle = new SetValueToIndexJob
                {
                    TypeRW = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false),
                }.ScheduleParallel(query, jobHandle);

                jobHandle.Complete();

                for (int i = 0; i < entities.Length; ++i)
                {
                    var chunkIndex = i / archetype.ChunkCapacity;
                    var indexInChunk = i - (chunkIndex * archetype.ChunkCapacity);
                    var halfChunkCapacity = archetype.ChunkCapacity / 2;
                    var expectedValue = indexInChunk % 2 == 0 ? chunkIndex * halfChunkCapacity + indexInChunk / 2 : 0;
                    Assert.AreEqual(expectedValue, m_Manager.GetComponentData<EcsTestDataEnableable>(entities[i]).value);
                }
            }
        }

        [Test]
        public void IJobEntityBatchWithIndex_WithFiltering_ParallelJob_GeneratesExpectedBatches()
        {
            var chunkCount = 10;
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestSharedComp));
            using (var entities =
                m_Manager.CreateEntity(archetype, archetype.ChunkCapacity * chunkCount, Allocator.TempJob))
            using (var query = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestDataEnableable>(),
                typeof(EcsTestSharedComp)))
            {
                for (int i = 0; i < entities.Length; ++i)
                {
                    if (i % 2 == 0)
                        m_Manager.SetSharedComponentData(entities[i], new EcsTestSharedComp(10));
                }

                query.SetSharedComponentFilter(new EcsTestSharedComp(10));

                var jobHandle = new DisableEveryOtherEntityJob
                {
                    TypeRW = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false),
                }.ScheduleParallel(query);

                jobHandle = new SetValueToIndexJob
                {
                    TypeRW = m_Manager.GetComponentTypeHandle<EcsTestDataEnableable>(false),
                }.ScheduleParallel(query, jobHandle);

                jobHandle.Complete();

                for (int i = 0; i < entities.Length; ++i)
                {
                    // if the entity is not in a chunk processed with filtering
                    if (i % 2 != 0)
                        Assert.AreEqual(0, m_Manager.GetComponentData<EcsTestDataEnableable>(entities[i]).value);
                    else
                    {
                        var indexInFilteredEntities = i / 2;
                        var chunkIndex = indexInFilteredEntities / archetype.ChunkCapacity;
                        var indexInChunk = indexInFilteredEntities - (chunkIndex * archetype.ChunkCapacity);
                        var halfChunkCapacity = archetype.ChunkCapacity / 2;
                        var expectedValue =
                            indexInChunk % 2 == 0 ? chunkIndex * halfChunkCapacity + indexInChunk / 2 : 0;
                        Assert.AreEqual(expectedValue,
                            m_Manager.GetComponentData<EcsTestDataEnableable>(entities[i]).value);
                    }

                }
            }
        }

        [Test]
        public void ArchetypeChunkIterator_GeneratesExpectedBatches()
        {
            var chunkCount = 2;
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
            using (var entities = m_Manager.CreateEntity(archetype, archetype.ChunkCapacity * chunkCount, Allocator.TempJob))
            using (var query = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestDataEnableable>()))
            {
                var iterator = query.GetArchetypeChunkIterator();

                m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[10], false);
                m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[63], false);
                m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entities[64], false);

                Assert.IsTrue(iterator.MoveNext());
                Assert.AreEqual(0, iterator.CurrentArchetypeChunk.m_BatchStartEntityIndex);
                Assert.AreEqual(10, iterator.CurrentArchetypeChunk.Count);

                Assert.IsTrue(iterator.MoveNext());
                Assert.AreEqual(11, iterator.CurrentArchetypeChunk.m_BatchStartEntityIndex);
                Assert.AreEqual(52, iterator.CurrentArchetypeChunk.Count);

                Assert.IsTrue(iterator.MoveNext());
                Assert.AreEqual(65, iterator.CurrentArchetypeChunk.m_BatchStartEntityIndex);
                Assert.AreEqual(archetype.ChunkCapacity - 65, iterator.CurrentArchetypeChunk.Count);

                // new chunk
                Assert.IsTrue(iterator.MoveNext());
                Assert.AreEqual(0, iterator.CurrentArchetypeChunk.m_BatchStartEntityIndex);
                Assert.AreEqual(archetype.ChunkCapacity, iterator.CurrentArchetypeChunk.Count);

                Assert.IsFalse(iterator.MoveNext());
            }
        }

        [Test]
        public void IsEmpty_RespectsEnabledBits()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestDataEnableable));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestDataEnableable2));

            var entityA = m_Manager.CreateEntity(archetypeA);
            var entityB = m_Manager.CreateEntity(archetypeB);

            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entityA, false);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable)))
            {
                Assert.IsTrue(query.IsEmpty);
            }

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable2)))
            {
                Assert.IsFalse(query.IsEmpty);
            }

            using (var query = m_Manager.CreateEntityQuery(new EntityQueryDesc()
            {
                All = new[] {ComponentType.ReadOnly<EcsTestData>()},
                None = new[] {ComponentType.ReadOnly<EcsTestDataEnableable>()}
            }))
            {
                Assert.IsFalse(query.IsEmpty);
            }
        }

        [Test]
        public void CalculateEntityCount_RespectsEnabledBits()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestDataEnableable));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestDataEnableable2));

            var entityA = m_Manager.CreateEntity(archetypeA);
            m_Manager.CreateEntity(archetypeA, 10);
            var entityB = m_Manager.CreateEntity(archetypeB);
            m_Manager.CreateEntity(archetypeB, 10);

            m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entityA, false);

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable)))
            {
                var entityCount = query.CalculateEntityCount();
                Assert.AreEqual(10, entityCount);
            }

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestDataEnableable2)))
            {
                var entityCount = query.CalculateEntityCount();
                Assert.AreEqual(11, entityCount);
            }

            using (var query = m_Manager.CreateEntityQuery(new EntityQueryDesc()
            {
                All = new[] {ComponentType.ReadOnly<EcsTestData>()},
                None = new[] {ComponentType.ReadOnly<EcsTestDataEnableable>()}
            }))
            {
                var entityCount = query.CalculateEntityCount();
                Assert.AreEqual(12, entityCount);
            }
        }
    }
}
