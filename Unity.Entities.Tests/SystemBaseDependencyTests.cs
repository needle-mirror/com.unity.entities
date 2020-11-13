using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
#pragma warning disable 649

namespace Unity.Entities.Tests
{
    partial class SystemBaseDependencyTests : ECSTestsFixture
    {
        public partial class ReadSystem1 : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities.ForEach((in EcsTestData c0) => {}).Schedule();
            }

            protected override void OnCreate()
            {
                EntityManager.CreateEntity(typeof(EcsTestData));
            }
        }

        public partial class ReadSystem2 : SystemBase
        {
            public bool returnWrongJob = false;
            public bool ignoreInputDeps = false;

            protected override void OnUpdate()
            {
                JobHandle h;
                if (ignoreInputDeps)
                    h = Entities.ForEach((in EcsTestData c0) => {}).Schedule(default);
                else
                    h = Entities.ForEach((in EcsTestData c0) => {}).Schedule(Dependency);

                Dependency = returnWrongJob ? Dependency : h;
            }

            protected override void OnCreate()
            {
                EntityManager.CreateEntity(typeof(EcsTestData));
            }
        }

        public partial class ReadSystem3 : SystemBase
        {
            public EntityQuery m_ReadGroup;

            protected override void OnUpdate() {}
            protected override void OnCreate()
            {
                m_ReadGroup = GetEntityQuery(ComponentType.ReadOnly<EcsTestData>());
            }
        }

        public partial class WriteSystem : SystemBase
        {
            public bool SkipJob = false;

            protected override void OnUpdate()
            {
                if (!SkipJob)
                {
                    Entities.ForEach((ref EcsTestData c0) => {}).Schedule();
                }
            }

            protected override void OnCreate()
            {
                EntityManager.CreateEntity(typeof(EcsTestData));
            }
        }

        public class GenericSystem<T> : SystemBase
        {
            public T thing;
            public T thing2;

            protected override void OnUpdate()
            {
                thing = thing2;
            }
        }

#if !NET_DOTS
        [Test]
        public void CreatingGenericSystem_Works()
        {
            var system = (GenericSystem<int>)World.CreateSystem(typeof(GenericSystem<int>));
            system.thing = 5;
            system.Update();
            Assert.AreEqual(system.thing, system.thing2);
        }
