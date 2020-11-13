using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;
#if !NET_DOTS
using System.Text.RegularExpressions;
#endif

namespace Unity.Entities.Tests
{
    class IJobEntityBatchTests : ECSTestsFixture
    {
        [BurstCompile(CompileSynchronously = true)]
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

        // Not Burst compiling since we are Asserting in this job
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

        // Not Burst compiling since we are Asserting in this job
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
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                var entityCount = 100;
                m_Manager.CreateEntity(archetype, entityCount);
                var job = new WriteBatchIndex
                {
                    EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                };
                Assert.DoesNotThrow(() => { job.Run(query); });
            }
        }

        [Test]
        public void IJobEntityBatchRunWithoutDependency_Throws()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                var entityCount = 100;
                m_Manager.CreateEntity(archetype, entityCount);
                var job = new WriteBatchIndex
                {
                    EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                };
                var handle = job.Schedule(query);
                Assert.Throws<InvalidOperationException>(() => { job.Run(query); });
                handle.Complete();
            }
        }

        [Test]
        public void IJobEntityBatch_WithoutFiltering_GeneratesExpectedBatches([Values(1, 4, 17, 100)] int batchesPerChunk)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entityCount = 10000;
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
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

        [BurstCompile(CompileSynchronously = true)]
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
        public void IJobEntityBatchWithIndex_WithoutFiltering_GeneratesExpectedBatches([Values(1, 4, 17, 100)] int batchesPerChunk)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entityCount = 10000;
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
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
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                var entityCount = 100;
                m_Manager.CreateEntity(archetype, entityCount);
                Assert.DoesNotThrow(() =>
                {
                    var job = new WriteEntityIndex
                    {
                        EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                    };
                    job.Run(query);
                });
            }
        }

        [Test]
        public void IJobEntityBatchRunWithIndexWithoutDependency_Throws()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                var entityCount = 100;
                m_Manager.CreateEntity(archetype, entityCount);
                var job = new WriteEntityIndex
                {
                    EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                };
                var handle = job.Schedule(query);
                Assert.Throws<InvalidOperationException>(() => { job.Run(query); });
                handle.Complete();
            }
        }

        [BurstCompile(CompileSynchronously = true)]
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
            using(var entitiesA = new NativeArray<Entity>(archetypeA.ChunkCapacity, Allocator.TempJob))
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var local = new NativeArray<int>(archetypeA.ChunkCapacity * 2, Allocator.TempJob))
            {
                m_Manager.CreateEntity(archetypeA, entitiesA);
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
            using(var entitiesA = new NativeArray<Entity>(archetypeA.ChunkCapacity, Allocator.TempJob))
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var local = new NativeArray<int>(archetypeA.ChunkCapacity * 2, Allocator.TempJob))
            {
                m_Manager.CreateEntity(archetypeA, entitiesA);
                new WriteToArray
                {
                    MyArray = local
                }.Schedule(query).Complete();
            }
        }

        [BurstCompile(CompileSynchronously = true)]
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
            using(var entitiesA = new NativeArray<Entity>(archetypeA.ChunkCapacity, Allocator.TempJob))
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var local = new NativeArray<int>(archetypeA.ChunkCapacity * 2, Allocator.TempJob))
            {
                m_Manager.CreateEntity(archetypeA, entitiesA);
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
            using(var entitiesA = new NativeArray<Entity>(archetypeA.ChunkCapacity, Allocator.TempJob))
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var local = new NativeArray<int>(archetypeA.ChunkCapacity * 2, Allocator.TempJob))
            {
                m_Manager.CreateEntity(archetypeA, entitiesA);
                new WriteToArrayWithIndex
                {
                    MyArray = local
                }.Schedule(query).Complete();
            }
        }

        [Test]
        public unsafe void IJobEntityBatch_ScheduleWithEntityList()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var allEntitiesA = m_Manager.CreateEntity(archetypeA, 100, Allocator.TempJob))
            using (var allEntitiesB = m_Manager.CreateEntity(archetypeB, 100, Allocator.TempJob))
            {

                // One batch, all matching
                {
                    var entities = new NativeArray<Entity>(10, Allocator.TempJob);

                    for (int i = 0; i < 10; ++i)
                    {
                        entities[i] = allEntitiesA[i];
                    }

                    for (int i = 0; i < allEntitiesA.Length; ++i)
                    {
                        m_Manager.SetComponentData(allEntitiesA[i], new EcsTestData {value = -1});
                    }

                    new WriteBatchIndex {EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)}
                        .Schedule(query, entities).Complete();

                    Assert.AreEqual(0, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[0]).value);
                    for (int i = 1; i < 100; ++i)
                        Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);

                    entities.Dispose();
                }

                // All separate batches, all matching
                {
                    var entities = new NativeArray<Entity>(10, Allocator.TempJob);
                    for (int i = 0; i < 10; ++i)
                    {
                        entities[i] = allEntitiesA[i * 10];
                    }

                    for (int i = 0; i < allEntitiesA.Length; ++i)
                    {
                        m_Manager.SetComponentData(allEntitiesA[i], new EcsTestData {value = -1});
                    }

                    new WriteBatchIndex {EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)}
                        .Schedule(query, entities).Complete();

                    for (int i = 0; i < 100; i++)
                    {
                        var div = i / 10;
                        var mod = i % 10;
                        if (mod == 0)
                            Assert.AreEqual(div, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                        else
                            Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                    }

                    entities.Dispose();
                }

                // Mixed batches, mixed matching archetype
                {
                    var entities = new NativeArray<Entity>(100, Allocator.TempJob);
                    for (int i = 0; i < 100; ++i)
                    {
                        if (i % 5 == 0)
                            entities[i] = allEntitiesB[i];
                        else
                            entities[i] = allEntitiesA[i];
                    }

                    for (int i = 0; i < allEntitiesA.Length; ++i)
                    {
                        m_Manager.SetComponentData(allEntitiesA[i], new EcsTestData {value = -1});
                    }

                    new WriteBatchIndex {EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)}
                        .Schedule(query, entities).Complete();

                    for (int i = 0; i < 100; ++i)
                    {
                        var div = i / 5;
                        var mod = i % 5;
                        if (mod == 1)
                            Assert.AreEqual(div, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                        else
                            Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                    }

                    entities.Dispose();
                }
            }
        }

        [Test]
        public unsafe void IJobEntityBatchWithIndex_ScheduleWithEntityList()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));

            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var allEntitiesA = m_Manager.CreateEntity(archetypeA, 100, Allocator.TempJob))
            using (var allEntitiesB = m_Manager.CreateEntity(archetypeB, 100, Allocator.TempJob))
            {
                // One batch, all matching
                {
                    var entities = new NativeArray<Entity>(10, Allocator.TempJob);

                    for (int i = 0; i < 10; ++i)
                    {
                        entities[i] = allEntitiesA[i];
                    }

                    for (int i = 0; i < allEntitiesA.Length; ++i)
                    {
                        m_Manager.SetComponentData(allEntitiesA[i], new EcsTestData {value = -1});
                    }

                    new WriteEntityIndex {EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)}
                        .Schedule(query, entities).Complete();

                    Assert.AreEqual(0, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[0]).value);
                    for (int i = 1; i < 100; ++i)
                        Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);

                    entities.Dispose();
                }

                // All separate batches, all matching
                {
                    var entities = new NativeArray<Entity>(10, Allocator.TempJob);
                    for (int i = 0; i < 10; ++i)
                    {
                        entities[i] = allEntitiesA[i * 10];
                    }

                    for (int i = 0; i < allEntitiesA.Length; ++i)
                    {
                        m_Manager.SetComponentData(allEntitiesA[i], new EcsTestData {value = -1});
                    }

                    new WriteEntityIndex {EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)}
                        .Schedule(query, entities).Complete();

                    for (int i = 0; i < 100; i++)
                    {
                        var div = i / 10;
                        var mod = i % 10;
                        if (mod == 0)
                            Assert.AreEqual(div, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                        else
                            Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                    }

                    entities.Dispose();
                }

                // Mixed batches, mixed matching archetype
                {
                    var entities = new NativeArray<Entity>(100, Allocator.TempJob);
                    for (int i = 0; i < 100; ++i)
                    {
                        if (i % 5 == 0)
                            entities[i] = allEntitiesB[i];
                        else
                            entities[i] = allEntitiesA[i];
                    }

                    for (int i = 0; i < allEntitiesA.Length; ++i)
                    {
                        m_Manager.SetComponentData(allEntitiesA[i], new EcsTestData {value = -1});
                    }

                    new WriteEntityIndex {EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)}
                        .Schedule(query, entities).Complete();

                    for (int i = 0; i < 100; ++i)
                    {
                        var div = i / 5;
                        var mod = i % 5;
                        if (mod == 1)
                            Assert.AreEqual(div * 4, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                        else
                            Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                    }

                    entities.Dispose();
                }
            }
        }

        [Test]
        public void IJobEntityBatch_GeneratesExpectedBatches_WithEntityList()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));
            var archetypeC = m_Manager.CreateArchetype(typeof(EcsTestData3));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using(var entitiesA = m_Manager.CreateEntity(archetypeA, 100, Allocator.TempJob))
            using(var entitiesB = m_Manager.CreateEntity(archetypeB, 100, Allocator.TempJob))
            using(var entitiesC = m_Manager.CreateEntity(archetypeC, 100, Allocator.TempJob))
            {
                for (int i = 0; i < entitiesA.Length; ++i)
                {
                    m_Manager.SetComponentData(entitiesA[i], new EcsTestData(i));
                }
                // AAAAABBBBCAAAAABBBBC...
                var limitEntities = new NativeArray<Entity>(100, Allocator.TempJob);
                for (int i = 0; i < 100; ++i)
                {
                    var mod = i % 10;
                    if (mod < 5)
                        limitEntities[i] = entitiesA[i];
                    else if (mod < 9)
                        limitEntities[i] = entitiesB[i];
                    else
                        limitEntities[i] = entitiesC[i];
                }

                var batches = new NativeArray<ArchetypeChunk>(10, Allocator.TempJob);
                var job = new WriteBatchInfoToArray
                {
                    BatchInfos = batches,
                };
                job.ScheduleParallel(query, limitEntities).Complete();

                using (var matchingEntities = query.ToEntityArray(limitEntities, Allocator.TempJob))
                {
                    var entityTypeHandle = m_Manager.GetEntityTypeHandle();
                    int markedEntityCount = 0;
                    for (int batchIndex = 0; batchIndex < batches.Length; ++batchIndex)
                    {
                        var batch = batches[batchIndex];
                        Assert.IsTrue(IsBatchInitialized(batch));

                        Assert.AreEqual(5, batch.Count);

                        var batchEntities = batch.GetNativeArray(entityTypeHandle);
                        for (int i = 0; i < batchEntities.Length; ++i)
                        {
                            Assert.AreEqual(batchIndex * 10 + i, m_Manager.GetComponentData<EcsTestData>(batchEntities[i]).value);
                            Assert.AreEqual(matchingEntities[markedEntityCount], batchEntities[i]);
                            markedEntityCount++;
                        }
                    }
                    Assert.AreEqual(query.CalculateEntityCount(limitEntities), markedEntityCount);
                }

                limitEntities.Dispose();
                batches.Dispose();
            }
        }

        [Test]
        public void IJobEntityBatch_GeneratesExpectedBatches_WithEntityList_WithFiltering()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2), typeof(EcsTestSharedComp));
            var archetypeC = m_Manager.CreateArchetype(typeof(EcsTestData3), typeof(EcsTestSharedComp));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp)))
            using(var entitiesA = m_Manager.CreateEntity(archetypeA, 100, Allocator.TempJob))
            using(var entitiesB = m_Manager.CreateEntity(archetypeB, 100, Allocator.TempJob))
            using(var entitiesC = m_Manager.CreateEntity(archetypeC, 100, Allocator.TempJob))
            {
                for (int i = 0; i < entitiesA.Length; ++i)
                {
                    m_Manager.SetComponentData(entitiesA[i], new EcsTestData(i));

                    var mod = i % 5;
                    var val = mod < 3 ? 17 : 7;
                    m_Manager.SetSharedComponentData(entitiesA[i], new EcsTestSharedComp(val));
                }

                query.SetSharedComponentFilter(new EcsTestSharedComp(17));

                // AAAAABBBBCAAAAABBBBC...
                    // With filtering its A1A1A1A2A2BBBBCA1A1A1A2A2BBBBC...
                var limitEntities = new NativeArray<Entity>(100, Allocator.TempJob);
                for (int i = 0; i < 100; ++i)
                {
                    var mod = i % 10;
                    if (mod < 5)
                        limitEntities[i] = entitiesA[i];
                    else if (mod < 9)
                        limitEntities[i] = entitiesB[i];
                    else
                        limitEntities[i] = entitiesC[i];
                }

                var batches = new NativeArray<ArchetypeChunk>(20, Allocator.TempJob);
                var job = new WriteBatchInfoToArray
                {
                    BatchInfos = batches,
                };
                job.ScheduleParallel(query, limitEntities).Complete();

                using (var matchingEntities = query.ToEntityArray(limitEntities, Allocator.TempJob))
                {
                    var entityTypeHandle = m_Manager.GetEntityTypeHandle();
                    int markedEntityCount = 0;
                    int validBatchCount = 0;
                    for (int batchIndex = 0; batchIndex < batches.Length; ++batchIndex)
                    {
                        var batch = batches[batchIndex];
                        if (!IsBatchInitialized(batch))
                            continue; // this is fine; empty/filtered batches will be skipped and left uninitialized.

                        Assert.AreEqual(3, batch.Count);

                        var batchEntities = batch.GetNativeArray(entityTypeHandle);
                        for (int i = 0; i < batchEntities.Length; ++i)
                        {
                            Assert.AreEqual(validBatchCount * 10 + i, m_Manager.GetComponentData<EcsTestData>(batchEntities[i]).value);
                            Assert.AreEqual(matchingEntities[markedEntityCount], batchEntities[i]);
                            markedEntityCount++;
                        }

                        validBatchCount++;
                    }
                    Assert.AreEqual(query.CalculateEntityCount(limitEntities), markedEntityCount);
                }

                limitEntities.Dispose();
                batches.Dispose();
            }
        }

        // Not Burst compiling since we are Asserting in this job
        struct CheckBatchIndices : IJobEntityBatch
        {
            public ComponentTypeHandle<EcsTestData> EcsTestDataTypeHandle;
            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var testData = batchInChunk.GetNativeArray(EcsTestDataTypeHandle);
                Assert.AreEqual(batchIndex, testData[0].value);
            }
        }
        struct CheckBatchAndFirstEntityIndices : IJobEntityBatchWithIndex
        {
            public ComponentTypeHandle<EcsTestData> EcsTestDataTypeHandle;
            public void Execute(ArchetypeChunk batchInChunk, int batchIndex, int indexOfFirstEntityInQuery)
            {
                var testData = batchInChunk.GetNativeArray(EcsTestDataTypeHandle);
                Assert.AreEqual(batchIndex, testData[0].value);
                Assert.AreEqual(indexOfFirstEntityInQuery, testData[0].value);
            }
        }

        public enum ScheduleMode
        {
            Parallel, Single, Run, RunWithoutJobs
        }

        [Test]
        public void IJobEntityBatch_WithNoBatching_HasCorrectIndices(
            [Values(ScheduleMode.Parallel, ScheduleMode.Single, ScheduleMode.Run, ScheduleMode.RunWithoutJobs)] ScheduleMode scheduleMode)
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp)))
            using (var entities = m_Manager.CreateEntity(archetypeA, 100, Allocator.TempJob))
            {
                var val = 0;
                foreach (var entity in entities)
                {
                    m_Manager.SetComponentData(entity, new EcsTestData() {value = val});
                    m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp() {value = val});
                    val++;
                }

                var job = new CheckBatchIndices {EcsTestDataTypeHandle = EmptySystem.GetComponentTypeHandle<EcsTestData>() };
                var jobWithIndex = new CheckBatchAndFirstEntityIndices {EcsTestDataTypeHandle = EmptySystem.GetComponentTypeHandle<EcsTestData>() };

                switch (scheduleMode)
                {
                    case ScheduleMode.Parallel:
                        job.ScheduleParallel(query).Complete();
                        jobWithIndex.ScheduleParallel(query).Complete();
                        break;
                    case ScheduleMode.Single:
                        job.Schedule(query).Complete();
                        jobWithIndex.Schedule(query).Complete();
                        break;
                    case ScheduleMode.Run:
                        job.Run(query);
                        jobWithIndex.Run(query);
                        break;
                    case ScheduleMode.RunWithoutJobs:
                        JobEntityBatchExtensions.RunWithoutJobs(ref job, query);
                        JobEntityBatchIndexExtensions.RunWithoutJobs(ref jobWithIndex, query);
                        break;
                }
            }
        }
    }
}
