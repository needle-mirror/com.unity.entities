using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities.Tests
{
    public struct TestComponent : IComponentData
    {
        public int BeforeValue;
        public int AfterValue;
        public int ManagedValue;
        public int UnmanagedValue;
        public int WriteJobValue;
    }

    public struct TestReadResult : IComponentData
    {
        public int ReadResult;
    }

    public partial struct TestJobWrite : IJobEntity
    {
        public void Execute(ref TestComponent testValue)
        {
            testValue.WriteJobValue++;
        }
    }

    public unsafe struct TestJobRead : IJobChunk
    {
        [ReadOnly] public ComponentTypeHandle<TestComponent> handle;
        public ComponentTypeHandle<TestReadResult> resultHandle;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var testValue = chunk.GetComponentDataPtrRO(ref handle);
            var testResult = chunk.GetComponentDataPtrRW(ref resultHandle);
            testResult->ReadResult += 1;
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class TestSystemGroup : ComponentSystemGroup
    {

    }

    [UpdateInGroup(typeof(TestSystemGroup))]
    [DisableAutoCreation]
    public partial class ManagedTargetSystem : SystemBase
    {
        public static bool ShouldWriteOnMainThread = false;
        protected override void OnCreate() { }
        protected override void OnUpdate()
        {
            if (ShouldWriteOnMainThread)
                SystemAPI.GetSingletonRW<TestComponent>().ValueRW.ManagedValue++;
            this.Dependency = new TestJobWrite().Schedule(this.Dependency);
        }
    }

    [UpdateInGroup(typeof(TestSystemGroup))]
    [DisableAutoCreation]
    public unsafe partial struct UnmanagedTargetSystem : ISystem
    {
        public static bool ShouldWriteOnMainThread = false;
        public void OnCreate(ref SystemState state) { }
        public void OnUpdate(ref SystemState state)
        {
            if (ShouldWriteOnMainThread)
                SystemAPI.GetSingletonRW<TestComponent>().ValueRW.UnmanagedValue++;
            state.Dependency = new TestJobWrite().Schedule(state.Dependency);
        }
    }

    [BurstCompile]
    public unsafe class UpdateBeforeAfterSystemTests : ECSTestsFixture
    {
        [BurstCompile]
        [AOT.MonoPInvokeCallback(typeof(ComponentSystemGroup.SystemWrapperDelegate))]
        public static void UpdateBeforeMainThread(SystemTypeIndex targetSystem, ref SystemState state)
        {
            var q = new EntityQueryBuilder(Allocator.Temp).WithAll<TestComponent>().Build(ref state);
            q.CompleteDependency();
            var e = q.GetSingletonEntity();
            var comp = state.EntityManager.GetComponentData<TestComponent>(e);
            comp.BeforeValue++;
            state.EntityManager.SetComponentData(e, comp);
        }
        [BurstCompile]
        [AOT.MonoPInvokeCallback(typeof(ComponentSystemGroup.SystemWrapperDelegate))]
        public static void UpdateAfterMainThread(SystemTypeIndex targetSystem, ref SystemState state)
        {
            var q = new EntityQueryBuilder(Allocator.Temp).WithAll<TestComponent>().Build(ref state);
            q.CompleteDependency();
            var e = q.GetSingletonEntity();
            var comp = state.EntityManager.GetComponentData<TestComponent>(e);
            comp.AfterValue++;
            state.EntityManager.SetComponentData(e, comp);
        }

        [BurstCompile]
        [AOT.MonoPInvokeCallback(typeof(ComponentSystemGroup.SystemWrapperDelegate))]
        public static void UpdateBeforeAfterWithJobRead(SystemTypeIndex targetSystem, ref SystemState state)
        {
            var typeIndicesRead = new NativeList<TypeIndex>(1, Allocator.Temp);
            typeIndicesRead.Add(TypeManager.GetTypeIndex<TestComponent>());
            var typeIndicesWrite = new NativeList<TypeIndex>(1, Allocator.Temp);
            typeIndicesWrite.Add(TypeManager.GetTypeIndex<TestReadResult>());

            var toCompleteForJob = state.m_DependencyManager->GetDependency(typeIndicesRead.GetUnsafeReadOnlyPtr(), typeIndicesRead.Length, typeIndicesWrite.GetUnsafeReadOnlyPtr(), typeIndicesWrite.Length, false);
            var dep = new TestJobRead()
            {
                handle = state.EntityManager.GetComponentTypeHandle<TestComponent>(isReadOnly: true),
                resultHandle = state.EntityManager.GetComponentTypeHandle<TestReadResult>(isReadOnly: false)
            }.ScheduleParallel(
                new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<TestComponent>()
                    .Build(state.EntityManager),
                toCompleteForJob);
            state.m_DependencyManager->AddDependency(typeIndicesRead.GetUnsafeReadOnlyPtr(), typeIndicesRead.Length, typeIndicesWrite.GetUnsafeReadOnlyPtr(), typeIndicesWrite.Length, dep);
        }

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            ManagedTargetSystem.ShouldWriteOnMainThread = false;
            UnmanagedTargetSystem.ShouldWriteOnMainThread = false;

            var systems = new List<Type>() { typeof(SimulationSystemGroup), typeof(TestSystemGroup), typeof(UnmanagedTargetSystem), typeof(ManagedTargetSystem) };
            TypeManager.SortSystemTypesInCreationOrder(systems);
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(World, systems);
        }

        [Test]
        public void UpdateBeforeAfter_WorksWith_MainThreadDependencies()
        {
            World.GetExistingSystemManaged<TestSystemGroup>().OnUpdateBefore = BurstCompiler.CompileFunctionPointer<ComponentSystemGroup.SystemWrapperDelegate>(UpdateBeforeMainThread);
            World.GetExistingSystemManaged<TestSystemGroup>().OnUpdateAfter = BurstCompiler.CompileFunctionPointer<ComponentSystemGroup.SystemWrapperDelegate>(UpdateAfterMainThread);

            UnmanagedTargetSystem.ShouldWriteOnMainThread = true;
            ManagedTargetSystem.ShouldWriteOnMainThread = true;

            World.EntityManager.CreateEntity(typeof(TestComponent), typeof(TestReadResult));

            World.Update();

            var q = new EntityQueryBuilder(Allocator.Temp).WithAll<TestComponent>().Build(World.EntityManager);
            var e = q.GetSingletonEntity();

            void TestValue(int value)
            {
                var comp = World.EntityManager.GetComponentData<TestComponent>(e);
                Assert.AreEqual(value, comp.ManagedValue);
                Assert.AreEqual(value, comp.UnmanagedValue);
                Assert.AreEqual(value * 2, comp.BeforeValue);
                Assert.AreEqual(value * 2, comp.AfterValue);
                Assert.AreEqual(value * 2, comp.WriteJobValue);
                var resultComp = World.EntityManager.GetComponentData<TestReadResult>(e);
                Assert.AreEqual(0, resultComp.ReadResult);
            }
            TestValue(1);
            World.Update();
            TestValue(2);
        }

        [Test]
        public void UpdateBeforeAfter_WorksWith_JobDependencies()
        {
            World.EntityManager.CreateEntity(typeof(TestComponent), typeof(TestReadResult));

            World.GetExistingSystemManaged<TestSystemGroup>().OnUpdateBefore = BurstCompiler.CompileFunctionPointer<ComponentSystemGroup.SystemWrapperDelegate>(UpdateBeforeAfterWithJobRead);
            World.Update();
            var q = new EntityQueryBuilder(Allocator.Temp).WithAll<TestComponent>().Build(World.EntityManager);
            var e = q.GetSingletonEntity();
            var resultComp = World.EntityManager.GetComponentData<TestReadResult>(e);
            Assert.AreEqual(2, resultComp.ReadResult, "read job result");
            World.GetExistingSystemManaged<TestSystemGroup>().OnUpdateBefore = default;
            World.GetExistingSystemManaged<TestSystemGroup>().OnUpdateAfter = BurstCompiler.CompileFunctionPointer<ComponentSystemGroup.SystemWrapperDelegate>(UpdateBeforeAfterWithJobRead);
            World.Update();
            World.EntityManager.CompleteAllTrackedJobs();
            resultComp = World.EntityManager.GetComponentData<TestReadResult>(e);
            Assert.AreEqual(4, resultComp.ReadResult);
        }

        [BurstCompile]
        [AOT.MonoPInvokeCallback(typeof(ComponentSystemGroup.SystemWrapperDelegate))]
        public static void CheckForTargetSystem(SystemTypeIndex targetSystem, ref SystemState state)
        {
            var unmanagedTargetSystemTypeIndex = state.WorldUnmanaged.GetSystemTypeIndex(state.WorldUnmanaged.GetExistingUnmanagedSystem<UnmanagedTargetSystem>());
            Assert.IsTrue(targetSystem == unmanagedTargetSystemTypeIndex);
        }

        [Test]
        public void UpdateBeforeAfter_HasRight_TargetSystemParameter()
        {
            World.EntityManager.CreateEntity(typeof(TestComponent));

            World.GetExistingSystemManaged<TestSystemGroup>().OnUpdateBefore = BurstCompiler.CompileFunctionPointer<ComponentSystemGroup.SystemWrapperDelegate>(CheckForTargetSystem);
            World.GetExistingSystemManaged<TestSystemGroup>().OnUpdateAfter = BurstCompiler.CompileFunctionPointer<ComponentSystemGroup.SystemWrapperDelegate>(CheckForTargetSystem);

            // also tests that removing a system from a group stops calling the Before/After function pointers for it
            World.GetExistingSystemManaged<TestSystemGroup>().RemoveSystemFromUpdateList(World.GetExistingSystemManaged<ManagedTargetSystem>());

            World.Update();
            World.Update();
        }
    }
}
