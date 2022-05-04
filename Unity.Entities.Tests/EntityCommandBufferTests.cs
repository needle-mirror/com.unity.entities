using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Entities.Tests
{
    class EntityCommandBufferTests : ECSTestsFixture
    {
        World m_World2;
        EntityManager m_Manager2;
        EntityManager.EntityManagerDebug m_ManagerDebug2;

        [SetUp]
        public override void Setup()
        {
            base.Setup();

            m_World2 = new World("Test World 2");
            m_Manager2 = m_World2.EntityManager;
            m_ManagerDebug2 = new EntityManager.EntityManagerDebug(m_Manager2);
        }

        [TearDown]
        public override void TearDown()
        {
            if (m_World2.IsCreated)
            {
                // Clean up systems before calling CheckInternalConsistency because we might have filters etc
                // holding on SharedComponentData making checks fail
                while (m_World2.Systems.Count > 0)
                {
                    m_World2.DestroySystem(m_World2.Systems[0]);
                }

                m_ManagerDebug2.CheckInternalConsistency();

                m_World2.Dispose();
                m_World2 = null;
            }

            base.TearDown();
        }

        [Test]
        public void EmptyOK()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.Playback(m_Manager);
        }

        [Test]
        public void Playback_WithSinglePlaybackPolicy_ThrowsOnMultiplePlaybacks()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.SinglePlayback);
            // First playback should succeed
            Assert.DoesNotThrow(() => {cmds.Playback(m_Manager); });
            // Subsequent playback attempts fail
            Assert.Throws<InvalidOperationException>(() => {cmds.Playback(m_Manager); });
            // Playback on a second EntityManager fails
            Assert.Throws<InvalidOperationException>(() => {cmds.Playback(m_Manager2); });
        }

        [Test]
        public void Playback_WithMultiPlaybackPolicy_DoesNotThrow()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.MultiPlayback);
            // First playback should succeed
            Assert.DoesNotThrow(() => {cmds.Playback(m_Manager); });
            // Subsequent playback attempts should not fail
            Assert.DoesNotThrow(() => {cmds.Playback(m_Manager); });
            // Playback on a second EntityManager also does not fail
            Assert.DoesNotThrow(() => {cmds.Playback(m_Manager2); });
        }

        unsafe bool CleanupListsAreEmpty(EntityCommandBufferChain* chain)
        {
            if (chain == null)
                return true;
            var cleanup = chain->m_Cleanup;
            if (cleanup->BufferCleanupList != null || cleanup->EntityArraysCleanupList != null)
                return false;
            return CleanupListsAreEmpty(chain->m_NextChain);
        }

        unsafe bool CleanupListsAreEmpty(EntityCommandBuffer ecb)
        {
            if (ecb.m_Data == null)
                return true;
            if (!CleanupListsAreEmpty(&ecb.m_Data->m_MainThreadChain))
                return false;
            if (ecb.m_Data->m_ThreadedChains != null)
            {
                for (int i = 0; i < JobsUtility.MaxJobThreadCount; ++i)
                {
                    if (!CleanupListsAreEmpty(&ecb.m_Data->m_ThreadedChains[i]))
                        return false;
                }
            }
            return true;
        }

        [Test]
        public void Playback_WithSinglePlaybackSuccess_DisposesCapturedEntityArrays()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            m_Manager.CreateEntity(archetype, 100);
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using(var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.SinglePlayback))
            {
                cmds.AddComponentForEntityQuery<EcsTestTag>(query);
                Assert.IsFalse(CleanupListsAreEmpty(cmds), "ECB has empty cleanup lists prior to playback");

                cmds.Playback(m_Manager);
                Assert.IsFalse(CleanupListsAreEmpty(cmds), "ECB with SinglePlayback has empty cleanup lists after playback");
            }
            // TODO(https://unity3d.atlassian.net/browse/DOTS-4497): at this point (after the ecb has been Disposed),
            // we should assert that the allocator passed to the ECB constructor has no remaining unreleased
            // allocations. However, that isn't currently possible.
        }

        [Test]
        public void Playback_WithSinglePlaybackError_DisposesCapturedEntityArrays()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            m_Manager.CreateEntity(archetype, 100);
            var ent = m_Manager.CreateEntity(archetype);
            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using(var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.SinglePlayback))
            {
                cmds.AddComponent<EcsTestData2>(ent);
                cmds.AddComponentForEntityQuery<EcsTestTag>(query); // entity array is captured here
                Assert.IsFalse(CleanupListsAreEmpty(cmds), "ECB has empty cleanup lists prior to playback");

                m_Manager.DestroyEntity(ent); // this will force an ECB playback error before the entity array command is played back
                Assert.Throws<InvalidOperationException>(() => cmds.Playback(m_Manager));
                // The entity array should still be present in the cleanup list
                Assert.IsFalse(CleanupListsAreEmpty(cmds), "ECB with SinglePlayback has empty cleanup lists after playback");
            }
            // TODO(https://unity3d.atlassian.net/browse/DOTS-4497): at this point (after the ecb has been Disposed),
            // we should assert that the allocator passed to the ECB constructor has no remaining unreleased
            // allocations. However, that isn't currently possible.
        }

        [BurstCompile(CompileSynchronously = true)]
        struct TestJob : IJob
        {
            public EntityCommandBuffer Buffer;

            public void Execute()
            {
                var e = Buffer.CreateEntity();
                Buffer.AddComponent(e, new EcsTestData { value = 1 });
            }
        }

        class TestEntityCommandBufferSystem : EntityCommandBufferSystem {}

        class TestECBPlaybackSystem : EntityCommandBufferSystem {}

        partial class TestECBRecordingSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                unsafe
                {
                    var ecb = World.GetOrCreateSystem<TestECBPlaybackSystem>().CreateCommandBuffer();
                    Assert.AreEqual(ecb.OriginSystemHandle, m_StatePtr->m_Handle);
                }
            }
        }

        [Test]
        public void EntityCommandBuffer_RecordingSystem_ValidHandle()
        {
            using (var world = new World("World A"))
            {
                var sim = world.GetOrCreateSystem<SimulationSystemGroup>();
                var ecbRecordingSystem = world.GetOrCreateSystem<TestECBRecordingSystem>();
                sim.AddSystemToUpdateList(ecbRecordingSystem);

                var pres = world.GetOrCreateSystem<PresentationSystemGroup>();
                var ecbPlaybackSystem = world.GetOrCreateSystem<TestECBPlaybackSystem>();
                pres.AddSystemToUpdateList(ecbPlaybackSystem);

                world.Update();
            }
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        // https://unity3d.atlassian.net/browse/DOTSR-1432
        [IgnoreInPortableTests("There are Assert.Throws in the WriteJob, which the runner doesn't find or support.")]
        [Test]
        public void EntityCommandBufferSystem_DisposeAfterPlaybackError_Succeeds()
        {
            TestEntityCommandBufferSystem barrier = World.GetOrCreateSystem<TestEntityCommandBufferSystem>();
            EntityCommandBuffer cmds = barrier.CreateCommandBuffer();

            // Schedule a job that writes concurrently to the ECB
            const int kCreateCount = 256;
            var job = new TestParallelJob
            {
                CommandBuffer = cmds.AsParallelWriter(),
            }.Schedule(kCreateCount, 64);
            // Intentionally omit this call, to trigger a safety manager exception during playback.
            //barrier.AddJobHandleForProducer(job)

            // This should throw an error; the job is still writing to the buffer we're playing back.
            Assert.Throws<ArgumentException>(() => { barrier.FlushPendingBuffers(true); }); // playback & dispose ECBs

            // ...but the ECB should have been successfully disposed.
            Assert.AreEqual(1, barrier.PendingBuffers.Count);
            Assert.IsFalse(barrier.PendingBuffers[0].IsCreated);

            job.Complete();
        }

        // These tests require:
        // - JobsDebugger support for static safety IDs (added in 2020.1)
#if !UNITY_DOTSRUNTIME
        [Test]
        [DotsRuntimeFixme("Static safety IDs - DOTSR-1432")]
        [IgnoreInPortableTests("There are Assert.Throws which the runner doesn't find or support.")]
        public void EntityCommandBufferConcurrent_PlaybackDuringWrite_UsesCustomOwnerTypeName()
        {
            EntityCommandBuffer cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            const int kCreateCount = 10000;
            var job = new TestParallelJob
            {
                CommandBuffer = cmds.AsParallelWriter(),
            }.Schedule(kCreateCount, 64);
            Assert.That(() => cmds.Playback(m_Manager), Throws.InvalidOperationException.With.Message.Contains("EntityCommandBuffer"));
            job.Complete();
        }

#endif

#endif

        [Test]
        public void SingleWriterEnforced()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.MultiPlayback);
            var job = new TestJob {Buffer = cmds};

            var e = cmds.CreateEntity();
            cmds.AddComponent(e, new EcsTestData { value = 42 });

            var handle = job.Schedule();

            Assert.Throws<InvalidOperationException>(() => { cmds.CreateEntity(); });
            Assert.Throws<InvalidOperationException>(() => { job.Buffer.CreateEntity(); });

            handle.Complete();

            cmds.Playback(m_Manager);
            cmds.Playback(m_Manager2);

            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            var arr = query.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);
            Assert.AreEqual(2, arr.Length);
            Assert.AreEqual(42, arr[0].value);
            Assert.AreEqual(1, arr[1].value);
            query.Dispose();

            var query2 = m_Manager2.CreateEntityQuery(typeof(EcsTestData));
            var arr2 = query2.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);
            Assert.AreEqual(2, arr2.Length);
            Assert.AreEqual(42, arr2[0].value);
            Assert.AreEqual(1, arr2[1].value);
            query2.Dispose();
        }

        [Test]
        public void DisposeWhileJobRunningThrows()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var job = new TestJob {Buffer = cmds};

            var handle = job.Schedule();

            Assert.Throws<InvalidOperationException>(() => { cmds.Dispose(); });

            handle.Complete();
        }

        [Test]
        public void ModifiesWhileJobRunningThrows()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var job = new TestJob {Buffer = cmds};

            var handle = job.Schedule();

            Assert.Throws<InvalidOperationException>(() => { cmds.CreateEntity(); });

            handle.Complete();
        }

        [Test]
        public void PlaybackWhileJobRunningThrows()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var job = new TestJob {Buffer = cmds};

            var handle = job.Schedule();

            Assert.Throws<InvalidOperationException>(() => { cmds.Playback(m_Manager); });

            handle.Complete();
        }

        struct EntityCommandBufferPlaybackJob : IJob
        {
            public EntityCommandBuffer Buffer;
            public ExclusiveEntityTransaction Manager;

            public void Execute()
            {
                Buffer.Playback(Manager);
            }
        }

        [Test]
        public void PlaybackWithExclusiveEntityTransactionInJob()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var job = new TestJob {Buffer = cmds};

            var e = cmds.CreateEntity();
            cmds.AddComponent(e, new EcsTestData { value = 42 });

            e = cmds.CreateEntity();
            cmds.AddSharedComponent(e, new EcsTestSharedComp(19));

            var jobHandle = job.Schedule();

            var manager = m_Manager.BeginExclusiveEntityTransaction();

            var playbackJob = new EntityCommandBufferPlaybackJob()
            {
                Buffer = cmds,
                Manager = manager
            };

            m_Manager.ExclusiveEntityTransactionDependency = playbackJob.Schedule(jobHandle);
            m_Manager.EndExclusiveEntityTransaction();

            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            var arr = group.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);
            Assert.AreEqual(2, arr.Length);
            Assert.AreEqual(42, arr[0].value);
            Assert.AreEqual(1, arr[1].value);
            group.Dispose();

            var sharedGroup = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp));
            var entities = sharedGroup.ToEntityArray(World.UpdateAllocator.ToAllocator);
            Assert.AreEqual(1, entities.Length);
            Assert.AreEqual(19, m_Manager.GetSharedComponentData<EcsTestSharedComp>(entities[0]).value);
            sharedGroup.Dispose();
        }

        [BurstCompile(CompileSynchronously = true)]
        struct TestParallelJob : IJobParallelFor
        {
            public EntityCommandBuffer.ParallelWriter CommandBuffer;

            public void Execute(int index)
            {
                var e = CommandBuffer.CreateEntity(index);
                CommandBuffer.AddComponent(index, e, new EcsTestData {value = index});
            }
        }

        [Test]
        public void EntityCommandBufferConcurrent_PlaybackDuringWrite_ThrowsInvalidOperation()
        {
            EntityCommandBuffer cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            const int kCreateCount = 10000;
            var job = new TestParallelJob
            {
                CommandBuffer = cmds.AsParallelWriter(),
            }.Schedule(kCreateCount, 64);
            Assert.Throws<InvalidOperationException>(() => { cmds.Playback(m_Manager); });
            job.Complete();
        }

        [Test]
        public void EntityCommandBufferConcurrent_DisposeDuringWrite_ThrowsInvalidOperation()
        {
            EntityCommandBuffer cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            const int kCreateCount = 10000;
            var job = new TestParallelJob
            {
                CommandBuffer = cmds.AsParallelWriter(),
            }.Schedule(kCreateCount, 64);
            Assert.Throws<InvalidOperationException>(() => { cmds.Dispose(); });
            job.Complete();
        }

        [Test]
        public void CreateEntity()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.MultiPlayback);
            var e = cmds.CreateEntity();
            cmds.AddComponent(e, new EcsTestData { value = 12 });
            cmds.Playback(m_Manager);
            cmds.Playback(m_Manager2);
            {
                var group = m_Manager.CreateEntityQuery(typeof(EcsTestData));
                var arr = group.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(1, arr.Length);
                Assert.AreEqual(12, arr[0].value);
                group.Dispose();
            }
            {
                var group = m_Manager2.CreateEntityQuery(typeof(EcsTestData));
                var arr = group.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(1, arr.Length);
                Assert.AreEqual(12, arr[0].value);
                group.Dispose();
            }
        }

        [Test]
        public void CreateEntityWithArchetype()
        {
            var a = m_Manager.CreateArchetype(typeof(EcsTestData));

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.MultiPlayback);
            var e = cmds.CreateEntity(a);
            cmds.SetComponent(e, new EcsTestData { value = 12 });
            cmds.Playback(m_Manager);
            cmds.Playback(m_Manager);

            {
                var group = m_Manager.CreateEntityQuery(typeof(EcsTestData));
                var arr = group.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(2, arr.Length);
                Assert.AreEqual(12, arr[0].value);
                Assert.AreEqual(12, arr[1].value);
                group.Dispose();
            }
        }

        [Test]
        public void CreateEntityWithArchetype_InvalidThrows()
        {
            var a = new EntityArchetype();
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            using (cmds)
            {
                Assert.Throws<ArgumentException>(() => cmds.CreateEntity(a));
            }
        }

        [Test]
        public void CreateEntityWithArchetype_Parallel_InvalidThrows()
        {
            var a = new EntityArchetype();
            var ecb = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var cmds = ecb.AsParallelWriter();
            using (ecb)
            {
                Assert.Throws<ArgumentException>(() => cmds.CreateEntity(0, a));
            }
        }

        [Test]
        public void CreateTwoComponents()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.MultiPlayback);
            var e = cmds.CreateEntity();
            cmds.AddComponent(e, new EcsTestData { value = 12 });
            cmds.AddComponent(e, new EcsTestData2 { value0 = 1, value1 = 2 });
            cmds.Playback(m_Manager);
            cmds.Playback(m_Manager2);

            {
                var group = m_Manager.CreateEntityQuery(typeof(EcsTestData));
                var arr = group.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(1, arr.Length);
                Assert.AreEqual(12, arr[0].value);
                group.Dispose();
            }

            {
                var group = m_Manager.CreateEntityQuery(typeof(EcsTestData2));
                var arr = group.ToComponentDataArray<EcsTestData2>(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(1, arr.Length);
                Assert.AreEqual(1, arr[0].value0);
                Assert.AreEqual(2, arr[0].value1);
                group.Dispose();
            }

            {
                var group = m_Manager2.CreateEntityQuery(typeof(EcsTestData));
                var arr = group.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(1, arr.Length);
                Assert.AreEqual(12, arr[0].value);
                group.Dispose();
            }

            {
                var group = m_Manager2.CreateEntityQuery(typeof(EcsTestData2));
                var arr = group.ToComponentDataArray<EcsTestData2>(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(1, arr.Length);
                Assert.AreEqual(1, arr[0].value0);
                Assert.AreEqual(2, arr[0].value1);
                group.Dispose();
            }
        }

        [Test]
        public void TestMultiChunks()
        {
#if UNITY_DOTSRUNTIME && !DEVELOP    // IL2CPP is a little slow in debug; reduce the number of tests in DEBUG (but not DEVELOP).
            const int count = 4096;
#else
            const int count = 65536;
#endif

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.MultiPlayback);
            cmds.MinimumChunkSize = 512;

            for (int i = 0; i < count; i++)
            {
                var e = cmds.CreateEntity();
                cmds.AddComponent(e, new EcsTestData { value = i });
                cmds.AddComponent(e, new EcsTestData2 { value0 = i, value1 = i });
            }

            cmds.Playback(m_Manager);
            cmds.Playback(m_Manager2);

            {
                var group = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2));
                var arr = group.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);
                var arr2 = group.ToComponentDataArray<EcsTestData2>(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(count, arr.Length);
                for (int i = 0; i < count; ++i)
                {
                    Assert.AreEqual(i, arr[i].value);
                    Assert.AreEqual(i, arr2[i].value0);
                    Assert.AreEqual(i, arr2[i].value1);
                }
                group.Dispose();
            }

            {
                var group = m_Manager2.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2));
                var arr = group.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);
                var arr2 = group.ToComponentDataArray<EcsTestData2>(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(count, arr.Length);
                for (int i = 0; i < count; ++i)
                {
                    Assert.AreEqual(i, arr[i].value);
                    Assert.AreEqual(i, arr2[i].value0);
                    Assert.AreEqual(i, arr2[i].value1);
                }
                group.Dispose();
            }
        }

        [Test]
        public void AddSharedComponent()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            var entity = m_Manager.CreateEntity();
            cmds.AddSharedComponent(entity, new EcsTestSharedComp(10));
            cmds.AddSharedComponent(entity, new EcsTestSharedComp2(20));
            cmds.AddSharedComponent(entity, new EcsTestSharedComp3(0));

            cmds.Playback(m_Manager);

            Assert.AreEqual(10, m_Manager.GetSharedComponentData<EcsTestSharedComp>(entity).value);
            Assert.AreEqual(20, m_Manager.GetSharedComponentData<EcsTestSharedComp2>(entity).value1);
            Assert.AreEqual(0, m_Manager.GetSharedComponentData<EcsTestSharedComp3>(entity).value0);
            Assert.AreEqual(0, m_Manager.GetSharedComponentDataIndex<EcsTestSharedComp3>(entity));
        }

        [Test]
        public void AddSharedComponentDefault()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.MultiPlayback);

            var e = cmds.CreateEntity();
            cmds.AddSharedComponent(e, new EcsTestSharedComp(10));
            cmds.AddSharedComponent(e, new EcsTestSharedComp2(20));

            cmds.Playback(m_Manager);
            cmds.Playback(m_Manager2);

            {
                var sharedComp1List = new List<EcsTestSharedComp>();
                var sharedComp2List = new List<EcsTestSharedComp2>();

                m_Manager.GetAllUniqueSharedComponentData(sharedComp1List);
                m_Manager.GetAllUniqueSharedComponentData(sharedComp2List);

                // the count must be 2 - the default value of the shared component and the one we actually set
                Assert.AreEqual(2, sharedComp1List.Count);
                Assert.AreEqual(2, sharedComp2List.Count);

                Assert.AreEqual(10, sharedComp1List[1].value);
                Assert.AreEqual(20, sharedComp2List[1].value1);
            }
            {
                var sharedComp1List = new List<EcsTestSharedComp>();
                var sharedComp2List = new List<EcsTestSharedComp2>();

                m_Manager2.GetAllUniqueSharedComponentData(sharedComp1List);
                m_Manager2.GetAllUniqueSharedComponentData(sharedComp2List);

                // the count must be 2 - the default value of the shared component and the one we actually set
                Assert.AreEqual(2, sharedComp1List.Count);
                Assert.AreEqual(2, sharedComp2List.Count);

                Assert.AreEqual(10, sharedComp1List[1].value);
                Assert.AreEqual(20, sharedComp2List[1].value1);
            }
        }

        [Test]
        public void AddComponent()
        {
            var e = m_Manager.CreateEntity();

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            cmds.AddComponent(e, new EcsTestData(10));
            cmds.AddComponent<EcsTestTag>(e);
            cmds.AddComponent(e, ComponentType.ReadWrite<EcsTestData3>());

            cmds.Playback(m_Manager);

            Assert.AreEqual(10, m_Manager.GetComponentData<EcsTestData>(e).value);
            Assert.IsTrue(m_Manager.HasComponent<EcsTestTag>(e));
            Assert.IsTrue(m_Manager.HasComponent<EcsTestData3>(e));
        }

        [Test]
        public void AddComponents_NoneExistPrior()
        {
            var e = m_Manager.CreateEntity();

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.AddComponent(e, new ComponentTypes(typeof(EcsTestTag), typeof(EcsTestData3)));
            cmds.Playback(m_Manager);

            Assert.IsTrue(m_Manager.HasComponent<EcsTestTag>(e));
            Assert.IsTrue(m_Manager.HasComponent<EcsTestData3>(e));
            Assert.AreEqual(m_Manager.GetComponentCount(e), 2);
        }

        [Test]
        public void AddComponents_SomeExistPrior()
        {
            var e = m_Manager.CreateEntity();
            m_Manager.AddComponent<EcsTestData3>(e);

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.AddComponent(e, new ComponentTypes(typeof(EcsTestTag), typeof(EcsTestData3)));
            cmds.Playback(m_Manager);

            Assert.IsTrue(m_Manager.HasComponent<EcsTestTag>(e));
            Assert.IsTrue(m_Manager.HasComponent<EcsTestData3>(e));
            Assert.AreEqual(2, m_Manager.GetComponentCount(e));
        }

        [Test]
        public void AddComponents_AllExistPrior()
        {
            var e = m_Manager.CreateEntity();
            m_Manager.AddComponent<EcsTestData3>(e);
            m_Manager.AddComponent<EcsTestTag>(e);

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.AddComponent(e, new ComponentTypes(typeof(EcsTestTag), typeof(EcsTestData3)));
            cmds.Playback(m_Manager);

            Assert.IsTrue(m_Manager.HasComponent<EcsTestTag>(e));
            Assert.IsTrue(m_Manager.HasComponent<EcsTestData3>(e));
            Assert.AreEqual(2,  m_Manager.GetComponentCount(e));
        }


        [BurstCompile(CompileSynchronously = true)]
        struct TestParallelJob_AddComponents : IJobParallelFor
        {
            public EntityCommandBuffer.ParallelWriter CommandBuffer;

            public void Execute(int index)
            {
                var e = CommandBuffer.CreateEntity(index);
                var types = new ComponentTypes(ComponentType.ReadWrite<EcsTestData>(), ComponentType.ReadWrite<EcsTestData2>());
                CommandBuffer.AddComponent(index, e, types);
            }
        }

        [Test]
        public void AddComponents_Parallel()
        {
            EntityCommandBuffer cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            const int kCreateCount = 10000;  // the bigger, the more likely to catch race conditions
            var job = new TestParallelJob_AddComponents
            {
                CommandBuffer = cmds.AsParallelWriter(),
            }.Schedule(kCreateCount, 64);
            job.Complete();
            cmds.Playback(m_Manager);

            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2));
            Assert.AreEqual(kCreateCount,  query.CalculateEntityCount());
            query.Dispose();
        }

        [Test]
        public void RemoveComponents()
        {
            var e = m_Manager.CreateEntity();
            m_Manager.AddComponents(e, new ComponentTypes(typeof(EcsTestTag), typeof(EcsTestData3)));

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.RemoveComponent(e, new ComponentTypes(typeof(EcsTestTag), typeof(EcsTestData3)));
            cmds.Playback(m_Manager);

            Assert.IsFalse(m_Manager.HasComponent<EcsTestTag>(e));
            Assert.IsFalse(m_Manager.HasComponent<EcsTestData3>(e));
            Assert.AreEqual(0, m_Manager.GetComponentCount(e));

            // same thing again, but reverse order of types in ComponentTypes
            m_Manager.AddComponents(e, new ComponentTypes(typeof(EcsTestTag), typeof(EcsTestData3)));

            cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.RemoveComponent(e, new ComponentTypes(typeof(EcsTestData3), typeof(EcsTestTag)));
            cmds.Playback(m_Manager);

            Assert.IsFalse(m_Manager.HasComponent<EcsTestTag>(e));
            Assert.IsFalse(m_Manager.HasComponent<EcsTestData3>(e));
            Assert.AreEqual(0, m_Manager.GetComponentCount(e));
        }

        [Test]
        public void SetSharedComponent()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            var e = cmds.CreateEntity();
            cmds.AddSharedComponent(e, new EcsTestSharedComp(10));
            cmds.SetSharedComponent(e, new EcsTestSharedComp(33));

            cmds.Playback(m_Manager);

            var sharedCompList = new List<EcsTestSharedComp>();
            m_Manager.GetAllUniqueSharedComponentData<EcsTestSharedComp>(sharedCompList);

            Assert.AreEqual(2, sharedCompList.Count);
            Assert.AreEqual(33, sharedCompList[1].value);
        }

        [Test]
        public void SetSharedComponentDefault()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.MultiPlayback);

            var e = cmds.CreateEntity();
            cmds.AddSharedComponent(e, new EcsTestSharedComp(10));
            cmds.SetSharedComponent(e, new EcsTestSharedComp());

            cmds.Playback(m_Manager);
            cmds.Playback(m_Manager2);

            {
                var sharedCompList = new List<EcsTestSharedComp>();
                m_Manager.GetAllUniqueSharedComponentData<EcsTestSharedComp>(sharedCompList);

                Assert.AreEqual(1, sharedCompList.Count);
                Assert.AreEqual(0, sharedCompList[0].value);
            }
            {
                var sharedCompList = new List<EcsTestSharedComp>();
                m_Manager2.GetAllUniqueSharedComponentData<EcsTestSharedComp>(sharedCompList);

                Assert.AreEqual(1, sharedCompList.Count);
                Assert.AreEqual(0, sharedCompList[0].value);
            }
        }

        [Test]
        public void RemoveSharedComponent()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            var entity = m_Manager.CreateEntity();
            var sharedComponent = new EcsTestSharedComp(10);
            m_Manager.AddSharedComponentData(entity, sharedComponent);

            cmds.RemoveComponent<EcsTestSharedComp>(entity);

            cmds.Playback(m_Manager);

            Assert.IsFalse(m_Manager.HasComponent<EcsTestSharedComp>(entity), "The shared component was not removed.");
        }

        [Test]
        public void AddAndSetComponent_ComponentDoesNotExist_Succeeds()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            var entity = m_Manager.CreateEntity();
            cmds.AddComponent(entity, ComponentType.ReadWrite<EcsTestData>());
            cmds.SetComponent(entity, new EcsTestData(42));

            Assert.DoesNotThrow(() => { cmds.Playback(m_Manager); });

            Assert.IsTrue(m_Manager.HasComponent<EcsTestData>(entity));
            Assert.AreEqual(42, m_Manager.GetComponentData<EcsTestData>(entity).value);
        }

        [Test]
        public void AddAndSetComponent_ComponentExists_NewValueWins()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            var entity = m_Manager.CreateEntity();
            cmds.AddComponent(entity, new EcsTestData(17));

            cmds.AddComponent(entity, ComponentType.ReadWrite<EcsTestData>());
            cmds.SetComponent(entity, new EcsTestData(42));

            Assert.DoesNotThrow(() => { cmds.Playback(m_Manager); });

            Assert.IsTrue(m_Manager.HasComponent<EcsTestData>(entity));
            Assert.AreEqual(42, m_Manager.GetComponentData<EcsTestData>(entity).value);
        }

        [Test]
        public void AddComponentData_ComponentExists_NewValueWins()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            var entity = m_Manager.CreateEntity();
            cmds.AddComponent(entity, new EcsTestData(17));
            cmds.AddComponent(entity, new EcsTestData(42));

            Assert.DoesNotThrow(() => { cmds.Playback(m_Manager); });

            Assert.AreEqual(42, m_Manager.GetComponentData<EcsTestData>(entity).value);
        }

        [Test]
        public void AddSharedComponent_ComponentDoesNotExist_Succeeds()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            var entity = m_Manager.CreateEntity();
            cmds.AddSharedComponent(entity, new EcsTestSharedComp(42));

            Assert.DoesNotThrow(() => { cmds.Playback(m_Manager); });

            Assert.AreEqual(42, m_Manager.GetSharedComponentData<EcsTestSharedComp>(entity).value);
        }

        [Test]
        public void AddSharedComponent_ComponentExists_NewValueWins()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            var entity = m_Manager.CreateEntity();
            cmds.AddSharedComponent(entity, new EcsTestSharedComp(17));
            cmds.AddSharedComponent(entity, new EcsTestSharedComp(42));

            Assert.DoesNotThrow(() => { cmds.Playback(m_Manager); });

            Assert.AreEqual(42, m_Manager.GetSharedComponentData<EcsTestSharedComp>(entity).value);
        }

        [Test]
        public void AddComponentForEntityQuery_CaptureAtRecord()
        {
            var entity = m_Manager.CreateEntity();
            var entity2 = m_Manager.CreateEntity();
            var data1 = new EcsTestData();
            m_Manager.AddComponentData(entity, data1);
            m_Manager.AddComponentData(entity2, data1);

            // these entities don't match the query and so should remain unaffected
            m_Manager.CreateEntity();
            m_Manager.CreateEntity(typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData4));

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                cmds.AddComponentForEntityQuery(entityQuery, typeof(EcsTestData2));
                cmds.Playback(m_Manager);
            }

            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData2)))
            using (var entities = entityQuery.ToEntityArray(World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(2, entities.Length, "Wrong number of entities with added component.");

                for (int i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    Assert.IsTrue(e == entity || e == entity2, "Wrong entity.");
                }
            }
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void AddManagedComponentForEntityQuery_WithValue_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));

            using (var originalEntities = m_Manager.CreateEntity(archetype, 2, World.UpdateAllocator.ToAllocator))
            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                var originalVal = new EcsTestManagedComponent();
                cmds.AddComponentObjectForEntityQuery(entityQuery, originalVal);

                // modifying entities between record and playback should be OK
                m_Manager.AddComponent<EcsTestData5>(originalEntities[0]);

                cmds.Playback(m_Manager);

                using (var entities = entityQuery.ToEntityArray(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(2, entities.Length, "Wrong number of entities with added component.");
                    CollectionAssert.AreEquivalent(originalEntities, entities);

                    for (int i = 0; i < entities.Length; i++)
                    {
                        var e = entities[i];
                        var val = m_Manager.GetComponentObject<EcsTestManagedComponent>(e);
                        Assert.AreSame(originalVal, val);
                    }
                }
            }
        }

        [Test]
        public void AddManagedComponentForEntityQuery_WithValue_HasComponentAlready_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestManagedComponent), typeof(EcsTestData));

            using (var originalEntities = m_Manager.CreateEntity(archetype, 2, World.UpdateAllocator.ToAllocator))
            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                var originalVal = new EcsTestManagedComponent();
                cmds.AddComponentObjectForEntityQuery(entityQuery, originalVal);

                // modifying entities between record and playback should be OK
                m_Manager.AddComponent<EcsTestData5>(originalEntities[0]);

                cmds.Playback(m_Manager);

                using (var entities = entityQuery.ToEntityArray(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(2, entities.Length, "Wrong number of entities with added component.");
                    CollectionAssert.AreEquivalent(originalEntities, entities);

                    for (int i = 0; i < entities.Length; i++)
                    {
                        var e = entities[i];
                        var val = m_Manager.GetComponentObject<EcsTestManagedComponent>(e);
                        Assert.AreSame(originalVal, val);
                    }
                }
            }
        }

        [Test]
        public void SetManagedComponentForEntityQuery_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestManagedComponent), typeof(EcsTestData));

            using (var originalEntities = m_Manager.CreateEntity(archetype, 2, World.UpdateAllocator.ToAllocator))
            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                var originalVal = new EcsTestManagedComponent();
                cmds.SetComponentObjectForEntityQuery(entityQuery, originalVal);

                // modifying entities between record and playback should be OK
                m_Manager.AddComponent<EcsTestData5>(originalEntities[0]);

                cmds.Playback(m_Manager);

                using (var entities = entityQuery.ToEntityArray(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(2, entities.Length, "Wrong number of entities with added component.");
                    CollectionAssert.AreEquivalent(originalEntities, entities);

                    for (int i = 0; i < entities.Length; i++)
                    {
                        var e = entities[i];
                        var val = m_Manager.GetComponentObject<EcsTestManagedComponent>(e);
                        Assert.AreSame(originalVal, val);
                    }
                }
            }
        }
