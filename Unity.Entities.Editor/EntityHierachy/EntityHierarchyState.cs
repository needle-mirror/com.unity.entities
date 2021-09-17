using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Editor.Bridge;
using Unity.Profiling;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;

namespace Unity.Entities.Editor
{
    class EntityHierarchyState : IEntityHierarchyState
    {
        static readonly string k_UnknownSceneName = L10n.Tr("<Unknown Scene>");
        static readonly string k_UnknownSubSceneName = L10n.Tr("<Unknown SubScene>");
        internal static readonly string DynamicallyLoadedSubSceneName = L10n.Tr("Dynamically loaded SubScene");

#if AGGRESSIVE_HIERARCHY_STATE_PROFILING
        // Note:
        // These markers are guarded by a compiler define because the methods they are responsible for are all trivial at
        // low volumes but expensive at high volumes. However, at the volume at which these methods become expensive, the
        // act of measuring them creates a noticeable overhead. It should therefore be a conscious choice to measure those.
        static readonly ProfilerMarker k_HasChildrenMarker = new ProfilerMarker($"{nameof(EntityHierarchyState)}.{nameof(HasChildren)}()");
        static readonly ProfilerMarker k_TryGetChildrenMarker = new ProfilerMarker($"{nameof(EntityHierarchyState)}.{nameof(TryGetChildren)}()");
        static readonly ProfilerMarker k_GetChildrenInListMarker = new ProfilerMarker($"{nameof(EntityHierarchyState)}.{nameof(GetChildren)}() [List]");
        static readonly ProfilerMarker k_GetChildrenHashSetMarker = new ProfilerMarker($"{nameof(EntityHierarchyState)}.{nameof(GetChildren)}() [HashSet]");
        static readonly ProfilerMarker k_GetParentMarker = new ProfilerMarker($"{nameof(EntityHierarchyState)}.{nameof(GetParent)}()");
        static readonly ProfilerMarker k_GetDepthMarker = new ProfilerMarker($"{nameof(EntityHierarchyState)}.{nameof(GetDepth)}()");
        static readonly ProfilerMarker k_ExistsMarker = new ProfilerMarker($"{nameof(EntityHierarchyState)}.{nameof(Exists)}()");
        static readonly ProfilerMarker k_GetNodeVersionMarker = new ProfilerMarker($"{nameof(EntityHierarchyState)}.{nameof(GetNodeVersion)}()");
        static readonly ProfilerMarker k_GetAllNodesUnorderedMarker = new ProfilerMarker($"{nameof(EntityHierarchyState)}.{nameof(GetAllNodesUnordered)}()");
#endif

        static readonly ProfilerMarker k_GetNodeNameMarker = new ProfilerMarker($"{nameof(EntityHierarchyState)}.{nameof(GetNodeName)}()");
        static readonly ProfilerMarker k_GetAllNodesOrderedMarker = new ProfilerMarker($"{nameof(EntityHierarchyState)}.{nameof(GetAllNodesOrdered)}()");
        static readonly ProfilerMarker k_FlushMarker = new ProfilerMarker($"{nameof(EntityHierarchyState)}.{nameof(FlushOperations)}()");
        static readonly ProfilerMarker k_ProcessRemovalsMarker = new ProfilerMarker("Process Removals");
        static readonly ProfilerMarker k_ProcessAdditionsMarker = new ProfilerMarker("Process Additions");
        static readonly ProfilerMarker k_ProcessMovesMarker = new ProfilerMarker("Process Moves");
        static readonly ProfilerMarker k_AssignDepthMarker = new ProfilerMarker($"{nameof(EntityHierarchyState)}.{nameof(AssignDepth)}()");
        static readonly ProfilerMarker k_PropagateDepthChangesMarker = new ProfilerMarker("Propagate Depth Changes To Children");

        readonly World m_World;

        // Note: A performance issue with iterating over NativeHashMaps with medium to large capacity (regardless of the count) forces us to use Dictionaries here.
        // This prevents burstability and jobification, but it's also a 10+x speedup in the Boids sample, when there is no changes to compute.
        // We should go back to NativeHashMap if / when this performance issue is addressed.
        readonly Dictionary<EntityHierarchyNodeId, AddOperation> m_AddedNodes = new Dictionary<EntityHierarchyNodeId, AddOperation>(Constants.EntityHierarchy.InitialCapacity.AllNodes);
        readonly Dictionary<EntityHierarchyNodeId, MoveOperation> m_MovedNodes = new Dictionary<EntityHierarchyNodeId, MoveOperation>(Constants.EntityHierarchy.InitialCapacity.AllNodes);
        readonly Dictionary<EntityHierarchyNodeId, RemoveOperation> m_RemovedNodes = new Dictionary<EntityHierarchyNodeId, RemoveOperation>(Constants.EntityHierarchy.InitialCapacity.AllNodes);

