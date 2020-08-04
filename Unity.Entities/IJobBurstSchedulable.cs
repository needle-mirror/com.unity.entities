using System;
using Unity.Burst;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Scripting;
using System.Diagnostics;
using Unity.Jobs;

namespace Unity.Entities
{
    [JobProducerType(typeof(IJobBurstSchedulableExtensions.JobStruct<>))]
    internal interface IJobBurstSchedulable
    {
        void Execute();
    }

    internal static class IJobBurstSchedulableExtensions
    {
        internal struct JobStruct<T> where T : struct, IJobBurstSchedulable
        {
            public static readonly SharedStatic<IntPtr> jobReflectionData = SharedStatic<IntPtr>.GetOrCreate<JobStruct<T>>();

            [Preserve]
            public static void Initialize()
            {
                if (jobReflectionData.Data == IntPtr.Zero)
                {
#if UNITY_2020_2_OR_NEWER || UNITY_DOTSRUNTIME
                    jobReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(T), (ExecuteJobFunction)Execute);
#else
                    jobReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(T), JobType.Single, (ExecuteJobFunction)Execute);
#endif
                }
            }

            public delegate void ExecuteJobFunction(ref T data, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);

            public static void Execute(ref T data, IntPtr additionalPtr, IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex)
            {
                data.Execute();
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckReflectionDataCorrect(IntPtr reflectionData)
        {
            if (reflectionData == IntPtr.Zero)
                throw new InvalidOperationException("Reflection data was not set up by an Initialize() call");
        }

        unsafe internal static JobHandle Schedule<T>(this T jobData, JobHandle dependsOn = new JobHandle()) where T : struct, IJobBurstSchedulable
        {
            var reflectionData = JobStruct<T>.jobReflectionData.Data;
            CheckReflectionDataCorrect(reflectionData);
#if UNITY_2020_2_OR_NEWER || UNITY_DOTSRUNTIME
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), reflectionData, dependsOn, ScheduleMode.Parallel);
#else
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), reflectionData, dependsOn, ScheduleMode.Batched);
#endif
            return JobsUtility.Schedule(ref scheduleParams);
        }

        unsafe internal static void Run<T>(this T jobData) where T : struct, IJobBurstSchedulable
        {
            var reflectionData = JobStruct<T>.jobReflectionData.Data;
            CheckReflectionDataCorrect(reflectionData);
            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobData), reflectionData, new JobHandle(), ScheduleMode.Run);
            JobsUtility.Schedule(ref scheduleParams);
        }
    }
}
