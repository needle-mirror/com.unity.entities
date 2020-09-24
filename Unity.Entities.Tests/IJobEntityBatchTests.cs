using System;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
#if !NET_DOTS
using System.Text.RegularExpressions;
#endif

namespace Unity.Entities.Tests
{
    class IJobEntityBatchTests : ECSTestsFixture
    {
        struct WriteBatchIndex : IJobEntityBatch
        {
            public ComponentTypeHandle<EcsTestData> EcsTestTypeHandle;

            public void Execute(ArchetypeChunk batch, int batchIndex)
            {
                var testDataArray = batch.GetNativeArray(EcsTestTypeHandle);
                testDataArray[0] = new EcsTestData
                {
                    value = batchIndex
                };
            }
        }

        static unsafe bool IsBatchInitialized(ArchetypeChunk batch)
        {
            return batch.m_Chunk != null;
        }

        struct WriteBatchInfoToArray : IJobEntityBatch
        {
            [NativeDisableParallelForRestriction]
            public NativeArray<ArchetypeChunk> BatchInfos;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                // We expect the BatchInfos array to be uninitialized until written by this job.
                // If this fires, some other job thread has filled in this batch's info already!
                Assert.IsFalse(IsBatchInitialized(BatchInfos[batchIndex]));
                Assert.NotZero(batchInChunk.Count);

                BatchInfos[batchIndex] = batchInChunk;
            }
        }