#endif

        [Test]
        public void ReturningWrongJobThrowsInCorrectSystemUpdate()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));
            ReadSystem1 rs1 = World.GetOrCreateSystem<ReadSystem1>();
            ReadSystem2 rs2 = World.GetOrCreateSystem<ReadSystem2>();

            rs2.returnWrongJob = true;

            rs1.Update();
            Assert.Throws<System.InvalidOperationException>(() => { rs2.Update(); });
        }

        [Test]
        public void IgnoredInputDepsThrowsInCorrectSystemUpdate()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));
            WriteSystem ws1 = World.GetOrCreateSystem<WriteSystem>();
            ReadSystem2 rs2 = World.GetOrCreateSystem<ReadSystem2>();

            rs2.ignoreInputDeps = true;

            ws1.Update();
            Assert.Throws<System.InvalidOperationException>(() => { rs2.Update(); });
        }

        [Test]
        public void NotSchedulingWriteJobIsHarmless()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));
            WriteSystem ws1 = World.GetOrCreateSystem<WriteSystem>();

            ws1.Update();
            ws1.SkipJob = true;
            ws1.Update();
        }

        [Test]
        public void NotUsingDataIsHarmless()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));
            ReadSystem1 rs1 = World.GetOrCreateSystem<ReadSystem1>();
            ReadSystem3 rs3 = World.GetOrCreateSystem<ReadSystem3>();

            rs1.Update();
            rs3.Update();
        }

        [Test]
        public void ReadAfterWrite_JobForEachGroup_Works()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));
            var ws = World.GetOrCreateSystem<WriteSystem>();
            var rs = World.GetOrCreateSystem<ReadSystem2>();

            ws.Update();
            rs.Update();
        }

        partial class UseEcsTestDataFromEntity : SystemBase
        {
            public struct MutateEcsTestDataJob : IJob
            {
                public ComponentDataFromEntity<EcsTestData> data;

                public void Execute()
                {
                }
            }

            protected override void OnUpdate()
            {
                var job = new MutateEcsTestDataJob { data = GetComponentDataFromEntity<EcsTestData>() };
                Dependency = job.Schedule(Dependency);
            }
        }

        // The writer dependency on EcsTestData is not predeclared during
        // OnCreate, but we still expect the code to work correctly.
        // This should result in a sync point when adding the dependency for the first time.
        [Test]
        public void AddingDependencyTypeDuringOnUpdateSyncsDependency()
        {
            var systemA = World.CreateSystem<UseEcsTestDataFromEntity>();
            var systemB = World.CreateSystem<UseEcsTestDataFromEntity>();

            systemA.Update();
            systemB.Update();
        }

        partial class EmptySystemBase : SystemBase
        {
            protected override void OnUpdate()
            {
            }
        }

        partial class SystemBaseWithJobChunkJob : SystemBase
        {
            public struct EmptyJob : IJobChunk
            {
                public ComponentTypeHandle<EcsTestData> TestDataTypeHandle;
                public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
                {
                }
            }

            protected override void OnUpdate()
            {
                Dependency = new EmptyJob
                {
                    TestDataTypeHandle = GetComponentTypeHandle<EcsTestData>()
                }.ScheduleParallel(EntityManager.UniversalQuery, Dependency);
            }
        }

        [Test]
        public void EmptySystemAfterNonEmptySystemDoesntThrow()
        {
            m_Manager.CreateEntity(typeof(EcsTestData));

            var systemA = World.CreateSystem<SystemBaseWithJobChunkJob>();
            var systemB = World.CreateSystem<EmptySystemBase>();

            systemA.Update();
            systemB.Update();
        }

        partial class SystemBaseEntitiesForEachDependencies : SystemBase
        {
            public bool DoRunToCompleteDependencies = false;

            protected override void OnUpdate()
            {
                Entities.ForEach((ref EcsTestData thing) => {}).Schedule();
                Entities.ForEach((ref EcsTestData thing) => {}).ScheduleParallel();

                if (DoRunToCompleteDependencies)
                    Entities.ForEach((ref EcsTestData thing) => {}).Run();

                if (!Dependency.Equals(new JobHandle())) //after completing all jobs an Dependency should be empty jobhandle
                    throw new Exception("Previous dependencies were not forced to completion.");
            }

            protected override void OnCreate()
            {
                EntityManager.CreateEntity(typeof(EcsTestData));
            }
        }

        [Test]
        public void SystemBaseEntitiesForEachDependencies_WithNoRun_HasUncompletedDependencies()
        {
            var system = World.CreateSystem<SystemBaseEntitiesForEachDependencies>();
            system.DoRunToCompleteDependencies = false;

            Assert.Throws<Exception>(() =>
            {
                system.Update();
            });
        }

        [Test]
        public void SystemBaseEntitiesForEachDependencies_WithRun_HasNoUncompletedDependencies()
        {
            var system = World.CreateSystem<SystemBaseEntitiesForEachDependencies>();
            system.DoRunToCompleteDependencies = true;

            Assert.DoesNotThrow(() =>
            {
                system.Update();
            });
        }

        partial class SystemBaseEntitiesForEachScheduling : SystemBase
        {
            public int ScheduleMode;
            public NativeArray<Entity> CreatedEntities;

            protected override void OnUpdate()
            {
                if (ScheduleMode == 0)
                    Entities.ForEach((int nativeThreadIndex, ref EcsTestData thing) =>
                    {
                        thing.value = nativeThreadIndex;
                    }).ScheduleParallel();
                else if (ScheduleMode == 1)
                    Entities.ForEach((int nativeThreadIndex, ref EcsTestData thing) =>
                    {
                        thing.value = nativeThreadIndex;
                    }).Schedule();
                else
                    Entities.ForEach((int nativeThreadIndex, ref EcsTestData thing) =>
                    {
                        thing.value = nativeThreadIndex;
                    }).Run();
            }

            protected override void OnCreate()
            {
                var archetype = EntityManager.CreateArchetype(new ComponentType[] {typeof(EcsTestData)});
                CreatedEntities = EntityManager.CreateEntity(archetype, 8000, Allocator.Persistent);
                foreach (var entity in CreatedEntities)
                    EntityManager.SetComponentData(entity, new EcsTestData(-1));
            }
        }

        [Test]
        public void SystemBaseEntitiesForEachScheduling_ScheduleAndRun_RunsOverAllComponentsWithCorrectThreadIndex([Values(0, 1, 2)] int runScheduleMode)
        {
            var system = World.CreateSystem<SystemBaseEntitiesForEachScheduling>();
            system.ScheduleMode = runScheduleMode;

            Assert.DoesNotThrow(() =>
            {
                system.Update();
            });

            int prevThreadIndex = -1;
            foreach (var entity in system.CreatedEntities)
            {
                var testData = system.EntityManager.GetComponentData<EcsTestData>(entity);

                // Ensure thread index is correct for the mode as well as validating all components were touched
#if !UNITY_SINGLETHREADED_JOBS
                if (runScheduleMode < 2)
                    Assert.True(testData.value > 0);
                else
                    Assert.Zero(testData.value);
#else
                Assert.Zero(testData.value);
#endif

                if (runScheduleMode != 0 && prevThreadIndex != -1)
                    Assert.AreEqual(testData.value, prevThreadIndex);
                prevThreadIndex = testData.value;
            }

            system.CreatedEntities.Dispose();
        }

        partial class SystemBaseEntitiesForEachComponentDataFromEntity : SystemBase
        {
            public bool RunScheduleParallel = false;

            protected override void OnUpdate()
            {
                var dataFromEntity = GetComponentDataFromEntity<EcsTestData>(false);

                if (RunScheduleParallel)
                {
                    Entities.ForEach((in EcsTestDataEntity data) =>
                    {
                        dataFromEntity[data.value1] = new EcsTestData() { value = data.value0 };
                    }).ScheduleParallel();
                }
                else
                {
                    Entities.ForEach((in EcsTestDataEntity data) =>
                    {
                        dataFromEntity[data.value1] = new EcsTestData() { value = data.value0 };
                    }).Schedule();
                }
            }
        }

        [Test]
        public void SystemBaseEntitiesForEachComponentDataFromEntity_Scheduled_ThrowsAppropriateException([Values(false, true)] bool runScheduleParallel)
        {
            var system = World.CreateSystem<SystemBaseEntitiesForEachComponentDataFromEntity>();
            system.RunScheduleParallel = runScheduleParallel;

            var archetype = system.EntityManager.CreateArchetype(new ComponentType[] { typeof(EcsTestDataEntity), typeof(EcsTestData) });
            using (var createdEntities = system.EntityManager.CreateEntity(archetype, 100, Allocator.Persistent))
            {
                for (int i = 0; i < createdEntities.Length; ++i)
                    system.EntityManager.SetComponentData(createdEntities[i], new EcsTestDataEntity { value1 = createdEntities[(i + 1) % createdEntities.Length] });

                if (runScheduleParallel)
                    Assert.Throws<InvalidOperationException>(() => { system.Update(); });
                else
                    Assert.DoesNotThrow(() => { system.Update(); });
            }
        }

        partial class SystemWithSyncPointAfterSchedule : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities.ForEach((ref EcsTestData _) => { }).Schedule();

                // this forces a sync-point and must finish the job we just scheduled
                EntityManager.CreateEntity();
            }

            protected override void OnCreate()
            {
                EntityManager.CreateEntity(typeof(EcsTestData));
            }
        }

        [Test]
        public void SystemBase_CanHaveSyncPointAfterSchedule()
        {
            var s = World.CreateSystem<SystemWithSyncPointAfterSchedule>();
            Assert.DoesNotThrow(() => s.Update());
        }
    }
}
