using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.Jobs;

namespace Unity.Entities.Tests
{
    class ComponentSystemGroupTests : ECSTestsFixture
    {
        [DisableAutoCreation]
        class TestGroup : ComponentSystemGroup
        {

        }

        [DisableAutoCreation]
#if UNITY_CSHARP_TINY
        private class TestSystemBase :ComponentSystem
        {
            protected override void OnUpdate() => throw new System.NotImplementedException();
        }

#else
        private class TestSystemBase : JobComponentSystem
        {
            protected override JobHandle OnUpdate(JobHandle inputDeps) => throw new System.NotImplementedException();
        }
#endif

        [Test]
        public void SortEmptyParentSystem()
        {
            var parent = new TestGroup();
            parent.SortSystemUpdateList();
        }

        [DisableAutoCreation]
        class TestSystem : TestSystemBase
        {
        }

        [Test]
        public void SortOneChildSystem()
        {
            var parent = new TestGroup();
            var child = new TestSystem();
            parent.AddSystemToUpdateList(child);
            parent.SortSystemUpdateList();
            CollectionAssert.AreEqual(new[] {child}, parent.Systems);
        }

        [DisableAutoCreation]
        [UpdateAfter(typeof(Sibling2System))]
        class Sibling1System : TestSystemBase
        {
        }
        [DisableAutoCreation]
        class Sibling2System : TestSystemBase
        {
        }

        [Test]
        public void SortTwoChildSystems_CorrectOrder()
        {
            var parent = new TestGroup();
            var child1 = new Sibling1System();
            var child2 = new Sibling2System();
            parent.AddSystemToUpdateList(child1);
            parent.AddSystemToUpdateList(child2);
            parent.SortSystemUpdateList();
            CollectionAssert.AreEqual(new TestSystemBase[] {child2, child1}, parent.Systems);
        }

        // This test constructs the following system dependency graph:
        // 1 -> 2 -> 3 -> 4 -v
        //           ^------ 5 -> 6
        // The expected results of topologically sorting this graph:
        // - systems 1 and 2 are properly sorted in the system update list.
        // - systems 3, 4, and 5 form a cycle (in that order, or equivalent).
        // - system 6 is not sorted AND is not part of the cycle.
        [DisableAutoCreation]
        [UpdateBefore(typeof(Circle2System))]
        class Circle1System : TestSystemBase
        {
        }
        [DisableAutoCreation]
        [UpdateBefore(typeof(Circle3System))]
        class Circle2System : TestSystemBase
        {
        }
        [DisableAutoCreation]
        [UpdateAfter(typeof(Circle5System))]
        class Circle3System : TestSystemBase
        {
        }
        [DisableAutoCreation]
        [UpdateAfter(typeof(Circle3System))]
        class Circle4System : TestSystemBase
        {
        }
        [DisableAutoCreation]
        [UpdateAfter(typeof(Circle4System))]
        class Circle5System : TestSystemBase
        {
        }
        [DisableAutoCreation]
        [UpdateAfter(typeof(Circle5System))]
        class Circle6System : TestSystemBase
        {
        }

        [Test]
#if UNITY_CSHARP_TINY
        [Ignore("Tiny pre-compiles systems. Many tests will fail if they exist, not just this one.")]
#endif
        public void DetectCircularDependency_Throws()
        {
            var parent = new TestGroup();
            var child1 = new Circle1System();
            var child2 = new Circle2System();
            var child3 = new Circle3System();
            var child4 = new Circle4System();
            var child5 = new Circle5System();
            var child6 = new Circle6System();
            parent.AddSystemToUpdateList(child3);
            parent.AddSystemToUpdateList(child6);
            parent.AddSystemToUpdateList(child2);
            parent.AddSystemToUpdateList(child4);
            parent.AddSystemToUpdateList(child1);
            parent.AddSystemToUpdateList(child5);
            var e = Assert.Throws<CircularSystemDependencyException>(() => parent.SortSystemUpdateList());
            // Make sure the system upstream of the cycle was properly sorted
            CollectionAssert.AreEqual(new TestSystemBase[] {child1, child2}, parent.Systems);
            // Make sure the cycle expressed in e.Chain is the one we expect, even though it could start at any node
            // in the cycle.
            var expectedCycle = new TestSystemBase[] {child5, child3, child4};
            var cycle = e.Chain.ToList();
            bool foundCycleMatch = false;
            for (int i = 0; i < cycle.Count; ++i)
            {
                var offsetCycle = new System.Collections.Generic.List<ComponentSystemBase>(cycle.Count);
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
    }
}
