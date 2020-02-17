using System;
using Unity.Burst;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    /// <summary>
    /// IJobChunk is a type of [Job](https://docs.unity3d.com/ScriptReference/Unity.Jobs.IJob.html) that iterates over
    /// a set of <see cref="ArchetypeChunk"/> instances.
    /// </summary>
    /// <remarks>
    /// Create and schedule an IJobChunk Job inside a <see cref="JobComponentSystem"/>. The Job component system calls
    /// the Execute function once for each <see cref="EntityArchetype"/> found by the <see cref="EntityQuery"/> used to
    /// schedule the Job.
    ///
    /// To pass data to the Execute function beyond the parameters of the Execute() function, add public fields to the
    /// IJobChunk struct declaration and set those fields immediately before scheduling the Job. You must pass the
    /// component type information for any components that the Job reads or writes using a field of type,
    /// <seealso cref="ArchetypeChunkComponentType{T}"/>. Get this type information by calling the appropriate
    /// <seealso cref="ComponentSystemBase.GetArchetypeChunkComponentType{T}(bool)"/> function for the type of
    /// component.
    ///
    /// For more information see [Using IJobChunk](xref:ecs-ijobchunk).
    /// </remarks>
    [JobProducerType(typeof(JobChunkExtensions.JobChunk_Process<>))]
    public interface IJobChunk
    {
        // firstEntityIndex refers to the index of the first entity in the current chunk within the EntityQuery the job was scheduled with
        // For example, if the job operates on 3 chunks with 20 entities each, then the firstEntityIndices will be [0, 20, 40] respectively
        /// <summary>
        /// Implement the Execute() function to perform a unit of work on an <see cref="ArchetypeChunk"/>.
        /// </summary>
        /// <remarks>The Job component system calls the Execute function once for each <see cref="EntityArchetype"/>
        /// found by the <see cref="EntityQuery"/> used to schedule the Job.</remarks>
        /// <param name="chunk">The current chunk.</param>
        /// <param name="chunkIndex">The index of the current chunk within the list of all chunks found by the
        /// Job's <see cref="EntityQuery"/>. Note that chunks are not processed in index order, except by chance.</param>
        /// <param name="firstEntityIndex">The index of the first entity in the current chunk within the list of all
        /// entities in all the chunks found by the Job's <see cref="EntityQuery"/>.</param>
        void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex);
    }

    /// <summary>
    /// Extensions for scheduling and running IJobChunk Jobs.
    /// </summary>
    public static class JobChunkExtensions
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [NativeContainer]
        internal struct EntitySafetyHandle
        {
            internal AtomicSafetyHandle m_Safety;
        }
#endif
        internal struct JobChunkData<T> where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
#pragma warning disable 414
            [ReadOnly] public EntitySafetyHandle safety;
