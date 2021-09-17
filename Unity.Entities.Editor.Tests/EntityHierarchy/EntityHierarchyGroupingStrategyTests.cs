using NUnit.Framework;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Transforms;

namespace Unity.Entities.Editor.Tests
{
    class EntityHierarchyGroupingStrategyTests : DifferTestFixture, IEntityHierarchyGroupingContext
    {
        NativeList<Entity> m_NewEntities;
        NativeList<Entity> m_RemovedEntities;
        EntityHierarchyState m_HierarchyState;
        IEntityHierarchyGroupingStrategy m_Strategy;
        EntityDiffer m_EntityDiffer;
        ComponentDataDiffer m_ComponentDiffer;
        TestHierarchyHelper m_AssertHelper;

        public override void Setup()
        {
            base.Setup();

            m_NewEntities = new NativeList<Entity>(Allocator.TempJob);
            m_RemovedEntities = new NativeList<Entity>(Allocator.TempJob);

            m_HierarchyState = new EntityHierarchyState(World);
            m_Strategy = new EntityHierarchyDefaultGroupingStrategy(World, m_HierarchyState);
            m_AssertHelper = new TestHierarchyHelper(m_HierarchyState);

            m_EntityDiffer = new EntityDiffer(World);
            m_ComponentDiffer = new ComponentDataDiffer(m_Strategy.ComponentsToWatch[0]);
        }

        public override void Teardown()
        {
            m_NewEntities.Dispose();
            m_RemovedEntities.Dispose();
            m_HierarchyState.Dispose();
            m_Strategy.Dispose();
            m_EntityDiffer.Dispose();
            m_ComponentDiffer.Dispose();

            base.Teardown();
        }

        [Test]
        public unsafe void EntityHierarchyState_RootMustExistEvenOnEmptyWorld()
        {
            Assert.That(World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->CountEntities(), Is.EqualTo(0));
            Assert.That(m_HierarchyState.Exists(EntityHierarchyNodeId.Root));
            Assert.DoesNotThrow(() => m_HierarchyState.GetNodeVersion(EntityHierarchyNodeId.Root));
        }

        [Test]
        public void EntityHierarchyState_GetNameOnDynamicSubSceneNode()
        {
            var node = EntityHierarchyNodeId.FromSubScene(1, true);
            Assert.That(m_HierarchyState.GetNodeName(node), Is.EqualTo(EntityHierarchyState.DynamicallyLoadedSubSceneName));

            m_HierarchyState.RegisterAddDynamicSubSceneOperation(1, "the name", out node);
            m_HierarchyState.FlushOperations(this);
            Assert.That(m_HierarchyState.GetNodeName(node), Is.EqualTo("the name"));
        }

        [Test]
        public unsafe void EntityAndComponentDiffer_EnsureReParenting()
        {
            World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
            var entityA = World.EntityManager.CreateEntity();
            var entityB = World.EntityManager.CreateEntity();
            {
                GatherChangesAndApplyToStrategy();
                var r = TestHierarchy.CreateRoot();
                r.AddChild(entityA);
                r.AddChild(entityB);

                m_AssertHelper.AssertHierarchy(r.Build());
            }

            World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
            World.EntityManager.AddComponentData(entityB, new Parent { Value = entityA });
            {
                GatherChangesAndApplyToStrategy();
                m_AssertHelper.AssertHierarchy(TestHierarchy.CreateRoot().AddChild(entityA).AddChild(entityB).Build());
            }

            World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
            World.EntityManager.RemoveComponent(entityB, typeof(Parent));
            {
                GatherChangesAndApplyToStrategy();
                var r = TestHierarchy.CreateRoot();
                r.AddChild(entityA);
                r.AddChild(entityB);

                m_AssertHelper.AssertHierarchy(r.Build());
            }
        }

