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
        struct WriteBatchId : IJobEntityBatch
        {
            public ComponentTypeHandle<EcsTestData> EcsTestTypeHandle;

            public void Execute(ArchetypeChunk batch, int batchId)
            {
                var testDataArray = batch.GetNativeArray(EcsTestTypeHandle);
                testDataArray[0] = new EcsTestData
                {
                    value = batchId
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

            public void Execute(ArchetypeChunk batchInChunk, int batchId)
            {
                // We expect the BatchInfos array to be uninitialized until written by this job.
                // If this fires, some other job thread has filled in this batch's info already!
                // TODO https://unity3d.atlassian.net/browse/DOTS-4591: this will break when enabled bits are enabled. batchId is no longer guaranteed to be tightly-packed and zero-based.
                // a new approach will be needed for this test.
                Assert.IsFalse(IsBatchInitialized(BatchInfos[batchId]));
                Assert.NotZero(batchInChunk.Count);

                BatchInfos[batchId] = batchInChunk;
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
                var job = new WriteBatchId
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
                var job = new WriteBatchId
                {
                    EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                };
                var handle = job.Schedule(query);
                Assert.Throws<InvalidOperationException>(() => { job.Run(query); });
                handle.Complete();
            }
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

        public enum ScheduleMode
        {
            Parallel, Single, Run, RunWithoutJobs
        }

// TODO(https://unity3d.atlassian.net/browse/DOTSR-2746): [TestCase(args)] is not supported in the portable test runner
#if !UNITY_PORTABLE_TEST_RUNNER
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Chunk)]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Entity)]
        [TestCase(ScheduleMode.Single)]
        [TestCase(ScheduleMode.Run)]
        [TestCase(ScheduleMode.RunWithoutJobs)]
        public void IJobEntityBatch_GeneratesExpectedBatches_WithoutFiltering(ScheduleMode mode,
            ScheduleGranularity granularity = ScheduleGranularity.Chunk)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entityCount = 10000;
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator))
            using (var batches = CollectionHelper.CreateNativeArray<ArchetypeChunk, RewindableAllocator>(
                (granularity == ScheduleGranularity.Chunk) ? archetype.ChunkCount : entityCount, ref World.UpdateAllocator))
            {
                for (var i = 0; i < entityCount; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new EcsTestData {value = -1});
                }

                var job = new WriteBatchInfoToArray
                {
                    BatchInfos = batches,
                };
                if (mode == ScheduleMode.Parallel)
                {
                    if (granularity == ScheduleGranularity.Chunk)
                        job.ScheduleParallel(query).Complete();
                    else
                        job.ScheduleParallel(query, granularity, default).Complete();
                }
                else if (mode == ScheduleMode.Single)
                    job.Schedule(query).Complete();
                else if (mode == ScheduleMode.Run)
                    job.Run(query);
                else if (mode == ScheduleMode.RunWithoutJobs)
                    JobEntityBatchExtensions.RunWithoutJobs(ref job, query);

                var entityTypeHandle = m_Manager.GetEntityTypeHandle();
                int markedEntityCount = 0;
                for (int batchIndex = 0; batchIndex < batches.Length; ++batchIndex)
                {
                    var batch = batches[batchIndex];
                    if (!IsBatchInitialized(batch))
                        continue; // this is fine; empty/filtered batches will be skipped and left uninitialized.
                    if (granularity == ScheduleGranularity.Entity)
                        Assert.AreEqual(1, batch.Count);
                    else
                    {
                        Assert.Greater(batch.Count, 0); // empty batches should not have been Execute()ed
                        Assert.AreEqual(batch.ChunkEntityCount, batch.Count);
                    }
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

        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Chunk)]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Entity)]
        [TestCase(ScheduleMode.Single)]
        [TestCase(ScheduleMode.Run)]
        [TestCase(ScheduleMode.RunWithoutJobs)]
        public void IJobEntityBatch_GeneratesExpectedBatches_WithFiltering(ScheduleMode mode,
            ScheduleGranularity granularity = ScheduleGranularity.Chunk)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp));
            query.SetSharedComponentFilter(new EcsTestSharedComp {value = 17});

            var entityCount = 10000;
            using (var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator))
            {
                for (var i = 0; i < entityCount; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new EcsTestData {value = -1});
                    if ((i % 2) == 0)
                    {
                        m_Manager.SetSharedComponentData(entities[i], new EcsTestSharedComp {value = 17});
                    }
                }

                var maxBatchCount = (granularity == ScheduleGranularity.Chunk) ? archetype.ChunkCount : entityCount;
                using (var batches = CollectionHelper.CreateNativeArray<ArchetypeChunk, RewindableAllocator>(maxBatchCount, ref World.UpdateAllocator))
                {
                    var job = new WriteBatchInfoToArray
                    {
                        BatchInfos = batches,
                    };
                    if (mode == ScheduleMode.Parallel)
                    {
                        if (granularity == ScheduleGranularity.Chunk)
                            job.ScheduleParallel(query).Complete();
                        else
                            job.ScheduleParallel(query, granularity, default).Complete();
                    }
                    else if (mode == ScheduleMode.Single)
                        job.Schedule(query).Complete();
                    else if (mode == ScheduleMode.Run)
                        job.Run(query);
                    else if (mode == ScheduleMode.RunWithoutJobs)
                        JobEntityBatchExtensions.RunWithoutJobs(ref job, query);

                    var entityTypeHandle = m_Manager.GetEntityTypeHandle();
                    int markedEntityCount = 0;
                    for (int batchIndex = 0; batchIndex < batches.Length; ++batchIndex)
                    {
                        var batch = batches[batchIndex];
                        if (!IsBatchInitialized(batch))
                            continue; // this is fine; empty/filtered batches will be skipped and left uninitialized.
                        if (granularity == ScheduleGranularity.Entity)
                            Assert.AreEqual(1, batch.Count);
                        else
                        {
                            Assert.Greater(batch.Count, 0); // empty batches should not have been Execute()ed
                            Assert.AreEqual(batch.ChunkEntityCount, batch.Count);
                        }
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

        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Chunk)]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Entity)]
        [TestCase(ScheduleMode.Single)]
        [TestCase(ScheduleMode.Run)]
        [TestCase(ScheduleMode.RunWithoutJobs)]
        public void IJobEntityBatchWithIndex_GeneratesExpectedBatches_WithoutFiltering(ScheduleMode mode,
            ScheduleGranularity granularity = ScheduleGranularity.Chunk)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entityCount = 10000;
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator))
            using (var batches = CollectionHelper.CreateNativeArray<ArchetypeChunk, RewindableAllocator>(
                (granularity == ScheduleGranularity.Chunk) ? archetype.ChunkCount : entityCount, ref World.UpdateAllocator))
            {
                for (var i = 0; i < entityCount; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new EcsTestData {value = -1});
                }

                var batchEntityOffsets = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(batches.Length, ref World.UpdateAllocator);
                for (int i = 0; i < batchEntityOffsets.Length; ++i)
                {
                    batchEntityOffsets[i] = -1;
                }

                var job = new WriteBatchInfoAndEntityOffsetToArray
                {
                    BatchInfos = batches,
                    BatchFirstEntityOffsets = batchEntityOffsets,
                };
                if (mode == ScheduleMode.Parallel)
                {
                    if (granularity == ScheduleGranularity.Chunk)
                        job.ScheduleParallel(query).Complete();
                    else
                        job.ScheduleParallel(query, granularity, default).Complete();
                }
                else if (mode == ScheduleMode.Single)
                    job.Schedule(query).Complete();
                else if (mode == ScheduleMode.Run)
                    job.Run(query);
                else if (mode == ScheduleMode.RunWithoutJobs)
                    JobEntityBatchIndexExtensions.RunWithoutJobs(ref job, query);

                using (var matchingEntities = query.ToEntityArray(World.UpdateAllocator.ToAllocator))
                {
                    var entityTypeHandle = m_Manager.GetEntityTypeHandle();
                    int markedEntityCount = 0;
                    for (int batchIndex = 0; batchIndex < batches.Length; ++batchIndex)
                    {
                        var batch = batches[batchIndex];
                        if (!IsBatchInitialized(batch))
                            continue; // this is fine; empty/filtered batches will be skipped and left uninitialized.
                        if (granularity == ScheduleGranularity.Entity)
                            Assert.AreEqual(1, batch.Count);
                        else
                        {
                            Assert.Greater(batch.Count, 0); // empty batches should not have been Execute()ed
                            Assert.AreEqual(batch.ChunkEntityCount, batch.Count);
                        }

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

                for (int i = 0; i < entities.Length; ++i)
                {
                    Assert.AreEqual(1, m_Manager.GetComponentData<EcsTestData>(entities[i]).value);
                }
            }
        }

        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Chunk)]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Entity)]
        [TestCase(ScheduleMode.Single)]
        [TestCase(ScheduleMode.Run)]
        [TestCase(ScheduleMode.RunWithoutJobs)]
        public void IJobEntityBatchWithIndex_GeneratesExpectedBatches_WithFiltering(ScheduleMode mode,
            ScheduleGranularity granularity = ScheduleGranularity.Chunk)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp));
            query.SetSharedComponentFilter(new EcsTestSharedComp {value = 17});

            var entityCount = 10000;
            using (var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator))
            {
                for (var i = 0; i < entityCount; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new EcsTestData {value = -1});
                    if ((i % 2) == 0)
                    {
                        m_Manager.SetSharedComponentData(entities[i], new EcsTestSharedComp {value = 17});
                    }
                }

                var maxBatchCount = (granularity == ScheduleGranularity.Chunk) ? archetype.ChunkCount : entityCount;
                using (var batches = CollectionHelper.CreateNativeArray<ArchetypeChunk, RewindableAllocator>(maxBatchCount, ref World.UpdateAllocator))
                {
                    var batchEntityOffsets = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(batches.Length, ref World.UpdateAllocator);
                    for (int i = 0; i < batchEntityOffsets.Length; ++i)
                    {
                        batchEntityOffsets[i] = -1;
                    }

                    var job = new WriteBatchInfoAndEntityOffsetToArray
                    {
                        BatchInfos = batches,
                        BatchFirstEntityOffsets = batchEntityOffsets,
                    };
                    if (mode == ScheduleMode.Parallel)
                    {
                        if (granularity == ScheduleGranularity.Chunk)
                            job.ScheduleParallel(query).Complete();
                        else
                            job.ScheduleParallel(query, granularity, default).Complete();
                    }
                    else if (mode == ScheduleMode.Single)
                        job.Schedule(query).Complete();
                    else if (mode == ScheduleMode.Run)
                        job.Run(query);
                    else if (mode == ScheduleMode.RunWithoutJobs)
                        JobEntityBatchIndexExtensions.RunWithoutJobs(ref job, query);

                    using (var matchingEntities = query.ToEntityArray(World.UpdateAllocator.ToAllocator))
                    {
                        var entityTypeHandle = m_Manager.GetEntityTypeHandle();
                        int markedEntityCount = 0;
                        for (int batchIndex = 0; batchIndex < batches.Length; ++batchIndex)
                        {
                            var batch = batches[batchIndex];
                            if (!IsBatchInitialized(batch))
                                continue; // this is fine; empty/filtered batches will be skipped and left uninitialized.
                            if (granularity == ScheduleGranularity.Entity)
                                Assert.AreEqual(1, batch.Count);
                            else
                            {
                                Assert.Greater(batch.Count, 0); // empty batches should not have been Execute()ed
                                Assert.AreEqual(batch.ChunkEntityCount, batch.Count);
                            }
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
#endif

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

            public void Execute(ArchetypeChunk batchInChunk, int batchId)
            {
                for (int i = 0; i < MyArray.Length; i++)
                {
                    MyArray[i] = batchId;
                }
            }
        }

#if !NET_DOTS // DOTS Runtimes does not support regex
        [Test]
        public void ParallelArrayWriteTriggersSafetySystem()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            using(var entitiesA = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(archetypeA.ChunkCapacity, ref World.UpdateAllocator))
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var local = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(archetypeA.ChunkCapacity * 2, ref World.UpdateAllocator))
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
            using(var entitiesA = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(archetypeA.ChunkCapacity, ref World.UpdateAllocator))
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var local = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(archetypeA.ChunkCapacity * 2, ref World.UpdateAllocator))
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
            using(var entitiesA = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(archetypeA.ChunkCapacity, ref World.UpdateAllocator))
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var local = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(archetypeA.ChunkCapacity * 2, ref World.UpdateAllocator))
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
            using(var entitiesA = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(archetypeA.ChunkCapacity, ref World.UpdateAllocator))
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var local = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(archetypeA.ChunkCapacity * 2, ref World.UpdateAllocator))
            {
                m_Manager.CreateEntity(archetypeA, entitiesA);
                new WriteToArrayWithIndex
                {
                    MyArray = local
                }.Schedule(query).Complete();
            }
        }