#pragma warning restore
#endif
            public T Data;

            [NativeDisableContainerSafetyRestriction]
            public NativeArray<byte> PrefilterData;

            public int IsParallel;
        }
        
        /// <summary>
        /// Adds an IJobChunk instance to the Job scheduler queue for parallel execution.
        /// Note: This method is being replaced with use of ScheduleParallel to make non-sequential execution explicit.
        /// </summary>
        /// <param name="jobData">An IJobChunk instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled Jobs that could constrain this Job.
        /// A Job that writes to a component must run before other Jobs that read or write that component. Jobs that
        /// only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific IJobChunk implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle Schedule<T>(this T jobData, EntityQuery query, JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobChunk
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Batched, true);
        }
        
        /// <summary>
        /// Adds an IJobChunk instance to the Job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <param name="jobData">An IJobChunk instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled Jobs that could constrain this Job.
        /// A Job that writes to a component must run before other Jobs that read or write that component. Jobs that
        /// only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific IJobChunk implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleSingle<T>(this T jobData, EntityQuery query, JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobChunk
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Batched, false);
        }

        /// <summary>
        /// Adds an IJobChunk instance to the Job scheduler queue for parallel execution.
        /// </summary>
        /// <param name="jobData">An IJobChunk instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled Jobs that could constrain this Job.
        /// A Job that writes to a component must run before other Jobs that read or write that component. Jobs that
        /// only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific IJobChunk implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleParallel<T>(this T jobData, EntityQuery query, JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobChunk
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Batched, true);
        }

        /// <summary>
        /// Runs the Job immediately on the current thread.
        /// </summary>
        /// <param name="jobData">An IJobChunk instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific IJobChunk implementation type.</typeparam>
        public static unsafe void Run<T>(this T jobData, EntityQuery query)
            where T : struct, IJobChunk
        {
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run);
        }
        
        /// <summary>
        /// Runs the job using an ArchetypeChunkIterator instead of the jobs API.
        /// </summary>
        /// <param name="jobData">The job to execute.</param>
        /// <param name="chunkIterator">The ArchetypeChunkIterator of the EntityQuery to run over.</param>
        /// <typeparam name="T">The specific IJobChunk implementation type.</typeparam>
        public static unsafe void RunWithoutJobs<T>(this ref T jobData, ref ArchetypeChunkIterator chunkIterator)
            where T : struct, IJobChunk
        {
            var chunkCount = 0;

            while (chunkIterator.MoveNext())
            {
                var archetypeChunk = chunkIterator.CurrentArchetypeChunk;
                jobData.Execute(archetypeChunk, chunkCount, chunkIterator.CurrentChunkFirstEntityIndex);

                chunkCount++;
            }
        }

        internal static unsafe JobHandle ScheduleInternal<T>(ref T jobData, EntityQuery query, JobHandle dependsOn, ScheduleMode mode, bool isParallel = true)
            where T : struct, IJobChunk
        {
            var unfilteredChunkCount = query.CalculateChunkCountWithoutFiltering();

            var prefilterHandle = ChunkIterationUtility.PreparePrefilteredChunkLists(unfilteredChunkCount,
                query._QueryData->MatchingArchetypes, query._Filter, dependsOn, mode,
                out NativeArray<byte> prefilterData,
                out void* deferredCountData);

            JobChunkData<T> fullData = new JobChunkData<T>
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // All IJobChunk jobs have a EntityManager safety handle to ensure that BeforeStructuralChange throws an error if
                // jobs without any other safety handles are still running (haven't been synced).
                safety = new EntitySafetyHandle{m_Safety = query.SafetyHandles->GetEntityManagerSafetyHandle()},
#endif

                Data = jobData,
                PrefilterData = prefilterData,
                IsParallel = isParallel ? 1 : 0 
            };

            var scheduleParams = new JobsUtility.JobScheduleParameters(
                UnsafeUtility.AddressOf(ref fullData),
                isParallel ? JobChunk_Process<T>.InitializeParallel() : JobChunk_Process<T>.InitializeSingle(),
                prefilterHandle,
                mode);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            try
            {
#endif
                JobHandle handle = default;

                if (!isParallel)
                {
                    handle = JobsUtility.Schedule(ref scheduleParams);
                }
                else
                {
                	if (mode == ScheduleMode.Batched)
                	{
                    	handle = JobsUtility.ScheduleParallelForDeferArraySize(ref scheduleParams, 1, deferredCountData, null);
                	}
                	else
                	{
                		handle = JobsUtility.ScheduleParallelFor(ref scheduleParams, unfilteredChunkCount, unfilteredChunkCount);
					}
				}

				return prefilterData.Dispose(handle);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            }
            catch (InvalidOperationException e)
            {
                prefilterHandle.Complete();
                prefilterData.Dispose();
                throw e;
            }
#endif
        }

        internal struct JobChunk_Process<T>
            where T : struct, IJobChunk
        {
            static IntPtr jobReflectionDataParallel;
            static IntPtr jobReflectionDataSingle;

            public static IntPtr InitializeSingle()
            {
                if (jobReflectionDataSingle == IntPtr.Zero)
                    jobReflectionDataSingle = JobsUtility.CreateJobReflectionData(typeof(JobChunkData<T>), 
                        typeof(T), JobType.Single, (ExecuteJobFunction) Execute);

                return jobReflectionDataSingle;
            }
            
            public static IntPtr InitializeParallel()
            {
                if (jobReflectionDataParallel == IntPtr.Zero)
                    jobReflectionDataParallel = JobsUtility.CreateJobReflectionData(typeof(JobChunkData<T>), 
                        typeof(T), JobType.ParallelFor, (ExecuteJobFunction) Execute);

                return jobReflectionDataParallel;
            }
            
            public delegate void ExecuteJobFunction(ref JobChunkData<T> data, System.IntPtr additionalPtr, System.IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);

            public static void Execute(ref JobChunkData<T> jobData, System.IntPtr additionalPtr, System.IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex)
            {
                ExecuteInternal(ref jobData, ref ranges, jobIndex);
            }

            internal unsafe static void ExecuteInternal(ref JobChunkData<T> jobData, ref JobRanges ranges, int jobIndex)
            {
                ChunkIterationUtility.UnpackPrefilterData(jobData.PrefilterData, out var filteredChunks, out var entityIndices, out var chunkCount);

                bool isParallel = jobData.IsParallel == 1;
                while (true)
				{
                    int beginChunkIndex = 0;
                    int endChunkIndex = chunkCount;
                    
                    // If we are running the job in parallel, steal some work.
                    if (isParallel)
                    {
                        // If we have no range to steal, exit the loop.
                        if (!JobsUtility.GetWorkStealingRange(ref ranges, jobIndex, out beginChunkIndex, out endChunkIndex))
                            break;
                    }
                    
                    // Do the actual user work.
                    for (int chunkIndex = beginChunkIndex; chunkIndex < endChunkIndex; ++chunkIndex)
                    {
                        var chunk = filteredChunks[chunkIndex];
                        var entityOffset = entityIndices[chunkIndex];
                        jobData.Data.Execute(chunk, chunkIndex, entityOffset);
                    }
                    
                    // If we are not running in parallel, our job is done.
                    if (!isParallel)
                        break;
                }
            }
        }
    }
}
