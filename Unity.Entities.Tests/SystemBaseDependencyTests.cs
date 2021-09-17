using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

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

        public partial class GenericSystem<T> : SystemBase
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
        [DotsRuntimeFixme("Debug.LogError is not burst compatible (for safety errors reported from bursted code) and LogAssert.Expect is not properly implemented in DOTS Runtime - DOTS-4294")]
        public void ReturningWrongJobReportsCorrectSystemUpdate()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));
            WriteSystem rs1 = World.GetOrCreateSystem<WriteSystem>();
            ReadSystem2 rs2 = World.GetOrCreateSystem<ReadSystem2>();

            LogAssert.Expect(LogType.Error, "The system Unity.Entities.Tests.SystemBaseDependencyTests+ReadSystem2 reads Unity.Entities.Tests.EcsTestData via ReadSystem2:ReadSystem2_LambdaJob_1_Job but that type was not assigned to the Dependency property. To ensure correct behavior of other systems, the job or a dependency must be assigned to the Dependency property before returning from the OnUpdate method.");

            rs2.returnWrongJob = true;

            rs1.Update();
            rs2.Update();
            rs1.Update();
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

                if (!runScheduleParallel)
                {
                    system.Update();
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => { system.Update();}); // this throws for parallel writing reasons
                }
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

        partial class UseStorageDataFromEntity : SystemBase
        {
            public struct MutateEcsTestDataJob : IJob
            {
                public StorageInfoFromEntity StorageInfo;

                public void Execute()
                {
                }
            }

            protected override void OnUpdate()
            {
                var job = new MutateEcsTestDataJob { StorageInfo = GetStorageInfoFromEntity() };
                //Dependency = job.Schedule(Dependency); commented out to show that StorageInfoFromEntity is always readonly and does not require dependency tracking
            }
        }

        [Test]
        public void StorageDataFromEntity_IsReadOnly_ThrowsNoSyncErrors()
        {
            var systemA = World.CreateSystem<UseStorageDataFromEntity>();
            var systemB = World.CreateSystem<UseStorageDataFromEntity>();

            systemA.Update();
            systemB.Update();
        }

        partial class EntityManagerGetBufferWriteSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                var buffer = EntityManager.GetBuffer<EcsIntElement>(GetSingletonEntity<EcsTestTag>());
                Job.WithCode(() => { buffer.Add(new EcsIntElement {Value = 123}); }).Schedule();
            }
        }

        partial class EntityManagerGetBufferWriteInUpdateSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                var buffer = EntityManager.GetBuffer<EcsIntElement>(GetSingletonEntity<EcsTestTag>());
                buffer.Add(new EcsIntElement {Value = 123});
            }
        }

        partial class EntityManagerGetBufferWriteViaRunSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                var buffer = EntityManager.GetBuffer<EcsIntElement>(GetSingletonEntity<EcsTestTag>());
                Job.WithCode(() => { buffer.Add(new EcsIntElement {Value = 123}); }).Run();
            }
        }

        partial class GetBufferWriteJobSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                var buffer = GetBuffer<EcsIntElement>(GetSingletonEntity<EcsTestTag>());
                Job.WithCode(() => { buffer.Add(new EcsIntElement {Value = 123}); }).Schedule();
            }
        }

        partial class GetBufferWriteInt2JobSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                var buffer = GetBuffer<EcsIntElement2>(GetSingletonEntity<EcsTestTag>());
                Job.WithCode(() => { buffer.Add(new EcsIntElement2 {Value0 = 0, Value1 = 1}); }).Schedule();
            }
        }

        partial class GetBufferFromEntityWriteJobSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                var buffer = GetBufferFromEntity<EcsIntElement>()[GetSingletonEntity<EcsTestTag>()];
                Job.WithCode(() => { buffer.Add(new EcsIntElement {Value = 123}); }).Schedule();
            }
        }

        partial class GetBufferFromEntityInEntitiesForEachWriteSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities.WithAll<EcsTestTag>().ForEach((Entity entity) =>
                {
                    var buffer = GetBufferFromEntity<EcsIntElement>()[entity];
                    buffer.Add(new EcsIntElement {Value = 123});
                }).Schedule();
            }
        }

        partial class GetBufferReadInUpdateSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                var buffer = GetBuffer<EcsIntElement>(GetSingletonEntity<EcsTestTag>());
                Assert.AreEqual(123, buffer[0].Value);
            }
        }

        partial class GetBufferReadOnlyInUpdateSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                var buffer = GetBuffer<EcsIntElement>(GetSingletonEntity<EcsTestTag>(), true);
                Assert.AreEqual(123, buffer[0].Value);
            }
        }

        partial class GetBufferReadJobSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                var buffer = GetBuffer<EcsIntElement>(GetSingletonEntity<EcsTestTag>());
                Job.WithCode(() =>
                {
                    Assert.AreEqual(123, buffer[0].Value);
                }).Schedule();
            }
        }

        partial class GetBufferReadOnlyJobSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                var buffer = GetBuffer<EcsIntElement>(GetSingletonEntity<EcsTestTag>(), true);
                Job.WithCode(() =>
                {
                    Assert.AreEqual(123, buffer[0].Value);
                }).Schedule();
            }
        }

        partial class GetBufferInsideForEachWithEntityIteratorSystem : SystemBase
        {
            protected override void OnCreate()
            {
                EntityManager.CreateEntity(typeof(EcsTestData));
            }

            protected override void OnUpdate()
            {
                Entities.WithAll<EcsTestTag>().ForEach((in Entity tagEntity) =>
                {
                    // Codegen should replace this with GetBufferFromEntity created in OnUpdate
                    var buffer = GetBuffer<EcsIntElement>(tagEntity);
                    Assert.AreEqual(123, buffer[0].Value);
                }).Schedule();
            }
        }

        partial class GetBufferInsideForEachWithSingletonSystem : SystemBase
        {
            protected override void OnCreate()
            {
                EntityManager.CreateEntity(typeof(EcsTestData));
            }

            protected override void OnUpdate()
            {
                var tagEntity = GetSingletonEntity<EcsTestTag>();
                Entities.ForEach((in EcsTestData testData) =>
                {
                    // Codegen should replace this with GetBufferFromEntity created in OnUpdate
                    var buffer = GetBuffer<EcsIntElement>(tagEntity);
                    Assert.AreEqual(123, buffer[0].Value);
                }).Schedule();
            }
        }

        partial class GetBufferAndPassAsNativeArrayToJob : SystemBase
        {
            protected override void OnCreate()
            {
                EntityManager.CreateEntity(typeof(EcsTestData));
            }

            protected override void OnUpdate()
            {
                var buffer = GetBuffer<EcsIntElement>(GetSingletonEntity<EcsTestTag>());
                var array = buffer.AsNativeArray();
                Entities.ForEach((in EcsTestData testData) =>
                {
                    Assert.AreEqual(123, array[0].Value);
                }).Schedule();
            }
        }

        [Test]
        public void BufferDependencies_WritingToUnrelatedBuffersDoesNotDepend()
        {
            m_Manager.CreateEntity(typeof(EcsTestTag), typeof(EcsIntElement), typeof(EcsIntElement2));

            var sysWriteInt = World.CreateSystem<EntityManagerGetBufferWriteSystem>();
            var sysWriteInt2 = World.CreateSystem<GetBufferWriteInt2JobSystem>();

            sysWriteInt.Update();
            Assert.DoesNotThrow(() => sysWriteInt2.Update());

            unsafe
            {
                var writeIntHandle = sysWriteInt.CheckedState()->Dependency;
                var writeInt2Handle = sysWriteInt2.CheckedState()->Dependency;
                Assert.IsFalse(JobHandle.CheckFenceIsDependencyOrDidSyncFence(writeInt2Handle, writeIntHandle));
            }
        }

        [Test]
        public void BufferDependencies_GetBufferReadAfterEntityManagerGetBufferWriteThrowsSafetyError()
        {
            m_Manager.CreateEntity(typeof(EcsTestTag), typeof(EcsIntElement));

            var sysWrite = World.CreateSystem<EntityManagerGetBufferWriteSystem>();
            var sysRead = World.CreateSystem<GetBufferReadInUpdateSystem>();
            sysWrite.Update();

            LogAssert.Expect(LogType.Error, new Regex(".*dependency.*"));

            // TODO: Fix discrepancy between runtimes. See https://jira.unity3d.com/browse/DOTS-5913
#if !UNITY_DOTSRUNTIME
            Assert.DoesNotThrow(() => sysRead.Update());
#else
            Assert.Throws<InvalidOperationException>(() => sysRead.Update());
#endif
        }

        [Test]
        public void BufferDependencies_WriteToBufferInUpdateViaEntityManagerGetBuffer()
        {
            m_Manager.CreateEntity(typeof(EcsTestTag), typeof(EcsIntElement));

            var sysWrite = World.CreateSystem<EntityManagerGetBufferWriteInUpdateSystem>();
            var sysRead = World.CreateSystem<GetBufferReadInUpdateSystem>();
            sysWrite.Update();
            Assert.DoesNotThrow(() => sysRead.Update());

            unsafe
            {
                var writeHandle = sysWrite.CheckedState()->Dependency;
                var readHandle = sysRead.CheckedState()->Dependency;
                Assert.IsTrue(JobHandle.CheckFenceIsDependencyOrDidSyncFence(readHandle, writeHandle));
            }
        }

        [Test]
        public void BufferDependencies_WriteToBufferInRunViaEntityManagerGetBuffer()
        {
            m_Manager.CreateEntity(typeof(EcsTestTag), typeof(EcsIntElement));

            var sysWrite = World.CreateSystem<EntityManagerGetBufferWriteViaRunSystem>();
            var sysRead = World.CreateSystem<GetBufferReadInUpdateSystem>();
            sysWrite.Update();
            Assert.DoesNotThrow(() => sysRead.Update());

            unsafe
            {
                var writeHandle = sysWrite.CheckedState()->Dependency;
                var readHandle = sysRead.CheckedState()->Dependency;
                Assert.IsTrue(JobHandle.CheckFenceIsDependencyOrDidSyncFence(readHandle, writeHandle));
            }
        }

        [Test]
        public void BufferDependencies_GetBufferReadInUpdateDependsOnGetBufferWriteJob()
        {
            m_Manager.CreateEntity(typeof(EcsTestTag), typeof(EcsIntElement));

            var sysWrite = World.CreateSystem<GetBufferWriteJobSystem>();
            var sysRead = World.CreateSystem<GetBufferReadInUpdateSystem>();
            sysWrite.Update();
            Assert.DoesNotThrow(() => sysRead.Update());

            unsafe
            {
                var writeHandle = sysWrite.CheckedState()->Dependency;
                var readHandle = sysRead.CheckedState()->Dependency;
                Assert.IsFalse(writeHandle.Equals(readHandle));
                // Investigate why this fails in DOTS Runtime https://jira.unity3d.com/browse/DOTS-5964
#if !UNITY_DOTSRUNTIME
                Assert.IsTrue(JobHandle.CheckFenceIsDependencyOrDidSyncFence(readHandle, writeHandle));
#endif
            }
        }

        [Test]
        public void BufferDependencies_GetBufferReadInUpdateDependsOnGetBufferFromEntityWriteJob()
        {
            m_Manager.CreateEntity(typeof(EcsTestTag), typeof(EcsIntElement));

            var sysWrite = World.CreateSystem<GetBufferFromEntityWriteJobSystem>();
            var sysRead = World.CreateSystem<GetBufferReadInUpdateSystem>();
            sysWrite.Update();
            Assert.DoesNotThrow(() => sysRead.Update());

            unsafe
            {
                var writeHandle = sysWrite.CheckedState()->Dependency;
                var readHandle = sysRead.CheckedState()->Dependency;
                Assert.IsFalse(writeHandle.Equals(readHandle));
#if !UNITY_DOTSRUNTIME
                Assert.IsTrue(JobHandle.CheckFenceIsDependencyOrDidSyncFence(readHandle, writeHandle));
#endif
            }
        }

        [Test]
        public void BufferDependencies_GetBufferReadInUpdateDependsOnGetBufferFromEntityForEachWriteJob()
        {
            m_Manager.CreateEntity(typeof(EcsTestTag), typeof(EcsIntElement));

            var sysWrite = World.CreateSystem<GetBufferFromEntityInEntitiesForEachWriteSystem>();
            var sysRead = World.CreateSystem<GetBufferReadInUpdateSystem>();
            sysWrite.Update();
            Assert.DoesNotThrow(() => sysRead.Update());

            unsafe
            {
                var writeHandle = sysWrite.CheckedState()->Dependency;
                var readHandle = sysRead.CheckedState()->Dependency;
                Assert.IsFalse(writeHandle.Equals(readHandle));
#if !UNITY_DOTSRUNTIME
                Assert.IsTrue(JobHandle.CheckFenceIsDependencyOrDidSyncFence(readHandle, writeHandle));
#endif
            }
        }

        [Test]
        public void BufferDependencies_GetBufferReadInForEachDependsOnGetBufferFromEntityWriteJob()
        {
            m_Manager.CreateEntity(typeof(EcsTestTag), typeof(EcsIntElement));

            var sysWrite = World.CreateSystem<GetBufferFromEntityWriteJobSystem>();
            var sysRead = World.CreateSystem<GetBufferInsideForEachWithEntityIteratorSystem>();
            sysWrite.Update();
            Assert.DoesNotThrow(() => sysRead.Update());

            unsafe
            {
                var writeHandle = sysWrite.CheckedState()->Dependency;
                var readHandle = sysRead.CheckedState()->Dependency;
                Assert.IsFalse(writeHandle.Equals(readHandle));
                // TODO: This fails, requires investigation https://jira.unity3d.com/browse/DOTS-5964
                //Assert.IsTrue(JobHandle.CheckFenceIsDependencyOrDidSyncFence(readHandle, writeHandle));
            }
        }

        [Test]
        public void BufferDependencies_GetBufferFromSingletonReadInForEachDependsOnGetBufferFromEntityWriteJob()
        {
            m_Manager.CreateEntity(typeof(EcsTestTag), typeof(EcsIntElement));

            var sysWrite = World.CreateSystem<GetBufferFromEntityWriteJobSystem>();
            var sysRead = World.CreateSystem<GetBufferInsideForEachWithSingletonSystem>();
            sysWrite.Update();
            Assert.DoesNotThrow(() => sysRead.Update());

            unsafe
            {
                var writeHandle = sysWrite.CheckedState()->Dependency;
                var readHandle = sysRead.CheckedState()->Dependency;
                Assert.IsFalse(writeHandle.Equals(readHandle));
                // TODO: This fails, requires investigation https://jira.unity3d.com/browse/DOTS-5964
                //Assert.IsTrue(JobHandle.CheckFenceIsDependencyOrDidSyncFence(readHandle, writeHandle));
            }
        }

        [Test]
        public void BufferDependencies_GetBufferAsNativeArrayReadJobDependsOnGetBufferWriteJob()
        {
            m_Manager.CreateEntity(typeof(EcsTestTag), typeof(EcsIntElement));

            var sysWrite = World.CreateSystem<GetBufferWriteJobSystem>();
            var sysRead = World.CreateSystem<GetBufferAndPassAsNativeArrayToJob>();
            sysWrite.Update();
            Assert.DoesNotThrow(() => sysRead.Update());

            unsafe
            {
                var writeHandle = sysWrite.CheckedState()->Dependency;
                var readHandle = sysRead.CheckedState()->Dependency;
                Assert.IsFalse(writeHandle.Equals(readHandle));
                Assert.IsFalse(JobHandle.CheckFenceIsDependencyOrDidSyncFence(readHandle, writeHandle));
            }
        }

        [Test]
        public void BufferDependencies_TwoGetBufferReadOnlyJobsShouldNotDependOnEachOther()
        {
            m_Manager.CreateEntity(typeof(EcsTestTag), typeof(EcsIntElement));

            // Read systems assume first buffer element is 123, so we write them first
            var sysWrite = World.CreateSystem<GetBufferFromEntityWriteJobSystem>();

            var sysReadOnly = World.CreateSystem<GetBufferReadOnlyJobSystem>();
            var sysReadOnly2 = World.CreateSystem<GetBufferReadOnlyJobSystem>();
            sysWrite.Update();
            sysReadOnly.Update();
            Assert.DoesNotThrow(() => sysReadOnly2.Update());

            unsafe
            {
                var writeHandle = sysWrite.CheckedState()->Dependency;
                var readHandle1 = sysReadOnly.CheckedState()->Dependency;
                var readHandle2 = sysReadOnly2.CheckedState()->Dependency;
                Assert.IsFalse(writeHandle.Equals(readHandle1));
                Assert.IsFalse(writeHandle.Equals(readHandle2));
                Assert.IsFalse(readHandle1.Equals(readHandle2));
                Assert.IsFalse(JobHandle.CheckFenceIsDependencyOrDidSyncFence(readHandle2, readHandle1));
            }
        }

        [Test]
        public void BufferDependencies_GetBufferShouldNotDependOnGetBufferReadOnly()
        {
            m_Manager.CreateEntity(typeof(EcsTestTag), typeof(EcsIntElement));

            // Read systems assume first buffer element is 123, so we write them first
            var sysWrite = World.CreateSystem<GetBufferFromEntityWriteJobSystem>();

            var sysReadOnly = World.CreateSystem<GetBufferReadOnlyJobSystem>();
            var sysRead = World.CreateSystem<GetBufferReadJobSystem>();
            sysWrite.Update();
            sysReadOnly.Update();
            Assert.DoesNotThrow(() => sysRead.Update());

            unsafe
            {
                var writeHandle = sysWrite.CheckedState()->Dependency;
                var readOnlyHandle = sysReadOnly.CheckedState()->Dependency;
                var readHandle = sysRead.CheckedState()->Dependency;
                Assert.IsFalse(writeHandle.Equals(readOnlyHandle));
                Assert.IsFalse(writeHandle.Equals(readHandle));
                Assert.IsFalse(readOnlyHandle.Equals(readHandle));
                Assert.IsFalse(JobHandle.CheckFenceIsDependencyOrDidSyncFence(readHandle, readOnlyHandle));
            }
        }
    }
}
