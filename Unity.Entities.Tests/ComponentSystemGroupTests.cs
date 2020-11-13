using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.TestTools;

#if !UNITY_PORTABLE_TEST_RUNNER
using System.Text.RegularExpressions;
using System.Linq;
#endif

namespace Unity.Entities.Tests
{
    class ComponentSystemGroupTests : ECSTestsFixture
    {
        class TestGroup : ComponentSystemGroup
        {
        }

        private class TestSystemBase : JobComponentSystem
        {
            protected override JobHandle OnUpdate(JobHandle inputDeps) => throw new System.NotImplementedException();
        }

        public override void Setup()
        {
            base.Setup();
#if UNITY_DOTSRUNTIME
            LogAssert.ExpectReset();
#endif
        }

        [Test]
        public void SortEmptyParentSystem()
        {
            var parent = World.CreateSystem<TestGroup>();
            Assert.DoesNotThrow(() => { parent.SortSystems(); });
        }

        class TestSystem : TestSystemBase
        {
        }

        [Test]
        public void SortOneChildSystem()
        {
            var parent = World.CreateSystem<TestGroup>();

            var child = World.CreateSystem<TestSystem>();
            parent.AddSystemToUpdateList(child);
            parent.SortSystems();
            CollectionAssert.AreEqual(new[] {child}, parent.Systems);
        }

        [UpdateAfter(typeof(Sibling2System))]
        class Sibling1System : TestSystemBase
        {
        }
        class Sibling2System : TestSystemBase
        {
        }

        [Test]
        public void SortTwoChildSystems_CorrectOrder()
        {
            var parent = World.CreateSystem<TestGroup>();
            var child1 = World.CreateSystem<Sibling1System>();
            var child2 = World.CreateSystem<Sibling2System>();
            parent.AddSystemToUpdateList(child1);
            parent.AddSystemToUpdateList(child2);
            parent.SortSystems();
            CollectionAssert.AreEqual(new TestSystemBase[] {child2, child1}, parent.Systems);
        }

        // This test constructs the following system dependency graph:
        // 1 -> 2 -> 3 -> 4 -v
        //           ^------ 5 -> 6
        // The expected results of topologically sorting this graph:
        // - systems 1 and 2 are properly sorted in the system update list.
        // - systems 3, 4, and 5 form a cycle (in that order, or equivalent).
        // - system 6 is not sorted AND is not part of the cycle.
        [UpdateBefore(typeof(Circle2System))]
        class Circle1System : TestSystemBase
        {
        }
        [UpdateBefore(typeof(Circle3System))]
        class Circle2System : TestSystemBase
        {
        }
        [UpdateAfter(typeof(Circle5System))]
        class Circle3System : TestSystemBase
        {
        }
        [UpdateAfter(typeof(Circle3System))]
        class Circle4System : TestSystemBase
        {
        }
        [UpdateAfter(typeof(Circle4System))]
        class Circle5System : TestSystemBase
        {
        }
        [UpdateAfter(typeof(Circle5System))]
        class Circle6System : TestSystemBase
        {
        }

#if !UNITY_PORTABLE_TEST_RUNNER
// https://unity3d.atlassian.net/browse/DOTSR-1432

        [Test]
        public void DetectCircularDependency_Throws()
        {
            var parent = World.CreateSystem<TestGroup>();
            var child1 = World.CreateSystem<Circle1System>();
            var child2 = World.CreateSystem<Circle2System>();
            var child3 = World.CreateSystem<Circle3System>();
            var child4 = World.CreateSystem<Circle4System>();
            var child5 = World.CreateSystem<Circle5System>();
            var child6 = World.CreateSystem<Circle6System>();
            parent.AddSystemToUpdateList(child3);
            parent.AddSystemToUpdateList(child6);
            parent.AddSystemToUpdateList(child2);
            parent.AddSystemToUpdateList(child4);
            parent.AddSystemToUpdateList(child1);
            parent.AddSystemToUpdateList(child5);
            var e = Assert.Throws<ComponentSystemSorter.CircularSystemDependencyException>(() => parent.SortSystems());
            // Make sure the cycle expressed in e.Chain is the one we expect, even though it could start at any node
            // in the cycle.
            var expectedCycle = new Type[] {typeof(Circle5System), typeof(Circle3System), typeof(Circle4System)};
            var cycle = e.Chain.ToList();
            bool foundCycleMatch = false;
            for (int i = 0; i < cycle.Count; ++i)
            {
                var offsetCycle = new System.Collections.Generic.List<Type>(cycle.Count);
                offsetCycle.AddRange(cycle.GetRange(i, cycle.Count - i));
                offsetCycle.AddRange(cycle.GetRange(0, i));
                Assert.AreEqual(cycle.Count, offsetCycle.Count);
                if (expectedCycle.SequenceEqual(offsetCycle))
                {
                    foundCycleMatch = true;
                    break;
                }
            }
            Assert.IsTrue(foundCycleMatch);
        }

#endif // UNITY_DOTSRUNTIME_IL2CPP

