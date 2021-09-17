using System;
using Unity.Entities.Tests;
using Unity.PerformanceTesting;
using Unity.Collections;
using NUnit.Framework;
using Unity.Burst;
using Unity.Jobs;

namespace Unity.Entities.PerformanceTests
{
    public partial class EntitiesTestsFixture : ECSTestsFixture
    {
        public enum ScheduleType
        {
            Run,
            Schedule,
            ScheduleParallel
        }

        protected partial class LambdaJobsTestComponentSystem : SystemBase
        {
            protected override void OnUpdate()
            {
            }

            public void WriteToOneComponentLambda(ScheduleType scheduleType)
            {
                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        Entities.ForEach((Entity entity, ref EcsTestFloatData d1) =>
                        {
                            d1.Value++;
                        }).Run();
                        break;
                    case ScheduleType.Schedule:
                        Entities.ForEach((Entity entity, ref EcsTestFloatData d1) =>
                        {
                            d1.Value++;
                        }).Schedule();
                        CompleteDependency();
                        break;
                    case ScheduleType.ScheduleParallel:
                        Entities.ForEach((Entity entity, ref EcsTestFloatData d1) =>
                        {
                            d1.Value++;
                        }).ScheduleParallel();
                        CompleteDependency();
                        break;
                }
            }

            public void ReadOneWriteOneComponentLambda(ScheduleType scheduleType)
            {
                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        Entities.ForEach((Entity entity, ref EcsTestFloatData d1, in EcsTestFloatData2 d2) =>
                        {
                            d1.Value = d2.Value0 * 2;
                        }).Run();
                        break;
                    case ScheduleType.Schedule:
                        Entities.ForEach((Entity entity, ref EcsTestFloatData d1, in EcsTestFloatData2 d2) =>
                        {
                            d1.Value = d2.Value0 * 2;
                        }).Schedule();
                        CompleteDependency();
                        break;
                    case ScheduleType.ScheduleParallel:
                        Entities.ForEach((Entity entity, ref EcsTestFloatData d1, in EcsTestFloatData2 d2) =>
                        {
                            d1.Value = d2.Value0 * 2;
                        }).ScheduleParallel();
                        CompleteDependency();
                        break;
                }
            }