#endif

        [Test]
        public void AddComponentForEntityQuery_CaptureAtRecord_SetValue()
        {
            var entity = m_Manager.CreateEntity();
            var entity2 = m_Manager.CreateEntity();
            var data1 = new EcsTestData();
            m_Manager.AddComponentData(entity, data1);
            m_Manager.AddComponentData(entity2, data1);
            m_Manager.AddComponentData(entity2, new EcsTestData2(8));  // entity that already has the component should have it set

            // these entities don't match the query and so should remain unaffected
            m_Manager.CreateEntity();
            m_Manager.CreateEntity(typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData4));

            var testVal = new EcsTestData2(5);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                cmds.AddComponentForEntityQuery(entityQuery, testVal);
                cmds.Playback(m_Manager);
            }

            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData2)))
            using (var entities = entityQuery.ToEntityArray(World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(2, entities.Length, "Wrong number of entities with added component.");

                for (int i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    Assert.AreEqual(testVal, m_Manager.GetComponentData<EcsTestData2>(e),
                        "A component did not have the correct value.");

                    Assert.IsTrue(e == entity || e == entity2, "Wrong entity.");
                }
            }
        }


        [Test]
        public void AddSharedComponentForEntityQuery_CaptureAtRecord_WithValue()
        {
            var entity = m_Manager.CreateEntity();
            var entity2 = m_Manager.CreateEntity();
            var entity3 = m_Manager.CreateEntity();
            var data1 = new EcsTestData();
            m_Manager.AddComponentData(entity, data1);
            m_Manager.AddComponentData(entity2, data1);
            m_Manager.AddComponentData(entity3, data1);
            m_Manager.AddSharedComponentData(entity2, new EcsTestSharedComp(8));  // entity that already has the component should have it set
            m_Manager.AddSharedComponentData(entity3, new EcsTestSharedComp(9));  // entity that already has the component should have it set

            // these entities don't match the query and so should remain unaffected
            m_Manager.CreateEntity();
            m_Manager.CreateEntity(typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData4));

            var testVal = new EcsTestSharedComp(5);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                cmds.AddSharedComponentForEntityQuery(entityQuery, testVal);
                cmds.Playback(m_Manager);
            }

            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp)))
            using (var entities = entityQuery.ToEntityArray(World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(3, entities.Length, "Wrong number of entities with added component.");

                for (int i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    Assert.AreEqual(5, m_Manager.GetSharedComponentData<EcsTestSharedComp>(e).value,
                        "A component did not have the correct value.");

                    Assert.AreEqual(2, m_Manager.GetComponentCount(e));
                    Assert.IsTrue(e == entity || e == entity2 || e == entity3, "Wrong entity.");
                }
            }
        }

        [Test]
        public void SetSharedComponentForEntityQuery_CaptureAtRecord_AllEntitiesHaveTheSharedComponent()
        {
            var entity = m_Manager.CreateEntity();
            var entity2 = m_Manager.CreateEntity();
            var entity3 = m_Manager.CreateEntity();
            var data1 = new EcsTestData();
            m_Manager.AddComponentData(entity, data1);
            m_Manager.AddComponentData(entity2, data1);
            m_Manager.AddComponentData(entity3, data1);
            m_Manager.AddSharedComponentData(entity, new EcsTestSharedComp(10));
            m_Manager.AddSharedComponentData(entity2, new EcsTestSharedComp(8));
            m_Manager.AddSharedComponentData(entity3, new EcsTestSharedComp(9));  // entity that already has the component should have it set

            // these entities don't match the query and so should remain unaffected
            m_Manager.CreateEntity();
            m_Manager.CreateEntity(typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData4));

            var testVal = new EcsTestSharedComp(5);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                cmds.SetSharedComponentForEntityQuery(entityQuery, testVal);
                cmds.Playback(m_Manager);
            }

            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp)))
            using (var entities = entityQuery.ToEntityArray(World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(3, entities.Length, "Wrong number of entities with added component.");

                for (int i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    Assert.AreEqual(5, m_Manager.GetSharedComponentData<EcsTestSharedComp>(e).value,
                         "A component did not have the correct value.");

                    Assert.AreEqual(2, m_Manager.GetComponentCount(e));
                    Assert.IsTrue(e == entity || e == entity2 || e == entity3, "Wrong entity.");
                }
            }
        }

        [Test]
        public void SetSharedComponentForEntityQuery_CaptureAtRecord_Playback_AllEntitiesMustExistAndHaveTheSharedComponent()
        {
            var entity = m_Manager.CreateEntity();
            var entity2 = m_Manager.CreateEntity();
            var data1 = new EcsTestData();
            m_Manager.AddComponentData(entity, data1);
            m_Manager.AddComponentData(entity2, data1);
            m_Manager.AddSharedComponentData(entity2, new EcsTestSharedComp(8));

            // these entities don't match the query and so should remain unaffected
            m_Manager.CreateEntity();
            m_Manager.CreateEntity(typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData4));

            var testVal = new EcsTestSharedComp(5);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                cmds.SetSharedComponentForEntityQuery(entityQuery, testVal);
                Assert.Throws<ArgumentException>(() => cmds.Playback(m_Manager));
            }
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void AddComponentForEntityQuery_CaptureAtRecord_ManagedComponent()
        {
            var entity = m_Manager.CreateEntity();
            var entity2 = m_Manager.CreateEntity();
            var data1 = new EcsTestData();
            m_Manager.AddComponentData(entity, data1);
            m_Manager.AddComponentData(entity2, data1);

            // these entities don't match the query and so should remain unaffected
            m_Manager.CreateEntity();
            m_Manager.CreateEntity(typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData4));

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                cmds.AddComponentForEntityQuery(entityQuery, typeof(EcsTestManagedComponent));
                cmds.Playback(m_Manager);
            }

            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestManagedComponent)))
            using (var entities = entityQuery.ToEntityArray(World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(2, entities.Length, "Wrong number of entities with added component.");

                for (int i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    Assert.IsTrue(e == entity || e == entity2, "Wrong entity.");
                    Assert.IsTrue(e == entity || e == entity2, "Wrong entity.");
                }
            }
        }
