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

namespace Unity.Entities
{
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

        // Note we need the three variants below to be non-generic which is why they are separate functions.
        // The non-generic requirement is so we can burst the functions
        [BurstCompile] // This function must be burst compiled so the offset comes from a native type layout
        internal static unsafe long GetJobChunkJobDataOffset()
        {
            // This is a fake job just so we can get the offset. Technically dangerous since the offset would not work if we had
            // more than one 'T' in the wrapper, but the job system likely can't support such a thing so passing an int here is fine
            JobChunkExtensions.JobChunkWrapper<int> wrapper = default;
            return (byte*) UnsafeUtility.AddressOf(ref wrapper.JobData) - (byte*) UnsafeUtility.AddressOf(ref wrapper);
        }

        [BurstCompile] // This function must be burst compiled so the offset comes from a native type layout
        internal static unsafe long GetJobEntityBatchJobDataOffset()
        {
            // This is a fake job just so we can get the offset. Technically dangerous since the offset would not work if we had
            // more than one 'T' in the wrapper, but the job system likely can't support such a thing so passing an int here is fine
            JobEntityBatchExtensions.JobEntityBatchWrapper<int> wrapper = default;
            return (byte*) UnsafeUtility.AddressOf(ref wrapper.JobData) - (byte*) UnsafeUtility.AddressOf(ref wrapper);
        }

