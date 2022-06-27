using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Scripting;
using Unity.Burst;
using System.Collections.Generic;

namespace Unity.Entities.Tests
{
    partial class ComponentSystemTests : ECSTestsFixture
    {
        class TestGroup : ComponentSystemGroup
        {
        }

        class TestSystem : ComponentSystem
        {
            public bool Created = false;

            protected override void OnUpdate()
            {
            }

            protected override void OnCreate()
            {
                Created = true;
            }

            protected override void OnDestroy()
            {
                Created = false;
            }
        }

        class DerivedTestSystem : TestSystem
        {
            protected override void OnUpdate()
            {
            }
        }

        class ThrowExceptionSystem : TestSystem
        {
            protected override void OnCreate()
            {
                throw new System.Exception();
            }

            protected override void OnUpdate()
            {
            }
        }

        partial class ScheduleJobAndDestroyArray : SystemBase
        {
            NativeArray<int> test = new NativeArray<int>(10, Allocator.Persistent);

            new struct Job : IJob
            {
                public NativeArray<int> test;

                public void Execute() { }
            }

            protected override void OnUpdate()
            {
                Dependency = new Job() { test = test }.Schedule(Dependency);
            }

            protected override void OnDestroy()
            {
                // We expect this to not throw an exception since the jobs scheduled
                // by this system should be synced before the system is destroyed
                test.Dispose();
            }
        }

        [Test]
        public void Create()
        {
            var system = World.CreateSystem<TestSystem>();
            Assert.AreEqual(system, World.GetExistingSystem<TestSystem>());
            Assert.IsTrue(system.Created);
        }

#if !UNITY_PORTABLE_TEST_RUNNER
        // TODO: IL2CPP_TEST_RUNNER can't handle Assert.That Throws
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [Test]
        public void ComponentSystem_CheckExistsAfterDestroy_CorrectMessage()
        {
            var destroyedSystem = World.CreateSystem<TestSystem>();
            World.DestroySystem(destroyedSystem);
            Assert.That(() => { destroyedSystem.ShouldRunSystem(); },
                Throws.InvalidOperationException.With.Message.Contains("destroyed"));
        }

#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [Test]
        public void ComponentSystem_CheckExistsBeforeCreate_CorrectMessage()
        {
            var incompleteSystem = new TestSystem();
            Assert.That(() => { incompleteSystem.ShouldRunSystem(); },
                Throws.InvalidOperationException.With.Message.Contains("initialized"));
        }

#endif
#endif

        [Test]
        public void CreateAndDestroy()
        {
            var system = World.CreateSystem<TestSystem>();
            World.DestroySystem(system);
            Assert.AreEqual(null, World.GetExistingSystem<TestSystem>());
            Assert.IsFalse(system.Created);
        }

        [Test]
        public void GetOrCreateSystemReturnsSameSystem()
        {
            var system = World.GetOrCreateSystem<TestSystem>();
            Assert.AreEqual(system, World.GetOrCreateSystem<TestSystem>());
        }

        [Test]
        public void InheritedSystem()
        {
            var system = World.CreateSystem<DerivedTestSystem>();
            Assert.AreEqual(system, World.GetExistingSystem<DerivedTestSystem>());
            Assert.AreEqual(system, World.GetExistingSystem<TestSystem>());

            World.DestroySystem(system);

            Assert.AreEqual(null, World.GetExistingSystem<DerivedTestSystem>());
            Assert.AreEqual(null, World.GetExistingSystem<TestSystem>());

            Assert.IsFalse(system.Created);
        }

#if !UNITY_DOTSRUNTIME
        [Test]
        public void CreateNonSystemThrows()
        {
            Assert.Throws<ArgumentException>(() => { World.CreateSystem(typeof(Entity)); });
        }

#endif

        [Test]
        public void GetOrCreateNonSystemThrows()
        {
            Assert.Throws<ArgumentException>(() => { World.GetOrCreateSystem(typeof(Entity)); });
        }