        struct WriteBatchInfoAndEntityOffsetToArray : IJobEntityBatchWithIndex
        {
            // These arrays are indexed by batchIndex, not entityIndex, so range-checking must be disabled.
            [NativeDisableParallelForRestriction]
            public NativeArray<ArchetypeChunk> BatchInfos;
            [NativeDisableParallelForRestriction]
            public NativeArray<int> BatchFirstEntityOffsets;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex, int indexOfFirstEntityInQuery)
            {
                // We expect the input arrays to be uninitialized until written by this job.
                // If these asserts fire, some other job thread has filled in this batch's info already!
                Assert.IsFalse(IsBatchInitialized(BatchInfos[batchIndex]));
                Assert.AreEqual(-1, BatchFirstEntityOffsets[batchIndex]);
                Assert.NotZero(batchInChunk.Count);

                BatchInfos[batchIndex] = batchInChunk;
                BatchFirstEntityOffsets[batchIndex] = indexOfFirstEntityInQuery;
            }
        }

        [Test]
        public void IJobEntityBatchRun()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            var entityCount = 100;

            var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.Temp);
            var job = new WriteBatchIndex
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };

            Assert.DoesNotThrow(()=>
            {
                job.Run(query);
            });

            query.Dispose();
        }

        [Test]
        public void IJobEntityBatchRunWithoutDependency_Throws()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            var entityCount = 100;

            var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.Temp);
            var job = new WriteBatchIndex
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };

            var handle = job.Schedule(query);

            Assert.Throws<InvalidOperationException>(()=>
            {
                job.Run(query);
            });

            handle.Complete();
            query.Dispose();
        }

        [Test]
        public void IJobEntityBatch_WithoutFiltering_GeneratesExpectedBatches([Values(1, 4, 17, 100)] int batchesPerChunk)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            var entityCount = 10000;

            using (var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob))
            using (var batches = new NativeArray<ArchetypeChunk>(archetype.ChunkCount * batchesPerChunk, Allocator.TempJob))
            {
                for (var i = 0; i < entityCount; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new EcsTestData {value = -1});
                }

                var job = new WriteBatchInfoToArray
                {
                    BatchInfos = batches,
                };
                job.ScheduleParallel(query, batchesPerChunk).Complete();

                var entityTypeHandle = m_Manager.GetEntityTypeHandle();
                int markedEntityCount = 0;
                for (int batchIndex = 0; batchIndex < batches.Length; ++batchIndex)
                {
                    var batch = batches[batchIndex];
                    if (!IsBatchInitialized(batch))
                        continue; // this is fine; empty/filtered batches will be skipped and left uninitialized.
                    Assert.Greater(batch.Count, 0); // empty batches should not have been Execute()ed
                    Assert.LessOrEqual(batch.Count, (batch.ChunkEntityCount / batchesPerChunk) + 1);
                    var batchEntities = batch.GetNativeArray(entityTypeHandle);
                    for (int i = 0; i < batchEntities.Length; ++i)
                    {
                        Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(batchEntities[i]).value);
                        m_Manager.SetComponentData(batchEntities[i], new EcsTestData {value = 1});
                        markedEntityCount++;
                    }
                }

                Assert.AreEqual(entities.Length, markedEntityCount);
                for (int i = 0; i < entities.Length; ++i)
                {
                    Assert.AreEqual(1, m_Manager.GetComponentData<EcsTestData>(entities[i]).value);
                }
            }

            query.Dispose();
        }

        [Test]
        public void IJobEntityBatch_WithFiltering_GeneratesExpectedBatches([Values(1, 4, 17, 100)] int batchesPerChunk)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp));
            query.SetSharedComponentFilter(new EcsTestSharedComp {value = 17});

            var entityCount = 10000;

            using (var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob))
            {
                for (var i = 0; i < entityCount; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new EcsTestData {value = -1});
                    if ((i % 2) == 0)
                    {
                        m_Manager.SetSharedComponentData(entities[i], new EcsTestSharedComp {value = 17});
                    }
                }

                using (var batches = new NativeArray<ArchetypeChunk>(archetype.ChunkCount * batchesPerChunk, Allocator.TempJob))
                {
                    var job = new WriteBatchInfoToArray
                    {
                        BatchInfos = batches,
                    };
                    job.ScheduleParallel(query, batchesPerChunk).Complete();

                    var entityTypeHandle = m_Manager.GetEntityTypeHandle();
                    int markedEntityCount = 0;
                    for (int batchIndex = 0; batchIndex < batches.Length; ++batchIndex)
                    {
                        var batch = batches[batchIndex];
                        if (!IsBatchInitialized(batch))
                            continue; // this is fine; empty/filtered batches will be skipped and left uninitialized.
                        Assert.Greater(batch.Count, 0); // empty batches should not have been Execute()ed.
                        Assert.LessOrEqual(batch.Count, (batch.ChunkEntityCount / batchesPerChunk) + 1);
                        var batchEntities = batch.GetNativeArray(entityTypeHandle);
                        for (int i = 0; i < batchEntities.Length; ++i)
                        {
                            Assert.AreEqual(17, m_Manager.GetSharedComponentData<EcsTestSharedComp>(batchEntities[i]).value);
                            Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(batchEntities[i]).value);
                            m_Manager.SetComponentData(batchEntities[i], new EcsTestData {value = 1});
                            markedEntityCount++;
                        }
                    }
                    Assert.AreEqual(query.CalculateEntityCount(), markedEntityCount);
                }

                for (int i = 0; i < entities.Length; ++i)
                {
                    int testValue = m_Manager.GetComponentData<EcsTestData>(entities[i]).value;
                    if (m_Manager.GetSharedComponentData<EcsTestSharedComp>(entities[i]).value == 17)
                    {
                        Assert.AreEqual(1, testValue);
                    }
                    else
                    {
                        Assert.AreEqual(-1, testValue);
                    }
                }
            }

            query.Dispose();
        }

        struct WriteEntityIndex : IJobEntityBatchWithIndex
        {
            public ComponentTypeHandle<EcsTestData> EcsTestTypeHandle;

            public void Execute(ArchetypeChunk batch, int batchIndex, int indexOfFirstEntityInQuery)
            {
                var testDataArray = batch.GetNativeArray(EcsTestTypeHandle);
                testDataArray[0] = new EcsTestData
                {
                    value = indexOfFirstEntityInQuery
                };
            }
        }

        [Test]
        public void IJobEntityBatchIndex_WithoutFiltering_GeneratesExpectedBatches([Values(1, 4, 17, 100)] int batchesPerChunk)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            var entityCount = 10000;

            using (var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob))
            using (var batches = new NativeArray<ArchetypeChunk>(archetype.ChunkCount * batchesPerChunk, Allocator.TempJob))
            {
                for (var i = 0; i < entityCount; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new EcsTestData {value = -1});
                }

                var batchEntityOffsets = new NativeArray<int>(batches.Length, Allocator.TempJob);
                for (int i = 0; i < batchEntityOffsets.Length; ++i)
                {
                    batchEntityOffsets[i] = -1;
                }

                var job = new WriteBatchInfoAndEntityOffsetToArray
                {
                    BatchInfos = batches,
                    BatchFirstEntityOffsets = batchEntityOffsets,
                };
                job.ScheduleParallel(query, batchesPerChunk).Complete();

                using (var matchingEntities = query.ToEntityArray(Allocator.TempJob))
                {
                    var entityTypeHandle = m_Manager.GetEntityTypeHandle();
                    int markedEntityCount = 0;
                    for (int batchIndex = 0; batchIndex < batches.Length; ++batchIndex)
                    {
                        var batch = batches[batchIndex];
                        if (!IsBatchInitialized(batch))
                            continue; // this is fine; empty/filtered batches will be skipped and left uninitialized.
                        Assert.Greater(batch.Count, 0); // empty batches should not have been Execute()ed
                        Assert.LessOrEqual(batch.Count, (batch.ChunkEntityCount / batchesPerChunk) + 1);
                        var batchEntities = batch.GetNativeArray(entityTypeHandle);
                        int batchFirstEntityIndex = batchEntityOffsets[batchIndex];
                        Assert.AreNotEqual(-1, batchFirstEntityIndex);
                        Assert.AreEqual(matchingEntities[batchFirstEntityIndex], batchEntities[0]);
                        for (int i = 0; i < batchEntities.Length; ++i)
                        {
                            Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(batchEntities[i]).value);
                            m_Manager.SetComponentData(batchEntities[i], new EcsTestData {value = 1});
                            markedEntityCount++;
                        }
                    }
                    Assert.AreEqual(entities.Length, markedEntityCount);
                }
                batchEntityOffsets.Dispose();

                for (int i = 0; i < entities.Length; ++i)
                {
                    Assert.AreEqual(1, m_Manager.GetComponentData<EcsTestData>(entities[i]).value);
                }

            }

            query.Dispose();
        }

        [Test]
        public void IJobEntityBatchWithIndex_WithFiltering_GeneratesExpectedBatches([Values(1, 4, 17, 100)] int batchesPerChunk)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp));
            query.SetSharedComponentFilter(new EcsTestSharedComp {value = 17});

            var entityCount = 10000;

            using (var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob))
            {
                for (var i = 0; i < entityCount; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new EcsTestData {value = -1});
                    if ((i % 2) == 0)
                    {
                        m_Manager.SetSharedComponentData(entities[i], new EcsTestSharedComp {value = 17});
                    }
                }

                using (var batches = new NativeArray<ArchetypeChunk>(archetype.ChunkCount * batchesPerChunk, Allocator.TempJob))
                {
                    var batchEntityOffsets = new NativeArray<int>(batches.Length, Allocator.TempJob);
                    for (int i = 0; i < batchEntityOffsets.Length; ++i)
                    {
                        batchEntityOffsets[i] = -1;
                    }

                    var job = new WriteBatchInfoAndEntityOffsetToArray
                    {
                        BatchInfos = batches,
                        BatchFirstEntityOffsets = batchEntityOffsets,
                    };
                    job.ScheduleParallel(query, batchesPerChunk).Complete();

                    using (var matchingEntities = query.ToEntityArray(Allocator.TempJob))
                    {
                        var entityTypeHandle = m_Manager.GetEntityTypeHandle();
                        int markedEntityCount = 0;
                        for (int batchIndex = 0; batchIndex < batches.Length; ++batchIndex)
                        {
                            var batch = batches[batchIndex];
                            if (!IsBatchInitialized(batch))
                                continue; // this is fine; empty/filtered batches will be skipped and left uninitialized.
                            Assert.Greater(batch.Count, 0); // empty batches should not have been Execute()ed
                            Assert.LessOrEqual(batch.Count, (batch.ChunkEntityCount / batchesPerChunk) + 1);
                            var batchEntities = batch.GetNativeArray(entityTypeHandle);
                            int batchFirstEntityIndex = batchEntityOffsets[batchIndex];
                            Assert.AreNotEqual(-1, batchFirstEntityIndex);
                            Assert.IsTrue(0 <= batchFirstEntityIndex &&
                                          batchFirstEntityIndex < matchingEntities.Length);
                            Assert.AreEqual(matchingEntities[batchFirstEntityIndex], batchEntities[0]);
                            for (int i = 0; i < batchEntities.Length; ++i)
                            {
                                Assert.AreEqual(17,
                                    m_Manager.GetSharedComponentData<EcsTestSharedComp>(batchEntities[i]).value);
                                Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(batchEntities[i]).value);
                                m_Manager.SetComponentData(batchEntities[i], new EcsTestData {value = 1});
                                markedEntityCount++;
                            }
                        }
                        Assert.AreEqual(query.CalculateEntityCount(), markedEntityCount);
                    }
                    batchEntityOffsets.Dispose();
                }

                for (int i = 0; i < entities.Length; ++i)
                {
                    int testValue = m_Manager.GetComponentData<EcsTestData>(entities[i]).value;
                    if (m_Manager.GetSharedComponentData<EcsTestSharedComp>(entities[i]).value == 17)
                    {
                        Assert.AreEqual(1, testValue);
                    }
                    else
                    {
                        Assert.AreEqual(-1, testValue);
                    }
                }
            }

            query.Dispose();
        }

        [Test]
        public void IJobEntityBatchWithIndex_Run()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            var entityCount = 100;

            var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.Temp);

            Assert.DoesNotThrow(()=>
            {
                var job = new WriteEntityIndex
                {
                    EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                };
                job.Run(query);
            });

            query.Dispose();
        }

        [Test]
        public void IJobEntityBatchRunWithIndexWithoutDependency_Throws()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            var entityCount = 100;

            var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.Temp);
            var job = new WriteEntityIndex
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };

            var handle = job.Schedule(query);

            Assert.Throws<InvalidOperationException>(()=>
            {
                job.Run(query);
            });

            handle.Complete();
            query.Dispose();
        }

        struct WriteToArray : IJobEntityBatch
        {
            public NativeArray<int> MyArray;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                for (int i = 0; i < MyArray.Length; i++)
                {
                    MyArray[i] = batchIndex;
                }
            }
        }