// TODO(https://unity3d.atlassian.net/browse/DOTSR-2746): [TestCase(args)] is not supported in the portable test runner
#if !UNITY_PORTABLE_TEST_RUNNER
        [Test]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Chunk)]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Entity)]
        [TestCase(ScheduleMode.Single)]
        [TestCase(ScheduleMode.Run)]
        [TestCase(ScheduleMode.RunWithoutJobs)]
        public unsafe void IJobEntityBatch_WithEntityList(ScheduleMode mode,
            ScheduleGranularity granularity = ScheduleGranularity.Chunk)
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var allEntitiesA = m_Manager.CreateEntity(archetypeA, 100, World.UpdateAllocator.ToAllocator))
            using (var allEntitiesB = m_Manager.CreateEntity(archetypeB, 100, World.UpdateAllocator.ToAllocator))
            {
                // One batch, all matching
                {
                    var entities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(10, ref World.UpdateAllocator);

                    for (int i = 0; i < 10; ++i)
                    {
                        entities[i] = allEntitiesA[i];
                    }

                    for (int i = 0; i < allEntitiesA.Length; ++i)
                    {
                        m_Manager.SetComponentData(allEntitiesA[i], new EcsTestData {value = -1});
                    }

                    var job = new WriteBatchId {EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)};
                    if (mode == ScheduleMode.Single)
                        job.Schedule(query, entities).Complete();
                    else if (mode == ScheduleMode.Run)
                        job.Run(query, entities);
                    else if (mode == ScheduleMode.RunWithoutJobs)
                        JobEntityBatchExtensions.RunWithoutJobs(ref job, query, entities);
                    else if (mode == ScheduleMode.Parallel)
                        job.ScheduleParallel(query, granularity, entities).Complete();

                    if (granularity == ScheduleGranularity.Chunk)
                    {
                        Assert.AreEqual(0, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[0]).value);
                        for (int i = 1; i < 100; ++i)
                            Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                    }
                    else if (granularity == ScheduleGranularity.Entity)
                    {
                        for(int i = 0; i < 10; ++i)
                            Assert.AreEqual(i, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                        for (int i = 10; i < 100; ++i)
                            Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                    }
                }

                // All separate batches, all matching
                {
                    var entities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(10, ref World.UpdateAllocator);
                    for (int i = 0; i < 10; ++i)
                    {
                        entities[i] = allEntitiesA[i * 10];
                    }

                    for (int i = 0; i < allEntitiesA.Length; ++i)
                    {
                        m_Manager.SetComponentData(allEntitiesA[i], new EcsTestData {value = -1});
                    }

                    var job = new WriteBatchId {EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)};
                    if (mode == ScheduleMode.Single)
                        job.Schedule(query, entities).Complete();
                    else if (mode == ScheduleMode.Run)
                        job.Run(query, entities);
                    else if (mode == ScheduleMode.RunWithoutJobs)
                        JobEntityBatchExtensions.RunWithoutJobs(ref job, query, entities);
                    else if (mode == ScheduleMode.Parallel)
                        job.ScheduleParallel(query, granularity, entities).Complete();

                    for (int i = 0; i < 100; i++)
                    {
                        var div = i / 10;
                        var mod = i % 10;
                        if (mod == 0)
                            Assert.AreEqual(div, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                        else
                            Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                    }
                }

                // Mixed batches, mixed matching archetype
                {
                    var entities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(100, ref World.UpdateAllocator);
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

                    var job = new WriteBatchId {EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)};
                    if (mode == ScheduleMode.Single)
                        job.Schedule(query, entities).Complete();
                    else if (mode == ScheduleMode.Run)
                        job.Run(query, entities);
                    else if (mode == ScheduleMode.RunWithoutJobs)
                        JobEntityBatchExtensions.RunWithoutJobs(ref job, query, entities);
                    else if (mode == ScheduleMode.Parallel)
                        job.ScheduleParallel(query, granularity, entities).Complete();

                    for (int i = 0; i < 100; ++i)
                    {
                        var div = i / 5;
                        var mod = i % 5;
                        if (granularity == ScheduleGranularity.Chunk)
                        {
                            // pattern is BAAAABAAAA. Job writes to the first entity in each batch, so
                            //             0    1     expected batch indices are as shown. Other As should be -1.
                            if (mod == 1)
                                Assert.AreEqual(div, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                            else
                                Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                        }
                        else if (granularity == ScheduleGranularity.Entity)
                        {
                            // pattern is BAAAABAAAA. Each entity from A will be its own batch, so
                            //             0123 4567  expected batch indices are as shown.
                            var index = (4 * div) + mod - 1;
                            if (mod == 0)
                                Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                            else
                                Assert.AreEqual(index, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                        }
                    }
                }
            }
        }

        [Test]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Chunk)]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Entity)]
        [TestCase(ScheduleMode.Single)]
        [TestCase(ScheduleMode.Run)]
        //[TestCase(ScheduleMode.RunWithoutJobs)] // TODO: https://unity3d.atlassian.net/browse/DOTS-4463
        public unsafe void IJobEntityBatchWithIndex_WithEntityList(ScheduleMode mode,
            ScheduleGranularity granularity = ScheduleGranularity.Chunk)
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));

            using (var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var allEntitiesA = m_Manager.CreateEntity(archetypeA, 100, World.UpdateAllocator.ToAllocator))
            using (var allEntitiesB = m_Manager.CreateEntity(archetypeB, 100, World.UpdateAllocator.ToAllocator))
            {
                // One batch, all matching
                {
                    var entities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(10, ref World.UpdateAllocator);

                    for (int i = 0; i < 10; ++i)
                    {
                        entities[i] = allEntitiesA[i];
                    }

                    for (int i = 0; i < allEntitiesA.Length; ++i)
                    {
                        m_Manager.SetComponentData(allEntitiesA[i], new EcsTestData {value = -1});
                    }

                    var job = new WriteEntityIndex {EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)};
                    if (mode == ScheduleMode.Single)
                        job.Schedule(query, entities).Complete();
                    else if (mode == ScheduleMode.Run)
                        job.Run(query, entities);
                    // TODO: this code path is missing; https://unity3d.atlassian.net/browse/DOTS-4463 tracks the fix
                    //else if (mode == ScheduleMode.RunWithoutJobs)
                    //    JobEntityBatchIndexExtensions.RunWithoutJobs(ref job, query, entities);
                    else if (mode == ScheduleMode.Parallel)
                        job.ScheduleParallel(query, granularity, entities).Complete();

                    if (granularity == ScheduleGranularity.Chunk)
                    {
                        Assert.AreEqual(0, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[0]).value);
                        for (int i = 1; i < 100; ++i)
                            Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                    }
                    else if (granularity == ScheduleGranularity.Entity)
                    {
                        for(int i = 0; i < 10; ++i)
                            Assert.AreEqual(i, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                        for (int i = 10; i < 100; ++i)
                            Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                    }
                }

                // All separate batches, all matching
                {
                    var entities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(10, ref World.UpdateAllocator);
                    for (int i = 0; i < 10; ++i)
                    {
                        entities[i] = allEntitiesA[i * 10];
                    }

                    for (int i = 0; i < allEntitiesA.Length; ++i)
                    {
                        m_Manager.SetComponentData(allEntitiesA[i], new EcsTestData {value = -1});
                    }

                    var job = new WriteEntityIndex {EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)};
                    if (mode == ScheduleMode.Single)
                        job.Schedule(query, entities).Complete();
                    else if (mode == ScheduleMode.Run)
                        job.Run(query, entities);
                    // TODO: this code path is missing; https://unity3d.atlassian.net/browse/DOTS-4463 tracks the fix
                    //else if (mode == ScheduleMode.RunWithoutJobs)
                    //    JobEntityBatchIndexExtensions.RunWithoutJobs(ref job, query, entities);
                    else if (mode == ScheduleMode.Parallel)
                        job.ScheduleParallel(query, granularity, entities).Complete();

                    for (int i = 0; i < 100; i++)
                    {
                        var div = i / 10;
                        var mod = i % 10;
                        if (mod == 0)
                            Assert.AreEqual(div, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                        else
                            Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                    }
                }

                // Mixed batches, mixed matching archetype
                {
                    var entities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(100, ref World.UpdateAllocator);
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

                    var job = new WriteEntityIndex {EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)};
                    if (mode == ScheduleMode.Single)
                        job.Schedule(query, entities).Complete();
                    else if (mode == ScheduleMode.Run)
                        job.Run(query, entities);
                    // TODO: this code path is missing; https://unity3d.atlassian.net/browse/DOTS-4463 tracks the fix
                    //else if (mode == ScheduleMode.RunWithoutJobs)
                    //    JobEntityBatchIndexExtensions.RunWithoutJobs(ref job, query, entities);
                    else if (mode == ScheduleMode.Parallel)
                        job.ScheduleParallel(query, granularity, entities).Complete();

                    for (int i = 0; i < 100; ++i)
                    {
                        var div = i / 5;
                        var mod = i % 5;
                        if (granularity == ScheduleGranularity.Chunk)
                        {
                            // pattern is BAAAABAAAA. Job writes to the first entity in each batch, so
                            //             0    1     expected batch indices are as shown. Other As should be -1.
                            if (mod == 1)
                                Assert.AreEqual(div * 4,  m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                            else
                                Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                        }
                        else if (granularity == ScheduleGranularity.Entity)
                        {
                            // pattern is BAAAABAAAA. Each entity from A will be its own batch, so
                            //             0123 4567  expected batch indices are as shown.
                            var index = (4 * div) + mod - 1;
                            if (mod == 0)
                                Assert.AreEqual(-1, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                            else
                                Assert.AreEqual(index, m_Manager.GetComponentData<EcsTestData>(allEntitiesA[i]).value);
                        }
                    }
                }
            }
        }

        [Test]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Chunk)]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Entity)]
        [TestCase(ScheduleMode.Single)]
        [TestCase(ScheduleMode.Run)]
        [TestCase(ScheduleMode.RunWithoutJobs)]
        public void IJobEntityBatch_GeneratesExpectedBatches_WithEntityList(ScheduleMode mode,
            ScheduleGranularity granularity = ScheduleGranularity.Chunk)
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));
            var archetypeC = m_Manager.CreateArchetype(typeof(EcsTestData3));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using(var entitiesA = m_Manager.CreateEntity(archetypeA, 100, World.UpdateAllocator.ToAllocator))
            using(var entitiesB = m_Manager.CreateEntity(archetypeB, 100, World.UpdateAllocator.ToAllocator))
            using(var entitiesC = m_Manager.CreateEntity(archetypeC, 100, World.UpdateAllocator.ToAllocator))
            {
                for (int i = 0; i < entitiesA.Length; ++i)
                {
                    m_Manager.SetComponentData(entitiesA[i], new EcsTestData(i));
                }
                // AAAAABBBBCAAAAABBBBC...
                var limitEntities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(100, ref World.UpdateAllocator);
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

                int expectedBatchCount = (granularity == ScheduleGranularity.Chunk) ? 10 : 50;
                var batches = CollectionHelper.CreateNativeArray<ArchetypeChunk, RewindableAllocator>(expectedBatchCount, ref World.UpdateAllocator);
                var job = new WriteBatchInfoToArray
                {
                    BatchInfos = batches,
                };
                if (mode == ScheduleMode.Single)
                    job.Schedule(query, limitEntities).Complete();
                else if (mode == ScheduleMode.Run)
                    job.Run(query, limitEntities);
                else if (mode == ScheduleMode.RunWithoutJobs)
                    JobEntityBatchExtensions.RunWithoutJobs(ref job, query, limitEntities);
                else if (mode == ScheduleMode.Parallel)
                    job.ScheduleParallel(query, granularity, limitEntities).Complete();

                using (var matchingEntities = query.ToEntityArray(limitEntities, World.UpdateAllocator.ToAllocator))
                {
                    var entityTypeHandle = m_Manager.GetEntityTypeHandle();
                    int markedEntityCount = 0;
                    for (int batchIndex = 0; batchIndex < batches.Length; ++batchIndex)
                    {
                        var batch = batches[batchIndex];
                        Assert.IsTrue(IsBatchInitialized(batch));

                        Assert.AreEqual((granularity == ScheduleGranularity.Chunk) ? 5 : 1, batch.Count);

                        var batchEntities = batch.GetNativeArray(entityTypeHandle);
                        for (int i = 0; i < batchEntities.Length; ++i)
                        {
                            var expectedValue = (granularity == ScheduleGranularity.Chunk)
                                ? batchIndex * 10 + i
                                : (batchIndex/5) * 10 + (batchIndex % 5);
                            Assert.AreEqual(expectedValue, m_Manager.GetComponentData<EcsTestData>(batchEntities[i]).value);
                            Assert.AreEqual(matchingEntities[markedEntityCount], batchEntities[i]);
                            markedEntityCount++;
                        }
                    }
                    Assert.AreEqual(query.CalculateEntityCount(limitEntities), markedEntityCount);
                }
            }
        }

        [Test]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Chunk)]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Entity)]
        [TestCase(ScheduleMode.Single)]
        [TestCase(ScheduleMode.Run)]
        [TestCase(ScheduleMode.RunWithoutJobs)]
        public void IJobEntityBatch_GeneratesExpectedBatches_WithEntityList_WithFiltering(ScheduleMode mode,
            ScheduleGranularity granularity = ScheduleGranularity.Chunk)
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2), typeof(EcsTestSharedComp));
            var archetypeC = m_Manager.CreateArchetype(typeof(EcsTestData3), typeof(EcsTestSharedComp));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp)))
            using(var entitiesA = m_Manager.CreateEntity(archetypeA, 100, World.UpdateAllocator.ToAllocator))
            using(var entitiesB = m_Manager.CreateEntity(archetypeB, 100, World.UpdateAllocator.ToAllocator))
            using(var entitiesC = m_Manager.CreateEntity(archetypeC, 100, World.UpdateAllocator.ToAllocator))
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
                var limitEntities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(100, ref World.UpdateAllocator);
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

                var batches = CollectionHelper.CreateNativeArray<ArchetypeChunk, RewindableAllocator>(50, ref World.UpdateAllocator);
                var job = new WriteBatchInfoToArray
                {
                    BatchInfos = batches,
                };
                if (mode == ScheduleMode.Single)
                    job.Schedule(query, limitEntities).Complete();
                else if (mode == ScheduleMode.Run)
                    job.Run(query, limitEntities);
                else if (mode == ScheduleMode.RunWithoutJobs)
                    JobEntityBatchExtensions.RunWithoutJobs(ref job, query, limitEntities);
                else if (mode == ScheduleMode.Parallel)
                    job.ScheduleParallel(query, granularity, limitEntities).Complete();

                using (var matchingEntities = query.ToEntityArray(limitEntities, World.UpdateAllocator.ToAllocator))
                {
                    var entityTypeHandle = m_Manager.GetEntityTypeHandle();
                    int markedEntityCount = 0;
                    int validBatchCount = 0;
                    for (int batchIndex = 0; batchIndex < batches.Length; ++batchIndex)
                    {
                        var batch = batches[batchIndex];
                        if (!IsBatchInitialized(batch))
                            continue; // this is fine; empty/filtered batches will be skipped and left uninitialized.

                        int expectedBatchEntityCount =
                            (granularity == ScheduleGranularity.Chunk) ? 3 : 1;
                        Assert.AreEqual(expectedBatchEntityCount, batch.Count);

                        var batchEntities = batch.GetNativeArray(entityTypeHandle);
                        for (int i = 0; i < batchEntities.Length; ++i)
                        {
                            int expectedComponentValue =
                                (granularity == ScheduleGranularity.Chunk)
                                    ? validBatchCount * 10 + i
                                    : (batchIndex/5) * 10 + (batchIndex % 5);
                            ;
                            Assert.AreEqual(expectedComponentValue, m_Manager.GetComponentData<EcsTestData>(batchEntities[i]).value);
                            Assert.AreEqual(matchingEntities[markedEntityCount], batchEntities[i]);
                            markedEntityCount++;
                        }

                        validBatchCount++;
                    }
                    Assert.AreEqual(query.CalculateEntityCount(limitEntities), markedEntityCount);
                }
            }
        }

        [Test]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Chunk)]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Entity)]
        [TestCase(ScheduleMode.Single)]
        [TestCase(ScheduleMode.Run)]
        //[TestCase(ScheduleMode.RunWithoutJobs)] // TODO: https://unity3d.atlassian.net/browse/DOTS-4463
        public void IJobEntityBatchWithIndex_GeneratesExpectedBatches_WithEntityList_WithFiltering(ScheduleMode mode,
            ScheduleGranularity granularity = ScheduleGranularity.Chunk)
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2), typeof(EcsTestSharedComp));
            var archetypeC = m_Manager.CreateArchetype(typeof(EcsTestData3), typeof(EcsTestSharedComp));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp)))
            using(var entitiesA = m_Manager.CreateEntity(archetypeA, 100, World.UpdateAllocator.ToAllocator))
            using(var entitiesB = m_Manager.CreateEntity(archetypeB, 100, World.UpdateAllocator.ToAllocator))
            using(var entitiesC = m_Manager.CreateEntity(archetypeC, 100, World.UpdateAllocator.ToAllocator))
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
                var limitEntities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(100, ref World.UpdateAllocator);
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

                var batches = CollectionHelper.CreateNativeArray<ArchetypeChunk, RewindableAllocator>(30, ref World.UpdateAllocator);
                var batchEntityOffsets = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(batches.Length, ref World.UpdateAllocator);
                for (int i = 0; i < batchEntityOffsets.Length; ++i)
                {
                    batchEntityOffsets[i] = -1;
                }

                var job = new WriteBatchInfoAndEntityOffsetToArray
                {
                    BatchInfos = batches,
                    BatchFirstEntityOffsets = batchEntityOffsets,
                };
                if (mode == ScheduleMode.Single)
                    job.Schedule(query, limitEntities).Complete();
                else if (mode == ScheduleMode.Run)
                    job.Run(query, limitEntities);
                // TODO: this code path is missing; https://unity3d.atlassian.net/browse/DOTS-4463 tracks the fix
                //else if (mode == ScheduleMode.RunWithoutJobs)
                //    JobEntityBatchIndexExtensions.RunWithoutJobs(ref job, query, entities);
                else if (mode == ScheduleMode.Parallel)
                    job.ScheduleParallel(query, granularity, limitEntities).Complete();

                using (var matchingEntities = query.ToEntityArray(limitEntities, World.UpdateAllocator.ToAllocator))
                {
                    var entityTypeHandle = m_Manager.GetEntityTypeHandle();
                    int markedEntityCount = 0;
                    int validBatchCount = 0;
                    for (int batchIndex = 0; batchIndex < batches.Length; ++batchIndex)
                    {
                        var batch = batches[batchIndex];
                        if (!IsBatchInitialized(batch))
                            continue; // this is fine; empty/filtered batches will be skipped and left uninitialized.

                        int expectedBatchEntityCount =
                            (granularity == ScheduleGranularity.Chunk) ? 3 : 1;
                        Assert.AreEqual(expectedBatchEntityCount, batch.Count);

                        var batchEntities = batch.GetNativeArray(entityTypeHandle);
                        int batchFirstEntityIndex = batchEntityOffsets[batchIndex];
                        Assert.AreNotEqual(-1, batchFirstEntityIndex);
                        Assert.AreEqual(matchingEntities[batchFirstEntityIndex], batchEntities[0]);
                        for (int i = 0; i < batchEntities.Length; ++i)
                        {
                            int expectedComponentValue =
                                (granularity == ScheduleGranularity.Chunk)
                                    ? validBatchCount * 10 + i
                                    : (batchIndex/3) * 10 + (batchIndex % 3);
                            ;
                            Assert.AreEqual(expectedComponentValue, m_Manager.GetComponentData<EcsTestData>(batchEntities[i]).value);
                            Assert.AreEqual(matchingEntities[markedEntityCount], batchEntities[i]);
                            markedEntityCount++;
                        }

                        validBatchCount++;
                    }
                    Assert.AreEqual(query.CalculateEntityCount(limitEntities), markedEntityCount);
                }
            }
        }

        [Test]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Chunk)]
        [TestCase(ScheduleMode.Parallel, ScheduleGranularity.Entity)]
        [TestCase(ScheduleMode.Single)]
        [TestCase(ScheduleMode.Run)]
        //[TestCase(ScheduleMode.RunWithoutJobs)] // TODO: https://unity3d.atlassian.net/browse/DOTS-4463
        public void IJobEntityBatchWithIndex_GeneratesExpectedBatches_WithEntityList(ScheduleMode mode,
            ScheduleGranularity granularity = ScheduleGranularity.Chunk)
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData2));
            var archetypeC = m_Manager.CreateArchetype(typeof(EcsTestData3));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using(var entitiesA = m_Manager.CreateEntity(archetypeA, 100, World.UpdateAllocator.ToAllocator))
            using(var entitiesB = m_Manager.CreateEntity(archetypeB, 100, World.UpdateAllocator.ToAllocator))
            using(var entitiesC = m_Manager.CreateEntity(archetypeC, 100, World.UpdateAllocator.ToAllocator))
            {
                for (int i = 0; i < entitiesA.Length; ++i)
                {
                    m_Manager.SetComponentData(entitiesA[i], new EcsTestData(i));
                }
                // AAAAABBBBCAAAAABBBBC...
                var limitEntities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(100, ref World.UpdateAllocator);
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

                int expectedBatchCount = (granularity == ScheduleGranularity.Chunk) ? 10 : 50;
                var batches = CollectionHelper.CreateNativeArray<ArchetypeChunk, RewindableAllocator>(expectedBatchCount, ref World.UpdateAllocator);
                var batchEntityOffsets = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(batches.Length, ref World.UpdateAllocator);
                for (int i = 0; i < batchEntityOffsets.Length; ++i)
                {
                    batchEntityOffsets[i] = -1;
                }

                var job = new WriteBatchInfoAndEntityOffsetToArray
                {
                    BatchInfos = batches,
                    BatchFirstEntityOffsets = batchEntityOffsets,
                };
                if (mode == ScheduleMode.Single)
                    job.Schedule(query, limitEntities).Complete();
                else if (mode == ScheduleMode.Run)
                    job.Run(query, limitEntities);
                // TODO: this code path is missing; https://unity3d.atlassian.net/browse/DOTS-4463 tracks the fix
                //else if (mode == ScheduleMode.RunWithoutJobs)
                //    JobEntityBatchIndexExtensions.RunWithoutJobs(ref job, query, entities);
                else if (mode == ScheduleMode.Parallel)
                    job.ScheduleParallel(query, granularity, limitEntities).Complete();

                using (var matchingEntities = query.ToEntityArray(limitEntities, World.UpdateAllocator.ToAllocator))
                {
                    var entityTypeHandle = m_Manager.GetEntityTypeHandle();
                    int markedEntityCount = 0;
                    for (int batchIndex = 0; batchIndex < batches.Length; ++batchIndex)
                    {
                        var batch = batches[batchIndex];
                        Assert.IsTrue(IsBatchInitialized(batch));

                        Assert.AreEqual((granularity == ScheduleGranularity.Chunk) ? 5 : 1, batch.Count);

                        var batchEntities = batch.GetNativeArray(entityTypeHandle);
                        int batchFirstEntityIndex = batchEntityOffsets[batchIndex];
                        Assert.AreNotEqual(-1, batchFirstEntityIndex);
                        Assert.AreEqual(matchingEntities[batchFirstEntityIndex], batchEntities[0]);
                        for (int i = 0; i < batchEntities.Length; ++i)
                        {
                            var expectedValue = (granularity == ScheduleGranularity.Chunk)
                                ? batchIndex * 10 + i
                                : (batchIndex/5) * 10 + (batchIndex % 5);
                            Assert.AreEqual(expectedValue, m_Manager.GetComponentData<EcsTestData>(batchEntities[i]).value);
                            Assert.AreEqual(matchingEntities[markedEntityCount], batchEntities[i]);
                            markedEntityCount++;
                        }
                    }
                    Assert.AreEqual(query.CalculateEntityCount(limitEntities), markedEntityCount);
                }
            }
        }