#endif

        [Test]
        public void AddComponentForEntityQuery_CaptureAtRecord_SharedComponent()
        {
            var entity = m_Manager.CreateEntity();
            var entity2 = m_Manager.CreateEntity();
            var data1 = new EcsTestData();
            m_Manager.AddComponentData(entity, data1);
            m_Manager.AddComponentData(entity2, data1);

            // these entities don't match the query and so should remain unaffected
            var entity3 = m_Manager.CreateEntity();
            var entity4 = m_Manager.CreateEntity(typeof(EcsTestData3));
            var entity5 = m_Manager.CreateEntity(typeof(EcsTestData4));

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                cmds.AddComponentForEntityQuery(entityQuery, typeof(EcsTestSharedComp));
                cmds.Playback(m_Manager);
            }

            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp)))
            using (var entities = entityQuery.ToEntityArray(World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(2, entities.Length, "Wrong number of entities with added component.");

                for (int i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    Assert.IsTrue(e == entity || e == entity2, "Wrong entity.");
                    Assert.IsTrue(e == entity || e == entity2, "Wrong entity.");
                }
            }
        }

        [Test]
        public void AddComponentForEntityQuery_CaptureAtRecord_SharedComponent_TooMany()
        {
            // test must be updated when kMaxSharedComponentCount is changed
            Assert.AreEqual(16, EntityComponentStore.kMaxSharedComponentCount);

            var entity = m_Manager.CreateEntity(
                typeof(EcsTestSharedComp), typeof(EcsTestSharedComp2), typeof(EcsTestSharedComp3), typeof(EcsTestSharedComp4),
                typeof(EcsTestSharedComp5), typeof(EcsTestSharedComp6), typeof(EcsTestSharedComp7), typeof(EcsTestSharedComp8),
                typeof(EcsTestSharedComp9), typeof(EcsTestSharedComp10), typeof(EcsTestSharedComp11), typeof(EcsTestSharedComp12),
                typeof(EcsTestSharedComp13), typeof(EcsTestSharedComp14));
            var entity2 = m_Manager.CreateEntity(typeof(EcsTestSharedComp), typeof(EcsTestSharedComp2),
                typeof(EcsTestSharedComp3), typeof(EcsTestSharedComp4));

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp)))
            {
                var types = new ComponentTypes(typeof(EcsTestSharedComp15), typeof(EcsTestSharedComp16), typeof(EcsTestSharedComp17));
                cmds.AddComponentForEntityQuery(entityQuery, types);

                // Assert.Throws "Cannot add more than {kMaxSharedComponentCount} SharedComponent to a single Archetype"
                Assert.Throws<InvalidOperationException>(() => cmds.Playback(m_Manager));
            }
        }

        [Test]
        public void AddComponentForEntityQuery_CaptureAtRecord_MultipleComponents()
        {
            var entity = m_Manager.CreateEntity();
            var entity2 = m_Manager.CreateEntity();
            var data1 = new EcsTestData();
            m_Manager.AddComponentData(entity, data1);
            m_Manager.AddComponentData(entity2, data1);

            // these entities don't match the query and so should remain unaffected
            m_Manager.CreateEntity();
            m_Manager.CreateEntity(typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData4));

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                var types = new ComponentTypes(typeof(EcsTestData2), typeof(EcsTestData3));
                cmds.AddComponentForEntityQuery(entityQuery, types);
                cmds.Playback(m_Manager);
            }

            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData2), typeof(EcsTestData3)))
            using (var entities = entityQuery.ToEntityArray(World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(2, entities.Length, "Wrong number of entities with added components.");
                for (int i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    Assert.IsTrue(e == entity || e == entity2, "Wrong entity.");
                    Assert.IsTrue(e == entity || e == entity2, "Wrong entity.");
                }
            }
        }

        [Test]
        public void RemoveComponentForEntityQuery_CaptureAtRecord()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));
            var entity2 = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));

            // these entities don't match the query and so should remain unaffected
            m_Manager.CreateEntity();
            m_Manager.CreateEntity(typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData4));

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                cmds.RemoveComponentForEntityQuery(entityQuery, typeof(EcsTestData2));
                cmds.Playback(m_Manager);
            }

            using (var entityQuery = m_Manager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new ComponentType[] {typeof(EcsTestData)},
                    None = new ComponentType[] {typeof(EcsTestData2)},
                }))
            using (var entities = entityQuery.ToEntityArray(World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(2, entities.Length, "Wrong number of entities with removed component.");

                for (int i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    Assert.IsTrue(e == entity || e == entity2, "Wrong entity.");
                    Assert.IsTrue(e == entity || e == entity2, "Wrong entity.");
                }
            }
        }

        [Test]
        public void RemoveComponentForEntityQuery_CaptureAtRecord_SharedComponent()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var entity = m_Manager.CreateEntity(archetype);
            var entity2 = m_Manager.CreateEntity(archetype);

            // these entities don't match the query and so should remain unaffected
            m_Manager.CreateEntity();
            m_Manager.CreateEntity(typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData4));

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                cmds.RemoveComponentForEntityQuery(entityQuery, typeof(EcsTestSharedComp));
                cmds.Playback(m_Manager);
            }

            using (var entityQuery = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(EcsTestData)},
                None = new ComponentType[] {typeof(EcsTestSharedComp)},
            }))
            using (var entities = entityQuery.ToEntityArray(World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(2, entities.Length, "Wrong number of entities with removed component.");

                for (int i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    Assert.IsTrue(e == entity || e == entity2, "Wrong entity.");
                    Assert.IsTrue(e == entity || e == entity2, "Wrong entity.");
                }
            }
        }

        [Test]
        public void RemoveComponentForEntityQuery_CaptureAtRecord_MultipleComponents()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3));
            var entity = m_Manager.CreateEntity(archetype);
            var entity2 = m_Manager.CreateEntity(archetype);

            // these entities don't match the query and so should remain unaffected
            m_Manager.CreateEntity();
            m_Manager.CreateEntity(typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData4));

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                var types = new ComponentTypes(typeof(EcsTestData), typeof(EcsTestData3));
                cmds.RemoveComponentForEntityQuery(entityQuery, types);
                cmds.Playback(m_Manager);
            }

            using (var entityQuery = m_Manager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(EcsTestData2)},
                None = new ComponentType[] {typeof(EcsTestData), typeof(EcsTestData3)},
            }))
            using (var entities = entityQuery.ToEntityArray(World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(2, entities.Length, "Wrong number of entities with removed component.");
                for (int i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    Assert.IsTrue(e == entity || e == entity2, "Wrong entity.");
                    Assert.IsTrue(e == entity || e == entity2, "Wrong entity.");
                }
            }
        }

        [Test]
        public void DestroyEntitiesForEntityQuery_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            m_Manager.CreateEntity(archetype, 5);

            // these entities don't match the query and so should remain unaffected
            m_Manager.CreateEntity();
            m_Manager.CreateEntity(typeof(EcsTestData3));
            m_Manager.CreateEntity(typeof(EcsTestData4));

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                cmds.DestroyEntitiesForEntityQuery(entityQuery);
                cmds.Playback(m_Manager);
            }

            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            using (var entities = entityQuery.ToEntityArray(World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(0, entities.Length, "Wrong number of entities destroyed.");
            }

            using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
            {
                Assert.AreEqual(3, entities.Length, "Wrong number of entities remaining.");
            }
        }

        [Test]
        public void AddComponentToEntityQueryWithFilter_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp));

            var entity1 = m_Manager.CreateEntity(archetype);
            var sharedComponent1 = new EcsTestSharedComp {value = 10};
            m_Manager.SetSharedComponentData(entity1, sharedComponent1);

            var entity2 = m_Manager.CreateEntity(archetype);
            var sharedComponent2 = new EcsTestSharedComp {value = 130};
            m_Manager.SetSharedComponentData(entity2, sharedComponent2);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp)))
            {
                entityQuery.SetSharedComponentFilter(sharedComponent2);
                cmds.AddComponentForEntityQuery(entityQuery, typeof(EcsTestData2));

                // modifying the entity in between recording and playback should be OK
                m_Manager.SetSharedComponentData(entity2, new EcsTestSharedComp {value = 200});
                m_Manager.AddComponent<EcsTestData3>(entity2);

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(2, entities.Length);
                    for (var i = 0; i < entities.Length; i++)
                    {
                        var e = entities[i];
                        var shared = m_Manager.GetSharedComponentData<EcsTestSharedComp>(e);
                        if (shared.value == 10)
                        {
                            Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(e),
                                "The component was incorrectly added based on the EntityQueryFilter.");
                        }
                        else
                        {
                            Assert.AreEqual(200, shared.value);
                            Assert.IsTrue(m_Manager.HasComponent<EcsTestData2>(e),
                                "The component was not added to the entities within the entity query.");
                        }
                    }
                }
            }
        }

        [Test]
        public void AddComponentsToEntityQueryWithFilter_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp));

            var entity1 = m_Manager.CreateEntity(archetype);
            var sharedComponent1 = new EcsTestSharedComp {value = 10};
            m_Manager.SetSharedComponentData(entity1, sharedComponent1);

            var entity2 = m_Manager.CreateEntity(archetype);
            var sharedComponent2 = new EcsTestSharedComp {value = 130};
            m_Manager.SetSharedComponentData(entity2, sharedComponent2);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp)))
            {
                entityQuery.SetSharedComponentFilter(sharedComponent2);
                cmds.AddComponentForEntityQuery(entityQuery, new ComponentTypes(typeof(EcsTestData2), typeof(EcsTestData3)));

                // modifying the entity in between recording and playback should be OK
                m_Manager.SetSharedComponentData(entity2, new EcsTestSharedComp {value = 200});
                m_Manager.AddComponent<EcsTestData3>(entity2);

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(2, entities.Length);

                    for (var i = 0; i < entities.Length; i++)
                    {
                        var e = entities[i];
                        var shared = m_Manager.GetSharedComponentData<EcsTestSharedComp>(e);
                        if (shared.value == 10)
                        {
                            Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(e));
                            Assert.IsFalse(m_Manager.HasComponent<EcsTestData3>(e));
                        } else
                        {
                            Assert.AreEqual(200, shared.value);
                            Assert.IsTrue(m_Manager.HasComponent<EcsTestData2>(e));
                            Assert.IsTrue(m_Manager.HasComponent<EcsTestData3>(e));
                        }
                    }
                }
            }
        }

#if  UNITY_2020_2_OR_NEWER
// 2020.2 no longer leaks strings when using Burst Abort
        [Test]
        public void AddComponentToEntityQuery_OnDifferentEntityManager_Throws_CaptureAtRecord()
        {
            var entity = m_Manager.CreateEntity();
            var data1 = new EcsTestData();
            m_Manager.AddComponentData(entity, data1);

            // create a query in one manager ...
            // ... but playback on a different manager
            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                cmds.AddComponentForEntityQuery(entityQuery, typeof(EcsTestData2));
                Assert.Throws<InvalidOperationException>(() => cmds.Playback(m_Manager2));
            }
        }
#endif

        [Test]
        public void RemoveComponentFromEntityQueryWithFilter_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp), typeof(EcsTestData));

            var entity1 = m_Manager.CreateEntity(archetype);
            var sharedComponent1 = new EcsTestSharedComp {value = 10};
            m_Manager.SetSharedComponentData(entity1, sharedComponent1);

            var entity2 = m_Manager.CreateEntity(archetype);
            var sharedComponent2 = new EcsTestSharedComp {value = 130};
            m_Manager.SetSharedComponentData(entity2, sharedComponent2);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp), typeof(EcsTestData)))
            {
                entityQuery.SetSharedComponentFilter(sharedComponent2);
                cmds.RemoveComponentForEntityQuery(entityQuery, typeof(EcsTestData));

                // modifying the entity in between recording and playback should be OK
                m_Manager.SetSharedComponentData(entity2, new EcsTestSharedComp {value = 200});
                m_Manager.AddComponent<EcsTestData3>(entity2);

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(2, entities.Length);
                    for (var i = 0; i < entities.Length; i++)
                    {
                        var e = entities[i];
                        var shared = m_Manager.GetSharedComponentData<EcsTestSharedComp>(e);
                        if (shared.value == 10)
                        {
                            Assert.IsTrue(m_Manager.HasComponent<EcsTestData>(e));
                        } else
                        {
                            Assert.AreEqual(200, shared.value);
                            Assert.IsFalse(m_Manager.HasComponent<EcsTestData>(e));
                        }
                    }
                }
            }
        }

        [Test]
        public void RemoveComponentsFromEntityQueryWithFilter_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp), typeof(EcsTestData), typeof(EcsTestData2));

            var entity1 = m_Manager.CreateEntity(archetype);
            var sharedComponent1 = new EcsTestSharedComp {value = 10};
            m_Manager.SetSharedComponentData(entity1, sharedComponent1);

            var entity2 = m_Manager.CreateEntity(archetype);
            var sharedComponent2 = new EcsTestSharedComp {value = 130};
            m_Manager.SetSharedComponentData(entity2, sharedComponent2);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp), typeof(EcsTestData)))
            {
                entityQuery.SetSharedComponentFilter(sharedComponent2);
                cmds.RemoveComponentForEntityQuery(entityQuery, new ComponentTypes(typeof(EcsTestData), typeof(EcsTestData2)));

                // modifying the entity in between recording and playback should be OK
                m_Manager.SetSharedComponentData(entity2, new EcsTestSharedComp {value = 200});
                m_Manager.AddComponent<EcsTestData3>(entity2);

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(2, entities.Length);

                    for (var i = 0; i < entities.Length; i++)
                    {
                        var e = entities[i];
                        var shared = m_Manager.GetSharedComponentData<EcsTestSharedComp>(e);
                        if (shared.value == 10)
                        {
                            Assert.IsTrue(m_Manager.HasComponent<EcsTestData>(e));
                            Assert.IsTrue(m_Manager.HasComponent<EcsTestData2>(e));
                        } else
                        {
                            Assert.AreEqual(200, shared.value);
                            Assert.IsFalse(m_Manager.HasComponent<EcsTestData>(e));
                            Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(e));
                        }
                    }
                }
            }
        }

        [Test]
        public void AddSharedComponentDataToEntityQueryWithFilter_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp), typeof(EcsTestData));

            var originalEntities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(25, ref World.UpdateAllocator);
            for (int i = 0; i < 50; i++)
            {
                var entity = m_Manager.CreateEntity(archetype);

                m_Manager.SetComponentData(entity, new EcsTestData());
                if (i % 2 == 0)
                {
                    m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp(0));
                    originalEntities[i / 2] = entity;
                }
                else
                {
                    m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp(1));
                }
            }

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp), typeof(EcsTestData)))
            {
                entityQuery.SetSharedComponentFilter(new EcsTestSharedComp(0));
                var shared2 = new EcsTestSharedComp2();
                cmds.AddSharedComponentForEntityQuery(entityQuery, shared2);

                // modifying the entity in between recording and playback should be OK
                m_Manager.AddComponent<EcsTestData3>(entityQuery);

                cmds.Playback(m_Manager);

                using (var entityQuery2 = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp), typeof(EcsTestSharedComp2), typeof(EcsTestData)))
                using (var entities = entityQuery2.ToEntityArray(World.UpdateAllocator.ToAllocator))
                {
                    CollectionAssert.AreEquivalent(entities, originalEntities);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        var value = m_Manager.GetSharedComponentData<EcsTestSharedComp>(entities[i]).value;
                        Assert.AreEqual(0, value, "The shared component was not correctly added based on the EntityQueryFilter.");
                    }
                }
            }
        }

        [Test]
        public void DestroyEntitiesFromEntityQuery_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));

            for (int i = 0; i < 50; i++)
            {
                var entity = m_Manager.CreateEntity(archetype);
                m_Manager.SetComponentData(entity, new EcsTestData());
            }

            var entity2 = m_Manager.CreateEntity();
            m_Manager.AddComponentData(entity2, new EcsTestData2());

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                cmds.DestroyEntitiesForEntityQuery(entityQuery);

                // modifying the entity in between recording and playback should be OK
                m_Manager.AddComponent<EcsTestData3>(entityQuery);

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(1, entities.Length, "Entities were not all deleted based on the EntityQuery.");
                    Assert.IsTrue(m_Manager.HasComponent(entities[0], typeof(EcsTestData2)), "This entity should not have been deleted based on the EntityQuery.");
                }
            }
        }

        [Test]
        public void DestroyEntitiesFromEntityQueryWithFilter_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp), typeof(EcsTestData));

            for (int i = 0; i < 50; i++)
            {
                var entity = m_Manager.CreateEntity(archetype);

                m_Manager.SetComponentData(entity, new EcsTestData());
                m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp(i % 2));
            }

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp), typeof(EcsTestData)))
            {
                entityQuery.SetSharedComponentFilter(new EcsTestSharedComp(0));
                cmds.DestroyEntitiesForEntityQuery(entityQuery);

                // modifying the entity in between recording and playback should be OK
                m_Manager.AddComponent<EcsTestData3>(entityQuery);

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(25, entities.Length,
                        "Half of the entities should be deleted based on the filter of the EntityQuery.");
                    for (int i = 0; i < entities.Length; i++)
                    {
                        var value = m_Manager.GetSharedComponentData<EcsTestSharedComp>(entities[i]).value;
                        Assert.AreEqual(1, value, "Entity should have been deleted based on the EntityQueryFilter.");
                    }
                }
            }
        }

        [Test]
        public void ChangeEntityQueryFilterDoesNotImpactRecordedCommand_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp));

            var entity1 = m_Manager.CreateEntity(archetype);
            var sharedComponent1 = new EcsTestSharedComp {value = 10};
            m_Manager.SetSharedComponentData(entity1, sharedComponent1);

            var entity2 = m_Manager.CreateEntity(archetype);
            var sharedComponent2 = new EcsTestSharedComp {value = 130};
            m_Manager.SetSharedComponentData(entity2, sharedComponent2);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp)))
            {
                entityQuery.SetSharedComponentFilter(sharedComponent2);
                cmds.AddComponentForEntityQuery(entityQuery, typeof(EcsTestData2));
                entityQuery.SetSharedComponentFilter(sharedComponent1);

                // modifying the entity in between recording and playback should be OK
                m_Manager.AddComponent<EcsTestData3>(entityQuery);

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(2, entities.Length);

                    for (var i = 0; i < entities.Length; i++)
                    {
                        var e = entities[i];
                        var shared = m_Manager.GetSharedComponentData<EcsTestSharedComp>(e);
                        if (shared.value == 10)
                        {
                            Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(e));
                        } else
                        {
                            Assert.AreEqual(130, shared.value);
                            Assert.IsTrue(m_Manager.HasComponent<EcsTestData2>(e));
                        }
                    }
                }
            }
        }

        [Test]
        public void DeleteEntityQueryDoesNotImpactRecordedCommand_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp));

            var entity1 = m_Manager.CreateEntity(archetype);
            var sharedComponent1 = new EcsTestSharedComp {value = 10};
            m_Manager.SetSharedComponentData(entity1, sharedComponent1);

            var entity2 = m_Manager.CreateEntity(archetype);
            var sharedComponent2 = new EcsTestSharedComp {value = 130};
            m_Manager.SetSharedComponentData(entity2, sharedComponent2);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp));
                entityQuery.SetSharedComponentFilter(sharedComponent2);
                cmds.AddComponentForEntityQuery(entityQuery, typeof(EcsTestData2));

                // modifying the entity in between recording and playback should be OK
                m_Manager.AddComponent<EcsTestData3>(entityQuery);

                entityQuery.Dispose();

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(2, entities.Length);

                    for (var i = 0; i < entities.Length; i++)
                    {
                        var e = entities[i];
                        var shared = m_Manager.GetSharedComponentData<EcsTestSharedComp>(e);
                        if (shared.value == 10)
                        {
                            Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(e));
                        } else
                        {
                            Assert.AreEqual(130, shared.value);
                            Assert.IsTrue(m_Manager.HasComponent<EcsTestData2>(e));
                        }
                    }
                }
            }
        }

        // Burst still doesn't work with managed shared components
        struct TestJobWithManagedSharedData : IJob
        {
            public EntityCommandBuffer Buffer;
            public EcsTestSharedComp2 Blah;

            public void Execute()
            {
                var e = Buffer.CreateEntity();
                Buffer.AddSharedComponent(e, Blah);
            }
        }

        [Test]
        public void JobWithSharedComponentData()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var job = new TestJobWithManagedSharedData { Buffer = cmds, Blah = new EcsTestSharedComp2(12) };

            job.Schedule().Complete();
            cmds.Playback(m_Manager);

            var list = new List<EcsTestSharedComp2>();
            m_Manager.GetAllUniqueSharedComponentData<EcsTestSharedComp2>(list);

            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(0, list[0].value0);
            Assert.AreEqual(0, list[0].value1);
            Assert.AreEqual(12, list[1].value0);
            Assert.AreEqual(12, list[1].value1);
        }

        [BurstCompile(CompileSynchronously = true)]
        public struct TestBurstCommandBufferJob : IJob
        {
            public Entity e0;
            public Entity e1;
            public EntityCommandBuffer Buffer;

            public void Execute()
            {
                Buffer.DestroyEntity(e0);
                Buffer.DestroyEntity(e1);
            }
        }

        [Test]
        public void TestCommandBufferDelete()
        {
            Entity[] entities = new Entity[2];
            for (int i = 0; i < entities.Length; ++i)
            {
                entities[i] = m_Manager.CreateEntity();
                m_Manager.AddComponentData(entities[i], new EcsTestData { value = i });
            }

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            new TestBurstCommandBufferJob
            {
                e0 = entities[0],
                e1 = entities[1],
                Buffer = cmds,
            }.Schedule().Complete();

            cmds.Playback(m_Manager);

            var allEntities = m_Manager.GetAllEntities();
            int count = allEntities.Length;
            allEntities.Dispose();

            Assert.AreEqual(0, count);
        }

        [Test]
        public void TestCommandBufferDeleteWithSystemState()
        {
            Entity[] entities = new Entity[2];
            for (int i = 0; i < entities.Length; ++i)
            {
                entities[i] = m_Manager.CreateEntity();
                m_Manager.AddComponentData(entities[i], new EcsTestData { value = i });
                m_Manager.AddComponentData(entities[i], new EcsState1 { Value = i });
            }

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            new TestBurstCommandBufferJob
            {
                e0 = entities[0],
                e1 = entities[1],
                Buffer = cmds,
            }.Schedule().Complete();

            cmds.Playback(m_Manager);

            var allEntities = m_Manager.GetAllEntities();
            int count = allEntities.Length;
            allEntities.Dispose();

            Assert.AreEqual(entities.Length, count);
        }

        [Test]
        public void TestCommandBufferDeleteRemoveSystemState()
        {
            Entity[] entities = new Entity[2];
            for (int i = 0; i < entities.Length; ++i)
            {
                entities[i] = m_Manager.CreateEntity();
                m_Manager.AddComponentData(entities[i], new EcsTestData { value = i });
                m_Manager.AddComponentData(entities[i], new EcsState1 { Value = i });
            }

            {
                var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
                new TestBurstCommandBufferJob
                {
                    e0 = entities[0],
                    e1 = entities[1],
                    Buffer = cmds,
                }.Schedule().Complete();

                cmds.Playback(m_Manager);
            }

            {
                var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
                for (var i = 0; i < entities.Length; i++)
                {
                    cmds.RemoveComponent<EcsState1>(entities[i]);
                }

                cmds.Playback(m_Manager);
            }

            var allEntities = m_Manager.GetAllEntities();
            int count = allEntities.Length;
            allEntities.Dispose();

            Assert.AreEqual(0, count);
        }

        [Test]
        public void Instantiate()
        {
            var e = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e, new EcsTestData(5));

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.Instantiate(e);
            cmds.Instantiate(e);
            cmds.Playback(m_Manager);

            VerifyEcsTestData(3, 5);
        }

        [Test]
        public void InstantiateWithNativeArray()
        {
            var e = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e, new EcsTestData(5));

            using var outEntities = CollectionHelper.CreateNativeArray<Entity>(2, World.UpdateAllocator.ToAllocator);
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.Instantiate(e, outEntities);
            cmds.Playback(m_Manager);

            VerifyEcsTestData(3, 5);
        }

        [Test]
        public void InstantiateWithSetComponentDataWorks()
        {
            var e = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e, new EcsTestData(5));

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            var e1 = cmds.Instantiate(e);
            cmds.SetComponent(e1, new EcsTestData(11));

            var e2 = cmds.Instantiate(e);
            cmds.SetComponent(e2, new EcsTestData(11));

            cmds.Playback(m_Manager);

            m_Manager.DestroyEntity(e);

            VerifyEcsTestData(2, 11);
        }

        [Test]
        public void DestroyEntityTwiceWorks()
        {
            var e = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e, new EcsTestData(5));

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            cmds.DestroyEntity(e);
            cmds.DestroyEntity(e);

            cmds.Playback(m_Manager);

            Assert.IsFalse(m_Manager.Exists(e));
        }

        [Test]
        public void AddSharedComponent_WhenComponentHasEntityField_DoesNotRemap()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            var es = m_Manager.CreateEntity();

            cmds.AddSharedComponent(es, new EcsTestSharedCompEntity(es));

            cmds.Playback(m_Manager);

            Assert.AreEqual(es, m_Manager.GetSharedComponentData<EcsTestSharedCompEntity>(es).value);
        }