        [Test]
        public void OnCreateThrowRemovesSystem()
        {
            Assert.Throws<Exception>(() => { World.CreateSystem<ThrowExceptionSystem>(); });
            Assert.AreEqual(null, World.GetExistingSystem<ThrowExceptionSystem>());
        }

        [Test]
        public void DestroySystemWhileJobUsingArrayIsRunningWorks()
        {
            var system = World.CreateSystem<ScheduleJobAndDestroyArray>();
            system.Update();
            World.DestroySystem(system);
        }

        [Test]
        public void DisposeSystemEntityQueryThrows()
        {
            var system = World.CreateSystem<EmptySystem>();
            var group = system.GetEntityQuery(typeof(EcsTestData));
            Assert.Throws<InvalidOperationException>(() => group.Dispose());
        }

        [Test]
        public void DestroySystemTwiceThrows()
        {
            var system = World.CreateSystem<TestSystem>();
            World.DestroySystem(system);
            Assert.Throws<ArgumentException>(() => World.DestroySystem(system));
        }

        [Test]
        public void CreateTwoSystemsOfSameType()
        {
            var systemA = World.CreateSystem<TestSystem>();
            var systemB = World.CreateSystem<TestSystem>();
            // CreateSystem makes a new system
            Assert.AreNotEqual(systemA, systemB);
            // Return first system
            Assert.AreEqual(systemA, World.GetOrCreateSystem<TestSystem>());
        }

        [Test]
        public void CreateTwoSystemsAfterDestroyReturnSecond()
        {
            var systemA = World.CreateSystem<TestSystem>();
            var systemB = World.CreateSystem<TestSystem>();
            World.DestroySystem(systemA);

            Assert.AreEqual(systemB, World.GetExistingSystem<TestSystem>());
        }

        [Test]
        public void CreateTwoSystemsAfterDestroyReturnFirst()
        {
            var systemA = World.CreateSystem<TestSystem>();
            var systemB = World.CreateSystem<TestSystem>();
            World.DestroySystem(systemB);

            Assert.AreEqual(systemA, World.GetExistingSystem<TestSystem>());
        }

        [Test]
        public void GetEntityQuery()
        {
            ComponentType[] ro_rw = { ComponentType.ReadOnly<EcsTestData>(), typeof(EcsTestData2) };
            ComponentType[] rw_rw = { typeof(EcsTestData), typeof(EcsTestData2) };
            ComponentType[] rw = { typeof(EcsTestData) };

            var ro_rw0_system = EmptySystem.GetEntityQuery(ro_rw);
            var rw_rw_system = EmptySystem.GetEntityQuery(rw_rw);
            var rw_system = EmptySystem.GetEntityQuery(rw);

            Assert.AreEqual(ro_rw0_system, EmptySystem.GetEntityQuery(ro_rw));
            Assert.AreEqual(rw_rw_system, EmptySystem.GetEntityQuery(rw_rw));
            Assert.AreEqual(rw_system, EmptySystem.GetEntityQuery(rw));

            Assert.AreEqual(3, EmptySystem.EntityQueries.Length);
        }

        [Test]
        public void GetEntityQuery_ArchetypeQuery()
        {
            var query1 = new ComponentType[] { typeof(EcsTestData) };
            var query2 = new EntityQueryDesc { All = new ComponentType[] { typeof(EcsTestData) } };
            var query3 = new EntityQueryDesc { All = new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) } };

            var group1 = EmptySystem.GetEntityQuery(query1);
            var group2 = EmptySystem.GetEntityQuery(query2);
            var group3 = EmptySystem.GetEntityQuery(query3);

            Assert.AreEqual(group1, EmptySystem.GetEntityQuery(query1));
            Assert.AreEqual(group2, EmptySystem.GetEntityQuery(query2));
            Assert.AreEqual(group3, EmptySystem.GetEntityQuery(query3));

