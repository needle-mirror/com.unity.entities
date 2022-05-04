using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Properties;

namespace Unity.Entities.Editor
{
    [GeneratePropertyBag]
    public class HierarchyNodesState
    {
        [CreateProperty] internal HashSet<HierarchyNodeHandle> Expanded = new HashSet<HierarchyNodeHandle>();
    }
    
    /// <summary>
    /// The <see cref="HierarchyNodes"/> class represents a virtualized list over the linearly packed hierarchy. It provides linear access to all 'expanded/visible' nodes.
    /// This can be used directly by a 'ListView' for the virtualization.
    /// </summary>
    class HierarchyNodes : IList, IDisposable
    {
        /// <summary>
        /// The set of packed node data this list is exposing. This contains 'ALL' nodes in the hierarchy packed for efficient access.
        /// </summary>
        HierarchyNodeStore.Immutable m_ImmutableNodes;

        /// <summary>
        /// The set of 'visible' nodes based on the expanded state. These indices map back to the <see cref="m_ImmutableNodes"/>.
        /// </summary>
        NativeList<int> m_Nodes;

        /// <summary>
        /// The <see cref="m_Nodes"/> index for a given <see cref="HierarchyNodeHandle"/>. This is used to find the virtualized index for a specific node.
        /// </summary>
        NativeList<int> m_IndexByNode;

        /// <summary>
        /// The set of expanded states for the nodes.
        /// </summary>
        NativeParallelHashSet<HierarchyNodeHandle> m_ExpandedHashSet;

        /// <summary>
        /// The currently applied search query.
        /// </summary>
        HierarchyFilter m_Filter;

        /// <summary>
        /// The serialized state which is managed externally.
        /// </summary>
        HierarchyNodesState m_SerializableState = new HierarchyNodesState();

        int m_ImmutableNodeChangeVersion;
        int m_ChangeVersion;

        bool m_Rebuild;

        /// <summary>
        /// Returns the number of 'visible' nodes. This is the virtualized count.
        /// </summary>
        public int Count => m_Nodes.Length;

        /// <summary>
        /// Returns the current change version for the nodes.
        /// </summary>
        public int ChangeVersion => m_ChangeVersion;

        /// <summary>
        /// Returns true if the nodes have been changed since the given version.
        /// </summary>
        public bool IsChanged(int changeVersion) => m_ChangeVersion != changeVersion;

        /// <summary>
        /// Returns true if the nodes are filtered by a search query.
        /// </summary>
        public bool HasFilter() => null != m_Filter;

        /// <summary>
        /// Returns the active search filter; if any.
        /// </summary>
        public HierarchyFilter GetFilter() => m_Filter;

        object IList.this[int index]
        {
            get => GetBuffer().GetNode(m_Nodes[index]);
            set => throw new NotImplementedException();
        }

        /// <summary>
        /// Returns the immutable node data for the given <see cref="HierarchyNodeHandle"/>.
        /// </summary>
        /// <param name="handle">The handle to get the data for.</param>
        public HierarchyNode.Immutable this[HierarchyNodeHandle handle]
            => GetBuffer().GetNode(handle);

        /// <summary>
        /// Returns the immutable node data for the given index.
        /// </summary>
        /// <param name="index">The index to get the data for.</param>
        public HierarchyNode.Immutable this[int index]
            => GetBuffer().GetNode(m_Nodes[index]);

        /// <summary>
        /// Gets the immutable node buffer storing the node data.
        /// </summary>
        HierarchyNodeStore.Immutable GetBuffer()
        {
            if (!m_ImmutableNodes.IsCreated)
                throw new InvalidOperationException($"The packed node buffer has not been initialized. {nameof(HierarchyNodes)}.{nameof(Refresh)} must be called to update the internal state.");

            if (m_ImmutableNodes.ChangeVersion != m_ImmutableNodeChangeVersion)
                throw new InvalidOperationException($"The packed node buffer has been changed. {nameof(HierarchyNodes)}.{nameof(Refresh)} must be called to update the internal state.");

            return m_ImmutableNodes;
        }

        internal HierarchyNodes(Allocator allocator)
        {
            m_ChangeVersion = 0;
            m_ImmutableNodeChangeVersion = 0;
            m_Nodes = new NativeList<int>(allocator);
            m_IndexByNode = new NativeList<int>(allocator);
            m_ExpandedHashSet = new NativeParallelHashSet<HierarchyNodeHandle>(16, allocator);
        }

        public void Dispose()
        {
            m_Nodes.Dispose();
            m_IndexByNode.Dispose();
            m_ExpandedHashSet.Dispose();
        }

        /// <summary>
        /// Clears the internal state for re-use.
        /// </summary>
        public void Clear()
        {
            m_ImmutableNodes = default;
            m_ChangeVersion = 0;
            m_ImmutableNodeChangeVersion = 0;
            m_Nodes.Clear();
            m_IndexByNode.Clear();
            m_ExpandedHashSet.Clear();
        }

        public HierarchyNodesState GetSerializableState()
        {
            return m_SerializableState;
        }