        readonly Dictionary<EntityHierarchyNodeId, uint> m_Versions = new Dictionary<EntityHierarchyNodeId, uint>(Constants.EntityHierarchy.InitialCapacity.AllNodes);
        readonly Dictionary<EntityHierarchyNodeId, FixedString64Bytes> m_CustomNodeNames = new Dictionary<EntityHierarchyNodeId, FixedString64Bytes>(Constants.EntityHierarchy.InitialCapacity.CustomNode);
        readonly Dictionary<EntityHierarchyNodeId, EntityHierarchyNodeId> m_Parents = new Dictionary<EntityHierarchyNodeId, EntityHierarchyNodeId>(Constants.EntityHierarchy.InitialCapacity.AllNodes);
        readonly Dictionary<EntityHierarchyNodeId, HashSet<EntityHierarchyNodeId>> m_Children = new Dictionary<EntityHierarchyNodeId, HashSet<EntityHierarchyNodeId>>(Constants.EntityHierarchy.InitialCapacity.AllNodes);
        readonly HashSet<EntityHierarchyNodeId> m_NodesWithChildren = new HashSet<EntityHierarchyNodeId>();
        readonly Dictionary<EntityHierarchyNodeId, int> m_Depths = new Dictionary<EntityHierarchyNodeId, int>(Constants.EntityHierarchy.InitialCapacity.AllNodes);

        // A cache of all the nodes, as a flat list, sorted like a TreeView or a ListView would display them (i.e. depth first)
        // This cache gets invalidated each time a change in topology is detected.
        readonly List<EntityHierarchyNodeId> m_AllNodesCache = new List<EntityHierarchyNodeId>(Constants.EntityHierarchy.InitialCapacity.AllNodes);

        // Helper Stack to rebuild the nodes cache, above
        readonly Stack<HashSet<EntityHierarchyNodeId>.Enumerator> m_CachedEnumeratorStack = new Stack<HashSet<EntityHierarchyNodeId>.Enumerator>(Constants.EntityHierarchy.InitialCapacity.AllNodes);

        public EntityHierarchyState(World world)
        {
            m_World = world;
            m_Children.Add(EntityHierarchyNodeId.Root, new HashSet<EntityHierarchyNodeId>());
            m_NodesWithChildren.Add(EntityHierarchyNodeId.Root);
            m_Versions.Add(EntityHierarchyNodeId.Root, 0);
            m_Depths.Add(EntityHierarchyNodeId.Root, -1);
        }

        public void Dispose()
        {
        }

        public bool HasChildren(in EntityHierarchyNodeId nodeId)
        {
#if AGGRESSIVE_HIERARCHY_STATE_PROFILING
            using var _ = k_HasChildrenMarker.Auto();
#endif
            return m_NodesWithChildren.Contains(nodeId);
        }

        public void GetChildren(in EntityHierarchyNodeId nodeId, List<EntityHierarchyNodeId> childrenList)
        {
#if AGGRESSIVE_HIERARCHY_STATE_PROFILING
            using var _ = k_GetChildrenInListMarker.Auto();
#endif
            childrenList.AddRange(m_Children[nodeId]);
        }

        public bool TryGetChildren(in EntityHierarchyNodeId nodeId, out HashSet<EntityHierarchyNodeId> children)
        {
#if AGGRESSIVE_HIERARCHY_STATE_PROFILING
            using var _ = k_TryGetChildrenMarker.Auto();
#endif
            return m_Children.TryGetValue(nodeId, out children) && children.Count > 0;
        }

        public HashSet<EntityHierarchyNodeId> GetChildren(in EntityHierarchyNodeId nodeId)
        {
#if AGGRESSIVE_HIERARCHY_STATE_PROFILING
            using var _ = k_GetChildrenHashSetMarker.Auto();
#endif
            return m_Children[nodeId];
        }

