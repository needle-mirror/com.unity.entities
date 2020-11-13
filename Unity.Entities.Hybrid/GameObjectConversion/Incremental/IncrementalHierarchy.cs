using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.SceneManagement;

namespace Unity.Entities.Conversion
{
    /// <summary>
    /// Represents a hierarchy of GameObjects by a Burst-compatible data structure.
    ///
    /// The hierarchy is encoded by three parallel arrays storing the instance ID of a GameObject, its Transform, and
    /// the index of the GameObject's parent. Two supporting data structures maintain a mapping from instance ID to
    /// index and from index to indices of the parents of a GameObject.
    /// </summary>
    struct IncrementalHierarchy : IDisposable
    {
        /// <summary>
        /// Contains the instance ID for every element in the hierarchy. This array is parallel to the parent index
        /// array and the transform array.
        /// </summary>
        public NativeList<int> InstanceId;

        /// <summary>
        /// Contains the index of the parent in the hierarchy for every element in the hierarchy. An invalid parent is
        /// denoted by index -1. This array is parallel to the instance ID array and the transform array.
        /// </summary>
        public NativeList<int> ParentIndex;

        /// <summary>
        /// Contains a handle for the transform data of all GameObjects in this hierarchy. This array is parallel to
        /// the instance ID array and the parent index array.
        /// </summary>
        public TransformAccessArray TransformArray;

        /// <summary>
        /// Maps the index of each element to the indices of its children.
        /// </summary>
        public NativeMultiHashMap<int, int> ChildIndicesByIndex;

        /// <summary>
        /// Maps instance IDs to indices in the hierarchy.
        /// </summary>
        public NativeHashMap<int, int> IndexByInstanceId;

        public void Dispose()
        {
            if (TransformArray.isCreated)
                TransformArray.Dispose();
            if (InstanceId.IsCreated)
                InstanceId.Dispose();
            if (ParentIndex.IsCreated)
                ParentIndex.Dispose();
            if (ChildIndicesByIndex.IsCreated)
                ChildIndicesByIndex.Dispose();
            if (IndexByInstanceId.IsCreated)
                IndexByInstanceId.Dispose();
        }

        public SceneHierarchy AsReadOnly() => new SceneHierarchy(this);
    }

    static class IncrementalHierarchyFunctions
    {
        internal static bool TryAddSingle(IncrementalHierarchy hierarchy, GameObject go)
        {
            var t = go.transform;
            var p = t.parent;
            GameObject parent = null;
            if (p != null)
                parent = p.gameObject;
            return TryAddSingle(hierarchy, go, t, parent);
        }

        internal static bool TryAddSingle(IncrementalHierarchy hierarchy, GameObject go, Transform t, GameObject parent)
        {
            int id = go.GetInstanceID();
            int index = hierarchy.InstanceId.Length;
            if (!hierarchy.IndexByInstanceId.TryAdd(id, index))
                return false;
            hierarchy.InstanceId.Add(id);
            hierarchy.TransformArray.Add(t);
            if (parent != null)
            {
                var pid = parent.GetInstanceID();
                // this line assumes that parent of this GameObject has already been added.
                hierarchy.IndexByInstanceId.TryGetValue(pid, out var parentIndex);
                hierarchy.ParentIndex.Add(parentIndex);
                hierarchy.ChildIndicesByIndex.Add(parentIndex, index);
            }
            else
                hierarchy.ParentIndex.Add(-1);

            return true;
        }

        internal static void AddRecurse(IncrementalHierarchy hierarchy, GameObject go)
        {
            var t = go.transform;
            var p = t.parent;
            AddRecurse(hierarchy, go, t, p != null ? p.gameObject : null);
        }

        static void AddRecurse(IncrementalHierarchy hierarchy, GameObject go, Transform top, GameObject parent)
        {
            TryAddSingle(hierarchy, go, top, parent);
            int n = top.transform.childCount;
            for (int i = 0; i < n; i++)
            {
                var child = top.transform.GetChild(i);
                AddRecurse(hierarchy, child.gameObject, child, go);
            }
        }

        static void AddRoots(IncrementalHierarchy hierarchy, IEnumerable<GameObject> gameObjects)
        {
            foreach (var go in gameObjects)
            {
                var t = go.transform;
                AddRecurse(hierarchy, go, t, null);
            }
        }