#if UNITY_2020_2_OR_NEWER
// 2020.2 no longer leaks strings when using Burst Abort
        [Test]
        public void DestroyInvalidEntity()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var entityBuffer = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(1, ref World.UpdateAllocator);
            var e = cmds.CreateEntity();
            cmds.AddComponent(e, new EcsTestData { value = 12 });
            entityBuffer[0] = e;
            cmds.Playback(m_Manager);

            var savedEntity = entityBuffer[0];

            var cmds2 = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds2.DestroyEntity(savedEntity);

            // savedEntity is invalid, so playing back this ECB should throw an exception
            Assert.Throws<InvalidOperationException>(() =>
            {
                cmds2.Playback(m_Manager);
            });
        }
#endif

        [Test]
        public void TestShouldPlaybackFalse()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.CreateEntity();
            cmds.ShouldPlayback = false;
            cmds.Playback(m_Manager);

            var allEntities = m_Manager.GetAllEntities();
            int count = allEntities.Length;
            allEntities.Dispose();

            Assert.AreEqual(0, count);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct TestConcurrentJob : IJob
        {
            public EntityCommandBuffer.ParallelWriter Buffer;

            public void Execute()
            {
                Entity e = Buffer.CreateEntity(0);
                Buffer.AddComponent(0, e, new EcsTestData { value = 1 });
            }
        }

        [Test]
        public void ConcurrentRecord()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.CreateEntity();
            new TestConcurrentJob { Buffer = cmds.AsParallelWriter() }.Schedule().Complete();
            cmds.Playback(m_Manager);

            var allEntities = m_Manager.GetAllEntities();
            int count = allEntities.Length;
            allEntities.Dispose();

            Assert.AreEqual(2, count);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct TestConcurrentParallelForJob : IJobParallelFor
        {
            public EntityCommandBuffer.ParallelWriter Buffer;

            public void Execute(int index)
            {
                Entity e = Buffer.CreateEntity(index);
                Buffer.AddComponent(index, e, new EcsTestData { value = index });
            }
        }

        [Test]
        public void ConcurrentRecordParallelFor()
        {
            const int kCreateCount = 10000;
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.CreateEntity();
            new TestConcurrentParallelForJob { Buffer = cmds.AsParallelWriter() }.Schedule(kCreateCount, 64).Complete();
            cmds.Playback(m_Manager);

            var allEntities = m_Manager.GetAllEntities();
            int count = allEntities.Length;
            Assert.AreEqual(kCreateCount + 1, count);
            bool[] foundEntity = new bool[kCreateCount];
            for (int i = 0; i < foundEntity.Length; ++i)
            {
                foundEntity[i] = false;
            }
            for (int i = 0; i < count; ++i)
            {
                if (m_Manager.HasComponent<EcsTestData>(allEntities[i]))
                {
                    var data1 = m_Manager.GetComponentData<EcsTestData>(allEntities[i]);
                    Assert.IsFalse(foundEntity[data1.value]);
                    foundEntity[data1.value] = true;
                }
            }
            for (int i = 0; i < foundEntity.Length; ++i)
            {
                Assert.IsTrue(foundEntity[i]);
            }
            allEntities.Dispose();
        }

        [BurstCompile(CompileSynchronously = true)]
        struct TestConcurrentInstantiateJob : IJobParallelFor
        {
            public Entity MasterCopy;
            public EntityCommandBuffer.ParallelWriter Buffer;

            public void Execute(int index)
            {
                Entity e = Buffer.Instantiate(index, MasterCopy);
                Buffer.AddComponent(index, e, new EcsTestData { value = index });
            }
        }

        [Test]
        public void ConcurrentRecordInstantiate()
        {
            const int kInstantiateCount = 10000;
            Entity master = m_Manager.CreateEntity();
            m_Manager.AddComponentData(master, new EcsTestData2 {value0 = 42, value1 = 17});

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            new TestConcurrentInstantiateJob { Buffer = cmds.AsParallelWriter(), MasterCopy = master }.Schedule(kInstantiateCount, 64).Complete();
            cmds.Playback(m_Manager);

            var allEntities = m_Manager.GetAllEntities();
            int count = allEntities.Length;
            Assert.AreEqual(kInstantiateCount + 1, count); // +1 for the master entity
            bool[] foundEntity = new bool[kInstantiateCount];
            for (int i = 0; i < foundEntity.Length; ++i)
            {
                foundEntity[i] = false;
            }
            for (int i = 0; i < count; ++i)
            {
                var data2 = m_Manager.GetComponentData<EcsTestData2>(allEntities[i]);
                Assert.AreEqual(data2.value0, 42);
                Assert.AreEqual(data2.value1, 17);
                if (m_Manager.HasComponent<EcsTestData>(allEntities[i]))
                {
                    var data1 = m_Manager.GetComponentData<EcsTestData>(allEntities[i]);
                    Assert.IsFalse(foundEntity[data1.value]);
                    foundEntity[data1.value] = true;
                }
            }
            for (int i = 0; i < foundEntity.Length; ++i)
            {
                Assert.IsTrue(foundEntity[i]);
            }
            allEntities.Dispose();
        }

        [Test]
        public void PlaybackInvalidatesBuffers()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var e = cmds.CreateEntity();
            DynamicBuffer<EcsIntElement> buffer = cmds.AddBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3 });
            cmds.Playback(m_Manager);

            // Should not be possible to access the temporary buffer after playback.
#if UNITY_2020_2_OR_NEWER
            Assert.Throws<ObjectDisposedException>(() =>
#else
            Assert.Throws<InvalidOperationException>(() =>
#endif
            {
                buffer.Add(1);
            });
        }

        [Test(Description = "Once a buffer command is played back, it has no side effects on the ECB.")]
        public void BufferChanged_BetweenPlaybacks_HasNoEffectOnECB()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.MultiPlayback);
            var e = cmds.CreateEntity();
            DynamicBuffer<EcsIntElement> buffer = cmds.AddBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
            cmds.Playback(m_Manager);

            var entities = m_Manager.GetAllEntities();
            var emBuffer = m_Manager.GetBuffer<EcsIntElement>(entities[0]);
            for (int i = 0; i < emBuffer.Length; ++i)
                emBuffer[i] = 5;
            entities.Dispose();

            cmds.Playback(m_Manager);

            entities = m_Manager.GetAllEntities();
            Assert.AreEqual(2, entities.Length);
            var b = m_Manager.GetBuffer<EcsIntElement>(entities[1]);
            for (int i = 0; i < b.Length; ++i)
                Assert.AreEqual(i + 1, b[i].Value);

            entities.Dispose();
        }

        [Test]
        public void ArrayAliasesOfPendingBuffersAreInvalidateOnResize()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var e = cmds.CreateEntity();
            var buffer = cmds.AddBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3 });
            var array = buffer.AsNativeArray();
            buffer.Add(12);
#if UNITY_2020_2_OR_NEWER
            Assert.Throws<ObjectDisposedException>(() =>
#else
            Assert.Throws<InvalidOperationException>(() =>
#endif
            {
                int val = array[0];
            });
            // Refresh array alias
            array = buffer.AsNativeArray();
            cmds.Playback(m_Manager);

            // Should not be possible to access the temporary buffer after playback.
#if UNITY_2020_2_OR_NEWER
            Assert.Throws<ObjectDisposedException>(() =>
#else
            Assert.Throws<InvalidOperationException>(() =>
#endif
            {
                buffer.Add(1);
            });
            // Array should not be accessible after playback
#if UNITY_2020_2_OR_NEWER
            Assert.Throws<ObjectDisposedException>(() =>
#else
            Assert.Throws<InvalidOperationException>(() =>
#endif
            {
                int l = array[0];
            });
        }

        [Test]
        public void AddBufferNoOverflow()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var e = cmds.CreateEntity();
            DynamicBuffer<EcsIntElement> buffer = cmds.AddBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3 });
            cmds.Playback(m_Manager);
            VerifySingleBuffer(m_Manager, 3);
        }

        [Test]
        public void AddBufferNoOverflow_MultiplePlaybacks()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.MultiPlayback);
            var e = cmds.CreateEntity();
            DynamicBuffer<EcsIntElement> buffer = cmds.AddBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3 });
            cmds.Playback(m_Manager);
            cmds.Playback(m_Manager2);
            VerifySingleBuffer(m_Manager, 3);
            VerifySingleBuffer(m_Manager2, 3);
        }

        [Test]
        public void AddBufferOverflow()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var e = cmds.CreateEntity();
            DynamicBuffer<EcsIntElement> buffer = cmds.AddBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
            cmds.Playback(m_Manager);
            VerifySingleBuffer(m_Manager, 10);
        }

        [Test]
        public void AddBufferOverflow_MultiplePlaybacks()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.MultiPlayback);
            var e = cmds.CreateEntity();
            DynamicBuffer<EcsIntElement> buffer = cmds.AddBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
            cmds.Playback(m_Manager);
            cmds.Playback(m_Manager2);
            VerifySingleBuffer(m_Manager, 10);
            VerifySingleBuffer(m_Manager2, 10);
        }

        [Test]
        public void AddBufferOverflow_MultiplePlaybacks_SingleManager()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.MultiPlayback);
            var e = cmds.CreateEntity();
            DynamicBuffer<EcsIntElement> buffer = cmds.AddBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
            cmds.Playback(m_Manager);
            cmds.Playback(m_Manager);

            var allEntities = m_Manager.GetAllEntities();
            Assert.AreEqual(2, allEntities.Length);
            for (int j = 0; j < allEntities.Length; ++j)
            {
                var resultBuffer = m_Manager.GetBuffer<EcsIntElement>(allEntities[j]);
                Assert.AreEqual(10, resultBuffer.Length);

                for (int i = 0; i < 10; ++i)
                {
                    Assert.AreEqual(i + 1, resultBuffer[i].Value);
                }
            }

            allEntities.Dispose();
        }

        [Test]
        public void AddBufferExplicit()
        {
            var e = m_Manager.CreateEntity();
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var buffer = cmds.AddBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3 });
            cmds.Playback(m_Manager);

            VerifySingleBuffer(3);
        }

        [Test]
        public void SetBufferExplicit()
        {
            var e = m_Manager.CreateEntity(typeof(EcsIntElement));
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            DynamicBuffer<EcsIntElement> buffer = cmds.SetBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3 });
            cmds.Playback(m_Manager);
            VerifySingleBuffer(3);
        }

        [Test]
        public void AppendToBufferExplicit()
        {
            var e = m_Manager.CreateEntity();
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var buffer = cmds.AddBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3 });
            cmds.AppendToBuffer(e, new EcsIntElement {Value = 4});
            cmds.Playback(m_Manager);

            VerifySingleBuffer(4);
        }

        [Test]
        public void AppendToBufferExplicit_LargerThanInternalCapacity()
        {
            var e = m_Manager.CreateEntity();
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var buffer = cmds.AddBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            cmds.AppendToBuffer(e, new EcsIntElement {Value = 9});
            cmds.Playback(m_Manager);

            VerifySingleBuffer(9);
        }

        [Test]
        public void AppendToBufferExplicit_WithinExternalCapacity()
        {
            var e = m_Manager.CreateEntity();
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var buffer = cmds.AddBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            cmds.AppendToBuffer(e, new EcsIntElement {Value = 10});
            cmds.Playback(m_Manager);

            VerifySingleBuffer(10);
        }

        [Test]
        public void AppendToBufferExplicit_LargerThanExternalCapacity()
        {
            var e = m_Manager.CreateEntity();
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var buffer = cmds.AddBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });
            cmds.AppendToBuffer(e, new EcsIntElement {Value = 17});
            cmds.Playback(m_Manager);

            VerifySingleBuffer(17);
        }

        [Test, Ignore("Leaks string allocs in Burst")] // TODO: Can be re-enabled when DOTS-1963 is closed
        public void AppendToBuffer_BufferDoesNotExist_Fails()
        {
            var e = m_Manager.CreateEntity();
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.AppendToBuffer(e, new EcsIntElement {Value = 9});
            Assert.Throws<InvalidOperationException>(() => cmds.Playback(m_Manager));
        }

        [Test]
        public void AppendToBufferWithEntity_DelayedFixup_ContainsRealizedEntity()
        {
            int kNumOfBuffers = 12; // Must be > 2
            int kNumOfDeferredEntities = 12;

            EntityCommandBuffer cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            Entity[] e = new Entity[kNumOfBuffers];

            for (int n = 0; n < kNumOfBuffers; n++)
            {
                e[n] = m_Manager.CreateEntity();
                m_Manager.AddBuffer<EcsComplexEntityRefElement>(e[n]);
                for (int i = 0; i < kNumOfDeferredEntities; i++)
                    cmds.AppendToBuffer(e[n], new EcsComplexEntityRefElement() {Entity = cmds.CreateEntity()});
            }

            cmds.RemoveComponent<EcsComplexEntityRefElement>(e[0]);
            cmds.DestroyEntity(e[1]);

            cmds.Playback(m_Manager);

            Assert.IsFalse(m_Manager.HasComponent<EcsComplexEntityRefElement>(e[0]));
            Assert.IsFalse(m_Manager.Exists(e[1]));

            for (int n = 2; n < kNumOfBuffers; n++)
            {
                var outbuf = m_Manager.GetBuffer<EcsComplexEntityRefElement>(e[n]);
                Assert.AreEqual(kNumOfDeferredEntities, outbuf.Length);
                for (int i = 0; i < outbuf.Length; i++)
                {
                    Assert.IsTrue(m_Manager.Exists(outbuf[i].Entity));
                }
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct AppendToBufferJob : IJobParallelFor
        {
            public EntityCommandBuffer.ParallelWriter CommandBuffer;
            public NativeArray<Entity> Entities;

            public void Execute(int index)
            {
                var sourceBuffer = new NativeArray<EcsIntElement>(100, Allocator.Temp);

                for (var i = 0; i < sourceBuffer.Length; ++i)
                    CommandBuffer.AppendToBuffer<EcsIntElement>(index, Entities[index], new EcsIntElement {Value = i});

                sourceBuffer.Dispose();
            }
        }

        [Test]
        public void AppendToBuffer_DoesNotThrowInJob()
        {
            var archetype = m_Manager.CreateArchetype(ComponentType.ReadWrite<EcsTestData>(), ComponentType.ReadWrite<EcsIntElement>());
            var entities = new NativeArray<Entity>(100, Allocator.Persistent);
            m_Manager.CreateEntity(archetype, entities);

            EntityCommandBuffer cb = new EntityCommandBuffer(Allocator.Persistent);
            var handle = new AppendToBufferJob()
            {
                CommandBuffer = cb.AsParallelWriter(),
                Entities = entities
            }.Schedule(100, 1);
            handle.Complete();
            cb.Playback(m_Manager);

            for (var i = 0; i < 100; ++i)
            {
                var buffer = m_Manager.GetBuffer<EcsIntElement>(entities[i]);
                Assert.AreEqual(100, buffer.Length);
            }

            cb.Dispose();
            entities.Dispose();
        }

        [Test]
        public void AddBuffer_BufferDoesNotExist_Succeeds()
        {
            var e = m_Manager.CreateEntity(typeof(EcsIntElement));
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            DynamicBuffer<EcsIntElement> buffer = cmds.AddBuffer<EcsIntElement>(e);
            buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3 });
            cmds.Playback(m_Manager);
            VerifySingleBuffer(3);
        }

        [Test]
        public void AddBuffer_BufferExists_NewValueWins()
        {
            var e = m_Manager.CreateEntity(typeof(EcsIntElement));
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            DynamicBuffer<EcsIntElement> bufferOld = cmds.AddBuffer<EcsIntElement>(e);
            DynamicBuffer<EcsIntElement> bufferNew = cmds.AddBuffer<EcsIntElement>(e);
            bufferNew.CopyFrom(new EcsIntElement[] {1, 2, 3});
            // Writes to old buffer at this point are still valid, but will be ignored.
            Assert.DoesNotThrow(() => { bufferOld.CopyFrom(new EcsIntElement[] {4, 5, 6}); });
            cmds.Playback(m_Manager);
            VerifySingleBuffer(3);
        }

