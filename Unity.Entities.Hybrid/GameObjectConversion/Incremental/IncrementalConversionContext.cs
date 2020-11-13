using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Conversion.IncrementalConversionJobs;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.Entities.Conversion
{
    [BurstCompile]
    struct IncrementalConversionContext : IDisposable
    {
        public ConversionDependencies Dependencies;
        public IncrementalHierarchy Hierarchy;
        public Scene Scene;

        public IncrementalConversionContext(bool isLiveLink)
        {
            Dependencies = new ConversionDependencies(isLiveLink);
            Hierarchy = default;
            Scene = default;
        }

        public void InitializeHierarchy(Scene scene, GameObject[] sceneRoots)
        {
            Hierarchy.Dispose();
            Scene = scene;
            IncrementalHierarchyFunctions.Build(sceneRoots, out Hierarchy, Allocator.Persistent);
        }

#if UNITY_2020_2_OR_NEWER
        static ProfilerMarker _parentChangeHierarchyMarker = new ProfilerMarker("ParentChangesInHierarchy");
        static ProfilerMarker _deleteFromHierarchyMarker = new ProfilerMarker("DeleteFromHierarchy");
        static ProfilerMarker _registerNewInstancesMarker = new ProfilerMarker("RegisterNewInstances");
        static ProfilerMarker _collectNewGameObjectsMarker = new ProfilerMarker(nameof(CollectNewGameObjects));
        static ProfilerMarker _updateHierarchyMarker = new ProfilerMarker(nameof(UpdateHierarchy));

        static readonly List<Object> ObjectCache = new List<Object>();
        static List<Object> InstanceIdToObject(NativeArray<int> instanceIds)
        {
            Resources.InstanceIDToObjectList(instanceIds, ObjectCache);
            return ObjectCache;
        }

        static void CopyToList(NativeHashSet<int> xs, NativeList<int> output)
        {
            foreach (var x in xs)
                output.Add(x);
        }

        void FilterOutValidObjects(NativeList<int> instanceIds)
        {
            var objs = InstanceIdToObject(instanceIds);
            for (int i = instanceIds.Length - 1; i >= 0; i--)
            {
                if (objs[i] == null)
                    continue;
                var go = objs[i] as GameObject;
                if (go.scene != Scene)
                    continue;
                instanceIds.RemoveAtSwapBack(i);
            }
        }

        // This cannot be a job, because we need to remove things from a TransformAccessArray. That's not possible in
        // jobs, because it needs to happen in a non-job context. Even if it is running on the main thread, it will
        // pretend it's not.
        [BurstCompile]
        unsafe struct RemoveFromHierarchy
        {
            public IncrementalHierarchy Hierarchy;
            public NativeArray<int> DeletedInstanceIds;
            public NativeArray<int> ReconvertHierarchyInstanceIds;
            public NativeList<int> RemovedInstanceIds;

            delegate void Exec(void* ptr);

            static FunctionPointer<Exec> _burstFunction;
            static readonly ProfilerMarker Marker = new ProfilerMarker(nameof(RemoveFromHierarchy));

            [BurstCompile]
            private static void Execute(void* ptr)
            {
                ref var data = ref UnsafeUtility.AsRef<RemoveFromHierarchy>(ptr);

                int capacity = data.DeletedInstanceIds.Length + data.ReconvertHierarchyInstanceIds.Length;
                var deletedInstances = new NativeHashSet<int>(capacity, Allocator.TempJob);
                {
                    data.Hierarchy.AsReadOnly().CollectHierarchyInstanceIds(data.DeletedInstanceIds, deletedInstances);
                    data.Hierarchy.AsReadOnly().CollectHierarchyInstanceIds(data.ReconvertHierarchyInstanceIds, deletedInstances);
                    var arr = deletedInstances.ToNativeArray(Allocator.Temp);
                    IncrementalHierarchyFunctions.Remove(data.Hierarchy, arr);
                    data.RemovedInstanceIds.AddRange(arr);
                }
                deletedInstances.Dispose();
            }

            public void RunWithBurst()
            {
                var data = this;
                void* ptr = UnsafeUtility.AddressOf(ref data);
                if (!_burstFunction.IsCreated)
                    _burstFunction = BurstCompiler.CompileFunctionPointer<Exec>(Execute);
                Marker.Begin();
                _burstFunction.Invoke(ptr);
                Marker.End();
            }
        }

        public void UpdateHierarchy(IncrementalConversionBatch batch, ref IncrementalConversionData outData)
        {
            _updateHierarchyMarker.Begin();
            outData.Clear();

            if (batch.DeletedAssets.Length != 0)
                outData.DeletedAssets.AddRange(batch.DeletedAssets);
            if (batch.ChangedAssets.Length != 0)
                outData.ChangedAssets.AddRange(batch.ChangedAssets);

            var requiresCleanConversion = new NativeList<int>(Allocator.Temp);
            requiresCleanConversion.AddRange(batch.ReconvertHierarchyInstanceIds);

            // Apply all parenting changes.
            if (!batch.ParentChangeInstanceIds.IsEmpty)
            {
                _parentChangeHierarchyMarker.Begin();

                var parentChanges = batch.ParentChangeInstanceIds.GetKeyValueArrays(Allocator.Temp);
                using (var changeFailed = new NativeList<int>(Allocator.TempJob))
                {
                    var changeSuccessful = outData.ParentChangeInstanceIds;
                    IncrementalHierarchyFunctions.ChangeParents(Hierarchy, parentChanges, changeFailed, changeSuccessful);
                    {
                        // When we failed to reparent something, we have to check:
                        //  - Either we failed because the parent was already deleted and this child must also be deleted,
                        //  - Or we failed because the child was never in the hierarchy to begin with, in which case we
                        //    should track it
                        var objs = InstanceIdToObject(changeFailed);
                        for (int i = 0; i < objs.Count; i++)
                        {
                            var go = objs[i] as GameObject;
                            if (go == null || go.scene != Scene)
                            {
                                // This might happen when an object is made a child of another object and then deleted.
                                outData.RemovedInstanceIds.Add(changeFailed[i]);
                            }
                            else
                            {
                                // This might happen when an object is created, a child is moved, and then the original
                                // object is deleted again.
                                requiresCleanConversion.Add(changeFailed[i]);
                            }
                        }

                        for (int i = 0; i < changeSuccessful.Length; i++)
                            outData.ReconvertHierarchyRequests.Add(changeSuccessful[i].InstanceId);
                    }

                    using (var visitedInstances = new NativeHashSet<int>(0, Allocator.TempJob))
                    {
                        Hierarchy.AsReadOnly().CollectHierarchyInstanceIds(changeFailed, visitedInstances);
                        CopyToList(visitedInstances, outData.RemovedInstanceIds);
                    }
                    IncrementalHierarchyFunctions.Remove(Hierarchy, changeFailed);
                }
                _parentChangeHierarchyMarker.End();
            }

            // Remove all deleted instances from the hierarchy, plus their children. Do the same for all instances that
            // require a clean conversion.
            bool hasExplicitDeletions = batch.DeletedInstanceIds.Length != 0;
            if (hasExplicitDeletions || batch.ReconvertHierarchyInstanceIds.Length != 0)
            {
                _deleteFromHierarchyMarker.Begin();
                new RemoveFromHierarchy
                {
                    Hierarchy = Hierarchy,
                    DeletedInstanceIds = batch.DeletedInstanceIds,
                    ReconvertHierarchyInstanceIds = batch.ReconvertHierarchyInstanceIds,
                    RemovedInstanceIds = outData.RemovedInstanceIds,
                }.RunWithBurst();

                FilterOutValidObjects(outData.RemovedInstanceIds);
                _deleteFromHierarchyMarker.End();
            }

            // Classify all clean conversions as either new or changed GameObjects.
            if (!requiresCleanConversion.IsEmpty)
            {
                var gameObjectIsNew = new HashSet<GameObject>();
                CollectNewGameObjects(Scene, ref Hierarchy, requiresCleanConversion, gameObjectIsNew);
                _registerNewInstancesMarker.Begin();
                foreach (var go in gameObjectIsNew)
                {
                    IncrementalHierarchyFunctions.TryAddSingle(Hierarchy, go);
                    outData.ChangedGameObjects.Add(go);
                    outData.ReconvertHierarchyRequests.Add(go.GetInstanceID());
                }
                _registerNewInstancesMarker.End();
            }

            // Look at all instances that have been changed. These are all instances that have changed in-place and we
            // only need to reconvert the GameObject locally (plus all dependents).
            if (batch.ChangedInstanceIds.Length != 0) {
                var objs = InstanceIdToObject(batch.ChangedInstanceIds);
                if (!hasExplicitDeletions)
                {
                    // If nothing has been deleted, we can get away with doing fewer checks.
                    outData.ReconvertSingleRequests.AddRange(batch.ChangedInstanceIds);
                    foreach (var obj in objs)
                        outData.ChangedGameObjects.Add(obj as GameObject);
                }
                else
                {
                    for (int i = 0; i < objs.Count; i++)
                    {
                        var obj = objs[i] as GameObject;

                        // Exclude destroyed objects and objects that are not in the scene anymore. This can happen when a
                        // root object is created, one of its children is dirtied, and the root is deleted or moved to
                        // another scene in a single frame.
                        if (obj == null || obj.scene != Scene)
                            continue;
                        outData.ReconvertSingleRequests.Add(batch.ChangedInstanceIds[i]);
                        outData.ChangedGameObjects.Add(obj);
                    }
                }
            }

            // If we still have dependencies on components instead of GameObjects, then they must have been added
            // before because we only had a reference to a destroyed component. There are two scenarios: Either the
            // component was on a then-destroyed GameObject, or the component itself was destroyed (but its
            // corresponding GameObject was alive).
            // In any case, we need to scan the changed GameObjects to resolve the component instances.
            if (Dependencies.HasUnresolvedComponentInstanceIds)
            {
                TryResolveComponentInstanceIds(outData.ChangedGameObjects);
            }

            outData.ReconvertHierarchyRequests.AddRange(batch.ReconvertHierarchyInstanceIds);

            if (hasExplicitDeletions)
            {
                // If there have been any deletions, this might invalidate any component because it might have been
                // deleted or it might have been moved to another scene.
                foreach (var c in batch.ChangedComponents)
                {
                    if (c == null || c.gameObject.scene != Scene)
                        continue;

                    outData.ChangedComponents.Add(c);
                }
            }
            else
            {
                foreach (var c in batch.ChangedComponents)
                    outData.ChangedComponents.Add(c);
            }

#if UNITY_EDITOR
            if (LiveConversionSettings.EnableInternalDebugValidation)
            {
                IncrementalHierarchyFunctions.Validate(Scene, Hierarchy);
            }
#endif
            _updateHierarchyMarker.End();
        }

        void TryResolveComponentInstanceIds(List<GameObject> gameObjects)
        {
            List<Component> components = new List<Component>();
            var type = typeof(Component);
            foreach (var go in gameObjects)
            {
                go.GetComponents(type, components);
                Dependencies.ResolveComponentInstanceIds(go.GetInstanceID(), components);
                components.Clear();
            }
        }

        static void CollectNewGameObjects(Scene scene, ref IncrementalHierarchy hierarchy, NativeArray<int> reconvertedObjects, HashSet<GameObject> outputObjects)
        {
            _collectNewGameObjectsMarker.Begin();
            var stack = new Stack<GameObject>();
            var objs = InstanceIdToObject(reconvertedObjects);
            for (int i = 0; i < objs.Count; i++)
            {
                var obj = objs[i] as GameObject;
                if (obj == null && objs[i] != null)
                    throw new ArgumentException($"InstanceId {reconvertedObjects[i]} does not correspond to a {nameof(GameObject)}, found {objs[i]} instead");

                // Ignore objects that where destroyed. This can happen when in a single frame an object is created,
                // a child is dirtied and added for reconversion, and then the parent is removed. In that case we don't
                // get a notification that this game object is destroyed - we only know that for the parent and the
                // easiest way to clean up the list is to ignore the destroyed objects here.
                if (obj == null)
                    continue;

                // Ignore objects that are not in the scene anymore. This can happen when a parent object is moved out
                // of the scene.
                if (obj.scene != scene)
                    continue;

                // Ignore objects that are not root objects or have a parent that wasn't already converted. In that case
                // we must have an event for the parent as well and later code assumes that we get the parent first.
                if (obj.transform.parent != null && !hierarchy.IndexByInstanceId.ContainsKey(obj.transform.parent.gameObject.GetInstanceID()))
                    continue;

                stack.Push(obj);
            }

            while (stack.Count > 0)
            {
                var top = stack.Pop();
                if (!outputObjects.Contains(top))
                {
                    outputObjects.Add(top);
                    int n = top.transform.childCount;
                    for (int c = 0; c < n; c++)
                        stack.Push(top.transform.GetChild(c).gameObject);
                }
            }

            _collectNewGameObjectsMarker.End();
        }

        public NativeHashSet<int> CollectAndClearDependencies(IncrementalConversionData conversionData)
        {
            using (var conversionRequests = new NativeList<int>(Allocator.TempJob))
            {
                conversionRequests.AddRange(conversionData.ReconvertSingleRequests);


                using (var visitedInstances = new NativeHashSet<int>(0, Allocator.TempJob))
                {
                    Hierarchy.AsReadOnly()
                        .CollectHierarchyInstanceIdsAsync(conversionData.ReconvertHierarchyRequests, visitedInstances)
                        .Complete();
                    CopyToList(visitedInstances, conversionRequests);
                }

                {
                    var componentChanges = conversionData.ChangedComponents;
                    foreach (var c in componentChanges)
                        conversionRequests.Add(c.gameObject.GetInstanceID());
                }

                var dependentInstanceIds = new NativeHashSet<int>(0, Allocator.TempJob);
                new CollectDependencies
                {
                    Dependencies = Dependencies,
                    Dependents = dependentInstanceIds,
                    ChangedAssets = conversionData.ChangedAssets,
                    DeletedAssets = conversionData.DeletedAssets,
                    ChangedInstanceIds = conversionRequests,
                    DeletedInstanceIds = conversionData.RemovedInstanceIds,
                }.Run();

                new ClearDependencies
                {
                    Dependencies = Dependencies,
                    ChangedInstances = dependentInstanceIds,
                    DeletedInstances = conversionData.RemovedInstanceIds
                }.Run();
                return dependentInstanceIds;
            }
        }
#endif

        public void Dispose()
        {
            Hierarchy.Dispose();
            Dependencies.Dispose();
        }
    }
}
