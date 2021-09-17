using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.PerformanceTesting;
using Unity.Entities.Tests;
using Unity.Jobs;
using Unity.Profiling;
using static Unity.Entities.PerformanceTests.PerformanceTestHelpers;

namespace Unity.Entities.PerformanceTests
{
    [TestFixture]
    [Category("Performance")]
    public sealed class EntityQueryIncrementalCachingPerformanceTests : EntityPerformanceTestFixture
    {
        NativeArray<EntityQuery> CreateUniqueQueries(int size)
        {
            var queries = new NativeArray<EntityQuery>(size, Allocator.TempJob);

            for (int i = 0; i < size; i++)
            {
                var typeCount = CollectionHelper.Log2Ceil(i);
                var typeList = new List<ComponentType>();
                for (int typeIndex = 0; typeIndex < typeCount; typeIndex++)
                {
                    if ((i & (1 << typeIndex)) != 0)
                        typeList.Add(TestTags.TagTypes[typeIndex]);
                }

                typeList.Add(typeof(EcsTestData));
                typeList.Add(typeof(EcsTestSharedComp));

                var types = typeList.ToArray();
                queries[i] = m_Manager.CreateEntityQuery(types);
            }

            return queries;
        }

        [Test, Performance]
        public void RebuildMatchingChunkCache_EmptyArchetypes([Values(100, 1000, 10000)] int archetypeCount)
        {
            using var archetypes = CreateUniqueArchetypes(m_Manager, archetypeCount, Allocator.TempJob,
                typeof(EcsTestData));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            Measure.Method(() =>
                {
                    query.UpdateCache();
                })
                .SetUp(() => query.InvalidateCache())
                .MeasurementCount(100)
                .WarmupCount(1)
                .Run();
        }

        [Test, Performance]
        public void CreateDestroyEntity_Scaling([Values(10, 100)] int archetypeCount, [Values(10, 100)] int queryCount)
        {
            const int kInitialEntityCount = 5000000;
            const int kCreateDestroyEntityCount = 200000;

            using(var archetypes = CreateUniqueArchetypes(m_Manager, archetypeCount, Allocator.TempJob,typeof(EcsTestData), typeof(EcsTestSharedComp)))
            using(var queries = CreateUniqueQueries(queryCount))
            {
                for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
                {
                    m_Manager.CreateEntity(archetypes[archetypeIndex], kInitialEntityCount / archetypeCount);
                }

                var basicArchetype = m_Manager.CreateArchetype(typeof(EcsTestData));

                var createEntities = default(NativeArray<Entity>);
                Measure.Method(() =>
                    {
                        createEntities = m_Manager.CreateEntity(basicArchetype, kCreateDestroyEntityCount,
                            Allocator.TempJob);
                    })
                    .CleanUp(() =>
                    {
                        m_Manager.DestroyEntity(createEntities);
                        createEntities.Dispose();
                    })
                    .MeasurementCount(100)
                    .WarmupCount(1)
                    .SampleGroup("CreateEntities")
                    .Run();

                var destroyEntities = default(NativeArray<Entity>);
                Measure.Method(() => { m_Manager.DestroyEntity(destroyEntities); })
                    .SetUp(() =>
                    {
                        destroyEntities = m_Manager.CreateEntity(basicArchetype, kCreateDestroyEntityCount,
                            Allocator.TempJob);
                    })
                    .CleanUp(() => { destroyEntities.Dispose(); })
                    .WarmupCount(1)
                    .MeasurementCount(100)
                    .SampleGroup("DestroyEntities")
                    .Run();
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct TestChunkJob : IJobChunk
        {
            public ComponentTypeHandle<EcsTestData> EcsTestDataRW;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var data = chunk.GetNativeArray(EcsTestDataRW);
                data[0] = new EcsTestData {value = 10};
            }
        }

        void IJobChunk_Performance_Scheduling(int entityCount, int archetypeCount, bool enableQueryFiltering,
            bool enableQueryChunkCache)
        {
            using (var archetypes = CreateUniqueArchetypes(m_Manager, archetypeCount,
                Allocator.TempJob, typeof(EcsTestData), typeof(EcsTestSharedComp)))
            using (var basicQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData),
                typeof(EcsTestSharedComp)))
            {
                for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
                {
                    m_Manager.CreateEntity(archetypes[archetypeIndex], entityCount / archetypeCount);
                }
                if (enableQueryFiltering)
                    basicQuery.SetSharedComponentFilter(default(EcsTestSharedComp));
                var handle = default(JobHandle);
                Measure.Method(() =>
                    {
                        handle = new TestChunkJob
                        {
                            EcsTestDataRW = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                        }.Schedule(basicQuery, handle);
                    })
                    .CleanUp(() =>
                    {
                        handle.Complete();
                        if (!enableQueryChunkCache)
                        {
                            basicQuery.InvalidateCache();
                        }
                    })
                    .WarmupCount(1)
                    .MeasurementCount(100)
                    .Run();
            }
        }

