using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Profiling;

namespace Unity.Entities.Editor
{
    /// <summary>
    /// Represents a search query object which can be 'applied' to a set of nodes. This uses the shared state from <see cref="Hierarchy.HierarchySearch"/>.
    /// </summary>
    class HierarchyFilter : IDisposable
    {
        static readonly ProfilerMarker k_PreProcessTokensMarker = new ProfilerMarker($"{nameof(HierarchyFilter)}.{nameof(PreProcessTokens)}");

        /// <summary>
        /// The search backend for the hierarchy.
        /// </summary>
        readonly HierarchySearch m_HierarchySearch;

        /// <summary>
        /// The processed result of a search query. This is used to drive the underlying change tracking/differs.
        /// </summary>
        readonly HierarchyQueryBuilder.Result m_QueryResult;

        /// <summary>
        /// The pre-processed tokens from the search query. This is used to drive the bursted searching algorithms.
        /// </summary>
        NativeList<FixedString64Bytes> m_Tokens;

        readonly int m_EntityIndex;

        /// <summary>
        /// Gets the current active <see cref="FilterQueryDesc"/>.
        /// </summary>
        public EntityQueryDesc FilterQueryDesc => m_QueryResult.EntityQueryDesc;
        
        /// <summary>
        /// Returns the pre-processed tokens.
        /// </summary>
        public NativeArray<FixedString64Bytes> Tokens => m_Tokens;

        /// <summary>
        /// Gets a value indicating if the filter is valid or not.
        /// </summary>
        public bool IsValid => m_QueryResult.IsValid;

        /// <summary>
        /// Returns the name of the invalid component type encountered during filtering.
        /// </summary>
        public string ErrorComponentType => m_QueryResult.ErrorComponentType;

        internal HierarchyFilter(HierarchySearch hierarchySearch, string searchString, ICollection<string> tokens, Allocator allocator)
        {
            m_HierarchySearch = hierarchySearch;
            m_QueryResult = HierarchyQueryBuilder.BuildQuery(searchString);
            m_Tokens = new NativeList<FixedString64Bytes>(tokens?.Count ?? 1, allocator);
            m_EntityIndex = -1;

            if (null != tokens)
                PreProcessTokens(tokens, m_Tokens, ref m_EntityIndex);
        }

        public void Dispose()
        {
            m_Tokens.Dispose();
        }

        static void PreProcessTokens(IEnumerable<string> inputTokens, NativeList<FixedString64Bytes> processedTokens, ref int targetIndex)
        {
            using var marker = k_PreProcessTokensMarker.Auto();

            // Ensure a clean list
            processedTokens.Clear();

            // Extract valid tokens
            foreach (var token in inputTokens)
            {
                // Component
                if (token.StartsWith($"{Constants.Hierarchy.ComponentToken}:", StringComparison.OrdinalIgnoreCase))
                    continue;
                
                if (token.StartsWith($"{Constants.Hierarchy.IndexToken}:", StringComparison.OrdinalIgnoreCase))
                {
                    var startIndex = Constants.Hierarchy.IndexToken.Length + 1;
                    var length = token.Length - startIndex;

                    if (length == 0)
                        continue;

                    var value = token.Substring(startIndex, token.Length - startIndex);

                    if (int.TryParse(value, out targetIndex))
                        continue;
                }

                // Strip the "" from multi-word tokens
                if (token.StartsWith("\"", StringComparison.Ordinal) && token.EndsWith("\"", StringComparison.Ordinal))
                {
                    // Discard single quote or empty double quotes
                    if (token.Length > 2)
                        processedTokens.Add(token.Substring(1, Math.Min(token.Length - 2, FixedString64Bytes.UTF8MaxLengthInBytes)).ToLowerInvariant());
                }
                else
                {
                    var truncatedToken = token.Length > FixedString64Bytes.UTF8MaxLengthInBytes ? token.Substring(0, FixedString64Bytes.UTF8MaxLengthInBytes) : token;

                    // Use the fixed string to-lower variant to be compatible with filtering. 
                    processedTokens.Add(FixedStringUtility.ToLower(truncatedToken));
                }
            }

            if (processedTokens.Length <= 1)
                return;

            // Factor out repeats by removing short strings contained in longer strings
            // e.g.: [GameObject, Object, a, z] -> [GameObject, z]
            for (var i = processedTokens.Length - 1; i >= 0; --i)
            {
                var testString = processedTokens[i];
                var match = false;
                for (var j = 0; !match && j < processedTokens.Length; ++j)
                {
                    if (j != i)
                    {
                        var token = processedTokens[j];
                        match |= token.IndexOf(testString) != -1;
                    }
                }

                if (match)
                {
                    // Swap with last and remove
                    processedTokens[i] = processedTokens[processedTokens.Length - 1];
                    processedTokens.RemoveAt(processedTokens.Length - 1);
                }
            }

            // Sort filters by length (longest first)
            // We are doing this because filters are additive and therefore longer filters are less likely to match making it faster to discard candidates
            processedTokens.Sort(new NameLengthComparer());
        }

        struct NameLengthComparer : IComparer<FixedString64Bytes>
        {
            public int Compare(FixedString64Bytes lhs, FixedString64Bytes rhs)
            {
                if (lhs.utf8LengthInBytes == rhs.utf8LengthInBytes)
                    return 0;
                return lhs.utf8LengthInBytes < rhs.utf8LengthInBytes ? 1 : -1;
            }
        }

        /// <summary>
        /// @TODO convert this to an enumerator which can be time-sliced.
        /// </summary>
        public NativeBitArray Apply(HierarchyNodeStore.Immutable nodes, Allocator allocator)
        {
            var mask = new NativeBitArray(nodes.Count, allocator);
            mask.SetBits(0, true, mask.Length);

            m_HierarchySearch.ApplyEntityIndexFilter(nodes, m_EntityIndex, mask);
            m_HierarchySearch.ApplyEntityQueryFilter(nodes, m_QueryResult.EntityQueryDesc, mask);
            m_HierarchySearch.ApplyNameFilter(nodes, m_Tokens, mask);
            m_HierarchySearch.ApplyIncludeSubSceneFilter(nodes, mask);

            return mask;
        }
    }
}