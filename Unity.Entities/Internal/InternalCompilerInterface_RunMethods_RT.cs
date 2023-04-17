// Run methods for DOTS_Runtime (and not IL2CPP)

// DOTS Runtime always compiles against the .Net Framework which will re-order structs if they contain non-blittable data (unlike mono which
// will keep structs as Layout.Sequential). However, Burst will always assume a struct layout as if Layout.Sequential was used which presents
// a data layout mismatch that must be accounted for. The DOTS Runtime job system handles this problem by marshalling jobData structs already
// but in the case where we are calling RunJob/RunJobChunk we bypass the job system data marshalling by executing the bursted static function directly
// passing the jobData as a void*. So we must account for this marshalling here. Note we only need to do this when collection checks are on since
// job structs must be non-blittable for bursting however collection checks add reference types which Burst internally treats as IntPtr which
// allows collection checks enabled code to still burst compile.

#if UNITY_DOTSRUNTIME &&!UNITY_DOTSRUNTIME_IL2CPP

using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.CodeGeneratedJobForEach;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Entities.Internal
{
    /// <summary>
    /// This exists only for internal use and is intended to be only used by source-generated code.
    /// DO NOT USE in user code (this API will change).
    /// </summary>
    public static partial class InternalCompilerInterface
    {
        [BurstCompile]
        struct JobMarshalFnLookup<T> where T : struct, IJobBase
        {
            static IntPtr MarshalToBurstFn;
            static IntPtr MarshalFromBurstFn;

            public static IntPtr GetMarshalToBurstFn()
            {
                if (MarshalToBurstFn == IntPtr.Zero)
                {
                    var job = default(T);
                    var fn = job.GetMarshalToBurstMethod_Gen();
                    // Pin the delegate so it can be used for the lifetime of the app. Never de-alloc
                    var handle = GCHandle.Alloc(fn);
                    MarshalToBurstFn = Marshal.GetFunctionPointerForDelegate(fn);
                }

                return MarshalToBurstFn;
            }

            public static IntPtr GetMarshalFromBurstFn()
            {
                if (MarshalFromBurstFn == IntPtr.Zero)
                {
                    var job = default(T);
                    var fn = job.GetMarshalFromBurstMethod_Gen();
                    // Pin the delegate so it can be used for the lifetime of the app. Never de-alloc
                    var handle = GCHandle.Alloc(fn);
                    MarshalFromBurstFn = Marshal.GetFunctionPointerForDelegate(fn);
                }

                return MarshalFromBurstFn;
            }
        }

        [BurstCompile] // This function must be burst compiled so the offset comes from a native type layout
        internal static unsafe long GetJobChunkJobDataOffset()
        {
            // This is a fake job just so we can get the offset. Technically dangerous since the offset would not work if we had
            // more than one 'T' in the wrapper, but the job system likely can't support such a thing so passing an int here is fine
            JobChunkExtensions.JobChunkWrapper<int> wrapper = default;
            return (byte*) UnsafeUtility.AddressOf(ref wrapper.JobData) - (byte*) UnsafeUtility.AddressOf(ref wrapper);
        }

        public static void RunIJob<T>(ref T jobData, JobRunWithoutJobSystemDelegate functionPointer) where T : struct, IJob, IJobBase
        {
            // Keep the API 'safe' so that only Entities.dll needs to be compiled with /unsafe
            unsafe
            {
                var managedJobDataPtr = UnsafeUtility.AddressOf(ref jobData);
                var unmanagedSize = jobData.GetUnmanagedJobSize_Gen();
                if (unmanagedSize != -1)
                {
                    const int kAlignment = 16;
                    int alignedSize = (unmanagedSize + kAlignment - 1) & ~(kAlignment - 1);
                    byte* unmanagedJobData = stackalloc byte[alignedSize];

                    // The following is needed if IJob's Producer were to contain any other fields along with a T JobData.
                    // Keeping this code in case IJob ever changes to be like that so we know how to fix what would otherwise
                    // be a difficult to diagnose crash.

                    //byte* alignedUnmanagedJobData = (byte*) ((UInt64) (unmanagedJobData + kAlignment - 1) & ~(UInt64) (kAlignment - 1));
                    //
                    //// DOTS Runtime job marshalling code assumes the job is wrapped so create the wrapper and assign the jobData
                    //
                    //IJobExtensions.JobProducer<T> jobStructData = default;
                    //jobStructData.JobData = jobData;
                    //byte* jobStructDataPtr = (byte*) UnsafeUtility.AddressOf(ref jobStructData);
                    //
                    //byte* dst = alignedUnmanagedJobData;
                    //byte* src = jobStructDataPtr;
                    //var marshalToBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalToBurstFn();
                    //
                    //UnsafeUtility.CallFunctionPtr_pp(marshalToBurstFnPtr.ToPointer(), dst, src);
                    //
                    //// In the case of JobStruct we know the jobwrapper doesn't add
                    //// anything to the jobData so just pass it along, no offset required unlike JobChunk
                    //functionPointer((IntPtr) alignedUnmanagedJobData);
                    //
                    //// Since Run can capture locals for write back, we must write back the marshalled jobData after the job executes
                    //var marshalFromBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalFromBurstFn();
                    //UnsafeUtility.CallFunctionPtr_pp(marshalFromBurstFnPtr.ToPointer(), src, dst);
                    //
                    //jobData = jobStructData.JobData;

                    byte* dstAlignedUnmanagedJobData = (byte*) ((UInt64) (unmanagedJobData + kAlignment - 1) & ~(UInt64) (kAlignment - 1));
                    byte* srcJobData = (byte*) UnsafeUtility.AddressOf(ref jobData);

                    var marshalToBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalToBurstFn();

                    UnsafeUtility.CallFunctionPtr_pp(marshalToBurstFnPtr.ToPointer(), dstAlignedUnmanagedJobData, srcJobData);

                    functionPointer((IntPtr) dstAlignedUnmanagedJobData);

                    // Since Run can capture locals for write back, we must write back the marshalled jobData after the job executes
                    var marshalFromBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalFromBurstFn();
                    UnsafeUtility.CallFunctionPtr_pp(marshalFromBurstFnPtr.ToPointer(), srcJobData, dstAlignedUnmanagedJobData);
                }
                else
                {
                    functionPointer((IntPtr) managedJobDataPtr);
                }
            }
        }

        public static void RunIJob<T>(ref T jobData, FunctionPointer<JobRunWithoutJobSystemDelegate> functionPointer) where T : struct, IJob, IJobBase
        {
            // Keep the API 'safe' so that only Entities.dll needs to be compiled with /unsafe
            unsafe
            {
                var managedJobDataPtr = UnsafeUtility.AddressOf(ref jobData);
                var unmanagedSize = jobData.GetUnmanagedJobSize_Gen();
                if (unmanagedSize != -1)
                {
                    const int kAlignment = 16;
                    int alignedSize = (unmanagedSize + kAlignment - 1) & ~(kAlignment - 1);
                    byte* unmanagedJobData = stackalloc byte[alignedSize];

                    // The following is needed if IJob's Producer were to contain any other fields along with a T JobData.
                    // Keeping this code in case IJob ever changes to be like that so we know how to fix what would otherwise
                    // be a difficult to diagnose crash.

                    //byte* alignedUnmanagedJobData = (byte*) ((UInt64) (unmanagedJobData + kAlignment - 1) & ~(UInt64) (kAlignment - 1));
                    //
                    //// DOTS Runtime job marshalling code assumes the job is wrapped so create the wrapper and assign the jobData
                    //
                    //IJobExtensions.JobProducer<T> jobStructData = default;
                    //jobStructData.JobData = jobData;
                    //byte* jobStructDataPtr = (byte*) UnsafeUtility.AddressOf(ref jobStructData);
                    //
                    //byte* dst = alignedUnmanagedJobData;
                    //byte* src = jobStructDataPtr;
                    //var marshalToBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalToBurstFn();
                    //
                    //UnsafeUtility.CallFunctionPtr_pp(marshalToBurstFnPtr.ToPointer(), dst, src);
                    //
                    //// In the case of JobStruct we know the jobwrapper doesn't add
                    //// anything to the jobData so just pass it along, no offset required unlike JobChunk
                    //functionPointer((IntPtr) alignedUnmanagedJobData);
                    //
                    //// Since Run can capture locals for write back, we must write back the marshalled jobData after the job executes
                    //var marshalFromBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalFromBurstFn();
                    //UnsafeUtility.CallFunctionPtr_pp(marshalFromBurstFnPtr.ToPointer(), src, dst);
                    //
                    //jobData = jobStructData.JobData;

                    byte* dstAlignedUnmanagedJobData = (byte*) ((UInt64) (unmanagedJobData + kAlignment - 1) & ~(UInt64) (kAlignment - 1));
                    byte* srcJobData = (byte*) UnsafeUtility.AddressOf(ref jobData);

                    var marshalToBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalToBurstFn();

                    UnsafeUtility.CallFunctionPtr_pp(marshalToBurstFnPtr.ToPointer(), dstAlignedUnmanagedJobData, srcJobData);

                    functionPointer.Invoke((IntPtr) dstAlignedUnmanagedJobData);

                    // Since Run can capture locals for write back, we must write back the marshalled jobData after the job executes
                    var marshalFromBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalFromBurstFn();
                    UnsafeUtility.CallFunctionPtr_pp(marshalFromBurstFnPtr.ToPointer(), srcJobData, dstAlignedUnmanagedJobData);                }
                else
                {
                    functionPointer.Invoke((IntPtr) managedJobDataPtr);
                }
            }
        }

        static unsafe void UnsafeRunJobChunkImpl(void* jobPtr, EntityQuery query, JobChunkRunWithoutJobSystemDelegate functionPointer)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            var impl = query._GetImpl();
            impl->_Access->DependencyManager->ForEachStructuralChange.BeginIsInForEach(impl);
            functionPointer(ref query, (IntPtr)jobPtr);
            impl->_Access->DependencyManager->ForEachStructuralChange.EndIsInForEach();
#else
            functionPointer(ref query, (IntPtr)jobPtr);
#endif
        }

        static unsafe void UnsafeRunJobChunkImpl(void* jobPtr, EntityQuery query, FunctionPointer<JobChunkRunWithoutJobSystemDelegate> functionPointer)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            var impl = query._GetImpl();
            impl->_Access->DependencyManager->ForEachStructuralChange.BeginIsInForEach(impl);
            functionPointer.Invoke(ref query, new IntPtr(jobPtr));
            impl->_Access->DependencyManager->ForEachStructuralChange.EndIsInForEach();
#else
            functionPointer.Invoke(ref query, (IntPtr)jobPtr);
#endif
        }

        public static void UnsafeRunJobChunk<T>(ref T jobData, EntityQuery query, JobChunkRunWithoutJobSystemDelegate functionPointer)
                where T : struct, IJobChunk, IJobBase
        {
            // Keep the API 'safe' so that only Entities.dll needs to be compiled with /unsafe
            unsafe
            {
                var managedJobDataPtr = UnsafeUtility.AddressOf(ref jobData);
                var unmanagedSize = jobData.GetUnmanagedJobSize_Gen();
                if (unmanagedSize != -1)
                {
                    const int kAlignment = 16;
                    int alignedSize = (unmanagedSize + kAlignment - 1) & ~(kAlignment - 1);
                    byte* unmanagedJobData = stackalloc byte[alignedSize];
                    byte* alignedUnmanagedJobData = (byte*) ((UInt64) (unmanagedJobData + kAlignment - 1) & ~(UInt64) (kAlignment - 1));

                    // DOTS Runtime job marshalling code assumes the job is wrapped so create the wrapper and assign the jobData

                    JobChunkExtensions.JobChunkWrapper<T> jobWrapper = default;
                    jobWrapper.JobData = jobData;
                    byte* jobStructDataPtr = (byte*) UnsafeUtility.AddressOf(ref jobWrapper);

                    byte* dst = alignedUnmanagedJobData;
                    byte* src = jobStructDataPtr;
                    var marshalToBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalToBurstFn();

                    UnsafeUtility.CallFunctionPtr_pp(marshalToBurstFnPtr.ToPointer(), dst, src);

                    // Since we are running inline, normally the outer job scheduling code would
                    // reference jobWrapper.Data however we can't do that since if we are in this code it means
                    // we are dealing with a job/jobwrapper that is burst compiled and is non-blittable. Thus we need to get
                    // the JobData ptr from what Burst sees which is what we do here.
                    var jobDataPtr = GetJobChunkJobDataOffset() + alignedUnmanagedJobData;
                    UnsafeRunJobChunkImpl(jobDataPtr, query, functionPointer);

                    // Since Run can capture locals for write back, we must write back the marshalled jobData after the job executes
                    var marshalFromBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalFromBurstFn();
                    UnsafeUtility.CallFunctionPtr_pp(marshalFromBurstFnPtr.ToPointer(), src, dst);

                    jobData = jobWrapper.JobData;
                }
                else
                {
                    UnsafeRunJobChunkImpl(managedJobDataPtr, query, functionPointer);
                }
            }
        }

        // Unsafe methods used to provide access for source-generated code.
        // Do not use these methods outside of source-generated code.
        // Unsafe methods to run jobs (replacing ILPP invoked methods)
        public static void UnsafeRunIJob<T>(ref T jobData, JobRunWithoutJobSystemDelegate functionPointer)
            where T : struct, IJob, IJobBase
        {
            RunIJob(ref jobData, functionPointer);
        }

        public static void UnsafeRunJobChunk<T>(ref T jobData, EntityQuery query, IntPtr limitToEntityArrayPtr, int limitToEntityArrayLength,
            JobChunkRunWithoutJobSystemDelegateLimitEntities functionPointer)
            where T : struct, IJobChunk, IJobBase
        {
            // Keep the API 'safe' so that only Entities.dll needs to be compiled with /unsafe
            unsafe
            {
                var managedJobDataPtr = UnsafeUtility.AddressOf(ref jobData);
                var unmanagedSize = jobData.GetUnmanagedJobSize_Gen();
                if (unmanagedSize != -1)
                {
                    const int kAlignment = 16;
                    int alignedSize = (unmanagedSize + kAlignment - 1) & ~(kAlignment - 1);
                    byte* unmanagedJobWrapper = stackalloc byte[alignedSize];
                    byte* alignedUnmanagedJobWrapper = (byte*) ((UInt64) (unmanagedJobWrapper + kAlignment - 1) & ~(UInt64) (kAlignment - 1));

                    // DOTS Runtime job marshalling code assumes the job is wrapped so create the wrapper and assign the jobData

                    JobChunkExtensions.JobChunkWrapper<T> jobStructData = default;
                    jobStructData.JobData = jobData;
                    byte* jobStructDataPtr = (byte*) UnsafeUtility.AddressOf(ref jobStructData);

                    byte* dst = alignedUnmanagedJobWrapper;
                    byte* src = jobStructDataPtr;
                    var marshalToBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalToBurstFn();

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                    var impl = query._GetImpl();
                    impl->_Access->DependencyManager->ForEachStructuralChange.BeginIsInForEach(impl);
#endif

                    UnsafeUtility.CallFunctionPtr_pp(marshalToBurstFnPtr.ToPointer(), dst, src);

                    // Since we are running inline, normally the outer job scheduling code would
                    // reference jobWrapper.Data however we can't do that since if we are in this code it means
                    // we are dealing with a job/jobwrapper that is burst compiled and is non-blittable. Thus we need to get
                    // the JobData ptr from what Burst sees which is what we do here.
                    var jobDataPtr = GetJobChunkJobDataOffset() + alignedUnmanagedJobWrapper;
                    functionPointer(ref query, limitToEntityArrayPtr, limitToEntityArrayLength, (IntPtr) jobDataPtr);

                    // Since Run can capture locals for write back, we must write back the marshalled jobData after the job executes
                    var marshalFromBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalFromBurstFn();
                    UnsafeUtility.CallFunctionPtr_pp(marshalFromBurstFnPtr.ToPointer(), src, dst);

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                    impl->_Access->DependencyManager->ForEachStructuralChange.EndIsInForEach();
#endif

                    jobData = jobStructData.JobData;
                }
                else
                {
                    functionPointer(ref query, limitToEntityArrayPtr, limitToEntityArrayLength, (IntPtr) managedJobDataPtr);
                }
            }
        }

        public static void UnsafeRunJobChunk<T>(ref T jobData, EntityQuery query, IntPtr limitToEntityArrayPtr, int limitToEntityArrayLength,
            FunctionPointer<JobChunkRunWithoutJobSystemDelegateLimitEntities> functionPointer)
            where T : struct, IJobChunk, IJobBase
        {
            // Keep the API 'safe' so that only Entities.dll needs to be compiled with /unsafe
            unsafe
            {
                var managedJobDataPtr = UnsafeUtility.AddressOf(ref jobData);
                var unmanagedSize = jobData.GetUnmanagedJobSize_Gen();
                if (unmanagedSize != -1)
                {
                    const int kAlignment = 16;
                    int alignedSize = (unmanagedSize + kAlignment - 1) & ~(kAlignment - 1);
                    byte* unmanagedJobWrapper = stackalloc byte[alignedSize];
                    byte* alignedUnmanagedJobWrapper = (byte*) ((UInt64) (unmanagedJobWrapper + kAlignment - 1) & ~(UInt64) (kAlignment - 1));

                    // DOTS Runtime job marshalling code assumes the job is wrapped so create the wrapper and assign the jobData

                    JobChunkExtensions.JobChunkWrapper<T> jobStructData = default;
                    jobStructData.JobData = jobData;
                    byte* jobStructDataPtr = (byte*) UnsafeUtility.AddressOf(ref jobStructData);

                    byte* dst = alignedUnmanagedJobWrapper;
                    byte* src = jobStructDataPtr;
                    var marshalToBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalToBurstFn();

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                    var impl = query._GetImpl();
                    impl->_Access->DependencyManager->ForEachStructuralChange.BeginIsInForEach(impl);
#endif
                    UnsafeUtility.CallFunctionPtr_pp(marshalToBurstFnPtr.ToPointer(), dst, src);

                    // Since we are running inline, normally the outer job scheduling code would
                    // reference jobWrapper.Data however we can't do that since if we are in this code it means
                    // we are dealing with a job/jobwrapper that is burst compiled and is non-blittable. Thus we need to get
                    // the JobData ptr from what Burst sees which is what we do here.
                    var jobDataPtr = GetJobChunkJobDataOffset() + alignedUnmanagedJobWrapper;
                    functionPointer.Invoke(ref query, limitToEntityArrayPtr, limitToEntityArrayLength, (IntPtr) jobDataPtr);

                    // Since Run can capture locals for write back, we must write back the marshalled jobData after the job executes
                    var marshalFromBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalFromBurstFn();
                    UnsafeUtility.CallFunctionPtr_pp(marshalFromBurstFnPtr.ToPointer(), src, dst);

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                    impl->_Access->DependencyManager->ForEachStructuralChange.EndIsInForEach();
#endif

                    jobData = jobStructData.JobData;
                }
                else
                {
                    functionPointer.Invoke(ref query, limitToEntityArrayPtr, limitToEntityArrayLength, (IntPtr) managedJobDataPtr);
                }
            }
        }
    }
}

#endif // #if UNITY_DOTSRUNTIME &&!UNITY_DOTSRUNTIME_IL2CPP