        [Test]
        public unsafe void EntityAndComponentDiffer_EnsureParentingToSubEntityWithSimulatingParentSystem()
        {
            World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
            var entityA = World.EntityManager.CreateEntity();
            var entityB = World.EntityManager.CreateEntity();
            var entityC = World.EntityManager.CreateEntity();
            AssertInSameChunk(entityA, entityB, entityC);

            GatherChangesAndApplyToStrategy();

            var expectedHierarchy = TestHierarchy.CreateRoot();
            expectedHierarchy.AddChild(entityA);
            expectedHierarchy.AddChild(entityB);
            expectedHierarchy.AddChild(entityC);

            m_AssertHelper.AssertHierarchy(expectedHierarchy.Build());

            // All entities are at the root
            // Now parent B to A

            World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();

            World.EntityManager.AddComponentData(entityB, new Parent { Value = entityA });
            AssertInSameChunk(entityA, entityC);
            AssertInDifferentChunks(entityA, entityB);

            GatherChangesAndApplyToStrategy();

            expectedHierarchy = TestHierarchy.CreateRoot();
            expectedHierarchy.AddChild(entityA)
                                .AddChild(entityB);
            expectedHierarchy.AddChild(entityC);

            m_AssertHelper.AssertHierarchy(expectedHierarchy.Build());

            // A and C are at the root, B is under A
            // Now simulate actual ParentSystem by adding Child buffer containing B to new parent A
            // This will move A to a different archetype

            World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
            World.EntityManager.AddBuffer<Child>(entityA).Add(new Child { Value = entityB });

            AssertInDifferentChunks(entityA, entityB, entityC);

            GatherChangesAndApplyToStrategy();

            expectedHierarchy = TestHierarchy.CreateRoot();
            expectedHierarchy.AddChild(entityA)
                                .AddChild(entityB);
            expectedHierarchy.AddChild(entityC);

            m_AssertHelper.AssertHierarchy(expectedHierarchy.Build());

            // A and C are at the root, B is under A
            // Now parent C to B

            World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();

            World.EntityManager.AddComponentData(entityC, new Parent { Value = entityB });
            AssertInDifferentChunks(entityA, entityB);
            AssertInSameChunk(entityB, entityC);
            GatherChangesAndApplyToStrategy();

            expectedHierarchy = TestHierarchy.CreateRoot();
            expectedHierarchy.AddChild(entityA)
                                .AddChild(entityB)
                                    .AddChild(entityC);

            m_AssertHelper.AssertHierarchy(expectedHierarchy.Build());


            // A is at the root, B is under A, C is under B
            // Now simulate actual ParentSystem by adding Child buffer containing C to new parent B
            // This will move B to a different archetype

            World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();

            var entityBChildren = World.EntityManager.AddBuffer<Child>(entityB);
            entityBChildren.Add(new Child { Value = entityC });

            AssertInDifferentChunks(entityA, entityB, entityC);
            GatherChangesAndApplyToStrategy();

            expectedHierarchy = TestHierarchy.CreateRoot();
            expectedHierarchy.AddChild(entityA)
                                .AddChild(entityB)
                                    .AddChild(entityC);

            m_AssertHelper.AssertHierarchy(expectedHierarchy.Build());
        }

        [Test]
        public unsafe void EntityAndComponentDiffer_PartialParenting_StartFilteredThenFull()
        {
            // Entity with missing parent should be at root
            // and should be correctly re-parented when parent appears

            World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
            var entityA = World.EntityManager.CreateEntity();
            var entityB = World.EntityManager.CreateEntity();
            var entityC = World.EntityManager.CreateEntity();
            var entityD = World.EntityManager.CreateEntity();
            World.EntityManager.AddBuffer<Child>(entityA).Add(new Child { Value = entityB });
            World.EntityManager.AddBuffer<Child>(entityB).Add(new Child { Value = entityC });
            World.EntityManager.AddBuffer<Child>(entityC).Add(new Child { Value = entityD });
            World.EntityManager.AddComponentData(entityB, new Parent { Value = entityA });
            World.EntityManager.AddComponentData(entityC, new Parent { Value = entityB });
            World.EntityManager.AddComponentData(entityD, new Parent { Value = entityC });
            World.EntityManager.AddComponent(entityB, typeof(EcsTestData));

            using (var query = World.EntityManager.CreateEntityQuery(new EntityQueryDesc { None = new ComponentType[] { typeof(EcsTestData) } }))
            {
                GatherChangesAndApplyToStrategy(query);

                // Even though entityB is not matched by the query, entityC should be visible at the root
                var expectedHierarchy = TestHierarchy.CreateRoot();
                expectedHierarchy.AddChild(entityA);
                expectedHierarchy.AddChild(entityC)
                                    .AddChild(entityD);

                m_AssertHelper.AssertHierarchy(expectedHierarchy.Build());
            }

            World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();

            {
                GatherChangesAndApplyToStrategy(World.EntityManager.UniversalQuery);

                var expectedHierarchy = TestHierarchy.CreateRoot();
                expectedHierarchy.AddChild(entityA)
                                    .AddChild(entityB)
                                        .AddChild(entityC)
                                            .AddChild(entityD);

                m_AssertHelper.AssertHierarchy(expectedHierarchy.Build());
            }
        }