        public void SetSerializableState(HierarchyNodesState state)
        {
            m_SerializableState = state ?? throw new ArgumentNullException(nameof(state));
            m_ExpandedHashSet.Clear();

            // Copy this data to a native acceleration structure to be available in bursted jobs.
            foreach (var node in state.Expanded)
                m_ExpandedHashSet.Add(node);

            m_Rebuild = true;
        }

        public bool Exists(HierarchyNodeHandle handle)
            => m_ImmutableNodes.Exists(handle);

        /// <summary>
        /// Returns <see langword="true"/> if the given <see cref="HierarchyNodeHandle"/> is expanded in the hierarchy.
        /// </summary>
        /// <param name="handle">The handle to get the expanded state for.</param>
        /// <returns><see langword="true"/> if the node is expanded; <see langword="false"/> otherwise.</returns>
        public bool IsExpanded(HierarchyNodeHandle handle)
        {
            switch (handle.Kind)
            {
                case NodeKind.Scene:
                case NodeKind.RootScene:
                case NodeKind.SubScene:
                case NodeKind.DynamicSubScene:
                    return !m_ExpandedHashSet.Contains(handle);
                default:
                    return m_ExpandedHashSet.Contains(handle);
            }
        }

        public void SetExpanded(HierarchyNodeHandle handle, bool expanded)
        {
            switch (handle.Kind)
            {
                case NodeKind.Scene:
                case NodeKind.RootScene:
                case NodeKind.SubScene:
                case NodeKind.DynamicSubScene:
                {
                    if (!expanded)
                    {
                        m_ExpandedHashSet.Add(handle);
                        m_SerializableState.Expanded.Add(handle);
                    }
                    else
                    {
                        m_ExpandedHashSet.Remove(handle);
                        m_SerializableState.Expanded.Remove(handle);
                    }

                    break;
                }
                default:
                {
                    if (expanded)
                    {
                        m_ExpandedHashSet.Add(handle);
                        m_SerializableState.Expanded.Add(handle);
                    }
                    else
                    {
                        m_ExpandedHashSet.Remove(handle);
                        m_SerializableState.Expanded.Remove(handle);
                    }

                    break;
                }
            }
            
            // Update both the serialized state and the acceleration structure.
            m_Rebuild = true;
        }

        /// <summary>
        /// Sets all ancestors to an expanded state. This is used for selecting a collapsed node deep in the hierarchy.
        /// </summary>
        /// <param name="handle">The node for which all ancestors will be expanded.</param>
        public void SetAncestorsExpanded(HierarchyNodeHandle handle)
        {
            if (handle.Kind == NodeKind.Root || !m_ImmutableNodes.IsCreated || m_ImmutableNodes.ChangeVersion != m_ImmutableNodeChangeVersion)
                return;
            
            var index = m_ImmutableNodes.IndexOf(handle);

            if (index == -1)
                return;
            
            var node = m_ImmutableNodes[index];

            var rebuild = false;

            for (;;)
            {
                index += node.ParentOffset;

                if (index <= 1)
                    break;
                
                node = m_ImmutableNodes[index];
                rebuild |= !IsExpanded(node.Handle);
                SetExpanded(node.Handle, true);
            }
            
            // Update both the serialized state and the acceleration structure.
            if (rebuild)
            {
                m_Rebuild = true;
            }
        }

        /// <summary>
        /// Returns the packed index for the given <see cref="HierarchyNodeHandle"/>.
        /// </summary>
        /// <param name="handle">The handle to get the index for.</param>
        /// <returns>The packed index.</returns>
        public int IndexOf(HierarchyNodeHandle handle)
        {
            if (handle.Kind == NodeKind.Root || !m_ImmutableNodes.IsCreated || m_ImmutableNodes.ChangeVersion != m_ImmutableNodeChangeVersion)
                return -1;

            var immutableIndex = m_ImmutableNodes.IndexOf(handle);

            if (immutableIndex == -1 || immutableIndex >= m_ImmutableNodes.Count)
                return -1;

            return m_IndexByNode[immutableIndex];
        }

        internal void SetFilter(HierarchyFilter query)
        {
            if (m_Filter == query)
                return;

            m_Filter = query;
            m_Rebuild = true;
        }
        