        [BurstCompile] // This function must be burst compiled so the offset comes from a native type layout
        internal static unsafe long GetJobEntityBatchWithIndexJobDataOffset()
        {
            // This is a fake job just so we can get the offset. Technically dangerous since the offset would not work if we had
            // more than one 'T' in the wrapper, but the job system likely can't support such a thing so passing an int here is fine
            JobEntityBatchIndexExtensions.JobEntityBatchIndexWrapper<int> wrapper = default;
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

        static unsafe void UnsafeRunJobEntityBatchImpl(void* jobPtr, EntityQuery query, JobEntityBatchRunWithoutJobSystemDelegate functionPointer)
        {
            var myIterator = query.GetArchetypeChunkIterator();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var access = query._GetImpl()->_Access;
            try
            {
                access->DependencyManager->IsInForEachDisallowStructuralChange++;
                functionPointer(ref myIterator, new IntPtr(jobPtr));
            }
            finally
            {
                access->DependencyManager->IsInForEachDisallowStructuralChange--;
            }
#else
            functionPointer(ref myIterator, (IntPtr)jobPtr);
#endif
        }

        static unsafe void UnsafeRunJobEntityBatchImpl(void* jobPtr, EntityQuery query, FunctionPointer<JobEntityBatchRunWithoutJobSystemDelegate> functionPointer)
        {
            var myIterator = query.GetArchetypeChunkIterator();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var access = query._GetImpl()->_Access;
            try
            {
                access->DependencyManager->IsInForEachDisallowStructuralChange++;
                functionPointer.Invoke(ref myIterator, new IntPtr(jobPtr));
            }
            finally
            {
                access->DependencyManager->IsInForEachDisallowStructuralChange--;
            }
#else
            functionPointer.Invoke(ref myIterator, (IntPtr)jobPtr);
#endif
        }

        public static void UnsafeRunJobEntityBatch<T>(ref T jobData, EntityQuery query, JobEntityBatchRunWithoutJobSystemDelegate functionPointer)
                where T : struct, IJobEntityBatch, IJobBase
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

                    JobEntityBatchExtensions.JobEntityBatchWrapper<T> jobWrapper = default;
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
                    var jobDataPtr = GetJobEntityBatchJobDataOffset() + alignedUnmanagedJobData;
                    UnsafeRunJobEntityBatchImpl(jobDataPtr, query, functionPointer);

                    // Since Run can capture locals for write back, we must write back the marshalled jobData after the job executes
                    var marshalFromBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalFromBurstFn();
                    UnsafeUtility.CallFunctionPtr_pp(marshalFromBurstFnPtr.ToPointer(), src, dst);

                    jobData = jobWrapper.JobData;
                }
                else
                {
                    UnsafeRunJobEntityBatchImpl(managedJobDataPtr, query, functionPointer);
                }
            }
        }

        public static void UnsafeRunJobEntityBatch<T>(ref T jobData, EntityQuery query,
            FunctionPointer<JobEntityBatchRunWithoutJobSystemDelegate> functionPointer)
                where T : struct, IJobEntityBatch, IJobBase
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

                    JobEntityBatchExtensions.JobEntityBatchWrapper<T> jobWrapper = default;
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
                    var jobDataPtr = GetJobEntityBatchJobDataOffset() + alignedUnmanagedJobData;
                    UnsafeRunJobEntityBatchImpl(jobDataPtr, query, functionPointer);

                    // Since Run can capture locals for write back, we must write back the marshalled jobData after the job executes
                    var marshalFromBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalFromBurstFn();
                    UnsafeUtility.CallFunctionPtr_pp(marshalFromBurstFnPtr.ToPointer(), src, dst);

                    jobData = jobWrapper.JobData;
                }
                else
                {
                    UnsafeRunJobEntityBatchImpl(managedJobDataPtr, query, functionPointer);
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

        // Unsafe methods used to provide access for source-generated code.
        // Do not use these methods outside of source-generated code.
        // Unsafe methods to run jobs (replacing ILPP invoked methods)
        public static void UnsafeRunIJob<T>(ref T jobData, FunctionPointer<JobRunWithoutJobSystemDelegate> functionPointer)
            where T : struct, IJob, IJobBase
        {
            RunIJob(ref jobData, functionPointer);
        }

        public static void UnsafeRunJobEntityBatchWithIndex<T>(ref T jobData, EntityQuery query, JobEntityBatchRunWithoutJobSystemDelegate functionPointer)
            where T : struct, IJobEntityBatchWithIndex, IJobBase
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
                    byte* unmanagedJobwrapper = stackalloc byte[alignedSize];
                    byte* alignedUnmanagedJobwrapper = (byte*) ((UInt64) (unmanagedJobwrapper + kAlignment - 1) & ~(UInt64) (kAlignment - 1));

                    // DOTS Runtime job marshalling code assumes the job is wrapped so create the wrapper and assign the jobData

                    JobEntityBatchIndexExtensions.JobEntityBatchIndexWrapper<T> JobWrapper = default;
                    JobWrapper.JobData = jobData;
                    byte* jobStructDataPtr = (byte*) UnsafeUtility.AddressOf(ref JobWrapper);

                    byte* dst = alignedUnmanagedJobwrapper;
                    byte* src = jobStructDataPtr;
                    var marshalToBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalToBurstFn();

                    UnsafeUtility.CallFunctionPtr_pp(marshalToBurstFnPtr.ToPointer(), dst, src);

                    // Since we are running inline, normally the outer job scheduling code would
                    // reference jobWrapper.Data however we can't do that since if we are in this code it means
                    // we are dealing with a job/jobwrapper that is burst compiled and is non-blittable. Thus we need to get
                    // the JobData ptr from what Burst sees which is what we do here.
                    var jobDataPtr = GetJobEntityBatchWithIndexJobDataOffset() + alignedUnmanagedJobwrapper;
                    UnsafeRunJobEntityBatchImpl(jobDataPtr, query, functionPointer);

                    // Since Run can capture locals for write back, we must write back the marshalled jobData after the job executes
                    var marshalFromBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalFromBurstFn();
                    UnsafeUtility.CallFunctionPtr_pp(marshalFromBurstFnPtr.ToPointer(), src, dst);

                    jobData = JobWrapper.JobData;
                }
                else
                {
                    UnsafeRunJobEntityBatchImpl(managedJobDataPtr, query, functionPointer);
                }
            }
        }

        public static void UnsafeRunJobEntityBatchWithIndex<T>(ref T jobData, EntityQuery query,
            FunctionPointer<JobEntityBatchRunWithoutJobSystemDelegate> functionPointer)
            where T : struct, IJobEntityBatchWithIndex, IJobBase
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
                    byte* unmanagedJobwrapper = stackalloc byte[alignedSize];
                    byte* alignedUnmanagedJobwrapper = (byte*) ((UInt64) (unmanagedJobwrapper + kAlignment - 1) & ~(UInt64) (kAlignment - 1));

                    // DOTS Runtime job marshalling code assumes the job is wrapped so create the wrapper and assign the jobData

                    JobEntityBatchIndexExtensions.JobEntityBatchIndexWrapper<T> JobWrapper = default;
                    JobWrapper.JobData = jobData;
                    byte* jobStructDataPtr = (byte*) UnsafeUtility.AddressOf(ref JobWrapper);

                    byte* dst = alignedUnmanagedJobwrapper;
                    byte* src = jobStructDataPtr;
                    var marshalToBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalToBurstFn();

                    UnsafeUtility.CallFunctionPtr_pp(marshalToBurstFnPtr.ToPointer(), dst, src);

                    // Since we are running inline, normally the outer job scheduling code would
                    // reference jobWrapper.Data however we can't do that since if we are in this code it means
                    // we are dealing with a job/jobwrapper that is burst compiled and is non-blittable. Thus we need to get
                    // the JobData ptr from what Burst sees which is what we do here.
                    var jobDataPtr = GetJobEntityBatchWithIndexJobDataOffset() + alignedUnmanagedJobwrapper;
                    UnsafeRunJobEntityBatchImpl(jobDataPtr, query, functionPointer);

                    // Since Run can capture locals for write back, we must write back the marshalled jobData after the job executes
                    var marshalFromBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalFromBurstFn();
                    UnsafeUtility.CallFunctionPtr_pp(marshalFromBurstFnPtr.ToPointer(), src, dst);

                    jobData = JobWrapper.JobData;
                }
                else
                {
                    UnsafeRunJobEntityBatchImpl(managedJobDataPtr, query, functionPointer);
                }
            }
        }

        public static void UnsafeRunJobEntityBatch<T>(ref T jobData, EntityQuery query, IntPtr limitToEntityArrayPtr, int limitToEntityArrayLength,
            JobEntityBatchRunWithoutJobSystemDelegateLimitEntities functionPointer)
            where T : struct, IJobEntityBatch, IJobBase
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

                    JobEntityBatchExtensions.JobEntityBatchWrapper<T> jobStructData = default;
                    jobStructData.JobData = jobData;
                    byte* jobStructDataPtr = (byte*) UnsafeUtility.AddressOf(ref jobStructData);

                    byte* dst = alignedUnmanagedJobWrapper;
                    byte* src = jobStructDataPtr;
                    var marshalToBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalToBurstFn();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    var access = query._GetImpl()->_Access;
                    access->DependencyManager->IsInForEachDisallowStructuralChange++;