#if UNITY_2020_2_OR_NEWER
// 2020.2 no longer leaks strings when using Burst Abort
        [Test]
        public void AddBuffer_OnEntityFromOtherWorld_Fails()
        {
            var e = m_Manager.CreateEntity();
            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                DynamicBuffer<EcsIntElement> buffer = cmds.AddBuffer<EcsIntElement>(e);
                buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3 });
                Assert.Throws<InvalidOperationException>(() => cmds.Playback(m_Manager2));

                using (var allEntities = m_Manager.GetAllEntities())
                {
                    Assert.AreEqual(1, allEntities.Length);
                    Assert.IsFalse(m_Manager.HasComponent<EcsIntElement>(e));
                }
            }
        }
#endif

        [Test]
        public void AddBuffer_AfterDispose_WithoutPlayback_Throws()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            var e = cmds.CreateEntity();
            var buffer = cmds.AddBuffer<EcsIntElement>(e);

            Assert.DoesNotThrow(() => buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3, 4, 5 }));

            cmds.Dispose();

#if UNITY_2020_2_OR_NEWER
            Assert.Throws<ObjectDisposedException>(
#else
            Assert.Throws<InvalidOperationException>(
#endif
                () => buffer.CopyFrom(new EcsIntElement[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
        }

        [Test]
        public void ParallelOnMainThread()
        {
            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                var c = cmds.AsParallelWriter();
                Assert.DoesNotThrow(() => c.CreateEntity(0));
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct DeterminismTestJob : IJobParallelFor
        {
            public EntityCommandBuffer.ParallelWriter Cmds;

            public void Execute(int index)
            {
                Entity e = Cmds.CreateEntity(index);
                Cmds.AddComponent(index, e, new EcsTestData { value = index });
            }
        }

        [Test]
        public void DeterminismTest()
        {
            const int kRepeat = 10000;
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var e = cmds.CreateEntity(); // implicitly, sortIndex=Int32.MaxValue on the main thread
            cmds.AddComponent(e, new EcsTestData { value = kRepeat });
            new DeterminismTestJob { Cmds = cmds.AsParallelWriter() }.Schedule(kRepeat, 64).Complete();
            cmds.Playback(m_Manager);

            var allEntities = m_Manager.GetAllEntities();
            int count = allEntities.Length;
            Assert.AreEqual(kRepeat + 1, count);
            for (int i = 0; i < count; ++i)
            {
                var data = m_Manager.GetComponentData<EcsTestData>(allEntities[i]);
                Assert.AreEqual(i, data.value);
            }
            allEntities.Dispose();
        }

        [Test]
        public void NoTempAllocatorInConcurrent()
        {
            var cmds = new EntityCommandBuffer(Allocator.Temp);
#pragma warning disable 0219 // assigned but its value is never used
            Assert.Throws<InvalidOperationException>(() => { EntityCommandBuffer.ParallelWriter c = cmds.AsParallelWriter(); });
#pragma warning restore 0219
            cmds.Dispose();
        }

        private void VerifySingleBuffer(int length)
        {
            VerifySingleBuffer(m_Manager, length);
        }

        private static void VerifySingleBuffer(EntityManager manager, int length)
        {
            var allEntities = manager.GetAllEntities();
            Assert.AreEqual(1, allEntities.Length);
            var resultBuffer = manager.GetBuffer<EcsIntElement>(allEntities[0]);
            Assert.AreEqual(length, resultBuffer.Length);

            for (int i = 0; i < length; ++i)
            {
                Assert.AreEqual(i + 1, resultBuffer[i].Value);
            }
            allEntities.Dispose();
        }

        private void VerifyEcsTestData(int length, int expectedValue)
        {
            var allEntities = m_Manager.GetAllEntities();
            Assert.AreEqual(length, allEntities.Length);

            for (int i = 0; i < length; ++i)
            {
                Assert.AreEqual(expectedValue, m_Manager.GetComponentData<EcsTestData>(allEntities[i]).value);
            }
            allEntities.Dispose();
        }

        [BurstCompile(CompileSynchronously = true)]
        struct BufferCopyFromNativeArrayJob : IJobParallelFor
        {
            public EntityCommandBuffer.ParallelWriter CommandBuffer;
            public NativeArray<Entity> Entities;
            [ReadOnly]
            public NativeArray<EcsIntElement> sourceArray;
            public void Execute(int index)
            {
                var buffer = CommandBuffer.AddBuffer<EcsIntElement>(index, Entities[index]);

                buffer.CopyFrom(sourceArray);
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct BufferCopyFromNativeSliceJob : IJobParallelFor
        {
            public EntityCommandBuffer.ParallelWriter CommandBuffer;
            public NativeArray<Entity> Entities;
            [ReadOnly]
            public NativeSlice<EcsIntElement> sourceSlice;
            public void Execute(int index)
            {
                var buffer = CommandBuffer.AddBuffer<EcsIntElement>(index, Entities[index]);

                buffer.CopyFrom(sourceSlice);
            }
        }

        // https://unity3d.atlassian.net/browse/DOTSR-1435
        // These tests cause crashes in the IL2CPP runner. Cause not yet debugged.
        // Only fails in Multi-Threaded
        [Test]
        public void BufferCopyFromNativeArrayDoesNotThrowInJob()
        {
            var archetype = m_Manager.CreateArchetype(ComponentType.ReadWrite<EcsTestData>());
            var entities = new NativeArray<Entity>(100, Allocator.Persistent);
            m_Manager.CreateEntity(archetype, entities);

            var testArray = CollectionHelper.CreateNativeArray<EcsIntElement, RewindableAllocator>(100, ref World.UpdateAllocator);

            for (int i = 0; i < testArray.Length; i++)
            {
                testArray[i] = i;
            }

            EntityCommandBuffer cb = new EntityCommandBuffer(Allocator.Persistent);
            var handle = new BufferCopyFromNativeArrayJob()
            {
                CommandBuffer = cb.AsParallelWriter(),
                Entities = entities,
                sourceArray = testArray
            }.Schedule(100, 1);

            handle.Complete();
            cb.Playback(m_Manager);

            for (var i = 0; i < 100; ++i)
            {
                var buffer = m_Manager.GetBuffer<EcsIntElement>(entities[i]);
                Assert.AreEqual(100, buffer.Length);
                for (int j = 0; j < buffer.Length; j++)
                {
                    Assert.AreEqual(testArray[j].Value, buffer[j].Value);
                }
            }

            cb.Dispose();
            entities.Dispose();
        }

        [Test]
        public void BufferCopyFromNativeSliceDoesNotThrowInJob()
        {
            var archetype = m_Manager.CreateArchetype(ComponentType.ReadWrite<EcsTestData>());
            var entities = new NativeArray<Entity>(100, Allocator.Persistent);
            m_Manager.CreateEntity(archetype, entities);
            var testArray = CollectionHelper.CreateNativeArray<EcsIntElement, RewindableAllocator>(100, ref World.UpdateAllocator);

            for (int i = 0; i < testArray.Length; i++)
            {
                testArray[i] = i;
            }

            var testSlice = new NativeSlice<EcsIntElement>(testArray);

            EntityCommandBuffer cb = new EntityCommandBuffer(Allocator.Persistent);
            var handle = new BufferCopyFromNativeSliceJob()
            {
                CommandBuffer = cb.AsParallelWriter(),
                Entities = entities,
                sourceSlice = testSlice
            }.Schedule(100, 1);
            handle.Complete();
            cb.Playback(m_Manager);

            for (var i = 0; i < 100; ++i)
            {
                var buffer = m_Manager.GetBuffer<EcsIntElement>(entities[i]);
                Assert.AreEqual(100, buffer.Length);
                for (int j = 0; j < buffer.Length; j++)
                {
                    Assert.AreEqual(testSlice[j].Value, buffer[j].Value);
                }
            }

            cb.Dispose();
            entities.Dispose();
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS && UNITY_2020_2_OR_NEWER
// 2020.2 no longer leaks strings when using Burst Abort
        [Test]
        [ManagedExceptionInPortableTests] // This test relies on side-effects of running exception generating code
        public void EntityCommandBufferSystemPlaybackExceptionIsolation()
        {
            var entityCommandBufferSystem = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();

            var buf1 = entityCommandBufferSystem.CreateCommandBuffer();
            var buf2 = entityCommandBufferSystem.CreateCommandBuffer();

            var e1 = buf1.CreateEntity();
            buf1.AddComponent(e1, new EcsTestData());
            buf1.AddComponent(e1, new EcsTestData());

            var e2 = buf2.CreateEntity();
            buf2.AddComponent(e2, new EcsTestData());
            buf2.AddComponent(Entity.Null, new EcsTestData());

            // We exp both command buffers to execute, and an exception thrown afterwards
            // Essentially we want isolation of two systems that might fail independently.
            Assert.Throws<ArgumentException>(() => { entityCommandBufferSystem.Update(); });
            Assert.AreEqual(2, EmptySystem.GetEntityQuery(typeof(EcsTestData)).CalculateEntityCount());

            // On second run, we expect all buffers to be removed...
            // So no more exceptions thrown.
            entityCommandBufferSystem.Update();

            Assert.AreEqual(2, EmptySystem.GetEntityQuery(typeof(EcsTestData)).CalculateEntityCount());
        }

#endif

        [Test]
        public void DeferredEntity_FromDifferentCommandBuffer_WithNoDeferredEntities_Throws()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using(var ecb1 = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var ecb2 = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                var deferredEnt = ecb1.CreateEntity(archetype);
                ecb1.SetComponent(deferredEnt, new EcsTestData(17));
                ecb1.Playback(m_Manager);

                ecb2.SetComponent(deferredEnt, new EcsTestData(23));
                Assert.Throws<InvalidOperationException>(() => ecb2.Playback(m_Manager));
            }
        }

        [Test]
        public void DeferredEntity_CreatedFromDifferentCommandBuffer_Throws()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using(var ecb1 = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var ecb2 = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                var deferredEnt = ecb1.CreateEntity(archetype);
                ecb1.SetComponent(deferredEnt, new EcsTestData(17));
                ecb1.Playback(m_Manager);

                // Create one deferred entity in ecb2, so that deferredEnt's index isn't out of range
                var dummyEnt = ecb2.CreateEntity(archetype);
                ecb2.SetComponent(deferredEnt, new EcsTestData(23));
                Assert.Throws<InvalidOperationException>(() => ecb2.Playback(m_Manager));
            }
        }

        [Test]
        public void DeferredEntity_InstantiatedFromDifferentCommandBuffer_Throws()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using(var ecb1 = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var ecb2 = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                var prefab = m_Manager.CreateEntity(archetype);
                var deferredEnt = ecb1.Instantiate(prefab);
                ecb1.SetComponent(deferredEnt, new EcsTestData(17));
                ecb1.Playback(m_Manager);

                // Create one deferred entity in ecb2, so that deferredEnt's index isn't out of range
                var dummyEnt = ecb2.CreateEntity(archetype);
                ecb2.SetComponent(deferredEnt, new EcsTestData(23));
                Assert.Throws<InvalidOperationException>(() => ecb2.Playback(m_Manager));
            }
        }

        [Test]
        public void DeferredEntity_CreatedFromDifferentCommandBuffer_ParallelWriter_Throws()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using(var ecb1 = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var ecb2 = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                var writer1 = ecb1.AsParallelWriter();
                var deferredEnt = writer1.CreateEntity(0, archetype);
                writer1.SetComponent(0, deferredEnt, new EcsTestData(17));
                ecb1.Playback(m_Manager);

                // Create one deferred entity in ecb2, so that deferredEnt's index isn't out of range
                var dummyEnt = ecb2.CreateEntity(archetype);
                ecb2.SetComponent(deferredEnt, new EcsTestData(23));
                Assert.Throws<InvalidOperationException>(() => ecb2.Playback(m_Manager));
            }
        }

        [Test]
        public void DeferredEntity_InstantiatedFromDifferentCommandBuffer_ParallelWriter_Throws()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using(var ecb1 = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var ecb2 = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                var prefab = m_Manager.CreateEntity(archetype);
                var writer1 = ecb1.AsParallelWriter();
                var deferredEnt = writer1.Instantiate(0, prefab);
                writer1.SetComponent(0, deferredEnt, new EcsTestData(17));
                ecb1.Playback(m_Manager);

                // Create one deferred entity in ecb2, so that deferredEnt's index isn't out of range
                var dummyEnt = ecb2.CreateEntity(archetype);
                ecb2.SetComponent(deferredEnt, new EcsTestData(23));
                Assert.Throws<InvalidOperationException>(() => ecb2.Playback(m_Manager));
            }
        }

        [Test]
        public void DeferredEntity_OutOfRangeIndex_Throws()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            using(var ecb = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                var deferredEnt = ecb.CreateEntity(archetype);
                deferredEnt.Index = -1000;
                ecb.SetComponent(deferredEnt, new EcsTestData(17));
                Assert.Throws<InvalidOperationException>(() => ecb.Playback(m_Manager));
            }
        }

        [Test]
        public void AddComponent_WhenDataContainsDeferredEntity_ThrowsOnMultiplePlaybacks()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            Entity e0 = cmds.CreateEntity();
            cmds.AddComponent(e0, new EcsTestDataEntity(1, e0));

            Assert.DoesNotThrow(() => cmds.Playback(m_Manager));
            Assert.Throws<InvalidOperationException>(() => cmds.Playback(m_Manager));
            Assert.Throws<InvalidOperationException>(() => cmds.Playback(m_Manager2));
        }

        [Test]
        public void AddComponents_WhenDataContainsDeferredEntity_ThrowsOnMultiplePlaybacks()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            Entity e0 = cmds.CreateEntity();
            cmds.AddComponent(e0, new ComponentTypes(typeof(EcsTestData), typeof(EcsTestData2)));

            Assert.DoesNotThrow(() => cmds.Playback(m_Manager));
            Assert.Throws<InvalidOperationException>(() => cmds.Playback(m_Manager));
            Assert.Throws<InvalidOperationException>(() => cmds.Playback(m_Manager2));
        }

        [Test]
        public void AddComponent_WhenDataContainsDeferredEntity_DeferredEntityIsResolved()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            Entity e0 = cmds.CreateEntity();
            cmds.AddComponent(e0, new EcsTestDataEntity(1, e0));

            cmds.Playback(m_Manager);

            using (var group = m_Manager.CreateEntityQuery(typeof(EcsTestDataEntity)))
            {
                var e = group.GetSingletonEntity();
                Assert.AreEqual(e, m_Manager.GetComponentData<EcsTestDataEntity>(e).value1);
            }
        }

        [Test]
        public void AddComponents_WhenDataContainsDeferredEntity_DeferredEntityIsResolved()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            Entity e0 = cmds.CreateEntity();
            cmds.AddComponent(e0, new ComponentTypes(typeof(EcsTestDataEntity)));
            cmds.SetComponent(e0, new EcsTestDataEntity(1, e0));
            cmds.Playback(m_Manager);

            using (var group = m_Manager.CreateEntityQuery(typeof(EcsTestDataEntity)))
            {
                var e = group.GetSingletonEntity();
                Assert.AreEqual(e, m_Manager.GetComponentData<EcsTestDataEntity>(e).value1);
            }
        }

        [Test]
        public void EntityCommands_WithManyDeferredEntities_PerformAsExpected()
        {
            EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.Persistent);

#if UNITY_DOTSPLAYER_IL2CPP && !DEVELOP    // IL2CPP is a little slow in debug; reduce the number of tests in DEBUG (but not DEVELOP).
            const int step = 100;
#else
            const int step = 1;
#endif

            for (int i = 0; i < 2500; i += step)
            {
                Entity e = cmds.CreateEntity();
                cmds.AddComponent(e, new EcsTestData(i));
                cmds.SetComponent(e, new EcsTestData(i + 1));
                cmds.AddBuffer<EcsIntElement>(e);
                cmds.SetBuffer<EcsIntElement>(e);
                cmds.DestroyEntity(e);
            }
            cmds.Playback(m_Manager);
            cmds.Dispose();

            var allEntities = m_Manager.GetAllEntities();
            Assert.AreEqual(0, allEntities.Length);
            allEntities.Dispose();
        }

        [Test]
        public void InstantiateEntity_BatchMode_DisabledIfEntityDirty()
        {
            EntityCommandBuffer cmds = new EntityCommandBuffer(Allocator.Persistent);
            Entity esrc = m_Manager.CreateEntity();

            Entity edst0 = cmds.Instantiate(esrc);
            cmds.AddComponent(esrc, new EcsTestData2(12));
            Entity edst1 = cmds.Instantiate(esrc);
            cmds.AddComponent(esrc, new EcsTestDataEntity(33, edst1));

            cmds.Playback(m_Manager);
            cmds.Dispose();

            var realDst1 = m_Manager.GetComponentData<EcsTestDataEntity>(esrc).value1;
            Assert.AreEqual(12, m_Manager.GetComponentData<EcsTestData2>(realDst1).value1);
        }

#if !UNITY_PORTABLE_TEST_RUNNER
        // The portable test runner can't handle the return value from Assert.Throws
        [Test]
        public void UninitializedEntityCommandBufferThrows()
        {
            EntityCommandBuffer cmds = new EntityCommandBuffer();
            var exception = Assert.Throws<NullReferenceException>(() => cmds.CreateEntity());
            Assert.AreEqual("The EntityCommandBuffer has not been initialized! The EntityCommandBuffer needs to be passed an Allocator when created!", exception.Message);
        }

        [Test]
        public void UninitializedConcurrentEntityCommandBufferThrows()
        {
            EntityCommandBuffer.ParallelWriter cmds = new EntityCommandBuffer.ParallelWriter();
            var exception = Assert.Throws<NullReferenceException>(() => cmds.CreateEntity(0));
            Assert.AreEqual("The EntityCommandBuffer has not been initialized! The EntityCommandBuffer needs to be passed an Allocator when created!", exception.Message);
        }

