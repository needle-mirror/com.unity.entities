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

        private unsafe struct MyUnmanagedSystemWithStartStop : ISystemBase, ISystemBaseStartStop
        {
            public int* Ptr;
            public int createdFlag;

            public void OnCreate(ref SystemState state)
            {
                createdFlag = 16;
            }

            public void OnDestroy(ref SystemState state)
            {
                *Ptr |= 1 | createdFlag;
            }

            public void OnStartRunning(ref SystemState state)
            {
                *Ptr |= 2;
            }

            public void OnStopRunning(ref SystemState state)
            {
                *Ptr |= 4;
            }

            public void OnUpdate(ref SystemState state)
            {
                *Ptr |= 8;
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
        public void UnmanagedSystemLifetime()
        {
            SystemHandle<MyUnmanagedSystem2> sysHandle = default;
            Assert.Throws<InvalidOperationException>(() => World.Unmanaged.ResolveSystem(sysHandle));

            using (var world = new World("Temp"))
            {
                Assert.Throws<InvalidOperationException>(() => World.Unmanaged.ResolveSystem(sysHandle));
                var sys = world.AddSystem<MyUnmanagedSystem2>();
                sysHandle = sys.Handle;
                Assert.IsTrue(world.Unmanaged.IsSystemValid(sysHandle));
                Assert.IsFalse(World.Unmanaged.IsSystemValid(sysHandle));
                ref var sys2 = ref world.Unmanaged.ResolveSystem(sysHandle);
            }

            Assert.IsFalse(World.Unmanaged.IsSystemValid(sysHandle));
            Assert.Throws<InvalidOperationException>(() => World.Unmanaged.ResolveSystem(sysHandle));
        }

        [Test]
        public void UnmanagedSystemLookup()
        {
            var s1 = World.AddSystem<MyUnmanagedSystem2>();
            var s2 = World.AddSystem<MyUnmanagedSystem2>();

            s1.Struct.Dummy = 19;
            s2.Struct.Dummy = -19;

            // We don't know which one we'll get currently, but the point is there will be two.
            Assert.AreEqual(19, Math.Abs(World.GetExistingSystem<MyUnmanagedSystem2>().Struct.Dummy));

            World.DestroyUnmanagedSystem(s1.Handle);

            Assert.AreEqual(19, Math.Abs(World.GetExistingSystem<MyUnmanagedSystem2>().Struct.Dummy));

            World.DestroyUnmanagedSystem(s2.Handle);

            Assert.Throws<InvalidOperationException>(() => World.GetExistingSystem<MyUnmanagedSystem2>());
        }

        [Test]
        public unsafe void UnmanagedSystemStartStop()
        {
            int bits = 0;

            using (var w = new World("Fisk"))
            {
                var group = w.GetOrCreateSystem<SimulationSystemGroup>();

                var s1 = w.AddSystem<MyUnmanagedSystemWithStartStop>();
                s1.Struct.Ptr = &bits;

                group.AddSystemToUpdateList(s1.Handle);
                w.Update();

                w.DestroyUnmanagedSystem(s1.Handle);
                w.Update();
            }

            Assert.AreEqual(31, bits);
        }

        [Test]
        public unsafe void RegistryCallManagedToManaged()
        {
            var sysRef = World.AddSystem<MyUnmanagedSystem2>();
            var statePtr = World.Unmanaged.ResolveSystemState(sysRef.Handle);
            SystemBaseRegistry.CallOnUpdate(statePtr);
            ref var sys = ref World.Unmanaged.ResolveSystem(sysRef.Handle);
            Assert.AreEqual(1, sys.UpdateCount);
        }

        [Test]
        public unsafe void RegistryCallManagedToBurst()
        {
            var sysId = World.AddSystem<MyUnmanagedSystem2WithBurst>();
            var statePtr = World.Unmanaged.ResolveSystemState(sysId.Handle);
            SystemBaseRegistry.CallOnUpdate(statePtr);
            ref var sys = ref World.Unmanaged.ResolveSystem(sysId.Handle);
            Assert.AreEqual(1, sys.UpdateCount);
        }

        internal unsafe delegate void DispatchDelegate(IntPtr state);

        [BurstCompile(CompileSynchronously = true)]
#if UNITY_DOTSRUNTIME
        // We intend to burst compile this function, so DOTS Runtime currently requires
        // these functions to have MonoPInvokeCallback attribute added to them explicitly
        [Jobs.MonoPInvokeCallback]
#endif
        private unsafe static void DispatchUpdate(IntPtr state)
        {
            SystemBase.UnmanagedUpdate(state, out _);
        }

        [Test]
        public unsafe void RegistryCallBurstToManaged()
        {
            var sysRef = World.AddSystem<MyUnmanagedSystem2>();
            var statePtr = (IntPtr) World.Unmanaged.ResolveSystemState(sysRef.Handle);
            BurstCompiler.CompileFunctionPointer<DispatchDelegate>(DispatchUpdate).Invoke(statePtr);
            ref var sys = ref World.Unmanaged.ResolveSystem(sysRef.Handle);
            Assert.AreEqual(1, sys.UpdateCount);
        }

        [Test]
        public unsafe void RegistryCallBurstToBurst()
        {
            var sysRef = World.AddSystem<MyUnmanagedSystem2WithBurst>();
            var statePtr = (IntPtr) World.Unmanaged.ResolveSystemState(sysRef.Handle);
            BurstCompiler.CompileFunctionPointer<DispatchDelegate>(DispatchUpdate).Invoke(statePtr);
            ref var sys = ref World.Unmanaged.ResolveSystem(sysRef.Handle);
            Assert.AreEqual(1, sys.UpdateCount);
        }

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
