using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.TestTools;
#if !NET_DOTS
using System.Text.RegularExpressions;
#endif

namespace Unity.Entities.Tests
{
    partial class IJobChunkTests : ECSTestsFixture
    {
        struct ProcessChunks : IJobChunk
        {
            public ComponentTypeHandle<EcsTestData> EcsTestTypeHandle;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int entityOffset)
            {
                var testDataArray = chunk.GetNativeArray(EcsTestTypeHandle);
                testDataArray[0] = new EcsTestData
                {
                    value = 5
                };
            }
        }

        [Test]
        public void IJobChunkProcess()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var group = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                Any = Array.Empty<ComponentType>(),
                None = Array.Empty<ComponentType>(),
                All = new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) }
            });

            var entity = m_Manager.CreateEntity(archetype);
            var job = new ProcessChunks
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.Run(group);

            Assert.AreEqual(5, m_Manager.GetComponentData<EcsTestData>(entity).value);
        }

        [Test]
        public void IJobChunk_Run_WorksWithMultipleChunks()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));
            var group = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                Any = Array.Empty<ComponentType>(),
                None = Array.Empty<ComponentType>(),
                All = new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) }
            });

            const int entityCount = 10;
            var entities = new NativeArray<Entity>(entityCount, Allocator.Temp);

            m_Manager.CreateEntity(archetype, entities);

            for (int i = 0; i < entityCount; ++i)
                m_Manager.SetSharedComponentData(entities[i], new EcsTestSharedComp(i));

            var job = new ProcessChunks
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.Run(group);

            for (int i = 0; i < entityCount; ++i)
                Assert.AreEqual(5, m_Manager.GetComponentData<EcsTestData>(entities[i]).value);

            entities.Dispose();
        }

        [Test]
        public void IJobChunkProcessFiltered()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));

            var entity1 = m_Manager.CreateEntity(archetype);
            var entity2 = m_Manager.CreateEntity(archetype);

            m_Manager.SetSharedComponentData<SharedData1>(entity1, new SharedData1 { value = 10 });
            m_Manager.SetComponentData<EcsTestData>(entity1, new EcsTestData { value = 10 });

            m_Manager.SetSharedComponentData<SharedData1>(entity2, new SharedData1 { value = 20 });
            m_Manager.SetComponentData<EcsTestData>(entity2, new EcsTestData { value = 20 });

            group.SetSharedComponentFilter(new SharedData1 { value = 20 });

            var job = new ProcessChunks
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.Schedule(group).Complete();

            Assert.AreEqual(10, m_Manager.GetComponentData<EcsTestData>(entity1).value);
            Assert.AreEqual(5,  m_Manager.GetComponentData<EcsTestData>(entity2).value);

            group.Dispose();
        }

        [Test]
        public void IJobChunkWithEntityOffsetCopy()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2));

            var entities = new NativeArray<Entity>(50000, Allocator.Temp);
            m_Manager.CreateEntity(archetype, entities);

            for (int i = 0; i < 50000; ++i)
                m_Manager.SetComponentData(entities[i], new EcsTestData { value = i });

            entities.Dispose();

            var copyIndices = group.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);

            for (int i = 0; i < 50000; ++i)
                Assert.AreEqual(copyIndices[i].value, i);
        }

        struct ProcessChunkIndex : IJobChunk
        {
            public ComponentTypeHandle<EcsTestData> EcsTestTypeHandle;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int entityOffset)
            {
                var testDataArray = chunk.GetNativeArray(EcsTestTypeHandle);
                testDataArray[0] = new EcsTestData
                {
                    value = chunkIndex
                };
            }
        }

        struct ProcessEntityOffset : IJobChunk
        {
            public ComponentTypeHandle<EcsTestData> EcsTestTypeHandle;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int entityOffset)
            {
                var testDataArray = chunk.GetNativeArray(EcsTestTypeHandle);
                for (int i = 0; i < chunk.Count; ++i)
                {
                    testDataArray[i] = new EcsTestData
                    {
                        value = entityOffset
                    };
                }
            }
        }

        [Test]
        public void IJobChunkProcessChunkIndexWithFilter()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));

            var entity1 = m_Manager.CreateEntity(archetype);
            var entity2 = m_Manager.CreateEntity(archetype);

            m_Manager.SetSharedComponentData<SharedData1>(entity1, new SharedData1 { value = 10 });
            m_Manager.SetComponentData<EcsTestData>(entity1, new EcsTestData { value = 10 });

            m_Manager.SetSharedComponentData<SharedData1>(entity2, new SharedData1 { value = 20 });
            m_Manager.SetComponentData<EcsTestData>(entity2, new EcsTestData { value = 20 });

            group.SetSharedComponentFilter(new SharedData1 { value = 10 });

            var job = new ProcessChunkIndex
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.Schedule(group).Complete();

            group.SetSharedComponentFilter(new SharedData1 { value = 20 });
            job.Schedule(group).Complete();

            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity1).value);
            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity2).value);

            group.Dispose();
        }

        [Test]
        public void IJobChunkProcessChunkIndexWithFilterRun()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));

            var entity1 = m_Manager.CreateEntity(archetype);
            var entity2 = m_Manager.CreateEntity(archetype);

            m_Manager.SetSharedComponentData<SharedData1>(entity1, new SharedData1 { value = 10 });
            m_Manager.SetComponentData<EcsTestData>(entity1, new EcsTestData { value = 10 });

            m_Manager.SetSharedComponentData<SharedData1>(entity2, new SharedData1 { value = 20 });
            m_Manager.SetComponentData<EcsTestData>(entity2, new EcsTestData { value = 20 });

            group.SetSharedComponentFilter(new SharedData1 { value = 10 });

            var job = new ProcessChunkIndex
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.Run(group);

            group.SetSharedComponentFilter(new SharedData1 { value = 20 });
            job.Run(group);

            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity1).value);
            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity2).value);

            group.Dispose();
        }

        [Test]
        public void IJobChunkProcessChunkIndex()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));

            var entity1 = m_Manager.CreateEntity(archetype);
            var entity2 = m_Manager.CreateEntity(archetype);

            m_Manager.SetSharedComponentData<SharedData1>(entity1, new SharedData1 { value = 10 });
            m_Manager.SetComponentData<EcsTestData>(entity1, new EcsTestData { value = 10 });

            m_Manager.SetSharedComponentData<SharedData1>(entity2, new SharedData1 { value = 20 });
            m_Manager.SetComponentData<EcsTestData>(entity2, new EcsTestData { value = 20 });

            var job = new ProcessChunkIndex
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            // ScheduleSingle forces all chunks to run on a single thread, so the for loop in IJobChunk.ExecuteInternal() has >1 iteration.
            job.ScheduleSingle(group).Complete();

            int[] values =
            {
                m_Manager.GetComponentData<EcsTestData>(entity1).value,
                m_Manager.GetComponentData<EcsTestData>(entity2).value,
            };
            CollectionAssert.AreEquivalent(values, new int[] {0, 1});

            group.Dispose();
        }

        [Test]
        public void IJobChunkProcessEntityOffset()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));
            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(SharedData1));

            var entity1 = m_Manager.CreateEntity(archetype);
            var entity2 = m_Manager.CreateEntity(archetype);

            m_Manager.SetSharedComponentData<SharedData1>(entity1, new SharedData1 { value = 10 });
            m_Manager.SetComponentData<EcsTestData>(entity1, new EcsTestData { value = 10 });

            m_Manager.SetSharedComponentData<SharedData1>(entity2, new SharedData1 { value = 20 });
            m_Manager.SetComponentData<EcsTestData>(entity2, new EcsTestData { value = 20 });

            group.SetSharedComponentFilter(new SharedData1 { value = 10 });

            var job = new ProcessEntityOffset
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.Schedule(group).Complete();

            group.SetSharedComponentFilter(new SharedData1 { value = 20 });
            job.Schedule(group).Complete();

            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity1).value);
            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity2).value);

            group.Dispose();
        }

        [Test]
        public void IJobChunkProcessChunkMultiArchetype()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var archetypeC = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3));

            var entity1A = m_Manager.CreateEntity(archetypeA);
            var entity2A = m_Manager.CreateEntity(archetypeA);
            var entityB = m_Manager.CreateEntity(archetypeB);
            var entityC = m_Manager.CreateEntity(archetypeC);

            EntityQuery query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            m_Manager.SetComponentData<EcsTestData>(entity1A, new EcsTestData { value = -1 });
            m_Manager.SetComponentData<EcsTestData>(entity2A, new EcsTestData { value = -1 });
            m_Manager.SetComponentData<EcsTestData>(entityB,  new EcsTestData { value = -1 });
            m_Manager.SetComponentData<EcsTestData>(entityC,  new EcsTestData { value = -1 });

            var job = new ProcessEntityOffset
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.Schedule(query).Complete();

            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity1A).value);
            Assert.AreEqual(0,  m_Manager.GetComponentData<EcsTestData>(entity2A).value);
            Assert.AreEqual(2,  m_Manager.GetComponentData<EcsTestData>(entityB).value);
            Assert.AreEqual(3,  m_Manager.GetComponentData<EcsTestData>(entityC).value);

            query.Dispose();
        }

        struct ProcessChunkWriteIndex : IJobChunk
        {
            public ComponentTypeHandle<EcsTestData> EcsTestTypeHandle;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int entityOffset)
            {
                var testDataArray = chunk.GetNativeArray(EcsTestTypeHandle);
                for (int i = 0; i < chunk.Count; ++i)
                {
                    testDataArray[i] = new EcsTestData
                    {
                        value = entityOffset + i
                    };
                }
            }
        }

        struct WriteToArray : IJobChunk
        {
            public NativeArray<int> MyArray;
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                for (int i = 0; i < MyArray.Length; i++)
                {
                    MyArray[i] = chunkIndex + firstEntityIndex;
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

            using (var local = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(archetypeA.ChunkCapacity * 2, ref World.UpdateAllocator))
            {
                LogAssert.Expect(LogType.Exception, new Regex("IndexOutOfRangeException: *"));

                new WriteToArray
                {
                    MyArray = local
                }.ScheduleParallel(query).Complete();
            }
        }

        [Test]
        public void SingleArrayWriteDoesNotTriggerSafetySystem()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entitiesA = new NativeArray<Entity>(archetypeA.ChunkCapacity, Allocator.Temp);
            m_Manager.CreateEntity(archetypeA, entitiesA);
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            using (var local = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(archetypeA.ChunkCapacity * 2, ref World.UpdateAllocator))
            {
                new WriteToArray
                {
                    MyArray = local
                }.ScheduleSingle(query).Complete();
            }
        }
