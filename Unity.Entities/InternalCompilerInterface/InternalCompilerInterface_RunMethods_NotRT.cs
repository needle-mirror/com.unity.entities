// Run methods for Non DOTS_Runtime (or IL2CPP)

#if !(UNITY_DOTSRUNTIME && !UNITY_DOTSRUNTIME_IL2CPP)

using System;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Entities
{
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

        public static void UnsafeRunIJob<T>(ref T jobData, FunctionPointer<JobRunWithoutJobSystemDelegate> functionPointer)
            where T : struct, IJob
        {
            unsafe
            {
                functionPointer.Invoke((IntPtr)UnsafeUtility.AddressOf(ref jobData));
            }
        }

        public static void UnsafeRunJobChunk<T>(ref T jobData, EntityQuery query,
            JobChunkRunWithoutJobSystemDelegate functionPointer)
            where T : struct, IJobChunk
        {
            unsafe
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                var impl = query._GetImpl();
                impl->_Access->DependencyManager->ForEachStructuralChange.BeginIsInForEach(impl);
                functionPointer(ref query, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
                impl->_Access->DependencyManager->ForEachStructuralChange.EndIsInForEach();
#else
                functionPointer(ref query, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
#endif
            }
        }

        public static void UnsafeRunJobChunk<T>(ref T jobData, EntityQuery query,
            FunctionPointer<JobChunkRunWithoutJobSystemDelegate> functionPointer)
            where T : struct, IJobChunk
        {
            unsafe
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                var impl = query._GetImpl();
                impl->_Access->DependencyManager->ForEachStructuralChange.BeginIsInForEach(impl);
                functionPointer.Invoke(ref query, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
                impl->_Access->DependencyManager->ForEachStructuralChange.EndIsInForEach();
#else
                functionPointer.Invoke(ref query, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
#endif
            }
        }
    }
}

#endif // #if !(UNITY_DOTSRUNTIME && !UNITY_DOTSRUNTIME_IL2CPP)