            Assert.AreEqual(2, EmptySystem.EntityQueries.Length);
        }

        [Test]
        public void GetEntityQuery_ComponentTypeArchetypeQueryEquality()
        {
            var query1 = new ComponentType[] { typeof(EcsTestData) };
            var query2 = new EntityQueryDesc { All = new ComponentType[] { typeof(EcsTestData) } };
            var query3 = new EntityQueryDesc { All = new[] { ComponentType.ReadWrite<EcsTestData>() } };

            var group1 = EmptySystem.GetEntityQuery(query1);
            var group2 = EmptySystem.GetEntityQuery(query2);
            var group3 = EmptySystem.GetEntityQuery(query3);

            Assert.AreEqual(group1, group2);
            Assert.AreEqual(group2, group3);
            Assert.AreEqual(1, EmptySystem.EntityQueries.Length);
        }

        [Test]
        public void GetEntityQuery_RespectsRWAccessInequality()
        {
            var query1 = new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<EcsTestData>(), ComponentType.ReadWrite<EcsTestData2>() } };
            var query2 = new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<EcsTestData>(), ComponentType.ReadOnly<EcsTestData2>() } };

            var group1 = EmptySystem.GetEntityQuery(query1);
            var group2 = EmptySystem.GetEntityQuery(query2);

            Assert.AreNotEqual(group1, group2);
            Assert.AreEqual(2, EmptySystem.EntityQueries.Length);
        }

        [Test]
        public void GetEntityQuery_OrderIndependent()
        {
            var query1 = new ComponentType[] { typeof(EcsTestData), typeof(EcsTestData2) };
            var query2 = new ComponentType[] { typeof(EcsTestData2), typeof(EcsTestData) };

            var group1 = EmptySystem.GetEntityQuery(query1);
            var group2 = EmptySystem.GetEntityQuery(query2);

            Assert.AreEqual(group1, group2);
            Assert.AreEqual(1, EmptySystem.EntityQueries.Length);

            var query3 = new EntityQueryDesc { All = new ComponentType[] { typeof(EcsTestData2), typeof(EcsTestData3) } };
            var query4 = new EntityQueryDesc { All = new ComponentType[] { typeof(EcsTestData3), typeof(EcsTestData2) } };

            var group3 = EmptySystem.GetEntityQuery(query3);
            var group4 = EmptySystem.GetEntityQuery(query4);

            Assert.AreEqual(group3, group4);
            Assert.AreEqual(2, EmptySystem.EntityQueries.Length);
        }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        //@TODO: Behaviour is a slightly dodgy... Should probably just ignore and return same as single typeof(EcsTestData)
        [Test]
        public void GetEntityQuery_WithEntityThrows()
        {
            ComponentType[] e = { typeof(Entity), typeof(EcsTestData) };
            Assert.Throws<ArgumentException>(() => EmptySystem.GetEntityQuery(e));
        }
#endif

        [Test]
        public void GetEntityQuery_WithDuplicates()
        {
            // Currently duplicates will create two seperate groups doing the same thing...
            ComponentType[] dup_1 = { typeof(EcsTestData2) };
            ComponentType[] dup_2 = { typeof(EcsTestData2), typeof(EcsTestData3) };

            var dup1_system = EmptySystem.GetEntityQuery(dup_1);
            var dup2_system = EmptySystem.GetEntityQuery(dup_2);

            Assert.AreEqual(dup1_system, EmptySystem.GetEntityQuery(dup_1));
            Assert.AreEqual(dup2_system, EmptySystem.GetEntityQuery(dup_2));

            Assert.AreEqual(2, EmptySystem.EntityQueries.Length);
        }

        [Test]
        public void UpdateDestroyedSystemThrows()
        {
            var system = EmptySystem;
            World.DestroySystem(system);
            Assert.Throws<InvalidOperationException>(system.Update);
        }