        class Unconstrained1System : TestSystemBase
        {
        }
        class Unconstrained2System : TestSystemBase
        {
        }
        class Unconstrained3System : TestSystemBase
        {
        }
        class Unconstrained4System : TestSystemBase
        {
        }

        [Test]
        public void SortUnconstrainedSystems_IsDeterministic()
        {
            var parent = World.CreateSystem<TestGroup>();
            var child1 = World.CreateSystem<Unconstrained1System>();
            var child2 = World.CreateSystem<Unconstrained2System>();
            var child3 = World.CreateSystem<Unconstrained3System>();
            var child4 = World.CreateSystem<Unconstrained4System>();
            parent.AddSystemToUpdateList(child2);
            parent.AddSystemToUpdateList(child4);
            parent.AddSystemToUpdateList(child3);
            parent.AddSystemToUpdateList(child1);
            parent.SortSystems();
            CollectionAssert.AreEqual(parent.Systems, new TestSystemBase[] {child1, child2, child3, child4});
        }

        private class UpdateCountingSystemBase : ComponentSystem
        {
            public int CompleteUpdateCount = 0;
            protected override void OnUpdate()
            {
                ++CompleteUpdateCount;
            }
        }
        class NonThrowing1System : UpdateCountingSystemBase
        {
        }
        class NonThrowing2System : UpdateCountingSystemBase
        {
        }
        class ThrowingSystem : UpdateCountingSystemBase
        {
            public string ExceptionMessage = "I should always throw!";
            protected override void OnUpdate()
            {
                if (CompleteUpdateCount == 0)
                {
                    throw new InvalidOperationException(ExceptionMessage);
                }
                base.OnUpdate();
            }
        }

#if !UNITY_DOTSRUNTIME // DOTS Runtime does not eat the Exception so this test can not pass (the 3rd assert will always fail)
        [Test]
        public void SystemInGroupThrows_LaterSystemsRun()
        {
            var parent = World.CreateSystem<TestGroup>();
            var child1 = World.CreateSystem<NonThrowing1System>();
            var child2 = World.CreateSystem<ThrowingSystem>();
            var child3 = World.CreateSystem<NonThrowing2System>();
            parent.AddSystemToUpdateList(child1);
            parent.AddSystemToUpdateList(child2);
            parent.AddSystemToUpdateList(child3);
            LogAssert.Expect(LogType.Exception, new Regex(child2.ExceptionMessage));
            parent.Update();
            LogAssert.NoUnexpectedReceived();
            Assert.AreEqual(1, child1.CompleteUpdateCount);
            Assert.AreEqual(0, child2.CompleteUpdateCount);
            Assert.AreEqual(1, child3.CompleteUpdateCount);
        }
#endif

#if !NET_DOTS
        [Test]
        public void SystemThrows_SystemNotRemovedFromUpdate()
        {
            var parent = World.CreateSystem<TestGroup>();
            var child = World.CreateSystem<ThrowingSystem>();
            parent.AddSystemToUpdateList(child);
            LogAssert.Expect(LogType.Exception, new Regex(child.ExceptionMessage));
#if UNITY_DOTSRUNTIME
            Assert.Throws<InvalidOperationException>(() => parent.Update());
#else
            parent.Update();
#endif
            LogAssert.Expect(LogType.Exception, new Regex(child.ExceptionMessage));
#if UNITY_DOTSRUNTIME
            Assert.Throws<InvalidOperationException>(() => parent.Update());
#else
            parent.Update();
#endif
            LogAssert.NoUnexpectedReceived();

            Assert.AreEqual(0, child.CompleteUpdateCount);
        }

        [UpdateAfter(typeof(NonSibling2System))]
        class NonSibling1System : TestSystemBase
        {
        }
        [UpdateBefore(typeof(NonSibling1System))]
        class NonSibling2System : TestSystemBase
        {
        }