#endif

        [Test]
        public void AddOrSetBufferWithEntity_NeedsFixup_ThrowsOnMultiplePlayback([Values(true, false)] bool setBuffer)
        {
            EntityCommandBuffer cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            Entity e0 = m_Manager.CreateEntity();
            Entity e1 = m_Manager.CreateEntity();
            Entity e2 = m_Manager.CreateEntity();

            if (setBuffer)
                m_Manager.AddComponent(e1, typeof(EcsComplexEntityRefElement));

            var deferred0 = cmds.CreateEntity();
            var deferred1 = cmds.CreateEntity();
            var deferred2 = cmds.CreateEntity();

            cmds.AddComponent(e0, new EcsTestDataEntity() { value1 = deferred0 });
            cmds.AddComponent(e1, new EcsTestDataEntity() { value1 = deferred1 });
            cmds.AddComponent(e2, new EcsTestDataEntity() { value1 = deferred2 });

            var buf = setBuffer ? cmds.SetBuffer<EcsComplexEntityRefElement>(e1) : cmds.AddBuffer<EcsComplexEntityRefElement>(e1);
            buf.Add(new EcsComplexEntityRefElement() {Entity = e0});
            buf.Add(new EcsComplexEntityRefElement() {Entity = deferred1});
            buf.Add(new EcsComplexEntityRefElement() {Entity = deferred2});
            buf.Add(new EcsComplexEntityRefElement() {Entity = deferred0});

            Assert.DoesNotThrow(() => cmds.Playback(m_Manager));
            Assert.Throws<InvalidOperationException>(() => cmds.Playback(m_Manager));
            Assert.Throws<InvalidOperationException>(() => cmds.Playback(m_Manager2));
        }

        [Test]
        public void AddOrSetBufferWithEntity_NeedsFixup_ContainsRealizedEntity([Values(true, false)] bool setBuffer)
        {
            EntityCommandBuffer cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            Entity e0 = m_Manager.CreateEntity();
            Entity e1 = m_Manager.CreateEntity();
            Entity e2 = m_Manager.CreateEntity();

            if (setBuffer)
                m_Manager.AddComponent(e1, typeof(EcsComplexEntityRefElement));

            {
                var deferred0 = cmds.CreateEntity();
                var deferred1 = cmds.CreateEntity();
                var deferred2 = cmds.CreateEntity();

                cmds.AddComponent(e0, new EcsTestDataEntity() { value1 = deferred0 });
                cmds.AddComponent(e1, new EcsTestDataEntity() { value1 = deferred1 });
                cmds.AddComponent(e2, new EcsTestDataEntity() { value1 = deferred2 });

                var buf = setBuffer ? cmds.SetBuffer<EcsComplexEntityRefElement>(e1) : cmds.AddBuffer<EcsComplexEntityRefElement>(e1);
                buf.Add(new EcsComplexEntityRefElement() {Entity = e0});
                buf.Add(new EcsComplexEntityRefElement() {Entity = deferred1});
                buf.Add(new EcsComplexEntityRefElement() {Entity = deferred2});
                buf.Add(new EcsComplexEntityRefElement() {Entity = deferred0});
                cmds.Playback(m_Manager);
            }
            {
                var outbuf = m_Manager.GetBuffer<EcsComplexEntityRefElement>(e1);
                Assert.AreEqual(4, outbuf.Length);
                var expect0 = m_Manager.GetComponentData<EcsTestDataEntity>(e0).value1;
                var expect1 = m_Manager.GetComponentData<EcsTestDataEntity>(e1).value1;
                var expect2 = m_Manager.GetComponentData<EcsTestDataEntity>(e2).value1;
                Assert.AreEqual(e0, outbuf[0].Entity);
                Assert.AreEqual(expect1, outbuf[1].Entity);
                Assert.AreEqual(expect2, outbuf[2].Entity);
                Assert.AreEqual(expect0, outbuf[3].Entity);
            }
        }

        [Test]
        public void BufferWithEntity_DelayedFixup_ContainsRealizedEntity()
        {
            int kNumOfBuffers = 12; // Must be > 2
            int kNumOfDeferredEntities = 12;

            EntityCommandBuffer cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            Entity[] e = new Entity[kNumOfBuffers];

            for (int n = 0; n < kNumOfBuffers; n++)
            {
                e[n] = m_Manager.CreateEntity();
                var buf = cmds.AddBuffer<EcsComplexEntityRefElement>(e[n]);
                for (int i = 0; i < kNumOfDeferredEntities; i++)
                    buf.Add(new EcsComplexEntityRefElement() {Entity = cmds.CreateEntity()});
            }

            cmds.RemoveComponent<EcsComplexEntityRefElement>(e[0]);
            cmds.DestroyEntity(e[1]);

            cmds.Playback(m_Manager);

            Assert.IsFalse(m_Manager.HasComponent<EcsComplexEntityRefElement>(e[0]));
            Assert.IsFalse(m_Manager.Exists(e[1]));

            for (int n = 2; n < kNumOfBuffers; n++)
            {
                var outbuf = m_Manager.GetBuffer<EcsComplexEntityRefElement>(e[n]);
                Assert.AreEqual(kNumOfDeferredEntities, outbuf.Length);
                for (int i = 0; i < outbuf.Length; i++)
                {
                    Assert.IsTrue(m_Manager.Exists(outbuf[i].Entity));
                }
            }
        }

#if !UNITY_PORTABLE_TEST_RUNNER
// https://unity3d.atlassian.net/browse/DOTSR-1432
        void VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(bool shouldThrow, TestDelegate code)
        {
            if (shouldThrow)
            {
                var ex = Assert.Throws<ArgumentException>(code);
                Assert.IsTrue(ex.Message.Contains("deferred"));
            }
            else
            {
                code();
            }
        }

        void RunDeferredTest(Entity entity)
        {
            bool isDeferredEntity = entity.Index < 0;

            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.AddComponent(entity, typeof(EcsTestData)));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.RemoveComponent(entity, typeof(EcsTestData)));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.AddComponents(entity, new ComponentTypes(typeof(EcsTestData), typeof(EcsTestData2))));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.AddComponentData(entity, new EcsTestData()));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.SetComponentData(entity, new EcsTestData()));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.GetComponentData<EcsTestData>(entity));

            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.IsComponentEnabled<EcsTestDataEnableable>(entity));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.IsComponentEnabled(entity, typeof(EcsTestDataEnableable)));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.SetComponentEnabled<EcsTestDataEnableable>(entity, true));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.SetComponentEnabled(entity, typeof(EcsTestDataEnableable), true));

            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.AddSharedComponentData(entity, new EcsTestSharedComp()));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp()));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.GetSharedComponentData<EcsTestSharedComp>(entity));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.RemoveComponent(entity, typeof(EcsTestSharedComp)));

            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.AddBuffer<EcsIntElement>(entity));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.GetBuffer<EcsIntElement>(entity));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.Exists(entity));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.HasComponent(entity, typeof(EcsTestData2)));
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.GetComponentCount(entity));

            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.DestroyEntity(entity));

#if !DOTS_DISABLE_DEBUG_NAMES
            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.SetName(entity,"Name"));

            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.SetName(entity,new FixedString64Bytes("Name")));

            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.GetName(entity));

            VerifyCommand_Or_CheckThatItThrowsIfEntityIsDeferred(
                isDeferredEntity,
                () => m_Manager.GetName(entity, out var fixed_name));
#endif
        }

        [Test]
        public void DeferredEntities_UsedInTheEntityManager_ShouldThrow()
        {
            using (EntityCommandBuffer cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable));
                var deferred = cmds.CreateEntity(archetype);
                cmds.AddComponent(cmds.CreateEntity(), new EcsTestDataEntity()
                {
                    value1 = deferred
                });

                RunDeferredTest(deferred);

                cmds.Playback(m_Manager);
                using (var group = m_Manager.CreateEntityQuery(typeof(EcsTestDataEntity)))
                using (var arr = group.ToComponentDataArray<EcsTestDataEntity>(World.UpdateAllocator.ToAllocator))
                {
                    RunDeferredTest(arr[0].value1);
                }
            }
        }

#endif

        [Test]
        public void IsEmpty_Works()
        {
            using (EntityCommandBuffer cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                Assert.IsTrue(cmds.IsEmpty, "newly-created EntityCommandBuffer has IsEmpty=false");
                cmds.CreateEntity();
                Assert.IsFalse(cmds.IsEmpty, "EntityCommandBuffer with one recorded commands has IsEmpty=true");
            }
        }

        [Test]
        public void AddSharedComponent_WithEntityQuery_ThatHasNoMatch_WillNotCorruptInternalState_CaptureAtRecord()
        {
            using (EntityCommandBuffer cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestData));
                cmds.AddSharedComponentForEntityQuery(entityQuery, new EcsTestSharedComp(1));

                // modifying the entity in between recording and playback should be OK
                m_Manager.AddComponent<EcsTestData3>(entityQuery);

                cmds.Playback(m_Manager);
            }
            m_Manager.Debug.CheckInternalConsistency();
        }

        [Test]
        public void AddAndRemoveComponent_EntityQueryCacheIsValid()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));
            using(EntityQuery query2 = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), ComponentType.Exclude<EcsTestData3>()))
            using(EntityQuery query3 = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData3)))
            using (EntityCommandBuffer cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                cmds.AddComponent<EcsTestData3>(ent);
                cmds.RemoveComponent<EcsTestData3>(ent);
                cmds.Playback(m_Manager);
                Assert.IsFalse(query2.IsCacheValid);
                Assert.IsFalse(query3.IsCacheValid);
            }
        }

        [Test]
        public void RemoveAndAddComponent_EntityQueryCacheIsValid()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));
            using(EntityQuery query2 = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestData2)))
            using(EntityQuery query1 = m_Manager.CreateEntityQuery(typeof(EcsTestData), ComponentType.Exclude<EcsTestData2>()))
            using (EntityCommandBuffer cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                cmds.RemoveComponent<EcsTestData2>(ent);
                cmds.AddComponent<EcsTestData2>(ent);
                cmds.Playback(m_Manager);
                Assert.IsFalse(query2.IsCacheValid);
                Assert.IsFalse(query1.IsCacheValid);
            }
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        private void VerifyEcsTestManagedComponent(int length, string expectedValue)
        {
            var allEntities = m_Manager.GetAllEntities();
            Assert.AreEqual(length, allEntities.Length);

            for (int i = 0; i < length; ++i)
            {
                var component = m_Manager.GetComponentData<EcsTestManagedComponent>(allEntities[i]);
                Assert.AreEqual(expectedValue, component.value);
            }
            allEntities.Dispose();
        }

        [Test]
        public void Playback_EmptyCommandBuffer_DoesntInvalidateSafetyHandles()
        {
            var ent = m_Manager.CreateEntity(typeof(EcsIntElement));
            var bfe = m_Manager.GetBufferFromEntity<EcsIntElement>();
            var buf = bfe[ent];
            using (var ecb = new EntityCommandBuffer(Allocator.TempJob))
            {
                ecb.Playback(m_Manager);
            }
            Assert.DoesNotThrow(() => AtomicSafetyHandle.CheckReadAndThrow(buf.m_Safety0));
        }

        [Test]
        public void CreateEntity_ManagedComponents()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.MultiPlayback);
            var e = cmds.CreateEntity();
            cmds.AddComponent(e, new EcsTestManagedComponent { value = "SomeString" });
            cmds.Playback(m_Manager);
            cmds.Playback(m_Manager2);

            {
                var group = m_Manager.CreateEntityQuery(typeof(EcsTestManagedComponent));
                var arr = group.ToComponentDataArray<EcsTestManagedComponent>();
                Assert.AreEqual(1, arr.Length);
                Assert.AreEqual("SomeString", arr[0].value);
                group.Dispose();
            }
            {
                var group = m_Manager2.CreateEntityQuery(typeof(EcsTestManagedComponent));
                var arr = group.ToComponentDataArray<EcsTestManagedComponent>();
                Assert.AreEqual(1, arr.Length);
                Assert.AreEqual("SomeString", arr[0].value);
                group.Dispose();
            }
        }

        [Test]
        public void CreateEntityWithArchetype_ManagedComponents()
        {
            var a = m_Manager.CreateArchetype(typeof(EcsTestManagedComponent));

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var e = cmds.CreateEntity(a);
            cmds.SetComponent(e, new EcsTestManagedComponent { value = "SomeString" });
            cmds.Playback(m_Manager);

            var group = m_Manager.CreateEntityQuery(typeof(EcsTestManagedComponent));
            var arr = group.ToComponentDataArray<EcsTestManagedComponent>();
            Assert.AreEqual(1, arr.Length);
            Assert.AreEqual("SomeString", arr[0].value);
            group.Dispose();
        }

        [Test]
        public void CreateTwoComponents_ManagedComponents()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator, PlaybackPolicy.MultiPlayback);
            var e = cmds.CreateEntity();
            cmds.AddComponent(e, new EcsTestManagedComponent { value = "SomeString" });
            cmds.AddComponent(e, new EcsTestManagedComponent2 { value = "SomeString", value2 = "SomeOtherString" });
            cmds.Playback(m_Manager);
            cmds.Playback(m_Manager2);

            using(var group = m_Manager.CreateEntityQuery(typeof(EcsTestManagedComponent)))
            {
                var component = EntityQueryManagedComponentExtensions.GetSingleton<EcsTestManagedComponent>(group);
                Assert.AreEqual("SomeString", component.value);
            }

            using(var group = m_Manager.CreateEntityQuery(typeof(EcsTestManagedComponent2)))
            {
                var component = EntityQueryManagedComponentExtensions.GetSingleton<EcsTestManagedComponent2>(group);
                Assert.AreEqual("SomeString", component.value);
                Assert.AreEqual("SomeOtherString", component.value2);
            }
            using(var group = m_Manager2.CreateEntityQuery(typeof(EcsTestManagedComponent)))
            {
                var component = EntityQueryManagedComponentExtensions.GetSingleton<EcsTestManagedComponent>(group);
                Assert.AreEqual("SomeString", component.value);
            }

            using (var group = m_Manager2.CreateEntityQuery(typeof(EcsTestManagedComponent2)))
            {
                var component = EntityQueryManagedComponentExtensions.GetSingleton<EcsTestManagedComponent2>(group);
                Assert.AreEqual("SomeString", component.value);
                Assert.AreEqual("SomeOtherString", component.value2);
            }
        }

        [Test]
        public void AddComponent_ManagedComponents()
        {
            var e = m_Manager.CreateEntity();

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            cmds.AddComponent(e, new EcsTestManagedComponent() { value = "SomeString" });
            cmds.AddComponent<EcsTestTag>(e);
            cmds.AddComponent(e, ComponentType.ReadWrite<EcsTestManagedComponent3>());

            cmds.Playback(m_Manager);

            Assert.AreEqual("SomeString", m_Manager.GetComponentData<EcsTestManagedComponent>(e).value);
            Assert.IsTrue(m_Manager.HasComponent<EcsTestTag>(e));
            Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent3>(e));
        }

        [Test]
        public void AddComponents_ManagedComponents()
        {
            var e = m_Manager.CreateEntity();

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.AddComponent(e, new ComponentTypes(typeof(EcsTestManagedComponent), typeof(EcsTestTag), typeof(EcsTestManagedComponent3)));
            cmds.SetComponent(e, new EcsTestManagedComponent() { value = "SomeString" });
            cmds.Playback(m_Manager);

            Assert.AreEqual("SomeString", m_Manager.GetComponentData<EcsTestManagedComponent>(e).value);
            Assert.IsTrue(m_Manager.HasComponent<EcsTestTag>(e));
            Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent3>(e));
        }

        [Test]
        public void AddComponentToEntityQuery_ManagedComponents_CaptureAtRecord()
        {
            var entity = m_Manager.CreateEntity();
            var data1 = new EcsTestManagedComponent();
            m_Manager.AddComponentData(entity, data1);
            m_Manager.AddComponent(entity, typeof(EcsTestManagedComponent));

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestManagedComponent)))
            {
                cmds.AddComponentForEntityQuery(entityQuery, typeof(EcsTestManagedComponent2));

                // modifying the entity in between recording and playback should be OK
                m_Manager.AddComponent<EcsTestData3>(entityQuery);

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(1, entities.Length);
                    Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent2>(entities[0]), "The component was not added to the entities within the entity query.");
                }
            }
        }

        [Test]
        public void AddComponentToEntityQueryWithFilter_ManagedComponents_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp));

            var entity1 = m_Manager.CreateEntity(archetype);
            var sharedComponent1 = new EcsTestSharedComp { value = 10 };
            m_Manager.SetSharedComponentData(entity1, sharedComponent1);

            var entity2 = m_Manager.CreateEntity(archetype);
            var sharedComponent2 = new EcsTestSharedComp { value = 130 };
            m_Manager.SetSharedComponentData(entity2, sharedComponent2);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp)))
            {
                entityQuery.SetSharedComponentFilter(sharedComponent2);
                cmds.AddComponentForEntityQuery(entityQuery, typeof(EcsTestManagedComponent2));

                // modifying the entity in between recording and playback should be OK
                m_Manager.AddComponent<EcsTestData3>(entityQuery);

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(2, entities.Length);

                    for (var i = 0; i < entities.Length; i++)
                    {
                        var e = entities[i];
                        var shared = m_Manager.GetSharedComponentData<EcsTestSharedComp>(e);
                        if (shared.value == 10)
                        {
                            Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(e));
                            Assert.IsFalse(m_Manager.HasComponent<EcsTestManagedComponent2>(e));
                        } else
                        {
                            Assert.AreEqual(130, shared.value);
                            Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent2>(e));
                        }
                    }
                }
            }
        }

        [Test]
        public void AddComponentsToEntityQueryWithFilter_ManagedComponents_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp));

            var entity1 = m_Manager.CreateEntity(archetype);
            var sharedComponent1 = new EcsTestSharedComp { value = 10 };
            m_Manager.SetSharedComponentData(entity1, sharedComponent1);

            var entity2 = m_Manager.CreateEntity(archetype);
            var sharedComponent2 = new EcsTestSharedComp { value = 130 };
            m_Manager.SetSharedComponentData(entity2, sharedComponent2);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp)))
            {
                entityQuery.SetSharedComponentFilter(sharedComponent2);
                cmds.AddComponentForEntityQuery(entityQuery, new ComponentTypes(typeof(EcsTestManagedComponent2)));

                // modifying the entity in between recording and playback should be OK
                m_Manager.AddComponent<EcsTestData3>(entityQuery);

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(2, entities.Length);

                    for (var i = 0; i < entities.Length; i++)
                    {
                        var e = entities[i];
                        var shared = m_Manager.GetSharedComponentData<EcsTestSharedComp>(e);
                        if (shared.value == 10)
                        {
                            Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(e));
                            Assert.IsFalse(m_Manager.HasComponent<EcsTestManagedComponent2>(e));
                        } else
                        {
                            Assert.AreEqual(130, shared.value);
                            Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent2>(e));
                        }
                    }
                }
            }
        }

        [Test]
        public void RemoveComponentFromEntityQuery_ManagedComponents_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp), typeof(EcsTestManagedComponent));
            var entity = m_Manager.CreateEntity(archetype);
            var data1 = new EcsTestManagedComponent();
            m_Manager.SetComponentData(entity, data1);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestManagedComponent)))
            {
                cmds.RemoveComponentForEntityQuery(entityQuery, typeof(EcsTestManagedComponent));

                // modifying the entity in between recording and playback should be OK
                m_Manager.AddComponent<EcsTestData3>(entityQuery);

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(1, entities.Length);
                    Assert.IsFalse(m_Manager.HasComponent<EcsTestManagedComponent>(entities[0]),
                        "The component was not removed from the entities in the entity query.");
                }
            }
        }

        [Test]
        public void RemoveComponentFromEntityQuery_ValidateComponents_ManagedComponents_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestManagedComponent), typeof(EcsTestManagedComponent2), typeof(EcsTestManagedComponent3), typeof(EcsTestManagedComponent4));

            var entity = m_Manager.CreateEntity(archetype);
            var data1 = new EcsTestManagedComponent() { value = "SomeString" };
            var data2 = new EcsTestManagedComponent2() { value = "SomeOtherString" };
            var data3 = new EcsTestManagedComponent3() { value = "YetAnotherString" };
            var data4 = new EcsTestManagedComponent4() { value = "SoManyStrings" };
            m_Manager.SetComponentData(entity, data1);
            m_Manager.SetComponentData(entity, data2);
            m_Manager.SetComponentData(entity, data3);
            m_Manager.SetComponentData(entity, data4);

            {
                var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(1, entities.Length);
                Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent>(entities[0]));
                Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent2>(entities[0]));
                Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent3>(entities[0]));
                Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent4>(entities[0]));
                Assert.AreEqual("SomeString", m_Manager.GetComponentData<EcsTestManagedComponent>(entities[0]).value);
                Assert.AreEqual("SomeOtherString", m_Manager.GetComponentData<EcsTestManagedComponent2>(entities[0]).value);
                Assert.AreEqual("YetAnotherString", m_Manager.GetComponentData<EcsTestManagedComponent3>(entities[0]).value);
                Assert.AreEqual("SoManyStrings", m_Manager.GetComponentData<EcsTestManagedComponent4>(entities[0]).value);
            }

            var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestManagedComponent));
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.RemoveComponentForEntityQuery(entityQuery, typeof(EcsTestManagedComponent2));

            // modifying the entity in between recording and playback should be OK
            m_Manager.AddComponent<EcsTestData3>(entityQuery);

            cmds.Playback(m_Manager);

            {
                var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(1, entities.Length);
                Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent>(entities[0]));
                Assert.IsFalse(m_Manager.HasComponent<EcsTestManagedComponent2>(entities[0]));
                Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent3>(entities[0]));
                Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent4>(entities[0]));
                Assert.AreEqual("SomeString", m_Manager.GetComponentData<EcsTestManagedComponent>(entities[0]).value);
                Assert.Throws<ArgumentException>(() => { m_Manager.GetComponentData<EcsTestManagedComponent2>(entities[0]); });
                Assert.AreEqual("YetAnotherString", m_Manager.GetComponentData<EcsTestManagedComponent3>(entities[0]).value);
                Assert.AreEqual("SoManyStrings", m_Manager.GetComponentData<EcsTestManagedComponent4>(entities[0]).value);
            }
            entityQuery.Dispose();
        }

        [Test]
        public void RemoveComponentFromEntityQueryWithFilter_ManagedComponents_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp), typeof(EcsTestManagedComponent));

            var entity1 = m_Manager.CreateEntity(archetype);
            var sharedComponent1 = new EcsTestSharedComp {value = 10};
            m_Manager.SetSharedComponentData(entity1, sharedComponent1);

            var entity2 = m_Manager.CreateEntity(archetype);
            var sharedComponent2 = new EcsTestSharedComp {value = 130};
            m_Manager.SetSharedComponentData(entity2, sharedComponent2);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery =
                m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp), typeof(EcsTestManagedComponent)))
            {
                entityQuery.SetSharedComponentFilter(sharedComponent2);
                cmds.RemoveComponentForEntityQuery(entityQuery, typeof(EcsTestManagedComponent));

                // modifying the entity in between recording and playback should be OK
                m_Manager.AddComponent<EcsTestData3>(entityQuery);

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(2, entities.Length);

                    for (var i = 0; i < entities.Length; i++)
                    {
                        var e = entities[i];
                        var shared = m_Manager.GetSharedComponentData<EcsTestSharedComp>(e);
                        if (shared.value == 10)
                        {
                            Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent>(e));
                        } else
                        {
                            Assert.AreEqual(130, shared.value);
                            Assert.IsFalse(m_Manager.HasComponent<EcsTestManagedComponent>(e));
                        }
                    }
                }
            }
        }

        [Test]
        public void RemoveComponentsFromEntityQueryWithFilter_ManagedComponents_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp), typeof(EcsTestManagedComponent));

            var entity1 = m_Manager.CreateEntity(archetype);
            var sharedComponent1 = new EcsTestSharedComp { value = 10 };
            m_Manager.SetSharedComponentData(entity1, sharedComponent1);

            var entity2 = m_Manager.CreateEntity(archetype);
            var sharedComponent2 = new EcsTestSharedComp { value = 130 };
            m_Manager.SetSharedComponentData(entity2, sharedComponent2);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestSharedComp), typeof(EcsTestManagedComponent)))
            {
                entityQuery.SetSharedComponentFilter(sharedComponent2);
                cmds.RemoveComponentForEntityQuery(entityQuery, new ComponentTypes(typeof(EcsTestManagedComponent)));

                // modifying the entity in between recording and playback should be OK
                m_Manager.AddComponent<EcsTestData3>(entityQuery);

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(2, entities.Length);

                    for (var i = 0; i < entities.Length; i++)
                    {
                        var e = entities[i];
                        var shared = m_Manager.GetSharedComponentData<EcsTestSharedComp>(e);
                        if (shared.value == 10)
                        {
                            Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent>(e));
                        } else
                        {
                            Assert.AreEqual(130, shared.value);
                            Assert.IsFalse(m_Manager.HasComponent<EcsTestManagedComponent>(e));
                        }
                    }
                }
            }
        }

        [Test]
        public void RemoveComponentsFromEntityQuery_ManagedComponents_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestSharedComp), typeof(EcsTestManagedComponent));

            var entity = m_Manager.CreateEntity(archetype);
            var data1 = new EcsTestManagedComponent();
            m_Manager.SetComponentData(entity, data1);

            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            using (var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestManagedComponent)))
            {
                cmds.RemoveComponentForEntityQuery(entityQuery, new ComponentTypes(typeof(EcsTestManagedComponent)));

                // modifying the entity in between recording and playback should be OK
                m_Manager.AddComponent<EcsTestData3>(entityQuery);

                cmds.Playback(m_Manager);

                using (var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator))
                {
                    Assert.AreEqual(1, entities.Length);
                    Assert.IsFalse(m_Manager.HasComponent<EcsTestManagedComponent>(entities[0]), "The component was not removed from the entities in the entity query.");
                }
            }
        }

        [Test]
        public void RemoveComponentsFromEntityQuery_ValidateComponents_ManagedComponents_CaptureAtRecord()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestManagedComponent), typeof(EcsTestManagedComponent2), typeof(EcsTestManagedComponent3), typeof(EcsTestManagedComponent4));

            var entity = m_Manager.CreateEntity(archetype);
            var data1 = new EcsTestManagedComponent() { value = "SomeString" };
            var data2 = new EcsTestManagedComponent2() { value = "SomeOtherString" };
            var data3 = new EcsTestManagedComponent3() { value = "YetAnotherString" };
            var data4 = new EcsTestManagedComponent4() { value = "SoManyStrings" };
            m_Manager.SetComponentData(entity, data1);
            m_Manager.SetComponentData(entity, data2);
            m_Manager.SetComponentData(entity, data3);
            m_Manager.SetComponentData(entity, data4);

            {
                var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(1, entities.Length);
                Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent>(entities[0]));
                Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent2>(entities[0]));
                Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent3>(entities[0]));
                Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent4>(entities[0]));
                Assert.AreEqual("SomeString", m_Manager.GetComponentData<EcsTestManagedComponent>(entities[0]).value);
                Assert.AreEqual("SomeOtherString", m_Manager.GetComponentData<EcsTestManagedComponent2>(entities[0]).value);
                Assert.AreEqual("YetAnotherString", m_Manager.GetComponentData<EcsTestManagedComponent3>(entities[0]).value);
                Assert.AreEqual("SoManyStrings", m_Manager.GetComponentData<EcsTestManagedComponent4>(entities[0]).value);
            }

            var entityQuery = m_Manager.CreateEntityQuery(typeof(EcsTestManagedComponent));
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.RemoveComponentForEntityQuery(entityQuery, new ComponentTypes(typeof(EcsTestManagedComponent2)));

            // modifying the entity in between recording and playback should be OK
            m_Manager.AddComponent<EcsTestData3>(entityQuery);

            cmds.Playback(m_Manager);

            {
                var entities = m_Manager.GetAllEntities(World.UpdateAllocator.ToAllocator);
                Assert.AreEqual(1, entities.Length);
                Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent>(entities[0]));
                Assert.IsFalse(m_Manager.HasComponent<EcsTestManagedComponent2>(entities[0]));
                Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent3>(entities[0]));
                Assert.IsTrue(m_Manager.HasComponent<EcsTestManagedComponent4>(entities[0]));
                Assert.AreEqual("SomeString", m_Manager.GetComponentData<EcsTestManagedComponent>(entities[0]).value);
                Assert.Throws<ArgumentException>(() => { m_Manager.GetComponentData<EcsTestManagedComponent2>(entities[0]); });
                Assert.AreEqual("YetAnotherString", m_Manager.GetComponentData<EcsTestManagedComponent3>(entities[0]).value);
                Assert.AreEqual("SoManyStrings", m_Manager.GetComponentData<EcsTestManagedComponent4>(entities[0]).value);
            }
            entityQuery.Dispose();
        }

        [Test]
        public void Instantiate_ManagedComponents()
        {
            var e = m_Manager.CreateEntity();
            var component = new EcsTestManagedComponent() { value = "SomeString" };
            m_Manager.AddComponentData(e, component);

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.Instantiate(e);
            cmds.Instantiate(e);
            cmds.Playback(m_Manager);

            VerifyEcsTestManagedComponent(3, "SomeString");
        }

        [Test]
        public void InstantiateWithSetComponentDataWorks_ManagedComponents()
        {
            var e = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e, new EcsTestManagedComponent() { value = "SomeString" });

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            var e1 = cmds.Instantiate(e);
            cmds.SetComponent(e1, new EcsTestManagedComponent() { value = "SomeOtherString" });

            var e2 = cmds.Instantiate(e);
            cmds.SetComponent(e2, new EcsTestManagedComponent() { value = "SomeOtherString" });

            cmds.Playback(m_Manager);

            m_Manager.DestroyEntity(e);

            VerifyEcsTestManagedComponent(2, "SomeOtherString");
        }

        [Test]
        public void DestroyEntityTwiceWorks_ManagedComponents()
        {
            var e = m_Manager.CreateEntity();
            m_Manager.AddComponentData(e, new EcsTestManagedComponent());

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            cmds.DestroyEntity(e);
            cmds.DestroyEntity(e);

            cmds.Playback(m_Manager);

            Assert.IsFalse(m_Manager.Exists(e));
        }

