using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.Entities.Conversion.IncrementalConversionJobs
{
    [BurstCompile]
    struct CollectDependencies : IJob
    {
        [ReadOnly] public ConversionDependencies Dependencies;

        [ReadOnly] public NativeArray<int> DeletedAssets;
        [ReadOnly] public NativeArray<int> ChangedAssets;
        [ReadOnly] public NativeList<int> ChangedInstanceIds;
        [ReadOnly] public NativeArray<int> DeletedInstanceIds;

        public NativeHashSet<int> Dependents;

        public void Execute()
        {
            Dependencies.CalculateDependents(ChangedInstanceIds, Dependents);
            Dependencies.CalculateDependents(DeletedInstanceIds, Dependents);
            Dependencies.CalculateAssetDependents(ChangedAssets, Dependents);
            Dependencies.CalculateAssetDependents(DeletedAssets, Dependents);

            for (int i = 0; i < ChangedInstanceIds.Length; i++)
            {
                Dependents.Add(ChangedInstanceIds[i]);
            }
        }
    }

    [BurstCompile]
    struct ClearDependencies : IJob
    {
        public ConversionDependencies Dependencies;
        [ReadOnly]
        public NativeArray<int> DeletedInstances;
        [ReadOnly]
        public NativeHashSet<int> ChangedInstances;

        public void Execute()
        {
            Dependencies.ClearDependencies(DeletedInstances);
            Dependencies.ClearDependencies(ChangedInstances.ToNativeArray(Allocator.Temp));
        }
    }
}
