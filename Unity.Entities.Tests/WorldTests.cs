using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Core;
using Unity.Jobs;
using Unity.Mathematics;
using System.Diagnostics;
#if !UNITY_PORTABLE_TEST_RUNNER
using System.Reflection;
using System.Linq;
#endif


namespace Unity.Entities.Tests
{
    public class WorldTests : ECSTestsCommonBase
    {
        World m_PreviousWorld;

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            m_PreviousWorld = World.DefaultGameObjectInjectionWorld;
        }

        [TearDown]
        public override void TearDown()
        {
            World.DefaultGameObjectInjectionWorld = m_PreviousWorld;
            base.TearDown();
        }

        [Test]
        public void ActiveWorldResets()
        {
            var copy = CopyWorlds(alsoClear: true);
            try
            {
                var worldA = new World("WorldA");
                var worldB = new World("WorldB");

                World.DefaultGameObjectInjectionWorld = worldB;

                Assert.AreEqual(worldB, World.DefaultGameObjectInjectionWorld);
                Assert.That(World.All[0], Is.EqualTo(worldA));
                Assert.That(World.All[1], Is.EqualTo(worldB));

                worldB.Dispose();

                Assert.That(worldB.IsCreated, Is.False);
                Assert.That(worldA.IsCreated, Is.True);
                Assert.That(World.DefaultGameObjectInjectionWorld, Is.Null);

                worldA.Dispose();

                Assert.That(World.All.Count, Is.EqualTo(0));
            }
            finally
            {
                ResetWorlds(copy);
            }
        }

        class TestManager : ComponentSystem
        {
            protected override void OnUpdate() {}
        }

        [Test]
        public void WorldVersionIsConsistent()
        {
            using (var world = new World("WorldX"))
            {
                Assert.AreEqual(0, world.Version);

                var version = world.Version;
                world.GetOrCreateSystem<TestManager>();
                Assert.AreNotEqual(version, world.Version);

                version = world.Version;
                var manager = world.GetOrCreateSystem<TestManager>();
                Assert.AreEqual(version, world.Version);

                version = world.Version;
                world.DestroySystem(manager);
                Assert.AreNotEqual(version, world.Version);
            }
        }

        [Test]
        public void UsingDisposedWorldThrows()
        {
            var world = new World("WorldX");
            world.Dispose();

            Assert.Throws<ArgumentException>(() => world.GetExistingSystem<TestManager>());
        }

        class AddWorldDuringConstructorThrowsSystem : ComponentSystem
        {
            public AddWorldDuringConstructorThrowsSystem()
            {
                Assert.IsNull(World);
                World.DefaultGameObjectInjectionWorld.AddSystem(this);
            }

            protected override void OnUpdate() {}
        }

        class SystemThrowingInOnCreateIsRemovedSystem : ComponentSystem
        {
            protected override void OnCreate()
            {
                throw new AssertionException("");
            }

            protected override void OnUpdate() {}
        }
        [Test]
        public void SystemThrowingInOnCreateIsRemoved()
        {
            using (var world = new World("WorldX"))
            {
                Assert.AreEqual(0, world.Systems.Count);

                Assert.Throws<AssertionException>(() =>
                    world.GetOrCreateSystem<SystemThrowingInOnCreateIsRemovedSystem>());

                // throwing during OnCreateManager does not add the manager to the behaviour manager list
                Assert.AreEqual(0, world.Systems.Count);
            }
        }

        class SystemIsAccessibleDuringOnCreateManagerSystem : ComponentSystem
        {
            protected override void OnCreate()
            {
                Assert.AreEqual(this, World.GetOrCreateSystem<SystemIsAccessibleDuringOnCreateManagerSystem>());
            }

            protected override void OnUpdate() {}
        }

        [Test]
        [IgnoreInPortableTests("There is an Assert.AreEqual(object, object) which in the SystemIsAccessibleDuringOnCreateManagerSystem.OnCreate, which the runner doesn't find.")]
        public void SystemIsAccessibleDuringOnCreateManager()
        {
            using (var world = new World("WorldX"))
            {
                Assert.AreEqual(0, world.Systems.Count);
                world.CreateSystem<SystemIsAccessibleDuringOnCreateManagerSystem>();
                Assert.AreEqual(1, world.Systems.Count);
            }
        }