#if !UNITY_DOTSRUNTIME // DOTSR doesn't support GetCustomAttributes()
        [DisableAutoCreation]
        class ParentWithDisableAutoCreation
        {
        }
        class ChildWithoutDisableAutoCreation : ParentWithDisableAutoCreation
        {
        }

        [Test]
        public void DisableAutoCreation_DoesNotInherit()
        {
            Type parentType = typeof(ParentWithDisableAutoCreation);
            Type childType = typeof(ChildWithoutDisableAutoCreation);
            // Parent has the DisableAutoCreation attribute
            var parentAttributes = parentType.GetCustomAttributes(false);
            Assert.AreEqual(1, parentAttributes.Length);
            Assert.AreEqual(typeof(DisableAutoCreationAttribute), parentAttributes[0].GetType());
            // Child does not inherit the attribute, even if inherit=true is passed
            var childAttributes = childType.GetCustomAttributes(true);
            Assert.AreEqual(0, childAttributes.Length);
        }

        [Test]
        public void ComponentDataFromEntity_TryGetComponent_Works()
        {
            var entityA = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entityA, new EcsTestData
            {
                value = 0
            });
            var entityB = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entityB, new EcsTestData
            {
                value = 1
            });
            var entityC = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.SetComponentData(entityC, new EcsTestData
            {
                value = 2
            });

            var array = m_Manager.GetComponentDataFromEntity<EcsTestData>();

            Assert.IsTrue(array.TryGetComponent(entityA, out var componentDataA));
            Assert.IsTrue(array.TryGetComponent(entityB, out var componentDataB));
            Assert.IsTrue(array.TryGetComponent(entityC, out var componentDataC));

            Assert.AreEqual(0, componentDataA.value);
            Assert.AreEqual(1, componentDataB.value);
            Assert.AreEqual(2, componentDataC.value);

        }

        [Test]
        public void ComponentDataFromEntity_TryGetComponent_NoComponent()
        {
            var entity = m_Manager.CreateEntity();
            var array = m_Manager.GetComponentDataFromEntity<EcsTestData>();
            Assert.IsFalse(array.TryGetComponent(entity, out var componentData));
            Assert.AreEqual(componentData, default(EcsTestData));
        }

        [Test]
        public void ComponentDataFromEntity_TryGetComponent_FullyUpdatesLookupCache()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsTestData));
            var archetypeX = m_Manager.CreateArchetype(typeof(EcsTestTag));

            var entityA = m_Manager.CreateEntity(archetypeA);
            m_Manager.SetComponentData(entityA, new EcsTestData(17));
            var entityX = m_Manager.CreateEntity(archetypeX);

            var lookup = m_Manager.GetComponentDataFromEntity<EcsTestData>();

            // For a while, TryGetComponent left the cdfe.LookupCache in an inconsistent state. We can't inspect the
            // (private) LookupCache directly, so instead we'll test the observable effect of an stale cache: a particular
            // sequence of calls that results in invalid data being returned (possibly a crash)

            // the get[] accessor fully updates the LookupCache, and returns correct data.
            EcsTestData data = lookup[entityA];
            Assert.AreEqual(17, data.value);
            // A failed TryGetComponent() will succeed, only (before the fix) only updates the cache's IndexInArchetype,
            // setting it to -1; the other cache fields are untouched.
            Assert.IsFalse(lookup.TryGetComponent(entityX, out data));
            // The set[] accessor will *NOT* update the LookupCache, because cache.Archetype still matches.
            // Before the fix, this will pass IndexInArchetype=-1 to SetChangeVersion(), which asserts / stomps unrelated memory.
            Assert.DoesNotThrow(() => { lookup[entityA] = new EcsTestData(23); });
        }

        [Test]
        public void BufferFromEntity_TryGetBuffer_Works()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddBuffer<EcsIntElement>(entity);
            m_Manager.GetBuffer<EcsIntElement>(entity).AddRange(new NativeArray<EcsIntElement>(new EcsIntElement[] { 0, 1, 2 }, Allocator.Temp));

            var array = m_Manager.GetBufferFromEntity<EcsIntElement>();

            Assert.IsTrue(array.TryGetBuffer(entity, out var bufferData));
            CollectionAssert.AreEqual(new EcsIntElement[] { 0, 1, 2 }, bufferData.ToNativeArray(Allocator.Temp).ToArray());
        }

        [Test]
        public void BufferFromEntity_TryGetBuffer_NoComponent()
        {
            var entity = m_Manager.CreateEntity();
            var array = m_Manager.GetBufferFromEntity<EcsIntElement>();
            Assert.IsFalse(array.TryGetBuffer(entity, out var bufferData));
            //I can't do an equivalence check to default since equals appears to not be implemented
            Assert.IsFalse(bufferData.IsCreated);
        }

        [Test]
        public void BufferFromEntity_TryGetBuffer_FullyUpdatesLookupCache()
        {
            var archetypeA = m_Manager.CreateArchetype(typeof(EcsIntElement));
            var archetypeX = m_Manager.CreateArchetype(typeof(EcsTestTag));

            var entityA = m_Manager.CreateEntity(archetypeA);
            var buffer = m_Manager.GetBuffer<EcsIntElement>(entityA);
            buffer.Add(new EcsIntElement{Value = 17});
            var entityX = m_Manager.CreateEntity(archetypeX);

            var lookup = m_Manager.GetBufferFromEntity<EcsIntElement>();

            // For a while, TryGetComponent left the cdfe.LookupCache in an inconsistent state. We can't inspect the
            // (private) LookupCache directly, so instead we'll test the observable effect of an stale cache: a particular
            // sequence of calls that results in invalid data being returned (possibly a crash)

            // the get[] accessor fully updates the LookupCache, and returns correct data.
            buffer = lookup[entityA];
            Assert.AreEqual(1, buffer.Length);
            Assert.AreEqual(17, buffer[0].Value);
            // A failed TryGetBuffer() will succeed, only (before the fix) only updates the cache's IndexInArchetype,
            // setting it to -1; the other cache fields are untouched.
            Assert.IsFalse(lookup.TryGetBuffer(entityX, out buffer));
            // The get[] accessor for a read/write BFE will *NOT* update the LookupCache, because cache.Archetype still matches.
            // Before the fix, this will pass IndexInArchetype=-1 to SetChangeVersion(), which asserts / stomps unrelated memory.
            Assert.DoesNotThrow(() => { buffer = lookup[entityA]; });
        }
