using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Unity.Entities.Editor.Tests
{
    class TestHierarchyHelper
    {
        IEntityHierarchyState m_HierarchyState;

        public TestHierarchyHelper(IEntityHierarchyState hierarchyState)
        {
            m_HierarchyState = hierarchyState;
        }

        public void AssertHierarchy(TestHierarchy expectedHierarchy)
        {
            if (!AssertNode(expectedHierarchy.Root))
                throw new AssertionException(GenerateTreeErrorMessage(expectedHierarchy));

            Assert.That(true);
        }

        bool AssertNode(TestHierarchy.TestNode expectedNode)
        {
            if (!m_HierarchyState.Exists(expectedNode.NodeId))
                return false;

            var isExpectedToHaveChildren = expectedNode.Children.Count > 0;
            if (m_HierarchyState.HasChildren(expectedNode.NodeId) != isExpectedToHaveChildren)
                return false;

            if (!isExpectedToHaveChildren)
                return true;

            using (var children = PooledList<EntityHierarchyNodeId>.Make())
            {
                m_HierarchyState.GetChildren(expectedNode.NodeId, children.List);

                if (children.List.Count != expectedNode.Children.Count)
                    return false;

                var orderedStrategyChildren = children.List.OrderBy(x => x).ToArray();
                var orderedExpectedChildren = expectedNode.Children.OrderBy(x => x.NodeId).ToArray();

                for (var i = 0; i < orderedStrategyChildren.Length; i++)
                {
                    if (!orderedStrategyChildren[i].Equals(orderedExpectedChildren[i].NodeId))
                        return false;
                }
            }

            foreach (var expectedNodeChild in expectedNode.Children)
            {
                if (!AssertNode(expectedNodeChild))
                    return false;
            }

            return true;
        }

        // Asserts a hierarchy where we only know the expected Kind of each EntityHierarchyNodeId, but not the actual Id
        // Useful in integration tests where an external system assigns Entity Ids, during conversion
        public void AssertHierarchyByKind(TestHierarchy expectedHierarchy)
        {
            if (!AssertNodes(new[] { expectedHierarchy.Root }, new[] { EntityHierarchyNodeId.Root }))
                throw new AssertionException(GenerateTreeErrorMessage(expectedHierarchy, true));

            Assert.That(true);
        }

        // Breadth-first search, sorted by Kind
        bool AssertNodes(ICollection<TestHierarchy.TestNode> expectedNodes, ICollection<EntityHierarchyNodeId> foundNodes)
        {
            if (expectedNodes.Count != foundNodes.Count)
                return false;

            var sortedExpectedNodes = expectedNodes.OrderBy(node => node.NodeId.Kind).ToList();
            var sortedFoundNodes = foundNodes.OrderBy(node => node.Kind).ToList();

            var expectedChildren = new List<TestHierarchy.TestNode>();
            var foundChildren = new List<EntityHierarchyNodeId>();

            for (int i = 0; i < expectedNodes.Count; ++i)
            {
                var expectedNode = sortedExpectedNodes[i];
                var foundNode = sortedFoundNodes[i];

                if (expectedNode.NodeId.Kind != foundNode.Kind)
                    return false;

                expectedChildren.AddRange(expectedNode.Children);

                if (m_HierarchyState.HasChildren(foundNode))
                {
                    using (var children = PooledList<EntityHierarchyNodeId>.Make())
                    {
                        m_HierarchyState.GetChildren(foundNode, children.List);
                        foundChildren.AddRange(children.List);
                    }
                }
            }

            if (expectedChildren.Count == 0 && foundChildren.Count == 0)
                return true;

            return AssertNodes(expectedChildren, foundChildren);
        }

        string GenerateTreeErrorMessage(TestHierarchy expectedHierarchy, bool kindOnly = false)
        {
            var errorMessage = new StringBuilder();
            errorMessage.AppendLine("Expected hierarchy doesn't match actual strategy state.");
            errorMessage.AppendLine("Expected: ");
            expectedHierarchy.WriteTree(errorMessage, 0, kindOnly);

            errorMessage.AppendLine("But was: ");
            WriteActualStrategyTree(errorMessage, EntityHierarchyNodeId.Root, 0, kindOnly);

            return errorMessage.ToString();
        }

        internal void WriteActualStrategyTree(StringBuilder errorMessage, EntityHierarchyNodeId nodeId, int indent, bool kindOnly = false)
        {
            errorMessage.Append(' ', indent);
            errorMessage.Append("- ");
            errorMessage.AppendLine(kindOnly ? nodeId.Kind.ToString() : nodeId.ToString());

            if (!m_HierarchyState.HasChildren(nodeId))
                return;
            indent++;

            using (var children = PooledList<EntityHierarchyNodeId>.Make())
            {
                m_HierarchyState.GetChildren(nodeId, children.List);

                foreach (var child in children.List.OrderBy(x => x))
                {
                    WriteActualStrategyTree(errorMessage, child, indent, kindOnly);
                }
            }

        }
    }
}