        //@TODO: Test for adding a manager from one world to another.

        [Test]
        public unsafe void WorldNoOverlappingChunkSequenceNumbers()
        {
            using(var worldA = new World("WorldA"))
            using(var worldB = new World("WorldB"))
            {
                World.DefaultGameObjectInjectionWorld = worldB;

                worldA.EntityManager.CreateEntity();
                worldB.EntityManager.CreateEntity();

                var worldAChunks = worldA.EntityManager.GetAllChunks();
                var worldBChunks = worldB.EntityManager.GetAllChunks();

                for (int i = 0; i < worldAChunks.Length; i++)
                {
                    var chunkA = worldAChunks[i].m_Chunk;
                    for (int j = 0; j < worldBChunks.Length; j++)
                    {
                        var chunkB = worldBChunks[i].m_Chunk;
                        var sequenceNumberDiff = chunkA->SequenceNumber - chunkB->SequenceNumber;

                        // Any chunk sequence numbers in different worlds should be separated by at least 32 bits
                        Assert.IsTrue(sequenceNumberDiff > 1 << 32);
                    }
                }

                worldAChunks.Dispose();
                worldBChunks.Dispose();
            }
        }

        [Test]
        public unsafe void WorldChunkSequenceNumbersNotReused()
        {
            using (var worldA = new World("WorldA"))
            {
                ulong lastChunkSequenceNumber = 0;
                {
                    var entity = worldA.EntityManager.CreateEntity();
                    var chunk = worldA.EntityManager.GetChunk(entity);
                    lastChunkSequenceNumber = chunk.m_Chunk->SequenceNumber;

                    worldA.EntityManager.DestroyEntity(entity);
                }

                for (int i = 0; i < 1000; i++)
                {
                    var entity = worldA.EntityManager.CreateEntity();
                    var chunk = worldA.EntityManager.GetChunk(entity);
                    var chunkSequenceNumber = chunk.m_Chunk->SequenceNumber;

                    // Sequence numbers should be increasing and should not be reused when chunk is re-used (after zero count)
                    Assert.IsTrue(chunkSequenceNumber > lastChunkSequenceNumber);
                    lastChunkSequenceNumber = chunkSequenceNumber;

                    worldA.EntityManager.DestroyEntity(entity);
                }
            }
        }

        [UpdateInGroup(typeof(SimulationSystemGroup))]
        public class UpdateCountSystem : ComponentSystem
        {
            public double lastUpdateTime;
            public float lastUpdateDeltaTime;
            public int updateCount;

            protected override void OnUpdate()
            {
                lastUpdateTime = Time.ElapsedTime;
                lastUpdateDeltaTime = Time.DeltaTime;
                updateCount++;
            }
        }

        [Test]
        public void WorldSimulation_FixedRateUtils_Simple()
        {
            using (var world = new World("World A"))
            {
                var sim = world.GetOrCreateSystem<SimulationSystemGroup>();
                var uc = world.GetOrCreateSystem<UpdateCountSystem>();
                sim.AddSystemToUpdateList(uc);

                // Unity.Core.Hybrid.UpdateWorldTimeSystem
                var timeData = new TimeData();

                void AdvanceWorldTime(float amount)
                {
                    uc.updateCount = 0;
                    timeData = new TimeData(timeData.ElapsedTime + amount, amount);
                    world.SetTime(timeData);
                }

                sim.FixedRateManager = new FixedRateUtils.FixedRateSimpleManager(1.0f);

                // first frame will tick at elapsedTime=0
                AdvanceWorldTime(0.5f);
                world.Update();
                Assert.AreEqual(0.0, uc.lastUpdateTime, 0.001f);
                Assert.AreEqual(1.0, uc.lastUpdateDeltaTime, 0.001f);
                Assert.AreEqual(1, uc.updateCount);

                // Advance elapsed time to 1.6 seconds. the second tick should occur at 1.0
                AdvanceWorldTime(1.1f);
                world.Update();
                Assert.AreEqual(1.0, uc.lastUpdateTime, 0.001f);
                Assert.AreEqual(1.0, uc.lastUpdateDeltaTime, 0.001f);
                Assert.AreEqual(1, uc.updateCount);

                // Advance elapsed time to 1.7 seconds. the third tick should occur at 2.0
                AdvanceWorldTime(0.1f);
                world.Update();
                Assert.AreEqual(2.0, uc.lastUpdateTime, 0.001f);
                Assert.AreEqual(1.0, uc.lastUpdateDeltaTime, 0.001f);
                Assert.AreEqual(1, uc.updateCount);

                // Advance elapsed time to 2.7 seconds. The third tick should occur at 3.0
                AdvanceWorldTime(1.0f);
                world.Update();
                Assert.AreEqual(3.0, uc.lastUpdateTime, 0.001f);
                Assert.AreEqual(1.0, uc.lastUpdateDeltaTime, 0.001f);
                Assert.AreEqual(1, uc.updateCount);

                // Advance elapsed time to 4.9 seconds. The fourth tick should occur at 4.0
                AdvanceWorldTime(2.2f);
                world.Update();
                Assert.AreEqual(4.0, uc.lastUpdateTime, 0.001f);
                Assert.AreEqual(1.0, uc.lastUpdateDeltaTime, 0.001f);
                Assert.AreEqual(1, uc.updateCount);
            }
        }