#if UNITY_2020_2_OR_NEWER
// 2020.2 no longer leaks strings when using Burst Abort
        [Test]
        public void DestroyInvalidEntity_ManagedComponents()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var entityBuffer = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(1, ref World.UpdateAllocator);
            var e = cmds.CreateEntity();
            cmds.AddComponent(e, new EcsTestManagedComponent { value = "SomeString" });
            entityBuffer[0] = e;
            cmds.Playback(m_Manager);

            var savedEntity = entityBuffer[0];

            var cmds2 = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds2.DestroyEntity(savedEntity);

            // savedEntity is invalid, so playing back this ECB should throw an exception
            Assert.Throws<System.InvalidOperationException>(() =>
            {
                cmds2.Playback(m_Manager);
            });
        }
#endif //UNITY_2020_2_OR_NEWER

        [Test]
        public void PlaybackWithExclusiveEntityTransactionInJob_ManagedComponents()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            var job = new TestJob {Buffer = cmds};

            var e = cmds.CreateEntity();
            cmds.AddComponent(e, new EcsTestManagedComponent { value = "SomeString" });

            var jobHandle = job.Schedule();

            var manager = m_Manager.BeginExclusiveEntityTransaction();

            var playbackJob = new EntityCommandBufferPlaybackJob()
            {
                Buffer = cmds,
                Manager = manager
            };

            m_Manager.ExclusiveEntityTransactionDependency = playbackJob.Schedule(jobHandle);
            m_Manager.EndExclusiveEntityTransaction();

            var group = m_Manager.CreateEntityQuery(typeof(EcsTestData));
            var arr = group.ToComponentDataArray<EcsTestData>(World.UpdateAllocator.ToAllocator);
            Assert.AreEqual(1, arr.Length);
            Assert.AreEqual(1, arr[0].value);
            group.Dispose();

            var managedGroup = m_Manager.CreateEntityQuery(typeof(EcsTestManagedComponent));
            var managedComponentArray = managedGroup.ToComponentDataArray<EcsTestManagedComponent>();
            Assert.AreEqual(1, managedComponentArray.Length);
            Assert.AreEqual("SomeString", managedComponentArray[0].value);
            managedGroup.Dispose();
        }

        [Test]
        public void AddManagedComponent_WithEntityPatch()
        {
            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            Entity e0 = cmds.CreateEntity();
            cmds.AddComponent(e0, new EcsTestManagedDataEntity {value1 = e0});

            cmds.Playback(m_Manager);

            using(var group = m_Manager.CreateEntityQuery(typeof(EcsTestManagedDataEntity)))
            {
                var e = group.GetSingletonEntity();
                Assert.AreEqual(e, m_Manager.GetComponentData<EcsTestManagedDataEntity>(e).value1);
            }
        }

        [Test]
        public void SetComponentEnabled_ECB()
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestDataEnableable), typeof(EcsTestDataEnableable2));
            Entity e0 = m_Manager.CreateEntity(archetype);
            m_Manager.SetComponentEnabled<EcsTestDataEnableable2>(e0, false);
            Entity e1 = m_Manager.CreateEntity(archetype);
            m_Manager.SetComponentEnabled(e1, typeof(EcsTestDataEnableable), false);

            // toggle "enabled" state on both components of both entities
            using (var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                cmds.SetComponentEnabled<EcsTestDataEnableable>(e0, false);
                cmds.SetComponentEnabled<EcsTestDataEnableable2>(e0, true);
                cmds.SetComponentEnabled(e1, typeof(EcsTestDataEnableable), true);
                cmds.SetComponentEnabled(e1, typeof(EcsTestDataEnableable2), false);
                cmds.Playback(m_Manager);
            }

            Assert.IsFalse(m_Manager.IsComponentEnabled<EcsTestDataEnableable>(e0));
            Assert.IsTrue(m_Manager.IsComponentEnabled<EcsTestDataEnableable2>(e0));
            Assert.IsTrue(m_Manager.IsComponentEnabled(e1, typeof(EcsTestDataEnableable)));
            Assert.IsFalse(m_Manager.IsComponentEnabled(e1, typeof(EcsTestDataEnableable2)));
        }

#if !DOTS_DISABLE_DEBUG_NAMES
        [Test]
        public void EntityName_SetName_ECB()
        {
            var name = new FixedString64Bytes("Test");

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);

            Entity e0 = cmds.CreateEntity();
            cmds.AddComponent(e0,typeof(EcsTestData));
            cmds.SetName(e0,name);

            cmds.Playback(m_Manager);

            using(var query = m_Manager.CreateEntityQuery(typeof(EcsTestData)))
            {
                var e = query.GetSingletonEntity();
                m_Manager.GetName(e, out var actualName);
                Assert.AreEqual(name,actualName);
            }

        }
#endif //!DOTS_DISABLE_DEBUG_NAMES

