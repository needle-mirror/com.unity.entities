using System;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Profiling;

namespace Unity.Entities.Editor
{
    class HierarchyNamesCache : IDisposable
    {
        static readonly ProfilerMarker k_RebuildCacheMarker = new ProfilerMarker($"{nameof(HierarchyNamesCache)}.{nameof(Rebuild)}()");
        static readonly ParallelOptions k_ParallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        NativeList<FixedString64Bytes> m_Names = new NativeList<FixedString64Bytes>(Constants.EntityHierarchy.InitialCapacity.AllNodes, Allocator.Persistent);
        public NativeList<FixedString64Bytes> Names => m_Names;

        public void Rebuild(IEntityHierarchyState state)
        {
            // How many nodes are required to start threading the names creation
            // Locally, we start seeing gains on the threaded version at around 500 items
            const int threadingThreshold = 500;

            using var marker = k_RebuildCacheMarker.Auto();

            // The names need to be in the same order they will be displayed to allow for positional comparison during search
            var nodes = state.GetAllNodesOrdered();

            var nodesCount = nodes.Count;
            m_Names.ResizeUninitialized(nodesCount);

            if (nodesCount >= threadingThreshold)
            {
                Parallel.For(0, nodesCount, k_ParallelOptions,  i =>
                {
                    if ((nodes[i].Kind & NodeKind.Scene) != 0)
                    {
                        // We don't need to cache scene names. Not doing it saves time twice:
                        // - No need to retrieve the name, which is expensive
                        // - Avoid any comparison while performing the string search, since scenes are special cases for search
                        m_Names[i] = default;
                    }
                    else
                    {
                        var name = state.GetNodeName(nodes[i]);

                        if (name.Length > FixedString64Bytes.utf8MaxLengthInBytes)
                            name = name.Substring(0, FixedString64Bytes.utf8MaxLengthInBytes);

                        m_Names[i] = name.ToLowerInvariant();
                    }
                });
            }
            else
            {
                for (var i = 0; i < nodes.Count; ++i)
                {
                    if ((nodes[i].Kind & NodeKind.Scene) != 0)
                    {
                        // We don't need to cache scene names. Not doing it saves time twice:
                        // - No need to retrieve the name, which is expensive
                        // - Avoid any comparison while performing the string search, since scenes are special cases for search
                        m_Names[i] = default;
                    }
                    else
                    {
                        var name = state.GetNodeName(nodes[i]);

                        if (name.Length > FixedString64Bytes.utf8MaxLengthInBytes)
                            name = name.Substring(0, FixedString64Bytes.utf8MaxLengthInBytes);

                        m_Names[i] = name.ToLowerInvariant();
                    }
                }
            }
        }

        public void Dispose()
        {
            m_Names.Dispose();
        }
    }
}
