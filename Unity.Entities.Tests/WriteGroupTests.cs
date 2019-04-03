using System;
using NUnit.Framework;
using Unity.Collections;

namespace Unity.Entities.Tests
{
    [TestFixture]
    class WriteGroupTests : ECSTestsFixture
    {
        // 
        //     +-----------+        +----------+
        //     |TestOutputA<--------+TestInputB|
        //     +-----------+    ^   +----------+
        //          ^           |   +----------+
        //          |           +---+TestInputC|
        //     +-----------+    ^   +----------+
        //     |TestOutputB<----+   +----------+
        //     +-----------+    +---+TestInputD|
        //                          +----------+

        struct TestOutputA : IComponentData
        {
        }

        [WriteGroup(typeof(TestOutputA))]
        struct TestOutputB : IComponentData
        {
        }

        [WriteGroup(typeof(TestOutputA))]
        [WriteGroup(typeof(TestOutputB))]
        struct TestInputB : IComponentData
        {
        }

        [WriteGroup(typeof(TestOutputA))]
        [WriteGroup(typeof(TestOutputB))]
        struct TestInputC : IComponentData
        {
        }

        [WriteGroup(typeof(TestOutputA))]
        [WriteGroup(typeof(TestOutputB))]
        struct TestInputD : IComponentData
        {
        }


