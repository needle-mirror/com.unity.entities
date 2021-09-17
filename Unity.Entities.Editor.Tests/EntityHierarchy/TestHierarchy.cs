using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.Entities.Editor.Tests
{
    class TestHierarchy
    {
        public TestNode Root;

        public static TestNode CreateRoot()
        {
            var testHierarchy = new TestHierarchy();
            var root = new TestNode(testHierarchy, EntityHierarchyNodeId.Root);
            testHierarchy.Root = root;
            return root;
        }

        internal class TestNode : IEquatable<TestNode>, IEquatable<EntityHierarchyNodeId>
        {
            readonly TestHierarchy m_TestHierarchy;

            public EntityHierarchyNodeId NodeId;

            public List<TestNode> Children = new List<TestNode>();

            public TestNode(TestHierarchy testHierarchy, EntityHierarchyNodeId nodeId)
                => (m_TestHierarchy, NodeId) = (testHierarchy, nodeId);

            public TestHierarchy Build()
                => m_TestHierarchy;

            public TestNode AddChild(EntityHierarchyNodeId childrenNodeId)
            {
                var childrenNode = new TestNode(m_TestHierarchy, childrenNodeId);
                Children.Add(childrenNode);
                return childrenNode;
            }

            public TestNode AddChild(Entity entity)
                => AddChild(EntityHierarchyNodeId.FromEntity(entity));

            public TestNode AddChildren(params EntityHierarchyNodeId[] childrenNodeIds)
            {
                foreach (var childrenNodeId in childrenNodeIds)
                {
                    var childrenNode = new TestNode(m_TestHierarchy, childrenNodeId);
                    Children.Add(childrenNode);
                }

                return this;
            }

            public TestNode AddChildren(params Entity[] entities)
                => AddChildren(entities.Select(EntityHierarchyNodeId.FromEntity).ToArray());

            public bool Equals(EntityHierarchyNodeId other) => other.Equals(NodeId);

            public void WriteTree(StringBuilder errorMessage, int indent, bool kindOnly = false)
            {
                errorMessage.Append(' ', indent);
                errorMessage.Append("- ");
                errorMessage.AppendLine(kindOnly ? NodeId.Kind.ToString() : NodeId.ToString());

                indent++;
                foreach (var child in Children.OrderBy(x => x.NodeId))
                {
                    child.WriteTree(errorMessage, indent, kindOnly);
                }
            }

            public bool Equals(TestNode other)
                => other != null && other.NodeId.Equals(NodeId);

            public override string ToString()
                => NodeId.ToString();
        }

        public void WriteTree(StringBuilder errorMessage, int indent, bool kindOnly = false)
            => Root.WriteTree(errorMessage, indent, kindOnly);
    }
}