#endif

#if UNITY_ENTITIES_RUNTIME_TOOLING
        partial class SystemThatTakesTime : SystemBase
        {
            private int updateCount = 0;
            protected override void OnUpdate()
            {
                var start = Stopwatch.StartNew();

                updateCount++;
                long howlongtowait = updateCount * 2;
                while (start.ElapsedMilliseconds < howlongtowait)
                    ;
            }
        }

        [Test]
        public void RuntimeToolingSystemTiming()
        {
            var s1 = World.CreateSystem<SystemThatTakesTime>();

            s1.Update();
            Assert.Greater(s1.SystemElapsedTicks, 0);
            Assert.Greater(s1.SystemStartTicks, 0);
            Assert.Greater(s1.SystemEndTicks, s1.SystemStartTicks);
            Assert.GreaterOrEqual(s1.SystemElapsedMilliseconds, 2);

            s1.Update();
            Assert.GreaterOrEqual(s1.SystemElapsedMilliseconds, 4);

            s1.Enabled = false;
            // check that the time still represents the last time it updated, even if
            // we disabled it in the meantime
            Assert.Greater(s1.SystemElapsedTicks, 0);
            Assert.Greater(s1.SystemStartTicks, 0);
            Assert.Greater(s1.SystemEndTicks, s1.SystemStartTicks);
            Assert.GreaterOrEqual(s1.SystemElapsedMilliseconds, 4);

            s1.Update();
            Assert.AreEqual(0, s1.SystemElapsedTicks);
        }
#endif

#if !UNITY_DOTSRUNTIME

        public class NonPreservedTestSystem : ComponentSystem
        {
            public string m_Test;

            public NonPreservedTestSystem() { m_Test = ""; }

            //This is essentially what removing [Preserve] would accomplish with max code stripping.
            //public NonPreservedTestSystem(string inputParam) { m_Test = inputParam; }
            protected override void OnUpdate() { }
        }

        [Preserve]
        public class PreservedTestSystem : ComponentSystem
        {
            public string m_Test;

            public PreservedTestSystem() { m_Test = ""; }
            public PreservedTestSystem(string inputParam) { m_Test = inputParam; }
            protected override void OnUpdate() { }
        }