        public int GetDepth(in EntityHierarchyNodeId nodeId)
        {
#if AGGRESSIVE_HIERARCHY_STATE_PROFILING
            using var _ = k_GetDepthMarker.Auto();
#endif
            return m_Depths[nodeId];
        }

        public EntityHierarchyNodeId GetParent(in EntityHierarchyNodeId nodeId)
        {
#if AGGRESSIVE_HIERARCHY_STATE_PROFILING
            using var _ = k_GetParentMarker.Auto();
#endif
            return m_Parents[nodeId];
        }

        public bool Exists(in EntityHierarchyNodeId nodeId)
        {
#if AGGRESSIVE_HIERARCHY_STATE_PROFILING
            using var _ = k_ExistsMarker.Auto();
#endif
            return m_Versions.ContainsKey(nodeId);
        }

        public uint GetNodeVersion(in EntityHierarchyNodeId nodeId)
        {
#if AGGRESSIVE_HIERARCHY_STATE_PROFILING
            using var _ = k_GetNodeVersionMarker.Auto();
#endif
            return m_Versions[nodeId];
        }

        public IReadOnlyCollection<EntityHierarchyNodeId> GetAllNodesUnordered()
        {
#if AGGRESSIVE_HIERARCHY_STATE_PROFILING
            using var _ = k_GetAllNodesUnorderedMarker.Auto();
#endif
            return m_Versions.Keys;
        }

        public IReadOnlyList<EntityHierarchyNodeId> GetAllNodesOrdered()
        {
            using var _ = k_GetAllNodesOrderedMarker.Auto();

            if (m_AllNodesCache.Count == 0)
            {
                // The cache was either cleared or we contain no nodes.
                // Either way, try to rebuild the cache and return it.

                var nodeCount = m_Versions.Count - 1; // -1 because we're not counting the Root

                // Only resize once, if needed
                if (m_AllNodesCache.Capacity < nodeCount)
                    m_AllNodesCache.Capacity = nodeCount;

                m_AllNodesCache.InsertRange(0, GetNodesAndTheirDescendants(GetChildren(EntityHierarchyNodeId.Root)));
            }

            return m_AllNodesCache;
        }

        public string GetNodeName(in EntityHierarchyNodeId nodeId)
        {
            using var _ = k_GetNodeNameMarker.Auto();

            switch (nodeId.Kind)
            {
                case NodeKind.RootScene:
                {
                    var scene = SceneBridge.GetSceneByHandle(nodeId.Id);
                    return string.IsNullOrEmpty(scene.name) ? k_UnknownSceneName : scene.name;
                }
                case NodeKind.SubScene:
                {
                    // TODO: Copied from EntityHierarchyItemView.OnPingSubSceneAsset -> Move into some utility method
                    var subSceneObject = EditorUtility.InstanceIDToObject(nodeId.Id);
                    if (subSceneObject == null || !subSceneObject ||
                        !(subSceneObject is GameObject subSceneGameObject))
                        return k_UnknownSubSceneName;

                    var subScene = subSceneGameObject.GetComponent<SubScene>();
                    if (subScene == null || !subScene || subScene.SceneAsset == null || !subScene.SceneAsset)
                        return k_UnknownSubSceneName;

                    return string.IsNullOrEmpty(subScene.SceneAsset.name)
                        ? k_UnknownSubSceneName
                        : subScene.SceneAsset.name;
                }
                case NodeKind.DynamicSubScene:
                {
                    return m_CustomNodeNames.TryGetValue(nodeId, out var name) ? name.ToString() : DynamicallyLoadedSubSceneName;
                }
                case NodeKind.Entity:
                {
                    var entity = new Entity {Index = nodeId.Id, Version = nodeId.Version};
                    var name = m_World.EntityManager.GetName(entity);
                    return string.IsNullOrEmpty(name) ? entity.ToString() : name;
                }
                case NodeKind.Custom:
                {
                    return m_CustomNodeNames.TryGetValue(nodeId, out var name)
                        ? name.ToString()
                        : nodeId.ToString();
                }
                default:
                {
                    throw new NotSupportedException();
                }
            }
        }

        public IEnumerable<EntityHierarchyNodeId> GetAllDescendants(in EntityHierarchyNodeId node)
            => GetNodesAndTheirDescendants(GetChildren(node));