#if !NET_DOTS // DOTS Runtimes does not support regex
        [Test]
        public void ParallelArrayWriteTriggersSafetySystem()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entitiesA = new NativeArray<Entity>(archetypeA.ChunkCapacity, Allocator.Temp);
            m_Manager.CreateEntity(archetypeA, entitiesA);
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            using (var local = new NativeArray<int>(archetypeA.ChunkCapacity * 2, Allocator.TempJob))
            {
                LogAssert.Expect(LogType.Exception, new Regex("IndexOutOfRangeException: *"));

                new WriteToArray
                {
                    MyArray = local
                }.ScheduleParallel(query).Complete();
            }
        }
#endif

        [Test]
        public void SingleArrayWriteDoesNotTriggerSafetySystem()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entitiesA = new NativeArray<Entity>(archetypeA.ChunkCapacity, Allocator.Temp);
            m_Manager.CreateEntity(archetypeA, entitiesA);
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            using (var local = new NativeArray<int>(archetypeA.ChunkCapacity * 2, Allocator.TempJob))
            {
                new WriteToArray
                {
                    MyArray = local
                }.Schedule(query).Complete();
            }
        }

        struct WriteToArrayWithIndex : IJobEntityBatchWithIndex
        {
            public NativeArray<int> MyArray;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex, int indexOfFirstEntityInQuery)
            {
                for (int i = 0; i < MyArray.Length; i++)
                {
                    MyArray[i] = batchIndex + indexOfFirstEntityInQuery;
                }
            }
        }

#if !NET_DOTS // DOTS Runtimes does not support regex
        [Test]
        public void ParallelArrayWriteTriggersSafetySystem_WithIndex()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entitiesA = new NativeArray<Entity>(archetypeA.ChunkCapacity, Allocator.Temp);
            m_Manager.CreateEntity(archetypeA, entitiesA);
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            using (var local = new NativeArray<int>(archetypeA.ChunkCapacity * 2, Allocator.TempJob))
            {
                LogAssert.Expect(LogType.Exception, new Regex("IndexOutOfRangeException: *"));

                new WriteToArrayWithIndex
                {
                    MyArray = local
                }.ScheduleParallel(query).Complete();
            }
        }
#endif

        [Test]
        public void SingleArrayWriteDoesNotTriggerSafetySystem_WithIndex()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entitiesA = new NativeArray<Entity>(archetypeA.ChunkCapacity, Allocator.Temp);
            m_Manager.CreateEntity(archetypeA, entitiesA);
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            using (var local = new NativeArray<int>(archetypeA.ChunkCapacity * 2, Allocator.TempJob))
            {
                new WriteToArrayWithIndex
                {
                    MyArray = local
                }.Schedule(query).Complete();
            }
        }
    }
}