        [Test]
        public void WorldSimulation_FixedRateUtils_CatchUp()
        {
            using (var world = new World("World A"))
            {
                var sim = world.GetOrCreateSystem<SimulationSystemGroup>();
                var uc = world.GetOrCreateSystem<UpdateCountSystem>();
                sim.AddSystemToUpdateList(uc);

                // Unity.Core.Hybrid.UpdateWorldTimeSystem
                var timeData = new TimeData();

                void AdvanceWorldTime(float amount)
                {
                    uc.updateCount = 0;
                    timeData = new TimeData(timeData.ElapsedTime + amount, amount);
                    world.SetTime(timeData);
                }

                sim.FixedRateManager = new FixedRateUtils.FixedRateCatchUpManager(0.1f);

                // first frame will tick at elapsedTime=0
                AdvanceWorldTime(0.05f);
                world.Update();
                Assert.AreEqual(0.0, uc.lastUpdateTime, 0.001f);
                Assert.AreEqual(0.1, uc.lastUpdateDeltaTime, 0.001f);
                Assert.AreEqual(1, uc.updateCount);

                // Advance elapsed time to 0.16 seconds. the second tick should occur at 0.1
                AdvanceWorldTime(0.11f);
                world.Update();
                Assert.AreEqual(0.1, uc.lastUpdateTime, 0.001f);
                Assert.AreEqual(0.1, uc.lastUpdateDeltaTime, 0.001f);
                Assert.AreEqual(1, uc.updateCount);

                // Advance elapsed time to 0.17 seconds. No tick should occur.
                AdvanceWorldTime(0.01f);
                world.Update();
                Assert.AreEqual(0.1, uc.lastUpdateTime, 0.001f);
                Assert.AreEqual(0.1, uc.lastUpdateDeltaTime, 0.001f);
                Assert.AreEqual(0, uc.updateCount);

                // Advance elapsed time to 0.27 seconds. The third tick should occur at 0.20
                AdvanceWorldTime(0.1f);
                world.Update();
                Assert.AreEqual(0.2, uc.lastUpdateTime, 0.001f);
                Assert.AreEqual(0.1, uc.lastUpdateDeltaTime, 0.001f);
                Assert.AreEqual(1, uc.updateCount);

                // Advance elapsed time to 0.49 seconds. The fourth and fifth ticks should occur at 0.30 and 0.40.
                AdvanceWorldTime(0.22f);
                world.Update();
                Assert.AreEqual(0.40, uc.lastUpdateTime, 0.001f);
                Assert.AreEqual(0.1, uc.lastUpdateDeltaTime, 0.001f);
                Assert.AreEqual(2, uc.updateCount);
            }
        }

#if !UNITY_PORTABLE_TEST_RUNNER
// https://unity3d.atlassian.net/browse/DOTSR-1432
        [Test]
        public void DisposeAllWorlds()
        {
            var worlds = CopyWorlds(alsoClear: true);
            try
            {
                var createdWorlds = new[] { new World("a"), new World("b") };

                foreach (var world in World.All)
                {
                    Assert.That(world.IsCreated, Is.True);
                }

                World.DisposeAllWorlds();

                Assert.That(World.All.Count, Is.EqualTo(0));
                Assert.IsFalse(createdWorlds.All(w => w.IsCreated));
            }
            finally
            {
                ResetWorlds(worlds);
            }
        }

#endif

#if !UNITY_PORTABLE_TEST_RUNNER
// https://unity3d.atlassian.net/browse/DOTSR-1432
        [Test]
        public void IteratingOverBoxedNoAllocReadOnlyCollectionThrows()
        {
            var sourceList = Enumerable.Range(1, 10).ToList();
            var readOnlyCollection = new World.NoAllocReadOnlyCollection<int>(sourceList);

            var ex = Assert.Throws<NotSupportedException>(() => ((IEnumerable<int>)readOnlyCollection).GetEnumerator());
            var ex2 = Assert.Throws<NotSupportedException>(() => ((IEnumerable)readOnlyCollection).GetEnumerator());
            Assert.That(ex.Message, Is.EqualTo($"To avoid boxing, do not cast {nameof(World.NoAllocReadOnlyCollection<int>)} to IEnumerable<T>."));
            Assert.That(ex2.Message, Is.EqualTo($"To avoid boxing, do not cast {nameof(World.NoAllocReadOnlyCollection<int>)} to IEnumerable."));
        }

#endif

#if UNITY_EDITOR
        [Test]
        public void WorldTimeSingletonHasAnEntityName()
        {
            using (var world = new World("w"))
            using (var timeSingletonQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<WorldTime>(), ComponentType.ReadWrite<WorldTimeQueue>()))
            {
                Assert.That(timeSingletonQuery.IsEmptyIgnoreFilter, Is.True);

                world.SetTime(new TimeData(10, 0.1f));
                Assert.That(timeSingletonQuery.IsEmptyIgnoreFilter, Is.False);
                var timeSingleton = timeSingletonQuery.GetSingletonEntity();
                Assert.That(world.EntityManager.GetName(timeSingleton), Is.EqualTo("WorldTime"));
            }
        }

