// Run methods for Non DOTS_Runtime (or IL2CPP)

#if !(UNITY_DOTSRUNTIME && !UNITY_DOTSRUNTIME_IL2CPP)

using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Entities.Internal
{
    /// <summary>
    /// This exists only for internal use and is intended to be only used by source-generated code.
    /// DO NOT USE in user code (this API will change).
    /// </summary>
    public static partial class InternalCompilerInterface
    {
        public static void UnsafeRunIJob<T>(ref T jobData, JobRunWithoutJobSystemDelegate functionPointer)
            where T : struct, IJob
        {
            unsafe
            {
                functionPointer((IntPtr)UnsafeUtility.AddressOf(ref jobData));
            }
        }

        public static void UnsafeRunJobChunk<T>(ref T jobData, EntityQuery query,
            JobChunkRunWithoutJobSystemDelegate functionPointer)
            where T : struct, IJobChunk
        {
            unsafe
            {
                functionPointer(ref query, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
            }
        }
    }
}

#endif
