using System;
using Unity.Entities.Tests;
using Unity.PerformanceTesting;
using Unity.Collections;
using NUnit.Framework;
using Unity.Burst;
using Unity.Jobs;

namespace Unity.Entities.PerformanceTests
{
    public partial class LambdaJobsTestFixture : ECSTestsFixture
    {
        public enum ScheduleType
        {
            Run,
            Schedule,
            ScheduleParallel
        }

        protected partial class TestComponentSystem : SystemBase
        {
            protected override void OnUpdate()
            {
            }

            public void OneComponentLambda(ScheduleType scheduleType)
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

            public void TwoComponentLambda(ScheduleType scheduleType)
            {
                switch (scheduleType)
            {
                    case ScheduleType.Run:
                Entities.ForEach((Entity entity, ref EcsTestFloatData d1, ref EcsTestFloatData2 d2) =>
                {
                    d1.Value++;
                    d2.Value0++;
                }).Run();
                        break;
                    case ScheduleType.Schedule:
                        Entities.ForEach((Entity entity, ref EcsTestFloatData d1, ref EcsTestFloatData2 d2) =>
                        {
                            d1.Value++;
                            d2.Value0++;
                        }).Schedule();
                        CompleteDependency();
                        break;
                    case ScheduleType.ScheduleParallel:
                        Entities.ForEach((Entity entity, ref EcsTestFloatData d1, ref EcsTestFloatData2 d2) =>
                        {
                            d1.Value++;
                            d2.Value0++;
                        }).ScheduleParallel();
                        CompleteDependency();
                        break;
                }
            }

            public void ThreeComponentLambda(ScheduleType scheduleType)
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

        protected TestComponentSystem TestSystem => World.GetOrCreateSystem<TestComponentSystem>();
    }

    [Category("Performance")]
    partial class LambdaJobsPerformanceTests : LambdaJobsTestFixture
    {
        // Tests the performance of the LambdaJobs ForEach & ForEach on ReadOnly components
        // No structural change expected
        [Test, Performance]
        [Category("Performance")]
        public void LambdaJobsForEach_Performance_OneComponent(
            [Values(ScheduleType.Run, ScheduleType.Schedule, ScheduleType.ScheduleParallel)] ScheduleType scheduleType, [Values(1, 1000, 100000)] int entityCount)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestFloatData));
            using (var entities = new NativeArray<Entity>(entityCount, Allocator.TempJob))
            {
                m_Manager.CreateEntity(archetype, entities);
                Measure.Method(() => { TestSystem.OneComponentLambda(scheduleType); }).WarmupCount(5).MeasurementCount(100).Run();
            }
            }

        [Test, Performance]
        [Category("Performance")]
        public void LambdaJobsForEach_Performance_TwoComponents(
            [Values(ScheduleType.Run, ScheduleType.Schedule, ScheduleType.ScheduleParallel)] ScheduleType scheduleType, [Values(1, 1000, 100000)] int entityCount)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestFloatData), typeof(EcsTestFloatData2));
            using (var entities = new NativeArray<Entity>(entityCount, Allocator.TempJob))
            {
                m_Manager.CreateEntity(archetype, entities);
                Measure.Method(() => { TestSystem.TwoComponentLambda(scheduleType); }).WarmupCount(5).MeasurementCount(100).Run();
            }
        }

        [Test, Performance]
        [Category("Performance")]
        public void LambdaJobsForEach_Performance_ThreeComponents(
            [Values(ScheduleType.Run, ScheduleType.Schedule, ScheduleType.ScheduleParallel)] ScheduleType scheduleType, [Values(1, 1000, 100000)] int entityCount)
                {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestFloatData), typeof(EcsTestFloatData2), typeof(EcsTestFloatData3));
            using (var entities = new NativeArray<Entity>(entityCount, Allocator.TempJob))
                        {
                m_Manager.CreateEntity(archetype, entities);
                Measure.Method(() => { TestSystem.ThreeComponentLambda(scheduleType); }).WarmupCount(5).MeasurementCount(100).Run();
            }
        }

        // Tests the performance of the LambdaJobs ForEach & ForEach on ReadOnly components
        // Also tests capturing a pointer (could affect bursted performance if NoAlias not applied correctly)
        [Test, Performance]
        [Category("Performance")]
        public void LambdaJobsForEach_Performance_Simple([Values(true, false)] bool withPointerCapture)
        {
            EntityArchetype archetype = m_Manager.CreateArchetype(typeof(EcsTestFloatData), typeof(EcsTestFloatData2), typeof(EcsTestFloatData3));

            var entityCount = 1000000;
            using (var entities = new NativeArray<Entity>(entityCount, Allocator.TempJob))
            {
                m_Manager.CreateEntity(archetype, entities);

                if (withPointerCapture)
                {
                    Measure.Method(() => { TestSystem.SimpleLambdaWithPointerCapture(); })
                        .WarmupCount(5)
                        .MeasurementCount(100)
                        .SampleGroup("LambdaJobsForEach_Performance_WithPointerCapture")
                        .Run();
                }
                else
                {
                    Measure.Method(() => { TestSystem.SimpleLambda(); })
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
            EntityArchetype archetype = new EntityArchetype();
            archetype = m_Manager.CreateArchetype();
            using (var entities = new NativeArray<Entity>(entityCount, Allocator.TempJob))
            {
                m_Manager.CreateEntity(archetype, entities);
                if (withECB)
                {
                    Measure.Method(() =>
                    {
                        TestSystem.StructuralChangesWithECB(m_Manager);
                    })
                        .SampleGroup("StructuralChangesWithECB")
                        .Run();
                }
                else
                {
                    Measure.Method(() =>
                    {
                        TestSystem.StructuralChangesInLambda(m_Manager);
                    })
                        .SampleGroup("StructuralChangesInLambda")
                        .Run();
                }
            }
        }
    }
}