#endif

        public class ContainerOwnerSystem : JobComponentSystem
        {
            public NativeArray<int> Container;
            protected override void OnCreate()
            {
                Container = new NativeArray<int>(1, Allocator.Persistent);
            }

            protected override void OnDestroy()
            {
                Container.Dispose();
            }

            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                return inputDeps;
            }
        }

        public class ContainerUsingSystem : JobComponentSystem
        {
            public struct ContainerJob : IJob
            {
                public NativeArray<int> Container;
                public void Execute()
                {}
            }
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                var job = new ContainerJob {Container = World.GetExistingSystem<ContainerOwnerSystem>().Container};
                return job.Schedule(inputDeps);
            }
        }

        [Test]
        public void World_DisposeWithRunningJobs_Succeeds()
        {
            using (var w = new World("Test"))
            {
                // Ordering is important, the owner system needs to be destroyed before the user system
                var user = w.GetOrCreateSystem<ContainerUsingSystem>();
                var owner = w.GetOrCreateSystem<ContainerOwnerSystem>();

                owner.Update();
                user.Update();
            }
        }

        public static World[] CopyWorlds(bool alsoClear = false)
        {
            var worlds = World.s_AllWorlds.ToArray();
            if (alsoClear)
                World.s_AllWorlds.Clear();
            return worlds;
        }

        public static void ResetWorlds(params World[] world)
        {
            World.s_AllWorlds.Clear();
            foreach (var w in world)
            {
                World.s_AllWorlds.Add(w);
            }
        }

        public class MultiPhaseTestSystem : ComponentSystem
        {
            private int TotalSystemCount;
            public bool IsRunning;
            protected override void OnStartRunning()
            {
                base.OnStartRunning();
                IsRunning = true;
            }

            protected override void OnStopRunning()
            {
                base.OnStopRunning();
                // All systems should still exist
                Assert.AreEqual(TotalSystemCount, World.Systems.Count);
                // Systems should not yet be destroyed
                foreach (var system in World.Systems)
                {
                    Assert.AreEqual(system.World, World); // stand-in for "has system.OnAfterDestroyInternal been called"
                }

                IsRunning = false;
            }

            protected override void OnDestroy()
            {
                base.OnDestroy();
                // All systems should still exist
                Assert.AreEqual(TotalSystemCount, World.Systems.Count);
                // Systems should all be stopped and disabled, but not yet destroyed
                foreach (var system in World.Systems)
                {
                    Assert.IsFalse((system as MultiPhaseTestSystem)?.IsRunning ?? false);
                    Assert.AreEqual(system.World, World); // stand-in for "has system.OnAfterDestroyInternal been called"
                }
            }

            protected override void OnUpdate()
            {
                TotalSystemCount = World.Systems.Count;
            }
        }
        public class MultiPhaseTestSystem1 : MultiPhaseTestSystem
        {
        }
        public class MultiPhaseTestSystem2 : MultiPhaseTestSystem
        {
        }
        public class MultiPhaseTestSystem3 : MultiPhaseTestSystem
        {
        }

        [Test]
        [IgnoreInPortableTests("There is an Assert.AreEqual(object, object) which in the OnStopRunning, which the runner doesn't find.")]
        public void World_Dispose_MultiPhaseSystemDestroy()
        {
            World world = new World("WorldX");
            var sys1 = world.CreateSystem<MultiPhaseTestSystem1>();
            var sys2 = world.CreateSystem<MultiPhaseTestSystem2>();
            var sys3 = world.CreateSystem<MultiPhaseTestSystem3>();
            sys1.Update();
            sys2.Update();
            sys3.Update();
            world.Dispose();
            Assert.AreEqual(0, world.Systems.Count);
        }

        struct BadUnmanagedSystem : ISystemBase
        {
            object m_TheThingThatShouldNotBe;

            public void OnCreate(ref SystemState state)
            {
            }

            public void OnDestroy(ref SystemState state)
            {
            }

            public void OnUpdate(ref SystemState state)
            {
            }
        }

        struct AcceptableUnmanagedSystem : ISystemBase
        {
            int m_TheThingThatIsOK;

            public void OnCreate(ref SystemState state)
            {
            }

            public void OnDestroy(ref SystemState state)
            {
            }

            public void OnUpdate(ref SystemState state)
            {
            }
        }

        [Test]
        [DotsRuntimeFixme]
        public void CreatingUnmanagedSystemWithManagedTypesThrows()
        {
            using (World w = new World("foo"))
            {
                w.AddSystem<AcceptableUnmanagedSystem>();
                w.GetOrCreateSystem<AcceptableUnmanagedSystem>();
                Assert.Throws<ArgumentException>(() => w.GetOrCreateSystem<BadUnmanagedSystem>());
                Assert.Throws<ArgumentException>(() => w.AddSystem<BadUnmanagedSystem>());
            }
        }

    }

    [BurstCompile]
    public unsafe class StateAllocatorTests
    {
        private struct SystemDummy
        {
            public fixed byte Bytes[4097];
        }

        private WorldUnmanagedImpl.StateAllocator alloc;
        private SystemDummy systems;

        [SetUp]
        public void SetUp()
        {
            alloc.Init();
        }

        [TearDown]
        public void TearDown()
        {
            alloc.Dispose();
        }

        internal static int CountLiveByBits(ref WorldUnmanagedImpl.StateAllocator alloc)
        {
            int live = 0;

            for (int i = 0; i < 64; ++i)
            {
                live += math.countbits(~alloc.m_Level1[i].FreeBits);
            }

            return live;
        }

        internal static int CountLiveByPointer(ref WorldUnmanagedImpl.StateAllocator alloc)
        {
            int live = 0;

            for (int i = 0; i < 64; ++i)
            {
                for (int s = 0; s < 64; ++s)
                {
                    live += alloc.m_Level1[i].SystemPointer[s] != 0 ? 1 : 0;
                }
            }

            return live;
        }

        internal static void SanityCheck(ref WorldUnmanagedImpl.StateAllocator alloc)
        {
        }

        [Test]
        public void BasicConstruction()
        {
            Assert.AreEqual(0, CountLiveByBits(ref alloc));
        }

        [Test]
        public void SimpleTest()
        {
            fixed(byte* b = systems.Bytes)
            {
                var p1 = alloc.Alloc(out var h1, out var v1, b + 0, 987);
                var p2 = alloc.Alloc(out var h2, out var v2, b + 1, 986);
                var p3 = alloc.Alloc(out var h3, out var v3, b + 2, 985);

                Assert.AreNotEqual((IntPtr)p1, (IntPtr)p2);
                Assert.AreNotEqual((IntPtr)p2, (IntPtr)p3);
                Assert.AreNotEqual((IntPtr)p1, (IntPtr)p3);

                Assert.AreNotEqual(0, v1);
                Assert.AreNotEqual(0, v2);
                Assert.AreNotEqual(0, v3);

                Assert.AreNotEqual(h1, h2);
                Assert.AreNotEqual(h2, h3);
                Assert.AreNotEqual(h3, h1);

                Assert.AreEqual(3, CountLiveByBits(ref alloc));
                Assert.AreEqual(3, CountLiveByPointer(ref alloc));

                alloc.Free(h2);

                Assert.AreEqual(2, CountLiveByBits(ref alloc));
                Assert.AreEqual(2, CountLiveByPointer(ref alloc));

                var p2_ = alloc.Alloc(out var h2_, out var v2_, b + 1, 981);

                Assert.AreEqual(3, CountLiveByBits(ref alloc));
                Assert.AreEqual(3, CountLiveByPointer(ref alloc));

                Assert.AreEqual((IntPtr)p2, (IntPtr)p2_);
                Assert.AreEqual(h2, h2_);
                Assert.AreNotEqual(v2, v2_);
            }
        }


        #if !NET_DOTS
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ThrowCountIsWrong()
        {
            throw new InvalidOperationException("count is wrong");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ThrowResolveFailed()
        {
            throw new InvalidOperationException("resolve failed");
        }

        internal delegate void RunBurstTest(IntPtr allocPtr, IntPtr sysPtr);
        [BurstCompile(CompileSynchronously = true)]
        static void RunStressTest(IntPtr allocPtr, IntPtr sys_)
        {
            var alloc = (WorldUnmanagedImpl.StateAllocator*)allocPtr;
            var sys = (byte*)sys_;
            ushort* handles = stackalloc ushort[4096];
            ushort* versions = stackalloc ushort[4096];

            // Fill the allocator completely
            for (int i = 0; i < 4096; ++i)
            {
                var p = alloc->Alloc(out handles[i], out versions[i], sys + i, i + 1);
            }

            if (CountLiveByBits(ref *alloc) != 4096)
                ThrowCountIsWrong();

            if (CountLiveByPointer(ref *alloc) != 4096)
                ThrowCountIsWrong();

            // They should all resolve
            for (int i = 0; i < 4096; ++i)
            {
                if (null == alloc->Resolve(handles[i], versions[i]))
                    ThrowResolveFailed();
            }

            // Free every other system
            for (int i = 0; i < 4096; i += 2)
            {
                alloc->Free(handles[i]);
            }

            // Every other system should resolve
            for (int i = 0; i < 4096; i += 2)
            {
                bool freed = 0 == (i & 1);
                if (freed)
                {
                    if (null != alloc->Resolve(handles[i], versions[i]))
                        ThrowResolveFailed();
                }
                else
                {
                    if (null == alloc->Resolve(handles[i], versions[i]))
                        ThrowResolveFailed();
                }
            }

            if (CountLiveByBits(ref *alloc) != 2048)
                ThrowCountIsWrong();

            if (CountLiveByPointer(ref *alloc) != 2048)
                ThrowCountIsWrong();
        }

        [Test]
        public void StressTestFromBurst()
        {
            fixed(WorldUnmanagedImpl.StateAllocator* p = &alloc)
            fixed(byte* s = systems.Bytes)
            {
                BurstCompiler.CompileFunctionPointer<RunBurstTest>(RunStressTest).Invoke((IntPtr)p, (IntPtr)s);
            }
        }

        [Test]
        public void StressTestFromMono()
        {
            fixed(WorldUnmanagedImpl.StateAllocator* p = &alloc)
            fixed(byte* s = systems.Bytes)
            {
                RunStressTest((IntPtr)p, (IntPtr)s);
            }
        }

        #endif
    }
}
