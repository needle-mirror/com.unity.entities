using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities.Tests;
using Unity.PerformanceTesting;

namespace Unity.Entities.PerformanceTests
{
    [TestFixture]
    public unsafe partial class ForEachISystemPerformanceTests : ECSTestsFixture
    {
        public enum ScheduleType
        {
            Run,
            Schedule,
            ScheduleParallel
        }

        public enum SystemType
        {
            StructSystemType,
            ClassSystemType
        }

        // Test and compare the performance of Entities.ForEach in ISystem to SystemBase
        [Test, Performance]
        [Category("Performance")]
        public void ISystem_EFE_Performance([Values(10, 1000)] int jobsPerUpdate,
            [Values(ScheduleType.Run, ScheduleType.Schedule, ScheduleType.ScheduleParallel)] ScheduleType scheduleType,
            [Values(SystemType.StructSystemType, SystemType.ClassSystemType)] SystemType systemType)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestFloatData), typeof(EcsTestFloatData2), typeof(EcsTestFloatData3));
            var entityCount = 1000;

            using (var entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, World.UpdateAllocator.ToAllocator))
            {
                m_Manager.CreateEntity(archetype, entities);

                var group = World.CreateSystem<BenchmarkSystemGroup>();
                group.SystemType = systemType;
                group.IterationCount = 1;
                group.JobsPerUpdate = jobsPerUpdate;
                group.ScheduleType = scheduleType;

                Measure.Method(
                        () =>
                        {
                            group.Update();
                        })
                    .WarmupCount(1)
                    .MeasurementCount(100)
                    .Run();
            }
        }

        public partial class BenchmarkSystemGroup : ComponentSystemGroup
        {
            // Assign values to these fields post-OnCreate() based on test case settings, before the first Update()
            public int IterationCount;
            public int JobsPerUpdate;
            public SystemType SystemType;
            public ScheduleType ScheduleType;

            protected override void OnStartRunning()
            {
                base.OnStartRunning();

                for (int i = 0; i != IterationCount; i++)
                {
                    if (SystemType == SystemType.StructSystemType)
                    {
                        var res = World.AddSystem<StructTestSystem>();
                        res.Struct.LoopsPerSystem = JobsPerUpdate;
                        res.Struct.ScheduleType = ScheduleType;
                        AddUnmanagedSystemToUpdateList(res);
                    }
                    else
                    {
                        var res = World.GetOrCreateSystem<ClassTestSystem>();
                        res.LoopsPerSystem = JobsPerUpdate;
                        res.ScheduleType = ScheduleType;
                        AddSystemToUpdateList(res);
                    }
                }
            }
        }

        partial class ClassTestSystem : SystemBase
        {
            public int LoopsPerSystem;
            public ScheduleType ScheduleType;

            protected override void OnUpdate()
            {
                switch (ScheduleType)
                {
                    case ScheduleType.Run:
                        for (var i = 0; i < LoopsPerSystem; i++)
                        {
                            Entities.ForEach((Entity entity, ref EcsTestFloatData d1, in EcsTestFloatData2 d2, in EcsTestFloatData3 d3) => { d1.Value = d2.Value0 + d3.Value0; })
                                .Run();
                        }
                        break;
                    case ScheduleType.Schedule:
                        for (var i = 0; i < LoopsPerSystem; i++)
                        {
                            Entities.ForEach((Entity entity, ref EcsTestFloatData d1, in EcsTestFloatData2 d2, in EcsTestFloatData3 d3) => { d1.Value = d2.Value0 + d3.Value0; })
                                .Schedule();
                        }
                        break;
                    case ScheduleType.ScheduleParallel:
                        for (var i = 0; i < LoopsPerSystem; i++)
                        {
                            Entities.ForEach((Entity entity, ref EcsTestFloatData d1, in EcsTestFloatData2 d2, in EcsTestFloatData3 d3) => { d1.Value = d2.Value0 + d3.Value0; })
                                .ScheduleParallel();
                        }
                        break;
                }
                Dependency.Complete();
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        partial struct StructTestSystem : ISystem
        {
            public int LoopsPerSystem;
            public ScheduleType ScheduleType;

            public void OnCreate(ref SystemState state) {}
            public void OnDestroy(ref SystemState state){}

            [BurstDiscard]
            static void CheckRunningBurst()
            {
                throw new ArgumentException("Not running burst");
            }

            [BurstCompile(CompileSynchronously = true)]
            public void OnUpdate(ref SystemState state)
            {
                CheckRunningBurst();

                switch (ScheduleType)
                {
                    case ScheduleType.Run:
                        for (var i = 0; i < LoopsPerSystem; i++)
                        {
                            state.Entities.ForEach((Entity entity, ref EcsTestFloatData d1, in EcsTestFloatData2 d2, in EcsTestFloatData3 d3) => { d1.Value = d2.Value0 + d3.Value0; })
                                .Run();
                        }
                        break;
                    case ScheduleType.Schedule:
                        for (var i = 0; i < LoopsPerSystem; i++)
                        {
                            state.Entities.ForEach((Entity entity, ref EcsTestFloatData d1, in EcsTestFloatData2 d2, in EcsTestFloatData3 d3) => { d1.Value = d2.Value0 + d3.Value0; })
                                .Schedule();
                        }
                        break;
                    case ScheduleType.ScheduleParallel:
                        for (var i = 0; i < LoopsPerSystem; i++)
                        {
                            state.Entities.ForEach((Entity entity, ref EcsTestFloatData d1, in EcsTestFloatData2 d2, in EcsTestFloatData3 d3) => { d1.Value = d2.Value0 + d3.Value0; })
                                .ScheduleParallel();
                        }
                        break;
                }
                state.Dependency.Complete();
            }
        }
    }
}