#endif

#if !UNITY_DOTSRUNTIME
        public partial class RewriteEcsTestDataSystem : SystemBase
        {
            public void RewriteData(JobHandle inputDeps = default)
            {
                Entities
                    .WithSharedComponentFilter(new EcsTestSharedComp { value = 1 })
                    .WithoutBurst()
                    .ForEach((Entity entity, int entityInQueryIndex, ref EcsTestData data, in EcsTestSharedComp _) =>
                    {
                        data = new EcsTestData { value = entityInQueryIndex };
                    }).Run();
            }

            protected override void OnUpdate()
            {
            }
        }

        RewriteEcsTestDataSystem _rewriteEcsTestDataSystem => World.GetOrCreateSystem<RewriteEcsTestDataSystem>();

        [Test]
        public void FilteredIJobChunkProcessesSameChunksAsFilteredJobForEach()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var archetypeB = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));

            var entitiesA = new NativeArray<Entity>(5000, Allocator.Temp);
            m_Manager.CreateEntity(archetypeA, entitiesA);

            var entitiesB = new NativeArray<Entity>(5000, Allocator.Temp);
            m_Manager.CreateEntity(archetypeB, entitiesB);

            for (int i = 0; i < 5000; ++i)
            {
                m_Manager.SetSharedComponentData(entitiesA[i], new EcsTestSharedComp { value = i % 8 });
                m_Manager.SetSharedComponentData(entitiesB[i], new EcsTestSharedComp { value = i % 8 });
            }

            var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp));
            entityQuery.SetSharedComponentFilter(new EcsTestSharedComp { value = 1 });

            var jobChunk = new ProcessChunkWriteIndex
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            jobChunk.Schedule(entityQuery).Complete();

            var componentArrayA = entityQuery.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);

            _rewriteEcsTestDataSystem.RewriteData();

            var componentArrayB = entityQuery.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);

            CollectionAssert.AreEqual(componentArrayA.ToArray(), componentArrayB.ToArray());

            entityQuery.Dispose();
        }

