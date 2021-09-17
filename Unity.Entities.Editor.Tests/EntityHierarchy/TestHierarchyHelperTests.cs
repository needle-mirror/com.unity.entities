using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Collections;

namespace Unity.Entities.Editor.Tests
{
    class TestHierarchyHelperTests
    {
        DummyState m_State;
        TestHierarchyHelper m_Helper;

        [SetUp]
        public void Setup()
        {
            m_State = new DummyState();
            m_Helper = new TestHierarchyHelper(m_State);
        }

        [Test]
        public void TestHierarchy_AssertSimpleHierarchy()
        {
            m_State.SetHierarchy(TestHierarchy.CreateRoot().Build());
            Assert.DoesNotThrow(() => m_Helper.AssertHierarchy(TestHierarchy.CreateRoot().Build()));

            m_State.SetHierarchy(TestHierarchy.CreateRoot()
                                        .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, 1, 0))
                                        .Build());
            Assert.Throws<AssertionException>(() => m_Helper.AssertHierarchy(TestHierarchy
                                                                                 .CreateRoot()
                                                                                 .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, 2, 0))
                                                                                 .Build()));
        }

        [Test]
        public void TestHierarchy_AssertShouldNotFailOnChildrenOrdering()
        {
            var actualHierarchy = TestHierarchy.CreateRoot();
            actualHierarchy.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, 1, 0));
            actualHierarchy.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, 2, 0));

            var expectedHierarchy = TestHierarchy.CreateRoot();
            expectedHierarchy.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, 2, 0));
            expectedHierarchy.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, 1, 0));


            Assert.That(expectedHierarchy.Children, Is.EquivalentTo(actualHierarchy.Children));
            Assert.That(expectedHierarchy.Children.SequenceEqual(actualHierarchy.Children), Is.False);

            m_State.SetHierarchy(actualHierarchy.Build());
            Assert.DoesNotThrow(() => m_Helper.AssertHierarchy(expectedHierarchy.Build()));
        }

        [Test]
        public void TestHierarchy_AssertSimpleHierarchyByKind()
        {
            m_State.SetHierarchy(TestHierarchy.CreateRoot().Build());
            Assert.DoesNotThrow(() => m_Helper.AssertHierarchyByKind(TestHierarchy.CreateRoot().Build()));

            m_State.SetHierarchy(TestHierarchy.CreateRoot()
                                                 .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, 1, 0))
                                                 .Build());

            // Different ids should not throw if kinds are the same
            Assert.DoesNotThrow(() => m_Helper.AssertHierarchyByKind(TestHierarchy
                                                                    .CreateRoot()
                                                                    .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, 2, 0))
                                                                    .Build()));

            m_State.SetHierarchy(TestHierarchy.CreateRoot()
                                                 .AddChild(new EntityHierarchyNodeId(NodeKind.SubScene, 1, 0))
                                                 .Build());

            // Different kinds should throw even if ids are the same
            Assert.Throws<AssertionException>(() => m_Helper.AssertHierarchyByKind(TestHierarchy
                                                                                  .CreateRoot()
                                                                                  .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, 1, 0))
                                                                                  .Build()));
        }

        [Test]
        public void TestHierarchy_AssertComplexHierarchyByKind()
        {
            var sceneId = 0;
            var entityId = 0;

            var hierarchyA1 = TestHierarchy.CreateRoot();
            {
                hierarchyA1.AddChildren(
                    new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0),
                    new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0),
                    new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0));
                hierarchyA1.AddChild(
                                new EntityHierarchyNodeId(NodeKind.SubScene, sceneId++, 0))
                               .AddChild(
                                    new EntityHierarchyNodeId(NodeKind.SubScene, sceneId++, 0))
                                   .AddChildren(
                                        new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0),
                                        new EntityHierarchyNodeId(NodeKind.SubScene, sceneId++, 0));
                hierarchyA1.AddChild(
                                new EntityHierarchyNodeId(NodeKind.SubScene, sceneId++, 0))
                               .AddChildren(
                                    new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0),
                                    new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0));
            }

            // Matches A1, but with different ids and in a different order
            var hierarchyA2 = TestHierarchy.CreateRoot();
            {
                hierarchyA2.AddChild(
                                new EntityHierarchyNodeId(NodeKind.SubScene, sceneId++, 0))
                               .AddChild(
                                    new EntityHierarchyNodeId(NodeKind.SubScene, sceneId++, 0))
                                   .AddChildren(
                                        new EntityHierarchyNodeId(NodeKind.SubScene, sceneId++, 0),
                                        new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0));
                hierarchyA2.AddChildren(
                    new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0),
                    new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0));
                hierarchyA2.AddChild(
                                new EntityHierarchyNodeId(NodeKind.SubScene, sceneId++, 0))
                               .AddChildren(
                                    new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0),
                                    new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0));
                hierarchyA2.AddChild(
                    new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0));
            }

            // Does not match A1 or A2
            var hierarchyB = TestHierarchy.CreateRoot();
            {
                hierarchyB.AddChild(
                    new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0));
                hierarchyB.AddChild(
                               new EntityHierarchyNodeId(NodeKind.SubScene, sceneId++, 0))
                              .AddChild(
                                   new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0))
                                  .AddChildren(
                                       new EntityHierarchyNodeId(NodeKind.SubScene, sceneId++, 0),
                                       new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0));
                hierarchyB.AddChildren(
                    new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0),
                    new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0));
                hierarchyB.AddChild(
                               new EntityHierarchyNodeId(NodeKind.SubScene, sceneId++, 0))
                              .AddChildren(
                                   new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0),
                                   new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0));
            }

            m_State.SetHierarchy(hierarchyA1.Build());
            Assert.DoesNotThrow(() => m_Helper.AssertHierarchyByKind(hierarchyA2.Build()));
            Assert.Throws<AssertionException>(() => m_Helper.AssertHierarchyByKind(hierarchyB.Build()));
        }

        [Test]
        public void TestHierarchy_ErrorMessageShouldPrintOrderedChildren()
        {
            var actualHierarchy = TestHierarchy.CreateRoot();
            actualHierarchy.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, 1, 0));
            actualHierarchy.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, 2, 0));
            m_State.SetHierarchy(actualHierarchy.Build());

            var testHierarchy = TestHierarchy.CreateRoot();
            testHierarchy.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, 2, 0));
            testHierarchy.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, 1, 0));

            var builder = new StringBuilder();

            testHierarchy.Build().WriteTree(builder, 0);
            var testHierarchyString = builder.ToString();

            builder.Clear();

            m_Helper.WriteActualStrategyTree(builder, EntityHierarchyNodeId.Root, 0);
            var strategyHierarchyString = builder.ToString();

            Assert.That(testHierarchyString, Is.EqualTo(strategyHierarchyString));
        }

        class DummyState : IEntityHierarchyState
        {
            Dictionary<EntityHierarchyNodeId, EntityHierarchyNodeId> m_Parents = new Dictionary<EntityHierarchyNodeId, EntityHierarchyNodeId>();
            Dictionary<EntityHierarchyNodeId, HashSet<EntityHierarchyNodeId>> m_Children = new Dictionary<EntityHierarchyNodeId, HashSet<EntityHierarchyNodeId>>();

            public void SetHierarchy(TestHierarchy expectedHierarchy)
            {
                m_Parents.Clear();
                m_Children.Clear();

                BuildState(expectedHierarchy.Root);
            }

            void BuildState(TestHierarchy.TestNode testNode)
            {
                if (!m_Children.TryGetValue(testNode.NodeId, out var children))
                {
                    children = new HashSet<EntityHierarchyNodeId>();
                    m_Children.Add(testNode.NodeId, children);
                }

                foreach (var child in testNode.Children)
                {
                    children.Add(child.NodeId);
                    m_Parents[child.NodeId] = testNode.NodeId;
                }

                foreach (var child in testNode.Children)
                {
                    BuildState(child);
                }
            }

            public void Dispose() { }

            public bool TryGetChildren(in EntityHierarchyNodeId nodeId, out HashSet<EntityHierarchyNodeId> children) => throw new NotImplementedException();

            public bool HasChildren(in EntityHierarchyNodeId nodeId)
                => m_Children.TryGetValue(nodeId, out var children) && children.Count > 0;

            public void GetChildren(in EntityHierarchyNodeId nodeId, List<EntityHierarchyNodeId> childrenList)
                => childrenList.AddRange(m_Children[nodeId]);

            public HashSet<EntityHierarchyNodeId> GetChildren(in EntityHierarchyNodeId nodeId) => m_Children[nodeId];
            public int GetDepth(in EntityHierarchyNodeId nodeId) => throw new NotImplementedException();
            public EntityHierarchyNodeId GetParent(in EntityHierarchyNodeId nodeId) => throw new NotImplementedException();
            public IReadOnlyList<EntityHierarchyNodeId> GetAllNodesOrdered() => throw new NotImplementedException();
            public IReadOnlyCollection<EntityHierarchyNodeId> GetAllNodesUnordered() => throw new NotImplementedException();
            public IEnumerable<EntityHierarchyNodeId> GetAllDescendants(in EntityHierarchyNodeId node) => throw new NotImplementedException();
            public bool Exists(in EntityHierarchyNodeId nodeId)
                => m_Parents.ContainsKey(nodeId) || m_Children.ContainsKey(nodeId);

            public uint GetNodeVersion(in EntityHierarchyNodeId nodeId) => throw new NotImplementedException();
            public string GetNodeName(in EntityHierarchyNodeId nodeId) => throw new NotImplementedException();

            public void GetNodesBeingAdded(List<EntityHierarchyNodeId> nodesBeingAdded) => throw new NotImplementedException();
            public void GetNodesBeingAdded(HashSet<EntityHierarchyNodeId> nodesBeingAdded) => throw new NotImplementedException();
            public void GetNodesBeingRemoved(List<EntityHierarchyNodeId> nodesBeingRemoved) => throw new NotImplementedException();
            public void GetNodesBeingRemoved(HashSet<EntityHierarchyNodeId> nodesBeingRemoved) => throw new NotImplementedException();
            public void GetNodesBeingMoved(List<EntityHierarchyNodeId> nodesBeingMoved) => throw new NotImplementedException();
            public void GetNodesBeingMoved(HashSet<EntityHierarchyNodeId> nodesBeingMoved) => throw new NotImplementedException();

            public bool TryGetFutureParent(in EntityHierarchyNodeId node, out EntityHierarchyNodeId nextParent) => throw new NotImplementedException();

            public void RegisterAddEntityOperation(Entity entity, out EntityHierarchyNodeId generatedNode) => throw new NotImplementedException();

            public void RegisterAddSceneOperation(int sceneId, out EntityHierarchyNodeId generatedNode) => throw new NotImplementedException();

            public void RegisterAddSubSceneOperation(int subSceneId, out EntityHierarchyNodeId generatedNode) => throw new NotImplementedException();

            public void RegisterAddDynamicSubSceneOperation(int subSceneId, string name, out EntityHierarchyNodeId generatedNode) => throw new NotImplementedException();

            public void RegisterAddCustomNodeOperation(FixedString64Bytes name, out EntityHierarchyNodeId generatedNode) => throw new NotImplementedException();

            public void RegisterRemoveOperation(in EntityHierarchyNodeId node) => throw new NotImplementedException();

            public void RegisterMoveOperation(in EntityHierarchyNodeId toNode, in EntityHierarchyNodeId node) => throw new NotImplementedException();

            public bool FlushOperations(IEntityHierarchyGroupingContext context) => throw new NotImplementedException();
        }
    }
}