#endif

        partial struct UnmanagedSystemWithSyncPointAfterSchedule : ISystem
        {
            struct MyJob : IJobChunk
            {
                public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
                {
                }
            }

            private EntityQuery m_Query;

            public void OnCreate(ref SystemState state)
            {
                state.EntityManager.CreateEntity(typeof(EcsTestData));
                m_Query = state.GetEntityQuery(typeof(EcsTestData));
            }

            public void OnDestroy(ref SystemState state)
            {
            }

            public void OnUpdate(ref SystemState state)
            {
                state.GetComponentTypeHandle<EcsTestData>();
                state.Dependency = new MyJob().ScheduleParallel(m_Query, state.Dependency);
                state.EntityManager.CreateEntity();
            }
        }

        [Test]
        public void ISystem_CanHaveSyncPointAfterSchedule()
        {
            var group = World.CreateSystem<TestGroup>();
            var sys = World.AddSystem<UnmanagedSystemWithSyncPointAfterSchedule>();
            group.AddSystemToUpdateList(sys.Handle);
            Assert.DoesNotThrow(() => group.Update());
        }

        partial class UpdateCountSystem : SystemBase
        {
            public int UpdateCount = 0;
            protected override void OnUpdate()
            {
                Entities.ForEach((ref EcsTestData data) => { }).Run();
                ++UpdateCount;
            }
        }
        partial class WithoutAlwaysUpdateSystem : UpdateCountSystem
        {
        }
        [AlwaysUpdateSystem]
        partial class WithAlwaysUpdateSystem : UpdateCountSystem
        {
        }

        [Test]
        public void SystemBase_AlwaysUpdateSystem_Works()
        {
            var sys1 = World.CreateSystem<WithoutAlwaysUpdateSystem>();
            sys1.Update();
            Assert.AreEqual(0, sys1.UpdateCount);

            var sys2 = World.CreateSystem<WithAlwaysUpdateSystem>();
            sys2.Update();
            Assert.AreEqual(1, sys2.UpdateCount);
        }

        partial struct WithoutAlwaysUpdateSystemUnmanaged : ISystem
        {
            public int UpdateCount;
            public void OnCreate(ref SystemState state) { }
            public void OnDestroy(ref SystemState state) { }
            public void OnUpdate(ref SystemState state)
            {
                state.Entities.ForEach((ref EcsTestData data) => { }).Run();
                ++UpdateCount;
            }
        }
        [AlwaysUpdateSystem]
        partial struct WithAlwaysUpdateSystemUnmanaged : ISystem
        {
            public int UpdateCount;
            public void OnCreate(ref SystemState state) { }
            public void OnDestroy(ref SystemState state) { }
            public void OnUpdate(ref SystemState state)
            {
                state.Entities.ForEach((ref EcsTestData data) => { }).Run();
                ++UpdateCount;
            }
        }

        [Test]
        public void ISystem_AlwaysUpdateSystem_Works()
        {
            var group = World.CreateSystem<TestGroup>();
            var sys1 = World.AddSystem<WithoutAlwaysUpdateSystemUnmanaged>();
            var sys2 = World.AddSystem<WithAlwaysUpdateSystemUnmanaged>();

            //m_Manager.CreateEntity(typeof(EcsTestData));
            group.AddSystemToUpdateList(sys1.Handle);
            group.AddSystemToUpdateList(sys2.Handle);
            group.Update();
            Assert.AreEqual(0, sys1.Struct.UpdateCount);
            Assert.AreEqual(1, sys2.Struct.UpdateCount);
        }

        [WorldSystemFilter((WorldSystemFilterFlags)(1 << 20))]   // unused filter flag
        partial struct WorldSystemFilterISystem : ISystem
        {
            public void OnCreate(ref SystemState state) { }
            public void OnDestroy(ref SystemState state) { }
            public void OnUpdate(ref SystemState state) { }
        }

        [WorldSystemFilter((WorldSystemFilterFlags)(1 << 20))]   // unused filter flag
        partial class WorldSystemFilterSystem : SystemBase
        {
            protected override void OnUpdate() { }
        }

        [Test]
        public void ISystem_WorldSystemFiltering_Exists()
        {
            Assert.IsTrue(TypeManager.GetSystemFilterFlags(typeof(WorldSystemFilterISystem)) == (WorldSystemFilterFlags)(1 << 20));
        }


        [Test]
        public void SystemBase_WorldSystemFiltering_Exists()
        {
            Assert.IsTrue(TypeManager.GetSystemFilterFlags(typeof(WorldSystemFilterSystem)) == (WorldSystemFilterFlags)(1 << 20));
        }

