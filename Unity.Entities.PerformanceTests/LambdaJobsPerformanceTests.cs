using Unity.Entities.Tests;
using Unity.PerformanceTesting;
using Unity.Collections;
using NUnit.Framework;
using Unity.Burst;
using Unity.Jobs;

namespace Unity.Entities.PerformanceTests
{
    public class LambdaJobsTestFixture : ECSTestsFixture
    {
        protected class TestComponentSystem : JobComponentSystem
        {
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                return default;
            }

            public void OneDataLambda()
            {
                Entities.ForEach((Entity entity, ref EcsTestData d1) =>
                {
                    d1.value++;
                }).Run();
            }
            
            public void TwoDataLambda()
            {
                Entities.ForEach((Entity entity, ref EcsTestData d1, ref EcsTestData2 d2) => 
                { 
                    d1.value++;
                    d2.value0++;
                }).Run();
            }
            
            public void ThreeDataLambda()
            {
                Entities.ForEach((Entity entity, ref EcsTestData d1, ref EcsTestData2 d2, ref EcsTestData3 d3) =>
                {
                    d1.value++;
                    d2.value0++;
                    d3.value0++;
                }).Run();
            }
            
            [BurstCompile]
            public struct OneDataJob : IJobForEachWithEntity<EcsTestData>
            {
                public void Execute(Entity entity, int index, ref EcsTestData d1)
                {
                    d1.value++;
                }
            }
        
            [BurstCompile]
            public struct TwoDataJob : IJobForEachWithEntity<EcsTestData, EcsTestData2>
            {
                public void Execute(Entity entity, int index, ref EcsTestData d1, ref EcsTestData2 d2)
                {
                    d1.value++;
                    d2.value0++;
                }
            }
        
            [BurstCompile]
            public struct ThreeDataJob : IJobForEachWithEntity<EcsTestData, EcsTestData2, EcsTestData3>
            {
                public int count;
                public void Execute(Entity entity, int index, ref EcsTestData d1, ref EcsTestData2 d2, ref EcsTestData3 d3)
                {
                    d1.value++;
                    d2.value0++;
                    d3.value0++;
                }
            }
            
            public void StructuralChangesWithECB(EntityManager manager)
            {
                {
                    var ecb = new EntityCommandBuffer(Allocator.Temp, -1);
                    Entities
                        .ForEach((Entity entity) =>
                        {
                            ecb.AddComponent<EcsTestData>(entity);
                        }).Run();
                    ecb.Playback(manager);
                }
                {
                    var ecb = new EntityCommandBuffer(Allocator.Temp, -1);
                    Entities
                        .ForEach((Entity entity) =>
                        {
                            ecb.RemoveComponent<EcsTestData>(entity);
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
                        manager.AddComponent<EcsTestData>(entity);
                    }).Run();
                Entities
                    .WithStructuralChanges()
                    .ForEach((Entity entity) =>
                    {
                        manager.RemoveComponent<EcsTestData>(entity);
                    }).Run();
            }
        }

        protected TestComponentSystem TestSystem => World.GetOrCreateSystem<TestComponentSystem>();
    }
    
    [Category("Performance")]
    class LambdaJobsPerformanceTests : LambdaJobsTestFixture
    {
        // Tests the performance of the LambdaJobs ForEach & ForEach on ReadOnly components
        // No structural change expected
        [Test, Performance]
        [Category("Performance")]  
        public void LambdaJobsForEach_Performance([Values(1, 1000, 100000)] int entityCount, [Range(1, 3)] int componentCount)
        {
            EntityArchetype archetype = new EntityArchetype();
            switch (componentCount)
            {
                case 1: archetype = m_Manager.CreateArchetype(typeof(EcsTestData)); break;
                case 2: archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2)); break;
                case 3: archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3)); break;
            }
            using (var entities = new NativeArray<Entity>(entityCount, Allocator.TempJob))
            {
                m_Manager.CreateEntity(archetype, entities);
                switch (componentCount)
                {
                    case 1:
                        Measure.Method(() =>
                            {
                                TestSystem.OneDataLambda();
                            })
                            .Definition("LambdaJobForEach")
                            .Run();
                        Measure.Method(() =>
                            {
                                var job = new TestComponentSystem.OneDataJob();
                                job.Run(TestSystem);
                            })
                            .Definition("IJobForEachWithEntity")
                            .Run();
                        break;
                    case 2:
                        Measure.Method(() =>
                            {
                                TestSystem.TwoDataLambda();
                            })
                            .Definition("LambdaJobForEach")
                            .Run();
                        Measure.Method(() =>
                            {
                                var job = new TestComponentSystem.TwoDataJob();
                                job.Run(TestSystem);
                            })
                            .Definition("IJobForEachWithEntity")
                            .Run();
                        break;
                    case 3:
                        Measure.Method(() =>
                            {
                                TestSystem.ThreeDataLambda();
                            })
                            .Definition("LambdaJobForEach")
                            .Run();
                        Measure.Method(() =>
                            {
                                var job = new TestComponentSystem.ThreeDataJob();
                                job.Run(TestSystem);
                            })
                            .Definition("IJobForEachWithEntity")
                            .Run();
                        break;
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
                        .Definition("StructuralChangesWithECB")
                        .Run();
                }
                else
                {
                    Measure.Method(() =>
                        {
                            TestSystem.StructuralChangesInLambda(m_Manager);
                        })
                        .Definition("StructuralChangesInLambda")
                        .Run();
                }
            }
        }
    }
}