#endif
                    try
                    {
                        UnsafeUtility.CallFunctionPtr_pp(marshalToBurstFnPtr.ToPointer(), dst, src);

                        // Since we are running inline, normally the outer job scheduling code would
                        // reference jobWrapper.Data however we can't do that since if we are in this code it means
                        // we are dealing with a job/jobwrapper that is burst compiled and is non-blittable. Thus we need to get
                        // the JobData ptr from what Burst sees which is what we do here.
                        var jobDataPtr = GetJobEntityBatchJobDataOffset() + alignedUnmanagedJobWrapper;
                        functionPointer(ref query, limitToEntityArrayPtr, limitToEntityArrayLength, (IntPtr) jobDataPtr);

                        // Since Run can capture locals for write back, we must write back the marshalled jobData after the job executes
                        var marshalFromBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalFromBurstFn();
                        UnsafeUtility.CallFunctionPtr_pp(marshalFromBurstFnPtr.ToPointer(), src, dst);
                    }
                    finally
                    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        access->DependencyManager->IsInForEachDisallowStructuralChange--;
#endif
                    }

                    jobData = jobStructData.JobData;
                }
                else
                {
                    functionPointer(ref query, limitToEntityArrayPtr, limitToEntityArrayLength, (IntPtr) managedJobDataPtr);
                }
            }
        }

        public static void UnsafeRunJobEntityBatch<T>(ref T jobData, EntityQuery query, IntPtr limitToEntityArrayPtr, int limitToEntityArrayLength,
            FunctionPointer<JobEntityBatchRunWithoutJobSystemDelegateLimitEntities> functionPointer)
            where T : struct, IJobEntityBatch, IJobBase
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

                    JobEntityBatchExtensions.JobEntityBatchWrapper<T> jobStructData = default;
                    jobStructData.JobData = jobData;
                    byte* jobStructDataPtr = (byte*) UnsafeUtility.AddressOf(ref jobStructData);

                    byte* dst = alignedUnmanagedJobWrapper;
                    byte* src = jobStructDataPtr;
                    var marshalToBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalToBurstFn();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    var access = query._GetImpl()->_Access;
                    access->DependencyManager->IsInForEachDisallowStructuralChange++;
#endif
                    try
                    {
                        UnsafeUtility.CallFunctionPtr_pp(marshalToBurstFnPtr.ToPointer(), dst, src);

                        // Since we are running inline, normally the outer job scheduling code would
                        // reference jobWrapper.Data however we can't do that since if we are in this code it means
                        // we are dealing with a job/jobwrapper that is burst compiled and is non-blittable. Thus we need to get
                        // the JobData ptr from what Burst sees which is what we do here.
                        var jobDataPtr = GetJobEntityBatchJobDataOffset() + alignedUnmanagedJobWrapper;
                        functionPointer.Invoke(ref query, limitToEntityArrayPtr, limitToEntityArrayLength, (IntPtr) jobDataPtr);

                        // Since Run can capture locals for write back, we must write back the marshalled jobData after the job executes
                        var marshalFromBurstFnPtr = JobMarshalFnLookup<T>.GetMarshalFromBurstFn();
                        UnsafeUtility.CallFunctionPtr_pp(marshalFromBurstFnPtr.ToPointer(), src, dst);
                    }
                    finally
                    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        access->DependencyManager->IsInForEachDisallowStructuralChange--;
#endif
                    }

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