        /// <summary>
        /// Updates the <see cref="HierarchyNodes"/> to the latest packed data. This method will early out if no work is needed and can be called 
        /// </summary>
        /// <remarks>
        /// @TODO convert this to an enumerator which can be time-sliced.
        /// </remarks>
        internal void Refresh(HierarchyNodeStore.Immutable immutable)
        {
            if (immutable.ChangeVersion == 0)
                return;
            
            if (null != m_Filter)
            {
                // Filtering is applied. We should be showing all nodes which match the specified filter. This ignores the expanded states.
                //
                // We must rebuild if:
                //  1) The packed nodes are newer than our current node version.
                //  2) Our underlying node store has be repacked.
                //  3) The search filter has changed.
                if (m_ImmutableNodeChangeVersion != immutable.ChangeVersion || m_Rebuild)
                {
                    m_ImmutableNodes = immutable;
                    m_ImmutableNodeChangeVersion = immutable.ChangeVersion;
                    m_Rebuild = false;
                    m_ChangeVersion++;

                    using var mask = m_Filter.Apply(m_ImmutableNodes, Allocator.TempJob);
                    
                    new BuildFilteredNodes
                    {
                        Hierarchy = m_ImmutableNodes,
                        Filter = mask,
                        Nodes = m_Nodes,
                        IndexByNode = m_IndexByNode
                    }.Run();
                }
            }
            else
            {
                // No filtering is applied. We should be viewing all 'expanded' nodes.
                //
                // We must rebuild if:
                //  1) The packed nodes are newer than our current node version.
                //  2) Our underlying node store has be repacked.
                //  3) The expanded/collapsed state has changed for one or more nodes.
                if (m_ImmutableNodeChangeVersion != immutable.ChangeVersion || m_Rebuild)
                {
                    m_ImmutableNodes = immutable;
                    m_ImmutableNodeChangeVersion = immutable.ChangeVersion;
                    m_Rebuild = false;
                    m_ChangeVersion++;

                    new BuildExpandedNodes
                    {
                        Hierarchy = m_ImmutableNodes,
                        Expanded = m_ExpandedHashSet,
                        Nodes = m_Nodes,
                        IndexByNode = m_IndexByNode
                    }.Run();
                }
            }
        }

        [BurstCompile]
        struct BuildExpandedNodes : IJob
        {
            [ReadOnly] public HierarchyNodeStore.Immutable Hierarchy;
            [ReadOnly] public NativeParallelHashSet<HierarchyNodeHandle> Expanded;
            
            public NativeList<int> Nodes;
            public NativeList<int> IndexByNode;

            public void Execute()
            {
                // Skip the root.
                var count = Hierarchy.Count;
                var readIndex = 1;
                var writeIndex = 0;

                Nodes.ResizeUninitialized(count);
                IndexByNode.ResizeUninitialized(count);

                for (var i = 0; i < IndexByNode.Length; i++)
                    IndexByNode[i] = -1;

                for (; readIndex < count;)
                {
                    var node = Hierarchy[readIndex];

                    // Hide empty Unity scene nodes. These are not useful in an ECS context.
                    // NOTE: This should be moved elsewhere when gameObject support is introduced.
                    if (node.Handle.Kind == NodeKind.Scene && node.ChildCount == 0)
                    {
                        readIndex += node.NextSiblingOffset; 
                        continue;
                    }

                    // Write the node index. 
                    Nodes[writeIndex] = readIndex;
                    IndexByNode[readIndex] = writeIndex;

                    // Skip children if the node is not expanded; otherwise continue to the next node
                    if (node.ChildCount != 0 && !IsExpanded(node.Handle))
                        readIndex += node.NextSiblingOffset; 
                    else
                        readIndex++;

                    writeIndex++;
                }

                Nodes.Length = writeIndex;
            }

            bool IsExpanded(HierarchyNodeHandle handle)
            {
                switch (handle.Kind)
                {
                    case NodeKind.Scene:
                    case NodeKind.RootScene:
                    case NodeKind.SubScene:
                    case NodeKind.DynamicSubScene:
                        return !Expanded.Contains(handle);
                    default:
                        return Expanded.Contains(handle);
                }
            }
        }

        [BurstCompile]
        struct BuildFilteredNodes : IJob
        {
            [ReadOnly] public HierarchyNodeStore.Immutable Hierarchy;
            [ReadOnly] public NativeBitArray Filter;
            
            public NativeList<int> Nodes;
            public NativeList<int> IndexByNode;
            
            public void Execute()
            {
                // Skip the root.
                var readIndex = 1;
                var writeIndex = 0;

                Nodes.ResizeUninitialized(Filter.Length);
                IndexByNode.ResizeUninitialized(Filter.Length);

                for (var i = 0; i < IndexByNode.Length; i++)
                    IndexByNode[i] = -1;
                
                for (; readIndex < Hierarchy.Count; readIndex++)
                {
                    if (!Filter.IsSet(readIndex)) 
                        continue;
                    
                    IndexByNode[readIndex] = writeIndex;
                    Nodes[writeIndex] = readIndex;

                    writeIndex++;
                }

                Nodes.Length = writeIndex;
            }
        }
        
        bool ICollection.IsSynchronized => throw new NotImplementedException();
        bool IList.IsFixedSize => throw new NotImplementedException();
        bool IList.IsReadOnly => throw new NotImplementedException();
        object ICollection.SyncRoot => throw new NotImplementedException();
        IEnumerator IEnumerable.GetEnumerator() => throw new NotImplementedException();
        void ICollection.CopyTo(Array array, int index) => throw new NotImplementedException();
        int IList.Add(object value) => throw new NotImplementedException();
        bool IList.Contains(object value) => throw new NotImplementedException();
        int IList.IndexOf(object value) => throw new NotImplementedException();
        void IList.Insert(int index, object value) => throw new NotImplementedException();
        void IList.Remove(object value) => throw new NotImplementedException();
        void IList.RemoveAt(int index) => throw new NotImplementedException();
    }
}