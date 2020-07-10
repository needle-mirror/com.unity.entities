using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Tests;
using Unity.PerformanceTesting;

namespace Unity.Entities.PerformanceTests
{
    [Category("Performance")]
    class ComponentDataFromEntityPerformanceTests : EntityQueryBuilderTestFixture
    {

        enum ScheduleMode
        {
            Parallel, Single, Run
        }

        class PerfTestSystem : SystemBase
        {
            public bool ReadOnly;
            public ScheduleMode Schedule;
            protected override void OnUpdate()
            {
                if (ReadOnly)
                {
                    var lookup = GetComponentDataFromEntity<EcsTestData>(true);
                    if (Schedule == ScheduleMode.Run)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            data.value0 += lookup[data.value1].value;
                        }).Run();
                    }
                    else if (Schedule == ScheduleMode.Parallel)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            data.value0 += lookup[data.value1].value;
                        }).ScheduleParallel();
                        CompleteDependency();
                    }
                    else if (Schedule == ScheduleMode.Single)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            data.value0 += lookup[data.value1].value;
                        }).Schedule();
                        CompleteDependency();
                    }

                }
                else
                {
                    var lookup = GetComponentDataFromEntity<EcsTestData>(false);

                    if (Schedule == ScheduleMode.Run)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            lookup[data.value1] = new EcsTestData(5);
                        }).Run();
                    }
                    else if (Schedule == ScheduleMode.Parallel)
                    {
                        Entities.WithNativeDisableParallelForRestriction(lookup).ForEach((ref EcsTestDataEntity data) =>
                        {
                            lookup[data.value1] = new EcsTestData(5);
                        }).ScheduleParallel();
                        CompleteDependency();
                    }
                    else if (Schedule == ScheduleMode.Single)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            lookup[data.value1] = new EcsTestData(5);
                        }).Schedule();
                        CompleteDependency();
                    }
                }
            }
        }

        void RunTest(bool readOnly, ScheduleMode schedule)
        {
            var system = World.GetOrCreateSystem<PerfTestSystem>();
            var name = (readOnly ? "ReadOnly" : "Write") + "_" + schedule.ToString();
            Measure.Method(() =>
            {
                system.ReadOnly = false;
                system.Schedule = schedule;
                system.Update();
            })
            .SampleGroup(name)
            .MeasurementCount(5)
            .IterationsPerMeasurement(1)
            .Run();
        }

        [Test, Performance]
        [Category("Performance")] // bug: this redundant category here required because our current test runner ignores Category on a fixture for generated test methods
        public void TestWriteComponentDataFromEntity([Values(10000, 1000000)] int entityCount)
        {
            var targetArchetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3), typeof(EcsTestData4), typeof(EcsTestData5));
            var targetEntities = m_Manager.CreateEntity(targetArchetype, entityCount, Allocator.TempJob);

            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEntity));
            var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.TempJob);

            for (int i = 0;i != entityCount;i++)
                m_Manager.SetComponentData(entities[i], new EcsTestDataEntity { value1 = targetEntities[i] });

            targetEntities.Dispose();
            entities.Dispose();

            RunTest(true, ScheduleMode.Run);
            RunTest(true, ScheduleMode.Single);
            RunTest(true, ScheduleMode.Parallel);

            RunTest(false, ScheduleMode.Run);
            RunTest(false, ScheduleMode.Single);
            RunTest(false, ScheduleMode.Parallel);
        }
    }
}