        internal static void ChangeParents(IncrementalHierarchy hierarchy, NativeKeyValueArrays<int, int> parentChange, NativeList<int> outChangeFailed, NativeList<IncrementalConversionChanges.ParentChange> outChangeSuccessful)
        {
            var instanceIds = parentChange.Keys;
            var parentInstanceIds = parentChange.Values;
            for (int i = 0; i < instanceIds.Length; i++)
            {
                var instanceId = instanceIds[i];
                if (!hierarchy.IndexByInstanceId.TryGetValue(instanceId, out int idx))
                {
                    outChangeFailed.Add(instanceId);
                    // this case might happen when an instance was already removed
                    continue;
                }

                int oldParentIdx = hierarchy.ParentIndex[idx];
                int oldParentId = 0;
                if (oldParentIdx != -1)
                {
                    oldParentId = hierarchy.InstanceId[oldParentIdx];
                    hierarchy.ChildIndicesByIndex.Remove(oldParentIdx, idx);
                }

                int newParentId = parentInstanceIds[i];
                if (hierarchy.IndexByInstanceId.TryGetValue(newParentId, out int newParentIdx))
                {
                    hierarchy.ChildIndicesByIndex.Add(newParentIdx, idx);
                    hierarchy.ParentIndex[idx] = newParentIdx;
                    outChangeSuccessful.Add(new IncrementalConversionChanges.ParentChange
                    {
                        InstanceId = instanceId,
                        NewParentInstanceId = newParentId,
                        PreviousParentInstanceId = oldParentId
                    });
                }
                else
                {
                    if (newParentId != 0)
                        outChangeFailed.Add(instanceId);
                    else
                    {
                        outChangeSuccessful.Add(new IncrementalConversionChanges.ParentChange
                        {
                            InstanceId = instanceId,
                            NewParentInstanceId = newParentId,
                            PreviousParentInstanceId = oldParentId
                        });
                    }

                    hierarchy.ParentIndex[idx] = -1;
                }
            }
        }

        internal static void Remove(IncrementalHierarchy hierarchy, NativeArray<int> instances)
        {
            var openInstanceIds = new NativeList<int>(instances.Length, Allocator.Temp);
            openInstanceIds.AddRange(instances);

            var tmpChildren = new NativeList<int>(16, Allocator.Temp);

            // This code currently doesn't make use of the fact that we are always deleting entire subhierarchies
            while (openInstanceIds.Length > 0)
            {
                int id = openInstanceIds[openInstanceIds.Length - 1];
                openInstanceIds.Length -= 1;
                if (!hierarchy.IndexByInstanceId.TryGetValue(id, out int idx))
                    continue;

                {
                    // push children and remove children array entry
                    var iter = hierarchy.ChildIndicesByIndex.GetValuesForKey(idx);
                    while (iter.MoveNext())
                        openInstanceIds.Add(hierarchy.InstanceId[iter.Current]);
                    hierarchy.ChildIndicesByIndex.Remove(idx);
                }

                // Remove-and-swap on the arrays
                hierarchy.InstanceId.RemoveAtSwapBack(idx);
                int oldParentIdx = hierarchy.ParentIndex[idx];
                hierarchy.ParentIndex.RemoveAtSwapBack(idx);
                hierarchy.TransformArray.RemoveAtSwapBack(idx);

                // then patch up the lookup tables
                hierarchy.IndexByInstanceId.Remove(id);
                if (oldParentIdx != -1)
                    hierarchy.ChildIndicesByIndex.Remove(oldParentIdx, idx);
                int swappedIdx = hierarchy.InstanceId.Length;
                if (swappedIdx > 0 && swappedIdx != idx)
                {
                    // update index to instance id lookup
                    int swappedId = hierarchy.InstanceId[idx];
                    hierarchy.IndexByInstanceId[swappedId] = idx;

                    // update index to children lookup of parent
                    int swappedParentIdx = hierarchy.ParentIndex[idx];
                    if (swappedParentIdx != -1)
                    {
                        hierarchy.ChildIndicesByIndex.Remove(swappedParentIdx, swappedIdx);
                        hierarchy.ChildIndicesByIndex.Add(swappedParentIdx, idx);
                    }

                    // update index to children lookup of swapped index
                    var iter = hierarchy.ChildIndicesByIndex.GetValuesForKey(swappedIdx);
                    while (iter.MoveNext())
                    {
                        tmpChildren.Add(iter.Current);
                        hierarchy.ParentIndex[iter.Current] = idx;
                    }

                    hierarchy.ChildIndicesByIndex.Remove(swappedIdx);

                    for (int i = 0; i < tmpChildren.Length; i++)
                        hierarchy.ChildIndicesByIndex.Add(idx, tmpChildren[i]);
                    tmpChildren.Clear();
                }
            }
        }

#if UNITY_EDITOR
        static void ValidateThatHierarchyContainsScene(Scene scene, IncrementalHierarchy hierarchy)
        {
            Stack<GameObject> open = new Stack<GameObject>();
            foreach (var go in scene.GetRootGameObjects())
                open.Push(go);
            var childIndexCache = new List<int>();
            var childIdCache = new List<int>();
            while (open.Count > 0)
            {
                var go = open.Pop();
                var id = go.GetInstanceID();
                if (hierarchy.IndexByInstanceId.TryGetValue(id, out var idx))
                {
                    if (go.transform != hierarchy.TransformArray[idx])
                    {
                        var otherTransform = hierarchy.TransformArray[idx];
                        var otherId = otherTransform?.gameObject?.GetInstanceID() ?? 0;
                        Debug.LogError($"Object {go} ({go.GetInstanceID()}) is stored at index {idx}, but the transform stored there is {otherTransform} ({otherId})");
                    }

                    var parentIdx = hierarchy.ParentIndex[idx];
                    if (go.transform.parent == null && parentIdx != -1)
                    {
                        int parentId = hierarchy.InstanceId[parentIdx];
                        var parentObj = UnityEditor.EditorUtility.InstanceIDToObject(parentId);
                        Debug.LogError(
                            $"Object {go} ({go.GetInstanceID()}) has no parent, but in the hierarchy parent {parentObj} ({parentId}) is stored");
                    }
                    else if (go.transform.parent != null)
                    {
                        int parentId = hierarchy.InstanceId[parentIdx];
                        var parentObjFromTransform = go.transform.parent.gameObject;
                        if (parentObjFromTransform.GetInstanceID() != parentId)
                        {
                            var parentObj = UnityEditor.EditorUtility.InstanceIDToObject(parentId);
                            Debug.LogError(
                                $"Object {go} ({go.GetInstanceID()}) has parent {parentObjFromTransform} ({parentObjFromTransform.GetInstanceID()}), but in the hierarchy parent {parentObj} ({parentId} is stored)");
                        }
                    }

                    // validate children
                    childIndexCache.Clear();
                    childIdCache.Clear();
                    var childIter = hierarchy.ChildIndicesByIndex.GetValuesForKey(idx);
                    while (childIter.MoveNext())
                    {
                        childIndexCache.Add(childIter.Current);
                        childIdCache.Add(hierarchy.InstanceId[childIter.Current]);
                    }

                    if (childIndexCache.Count != go.transform.childCount)
                        Debug.LogError(
                            $"Object {go} ({go.GetInstanceID()}) has {go.transform.childCount} children, but in the hierarchy {childIndexCache.Count} children are stored");

                    for (int i = 0; i < go.transform.childCount; i++)
                    {
                        var child = go.transform.GetChild(i).gameObject;
                        var childId = child.GetInstanceID();
                        if (!childIdCache.Contains(childId))
                            Debug.LogError(
                                $"Object {go} ({go.GetInstanceID()}) has child {child} ({childId}), but in the hierarchy it is missing");
                    }
                }
                else
                    Debug.LogError($"Object {go} ({go.GetInstanceID()}) is not present in the hierarchy");

                for (int i = 0; i < go.transform.childCount; i++)
                    open.Push(go.transform.GetChild(i).gameObject);
            }
        }