#endif

        // Not Burst compiling since we are Asserting in this job
        struct CheckBatchIndices : IJobEntityBatch
        {
            public ComponentTypeHandle<EcsTestData> EcsTestDataTypeHandle;
            public void Execute(ArchetypeChunk batchInChunk, int batchId)
            {
                var testData = batchInChunk.GetNativeArray(EcsTestDataTypeHandle);
                Assert.AreEqual(batchId, testData[0].value);
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

        [Test]
        public void IJobEntityBatch_WithNoBatching_HasCorrectIndices(
            [Values(ScheduleMode.Parallel, ScheduleMode.Single, ScheduleMode.Run, ScheduleMode.RunWithoutJobs)] ScheduleMode scheduleMode)
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp)))
            using (var entities = m_Manager.CreateEntity(archetypeA, 100, World.UpdateAllocator.ToAllocator))
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

        struct LargeJobEntityBatch : IJobEntityBatch
        {
            public FixedString4096Bytes StrA;
            public FixedString4096Bytes StrB;
            public FixedString4096Bytes StrC;
            public FixedString4096Bytes StrD;
            [NativeDisableParallelForRestriction]
            public NativeArray<int> TotalLengths;
            public void Execute(ArchetypeChunk batchInChunk, int batchId)
            {
                TotalLengths[0] = StrA.Length + StrB.Length + StrC.Length + StrD.Length;
            }
        }

        [Test]
        public void IJobEntityBatch_LargeJobStruct_ScheduleByRefWorks(
            [Values(ScheduleMode.Parallel, ScheduleMode.Single, ScheduleMode.Run)] ScheduleMode scheduleMode)
        {
            m_Manager.CreateEntity(typeof(EcsTestData));
            using(var lengths = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(1, ref World.UpdateAllocator))
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                var job = new LargeJobEntityBatch
                {
                        StrA = "A",
                        StrB = "BB",
                        StrC = "CCC",
                        StrD = "DDDD",
                        TotalLengths = lengths,
                };
                if (scheduleMode == ScheduleMode.Parallel)
                    Assert.DoesNotThrow(() => { job.ScheduleParallelByRef(query).Complete(); });
                else if (scheduleMode == ScheduleMode.Single)
                    Assert.DoesNotThrow(() => { job.ScheduleByRef(query).Complete(); });
                else if (scheduleMode == ScheduleMode.Run)
                    Assert.DoesNotThrow(() => { job.RunByRef(query); });
                Assert.AreEqual(lengths[0], 10);
            }
        }

        struct LargeJobEntityBatchWithIndex : IJobEntityBatchWithIndex
        {
            public FixedString4096Bytes StrA;
            public FixedString4096Bytes StrB;
            public FixedString4096Bytes StrC;
            public FixedString4096Bytes StrD;
            [NativeDisableParallelForRestriction]
            public NativeArray<int> TotalLengths;
            public void Execute(ArchetypeChunk batchInChunk, int batchId, int indexOfFirstEntityInQuery)
            {
                TotalLengths[0] = StrA.Length + StrB.Length + StrC.Length + StrD.Length;
            }
        }

        [Test]
        public void IJobEntityBatchWithIndex_LargeJobStruct_ScheduleByRefWorks(
            [Values(ScheduleMode.Parallel, ScheduleMode.Single, ScheduleMode.Run)] ScheduleMode scheduleMode)
        {
            m_Manager.CreateEntity(typeof(EcsTestData));
            using(var lengths = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(1, ref World.UpdateAllocator))
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                var job = new LargeJobEntityBatchWithIndex
                {
                        StrA = "A",
                        StrB = "BB",
                        StrC = "CCC",
                        StrD = "DDDD",
                        TotalLengths = lengths,
                };
                if (scheduleMode == ScheduleMode.Parallel)
                    Assert.DoesNotThrow(() => { job.ScheduleParallelByRef(query).Complete(); });
                else if (scheduleMode == ScheduleMode.Single)
                    Assert.DoesNotThrow(() => { job.ScheduleByRef(query).Complete(); });
                else if (scheduleMode == ScheduleMode.Run)
                    Assert.DoesNotThrow(() => { job.RunByRef(query); });
                Assert.AreEqual(lengths[0], 10);
            }
        }
    }
}