        // Returns the input nodes and all of their descendants in a depth-first manner
        IEnumerable<EntityHierarchyNodeId> GetNodesAndTheirDescendants(HashSet<EntityHierarchyNodeId> rootNodes)
        {
            if (rootNodes == null)
                yield break;

            var currentIterator = rootNodes.GetEnumerator();

            while (true)
            {
                if (!currentIterator.MoveNext())
                {
                    if (m_CachedEnumeratorStack.Count > 0)
                    {
                        currentIterator = m_CachedEnumeratorStack.Pop();
                        continue;
                    }

                    // We're at the end of the root items list.
                    break;
                }

                var current = currentIterator.Current;
                yield return current;

                if (TryGetChildren(current, out var currentChildren))
                {
                    m_CachedEnumeratorStack.Push(currentIterator);
                    currentIterator = currentChildren.GetEnumerator();
                }
            }
        }

        public void GetNodesBeingAdded(List<EntityHierarchyNodeId> nodesBeingAdded) => nodesBeingAdded.AddRange(m_AddedNodes.Keys);
        public void GetNodesBeingAdded(HashSet<EntityHierarchyNodeId> nodesBeingAdded) => nodesBeingAdded.UnionWith(m_AddedNodes.Keys);
        public void GetNodesBeingRemoved(List<EntityHierarchyNodeId> nodesBeingRemoved) => nodesBeingRemoved.AddRange(m_RemovedNodes.Keys);
        public void GetNodesBeingRemoved(HashSet<EntityHierarchyNodeId> nodesBeingRemoved) => nodesBeingRemoved.UnionWith(m_RemovedNodes.Keys);
        public void GetNodesBeingMoved(List<EntityHierarchyNodeId> nodesBeingMoved) => nodesBeingMoved.AddRange(m_MovedNodes.Keys);
        public void GetNodesBeingMoved(HashSet<EntityHierarchyNodeId> nodesBeingMoved) => nodesBeingMoved.UnionWith(m_MovedNodes.Keys);

        public bool TryGetFutureParent(in EntityHierarchyNodeId node, out EntityHierarchyNodeId nextParent)
        {
            if (m_AddedNodes.TryGetValue(node, out var added))
            {
                nextParent = added.Parent;
                return true;
            }

            if (m_MovedNodes.TryGetValue(node, out var moved))
            {
                nextParent = moved.ToNode;
                return true;
            }

            nextParent = default;
            return false;
        }

        public void RegisterAddEntityOperation(Entity entity, out EntityHierarchyNodeId generatedNode)
        {
            generatedNode = EntityHierarchyNodeId.FromEntity(entity);
            if (!m_RemovedNodes.Remove(generatedNode))
            {
                m_AddedNodes[generatedNode] = new AddOperation
                {
                    Parent = EntityHierarchyNodeId.Root,
                    Entity = entity
                };
            }
        }

        public void RegisterAddSceneOperation(int sceneId, out EntityHierarchyNodeId generatedNode)
        {
            generatedNode = EntityHierarchyNodeId.FromScene(sceneId);
            if (!m_RemovedNodes.Remove(generatedNode))
                m_AddedNodes[generatedNode] = new AddOperation { Parent = EntityHierarchyNodeId.Root };
        }

        public void RegisterAddSubSceneOperation(int subSceneId, out EntityHierarchyNodeId generatedNode)
        {
            generatedNode = EntityHierarchyNodeId.FromSubScene(subSceneId);
            if (!m_RemovedNodes.Remove(generatedNode))
                m_AddedNodes[generatedNode] = new AddOperation { Parent = EntityHierarchyNodeId.Root };
        }

        public void RegisterAddDynamicSubSceneOperation(int subSceneId, string name, out EntityHierarchyNodeId generatedNode)
        {
            generatedNode = EntityHierarchyNodeId.FromSubScene(subSceneId, true);
            if (!m_RemovedNodes.Remove(generatedNode))
                m_AddedNodes[generatedNode] = new AddOperation { Parent = EntityHierarchyNodeId.Root, CustomName = new FixedString64Bytes(name) };
        }

        public void RegisterAddCustomNodeOperation(FixedString64Bytes name, out EntityHierarchyNodeId generatedNode)
        {
            generatedNode = EntityHierarchyNodeId.FromName(name);
            if (!m_RemovedNodes.Remove(generatedNode))
            {
                m_AddedNodes[generatedNode] = new AddOperation
                {
                    Parent = EntityHierarchyNodeId.Root,
                    CustomName = name
                };
            }
        }

