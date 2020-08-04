using System;
using NUnit.Framework;
using Unity.Burst;

namespace Unity.Entities.Tests
{
    [BurstCompile]
    public class EntityManagerUnmanagedTests : ECSTestsFixture
    {
        private struct MyUnmanagedSystem2 : ISystemBase
        {
            public int UpdateCount;
            public int Dummy;

            public void OnCreate(ref SystemState state)
            {
            }

            public void OnDestroy(ref SystemState state)
            {
            }

            public void OnUpdate(ref SystemState state)
            {
                ++UpdateCount;
            }
        }

        [BurstCompile]
        private struct MyUnmanagedSystem2WithBurst : ISystemBase
        {
            public int UpdateCount;
            public int Dummy;

            public void OnCreate(ref SystemState state)
            {
            }

            public void OnDestroy(ref SystemState state)
            {
            }


            [BurstCompile(CompileSynchronously = true)]
            public void OnUpdate(ref SystemState state)
            {
                ++UpdateCount;
            }
        }

        [Test]
        [DotsRuntimeFixme] // Need to initialize SystemBaseRegistry on startup
        public void UnmanagedSystemLifetime()
        {
            SystemHandle<MyUnmanagedSystem2> sysHandle = default;
            Assert.Throws<InvalidOperationException>(() => World.ResolveSystem(sysHandle));

            using (var world = new World("Temp"))
            {
                Assert.Throws<InvalidOperationException>(() => World.ResolveSystem(sysHandle));
                var sys = world.AddSystem<MyUnmanagedSystem2>();
                sysHandle = sys.Handle;
                Assert.IsTrue(world.IsSystemValid(sysHandle));
                Assert.IsFalse(World.IsSystemValid(sysHandle));
                ref var sys2 = ref world.ResolveSystem(sysHandle);
            }

            Assert.IsFalse(World.IsSystemValid(sysHandle));
            Assert.Throws<InvalidOperationException>(() => World.ResolveSystem(sysHandle));
        }

        [Test]
        [DotsRuntimeFixme] // Need to initialize SystemBaseRegistry on startup
        public void UnmanagedSystemLookup()
        {
            var s1 = World.AddSystem<MyUnmanagedSystem2>();
            var s2 = World.AddSystem<MyUnmanagedSystem2>();

            s1.Struct.Dummy = 19;
            s2.Struct.Dummy = -19;

            // We don't know which one we'll get currently, but the point is there will be two.
            Assert.AreEqual(19, Math.Abs(World.GetExistingSystem<MyUnmanagedSystem2>().Struct.Dummy));

            World.DestroySystem(s1.Handle);

            Assert.AreEqual(19, Math.Abs(World.GetExistingSystem<MyUnmanagedSystem2>().Struct.Dummy));

            World.DestroySystem(s2.Handle);

            Assert.Throws<InvalidOperationException>(() => World.GetExistingSystem<MyUnmanagedSystem2>());
        }

        [Test]
        [DotsRuntimeFixme] // Need to initialize SystemBaseRegistry on startup
        public unsafe void RegistryCallManagedToManaged()
        {
            var sysRef = World.AddSystem<MyUnmanagedSystem2>();
            var statePtr = World.ResolveSystemState(sysRef.Handle);
            SystemBaseRegistry.CallOnUpdate(statePtr);
            ref var sys = ref World.ResolveSystem(sysRef.Handle);
            Assert.AreEqual(1, sys.UpdateCount);
        }

        [Test]
        [DotsRuntimeFixme] // Need to initialize SystemBaseRegistry on startup
        public unsafe void RegistryCallManagedToBurst()
        {
            var sysId = World.AddSystem<MyUnmanagedSystem2WithBurst>();
            var statePtr = World.ResolveSystemState(sysId.Handle);
            SystemBaseRegistry.CallOnUpdate(statePtr);
            ref var sys = ref World.ResolveSystem(sysId.Handle);
            Assert.AreEqual(1, sys.UpdateCount);
        }

        internal unsafe delegate void DispatchDelegate(SystemState* state);

        [BurstCompile(CompileSynchronously = true)]
        private unsafe static void DispatchUpdate(SystemState* state)
        {
            SystemBase.UnmanagedUpdate(state, out _);
        }

#if !UNITY_DOTSRUNTIME
        [Test]
        public unsafe void RegistryCallBurstToManaged()
        {
            var sysRef = World.AddSystem<MyUnmanagedSystem2>();
            var statePtr = World.ResolveSystemState(sysRef.Handle);
            BurstCompiler.CompileFunctionPointer<DispatchDelegate>(DispatchUpdate).Invoke(statePtr);
            ref var sys = ref World.ResolveSystem(sysRef.Handle);
            Assert.AreEqual(1, sys.UpdateCount);
        }

        [Test]
        public unsafe void RegistryCallBurstToBurst()
        {
            var sysRef = World.AddSystem<MyUnmanagedSystem2WithBurst>();
            var statePtr = World.ResolveSystemState(sysRef.Handle);
            BurstCompiler.CompileFunctionPointer<DispatchDelegate>(DispatchUpdate).Invoke(statePtr);
            ref var sys = ref World.ResolveSystem(sysRef.Handle);
            Assert.AreEqual(1, sys.UpdateCount);
        }

#endif

        private class SnoopGroup : ComponentSystemGroup
        {
        }

        [BurstCompile]
        private struct SnoopSystemBase : ISystemBase
        {
            internal static int Flags = 0;

            public void OnCreate(ref SystemState state)
            {
                Flags |= 1;
            }

            public void OnUpdate(ref SystemState state)
            {
                Flags |= 2;
            }

            public void OnDestroy(ref SystemState state)
            {
                Flags |= 4;
            }
        }

        [Test]
        [DotsRuntimeFixme] // Need to initialize SystemBaseRegistry on startup
        public void UnmanagedSystemUpdate()
        {
            SnoopSystemBase.Flags = 0;

            using (var world = new World("Temp"))
            {
                var group = world.GetOrCreateSystem<SimulationSystemGroup>();

                var sysRef = world.AddSystem<SnoopSystemBase>();

                @group.AddSystemToUpdateList(sysRef.Handle);

                Assert.AreEqual(1, SnoopSystemBase.Flags, "OnCreate was not called");

                world.Update();

                Assert.AreEqual(3, SnoopSystemBase.Flags, "OnUpdate was not called");
            }

            Assert.AreEqual(7, SnoopSystemBase.Flags, "OnDestroy was not called");
        }
    }
}
