using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace Unity.Entities
{
    /// <summary>
    /// Allows you to query dependents for specific component types.
    ///
    /// ATTENTION: This is future public API.
    /// </summary>
    [BurstCompile]
    [GenerateTestsForBurstCompatibility]
    internal struct DependencyTracker : IDisposable
    {
        UnsafeParallelMultiHashMap<int, int> _dependentsByInstanceId;
        UnsafeParallelMultiHashMap<int, int> _dependenciesByInstanceId;

        internal DependencyTracker(Allocator allocator)
        {
            _dependentsByInstanceId = new UnsafeParallelMultiHashMap<int, int>(0, allocator);
            _dependenciesByInstanceId = new UnsafeParallelMultiHashMap<int, int>(0, allocator);
        }

        public void Dispose()
        {
            if (_dependentsByInstanceId.IsCreated)
                _dependentsByInstanceId.Dispose();
            if (_dependenciesByInstanceId.IsCreated)
                _dependenciesByInstanceId.Dispose();
        }

        internal void ClearDependencies(NativeArray<int> instanceIds)
        {
            for (int i = 0; i < instanceIds.Length; i++)
            {
                int id = instanceIds[i];
                var iter = _dependenciesByInstanceId.GetValuesForKey(id);
                while (iter.MoveNext())
                    _dependentsByInstanceId.Remove(iter.Current, id);
                _dependenciesByInstanceId.Remove(id);
            }
        }

        internal void AddDependency(int dependentId, int dependsOnId)
        {
            _dependentsByInstanceId.Add(dependsOnId, dependentId);
            _dependenciesByInstanceId.Add(dependentId, dependsOnId);
        }

        internal void RemapInstanceId(int previousId, int newId)
        {
            var dependents = new UnsafeList<int>(0, Allocator.Temp);
            foreach (var v in _dependentsByInstanceId.GetValuesForKey(previousId))
            {
                dependents.Add(v);
                _dependenciesByInstanceId.Remove(v, previousId);
                _dependenciesByInstanceId.Add(v, newId);
            }

            for (int i = 0; i < dependents.Length; i++)
                _dependentsByInstanceId.Add(newId, dependents[i]);
            _dependentsByInstanceId.Remove(previousId);
        }

        /// <summary>
        /// Returns whether a given instance has any dependents registered to it.
        /// </summary>
        /// <param name="instanceId">The instance to query for dependents.</param>
        /// <returns>True if there are any dependents, false otherwise.</returns>
        public bool HasDependents(int instanceId) => _dependentsByInstanceId.ContainsKey(instanceId);

        internal NativeArray<int> GetAllDependencies(Allocator allocator) => _dependentsByInstanceId.GetKeyArray(allocator);
        internal UnsafeParallelMultiHashMap<int, int>.Enumerator GetAllDependents(int instanceId) => _dependentsByInstanceId.GetValuesForKey(instanceId);
        internal bool HasDependencies() => !_dependentsByInstanceId.IsEmpty;
        internal bool HasDependencies(int instanceId) => _dependentsByInstanceId.ContainsKey(instanceId);

        internal unsafe void CalculateDependents(NativeArray<int> instanceIds, NativeParallelHashSet<int> outDependents)
        {
            var toBeProcessed = new UnsafeList<int>(0, Allocator.Temp);
            toBeProcessed.AddRange(instanceIds.GetUnsafeReadOnlyPtr(), instanceIds.Length);
            while (toBeProcessed.Length != 0)
            {
                var instance = toBeProcessed.Ptr[toBeProcessed.Length - 1];
                toBeProcessed.RemoveAtSwapBack(toBeProcessed.Length - 1);
                if (outDependents.Add(instance))
                {
                    var dependentIds = _dependentsByInstanceId.GetValuesForKey(instance);
                    while (dependentIds.MoveNext())
                    {
                        if (!outDependents.Contains(dependentIds.Current))
                            toBeProcessed.Add(dependentIds.Current);
                    }
                }
            }
        }

        /// <summary>
        /// Calculate all direct dependents for a given set of instances. Transitive dependents are not returned.
        /// </summary>
        /// <param name="instanceIds">The instance ids whose dependents should be collected</param>
        /// <param name="outDependents">The hash set to add the dependents to.</param>
        public void CalculateDirectDependents(NativeArray<int> instanceIds, NativeParallelHashSet<int> outDependents)
        {
            for (int i = 0; i < instanceIds.Length; i++)
            {
                var dependents = _dependentsByInstanceId.GetValuesForKey(instanceIds[i]);
                while (dependents.MoveNext())
                    outDependents.Add(dependents.Current);
            }
        }

        /// <summary>
        /// Calculate all direct dependents for a given set of instances. Transitive dependents are not returned.
        /// </summary>
        /// <param name="instanceIds">The instance ids whose dependents should be collected</param>
        /// <param name="outDependents">The list to add the dependents to.</param>
        public void CalculateDirectDependents(NativeArray<int> instanceIds, NativeList<int> outDependents)
        {
            for (int i = 0; i < instanceIds.Length; i++)
            {
                var dependents = _dependentsByInstanceId.GetValuesForKey(instanceIds[i]);
                while (dependents.MoveNext())
                    outDependents.Add(dependents.Current);
            }
        }

        /// <summary>
        /// Calculate all direct dependents for a given set of instances. Transitive dependents are not returned.
        /// This method is asynchronous and returns a job handle that you can use to chain further jobs.
        /// </summary>
        /// <param name="instanceIds">The instance ids whose dependents should be collected.</param>
        /// <param name="outDependents">The list to add the dependents to.</param>
        /// <param name="dependency">A JobHandle that will be treated as a dependency for all jobs scheduled by this function.</param>
        /// <returns>A JobHandle for the jobs scheduled by ths function.</returns>
        public JobHandle CalculateDirectDependentsAsync(NativeArray<int> instanceIds, NativeList<int> outDependents, JobHandle dependency=default)
        {
            return new CollectDependencies
            {
                Dependencies = this,
                OutputIds = outDependents,
                ChangedInstanceIds = instanceIds
            }.Schedule(dependency);
        }

        [BurstCompile]
        struct CollectDependencies : IJob
        {
            [ReadOnly]
            public DependencyTracker Dependencies;
            [WriteOnly]
            public NativeList<int> OutputIds;
            [ReadOnly]
            public NativeArray<int> ChangedInstanceIds;

            public void Execute()
            {
                Dependencies.CalculateDirectDependents(ChangedInstanceIds, OutputIds);
            }
        }
    }
}