        [Test]
        public unsafe void EntityAndComponentDiffer_PartialParenting_StartFullThenFilter()
        {
            // Entity with missing parent should be at root
            // and should be correctly re-parented when parent appears

            World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();

            var entityA = World.EntityManager.CreateEntity();
            var entityB = World.EntityManager.CreateEntity();
            var entityC = World.EntityManager.CreateEntity();
            var entityD = World.EntityManager.CreateEntity();
            World.EntityManager.AddBuffer<Child>(entityA).Add(new Child { Value = entityB });
            World.EntityManager.AddBuffer<Child>(entityB).Add(new Child { Value = entityC });
            World.EntityManager.AddBuffer<Child>(entityC).Add(new Child { Value = entityD });
            World.EntityManager.AddComponentData(entityB, new Parent { Value = entityA });
            World.EntityManager.AddComponentData(entityC, new Parent { Value = entityB });
            World.EntityManager.AddComponentData(entityD, new Parent { Value = entityC });
            World.EntityManager.AddComponent(entityB, typeof(EcsTestData));

            {
                GatherChangesAndApplyToStrategy(World.EntityManager.UniversalQuery);

                var expectedHierarchy = TestHierarchy.CreateRoot();
                expectedHierarchy.AddChild(entityA)
                                    .AddChild(entityB)
                                        .AddChild(entityC)
                                            .AddChild(entityD);

                m_AssertHelper.AssertHierarchy(expectedHierarchy.Build());
            }

            using (var query = World.EntityManager.CreateEntityQuery(new EntityQueryDesc { None = new ComponentType[] { typeof(EcsTestData) } }))
            {
                GatherChangesAndApplyToStrategy(query);

                // Even though entityB is not matched by the query, entityC should be visible at the root
                var expectedHierarchy = TestHierarchy.CreateRoot();
                expectedHierarchy.AddChild(entityA);
                expectedHierarchy.AddChild(entityC)
                                    .AddChild(entityD);

                m_AssertHelper.AssertHierarchy(expectedHierarchy.Build());
            }

            {
                GatherChangesAndApplyToStrategy(World.EntityManager.UniversalQuery);

                var expectedHierarchy = TestHierarchy.CreateRoot();
                expectedHierarchy.AddChild(entityA)
                                    .AddChild(entityB)
                                        .AddChild(entityC)
                                            .AddChild(entityD);

                m_AssertHelper.AssertHierarchy(expectedHierarchy.Build());
            }
        }

        [Test]
        public unsafe void State_DoNotDetectChangesWhenOnlyNoop()
        {
            World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();

            var entityA = World.EntityManager.CreateEntity();
            var entityB = World.EntityManager.CreateEntity();
            World.EntityManager.AddComponent<Parent>(entityA);
            World.EntityManager.AddComponent<Parent>(entityB);

            // new entities has been created, we expect a change to be detected here
            Assert.That(GatherChangesAndApplyToStrategy(World.EntityManager.UniversalQuery), Is.True);

            World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();

            World.EntityManager.AddComponent<Scale>(entityA);
            World.EntityManager.AddComponent<Scale>(entityB);

            // Even though entities have changed archetype, the state should detect this change as a noop because
            // the impacted entities should be detected as destroyed and recreated, and didn't change parent
            Assert.That(GatherChangesAndApplyToStrategy(World.EntityManager.UniversalQuery), Is.False);
        }

        unsafe void AssertInSameChunk(params Entity[] entities)
        {
            ulong chunkSequenceNumber = 0;

            foreach (var e in entities)
            {
                var c = World.EntityManager.GetChunk(e);
                if (chunkSequenceNumber == 0)
                    chunkSequenceNumber = c.m_Chunk->SequenceNumber;
                else
                    Assert.That(c.m_Chunk->SequenceNumber, Is.EqualTo(chunkSequenceNumber));
            }
        }

        unsafe void AssertInDifferentChunks(params Entity[] entities)
        {
            var chunkSequenceNumbers = new HashSet<ulong>();

            foreach (var e in entities)
            {
                Assert.That(chunkSequenceNumbers.Add(World.EntityManager.GetChunk(e).m_Chunk->SequenceNumber), Is.True);
            }
        }

        bool GatherChangesAndApplyToStrategy(EntityQuery? query = null)
        {
            var entityJobHandle = m_EntityDiffer.GetEntityQueryMatchDiffAsync(query ?? World.EntityManager.UniversalQuery, m_NewEntities, m_RemovedEntities);
            using (var changes = m_ComponentDiffer.GatherComponentChangesAsync(query ?? World.EntityManager.UniversalQuery, Allocator.TempJob, out var componentJobHandle))
            {
                m_Strategy.BeginApply(this);
                entityJobHandle.Complete();
                m_Strategy.ApplyEntityChanges(m_NewEntities, m_RemovedEntities, this);
                componentJobHandle.Complete();
                m_Strategy.ApplyComponentDataChanges(typeof(Parent), changes, this);
                return m_Strategy.EndApply(this);
            }
        }

        public uint Version => World.EntityManager.GlobalSystemVersion;
        public ISceneMapper SceneMapper { get; } = new NoopSceneMapper();

        class NoopSceneMapper : ISceneMapper
        {
            public Hash128 GetSubSceneHash(World world, Entity tagSceneEntity)
            {
                return default;
            }

            public (int subSceneId, bool isDynamicSubScene) GetSubSceneId(Hash128 subSceneHash)
            {
                return default;
            }

            public bool TryGetSceneOrSubSceneInstanceId(Hash128 subSceneHash, out int instanceId)
            {
                instanceId = 0;
                return false;
            }

            public Hash128 GetParentSceneHash(Hash128 subSceneHash)
            {
                return default;
            }
        }
    }
}
