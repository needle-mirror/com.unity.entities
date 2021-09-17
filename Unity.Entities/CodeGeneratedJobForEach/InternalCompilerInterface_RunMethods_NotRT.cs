// Run methods for Non DOTS_Runtime (or IL2CPP)

#if !(UNITY_DOTSRUNTIME && !UNITY_DOTSRUNTIME_IL2CPP)

using System;
using Unity.Burst;
using Unity.Collections;
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

        public static void UnsafeRunJobEntityBatch<T>(ref T jobData, EntityQuery query,
            JobEntityBatchRunWithoutJobSystemDelegate functionPointer)
            where T : struct, IJobEntityBatch
        {
            unsafe
            {
                var myIterator = query.GetArchetypeChunkIterator();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var access = query._GetImpl()->_Access;
                try
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange++;
                    functionPointer(ref myIterator, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
                }
                finally
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange--;
                }
#else
                functionPointer(ref myIterator, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
#endif
            }
        }

        public static void UnsafeRunJobEntityBatch<T>(ref T jobData, EntityQuery query,
            FunctionPointer<JobEntityBatchRunWithoutJobSystemDelegate> functionPointer)
            where T : struct, IJobEntityBatch
        {
            unsafe
            {
                var myIterator = query.GetArchetypeChunkIterator();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var access = query._GetImpl()->_Access;
                try
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange++;
                    functionPointer.Invoke(ref myIterator, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
                }
                finally
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange--;
                }
#else
                functionPointer.Invoke(ref myIterator, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
#endif
            }
        }

        public static void UnsafeRunJobEntityBatchWithIndex<T>(ref T jobData, EntityQuery query,
            JobEntityBatchRunWithoutJobSystemDelegate functionPointer)
            where T : struct, IJobEntityBatchWithIndex
        {
            unsafe
            {
                var myIterator = query.GetArchetypeChunkIterator();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var access = query._GetImpl()->_Access;
                try
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange++;
                    functionPointer(ref myIterator, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
                }
                finally
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange--;
                }
#else
                functionPointer(ref myIterator, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
#endif
            }
        }

        public static void UnsafeRunJobEntityBatchWithIndex<T>(ref T jobData, EntityQuery query,
            FunctionPointer<JobEntityBatchRunWithoutJobSystemDelegate> functionPointer)
            where T : struct, IJobEntityBatchWithIndex
        {
            unsafe
            {
                var myIterator = query.GetArchetypeChunkIterator();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var access = query._GetImpl()->_Access;
                try
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange++;
                    functionPointer.Invoke(ref myIterator, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
                }
                finally
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange--;
                }
#else
                functionPointer.Invoke(ref myIterator, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
#endif
            }
        }

        public static void UnsafeRunJobEntityBatch<T>(ref T jobData, EntityQuery query, IntPtr limitToEntityArrayPtr, int limitToEntityArrayLength,
            JobEntityBatchRunWithoutJobSystemDelegateLimitEntities functionPointer)
            where T : struct, IJobEntityBatch
        {
            unsafe
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var access = query._GetImpl()->_Access;
                try
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange++;
                    functionPointer(ref query, limitToEntityArrayPtr, limitToEntityArrayLength, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
                }
                finally
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange--;
                }
#else
                functionPointer(ref query, limitToEntityArrayPtr, limitToEntityArrayLength, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
#endif
            }
        }

        public static void UnsafeRunJobEntityBatch<T>(ref T jobData, EntityQuery query, IntPtr limitToEntityArrayPtr, int limitToEntityArrayLength,
            FunctionPointer<JobEntityBatchRunWithoutJobSystemDelegateLimitEntities> functionPointer)
            where T : struct, IJobEntityBatch
        {
            unsafe
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var access = query._GetImpl()->_Access;
                try
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange++;
                    functionPointer.Invoke(ref query, limitToEntityArrayPtr, limitToEntityArrayLength, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
                }
                finally
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange--;
                }
#else
                functionPointer.Invoke(ref query, limitToEntityArrayPtr, limitToEntityArrayLength, (IntPtr)UnsafeUtility.AddressOf(ref jobData));
#endif
            }
        }
    }
}

#endif // #if !(UNITY_DOTSRUNTIME && !UNITY_DOTSRUNTIME_IL2CPP)