        public void RegisterRemoveOperation(in EntityHierarchyNodeId node)
        {
            if (!m_AddedNodes.Remove(node))
                m_RemovedNodes[node] = new RemoveOperation();
        }

        public void RegisterMoveOperation(in EntityHierarchyNodeId toNode, in EntityHierarchyNodeId node)
        {
            m_Parents.TryGetValue(node, out var previousParentNodeId);
            RegisterMoveOperationInternal(previousParentNodeId, toNode, node);
        }

        void RegisterMoveOperationInternal(in EntityHierarchyNodeId fromNode, in EntityHierarchyNodeId toNode, in EntityHierarchyNodeId node)
        {
            if (m_RemovedNodes.ContainsKey(node))
                return;

            // Move a node to root if the intended parent does not exist and will not be created in this batch
            var destinationNode = Exists(toNode) || m_AddedNodes.ContainsKey(toNode) ? toNode : EntityHierarchyNodeId.Root;

            if (m_AddedNodes.TryGetValue(node, out var addOperation))
            {
                addOperation.Parent = destinationNode;
                m_AddedNodes[node] = addOperation;
            }
            else if (m_MovedNodes.TryGetValue(node, out var moveOperation))
            {
                moveOperation.ToNode = destinationNode;
                m_MovedNodes[node] = moveOperation;
            }
            else
            {
                m_MovedNodes[node] = new MoveOperation { FromNode = fromNode, ToNode = destinationNode };
            }
        }

        public bool FlushOperations(IEntityHierarchyGroupingContext context)
        {
            using var marker = k_FlushMarker.Auto();

            // NOTE - Order matters:
            // 1.Removed -> Can cause Move operations
            // 2.Added
            // 3.Moved
            // 4.Clear operation buffers

            k_ProcessRemovalsMarker.Begin();
            var hasRemovals = m_RemovedNodes.Count > 0;
            if (hasRemovals)
            {
                foreach (var node in m_RemovedNodes.Keys)
                {
                    RemoveNode(node, context.Version);
                }

                m_RemovedNodes.Clear();
            }
            k_ProcessRemovalsMarker.End();

            k_ProcessAdditionsMarker.Begin();
            var hasAdditions = m_AddedNodes.Count > 0;
            if (hasAdditions)
            {
                foreach (var kvp in m_AddedNodes)
                {
                    var node = kvp.Key;
                    var operation = kvp.Value;
                    AddNode(operation.Parent, node, context.Version);

                    if (!operation.CustomName.IsEmpty)
                        m_CustomNodeNames[node] = operation.CustomName;
                }

                // Once all additions are accounted for, ensure all depths are calculated
                // Needs to be done after all additions, because we don't know the order in which the nodes get created
                foreach (var node in m_AddedNodes.Keys)
                {
                    AssignDepth(node);
                }

                m_AddedNodes.Clear();
            }
            k_ProcessAdditionsMarker.End();

            k_ProcessMovesMarker.Begin();
            var hasMoves = false;
            if (m_MovedNodes.Count > 0)
            {
                foreach (var kvp in m_MovedNodes)
                {
                    var node = kvp.Key;
                    var operation = kvp.Value;
                    hasMoves |= MoveNode(operation.FromNode, operation.ToNode, node, context);
                }

                m_MovedNodes.Clear();
            }

            k_ProcessMovesMarker.End();

            if (hasAdditions || hasMoves || hasRemovals)
            {
                // All items cache is no longer valid
                m_AllNodesCache.Clear();
                return true;
            }

            return false;
        }

        void AddNode(in EntityHierarchyNodeId parentNode, in EntityHierarchyNodeId newNode, uint version)
        {
            if (parentNode.Equals(default))
                throw new ArgumentException("Trying to add a new node to an invalid parent node.");

            if (newNode.Equals(default))
                throw new ArgumentException("Trying to add an invalid node to the tree.");

            m_Versions[newNode] = version;
            m_Versions[parentNode] = version;
            m_Parents[newNode] = parentNode;

            m_NodesWithChildren.Add(parentNode);

            AddChild(m_Children, parentNode, newNode);
        }