        [Test]
        public void ComponentSystemGroup_UpdateAfterTargetIsNotSibling_LogsWarning()
        {
            var parent = World.CreateSystem<TestGroup>();
            var child = World.CreateSystem<NonSibling1System>();
            LogAssert.Expect(LogType.Warning, new Regex(@"Ignoring invalid \[UpdateAfter\] attribute on .+NonSibling1System targeting.+NonSibling2System"));
            parent.AddSystemToUpdateList(child);
            parent.SortSystems();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ComponentSystemGroup_UpdateBeforeTargetIsNotSibling_LogsWarning()
        {
            var parent = World.CreateSystem<TestGroup>();
            var child = World.CreateSystem<NonSibling2System>();
            LogAssert.Expect(LogType.Warning, new Regex(@"Ignoring invalid \[UpdateBefore\] attribute on .+NonSibling2System targeting.+NonSibling1System"));
            parent.AddSystemToUpdateList(child);
            parent.SortSystems();
            LogAssert.NoUnexpectedReceived();
        }

        [UpdateAfter(typeof(NotEvenASystem))]
        class InvalidUpdateAfterSystem : TestSystemBase
        {
        }
        [UpdateBefore(typeof(NotEvenASystem))]
        class InvalidUpdateBeforeSystem : TestSystemBase
        {
        }
        class NotEvenASystem
        {
        }

        [Test]
        public void ComponentSystemGroup_UpdateAfterTargetIsNotSystem_LogsWarning()
        {
            var parent = World.CreateSystem<TestGroup>();
            var child = World.CreateSystem<InvalidUpdateAfterSystem>();
            LogAssert.Expect(LogType.Warning, new Regex(@"Ignoring invalid \[UpdateAfter\].+InvalidUpdateAfterSystem.+NotEvenASystem is not a subclass of ComponentSystemBase"));
            parent.AddSystemToUpdateList(child);
            parent.SortSystems();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ComponentSystemGroup_UpdateBeforeTargetIsNotSystem_LogsWarning()
        {
            var parent = World.CreateSystem<TestGroup>();
            var child = World.CreateSystem<InvalidUpdateBeforeSystem>();
            LogAssert.Expect(LogType.Warning, new Regex(@"Ignoring invalid \[UpdateBefore\].+InvalidUpdateBeforeSystem.+NotEvenASystem is not a subclass of ComponentSystemBase"));
            parent.AddSystemToUpdateList(child);
            parent.SortSystems();
            LogAssert.NoUnexpectedReceived();
        }

        [UpdateAfter(typeof(UpdateAfterSelfSystem))]
        class UpdateAfterSelfSystem : TestSystemBase
        {
        }
        [UpdateBefore(typeof(UpdateBeforeSelfSystem))]
        class UpdateBeforeSelfSystem : TestSystemBase
        {
        }

        [Test]
        public void ComponentSystemGroup_UpdateAfterTargetIsSelf_LogsWarning()
        {
            var parent = World.CreateSystem<TestGroup>();
            var child = World.CreateSystem<UpdateAfterSelfSystem>();
            LogAssert.Expect(LogType.Warning, new Regex(@"Ignoring invalid \[UpdateAfter\].+UpdateAfterSelfSystem.+cannot be updated after itself."));
            parent.AddSystemToUpdateList(child);
            parent.SortSystems();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ComponentSystemGroup_UpdateBeforeTargetIsSelf_LogsWarning()
        {
            var parent = World.CreateSystem<TestGroup>();
            var child = World.CreateSystem<UpdateBeforeSelfSystem>();
            LogAssert.Expect(LogType.Warning, new Regex(@"Ignoring invalid \[UpdateBefore\].+UpdateBeforeSelfSystem.+cannot be updated before itself."));
            parent.AddSystemToUpdateList(child);
            parent.SortSystems();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ComponentSystemGroup_AddNullToUpdateList_QuietNoOp()
        {
            var parent = World.CreateSystem<TestGroup>();
            Assert.DoesNotThrow(() => { parent.AddSystemToUpdateList(null); });
            Assert.IsEmpty(parent.Systems);
        }

        [Test]
        public void ComponentSystemGroup_AddSelfToUpdateList_Throws()
        {
            var parent = World.CreateSystem<TestGroup>();
            Assert.That(() => { parent.AddSystemToUpdateList(parent); },
                Throws.ArgumentException.With.Message.Contains("to its own update list"));
        }

#endif

        class StartAndStopSystemGroup : ComponentSystemGroup
        {
            public List<int> Operations;
            protected override void OnCreate()
            {
                base.OnCreate();
                Operations = new List<int>(6);
            }

            protected override void OnStartRunning()
            {
                Operations.Add(0);
                base.OnStartRunning();
            }

            protected override void OnUpdate()
            {
                Operations.Add(1);
                base.OnUpdate();
            }

            protected override void OnStopRunning()
            {
                Operations.Add(2);
                base.OnStopRunning();
            }
        }

        class StartAndStopSystemA : ComponentSystem
        {
            private StartAndStopSystemGroup Group;
            protected override void OnCreate()
            {
                base.OnCreate();
                Group = World.GetExistingSystem<StartAndStopSystemGroup>();
            }

            protected override void OnStartRunning()
            {
                Group.Operations.Add(10);
                base.OnStartRunning();
            }

            protected override void OnUpdate()
            {
                Group.Operations.Add(11);
            }

            protected override void OnStopRunning()
            {
                Group.Operations.Add(12);
                base.OnStopRunning();
            }
        }
        class StartAndStopSystemB : ComponentSystem
        {
            private StartAndStopSystemGroup Group;
            protected override void OnCreate()
            {
                base.OnCreate();
                Group = World.GetExistingSystem<StartAndStopSystemGroup>();
            }

            protected override void OnStartRunning()
            {
                Group.Operations.Add(20);
                base.OnStartRunning();
            }

            protected override void OnUpdate()
            {
                Group.Operations.Add(21);
            }

            protected override void OnStopRunning()
            {
                Group.Operations.Add(22);
                base.OnStopRunning();
            }
        }
        class StartAndStopSystemC : ComponentSystem
        {
            private StartAndStopSystemGroup Group;
            protected override void OnCreate()
            {
                base.OnCreate();
                Group = World.GetExistingSystem<StartAndStopSystemGroup>();
            }

            protected override void OnStartRunning()
            {
                Group.Operations.Add(30);
                base.OnStartRunning();
            }

            protected override void OnUpdate()
            {
                Group.Operations.Add(31);
            }

            protected override void OnStopRunning()
            {
                Group.Operations.Add(32);
                base.OnStopRunning();
            }
        }

        [Test]
        public void ComponentSystemGroup_OnStartRunningOnStopRunning_Recurses()
        {
            var parent = World.CreateSystem<StartAndStopSystemGroup>();
            var childA = World.CreateSystem<StartAndStopSystemA>();
            var childB = World.CreateSystem<StartAndStopSystemB>();
            var childC = World.CreateSystem<StartAndStopSystemC>();
            parent.AddSystemToUpdateList(childA);
            parent.AddSystemToUpdateList(childB);
            parent.AddSystemToUpdateList(childC);
            // child C is always disabled; make sure enabling/disabling the parent doesn't change that
            childC.Enabled = false;

            // first update
            parent.Update();
            CollectionAssert.AreEqual(parent.Operations, new[] {0, 1, 10, 11, 20, 21});
            parent.Operations.Clear();

            // second update with no new enabled/disabled
            parent.Update();
            CollectionAssert.AreEqual(parent.Operations, new[] {1, 11, 21});
            parent.Operations.Clear();

            // parent is disabled
            parent.Enabled = false;
            parent.Update();
            CollectionAssert.AreEqual(parent.Operations, new[] {2, 12, 22});
            parent.Operations.Clear();

            // parent is re-enabled
            parent.Enabled = true;
            parent.Update();
            CollectionAssert.AreEqual(parent.Operations, new[] {0, 1, 10, 11, 20, 21});
            parent.Operations.Clear();
        }

        class TrackUpdatedSystem : JobComponentSystem
        {
            public List<ComponentSystemBase> Updated;

            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                Updated.Add(this);
                return inputDeps;
            }
        }

        [Test]
        public void AddAndRemoveTakesEffectBeforeUpdate()
        {
            var parent = World.CreateSystem<TestGroup>();
            var childa = World.CreateSystem<TrackUpdatedSystem>();
            var childb = World.CreateSystem<TrackUpdatedSystem>();

            var updates = new List<ComponentSystemBase>();
            childa.Updated = updates;
            childb.Updated = updates;

            // Add 2 systems & validate Update calls
            parent.AddSystemToUpdateList(childa);
            parent.AddSystemToUpdateList(childb);
            parent.Update();

            // Order is not guaranteed
            Assert.IsTrue(updates.Count == 2 && updates.Contains(childa) && updates.Contains(childb));

            // Remove system & validate Update calls
            updates.Clear();
            parent.RemoveSystemFromUpdateList(childa);
            parent.Update();
            Assert.AreEqual(new ComponentSystemBase[] {childb}, updates.ToArray());
        }

        [UpdateInGroup(typeof(int))]
        public class GroupIsntAComponentSystem : EmptySystem
        {
        }

        [Test]
        public void UpdateInGroup_TargetNotASystem_Throws()
        {
            World w = new World("Test World");
#if NET_DOTS
            // In Tiny, the IsSystemAGroup() call will throw
            Assert.Throws<ArgumentException>(() =>
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(w, typeof(GroupIsntAComponentSystem)));
#else
            // In hybrid, IsSystemAGroup() returns false for non-system inputs
            Assert.That(() => DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(w, typeof(GroupIsntAComponentSystem)),
                Throws.InvalidOperationException.With.Message.Contains("must be derived from ComponentSystemGroup"));
#endif
            w.Dispose();
        }

        [UpdateInGroup(typeof(TestSystem))]
        public class GroupIsntAComponentSystemGroup : EmptySystem
        {
        }

        [Test]
        public void UpdateInGroup_TargetNotAGroup_Throws()
        {
            World w = new World("Test World");
#if NET_DOTS
            Assert.Throws<InvalidOperationException>(() =>
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(w,
                    typeof(GroupIsntAComponentSystemGroup)));
#else
            Assert.That(() => DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(w, typeof(GroupIsntAComponentSystemGroup)),
                Throws.InvalidOperationException.With.Message.Contains("must be derived from ComponentSystemGroup"));
#endif
            w.Dispose();
        }

        [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true, OrderLast = true)]
        public class FirstAndLast : EmptySystem
        {
        }

        [Test]
        public void UpdateInGroup_OrderFirstAndOrderLast_Throws()
        {
            World w = new World("Test World");
            var systemTypes = new[] {typeof(FirstAndLast), typeof(TestGroup)};
#if NET_DOTS
            Assert.Throws<InvalidOperationException>(() =>
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(w, systemTypes));
#else
            Assert.That(() => DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(w, systemTypes),
                Throws.InvalidOperationException.With.Message.Contains("can not specify both OrderFirst=true and OrderLast=true"));
#endif
            w.Dispose();
        }

        // All the ordering constraints below are valid (though some are redundant). All should sort correctly without warnings.
        [UpdateInGroup(typeof(TestGroup), OrderFirst = true)]
        [UpdateBefore(typeof(FirstSystem))]
        public class FirstBeforeFirstSystem : EmptySystem { }

        [UpdateInGroup(typeof(TestGroup), OrderFirst = true)]
        [UpdateBefore(typeof(MiddleSystem))] // redundant
        [UpdateBefore(typeof(LastSystem))] // redundant
        public class FirstSystem : EmptySystem { }

        [UpdateInGroup(typeof(TestGroup), OrderFirst = true)]
        [UpdateAfter(typeof(FirstSystem))]
        public class FirstAfterFirstSystem : EmptySystem { }

        [UpdateInGroup(typeof(TestGroup))]
        [UpdateAfter(typeof(FirstSystem))] // redundant
        [UpdateBefore(typeof(MiddleSystem))]
        public class MiddleAfterFirstSystem : EmptySystem { }

        [UpdateInGroup(typeof(TestGroup))]
        public class MiddleSystem : EmptySystem { }

        [UpdateInGroup(typeof(TestGroup))]
        [UpdateAfter(typeof(MiddleSystem))]
        [UpdateBefore(typeof(LastSystem))] // redundant
        public class MiddleBeforeLastSystem : EmptySystem { }

        [UpdateInGroup(typeof(TestGroup), OrderLast = true)]
        [UpdateBefore(typeof(LastSystem))]
        public class LastBeforeLastSystem : EmptySystem { }

        [UpdateInGroup(typeof(TestGroup), OrderLast = true)]
        [UpdateAfter(typeof(FirstSystem))] // redundant
        [UpdateAfter(typeof(MiddleSystem))] // redundant
        public class LastSystem : EmptySystem { }

        [UpdateInGroup(typeof(TestGroup), OrderLast = true)]
        [UpdateAfter(typeof(LastSystem))]
        public class LastAfterLastSystem : EmptySystem { }

        [Test]
        public void ComponentSystemSorter_ValidUpdateConstraints_SortCorrectlyWithNoWarnings()
        {
            var parent = World.CreateSystem<TestGroup>();
            var systems = new List<EmptySystem>
            {
                World.CreateSystem<FirstBeforeFirstSystem>(),
                World.CreateSystem<FirstSystem>(),
                World.CreateSystem<FirstAfterFirstSystem>(),
                World.CreateSystem<MiddleAfterFirstSystem>(),
                World.CreateSystem<MiddleSystem>(),
                World.CreateSystem<MiddleBeforeLastSystem>(),
                World.CreateSystem<LastBeforeLastSystem>(),
                World.CreateSystem<LastSystem>(),
                World.CreateSystem<LastAfterLastSystem>(),
            };
            // Insert in reverse order
            for (int i = systems.Count - 1; i >= 0; --i)
            {
                parent.AddSystemToUpdateList(systems[i]);
            }

            parent.SortSystems();

            CollectionAssert.AreEqual(systems, parent.Systems);
            LogAssert.NoUnexpectedReceived();
        }

#if !UNITY_DOTSRUNTIME_IL2CPP

        // Invalid constraints
        [UpdateInGroup(typeof(TestGroup), OrderFirst = true)]
        public class DummyFirstSystem : EmptySystem { }

        [UpdateInGroup(typeof(TestGroup), OrderLast = true)]
        public class DummyLastSystem : EmptySystem { }

        [UpdateInGroup(typeof(TestGroup), OrderFirst = true)]
        [UpdateAfter(typeof(DummyLastSystem))] // can't update after an OrderLast without also being OrderLast
        public class FirstAfterLastSystem : EmptySystem { }

        [UpdateInGroup(typeof(TestGroup))]
        [UpdateBefore(typeof(DummyFirstSystem))] // can't update before an OrderFirst without also being OrderFirst
        public class MiddleBeforeFirstSystem : EmptySystem { }

        [UpdateInGroup(typeof(TestGroup))]
        [UpdateAfter(typeof(DummyLastSystem))] // can't update after an OrderLast without also being OrderLast
        public class MiddleAfterLastSystem : EmptySystem { }

        [UpdateInGroup(typeof(TestGroup), OrderLast = true)]
        [UpdateBefore(typeof(DummyFirstSystem))] // can't update before an OrderFirst without also being OrderFirst
        public class LastBeforeFirstSystem : EmptySystem { }

        [Test] // runtime string formatting
        public void ComponentSystemSorter_OrderFirstUpdateAfterOrderLast_WarnAndIgnoreConstraint()
        {
            var parent = World.CreateSystem<TestGroup>();
            var systems = new List<EmptySystem>
            {
                World.CreateSystem<FirstAfterLastSystem>(),
                World.CreateSystem<DummyLastSystem>(),
            };
            // Insert in reverse order
            for (int i = systems.Count - 1; i >= 0; --i)
            {
                parent.AddSystemToUpdateList(systems[i]);
            }

            LogAssert.Expect(LogType.Warning, "Ignoring invalid [UpdateAfter(Unity.Entities.Tests.ComponentSystemGroupTests+DummyLastSystem)] attribute on Unity.Entities.Tests.ComponentSystemGroupTests+FirstAfterLastSystem because OrderFirst/OrderLast has higher precedence.");
            parent.SortSystems();
            LogAssert.NoUnexpectedReceived();

            CollectionAssert.AreEqual(systems, parent.Systems);
        }

        [Test] // runtime string formatting
        public void ComponentSystemSorter_MiddleUpdateBeforeOrderFirst_WarnAndIgnoreConstraint()
        {
            var parent = World.CreateSystem<TestGroup>();
            var systems = new List<EmptySystem>
            {
                World.CreateSystem<DummyFirstSystem>(),
                World.CreateSystem<MiddleBeforeFirstSystem>(),
            };
            // Insert in reverse order
            for (int i = systems.Count - 1; i >= 0; --i)
            {
                parent.AddSystemToUpdateList(systems[i]);
            }

            LogAssert.Expect(LogType.Warning, "Ignoring invalid [UpdateBefore(Unity.Entities.Tests.ComponentSystemGroupTests+DummyFirstSystem)] attribute on Unity.Entities.Tests.ComponentSystemGroupTests+MiddleBeforeFirstSystem because OrderFirst/OrderLast has higher precedence.");
            parent.SortSystems();
            LogAssert.NoUnexpectedReceived();
            CollectionAssert.AreEqual(systems, parent.Systems);
        }

        [Test] // runtime string formatting
        public void ComponentSystemSorter_MiddleUpdateAfterOrderLast_WarnAndIgnoreConstraint()
        {
            var parent = World.CreateSystem<TestGroup>();
            var systems = new List<EmptySystem>
            {
                World.CreateSystem<MiddleAfterLastSystem>(),
                World.CreateSystem<DummyLastSystem>(),
            };
            // Insert in reverse order
            for (int i = systems.Count - 1; i >= 0; --i)
            {
                parent.AddSystemToUpdateList(systems[i]);
            }

            LogAssert.Expect(LogType.Warning, "Ignoring invalid [UpdateAfter(Unity.Entities.Tests.ComponentSystemGroupTests+DummyLastSystem)] attribute on Unity.Entities.Tests.ComponentSystemGroupTests+MiddleAfterLastSystem because OrderFirst/OrderLast has higher precedence.");
            parent.SortSystems();
            LogAssert.NoUnexpectedReceived();
            CollectionAssert.AreEqual(systems, parent.Systems);
        }

        [Test] // runtime string formatting
        public void ComponentSystemSorter_OrderLastUpdateBeforeOrderFirst_WarnAndIgnoreConstraint()
        {
            var parent = World.CreateSystem<TestGroup>();
            var systems = new List<EmptySystem>
            {
                World.CreateSystem<DummyFirstSystem>(),
                World.CreateSystem<LastBeforeFirstSystem>(),
            };
            // Insert in reverse order
            for (int i = systems.Count - 1; i >= 0; --i)
            {
                parent.AddSystemToUpdateList(systems[i]);
            }

            LogAssert.Expect(LogType.Warning, "Ignoring invalid [UpdateBefore(Unity.Entities.Tests.ComponentSystemGroupTests+DummyFirstSystem)] attribute on Unity.Entities.Tests.ComponentSystemGroupTests+LastBeforeFirstSystem because OrderFirst/OrderLast has higher precedence.");
            parent.SortSystems();
            LogAssert.NoUnexpectedReceived();
            CollectionAssert.AreEqual(systems, parent.Systems);
        }

#endif

        [UpdateInGroup(typeof(TestGroup), OrderFirst = true)]
        public class OFL_A : EmptySystem
        {
        }

        [UpdateInGroup(typeof(TestGroup), OrderFirst = true)]
        public class OFL_B : EmptySystem
        {
        }

        public class OFL_C : EmptySystem
        {
        }

        [UpdateInGroup(typeof(TestGroup), OrderLast = true)]
        public class OFL_D : EmptySystem
        {
        }

        [UpdateInGroup(typeof(TestGroup), OrderLast = true)]
        public class OFL_E : EmptySystem
        {
        }

        [Test]
        public void OrderFirstLastWorks([Values(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 30, 31)] int bits)
        {
            var parent = World.CreateSystem<TestGroup>();

            // Add in reverse order
            if (0 != (bits & (1 << 4))) { parent.AddSystemToUpdateList(World.CreateSystem<OFL_E>()); }
            if (0 != (bits & (1 << 3))) { parent.AddSystemToUpdateList(World.CreateSystem<OFL_D>()); }
            if (0 != (bits & (1 << 2))) { parent.AddSystemToUpdateList(World.CreateSystem<OFL_C>()); }
            if (0 != (bits & (1 << 1))) { parent.AddSystemToUpdateList(World.CreateSystem<OFL_B>()); }
            if (0 != (bits & (1 << 0))) { parent.AddSystemToUpdateList(World.CreateSystem<OFL_A>()); }

            parent.SortSystems();

            // Ensure they are always in alphabetical order
            string prev = null;
            foreach (var sys in parent.Systems)
            {
                var curr = TypeManager.GetSystemName(sys.GetType());
                // no string.CompareTo() in DOTS Runtime, but in this case we know only the last character will be different
                int len = curr.Length;
                Assert.IsTrue(prev == null || (prev[len-1] < curr[len-1]));
                prev = curr;
            }
        }

        [UpdateAfter(typeof(TestSystem))]
        struct MyUnmanagedSystem : ISystemBase
        {
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
        public void NewSortWorksWithBoth()
        {
            var parent = World.CreateSystem<TestGroup>();
            var sys = World.AddSystem<MyUnmanagedSystem>();
            var s1 = World.GetOrCreateSystem<TestSystem>();

            parent.AddSystemToUpdateList(sys.Handle);
            parent.AddSystemToUpdateList(s1);

            parent.SortSystems();
        }

        [Test]
        public void ComponentSystemGroup_RemoveThenReAddManagedSystem_SystemIsInGroup()
        {
            var group = World.CreateSystem<TestGroup>();
            var sys = World.CreateSystem<TestSystem>();
            group.AddSystemToUpdateList(sys);

            group.RemoveSystemFromUpdateList(sys);
            group.AddSystemToUpdateList(sys);
            // This is where removals are processed
            group.SortSystems();
            var expectedSystems = new List<ComponentSystemBase> {sys};
            CollectionAssert.AreEqual(expectedSystems, group.Systems);
        }

        [Test]
        public void ComponentSystemGroup_RemoveSystemNotInGroup_Ignored()
        {
            var group = World.CreateSystem<TestGroup>();
            var sys = World.CreateSystem<TestSystem>();
            // group.AddSystemToUpdateList(sys); // the point here is to remove a system _not_ in the group
            group.RemoveSystemFromUpdateList(sys);
            Assert.AreEqual(0, group.m_systemsToRemove.Count);
        }

        [Test]
        public void ComponentSystemGroup_DuplicateRemove_Ignored()
        {
            var group = World.CreateSystem<TestGroup>();
            var sys = World.CreateSystem<TestSystem>();
            group.AddSystemToUpdateList(sys);

            group.RemoveSystemFromUpdateList(sys);
            group.RemoveSystemFromUpdateList(sys);
            var expectedSystems = new List<ComponentSystemBase> {sys};
            CollectionAssert.AreEqual(expectedSystems, group.m_systemsToRemove);
        }

        struct UnmanagedTestSystem : ISystemBase
        {
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
        public void ComponentSystemGroup_RemoveThenReAddUnmanagedSystem_SystemIsInGroup()
        {
            var group = World.CreateSystem<TestGroup>();
            var sys = World.AddSystem<UnmanagedTestSystem>();
            group.AddSystemToUpdateList(sys.Handle);
            Assert.IsTrue(group.m_UnmanagedSystemsToUpdate.Contains(sys.Handle.MHandle), "system not in group after initial add");

            group.RemoveSystemFromUpdateList(sys.Handle);
            group.AddSystemToUpdateList(sys.Handle);
            Assert.IsTrue(group.m_UnmanagedSystemsToUpdate.Contains(sys.Handle.MHandle), "system not in group after remove-and-add");

            group.SortSystems();
            Assert.IsTrue(group.m_UnmanagedSystemsToUpdate.Contains(sys.Handle.MHandle), "system not in group after re-sorting");
        }

        [Test]
        public void ComponentSystemGroup_RemoveUnmanagedSystemNotInGroup_Ignored()
        {
            var group = World.CreateSystem<TestGroup>();
            var sys = World.AddSystem<UnmanagedTestSystem>();
            // group.AddSystemToUpdateList(sys.Handle); // the point here is to remove a system _not_ in the group
            group.RemoveSystemFromUpdateList(sys.Handle);
            Assert.AreEqual(0, group.m_UnmanagedSystemsToRemove.Length);
        }

        [Test]
        public void ComponentSystemGroup_DuplicateRemoveUnmanaged_Ignored()
        {
            var group = World.CreateSystem<TestGroup>();
            var sys = World.AddSystem<UnmanagedTestSystem>();
            group.AddSystemToUpdateList(sys.Handle);

            group.RemoveSystemFromUpdateList(sys.Handle);
            group.RemoveSystemFromUpdateList(sys.Handle);
            var expectedSystems = new List<SystemHandleUntyped> {sys.Handle};
            Assert.AreEqual(1, group.m_UnmanagedSystemsToRemove.Length);
            Assert.AreEqual(sys.Handle.MHandle, group.m_UnmanagedSystemsToRemove[0]);
        }

        [Test]
        public void ComponentSystemGroup_NullFixedRateManager_DoesntThrow()
        {
            var group = World.CreateSystem<TestGroup>();
            group.FixedRateManager = null;
            Assert.DoesNotThrow(() => { group.Update(); });
        }

        class ParentSystemGroup : ComponentSystemGroup
        {
        }

        class ChildSystemGroup : ComponentSystemGroup
        {
        }

        [Test]
        public void ComponentSystemGroup_SortCleanParentWithDirtyChild_ChildIsSorted()
        {
            var parentGroup = World.CreateSystem<ParentSystemGroup>();
            var childGroup = World.CreateSystem<ChildSystemGroup>();
            parentGroup.AddSystemToUpdateList(childGroup); // parent group sort order is dirty
            parentGroup.SortSystems(); // parent group sort order is clean

            var child1 = World.CreateSystem<Sibling1System>();
            var child2 = World.CreateSystem<Sibling2System>();
            childGroup.AddSystemToUpdateList(child1); // child group sort order is dirty
            childGroup.AddSystemToUpdateList(child2);
            parentGroup.SortSystems(); // parent and child group sort orders should be clean

            // If the child group's systems aren't in the correct order, it wasn't recursively sorted by the parent group.
            CollectionAssert.AreEqual(new TestSystemBase[] {child2, child1}, childGroup.Systems);
        }

        class NoSortGroup : ComponentSystemGroup
        {
            public NoSortGroup()
            {
                EnableSystemSorting = false;
            }
        }

        [Test]
        public void ComponentSystemGroup_SortManuallySortedParentWithDirtyChild_ChildIsSorted()
        {
            var parentGroup = World.CreateSystem<NoSortGroup>();
            var childGroup = World.CreateSystem<ChildSystemGroup>();
            parentGroup.AddSystemToUpdateList(childGroup);

            var child1 = World.CreateSystem<Sibling1System>();
            var child2 = World.CreateSystem<Sibling2System>();
            childGroup.AddSystemToUpdateList(child1); // child group sort order is dirty
            childGroup.AddSystemToUpdateList(child2);
            parentGroup.SortSystems(); // parent and child group sort orders should be clean

            // If the child group's systems aren't in the correct order, it wasn't recursively sorted by the parent group.
            CollectionAssert.AreEqual(new TestSystemBase[] {child2, child1}, childGroup.Systems);
        }

        [Test]
        public void ComponentSystemGroup_RemoveFromManuallySortedGroup_Throws()
        {
            var group = World.CreateSystem<NoSortGroup>();
            var sys = World.CreateSystem<TestSystem>();
            group.AddSystemToUpdateList(sys);
            Assert.Throws<InvalidOperationException>(() => group.RemoveSystemFromUpdateList(sys));
        }

        [DotsRuntimeFixme]  // DOTSR-1591 Need ILPP support for ISystemBase in DOTS Runtime
        [Test]
        public void ComponentSystemGroup_RemoveUnmanagedFromManuallySortedGroup_Throws()
        {
            var group = World.CreateSystem<NoSortGroup>();
            var sysHandle = World.AddSystem<MyUnmanagedSystem>().Handle;
            group.AddUnmanagedSystemToUpdateList(sysHandle);
            Assert.Throws<InvalidOperationException>(() => group.RemoveUnmanagedSystemFromUpdateList(sysHandle));
        }

        [DotsRuntimeFixme]  // DOTSR-1591 Need ILPP support for ISystemBase in DOTS Runtime
        [Test]
        public void ComponentSystemGroup_DisableAutoSorting_UpdatesInInsertionOrder()
        {
            var noSortGroup = World.CreateSystem<NoSortGroup>();
            var child1 = World.CreateSystem<Sibling1System>();
            var child2 = World.CreateSystem<Sibling2System>();
            var unmanagedChild = World.AddSystem<MyUnmanagedSystem>();
            noSortGroup.AddSystemToUpdateList(child1);
            noSortGroup.AddUnmanagedSystemToUpdateList(unmanagedChild.Handle);
            noSortGroup.AddSystemToUpdateList(child2);
            // Just adding the systems should cause them to be updated in insertion order
            var expectedUpdateList = new[]
            {
                new UpdateIndex(0, true),
                new UpdateIndex(0, false),
                new UpdateIndex(1, true),
            };
            CollectionAssert.AreEqual(new TestSystemBase[] {child1, child2}, noSortGroup.Systems);
            Assert.AreEqual(1, noSortGroup.UnmanagedSystems.Length);
            Assert.AreEqual(unmanagedChild.Handle.MHandle, noSortGroup.UnmanagedSystems[0]);
            for (int i = 0; i < expectedUpdateList.Length; ++i)
            {
                Assert.AreEqual(expectedUpdateList[i], noSortGroup.m_MasterUpdateList[i]);
            }
            // Sorting the system group should have no effect on the update order
            noSortGroup.SortSystems();
            CollectionAssert.AreEqual(new TestSystemBase[] {child1, child2}, noSortGroup.Systems);
            Assert.AreEqual(1, noSortGroup.UnmanagedSystems.Length);
            Assert.AreEqual(unmanagedChild.Handle.MHandle, noSortGroup.UnmanagedSystems[0]);
            for (int i = 0; i < expectedUpdateList.Length; ++i)
            {
                Assert.AreEqual(expectedUpdateList[i], noSortGroup.m_MasterUpdateList[i]);
            }
        }

        struct UnmanagedSystemWithSyncPointAfterSchedule : ISystemBase
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
        public void ISystemBase_CanHaveSyncPointAfterSchedule()
        {
            var group = World.CreateSystem<TestGroup>();
            var sys = World.AddSystem<UnmanagedSystemWithSyncPointAfterSchedule>();
            group.AddSystemToUpdateList(sys.Handle);
            Assert.DoesNotThrow(() => group.Update());
        }
    }
}