            public void WriteThreeComponentLambda(ScheduleType scheduleType)
            {
                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        Entities.ForEach((Entity entity, ref EcsTestFloatData d1, ref EcsTestFloatData2 d2, ref EcsTestFloatData3 d3) =>
                        {
                            d1.Value++;
                            d2.Value0++;
                            d3.Value0++;
                        }).Run();
                        break;
                    case ScheduleType.Schedule:
                        Entities.ForEach((Entity entity, ref EcsTestFloatData d1, ref EcsTestFloatData2 d2, ref EcsTestFloatData3 d3) =>
                        {
                            d1.Value++;
                            d2.Value0++;
                            d3.Value0++;
                        }).Schedule();
                        CompleteDependency();
                        break;
                    case ScheduleType.ScheduleParallel:
                        Entities.ForEach((Entity entity, ref EcsTestFloatData d1, ref EcsTestFloatData2 d2, ref EcsTestFloatData3 d3) =>
                        {
                            d1.Value++;
                            d2.Value0++;
                            d3.Value0++;
                        }).ScheduleParallel();
                        CompleteDependency();
                        break;
                }
            }

            public void SimpleLambda()
            {
                Entities.ForEach((Entity entity, ref EcsTestFloatData d1, ref EcsTestFloatData2 d2, ref EcsTestFloatData3 d3) =>
                {
                    d1.Value = d2.Value0 + d3.Value0;
                }).Run();
            }

            public unsafe void SimpleLambdaWithPointerCapture()
            {
                byte* innerRawPtr = (byte*)IntPtr.Zero;
                Entities
                    .WithNativeDisableUnsafePtrRestriction(innerRawPtr)
                    .ForEach((Entity entity, ref EcsTestFloatData d1, ref EcsTestFloatData2 d2, ref EcsTestFloatData3 d3) =>
                    {
                        if (innerRawPtr == null)
                            d1.Value = d2.Value0 + d3.Value0;
                    }).Run();
            }

            public void StructuralChangesWithECB(EntityManager manager)
            {
                {
                    var ecb = new EntityCommandBuffer(Allocator.Temp, -1, PlaybackPolicy.SinglePlayback);
                    Entities
                        .ForEach((Entity entity) =>
                        {
                            ecb.AddComponent<EcsTestFloatData>(entity);
                        }).Run();
                    ecb.Playback(manager);
                }
                {
                    var ecb = new EntityCommandBuffer(Allocator.Temp, -1, PlaybackPolicy.SinglePlayback);
                    Entities
                        .ForEach((Entity entity) =>
                    {
                        ecb.RemoveComponent<EcsTestFloatData>(entity);
                    }).Run();
                    ecb.Playback(manager);
                }
            }

            public void StructuralChangesInLambda(EntityManager manager)
            {
                Entities
                    .WithStructuralChanges()
                    .ForEach((Entity entity) =>
                    {
                        manager.AddComponent<EcsTestFloatData>(entity);
                    }).Run();
                Entities
                    .WithStructuralChanges()
                    .ForEach((Entity entity) =>
                    {
                        manager.RemoveComponent<EcsTestFloatData>(entity);
                    }).Run();
            }
        }

        protected LambdaJobsTestComponentSystem LambdaJobsTestSystem => World.GetOrCreateSystem<LambdaJobsTestComponentSystem>();
    }

    [Category("Performance")]
    partial class LambdaJobsPerformanceTests : EntitiesTestsFixture
    {
        [Test, Performance]
        [Category("Performance")]
        public void LambdaJobsForEach_Performance_WriteToOneComponentLambda(
            [Values(ScheduleType.Run, ScheduleType.Schedule, ScheduleType.ScheduleParallel)] ScheduleType scheduleType, [Values(1, 1000, 100000)] int entityCount)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestFloatData));
            using (var entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, World.UpdateAllocator.ToAllocator))
            {
                m_Manager.CreateEntity(archetype, entities);
                Measure.Method(() => { LambdaJobsTestSystem.WriteToOneComponentLambda(scheduleType); }).WarmupCount(5).MeasurementCount(100).Run();
            }
        }

        [Test, Performance]
        [Category("Performance")]
        public void LambdaJobsForEach_Performance_ReadOneWriteOneComponentLambda(
            [Values(ScheduleType.Run, ScheduleType.Schedule, ScheduleType.ScheduleParallel)] ScheduleType scheduleType, [Values(1, 1000, 100000)] int entityCount)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestFloatData), typeof(EcsTestFloatData2));
            using (var entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, World.UpdateAllocator.ToAllocator))
            {
                m_Manager.CreateEntity(archetype, entities);
                Measure.Method(() => { LambdaJobsTestSystem.ReadOneWriteOneComponentLambda(scheduleType); }).WarmupCount(5).MeasurementCount(100).Run();
            }
        }

        [Test, Performance]
        [Category("Performance")]
        public void LambdaJobsForEach_Performance_WriteThreeComponentLambda(
            [Values(ScheduleType.Run, ScheduleType.Schedule, ScheduleType.ScheduleParallel)] ScheduleType scheduleType, [Values(1, 1000, 100000)] int entityCount)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestFloatData), typeof(EcsTestFloatData2), typeof(EcsTestFloatData3));
            using (var entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, World.UpdateAllocator.ToAllocator))
            {
                m_Manager.CreateEntity(archetype, entities);
                Measure.Method(() => { LambdaJobsTestSystem.WriteThreeComponentLambda(scheduleType); }).WarmupCount(5).MeasurementCount(100).Run();
            }
        }

        // Tests the performance of the LambdaJobs ForEach & ForEach on ReadOnly components
        // Also tests capturing a pointer (could affect bursted performance if NoAlias not applied correctly)
        [Test, Performance]
        [Category("Performance")]
        public void LambdaJobsForEach_Performance_Simple([Values(true, false)] bool withPointerCapture)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestFloatData), typeof(EcsTestFloatData2), typeof(EcsTestFloatData3));
            var entityCount = 1000000;
            using (var entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, World.UpdateAllocator.ToAllocator))
            {
                m_Manager.CreateEntity(archetype, entities);

                if (withPointerCapture)
                {
                    Measure.Method(() => { LambdaJobsTestSystem.SimpleLambdaWithPointerCapture(); })
                        .WarmupCount(5)
                        .MeasurementCount(100)
                        .SampleGroup("LambdaJobsForEach_Performance_WithPointerCapture")
                        .Run();
                }
                else
                {
                    Measure.Method(() => { LambdaJobsTestSystem.SimpleLambda(); })
                        .WarmupCount(5)
                        .MeasurementCount(100)
                        .SampleGroup("LambdaJobsForEach_Performance_Simple")
                        .Run();
                }
            }
        }

        [Test, Performance]
        [Category("Performance")]
        public void LambdaJobsForEachStructuralChanges_Performance_InLambda_vs_WithECB([Values(1, 1000, 10000)] int entityCount, [Values(true, false)] bool withECB)
        {
            var archetype = new EntityArchetype();
            archetype = m_Manager.CreateArchetype();
            using (var entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, World.UpdateAllocator.ToAllocator))
            {
                m_Manager.CreateEntity(archetype, entities);
                if (withECB)
                {
                    Measure.Method(() =>
                    {
                        LambdaJobsTestSystem.StructuralChangesWithECB(m_Manager);
                    })
                        .SampleGroup("StructuralChangesWithECB")
                        .Run();
                }
                else
                {
                    Measure.Method(() =>
                    {
                        LambdaJobsTestSystem.StructuralChangesInLambda(m_Manager);
                    })
                        .SampleGroup("StructuralChangesInLambda")
                        .Run();
                }
            }
        }

        protected partial class TestSystem3_EB : SystemBase
        {
            public int Count;
            protected override void OnUpdate()
            {
                int count = 0;
                Entities
                    .ForEach((Entity entity, ref EcsTestData d1, ref EcsTestData2 d2 , ref EcsTestDataEnableable3 d3) =>
                    {
                        count++;
                    }).Run();
                Count = count;
            }
        }
        protected partial class TestSystem3_EB_RO : SystemBase
        {
            public int Count;
            protected override void OnUpdate()
            {
                int count = 0;
                Entities
                    .ForEach((Entity entity, in EcsTestData d1, in EcsTestData2 d2, in EcsTestDataEnableable3 d3) =>
                    {
                        count++;
                    }).Run();
                Count = count;
            }
        }

        [Test, Performance]
        [Category("Performance")]
        public void ForEach_Overhead_EnabledBits([Values(0,1,2,4,8,16,32,64,128,256,512)] int disableEveryNth)
        {
            EntityArchetype archetype = m_Manager.CreateArchetype(typeof(EcsTestData),
                typeof(EcsTestData2), typeof(EcsTestDataEnableable3));

            int entityCount = 100000;
            using var entities = new NativeArray<Entity>(entityCount, Allocator.TempJob);
            m_Manager.CreateEntity(archetype, entities);
            if (disableEveryNth > 0)
            {
                for (int i = 0; i < entityCount; i += disableEveryNth)
                {
                    m_Manager.SetComponentEnabled<EcsTestDataEnableable3>(entities[i], false);
                }
            }

            var sys3 = World.GetOrCreateSystem<TestSystem3_EB>();
            Measure.Method(() =>
                {
                    sys3.Update();
                })
                .WarmupCount(1)
                .MeasurementCount(100)
                .SampleGroup("ForEach")
                .Run();

            var sys3RO = World.GetOrCreateSystem<TestSystem3_EB_RO>();
            Measure.Method(() =>
                {
                    sys3RO.Update();
                })
                .WarmupCount(1)
                .MeasurementCount(100)
                .SampleGroup("ForEachRO")
                .Run();
        }
    }
}
