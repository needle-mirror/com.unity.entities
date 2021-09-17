using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;

namespace Unity.Entities
{
    /// <summary>
    ///     Assign Value to each element of NativeArray
    /// </summary>
    /// <typeparam name="T">Type of element in NativeArray</typeparam>
    [BurstCompile]
    public struct MemsetNativeArray<T> : IJobParallelFor
        where T : struct
    {
        public NativeArray<T> Source;
        public T Value;

        public void Execute(int index)
        {
            Source[index] = Value;
        }
    }
}
