#pragma warning disable CS0618 // Disable Entities.ForEach obsolete warnings
using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Tests;
using Unity.PerformanceTesting;

namespace Unity.Entities.PerformanceTests
{
    [Category("Performance")]
    partial class BufferLookupPerformanceTests : ECSTestsFixture
    {

        enum ScheduleMode
        {
            Parallel, Single, Run
        }


        partial class TryGetPerformanceSystem : SystemBase
        {
            public bool ReadOnly;
            public ScheduleMode Schedule;
            public bool UseHasBuffer; //either use the if(hasBuffer...) bufferLookup[entity] path of the tryGetComponent path.
            protected override void OnUpdate()
            {
                if (UseHasBuffer)
                    RunHasBuffer();
                else
                    RunTryGetBuffer();
            }

            private void RunHasBuffer()
            {
                if (ReadOnly)
                {
                    var lookup = GetBufferLookup<EcsIntElement4>();
                    if (Schedule == ScheduleMode.Run)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            if(lookup.HasBuffer(data.value1))
                                data.value0 += lookup[data.value1][0].Value3;
                        }).Run();
                    }
                    else if (Schedule == ScheduleMode.Parallel)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            if(lookup.HasBuffer(data.value1))
                                data.value0 += lookup[data.value1][0].Value3;
                        }).ScheduleParallel();
                        CompleteDependency();
                    }
                    else if (Schedule == ScheduleMode.Single)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            if(lookup.HasBuffer(data.value1))
                                data.value0 += lookup[data.value1][0].Value3;
                        }).Schedule();
                        CompleteDependency();
                    }

                }
                else
                {
                    var lookup = GetBufferLookup<EcsIntElement4>(false);

                    if (Schedule == ScheduleMode.Run)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            if(lookup.HasBuffer(data.value1))
                                data.value0 = lookup[data.value1][0].Value3;
                        }).Run();
                    }
                    else if (Schedule == ScheduleMode.Parallel)
                    {
                        Entities.WithNativeDisableParallelForRestriction(lookup).ForEach((ref EcsTestDataEntity data) =>
                        {
                            if(lookup.HasBuffer(data.value1))
                                data.value0 = lookup[data.value1][0].Value3;
                        }).ScheduleParallel();
                        CompleteDependency();
                    }
                    else if (Schedule == ScheduleMode.Single)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            if(lookup.HasBuffer(data.value1))
                                data.value0 = lookup[data.value1][0].Value3;
                        }).Schedule();
                        CompleteDependency();
                    }
                }
            }

            private void RunTryGetBuffer()
            {
                if (ReadOnly)
                {
                    var lookup = GetBufferLookup<EcsIntElement4>(true);
                    if (Schedule == ScheduleMode.Run)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            if(lookup.TryGetBuffer(data.value1, out var buffer))
                                data.value0 += buffer[0].Value3;
                        }).Run();
                    }
                    else if (Schedule == ScheduleMode.Parallel)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            if(lookup.TryGetBuffer(data.value1, out var buffer))
                                data.value0 += buffer[0].Value3;
                        }).ScheduleParallel();
                        CompleteDependency();
                    }
                    else if (Schedule == ScheduleMode.Single)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            if(lookup.TryGetBuffer(data.value1, out var buffer))
                                data.value0 += buffer[0].Value3;
                        }).Schedule();
                        CompleteDependency();
                    }

                }
                else
                {
                    var lookup = GetBufferLookup<EcsIntElement4>(false);
                    if (Schedule == ScheduleMode.Run)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            if(lookup.TryGetBuffer(data.value1, out var buffer))
                                data.value0 = buffer[0].Value3;
                        }).Run();
                    }
                    else if (Schedule == ScheduleMode.Parallel)
                    {
                        Entities.WithNativeDisableParallelForRestriction(lookup).ForEach((ref EcsTestDataEntity data) =>
                        {
                            if(lookup.TryGetBuffer(data.value1, out var buffer))
                                data.value0 = buffer[0].Value3;
                        }).ScheduleParallel();
                        CompleteDependency();
                    }
                    else if (Schedule == ScheduleMode.Single)
                    {
                        Entities.ForEach((ref EcsTestDataEntity data) =>
                        {
                            if(lookup.TryGetBuffer(data.value1, out var buffer))
                                data.value0 = buffer[0].Value3;
                        }).Schedule();
                        CompleteDependency();
                    }
                }
            }
        }

        void RunHasBufferSystem(bool readOnly, bool useHasBuffer, ScheduleMode schedule)
        {
            var system = World.GetOrCreateSystemManaged<TryGetPerformanceSystem>();
            var name = (readOnly ? "ReadOnly" : "Write") + "_" + schedule.ToString();
            Measure.Method(() =>
                {
                    system.ReadOnly = false;
                    system.Schedule = schedule;
                    system.UseHasBuffer = useHasBuffer;
                    system.Update();
                })
                .SampleGroup(name)
                .MeasurementCount(10)
                .IterationsPerMeasurement(1)
                .WarmupCount(1)
                .Run();
        }

        [Test, Performance]
        [Category("Performance")] // bug: this redundant category here required because our current test runner ignores Category on a fixture for generated test methods
        public void TestHasBufferLookup([Values(10000, 1000000)] int entityCount, [Values] bool useHasBuffer)
        {
            var targetArchetype = m_Manager.CreateArchetype();

            var targetEntities = m_Manager.CreateEntity(targetArchetype, entityCount, World.UpdateAllocator.ToAllocator);

            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEntity));
            var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator);


            for (int i = 0; i != entityCount;i++)
                m_Manager.SetComponentData(entities[i], new EcsTestDataEntity { value1 = targetEntities[i] });

            //set every other entity with component data "latest" in typeindex. Which happens to be based on insertion order according to a quick debug log
            for (int i = 0; i < entityCount; i += 2)
            {
                var buffer = m_Manager.AddBuffer<EcsIntElement4>(targetEntities[i]);
                buffer.Add(new EcsIntElement4
                {
                    Value0 = i,
                    Value1 = i + 1,
                    Value2 = i + 2,
                    Value3 = i + 3,
                });
            }

            targetEntities.Dispose();
            entities.Dispose();

            RunHasBufferSystem(true,useHasBuffer, ScheduleMode.Run);
            RunHasBufferSystem(true,useHasBuffer, ScheduleMode.Single);
            RunHasBufferSystem(true,useHasBuffer, ScheduleMode.Parallel);

            RunHasBufferSystem(false,useHasBuffer, ScheduleMode.Run);
            RunHasBufferSystem(false,useHasBuffer, ScheduleMode.Single);
            RunHasBufferSystem(false,useHasBuffer, ScheduleMode.Parallel);
        }
    }
}