        void AssignDepth(in EntityHierarchyNodeId node)
        {
            using var marker = k_AssignDepthMarker.Auto();

            var currentNode = node;
            using var stack = PooledList<EntityHierarchyNodeId>.Make();

            while (!m_Depths.ContainsKey(currentNode))
            {
                stack.List.Add(currentNode);
                currentNode = m_Parents[currentNode];
            }

            var currentDepth = m_Depths[currentNode] + 1;

            for (var i = stack.List.Count - 1; i >= 0; --i)
            {
                m_Depths[stack.List[i]] = currentDepth++;
            }
        }

        void RemoveNode(in EntityHierarchyNodeId node, uint version)
        {
            if (node.Equals(default))
                throw new ArgumentException("Trying to remove an invalid node from the tree.");

            m_Versions.Remove(node);
            m_Depths.Remove(node);
            m_NodesWithChildren.Remove(node);

            if (node.Kind == NodeKind.Custom)
                m_CustomNodeNames.Remove(node);

            if (!m_Parents.TryGetValue(node, out var parentNodeId))
                return;

            m_Parents.Remove(node);
            m_Versions[parentNodeId] = version;

            if (RemoveChild(m_Children, parentNodeId, node) == 0)
                m_NodesWithChildren.Remove(parentNodeId);

            // Move all children of the removed node to root
            if (m_Children.TryGetValue(node, out var children))
            {
                foreach (var child in children)
                {
                    // Move to root if nothing else claimed that node
                    if (!m_MovedNodes.ContainsKey(child))
                        RegisterMoveOperationInternal(node, EntityHierarchyNodeId.Root, child);
                }
            }
        }

        bool MoveNode(in EntityHierarchyNodeId previousParent, in EntityHierarchyNodeId newParent, in EntityHierarchyNodeId node, IEntityHierarchyGroupingContext context)
        {
            if (previousParent.Equals(default))
                throw new ArgumentException("Trying to unparent from an invalid node.");

            if (newParent.Equals(default))
                throw new ArgumentException("Trying to parent to an invalid node.");

            if (node.Equals(default))
                throw new ArgumentException("Trying to add an invalid node to the tree.");

            if (previousParent.Equals(newParent))
                return false; // NOOP

            if (m_Parents[node] == newParent)
                return false; // NOOP

            if (RemoveChild(m_Children, previousParent, node) == 0)
                m_NodesWithChildren.Remove(previousParent);

            if (Exists(previousParent))
                m_Versions[previousParent] = context.Version;

            m_Parents[node] = newParent;
            AddChild(m_Children, newParent, node);

            m_Versions[newParent] = context.Version;
            m_NodesWithChildren.Add(newParent);

            var depth = m_Depths[newParent] + 1;
            m_Depths[node] = depth;

            using (k_PropagateDepthChangesMarker.Auto())
            {
                if (m_Children.TryGetValue(node, out var childrenList))
                    SetDepthRecursively(childrenList, depth + 1);
            }

            return true;
        }

        void SetDepthRecursively(HashSet<EntityHierarchyNodeId> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                m_Depths[node] = depth;
                if (m_Children.TryGetValue(node, out var childrenList))
                    SetDepthRecursively(childrenList, depth + 1);
            }
        }

        static void AddChild(Dictionary<EntityHierarchyNodeId, HashSet<EntityHierarchyNodeId>> children, in EntityHierarchyNodeId parentId, in EntityHierarchyNodeId newChild)
        {
            if (!children.TryGetValue(parentId, out var siblings))
                siblings = new HashSet<EntityHierarchyNodeId>();

            siblings.Add(newChild);
            children[parentId] = siblings;
        }

        static int RemoveChild(Dictionary<EntityHierarchyNodeId, HashSet<EntityHierarchyNodeId>> children, in EntityHierarchyNodeId parentId, in EntityHierarchyNodeId childToRemove)
        {
            if (!children.TryGetValue(parentId, out var siblings))
                return 0;

            siblings.Remove(childToRemove);
            children[parentId] = siblings;
            return siblings.Count;
        }

        struct AddOperation
        {
            public EntityHierarchyNodeId Parent;

            // Possible Payloads
            public Entity Entity;
            public FixedString64Bytes CustomName;
        }

        struct MoveOperation
        {
            public EntityHierarchyNodeId FromNode;
            public EntityHierarchyNodeId ToNode;
        }

        struct RemoveOperation
        {
        }
    }
}
