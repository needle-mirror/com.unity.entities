using System;
using NUnit.Framework;
using Unity.Burst;

namespace Unity.Entities.Tests
{
    [BurstCompile]
    public class EntityManagerUnmanagedTests : ECSTestsFixture
    {
        private partial struct MyUnmanagedSystem2 : ISystem
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

        private unsafe partial struct MyUnmanagedSystemWithStartStop : ISystem, ISystemStartStop
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
        private partial struct MyUnmanagedSystem2WithBurst : ISystem
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
        private struct SnoopSystem : ISystem, ISystemStartStop
        {
            [Flags]
            public enum CallFlags
            {
                None = 0,
                OnCreate = 1 << 0,
                OnUpdate = 1 << 1,
                OnDestroy = 1 << 2,
                OnStartRunning = 1 << 3,
                OnStopRunning = 1 << 4,
            }
            internal static CallFlags Flags = CallFlags.None;

            public void OnCreate(ref SystemState state)
            {
                Flags |= CallFlags.OnCreate;
            }

            public void OnUpdate(ref SystemState state)
            {
                Flags |= CallFlags.OnUpdate;
            }

            public void OnDestroy(ref SystemState state)
            {
                Flags |= CallFlags.OnDestroy;
            }

            public void OnStartRunning(ref SystemState state)
            {
                Flags |= CallFlags.OnStartRunning;
            }

            public void OnStopRunning(ref SystemState state)
            {
                Flags |= CallFlags.OnStopRunning;
            }

            public static void AssertCallsWereMade(CallFlags flags)
            {
                if (flags != (Flags & flags))
                {
                    Assert.Fail($"Expected {flags} but have {Flags & flags}");
                }
            }

            public static void AssertCallsWereNotMade(CallFlags flags)
            {
                if (0 != (Flags & flags))
                {
                    Assert.Fail($"Expected nothing, but have {Flags & flags}");
                }
            }
        }

        [Test]
        public void UnmanagedSystemUpdate()
        {
            SnoopSystem.Flags = 0;

            using (var world = new World("Temp"))
            {
                var group = world.GetOrCreateSystem<SimulationSystemGroup>();

                var sysRef = world.AddSystem<SnoopSystem>();

                @group.AddSystemToUpdateList(sysRef.Handle);

                SnoopSystem.AssertCallsWereMade(SnoopSystem.CallFlags.OnCreate);

                world.Update();

                SnoopSystem.AssertCallsWereMade(SnoopSystem.CallFlags.OnUpdate | SnoopSystem.CallFlags.OnStartRunning);
            }

            SnoopSystem.AssertCallsWereMade(SnoopSystem.CallFlags.OnDestroy | SnoopSystem.CallFlags.OnStopRunning);
        }

        [Test]
        public unsafe void UnmanagedSystemNoDuplicateStopSystem()
        {
            SnoopSystem.Flags = 0;

            using (var world = new World("Temp"))
            {
                var group = world.GetOrCreateSystem<SimulationSystemGroup>();

                var sysRef = world.AddSystem<SnoopSystem>();

                @group.AddSystemToUpdateList(sysRef.Handle);

                world.Update();

                SnoopSystem.AssertCallsWereMade(SnoopSystem.CallFlags.OnUpdate);

                world.Unmanaged.ResolveSystemState(sysRef.Handle)->Enabled = false;

                SnoopSystem.Flags = SnoopSystem.CallFlags.None;
                world.Update();

                SnoopSystem.AssertCallsWereNotMade(SnoopSystem.CallFlags.OnUpdate);
                SnoopSystem.AssertCallsWereMade(SnoopSystem.CallFlags.OnStopRunning);

                SnoopSystem.Flags = SnoopSystem.CallFlags.None;
            }

            SnoopSystem.AssertCallsWereMade(SnoopSystem.CallFlags.OnDestroy);
            SnoopSystem.AssertCallsWereNotMade(SnoopSystem.CallFlags.OnStopRunning);
        }
    }
}