        static void ValidateThatSceneContainsHierarchy(Scene scene, IncrementalHierarchy hierarchy)
        {
            if (hierarchy.InstanceId.Length == 0)
                return;
            var objects = new List<UnityEngine.Object>();
#if UNITY_2020_2_OR_NEWER
            Resources.InstanceIDToObjectList(hierarchy.InstanceId, objects);
#else
            {
                var instances = hierarchy.InstanceId;
                for (int i = 0; i < instances.Length; i++)
                    objects.Add(UnityEditor.EditorUtility.InstanceIDToObject(instances[i]));
            }
#endif
            for (int i = 0; i < objects.Count; i++)
            {
                var go = objects[i] as GameObject;
                if (go == null)
                {
                    Debug.LogError(
                        $"Object {objects[i]} ({hierarchy.InstanceId[i]}) is in the hierarchy, but doesn't exist anymore or isn't a GameObject");
                    continue;
                }
                if (go.scene != scene)
                    Debug.LogError($"Object {objects[i]} ({hierarchy.InstanceId[i]}) from scene {go.scene.name} ({go.scene.handle}) is in the hierarchy, but is not part of the conversion scene {scene.name} ({scene.handle})");
                if (hierarchy.TransformArray[i] != go.transform)
                {
                    var otherTransform = hierarchy.TransformArray[i];
                    var otherId = otherTransform?.gameObject.GetInstanceID() ?? 0;
                    Debug.LogError($"Object {go} ({go.GetInstanceID()}) is stored at index {i}, but the transform stored there is {otherTransform} ({otherId})");
                }
            }
        }

        internal static void Validate(Scene scene, IncrementalHierarchy hierarchy)
        {
            ValidateThatHierarchyContainsScene(scene, hierarchy);
            ValidateThatSceneContainsHierarchy(scene, hierarchy);
        }
#endif

        internal static void Build(GameObject[] roots, out IncrementalHierarchy hierarchy, Allocator alloc)
        {
            hierarchy = new IncrementalHierarchy
            {
                TransformArray = new TransformAccessArray(roots.Length),
                ChildIndicesByIndex = new NativeMultiHashMap<int, int>(roots.Length, alloc),
                IndexByInstanceId = new NativeHashMap<int, int>(roots.Length, alloc),
                InstanceId = new NativeList<int>(roots.Length, alloc),
                ParentIndex = new NativeList<int>(roots.Length, alloc)
            };
            AddRoots(hierarchy, roots);
        }
    }
}
