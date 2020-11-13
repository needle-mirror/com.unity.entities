using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Entities.Conversion;

namespace Unity.Transforms.Hybrid.Tests
{
    public class IncrementalConversionTests
    {
        private IncrementalHierarchy m_Hierarchy;

        [TearDown]
        public void TearDown()
        {
            m_Hierarchy.Dispose();
        }

        static IEnumerable<int> GetInstanceIds(GameObject go)
        {
            var open = new Stack<Transform>();
            open.Push(go.transform);
            while (open.Count > 0)
            {
                var top = open.Pop();
                yield return top.gameObject.GetInstanceID();
                int n = top.childCount;
                for (int i = 0; i < n; i++)
                    open.Push(top.GetChild(i));
            }
        }

        [Test]
        public void Hierarchy_CollectChildIndices_WithHierarchy()
        {
            var go = new GameObject("root");
            new GameObject("c1").transform.SetParent(go.transform);
            var c2 = new GameObject("c2");
            c2.transform.SetParent(go.transform);
            new GameObject("c3").transform.SetParent(go.transform);
            new GameObject("c21").transform.SetParent(c2.transform);
            new GameObject("c22").transform.SetParent(c2.transform);
            IncrementalHierarchyFunctions.Build(new [] {go}, out m_Hierarchy, Allocator.TempJob);

            var changedIds = new NativeList<int>(1, Allocator.TempJob);
            changedIds.Add(go.GetInstanceID());

            var visitedIndices = new NativeHashMap<int, bool>(6, Allocator.TempJob);


            try
            {
                m_Hierarchy.AsReadOnly().CollectHierarchyInstanceIdsAndIndicesAsync(changedIds, visitedIndices).Complete();
                Assert.AreEqual(6, changedIds.Length);
                Assert.AreEqual(6, visitedIndices.Count());
                foreach (var id in GetInstanceIds(go))
                {
                    Assert.IsTrue(changedIds.Contains(id));
                    var index = m_Hierarchy.IndexByInstanceId[id];
                    bool success = visitedIndices.TryGetValue(index, out bool isInOriginalList);
                    Assert.IsTrue(success);
                    Assert.AreEqual(id == go.GetInstanceID(), isInOriginalList);
                }
            }
            finally
            {
                changedIds.Dispose();
                visitedIndices.Dispose();
                GameObject.DestroyImmediate(go);
            }
        }
    }
}
