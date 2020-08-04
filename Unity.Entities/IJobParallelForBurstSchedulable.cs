using System;
using Unity.Burst;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe;
using System.Diagnostics;
using Unity.Jobs;

namespace Unity.Entities
{
    [JobProducerType(typeof(IJobParallelForExtensionsBurstSchedulable.ParallelForJobStructBurstSchedulable<>))]
    internal interface IJobParallelForBurstSchedulable
    {
        void Execute(int index);
    }

    internal static class IJobParallelForExtensionsBurstSchedulable
    {
        internal struct ParallelForJobStructBurstSchedulable<T> where T : struct, IJobParallelForBurstSchedulable
        {
            internal static readonly SharedStatic<IntPtr> jobReflectionData = SharedStatic<IntPtr>.GetOrCreate<ParallelForJobStructBurstSchedulable<T>>();

            internal static void Initialize()
            {
                if (jobReflectionData.Data == IntPtr.Zero)
                {
#if UNITY_2020_2_OR_NEWER || UNITY_DOTSRUNTIME
                    jobReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(T), (ExecuteJobFunction)Execute);
#else
                    jobReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(T), JobType.ParallelFor, (ExecuteJobFunction)Execute);
#endif
                }
            }

            internal delegate void ExecuteJobFunction(ref T data, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);

            public static unsafe void Execute(ref T jobData, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex)
            {
                while (true)
                {
                    int begin;
                    int end;
                    if (!JobsUtility.GetWorkStealingRange(ref ranges, jobIndex, out begin, out end))
                        break;

                    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    JobsUtility.PatchBufferMinMaxRanges(bufferRangePatchData, UnsafeUtility.AddressOf(ref jobData), begin, end - begin);
                    #endif

                    var endThatCompilerCanSeeWillNeverChange = end;
                    for (var i = begin; i < endThatCompilerCanSeeWillNeverChange; ++i)
                        jobData.Execute(i);
                }
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckReflectionDataCorrect(IntPtr reflectionData)
        {
            if (reflectionData == IntPtr.Zero)
                throw new InvalidOperationException("Reflection data was not set up by a call to Initialize()");
        }

        unsafe internal static JobHandle Schedule<T>(this T jobData, int arrayLength, int innerloopBatchCount, JobHandle dependsOn = new JobHandle()) where T : struct, IJobParallelForBurstSchedulable
        {
            var reflectionData = ParallelForJobStructBurstSchedulable<T>.jobReflectionData.Data;
            CheckReflectionDataCorrect(reflectionData);
#if UNITY_2020_2_OR_NEWER || UNITY_DOTSRUNTIME
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), reflectionData, dependsOn, ScheduleMode.Parallel);
#else
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), reflectionData, dependsOn, ScheduleMode.Batched);
#endif
            return JobsUtility.ScheduleParallelFor(ref scheduleParams, arrayLength, innerloopBatchCount);
        }

        unsafe internal static void Run<T>(this T jobData, int arrayLength) where T : struct, IJobParallelForBurstSchedulable
        {
            var reflectionData = ParallelForJobStructBurstSchedulable<T>.jobReflectionData.Data;
            CheckReflectionDataCorrect(reflectionData);
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), reflectionData, new JobHandle(), ScheduleMode.Run);
            JobsUtility.ScheduleParallelFor(ref scheduleParams, arrayLength, arrayLength);
        }
    }
}