#endif // !UNITY_DOTSRUNTIME

        struct LargeJobChunk : IJobChunk
        {
            public FixedString4096Bytes StrA;
            public FixedString4096Bytes StrB;
            public FixedString4096Bytes StrC;
            public FixedString4096Bytes StrD;
            public NativeArray<int> TotalLengths;
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                TotalLengths[chunkIndex] = StrA.Length + StrB.Length + StrC.Length + StrD.Length;
            }
        }

        public enum ScheduleMode
        {
            Schedule, ScheduleParallel, ScheduleSingle, Run
        }

        [Test]
        public void IJobChunk_LargeJobStruct_ScheduleByRefWorks(
            [Values(ScheduleMode.Schedule, ScheduleMode.ScheduleParallel, ScheduleMode.ScheduleSingle, ScheduleMode.Run)] ScheduleMode scheduleMode)
        {
            m_Manager.CreateEntity(typeof(EcsTestData));
            using(var lengths = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(1, ref World.UpdateAllocator))
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                var job = new LargeJobChunk
                {
                        StrA = "A",
                        StrB = "BB",
                        StrC = "CCC",
                        StrD = "DDDD",
                        TotalLengths = lengths,
                };
                if (scheduleMode == ScheduleMode.Schedule)
                    Assert.DoesNotThrow(() => { job.ScheduleByRef(query).Complete(); });
                else if (scheduleMode == ScheduleMode.ScheduleParallel)
                    Assert.DoesNotThrow(() => { job.ScheduleParallelByRef(query).Complete(); });
                else if (scheduleMode == ScheduleMode.ScheduleSingle)
                    Assert.DoesNotThrow(() => { job.ScheduleSingleByRef(query).Complete(); });
                else if (scheduleMode == ScheduleMode.Run)
                    Assert.DoesNotThrow(() => { job.RunByRef(query); });
                Assert.AreEqual(lengths[0], 10);
            }
        }
    }
}