        [Test]
        public void WG_AllOnlyMatchesExplicit()
        {
            var archetype0 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC));
            var archetype1 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC),
                typeof(TestInputD));
            var group0 = m_Manager.CreateComponentGroup(new EntityArchetypeQuery()
            {
                All = new ComponentType[]
                {
                    typeof(TestOutputA),
                    ComponentType.ReadOnly<TestInputB>(),
                    ComponentType.ReadOnly<TestInputC>(),
                }
            });

            m_Manager.CreateEntity(archetype0);
            m_Manager.CreateEntity(archetype1);

            var results0 = group0.ToEntityArray(Allocator.TempJob);
            Assert.AreEqual(1, results0.Length);
            results0.Dispose();

            group0.Dispose();
        }

        [Test]
        public void WG_AllOnlyMatchesExplicitLateDefinition()
        {
            var group0 = m_Manager.CreateComponentGroup(new EntityArchetypeQuery()
            {
                All = new ComponentType[]
                {
                    typeof(TestOutputA),
                    ComponentType.ReadOnly<TestInputB>(),
                    ComponentType.ReadOnly<TestInputC>(),
                }
            });

            var archetype0 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC));
            var archetype1 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC),
                typeof(TestInputD));

            m_Manager.CreateEntity(archetype0);
            m_Manager.CreateEntity(archetype1);

            var results0 = group0.ToEntityArray(Allocator.TempJob);
            Assert.AreEqual(1, results0.Length);
            results0.Dispose();

            group0.Dispose();
        }

        [Test]
        public void WG_AllOnlyMatchesExtended()
        {
            var archetype0 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC));
            var archetype1 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC),
                typeof(TestInputD));
            var group0 = m_Manager.CreateComponentGroup(new EntityArchetypeQuery()
            {
                All = new ComponentType[]
                {
                    typeof(TestOutputA),
                    ComponentType.ReadOnly<TestInputB>(),
                    ComponentType.ReadOnly<TestInputC>(),
                    ComponentType.ReadOnly<TestInputD>(),
                }
            });

            m_Manager.CreateEntity(archetype0);
            m_Manager.CreateEntity(archetype1);

            var results0 = group0.ToEntityArray(Allocator.TempJob);
            Assert.AreEqual(1, results0.Length);
            results0.Dispose();

            group0.Dispose();
        }

        [Test]
        public void WG_AnyOnlyMatchesExplicit()
        {
            var archetype0 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC));
            var archetype1 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC),
                typeof(TestInputD));
            var group0 = m_Manager.CreateComponentGroup(new EntityArchetypeQuery()
            {
                Any = new ComponentType[]
                {
                    typeof(TestOutputA),
                    ComponentType.ReadOnly<TestInputB>(),
                    ComponentType.ReadOnly<TestInputC>(),
                }
            });

            m_Manager.CreateEntity(archetype0);
            m_Manager.CreateEntity(archetype1);

            var results0 = group0.ToEntityArray(Allocator.TempJob);
            Assert.AreEqual(1, results0.Length);
            results0.Dispose();

            group0.Dispose();
        }

        [Test]
        public void WG_AnyMatchesAll()
        {
            var archetype0 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC));
            var archetype1 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC),
                typeof(TestInputD));
            var group0 = m_Manager.CreateComponentGroup(new EntityArchetypeQuery()
            {
                Any = new ComponentType[]
                {
                    typeof(TestOutputA),
                    ComponentType.ReadOnly<TestInputB>(),
                    ComponentType.ReadOnly<TestInputC>(),
                    ComponentType.ReadOnly<TestInputD>(),
                }
            });

            m_Manager.CreateEntity(archetype0);
            m_Manager.CreateEntity(archetype1);

            var results0 = group0.ToEntityArray(Allocator.TempJob);
            Assert.AreEqual(2, results0.Length);
            results0.Dispose();

            group0.Dispose();
        }

        [Test]
        public void WG_AnyExplicitlyExcludesExtension()
        {
            var archetype0 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC));
            var archetype1 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC),
                typeof(TestInputD));
            var group0 = m_Manager.CreateComponentGroup(new EntityArchetypeQuery()
            {
                Any = new ComponentType[]
                {
                    typeof(TestOutputA),
                    ComponentType.ReadOnly<TestInputB>(),
                    ComponentType.ReadOnly<TestInputC>(),
                },
                None = new ComponentType[]
                {
                    ComponentType.ReadOnly<TestInputD>(),
                }
            });

            m_Manager.CreateEntity(archetype0);
            m_Manager.CreateEntity(archetype1);

            var results0 = group0.ToEntityArray(Allocator.TempJob);
            Assert.AreEqual(1, results0.Length);
            results0.Dispose();

            group0.Dispose();
        }

        [Test]
        public void WG_AllAllowsDependentWriteGroups()
        {
            var archetype0 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestOutputB), typeof(TestInputB),
                typeof(TestInputC));
            var archetype1 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestOutputB), typeof(TestInputB),
                typeof(TestInputC), typeof(TestInputD));
            var group0 = m_Manager.CreateComponentGroup(new EntityArchetypeQuery()
            {
                All = new ComponentType[]
                {
                    typeof(TestOutputA),
                    ComponentType.ReadOnly<TestOutputB>()
                }
            });

            m_Manager.CreateEntity(archetype0);
            m_Manager.CreateEntity(archetype1);

            var results0 = group0.ToEntityArray(Allocator.TempJob);
            Assert.AreEqual(2, results0.Length);
            results0.Dispose();

            group0.Dispose();
        }

        [Test]
        public void WG_AllExcludesFromDependentWriteGroup()
        {
            var archetype0 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestOutputB), typeof(TestInputB),
                typeof(TestInputC));
            var archetype1 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestOutputB), typeof(TestInputB),
                typeof(TestInputC), typeof(TestInputD));
            var group0 = m_Manager.CreateComponentGroup(new EntityArchetypeQuery()
            {
                All = new ComponentType[]
                {
                    typeof(TestOutputA),
                    ComponentType.ReadOnly<TestInputB>()
                }
            });

            m_Manager.CreateEntity(archetype0);
            m_Manager.CreateEntity(archetype1);

            var results0 = group0.ToEntityArray(Allocator.TempJob);
            Assert.AreEqual(0, results0.Length);
            results0.Dispose();

            group0.Dispose();
        }

        [Test]
        public void WG_AllOnlyMatchesExplicitNoQuery()
        {
            var archetype0 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC));
            var archetype1 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC),
                typeof(TestInputD));
            var group0 = m_Manager.CreateComponentGroup(
                typeof(TestOutputA),
                ComponentType.ReadOnly<TestInputB>(),
                ComponentType.ReadOnly<TestInputC>()
            );

            m_Manager.CreateEntity(archetype0);
            m_Manager.CreateEntity(archetype1);

            var results0 = group0.ToEntityArray(Allocator.TempJob);
            Assert.AreEqual(1, results0.Length);
            results0.Dispose();

            group0.Dispose();
        }

        [Test]
        public void WG_AllOnlyMatchesExplicitLateDefinitionNoQuery()
        {
            var group0 = m_Manager.CreateComponentGroup(
                typeof(TestOutputA),
                ComponentType.ReadOnly<TestInputB>(),
                ComponentType.ReadOnly<TestInputC>()
            );

            var archetype0 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC));
            var archetype1 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC),
                typeof(TestInputD));

            m_Manager.CreateEntity(archetype0);
            m_Manager.CreateEntity(archetype1);

            var results0 = group0.ToEntityArray(Allocator.TempJob);
            Assert.AreEqual(1, results0.Length);
            results0.Dispose();

            group0.Dispose();
        }

        [Test]
        public void WG_AllOnlyMatchesExtendedNoQuery()
        {
            var archetype0 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC));
            var archetype1 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestInputB), typeof(TestInputC),
                typeof(TestInputD));
            var group0 = m_Manager.CreateComponentGroup(
                typeof(TestOutputA),
                ComponentType.ReadOnly<TestInputB>(),
                ComponentType.ReadOnly<TestInputC>(),
                ComponentType.ReadOnly<TestInputD>()
            );

            m_Manager.CreateEntity(archetype0);
            m_Manager.CreateEntity(archetype1);

            var results0 = group0.ToEntityArray(Allocator.TempJob);
            Assert.AreEqual(1, results0.Length);
            results0.Dispose();

            group0.Dispose();
        }

        [Test]
        public void WG_AllAllowsDependentWriteGroupsNoQuery()
        {
            var archetype0 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestOutputB), typeof(TestInputB),
                typeof(TestInputC));
            var archetype1 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestOutputB), typeof(TestInputB),
                typeof(TestInputC), typeof(TestInputD));
            var group0 = m_Manager.CreateComponentGroup(
                typeof(TestOutputA),
                ComponentType.ReadOnly<TestOutputB>()
            );

            m_Manager.CreateEntity(archetype0);
            m_Manager.CreateEntity(archetype1);

            var results0 = group0.ToEntityArray(Allocator.TempJob);
            Assert.AreEqual(2, results0.Length);
            results0.Dispose();

            group0.Dispose();
        }

        [Test]
        public void WG_AllExcludesFromDependentWriteGroupNoQuery()
        {
            var archetype0 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestOutputB), typeof(TestInputB),
                typeof(TestInputC));
            var archetype1 = m_Manager.CreateArchetype(typeof(TestOutputA), typeof(TestOutputB), typeof(TestInputB),
                typeof(TestInputC), typeof(TestInputD));
            var group0 = m_Manager.CreateComponentGroup(
                typeof(TestOutputA),
                ComponentType.ReadOnly<TestInputB>()
            );

            m_Manager.CreateEntity(archetype0);
            m_Manager.CreateEntity(archetype1);

            var results0 = group0.ToEntityArray(Allocator.TempJob);
            Assert.AreEqual(0, results0.Length);
            results0.Dispose();

            group0.Dispose();
        }
    }
}
