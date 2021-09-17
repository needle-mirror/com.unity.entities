using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Profiling;

namespace Unity.Entities.Editor
{
    static class HierarchyItemSearchUtils
    {
        static readonly ProfilerMarker k_PreProcessTokensMarker = new ProfilerMarker($"{nameof(HierarchyItemSearchUtils)}.{nameof(PreProcessTokens)}");

        public static void PreProcessTokens(IEnumerable<string> inputTokens, NativeList<FixedString64Bytes> processedTokens)
        {
            using var marker = k_PreProcessTokensMarker.Auto();

            // Ensure a clean list
            processedTokens.Clear();

            // Extract valid tokens
            foreach (var token in inputTokens)
            {
                // Component
                if (token.StartsWith($"{Constants.EntityHierarchy.ComponentToken}:", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Strip the "" from multi-word tokens
                if (token.StartsWith("\"", StringComparison.Ordinal) && token.EndsWith("\"", StringComparison.Ordinal))
                {
                    // Discard single quote or empty double quotes
                    if (token.Length > 2)
                        processedTokens.Add(token.Substring(1, Math.Min(token.Length - 2, FixedString64Bytes.UTF8MaxLengthInBytes)).ToLowerInvariant());
                }
                else
                {
                    processedTokens.Add(token.Length > FixedString64Bytes.UTF8MaxLengthInBytes
                        ? token.Substring(0, FixedString64Bytes.UTF8MaxLengthInBytes).ToLowerInvariant()
                        : token.ToLowerInvariant());
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

        public static void FilterByName(
            IReadOnlyList<EntityHierarchyNodeId> itemsSource, NativeList<FixedString64Bytes> namesSource,
            NativeList<FixedString64Bytes> searchTokens, IList<EntityHierarchyNodeId> result)
        {
            if (searchTokens.Length == 0)
                FilterWithoutSearch(itemsSource, result);
            else
                FilterWithSearch(itemsSource, namesSource, searchTokens, result);

            // We could end up with a single dangling scene node, clean it up
            if (result.Count == 1 && (result[0].Kind & NodeKind.Scene) != 0)
                result.Clear();
        }

        static void FilterWithoutSearch(IReadOnlyList<EntityHierarchyNodeId> itemsSource, IList<EntityHierarchyNodeId> result)
        {
            // Correctly formats a list of items for a search with no terms

            var lastMatchIndex = -1;
            for (var i = 0; i < itemsSource.Count; i++)
            {
                var item = itemsSource[i];
                if ((item.Kind & NodeKind.Scene) != 0)
                {
                    if (lastMatchIndex > -1 && (result[lastMatchIndex].Kind & NodeKind.Scene) != 0)
                    {
                        // Previous node was already a scene, replace it to only keep the closest to actual data
                        result[lastMatchIndex] = item;
                    }
                    else
                    {
                        result.Add(item);
                        lastMatchIndex++;
                    }
                }
                else
                {
                    result.Add(item);
                    lastMatchIndex++;
                }
            }
        }

        static void FilterWithSearch(
            IReadOnlyList<EntityHierarchyNodeId> itemsSource, NativeList<FixedString64Bytes> namesSource,
            NativeList<FixedString64Bytes> searchTokens, IList<EntityHierarchyNodeId> result)
        {
            using var matches = SIMDSearch.GetFilteredMatches(namesSource, searchTokens);

            var lastMatchIndex = -1;
            for (var i = 0; i < itemsSource.Count; i++)
            {
                var item = itemsSource[i];

                if (matches.IsSet(i))
                {
                    result.Add(item);
                    lastMatchIndex++;
                }
                else if ((item.Kind & NodeKind.Scene) != 0)
                {
                    if (lastMatchIndex > -1 && (result[lastMatchIndex].Kind & NodeKind.Scene) != 0)
                    {
                        // Previous node was already a scene, replace it to only keep the closest to actual data
                        result[lastMatchIndex] = item;
                    }
                    else
                    {
                        result.Add(item);
                        lastMatchIndex++;
                    }
                }
            }
        }
    }
}