#if !UNITY_DOTSRUNTIME
        /*
          Fails with Burst compile errors on DOTS RT use of try/catch in JobChunkExtensions.cs
          Once we have a shared job system between Big Unity and DOTS RT, we should re-evaluate.
        */
        [BurstCompile]
        partial struct BurstCompiledUnmanagedSystem : ISystem
        {
            [BurstCompile]
            struct MyJob : IJobChunk
            {
                public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
                {
                }
            }

            private EntityQuery m_Query;

            [BurstCompile]
            public void OnCreate(ref SystemState state)
            {
                var myTypes = new NativeArray<ComponentType>(1, Allocator.Temp);
                myTypes[0] = ComponentType.ReadWrite<EcsTestData>();
                var arch = state.EntityManager.CreateArchetype(myTypes);

                state.EntityManager.CreateEntity(arch);
                m_Query = state.GetEntityQuery(myTypes);

                myTypes.Dispose();
            }

            [BurstCompile]
            public void OnDestroy(ref SystemState state)
            {
            }

            [BurstCompile]
            public void OnUpdate(ref SystemState state)
            {
                state.GetComponentTypeHandle<EcsTestData>();
                state.Dependency = new MyJob().ScheduleParallel(m_Query, state.Dependency);
                state.EntityManager.CreateEntity();
            }
        }
#endif

#if !UNITY_DOTSRUNTIME  // Reflection required
        unsafe partial struct UnmanagedSystemWithRefA : ISystem
        {
            public SystemHandle<UnmanagedSystemWithRefB> other;

            public void OnCreate(ref SystemState state)
            {
                other = state.WorldUnmanaged.GetExistingUnmanagedSystem<UnmanagedSystemWithRefB>().Handle;
            }

            public void OnDestroy(ref SystemState state)
            {
            }

            public void OnUpdate(ref SystemState state)
            {
            }
        }

        unsafe partial struct UnmanagedSystemWithRefB : ISystem
        {
            public SystemHandle<UnmanagedSystemWithRefA> other;
            public void OnCreate(ref SystemState state)
            {
                other = state.WorldUnmanaged.GetExistingUnmanagedSystem<UnmanagedSystemWithRefA>().Handle;
            }

            public void OnDestroy(ref SystemState state)
            {
            }

            public void OnUpdate(ref SystemState state)
            {
            }
        }

        [Test]
        public void UnmanagedSystemRefsBatchCreateWorks()
        {
            World.Unmanaged.GetOrCreateUnmanagedSystems(World, new[] { typeof(UnmanagedSystemWithRefA), typeof(UnmanagedSystemWithRefB) });

            var sysA = World.Unmanaged.GetExistingUnmanagedSystem<UnmanagedSystemWithRefA>();
            var sysB = World.Unmanaged.GetExistingUnmanagedSystem<UnmanagedSystemWithRefB>();

            Assert.IsTrue(World.Unmanaged.IsSystemValid(sysA));
            Assert.IsTrue(World.Unmanaged.IsSystemValid(sysB));

            Assert.IsTrue(sysA.Struct.other.UntypedHandle == sysB.Handle.UntypedHandle);
            Assert.IsTrue(sysB.Struct.other.UntypedHandle == sysA.Handle.UntypedHandle);
        }
#endif
    }
}
