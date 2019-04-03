using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.Entities.Tests
{
    class JobSafetyTests : ECSTestsFixture
	{
        public JobSafetyTests()
        {
            Assert.IsTrue(Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled, "JobDebugger must be enabled for these tests");
        }

        struct TestIncrementJob : IJob
        {
            public ComponentDataArray<EcsTestData> data;
            public void Execute()
            {
                for (int i = 0; i != data.Length; i++)
                {
                    var d = data[i];
                    d.value++;
                    data[i] = d;
                }
            }
        }



        [Test]
        public void ComponentAccessAfterScheduledJobThrows()
        {
            var group = m_Manager.CreateComponentGroup(typeof(EcsTestData));
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entity, new EcsTestData(42));

            var job = new TestIncrementJob();
            job.data = group.GetComponentDataArray<EcsTestData>();
            Assert.AreEqual(42, job.data[0].value);

            var fence = job.Schedule();

            Assert.Throws<System.InvalidOperationException>(() =>
            {
                var f = job.data[0].value;
                // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                f.GetHashCode();
            });

            fence.Complete();
            Assert.AreEqual(43, job.data[0].value);
        }

        [Test]
        public void GetComponentCompletesJob()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            var group = m_Manager.CreateComponentGroup(typeof(EcsTestData));

            var job = new TestIncrementJob();
            job.data = group.GetComponentDataArray<EcsTestData>();
            group.AddDependency(job.Schedule());

            // Implicit Wait for job, returns value after job has completed.
            Assert.AreEqual(1, m_Manager.GetComponentData<EcsTestData>(entity).value);
        }

        [Test]
        public void DestroyEntityCompletesScheduledJobs()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            /*var entity2 =*/ m_Manager.CreateEntity(typeof(EcsTestData));
            var group = m_Manager.CreateComponentGroup(typeof(EcsTestData));

            var job = new TestIncrementJob();
            job.data = group.GetComponentDataArray<EcsTestData>();
            group.AddDependency(job.Schedule());

            m_Manager.DestroyEntity(entity);

            // @TODO: This is maybe a little bit dodgy way of determining if the job has been completed...
            //        Probably should expose api to inspector job debugger state...
            Assert.AreEqual(1, group.GetComponentDataArray<EcsTestData>().Length);
            Assert.AreEqual(1, group.GetComponentDataArray<EcsTestData>()[0].value);
        }

        [Test]
        public void EntityManagerDestructionDetectsUnregisteredJob()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("job is still running"));

            /*var entity =*/ m_Manager.CreateEntity(typeof(EcsTestData));
            var group = m_Manager.CreateComponentGroup(typeof(EcsTestData));

            var job = new TestIncrementJob();
            job.data = group.GetComponentDataArray<EcsTestData>();
            job.Schedule();

            TearDown();
        }

        [Test]
        public void DestroyEntityDetectsUnregisteredJob()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            var group = m_Manager.CreateComponentGroup(typeof(EcsTestData));

            var job = new TestIncrementJob();
            job.data = group.GetComponentDataArray<EcsTestData>();
            var fence = job.Schedule();

            Assert.Throws<System.InvalidOperationException>(() => { m_Manager.DestroyEntity(entity); });

            fence.Complete();
        }

        [Test]
        public void GetComponentDetectsUnregisteredJob()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            var group = m_Manager.CreateComponentGroup(typeof(EcsTestData));

            var job = new TestIncrementJob();
            job.data = group.GetComponentDataArray<EcsTestData>();
            var jobHandle = job.Schedule();

            Assert.Throws<System.InvalidOperationException>(() => { m_Manager.GetComponentData<EcsTestData>(entity); });

            jobHandle.Complete();
        }

	    [Test]
	    [Ignore("Should work, need to write test")]
	    public void TwoJobsAccessingEntityArrayCanRunInParallel()
	    {
	    }

        struct EntityOnlyDependencyJob : IJobChunk
        {
            [ReadOnly] public ArchetypeChunkEntityType entityType;
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
            }
        }

        struct NoDependenciesJob : IJobChunk
        {
            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
            }
        }

        [DisableAutoCreation]
        class EntityOnlyDependencySystem : JobComponentSystem
        {
            public JobHandle JobHandle;
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                EntityManager.CreateEntity(typeof(EcsTestData));
                var group = GetComponentGroup(new ComponentType[]{});
                var job = new EntityOnlyDependencyJob
                {
                    entityType = m_EntityManager.GetArchetypeChunkEntityType()
                };
                JobHandle = job.Schedule(group, inputDeps);
                return JobHandle;
            }
        }

        [DisableAutoCreation]
        class NoComponentDependenciesSystem : JobComponentSystem
        {
            public JobHandle JobHandle;
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                EntityManager.CreateEntity(typeof(EcsTestData));
                var group = GetComponentGroup(new ComponentType[]{});
                var job = new NoDependenciesJob{};

                JobHandle = job.Schedule(group, inputDeps);
                return JobHandle;
            }
        }

        [DisableAutoCreation]
        class DestroyAllEntitiesSystem : JobComponentSystem
        {
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                var allEntities = EntityManager.GetAllEntities();
                EntityManager.DestroyEntity(allEntities);
                allEntities.Dispose();
                return inputDeps;
            }
        }

        [Test]
        public void StructuralChangeCompletesEntityOnlyDependencyJob()
        {
            var system = World.GetOrCreateManager<EntityOnlyDependencySystem>();
            system.Update();
            World.GetOrCreateManager<DestroyAllEntitiesSystem>().Update();
            Assert.IsTrue(JobHandle.CheckFenceIsDependencyOrDidSyncFence(system.JobHandle, new JobHandle()));
        }

        [Test]
        public void StructuralChangeCompletesNoComponentDependenciesJob()
        {
            var system = World.GetOrCreateManager<NoComponentDependenciesSystem>();
            system.Update();
            World.GetOrCreateManager<DestroyAllEntitiesSystem>().Update();
            Assert.IsTrue(JobHandle.CheckFenceIsDependencyOrDidSyncFence(system.JobHandle, new JobHandle()));
        }

        [Test]
        public void StructuralChangeAfterSchedulingNoDependenciesJobThrows()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entity = m_Manager.CreateEntity(archetype);
            var group = EmptySystem.GetComponentGroup(typeof(EcsTestData));
            var handle = new NoDependenciesJob().Schedule(group);
            Assert.Throws<InvalidOperationException>(() => m_Manager.DestroyEntity(entity));
            handle.Complete();
        }

        [Test]
        public void StructuralChangeAfterSchedulingEntityOnlyDependencyJobThrows()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entity = m_Manager.CreateEntity(archetype);
            var group = EmptySystem.GetComponentGroup(typeof(EcsTestData));
            var handle = new EntityOnlyDependencyJob{entityType = m_Manager.GetArchetypeChunkEntityType()}.Schedule(group);
            Assert.Throws<InvalidOperationException>(() => m_Manager.DestroyEntity(entity));
            handle.Complete();
        }
    }
}
