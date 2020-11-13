using System;
using System.Diagnostics;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace Unity.Entities.Tests
{
    class ComponentSystemTests : ECSTestsFixture
    {
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

        class ScheduleJobAndDestroyArray : JobComponentSystem
        {
            NativeArray<int> test = new NativeArray<int>(10, Allocator.Persistent);

            new struct Job : IJob
            {
                public NativeArray<int> test;

                public void Execute() {}
            }

            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                return new Job(){ test = test }.Schedule(inputDeps);
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

            Assert.AreEqual(systemB, World.GetExistingSystem<TestSystem>());;
        }

        [Test]
        public void CreateTwoSystemsAfterDestroyReturnFirst()
        {
            var systemA = World.CreateSystem<TestSystem>();
            var systemB = World.CreateSystem<TestSystem>();
            World.DestroySystem(systemB);

            Assert.AreEqual(systemA, World.GetExistingSystem<TestSystem>());;
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
            var query2 = new EntityQueryDesc { All = new ComponentType[] {typeof(EcsTestData)} };
            var query3 = new EntityQueryDesc { All = new ComponentType[] {typeof(EcsTestData), typeof(EcsTestData2)} };

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
            var query2 = new EntityQueryDesc { All = new ComponentType[] {typeof(EcsTestData)} };
            var query3 = new EntityQueryDesc { All = new[] {ComponentType.ReadWrite<EcsTestData>()} };

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
            var query1 = new EntityQueryDesc { All = new[] {ComponentType.ReadOnly<EcsTestData>(), ComponentType.ReadWrite<EcsTestData2>()} };
            var query2 = new EntityQueryDesc { All = new[] {ComponentType.ReadOnly<EcsTestData>(), ComponentType.ReadOnly<EcsTestData2>()} };

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

            var query3 = new EntityQueryDesc { All = new ComponentType[] {typeof(EcsTestData2), typeof(EcsTestData3)} };
            var query4 = new EntityQueryDesc { All = new ComponentType[] {typeof(EcsTestData3), typeof(EcsTestData2)} };

            var group3 = EmptySystem.GetEntityQuery(query3);
            var group4 = EmptySystem.GetEntityQuery(query4);

            Assert.AreEqual(group3, group4);
            Assert.AreEqual(2, EmptySystem.EntityQueries.Length);
        }

        //@TODO: Behaviour is a slightly dodgy... Should probably just ignore and return same as single typeof(EcsTestData)
        [Test]
        public void GetEntityQuery_WithEntityThrows()
        {
            ComponentType[] e = { typeof(Entity), typeof(EcsTestData) };
            EmptySystem.GetEntityQuery(e);
            Assert.Throws<ArgumentException>(() => EmptySystem.GetEntityQuery(e));
        }

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

#if UNITY_ENTITIES_RUNTIME_TOOLING
        class SystemThatTakesTime : SystemBase
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
            protected override void OnUpdate() {}
        }

        [Preserve]
        public class PreservedTestSystem : ComponentSystem
        {
            public string m_Test;

            public PreservedTestSystem() { m_Test = ""; }
            public PreservedTestSystem(string inputParam) { m_Test = inputParam; }
            protected override void OnUpdate() {}
        }
#endif
    }
}
