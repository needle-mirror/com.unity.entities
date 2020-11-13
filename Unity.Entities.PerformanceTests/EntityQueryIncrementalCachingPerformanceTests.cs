using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.PerformanceTesting;
using Unity.Entities.Tests;
using Unity.Jobs;

namespace Unity.Entities.PerformanceTests
{
    [TestFixture]
    [Category("Performance")]
    public sealed class EntityQueryIncrementalCachingPerformanceTests : EntityPerformanceTestFixture
    {
        struct TestTag0 : IComponentData
        {
        }

        struct TestTag1 : IComponentData
        {
        }

        struct TestTag2 : IComponentData
        {
        }

        struct TestTag3 : IComponentData
        {
        }

        struct TestTag4 : IComponentData
        {
        }

        struct TestTag5 : IComponentData
        {
        }

        struct TestTag6 : IComponentData
        {
        }

        struct TestTag7 : IComponentData
        {
        }

        struct TestTag8 : IComponentData
        {
        }

        struct TestTag9 : IComponentData
        {
        }

        struct TestTag10 : IComponentData
        {
        }

        struct TestTag11 : IComponentData
        {
        }

        struct TestTag12 : IComponentData
        {
        }

        struct TestTag13 : IComponentData
        {
        }

        struct TestTag14 : IComponentData
        {
        }

        struct TestTag15 : IComponentData
        {
        }

        struct TestTag16 : IComponentData
        {
        }

        struct TestTag17 : IComponentData
        {
        }

        Type[] TagTypes =
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
            typeof(TestTag17),
        };

        NativeArray<EntityArchetype> CreateUniqueArchetypes(int size)
        {
            var archetypes = new NativeArray<EntityArchetype>(size, Allocator.TempJob);

            for (int i = 0; i < size; i++)
            {
                var typeCount = CollectionHelper.Log2Ceil(i);
                var typeList = new List<ComponentType>();
                for (int typeIndex = 0; typeIndex < typeCount; typeIndex++)
                {
                    if ((i & (1 << typeIndex)) != 0)
                        typeList.Add(TagTypes[typeIndex]);
                }

                typeList.Add(typeof(EcsTestData));
                typeList.Add(typeof(EcsTestSharedComp));

                var types = typeList.ToArray();
                archetypes[i] = m_Manager.CreateArchetype(types);
            }

            return archetypes;
        }

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
                        typeList.Add(TagTypes[typeIndex]);
                }

                typeList.Add(typeof(EcsTestData));
                typeList.Add(typeof(EcsTestSharedComp));

                var types = typeList.ToArray();
                queries[i] = m_Manager.CreateEntityQuery(types);
            }

            return queries;
        }

        [Test, Performance]
        public void CreateDestroyEntity_Scaling([Values(10, 100)] int archetypeCount, [Values(10, 100)] int queryCount)
        {
            const int kInitialEntityCount = 5000000;
            const int kCreateDestroyEntityCount = 200000;

            using(var archetypes = CreateUniqueArchetypes(archetypeCount))
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

        [Test, Performance]
        public void IJobChunkPerformance_Scheduling([Values(100, 10000, 5000000)] int entityCount, [Values(10, 100)] int archetypeCount)
        {
            using (var archetypes = CreateUniqueArchetypes(archetypeCount))
            using (var basicQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp)))
            {
                for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
                {
                    m_Manager.CreateEntity(archetypes[archetypeIndex], entityCount / archetypeCount);
                }
                var handle = default(JobHandle);
                Measure.Method(() =>
                    {
                        handle = new TestChunkJob
                        {
                            EcsTestDataRW = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                        }.Schedule(basicQuery, handle);
                    })
                    .CleanUp(() => { handle.Complete(); })
                    .WarmupCount(1)
                    .Run();
            }
        }

        [Test, Performance]
        public void IJobChunk_Performance_Executing([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount, [Values(true, false)] bool enableQueryFiltering)
        {
            using (var archetypes = CreateUniqueArchetypes(archetypeCount))
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
                    .WarmupCount(1)
                    .Run();
            }
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

        [Test, Performance]
        public void IJobEntityBatch_Performance_Scheduling([Values(100, 10000, 5000000)] int entityCount, [Values(10, 100)] int archetypeCount)
        {
            using (var archetypes = CreateUniqueArchetypes(archetypeCount))
            using (var basicQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp)))
            {
                for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
                {
                    m_Manager.CreateEntity(archetypes[archetypeIndex], entityCount / archetypeCount);
                }

                var handle = default(JobHandle);
                Measure.Method(() =>
                    {
                        handle = new TestEntityBatchJob
                        {
                            EcsTestDataRW = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                        }.ScheduleParallel(basicQuery, 1, handle);
                    })
                    .CleanUp(() => { handle.Complete(); })
                    .WarmupCount(1)
                    .Run();
            }
        }

        [Test, Performance]
        public void IJobEntityBatch_Performance_Executing([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount, [Values(true,false)] bool enableQueryFiltering)
        {
            using (var archetypes = CreateUniqueArchetypes(archetypeCount))
            using (var basicQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp)))
            {
                for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
                {
                    m_Manager.CreateEntity(archetypes[archetypeIndex], entityCount / archetypeCount);
                }

                {
                    if (enableQueryFiltering)
                        basicQuery.SetSharedComponentFilter(default(EcsTestSharedComp));

                    Measure.Method(() =>
                        {
                            new TestEntityBatchJob
                            {
                                EcsTestDataRW = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                            }.Run(basicQuery);
                        })
                        .WarmupCount(1)
                        .Run();
                }
            }
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

        [Test, Performance]
        public void IJobEntityBatchWithIndex_Performance_Scheduling([Values(100, 10000, 5000000)] int entityCount, [Values(10, 100)] int archetypeCount)
        {
            using (var archetypes = CreateUniqueArchetypes(archetypeCount))
            using (var basicQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp)))
            {
                for (int archetypeIndex = 0; archetypeIndex < archetypeCount; ++archetypeIndex)
                {
                    m_Manager.CreateEntity(archetypes[archetypeIndex], entityCount / archetypeCount);
                }
                var handle = default(JobHandle);
                Measure.Method(() =>
                    {
                        handle = new TestEntityBatchWithIndexJob
                        {
                            EcsTestDataRW = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
                        }.ScheduleParallel(basicQuery, 1, handle);
                    })
                    .CleanUp(() => { handle.Complete(); })
                    .WarmupCount(1)
                    .Run();
            }
        }

        [Test, Performance]
        public void IJobEntityBatchWithIndex_Performance_Executing([Values(100, 10000, 5000000)] int entityCount,
            [Values(10, 100)] int archetypeCount, [Values(true, false)] bool enableQueryFiltering)
        {
            using (var archetypes = CreateUniqueArchetypes(archetypeCount))
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
                    .WarmupCount(1)
                    .Run();
            }
        }
    }
}