        [Test, Performance]
        public void IJobChunk_Performance_Scheduling_WithFilter_WithCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobChunk_Performance_Scheduling(entityCount, archetypeCount, true, true);
        }
        [Test, Performance]
        public void IJobChunk_Performance_Scheduling_WithoutFilter_WithCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobChunk_Performance_Scheduling(entityCount, archetypeCount, false, true);
        }
        [Test, Performance]
        public void IJobChunk_Performance_Scheduling_WithFilter_WithoutCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobChunk_Performance_Scheduling(entityCount, archetypeCount, true, false);
        }
        [Test, Performance]
        public void IJobChunk_Performance_Scheduling_WithoutFilter_WithoutCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobChunk_Performance_Scheduling(entityCount, archetypeCount, false, false);
        }

        void IJobChunk_Performance_Executing(int entityCount, int archetypeCount, bool enableQueryFiltering,
            bool enableQueryChunkCache)
        {
            using (var archetypes = CreateUniqueArchetypes(m_Manager, archetypeCount, Allocator.TempJob, typeof(EcsTestData), typeof(EcsTestSharedComp)))
            using (var basicQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp)))
            {
                for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
                {
                    m_Manager.CreateEntity(archetypes[archetypeIndex], entityCount / archetypeCount);
                }

                if (enableQueryFiltering)
                    basicQuery.SetSharedComponentFilter(default(EcsTestSharedComp));

                Measure.Method(() =>
                    {
                        new TestChunkJob
                        {
                            EcsTestDataRW = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                        }.Run(basicQuery);
                    })
                    .CleanUp(() =>
                    {
                        if (!enableQueryChunkCache)
                        {
                            basicQuery.InvalidateCache();
                        }
                    })
                    .WarmupCount(1)
                    .MeasurementCount(100)
                    .Run();
            }
        }

        [Test, Performance]
        public void IJobChunk_Performance_Executing_WithFilter_WithCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobChunk_Performance_Executing(entityCount, archetypeCount, true, true);
        }
        [Test, Performance]
        public void IJobChunk_Performance_Executing_WithoutFilter_WithCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobChunk_Performance_Executing(entityCount, archetypeCount, false, true);
        }
        [Test, Performance]
        public void IJobChunk_Performance_Executing_WithFilter_WithoutCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobChunk_Performance_Executing(entityCount, archetypeCount, true, false);
        }
        [Test, Performance]
        public void IJobChunk_Performance_Executing_WithoutFilter_WithoutCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobChunk_Performance_Executing(entityCount, archetypeCount, false, false);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct TestEntityBatchJob : IJobEntityBatch
        {
            public ComponentTypeHandle<EcsTestData> EcsTestDataRW;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var data = batchInChunk.GetNativeArray(EcsTestDataRW);
                data[0] = new EcsTestData {value = 10};
            }
        }

        void IJobEntityBatch_Performance_Scheduling(int entityCount, int archetypeCount, bool enableQueryFiltering,
            bool enableQueryChunkCache)
        {
            using (var archetypes = CreateUniqueArchetypes(m_Manager, archetypeCount,  Allocator.TempJob, typeof(EcsTestData), typeof(EcsTestSharedComp)))
            using (var basicQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp)))
            {
                for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
                {
                    m_Manager.CreateEntity(archetypes[archetypeIndex], entityCount / archetypeCount);
                }

                if (enableQueryFiltering)
                    basicQuery.SetSharedComponentFilter(default(EcsTestSharedComp));

                var handle = default(JobHandle);
                Measure.Method(() =>
                    {
                        handle = new TestEntityBatchJob
                        {
                            EcsTestDataRW = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                        }.ScheduleParallel(basicQuery, handle);
                    })
                    .CleanUp(() =>
                    {
                        handle.Complete();
                        if (!enableQueryChunkCache)
                        {
                            basicQuery.InvalidateCache();
                        }
                    })
                    .WarmupCount(1)
                    .MeasurementCount(100)
                    .Run();
            }
        }

        [Test, Performance]
        public void IJobEntityBatch_Performance_Scheduling_WithFilter_WithCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatch_Performance_Scheduling(entityCount, archetypeCount, true, true);
        }
        [Test, Performance]
        public void IJobEntityBatch_Performance_Scheduling_WithoutFilter_WithCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatch_Performance_Scheduling(entityCount, archetypeCount, false, true);
        }
        [Test, Performance]
        public void IJobEntityBatch_Performance_Scheduling_WithFilter_WithoutCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatch_Performance_Scheduling(entityCount, archetypeCount, true, false);
        }
        [Test, Performance]
        public void IJobEntityBatch_Performance_Scheduling_WithoutFilter_WithoutCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatch_Performance_Scheduling(entityCount, archetypeCount, false, false);
        }

        void IJobEntityBatch_Performance_Executing(int entityCount, int archetypeCount, bool enableQueryFiltering,
            bool enableQueryChunkCache)
        {
            using (var archetypes = CreateUniqueArchetypes(m_Manager, archetypeCount,  Allocator.TempJob, typeof(EcsTestData), typeof(EcsTestSharedComp)))
            using (var basicQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp)))
            {
                for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
                {
                    m_Manager.CreateEntity(archetypes[archetypeIndex], entityCount / archetypeCount);
                }

                if (enableQueryFiltering)
                    basicQuery.SetSharedComponentFilter(default(EcsTestSharedComp));

                Measure.Method(() =>
                    {
                        new TestEntityBatchJob
                        {
                            EcsTestDataRW = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                        }.Run(basicQuery);
                    })
                    .CleanUp(() =>
                    {
                        if (!enableQueryChunkCache)
                        {
                            basicQuery.InvalidateCache();
                        }
                    })
                    .WarmupCount(1)
                    .MeasurementCount(100)
                    .Run();
            }
        }

        [Test, Performance]
        public void IJobEntityBatch_Performance_Executing_WithFilter_WithCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatch_Performance_Executing(entityCount, archetypeCount, true, true);
        }
        [Test, Performance]
        public void IJobEntityBatch_Performance_Executing_WithoutFilter_WithCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatch_Performance_Executing(entityCount, archetypeCount, false, true);
        }
        [Test, Performance]
        public void IJobEntityBatch_Performance_Executing_WithFilter_WithoutCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatch_Performance_Executing(entityCount, archetypeCount, true, false);
        }
        [Test, Performance]
        public void IJobEntityBatch_Performance_Executing_WithoutFilter_WithoutCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatch_Performance_Executing(entityCount, archetypeCount, false, false);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct TestEntityBatchWithIndexJob : IJobEntityBatchWithIndex
        {
            public ComponentTypeHandle<EcsTestData> EcsTestDataRW;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex, int indexOfFirstEntityInQuery)
            {
                var data = batchInChunk.GetNativeArray(EcsTestDataRW);
                data[0] = new EcsTestData {value = 10};
            }
        }

        public void IJobEntityBatchWithIndex_Performance_Scheduling(int entityCount, int archetypeCount,
            bool enableQueryFiltering, bool enableQueryChunkCache)
        {
            using (var archetypes = CreateUniqueArchetypes(m_Manager, archetypeCount,
                Allocator.TempJob, typeof(EcsTestData),
                typeof(EcsTestSharedComp)))
            using (var basicQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData),
                typeof(EcsTestSharedComp)))
            {
                for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
                {
                    m_Manager.CreateEntity(archetypes[archetypeIndex], entityCount / archetypeCount);
                }
                if (enableQueryFiltering)
                    basicQuery.SetSharedComponentFilter(default(EcsTestSharedComp));
                var handle = default(JobHandle);
                Measure.Method(() =>
                    {
                        handle = new TestEntityBatchWithIndexJob
                        {
                            EcsTestDataRW = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                        }.ScheduleParallel(basicQuery, handle);
                    })
                    .CleanUp(() =>
                    {
                        handle.Complete();
                        if (!enableQueryChunkCache)
                        {
                            basicQuery.InvalidateCache();
                        }
                    })
                    .WarmupCount(1)
                    .MeasurementCount(100)
                    .Run();
            }
        }

        [Test, Performance]
        public void IJobEntityBatchWithIndex_Performance_Scheduling_WithFilter_WithCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatchWithIndex_Performance_Scheduling(entityCount, archetypeCount, true, true);
        }
        [Test, Performance]
        public void IJobEntityBatchWithIndex_Performance_Scheduling_WithoutFilter_WithCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatchWithIndex_Performance_Scheduling(entityCount, archetypeCount, false, true);
        }
        [Test, Performance]
        public void IJobEntityBatchWithIndex_Performance_Scheduling_WithFilter_WithoutCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatchWithIndex_Performance_Scheduling(entityCount, archetypeCount, true, false);
        }
        [Test, Performance]
        public void IJobEntityBatchWithIndex_Performance_Scheduling_WithoutFilter_WithoutCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatchWithIndex_Performance_Scheduling(entityCount, archetypeCount, false, false);
        }

        void IJobEntityBatchWithIndex_Performance_Executing(int entityCount, int archetypeCount,
            bool enableQueryFiltering, bool enableQueryChunkCache)
        {
            using (var archetypes = CreateUniqueArchetypes(m_Manager, archetypeCount,  Allocator.TempJob, typeof(EcsTestData), typeof(EcsTestSharedComp)))
            using (var basicQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp)))
            {
                for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
                {
                    m_Manager.CreateEntity(archetypes[archetypeIndex], entityCount / archetypeCount);
                }
                if (enableQueryFiltering)
                    basicQuery.SetSharedComponentFilter(default(EcsTestSharedComp));

                Measure.Method(() =>
                    {
                        new TestEntityBatchWithIndexJob
                        {
                            EcsTestDataRW = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                        }.Run(basicQuery);
                    })
                    .CleanUp(() =>
                    {
                        if (!enableQueryChunkCache)
                        {
                            basicQuery.InvalidateCache();
                        }
                    })
                    .WarmupCount(1)
                    .MeasurementCount(100)
                    .Run();
            }
        }

        [Test, Performance]
        public void IJobEntityBatchWithIndex_Performance_Executing_WithFilter_WithCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatchWithIndex_Performance_Executing(entityCount, archetypeCount, true, true);
        }
        [Test, Performance]
        public void IJobEntityBatchWithIndex_Performance_Executing_WithoutFilter_WithCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatchWithIndex_Performance_Executing(entityCount, archetypeCount, false, true);
        }
        [Test, Performance]
        public void IJobEntityBatchWithIndex_Performance_Executing_WithFilter_WithoutCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatchWithIndex_Performance_Executing(entityCount, archetypeCount, true, false);
        }
        [Test, Performance]
        public void IJobEntityBatchWithIndex_Performance_Executing_WithoutFilter_WithoutCache([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            IJobEntityBatchWithIndex_Performance_Executing(entityCount, archetypeCount, false, false);
        }

        [StructLayout(LayoutKind.Explicit, Size = 32)]
        unsafe struct TestArchetypeChunk
        {
            [FieldOffset(0)] [NativeDisableUnsafePtrRestriction] internal Chunk* m_Chunk;
            [FieldOffset(8)] [NativeDisableUnsafePtrRestriction] internal int* m_Dummy;
            [FieldOffset(16)] internal int m_Padding1;
            [FieldOffset(20)] internal int m_Padding2;
            internal TestArchetypeChunk(Chunk* chunk, int* dummyData)
            {
                m_Chunk = chunk;
                m_Dummy = dummyData;
                m_Padding1 = 0;
                m_Padding2 = 0;
            }
        }

        unsafe struct TestArchetypeChunkData
        {
            private Chunk** p;
            private int* chunkEntityCounts;
            public int Count { get; private set; } // number of chunks currently tracked [0..Capacity]
            // EntityCount[Capacity]
            private int* EntityCount => chunkEntityCounts;
            public int* GetChunkEntityCountArray()
            {
                return EntityCount;
            }
            public Chunk* this[int index]
            {
                get { return p[index]; }
            }

            public TestArchetypeChunkData(int chunkCount)
            {
                Count = chunkCount;
                p =  (Chunk**)UnsafeUtility.Malloc(chunkCount*sizeof(Chunk*), 64, Allocator.TempJob);
                UnsafeUtility.MemClear(p, chunkCount * sizeof(Chunk*));
                chunkEntityCounts = (int*)UnsafeUtility.Malloc(chunkCount * sizeof(int), 64, Allocator.TempJob);
                UnsafeUtility.MemClear(chunkEntityCounts, chunkCount * sizeof(int));
            }

            public void Free()
            {
                UnsafeUtility.Free(p, Allocator.TempJob);
                UnsafeUtility.Free(chunkEntityCounts, Allocator.TempJob);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        unsafe struct TestArchetype
        {
            public TestArchetypeChunkData Chunks;
            public int EntityCount;

            public TestArchetype(int chunkCount)
            {
                Chunks = new TestArchetypeChunkData(chunkCount);
                EntityCount = 1000; // just needs to be non-zero
            }

            public void Free()
            {
                Chunks.Free();
            }
        }

        unsafe struct TestMatchingArchetype
        {
            public TestArchetype* Archetype;
        }

        unsafe struct TestArchetypePtrList
        {
            [NativeDisableUnsafePtrRestriction]
            private UnsafeList<IntPtr>* ListData;

            public TestMatchingArchetype** Ptr { get => (TestMatchingArchetype**)ListData->Ptr; }
            public int Length { get => ListData->Length; }

            public void Dispose() { ListData->Dispose(); }
            public void Add(void* t) { ListData->Add((IntPtr)t); }

            [NativeDisableUnsafePtrRestriction]
            public int* dummyData;

            public TestArchetypePtrList(int* dummy)
            {
                ListData = UnsafeList<IntPtr>.Create(0, Allocator.Persistent);
                this.dummyData = dummy;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        unsafe struct TestJobOld : IJobBurstSchedulable
        {
            [NativeDisableUnsafePtrRestriction] public TestArchetypePtrList MatchingArchetypes;
            [NativeDisableUnsafePtrRestriction] public int* DummyData;
            [NativeDisableUnsafePtrRestriction] public void* PrefilterData;

            public int FilteredChunkCount;

            public void Execute()
            {
                var preambleMarker = new ProfilerMarker("TestJobOld.Preamble");
                var loopMarker = new ProfilerMarker("TestJobOld.Loop");

                preambleMarker.Begin();
                var batches = (TestArchetypeChunk*)PrefilterData;
                var entityIndices = (int*)(batches + FilteredChunkCount);

                var filteredBatchCounter = 0;
                var entityIndexAggregate = 0;

                int archetypeCount = MatchingArchetypes.Length;
                var ptrs = MatchingArchetypes.Ptr;
                preambleMarker.End();
                loopMarker.Begin();
                {
                    // one batch per chunk, filtering disabled
                    for (var m = 0; m < archetypeCount; ++m)
                    {
                        var match = ptrs[m];
                        if (match->Archetype->EntityCount <= 0)
                            continue;

                        var archetype = match->Archetype;
                        int chunkCount = archetype->Chunks.Count;
                        var chunkEntityCountArray = archetype->Chunks.GetChunkEntityCountArray();

                        for (int chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
                        {
                            var chunk = archetype->Chunks[chunkIndex];
                            var batch = new TestArchetypeChunk(chunk, DummyData);
                            batches[filteredBatchCounter] = batch;
                            entityIndices[filteredBatchCounter] = entityIndexAggregate;

                            ++filteredBatchCounter;
                            entityIndexAggregate += chunkEntityCountArray[chunkIndex];
                        }
                    }
                }
                loopMarker.End();
                var chunkCounter = entityIndices + FilteredChunkCount;
                *chunkCounter = filteredBatchCounter;
            }
        }

        [Test, Performance]
        public unsafe void CompareJobPerf()
        {
            var worldUpdateAllocator = m_World.UpdateAllocator.ToAllocator;
            var expectedBatchCount = 6000;
            var sizeOfOutputData = expectedBatchCount * (sizeof(TestArchetypeChunk) + sizeof(int));

            int dummyECS = 0;
            var archetypes = new NativeList<TestArchetype>(6, Allocator.TempJob);
            for (int i = 0; i < 6; ++i)
            {
                archetypes.Add(new TestArchetype(1000));
            }
            var matchingArchetypes = new NativeList<TestMatchingArchetype>(6, Allocator.TempJob);
            var archetypesPtr = (TestArchetype*)archetypes.GetUnsafePtr();
            for (int i = 0; i < archetypes.Length; ++i)
            {
                matchingArchetypes.Add(new TestMatchingArchetype { Archetype = archetypesPtr + i });
            }
            var matchingArchetypesPtr = (TestMatchingArchetype*)matchingArchetypes.GetUnsafePtr();
            var matchingArchetypesList = new TestArchetypePtrList(&dummyECS);
            for (int i = 0; i < matchingArchetypes.Length; ++i)
            {
                matchingArchetypesList.Add(matchingArchetypesPtr + i);
            }

            // old job
            byte* outputData = null;
            Measure.Method(() =>
                {
                    outputData = (byte*)UnsafeUtility.Malloc(sizeOfOutputData, 64, Allocator.TempJob);
                    new TestJobOld
                    {
                        MatchingArchetypes = matchingArchetypesList,
                        DummyData = null,
                        PrefilterData = outputData,
                        FilteredChunkCount = expectedBatchCount,
                    }.Run();
                })
                .CleanUp(() =>
                {
                    UnsafeUtility.Free(outputData, Allocator.TempJob);
                })
                .SampleGroup("Output to TempJob")
                .WarmupCount(1)
                .MeasurementCount(1000)
                .Run();

            // new job
            Measure.Method(() =>
                {
                    outputData = (byte*)AllocatorManager.Allocate(m_World.UpdateAllocator.Handle, sizeOfOutputData, 64);
                    new TestJobOld
                    {
                        MatchingArchetypes = matchingArchetypesList,
                        DummyData = null,
                        PrefilterData = outputData,
                        FilteredChunkCount = expectedBatchCount,
                    }.Run();
                })
                .CleanUp(() =>
                {
                    AllocatorManager.Free(m_World.UpdateAllocator.Handle, outputData);
                })
                .SampleGroup("Output to World.UpdateAllocator")
                .WarmupCount(1)
                .MeasurementCount(1000)
                .Run();

            // cleanup
            matchingArchetypesList.Dispose();
            matchingArchetypes.Dispose();
            for (int i = 0; i < archetypes.Length; ++i)
            {
                archetypes[i].Free();
            }
            archetypes.Dispose();
        }

    }
}