#endif //!UNITY_DISABLE_MANAGED_COMPONENTS

        [Test]
        public void CommandsAfterPlayback_Throws()
        {
            var archetype = m_Manager.CreateArchetype(ComponentType.ReadOnly<EcsTestData>());
            var e = m_Manager.CreateEntity(archetype);
            var query = m_Manager.CreateEntityQuery(ComponentType.ReadOnly<EcsTestData>());

            var cmds = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            cmds.Playback(m_Manager);

            Assert.Throws<InvalidOperationException>(() => cmds.CreateEntity());
            Assert.Throws<InvalidOperationException>(() => cmds.CreateEntity(archetype));
            Assert.Throws<InvalidOperationException>(() => cmds.Instantiate(e));
            using (var outEntities = CollectionHelper.CreateNativeArray<Entity>(100, World.UpdateAllocator.ToAllocator))
            {
                Assert.Throws<InvalidOperationException>(() => cmds.Instantiate(e,outEntities));
            }
            Assert.Throws<InvalidOperationException>(() => cmds.DestroyEntity(e));
            Assert.Throws<InvalidOperationException>(() => cmds.DestroyEntitiesForEntityQuery(query));
            Assert.Throws<InvalidOperationException>(() => cmds.AddBuffer<EcsIntElement>(e));
            Assert.Throws<InvalidOperationException>(() => cmds.SetBuffer<EcsIntElement>(e));
            Assert.Throws<InvalidOperationException>(() => cmds.AddSharedComponent(e, new EcsTestSharedComp(10)));
            Assert.Throws<InvalidOperationException>(() => cmds.SetSharedComponent(e, new EcsTestSharedComp(10)));
            Assert.Throws<InvalidOperationException>(() => cmds.AddComponent(e, new EcsTestData2(10)));
            Assert.Throws<InvalidOperationException>(() => cmds.AddComponent(e, new ComponentTypes(ComponentType.ReadOnly<EcsTestData2>())));
            Assert.Throws<InvalidOperationException>(() => cmds.SetComponent(e, new EcsTestData2(10)));
            Assert.Throws<InvalidOperationException>(() => cmds.SetComponent(e, new EcsTestData2(10)));
            Assert.Throws<InvalidOperationException>(() => cmds.AddComponentForEntityQuery(query, ComponentType.ReadOnly<EcsTestData2>()));
            Assert.Throws<InvalidOperationException>(() => cmds.AddComponent(e, new ComponentTypes(ComponentType.ReadOnly<EcsTestData2>())));
            Assert.Throws<InvalidOperationException>(() => cmds.RemoveComponent<EcsTestData>(e));
            Assert.Throws<InvalidOperationException>(() => cmds.RemoveComponentForEntityQuery(query, ComponentType.ReadOnly<EcsTestData>()));
            Assert.Throws<InvalidOperationException>(() => cmds.RemoveComponent(e, new ComponentTypes(ComponentType.ReadOnly<EcsTestData>())));
#if !UNITY_DISABLE_MANAGED_COMPONENTS
            Assert.Throws<InvalidOperationException>(() => cmds.AddComponent(e, new EcsTestManagedComponent()));
            Assert.Throws<InvalidOperationException>(() => cmds.SetComponent(e, new EcsTestManagedComponent()));
            Assert.Throws<InvalidOperationException>(() => cmds.RemoveComponent<EcsTestManagedComponent>(e));
#endif
            query.Dispose();
        }

        [BurstCompile(CompileSynchronously = true)]
        struct DestroyEntity_EntityArray_Job : IJob
        {
            public EntityCommandBuffer.ParallelWriter Ecb;
            public NativeArray<Entity> TestEntities;
            public NativeArray<Entity> DeferredEntities;
            public Entity Prefab;
            public void Execute()
            {
                Ecb.DestroyEntity(0, TestEntities);
                Ecb.Instantiate(0, Prefab, DeferredEntities);
                Ecb.DestroyEntity(0, DeferredEntities);
            }
        }

        [Test]
        public void DestroyEntity_TargetIsEntityArray_Works()
        {
            const int TEST_ENTITY_COUNT = 10;
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var prefab = m_Manager.CreateEntity(typeof(EcsTestData3), typeof(Prefab));
            using var ecb = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            using var testEntities = m_Manager.CreateEntity(archetype, 2*TEST_ENTITY_COUNT, Allocator.TempJob);
            using var controlEntities = m_Manager.CreateEntity(archetype, 2*TEST_ENTITY_COUNT, Allocator.TempJob);
            using var deferredEntities = CollectionHelper.CreateNativeArray<Entity>(2*TEST_ENTITY_COUNT, World.UpdateAllocator.ToAllocator);
            // Test main thread writer
            ecb.DestroyEntity(testEntities.GetSubArray(0, TEST_ENTITY_COUNT));
            ecb.Instantiate(prefab, deferredEntities.GetSubArray(0, TEST_ENTITY_COUNT));
            ecb.DestroyEntity(deferredEntities.GetSubArray(0, TEST_ENTITY_COUNT));
            // Test ParallelWriter
            new DestroyEntity_EntityArray_Job
            {
                Ecb = ecb.AsParallelWriter(),
                TestEntities = testEntities.GetSubArray(TEST_ENTITY_COUNT, TEST_ENTITY_COUNT),
                DeferredEntities = deferredEntities.GetSubArray(TEST_ENTITY_COUNT, TEST_ENTITY_COUNT),
                Prefab = prefab,
            }.Run();

            ecb.Playback(m_Manager);
            for (int i = 0; i < testEntities.Length; ++i)
                Assert.IsFalse(m_Manager.Exists(testEntities[i]));
            for (int i = 0; i < controlEntities.Length; ++i)
                Assert.IsTrue(m_Manager.Exists(controlEntities[i]));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData3), typeof(EcsTestData2));
            using var finalEntities = query.ToEntityArray(World.UpdateAllocator.ToAllocator);
            Assert.AreEqual(0, finalEntities.Length);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct AddComponent_EntityArray_Job : IJob
        {
            public EntityCommandBuffer.ParallelWriter Ecb;
            public NativeArray<Entity> TestEntities;
            public NativeArray<Entity> DeferredEntities;
            public Entity Prefab;
            public void Execute()
            {
                Ecb.AddComponent<EcsTestData2>(0, TestEntities);
                Ecb.Instantiate(0, Prefab, DeferredEntities);
                Ecb.AddComponent<EcsTestData2>(0, DeferredEntities);
            }
        }

        // In the EntityDataAccess, we will take a batched approach when the number of entities is >10 for this command
        [Test]
        public void AddComponent_TargetIsEntityArray_Works([Values(10, 15)] int testEntityCount)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var prefab = m_Manager.CreateEntity(typeof(EcsTestData3), typeof(Prefab));
            using var ecb = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            using var testEntities = m_Manager.CreateEntity(archetype, 2*testEntityCount, Allocator.TempJob);
            using var controlEntities = m_Manager.CreateEntity(archetype, 2*testEntityCount, Allocator.TempJob);
            using var deferredEntities = CollectionHelper.CreateNativeArray<Entity>(2*testEntityCount, World.UpdateAllocator.ToAllocator);
            // Test main thread writer
            ecb.AddComponent<EcsTestData2>(testEntities.GetSubArray(0, testEntityCount));
            ecb.Instantiate(prefab, deferredEntities.GetSubArray(0, testEntityCount));
            ecb.AddComponent<EcsTestData2>(deferredEntities.GetSubArray(0, testEntityCount));
            // Test ParallelWriter
            new AddComponent_EntityArray_Job
            {
                Ecb = ecb.AsParallelWriter(),
                TestEntities = testEntities.GetSubArray(testEntityCount, testEntityCount),
                DeferredEntities = deferredEntities.GetSubArray(testEntityCount, testEntityCount),
                Prefab = prefab,
            }.Run();

            ecb.Playback(m_Manager);
            for (int i = 0; i < testEntities.Length; ++i)
                Assert.IsTrue(m_Manager.HasComponent<EcsTestData2>(testEntities[i]));
            for (int i = 0; i < controlEntities.Length; ++i)
                Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(controlEntities[i]));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData3), typeof(EcsTestData2));
            using var finalEntities = query.ToEntityArray(World.UpdateAllocator.ToAllocator);
            Assert.AreEqual(deferredEntities.Length, finalEntities.Length);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct AddComponents_EntityArray_Job : IJob
        {
            public EntityCommandBuffer.ParallelWriter Ecb;
            public NativeArray<Entity> TestEntities;
            public NativeArray<Entity> DeferredEntities;
            public Entity Prefab;
            public ComponentTypes TypesToAdd;
            public void Execute()
            {
                Ecb.AddComponent(0, TestEntities, TypesToAdd);
                Ecb.Instantiate(0, Prefab, DeferredEntities);
                Ecb.AddComponent(0, DeferredEntities, TypesToAdd);
            }
        }

        // In the EntityDataAccess, we will take a batched approach when the number of entities is >10 for this command
        [Test]
        public void AddComponents_TargetIsEntityArray_Works([Values(10, 15)] int testEntityCount)
        {
            var typesToAdd = new ComponentTypes(typeof(EcsTestData2));
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var prefab = m_Manager.CreateEntity(typeof(EcsTestData3), typeof(Prefab));
            using var ecb = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            using var testEntities = m_Manager.CreateEntity(archetype, 2*testEntityCount, Allocator.TempJob);
            using var controlEntities = m_Manager.CreateEntity(archetype, 2*testEntityCount, Allocator.TempJob);
            using var deferredEntities = CollectionHelper.CreateNativeArray<Entity>(2*testEntityCount, World.UpdateAllocator.ToAllocator);
            // Test main thread writer
            ecb.AddComponent(testEntities.GetSubArray(0, testEntityCount), typesToAdd);
            ecb.Instantiate(prefab, deferredEntities.GetSubArray(0, testEntityCount));
            ecb.AddComponent(deferredEntities.GetSubArray(0, testEntityCount), typesToAdd);
            // Test ParallelWriter
            new AddComponents_EntityArray_Job
            {
                Ecb = ecb.AsParallelWriter(),
                TestEntities = testEntities.GetSubArray(testEntityCount, testEntityCount),
                DeferredEntities = deferredEntities.GetSubArray(testEntityCount, testEntityCount),
                Prefab = prefab,
                TypesToAdd = typesToAdd,
            }.Run();

            ecb.Playback(m_Manager);
            for (int i = 0; i < testEntities.Length; ++i)
                Assert.IsTrue(m_Manager.HasComponent<EcsTestData2>(testEntities[i]));
            for (int i = 0; i < controlEntities.Length; ++i)
                Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(controlEntities[i]));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData3), typeof(EcsTestData2));
            using var finalEntities = query.ToEntityArray(World.UpdateAllocator.ToAllocator);
            Assert.AreEqual(deferredEntities.Length, finalEntities.Length);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct AddComponentWithValue_EntityArray_Job : IJob
        {
            public EntityCommandBuffer.ParallelWriter Ecb;
            public NativeArray<Entity> TestEntities;
            public NativeArray<Entity> DeferredEntities;
            public Entity Prefab;
            public EcsTestData2 Value;
            public void Execute()
            {
                Ecb.AddComponent(0, TestEntities, Value);
                Ecb.Instantiate(0, Prefab, DeferredEntities);
                Ecb.AddComponent(0, DeferredEntities, Value);
            }
        }

        [Test]
        public void AddComponentWithValue_TargetIsEntityArray_Works()
        {
            const int TEST_ENTITY_COUNT = 10;
            var value = new EcsTestData2(17);
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var prefab = m_Manager.CreateEntity(typeof(EcsTestData3), typeof(Prefab));
            using var ecb = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            using var testEntities = m_Manager.CreateEntity(archetype, 2*TEST_ENTITY_COUNT, Allocator.TempJob);
            using var controlEntities = m_Manager.CreateEntity(archetype, 2*TEST_ENTITY_COUNT, Allocator.TempJob);
            using var deferredEntities = CollectionHelper.CreateNativeArray<Entity>(2*TEST_ENTITY_COUNT, World.UpdateAllocator.ToAllocator);
            // Test main thread writer
            ecb.AddComponent(testEntities.GetSubArray(0, TEST_ENTITY_COUNT), value);
            ecb.Instantiate(prefab, deferredEntities.GetSubArray(0, TEST_ENTITY_COUNT));
            ecb.AddComponent(deferredEntities.GetSubArray(0, TEST_ENTITY_COUNT), value);
            // Test ParallelWriter
            new AddComponentWithValue_EntityArray_Job
            {
                Ecb = ecb.AsParallelWriter(),
                TestEntities = testEntities.GetSubArray(TEST_ENTITY_COUNT, TEST_ENTITY_COUNT),
                DeferredEntities = deferredEntities.GetSubArray(TEST_ENTITY_COUNT, TEST_ENTITY_COUNT),
                Prefab = prefab,
                Value = value,
            }.Run();

            ecb.Playback(m_Manager);
            for (int i = 0; i < testEntities.Length; ++i)
                Assert.AreEqual(value, m_Manager.GetComponentData<EcsTestData2>(testEntities[i]));
            for (int i = 0; i < controlEntities.Length; ++i)
                Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(controlEntities[i]));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData3), typeof(EcsTestData2));
            using var finalEntities = query.ToEntityArray(World.UpdateAllocator.ToAllocator);
            Assert.AreEqual(deferredEntities.Length, finalEntities.Length);
            for (int i = 0; i < finalEntities.Length; ++i)
                Assert.AreEqual(value, m_Manager.GetComponentData<EcsTestData2>(finalEntities[i]));
        }

        [BurstCompile(CompileSynchronously = true)]
        struct RemoveComponent_EntityArray_Job : IJob
        {
            public EntityCommandBuffer.ParallelWriter Ecb;
            public NativeArray<Entity> TestEntities;
            public NativeArray<Entity> DeferredEntities;
            public Entity Prefab;
            public void Execute()
            {
                Ecb.RemoveComponent<EcsTestData2>(0, TestEntities);
                Ecb.Instantiate(0, Prefab, DeferredEntities);
                Ecb.RemoveComponent<EcsTestData2>(0, DeferredEntities);
            }
        }

        // In the EntityDataAccess, we will take a batched approach when the number of entities is >10 for this command
        [Test]
        public void RemoveComponent_TargetIsEntityArray_Works([Values(10, 15)] int testEntityCount)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var prefab = m_Manager.CreateEntity(typeof(EcsTestData2), typeof(EcsTestData3), typeof(Prefab));
            using var ecb = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            using var testEntities = m_Manager.CreateEntity(archetype, 2*testEntityCount, Allocator.TempJob);
            using var controlEntities = m_Manager.CreateEntity(archetype, 2*testEntityCount, Allocator.TempJob);
            using var deferredEntities = CollectionHelper.CreateNativeArray<Entity>(2*testEntityCount, World.UpdateAllocator.ToAllocator);
            // Test main thread writer
            ecb.RemoveComponent<EcsTestData2>(testEntities.GetSubArray(0, testEntityCount));
            ecb.Instantiate(prefab, deferredEntities.GetSubArray(0, testEntityCount));
            ecb.RemoveComponent<EcsTestData2>(deferredEntities.GetSubArray(0, testEntityCount));
            // Test ParallelWriter
            new RemoveComponent_EntityArray_Job
            {
                Ecb = ecb.AsParallelWriter(),
                TestEntities = testEntities.GetSubArray(testEntityCount, testEntityCount),
                DeferredEntities = deferredEntities.GetSubArray(testEntityCount, testEntityCount),
                Prefab = prefab,
            }.Run();

            ecb.Playback(m_Manager);
            for (int i = 0; i < testEntities.Length; ++i)
                Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(testEntities[i]));
            for (int i = 0; i < controlEntities.Length; ++i)
                Assert.IsTrue(m_Manager.HasComponent<EcsTestData2>(controlEntities[i]));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData3), ComponentType.Exclude<EcsTestData2>());
            using var finalEntities = query.ToEntityArray(World.UpdateAllocator.ToAllocator);
            Assert.AreEqual(deferredEntities.Length, finalEntities.Length);
        }


        [BurstCompile(CompileSynchronously = true)]
        struct RemoveComponents_EntityArray_Job : IJob
        {
            public EntityCommandBuffer.ParallelWriter Ecb;
            public NativeArray<Entity> TestEntities;
            public NativeArray<Entity> DeferredEntities;
            public Entity Prefab;
            public ComponentTypes TypesToRemove;
            public void Execute()
            {
                Ecb.RemoveComponent(0, TestEntities, TypesToRemove);
                Ecb.Instantiate(0, Prefab, DeferredEntities);
                Ecb.RemoveComponent(0, DeferredEntities, TypesToRemove);
            }
        }

        // In the EntityDataAccess, we will take a batched approach when the number of entities is >10 for this command
        [Test]
        public void RemoveComponents_TargetIsEntityArray_Works([Values(10, 15)] int testEntityCount)
        {
            var typesToRemove = new ComponentTypes(typeof(EcsTestData2));
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            var prefab = m_Manager.CreateEntity(typeof(EcsTestData2), typeof(EcsTestData3), typeof(Prefab));
            using var ecb = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            using var testEntities = m_Manager.CreateEntity(archetype, 2*testEntityCount, Allocator.TempJob);
            using var controlEntities = m_Manager.CreateEntity(archetype, 2*testEntityCount, Allocator.TempJob);
            using var deferredEntities = CollectionHelper.CreateNativeArray<Entity>(2*testEntityCount, World.UpdateAllocator.ToAllocator);
            // Test main thread writer
            ecb.RemoveComponent(testEntities.GetSubArray(0, testEntityCount), typesToRemove);
            ecb.Instantiate(prefab, deferredEntities.GetSubArray(0, testEntityCount));
            ecb.RemoveComponent(deferredEntities.GetSubArray(0, testEntityCount), typesToRemove);
            // Test ParallelWriter
            new RemoveComponents_EntityArray_Job
            {
                Ecb = ecb.AsParallelWriter(),
                TestEntities = testEntities.GetSubArray(testEntityCount, testEntityCount),
                DeferredEntities = deferredEntities.GetSubArray(testEntityCount, testEntityCount),
                Prefab = prefab,
                TypesToRemove = typesToRemove,
            }.Run();

            ecb.Playback(m_Manager);
            for (int i = 0; i < testEntities.Length; ++i)
                Assert.IsFalse(m_Manager.HasComponent<EcsTestData2>(testEntities[i]));
            for (int i = 0; i < controlEntities.Length; ++i)
                Assert.IsTrue(m_Manager.HasComponent<EcsTestData2>(controlEntities[i]));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData3), ComponentType.Exclude<EcsTestData2>());
            using var finalEntities = query.ToEntityArray(World.UpdateAllocator.ToAllocator);
            Assert.AreEqual(deferredEntities.Length, finalEntities.Length);
        }

        //[BurstCompile(CompileSynchronously = true)]
        struct AddSharedComponentWithValue_EntityArray_Job : IJob
        {
            public EntityCommandBuffer.ParallelWriter Ecb;
            public NativeArray<Entity> TestEntities;
            public NativeArray<Entity> DeferredEntities;
            public Entity Prefab;
            public EcsTestSharedComp Value;
            public void Execute()
            {
                Ecb.AddSharedComponent(0, TestEntities, Value);
                Ecb.Instantiate(0, Prefab, DeferredEntities);
                Ecb.AddSharedComponent(0, DeferredEntities, Value);
            }
        }

        [Test]
        public void AddSharedComponentWithValue_TargetIsEntityArray_Works()
        {
            const int TEST_ENTITY_COUNT = 10;
            var value = new EcsTestSharedComp(17);
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var prefab = m_Manager.CreateEntity(typeof(EcsTestData3), typeof(Prefab));
            using var ecb = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            using var testEntities = m_Manager.CreateEntity(archetype, 2*TEST_ENTITY_COUNT, Allocator.TempJob);
            using var controlEntities = m_Manager.CreateEntity(archetype, 2*TEST_ENTITY_COUNT, Allocator.TempJob);
            using var deferredEntities = CollectionHelper.CreateNativeArray<Entity>(2*TEST_ENTITY_COUNT, World.UpdateAllocator.ToAllocator);
            // Test main thread writer
            ecb.AddSharedComponent(testEntities.GetSubArray(0, TEST_ENTITY_COUNT), value);
            ecb.Instantiate(prefab, deferredEntities.GetSubArray(0, TEST_ENTITY_COUNT));
            ecb.AddSharedComponent(deferredEntities.GetSubArray(0, TEST_ENTITY_COUNT), value);
            // Test ParallelWriter
            new AddSharedComponentWithValue_EntityArray_Job
            {
                Ecb = ecb.AsParallelWriter(),
                TestEntities = testEntities.GetSubArray(TEST_ENTITY_COUNT, TEST_ENTITY_COUNT),
                DeferredEntities = deferredEntities.GetSubArray(TEST_ENTITY_COUNT, TEST_ENTITY_COUNT),
                Prefab = prefab,
                Value = value,
            }.Run();

            ecb.Playback(m_Manager);
            for (int i = 0; i < testEntities.Length; ++i)
                Assert.AreEqual(value, m_Manager.GetSharedComponentData<EcsTestSharedComp>(testEntities[i]));
            for (int i = 0; i < controlEntities.Length; ++i)
                Assert.IsFalse(m_Manager.HasComponent<EcsTestSharedComp>(controlEntities[i]));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData3), typeof(EcsTestSharedComp));
            using var finalEntities = query.ToEntityArray(World.UpdateAllocator.ToAllocator);
            Assert.AreEqual(deferredEntities.Length, finalEntities.Length);
            for (int i = 0; i < finalEntities.Length; ++i)
                Assert.AreEqual(value, m_Manager.GetSharedComponentData<EcsTestSharedComp>(finalEntities[i]));
        }

        //[BurstCompile(CompileSynchronously = true)]
        struct SetSharedComponentWithValue_EntityArray_Job : IJob
        {
            public EntityCommandBuffer.ParallelWriter Ecb;
            public NativeArray<Entity> TestEntities;
            public NativeArray<Entity> DeferredEntities;
            public Entity Prefab;
            public EcsTestSharedComp Value;
            public void Execute()
            {
                Ecb.SetSharedComponent(0, TestEntities, Value);
                Ecb.Instantiate(0, Prefab, DeferredEntities);
                Ecb.SetSharedComponent(0, DeferredEntities, Value);
            }
        }

        [Test]
        public void SetSharedComponent_TargetIsEntityArray_Works()
        {
            const int TEST_ENTITY_COUNT = 10;
            var value = new EcsTestSharedComp(17);
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestSharedComp));
            var prefab = m_Manager.CreateEntity(typeof(EcsTestData3), typeof(EcsTestSharedComp), typeof(Prefab));
            using var ecb = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator);
            using var testEntities = m_Manager.CreateEntity(archetype, 2*TEST_ENTITY_COUNT, Allocator.TempJob);
            using var controlEntities = m_Manager.CreateEntity(archetype, 2*TEST_ENTITY_COUNT, Allocator.TempJob);
            using var deferredEntities = CollectionHelper.CreateNativeArray<Entity>(2*TEST_ENTITY_COUNT, World.UpdateAllocator.ToAllocator);
            // Test main thread writer
            ecb.SetSharedComponent(testEntities.GetSubArray(0, TEST_ENTITY_COUNT), value);
            ecb.Instantiate(prefab, deferredEntities.GetSubArray(0, TEST_ENTITY_COUNT));
            ecb.SetSharedComponent(deferredEntities.GetSubArray(0, TEST_ENTITY_COUNT), value);
            // Test ParallelWriter
            new SetSharedComponentWithValue_EntityArray_Job
            {
                Ecb = ecb.AsParallelWriter(),
                TestEntities = testEntities.GetSubArray(TEST_ENTITY_COUNT, TEST_ENTITY_COUNT),
                DeferredEntities = deferredEntities.GetSubArray(TEST_ENTITY_COUNT, TEST_ENTITY_COUNT),
                Prefab = prefab,
                Value = value,
            }.Run();

            ecb.Playback(m_Manager);
            for (int i = 0; i < testEntities.Length; ++i)
                Assert.AreEqual(value, m_Manager.GetSharedComponentData<EcsTestSharedComp>(testEntities[i]));
            for (int i = 0; i < controlEntities.Length; ++i)
                Assert.AreNotEqual(value, m_Manager.GetSharedComponentData<EcsTestSharedComp>(controlEntities[i]));
            using var query = m_Manager.CreateEntityQuery(typeof(EcsTestData3), typeof(EcsTestSharedComp));
            using var finalEntities = query.ToEntityArray(World.UpdateAllocator.ToAllocator);
            Assert.AreEqual(deferredEntities.Length, finalEntities.Length);
            for (int i = 0; i < finalEntities.Length; ++i)
                Assert.AreEqual(value, m_Manager.GetSharedComponentData<EcsTestSharedComp>(finalEntities[i]));
        }
    }
}
