using System;
using Unity.Assertions;
using Unity.Burst;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using Assert = UnityEngine.Assertions.Assert;

namespace Unity.Entities
{
    /// <summary>
    /// IJobEntityBatchWithIndex is a variant of [IJobEntityBatch] that provides an additional indexOfFirstEntityInQuery parameter, which
    /// provides a per-batch index that is the aggregate of all previous batch counts.
    ///
    /// [IJob]: https://docs.unity3d.com/ScriptReference/Unity.Jobs.IJob.html
    /// [chunk]: xref:ecs-concepts#chunk
    /// </summary>
    /// <remarks>
    /// Schedule or run an IJobEntityBatchWithIndex job inside the <see cref="SystemBase.OnUpdate"/> function of a
    /// <see cref="SystemBase"/> implementation. When the system schedules or runs an IJobEntityBatchWithIndex job, it uses
    /// the specified <see cref="EntityQuery"/> to select a set of [chunks]. These selected chunks are divided into
    /// batches of entities. A batch is a contiguous set of entities, always stored in the same chunk. The job
    /// struct's `Execute` function is called for each batch.
    ///
    /// When you schedule or run the job with one of the following methods:
    /// * <see cref="JobEntityBatchIndexExtensions.Schedule{T}"/>,
    /// * <see cref="JobEntityBatchIndexExtensions.ScheduleParallel{T}(T, EntityQuery, int, JobHandle)"/>,
    /// * or <see cref="JobEntityBatchIndexExtensions.Run{T}(T, EntityQuery)"/>
    ///
    /// all the entities of each chunk are processed as
    /// a single batch. The <see cref="ArchetypeChunk"/> object passed to the `Execute` function of your job struct provides access
    /// to the components of all the entities in the chunk.
    ///
    /// Use <see cref="JobEntityBatchIndexExtensions.ScheduleParallel{T}(T, EntityQuery, int, JobHandle)"/> to divide
    /// each chunk selected by your query into (approximately) equal batches of contiguous entities. For example,
    /// if you use a batch count of two, one batch provides access to the first half of the component arrays in a chunk and the other
    /// provides access to the second half. When you use batching, the <see cref="ArchetypeChunk"/> object only
    /// provides access to the components in the current batch of entities -- not those of all entities in a chunk.
    ///
    /// In general, processing whole chunks at a time (setting batch count to one) is the most efficient. However, in cases
    /// where the algorithm itself is relatively expensive for each entity, executing smaller batches in parallel can provide
    /// better overall performance, especially when the entities are contained in a small number of chunks. As always, you
    /// should profile your job to find the best arrangement for your specific application.
    ///
    /// To pass data to your Execute function (beyond the `Execute` parameters), add public fields to the IJobEntityBatchWithIndex
    /// struct declaration and set those fields immediately before scheduling the job. You must always pass the
    /// component type information for any components that the job reads or writes using a field of type,
    /// <seealso cref="ComponentTypeHandle{T}"/>. Get this type information by calling the appropriate
    /// <seealso cref="ComponentSystemBase.GetComponentTypeHandle{T}"/> function for the type of
    /// component.
    ///
    /// For more information see [Using IJobEntityBatch].
    /// <example>
    /// <code source="../DocCodeSamples.Tests/ChunkIterationJob.cs" region="basic-ijobentitybatch" title="IJobEntityBatch Example"/>
    /// </example>
    ///
    /// [Using IJobEntityBatch]: xref:ecs-ijobentitybatch
    /// [chunks]: xref:ecs-concepts#chunk
    /// </remarks>
    /// <seealso cref="JobEntityBatchIndexExtensions"/>
    /// <seealso cref="IJobEntityBatch"/>
    [JobProducerType(typeof(JobEntityBatchIndexExtensions.JobEntityBatchIndexProducer<>))]
    public interface IJobEntityBatchWithIndex
    {
        /// <summary>
        /// Implement the `Execute` function to perform a unit of work on an <see cref="ArchetypeChunk"/> representing
        /// a contiguous batch of entities within a chunk.
        /// </summary>
        /// <remarks>
        /// The chunks selected by the <see cref="EntityQuery"/> used to schedule the job are the input to your `Execute`
        /// function. If you use <see cref="JobEntityBatchExtensions.ScheduleParallel{T}(T, EntityQuery, int, JobHandle)"/>
        /// to schedule the job, the entities in each matching chunk are partitioned into contiguous batches based on the
        /// `batchesInChunk` parameter, and the `Execute` function is called once for each batch. When you use one of the
        /// other scheduling or run methods, the `Execute` function is called once per matching chunk (in other words, the
        /// batch count is one).
        /// </remarks>
        /// <param name="batchInChunk">An object providing access to a batch of entities within a chunk.</param>
        /// <param name="batchIndex">The index of the current batch within the list of all batches in all chunks found by the
        /// job's <see cref="EntityQuery"/>. If the batch count is one, this list contains one entry for each selected chunk; if
        /// the batch count is two, the list contains two entries per chunk; and so on. Note that batches are not processed in
        /// index order, except by chance.</param>
        /// <param name="indexOfFirstEntityInQuery">The index of the first entity in the current chunk within the list of all
        /// entities in all the chunks found by the Job's <see cref="EntityQuery"/>.</param>
        void Execute(ArchetypeChunk batchInChunk, int batchIndex, int indexOfFirstEntityInQuery);
    }

    /// <summary>
    /// Extensions for scheduling and running <see cref="IJobEntityBatchWithIndex"/> jobs.
    /// </summary>
    public static class JobEntityBatchIndexExtensions
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [NativeContainer]
        internal struct EntitySafetyHandle
        {
            internal AtomicSafetyHandle m_Safety;
        }
#endif
        internal struct JobEntityBatchIndexWrapper<T> where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
#pragma warning disable 414
            [ReadOnly] public EntitySafetyHandle safety;
#pragma warning restore
#endif
            public T JobData;

            [DeallocateOnJobCompletion]
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<byte> PrefilterData;

            public int JobsPerChunk;
            public int IsParallel;
        }


#if UNITY_2020_2_OR_NEWER && !UNITY_DOTSRUNTIME
        /// <summary>
        /// This method is only to be called by automatically generated setup code.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static void EarlyJobInit<T>()
            where T : struct, IJobEntityBatchWithIndex
        {
            JobEntityBatchIndexProducer<T>.CreateReflectionData();
        }
#endif
        /// <summary>
        /// Adds an <see cref="IJobEntityBatchWithIndex"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each matching chunk as a single batch. All chunks execute
        /// sequentially.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle Schedule<T>(
            this T jobData,
            EntityQuery query,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
        {
#if UNITY_2020_2_OR_NEWER
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, 1, false);
#else
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Batched, 1, false);
#endif
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatchWithIndex"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the entity array. All batches are processed
        /// sequentially.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be processed.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle Schedule<T>(
            this T jobData,
            EntityQuery query,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
        {
#if UNITY_2020_2_OR_NEWER
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, 1, false, limitToEntityArray);
#else
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Batched, 1, false, limitToEntityArray);
#endif
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatchWithIndex"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each matching chunk as a single batch. Each
        /// chunk can execute in parallel. This scheduling method is equivalent to calling
        /// <see cref="JobEntityBatchIndexExtensions.ScheduleParallel{T}(T, EntityQuery, int, JobHandle)"/>
        /// with the batchesPerChunk parameter set to 1.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleParallel<T>(
            this T jobData,
            EntityQuery query,
            int batchesPerChunk = 1,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
        {
#if UNITY_2020_2_OR_NEWER
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, batchesPerChunk, true);
#else
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Batched, batchesPerChunk, true);
#endif
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatchWithIndex"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the entity array. Each
        /// batch can execute in parallel.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be processed.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleParallel<T>(
            this T jobData,
            EntityQuery query,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
        {
#if UNITY_2020_2_OR_NEWER
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, 1, true, limitToEntityArray);
#else
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Batched, 1, true, limitToEntityArray);
#endif
        }

        /// <summary>
        /// Runs the job immediately on the current thread.
        /// </summary>
        /// <remarks>This scheduling variant processes each matching chunk as a single batch. All chunks execute
        /// sequentially on the current thread.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        public static unsafe void Run<T>(this T jobData, EntityQuery query)
            where T : struct, IJobEntityBatchWithIndex
        {
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, 1, false);
        }

        /// <summary>
        /// Runs the job immediately on the current thread.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the input array. All
        /// batches are processed sequentially on the current thread.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be processed.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        public static unsafe void Run<T>(this T jobData, EntityQuery query, NativeArray<Entity> limitToEntityArray)
            where T : struct, IJobEntityBatchWithIndex
        {
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, 1, false, limitToEntityArray);
        }

        public static unsafe void RunWithoutJobsInternal<T>(ref T jobData, ref ArchetypeChunkIterator chunkIterator)
            where T : struct, IJobEntityBatchWithIndex
        {
            var chunkCount = 0;
            var entitiesSeen = 0;
            while (chunkIterator.MoveNext())
            {
                var archetypeChunk = chunkIterator.CurrentArchetypeChunk;
                jobData.Execute(archetypeChunk, chunkCount, entitiesSeen);
                chunkCount++;
                entitiesSeen += archetypeChunk.Count;
            }
        }

        /// <summary>
        /// Runs the job without using the jobs API.
        /// </summary>
        /// <param name="jobData">The job to execute.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific IJobEntityBatch implementation type.</typeparam>
        public static unsafe void RunWithoutJobs<T>(ref T jobData, EntityQuery query)
            where T : struct, IJobEntityBatchWithIndex
        {
            var myIterator = query.GetArchetypeChunkIterator();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var access = query._GetImpl()->_Access;
            try
            {
                access->DependencyManager->IsInForEachDisallowStructuralChange++;
                RunWithoutJobsInternal(ref jobData, ref myIterator);
            }
            finally
            {
                access->DependencyManager->IsInForEachDisallowStructuralChange--;
            }
#else
            RunWithoutJobsInternal(ref jobData, ref myIterator);
#endif
        }

        internal static unsafe JobHandle ScheduleInternal<T>(
            ref T jobData,
            EntityQuery query,
            JobHandle dependsOn,
            ScheduleMode mode,
            int batchesPerChunk,
            bool isParallel = true,
            NativeArray<Entity> limitToEntityArray = default(NativeArray<Entity>))
            where T : struct, IJobEntityBatchWithIndex
        {
            var queryImpl = query._GetImpl();
            var queryData = queryImpl->_QueryData;

            var batchCount = 0;
            var filteredChunkCount = 0;
            var useEntityArray = limitToEntityArray.IsCreated;
            var prebuiltBatchList = new UnsafeList(Allocator.TempJob);
            var perBatchMatchingArchetypeIndex = new UnsafeIntList(0, Allocator.TempJob);

            if (useEntityArray)
            {
                // Forces the creation of an EntityQueryMask, which is necessary to filter batches.
                var access = queryImpl->_Access;
                access->EntityQueryManager->GetEntityQueryMask(queryData, access->EntityComponentStore);

                ChunkIterationUtility.FindBatchesForEntityArrayWithQuery(
                    queryImpl->_Access->EntityComponentStore,
                    queryData,
                    ref queryImpl->_Filter,
                    (Entity*) limitToEntityArray.GetUnsafePtr(),
                    limitToEntityArray.Length,
                    ref prebuiltBatchList,
                    ref perBatchMatchingArchetypeIndex);

                batchCount = prebuiltBatchList.Length;
            }
            else
            {
                filteredChunkCount = query.CalculateChunkCount();
                batchCount = filteredChunkCount * batchesPerChunk;
            }

            // Allocate one buffer for all prefilter data and distribute it
            // We keep the full buffer as a "dummy array" so we can deallocate it later with [DeallocateOnJobCompletion]
            var sizeofBatchArray = sizeof(ArchetypeChunk) * batchCount;
            var sizeofIndexArray = sizeof(int) * batchCount;
            var prefilterDataSize = sizeofBatchArray + sizeofIndexArray + sizeof(int);

            var prefilterData = (byte*)Memory.Unmanaged.Allocate(prefilterDataSize, 64, Allocator.TempJob);
            var prefilterDataArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(prefilterData, prefilterDataSize, Allocator.TempJob);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref prefilterDataArray, AtomicSafetyHandle.Create());
#endif

            var prefilterHandle = dependsOn;
            if (useEntityArray)
            {
                var prefilterJob = new PrefilterForJobEntityBatchWithIndex_EntityArray
                {
                    MatchingArchetypes = queryImpl->_QueryData->MatchingArchetypes,
                    Filter = queryImpl->_Filter,
                    EntityComponentStore = queryImpl->_Access->EntityComponentStore,
                    PrefilterData = prefilterData,
                    PrebuiltBatches = prebuiltBatchList,
                    PerBatchMatchingArchetypeIndex = perBatchMatchingArchetypeIndex
                };

                if (mode != ScheduleMode.Run)
                    prefilterHandle = prefilterJob.Schedule(dependsOn);
                else
                    prefilterJob.Run();

                prefilterHandle = prebuiltBatchList.Dispose(prefilterHandle);
                prefilterHandle = perBatchMatchingArchetypeIndex.Dispose(prefilterHandle);
            }
            else
            {
                var prefilterJob = new PrefilterForJobEntityBatchWithIndex
                {
                    MatchingArchetypes = queryImpl->_QueryData->MatchingArchetypes,
                    Filter = queryImpl->_Filter,
                    BatchesPerChunk = batchesPerChunk,
                    EntityComponentStore = queryImpl->_Access->EntityComponentStore,
                    PrefilterData = prefilterData,
                    FilteredChunkCount = filteredChunkCount
                };

                if (mode != ScheduleMode.Run)
                    prefilterHandle = prefilterJob.Schedule(dependsOn);
                else
                    prefilterJob.Run();
            }


            JobEntityBatchIndexWrapper<T> jobEntityBatchIndexWrapper = new JobEntityBatchIndexWrapper<T>
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // All IJobEntityBatchWithIndex jobs have a EntityManager safety handle to ensure that BeforeStructuralChange throws an error if
                // jobs without any other safety handles are still running (haven't been synced).
                safety = new EntitySafetyHandle {m_Safety = queryImpl->SafetyHandles->GetEntityManagerSafetyHandle()},
#endif

                JobData = jobData,
                PrefilterData = prefilterDataArray,

                JobsPerChunk = batchesPerChunk,
                IsParallel = isParallel ? 1 : 0
            };

            var scheduleParams = new JobsUtility.JobScheduleParameters(
                UnsafeUtility.AddressOf(ref jobEntityBatchIndexWrapper),
                isParallel
                ? JobEntityBatchIndexProducer<T>.InitializeParallel()
                : JobEntityBatchIndexProducer<T>.InitializeSingle(),
                prefilterHandle,
                mode);

#if UNITY_DOTSRUNTIME
            // This should just be a call to FinalizeScheduleChecked, but DOTSR requires the JobsUtility calls to be
            // in this specific function.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            try
            {
#endif
                if (!isParallel)
                {
                    return JobsUtility.Schedule(ref scheduleParams);
                }
                else
                {
                    return JobsUtility.ScheduleParallelFor(ref scheduleParams, batchCount, 1);
                }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            }
            catch (InvalidOperationException e)
            {
                prefilterHandle.Complete();
                prefilterDataArray.Dispose();
                throw e;
            }
#endif
#else
            // We can't use try {} catch {} with 2020.2 as we will be burst compiling the schedule code.
            // Burst doesn't support exception handling.
            bool executedManaged = false;
            JobHandle result = default;
            FinalizeScheduleChecked(isParallel, batchCount, prefilterHandle, prefilterDataArray, ref scheduleParams, ref executedManaged, ref result);

            if (executedManaged)
                return result;

            return FinalizeScheduleNoExceptions(isParallel, batchCount, ref scheduleParams);
#endif
        }

#if !UNITY_DOTSRUNTIME
        // Burst does not support exception handling.
        [BurstDiscard]
        private static unsafe void FinalizeScheduleChecked(bool isParallel, int batchCount, JobHandle prefilterHandle, NativeArray<byte> prefilterDataArray, ref JobsUtility.JobScheduleParameters scheduleParams, ref bool executed, ref JobHandle result)
        {
            executed = true;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            try
            {
#endif
                result = FinalizeScheduleNoExceptions(isParallel, batchCount, ref scheduleParams);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            }
            catch (InvalidOperationException e)
            {
                prefilterHandle.Complete();
                prefilterDataArray.Dispose();
                throw e;
            }
#endif
        }

        private static unsafe JobHandle FinalizeScheduleNoExceptions(bool isParallel, int batchCount, ref JobsUtility.JobScheduleParameters scheduleParams)
        {
            if (!isParallel)
            {
                return JobsUtility.Schedule(ref scheduleParams);
            }
            else
            {
                return JobsUtility.ScheduleParallelFor(ref scheduleParams, batchCount, 1);
            }
        }
#endif
        internal struct JobEntityBatchIndexProducer<T>
            where T : struct, IJobEntityBatchWithIndex
        {
#if UNITY_2020_2_OR_NEWER && !UNITY_DOTSRUNTIME
            internal static readonly SharedStatic<IntPtr> s_ReflectionData = SharedStatic<IntPtr>.GetOrCreate<T>();

            internal static void CreateReflectionData()
            {
                s_ReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(JobEntityBatchIndexWrapper<T>), typeof(T), (ExecuteJobFunction)Execute);
            }
#else
            static IntPtr s_JobReflectionDataParallel;
            static IntPtr s_JobReflectionDataSingle;
#endif

            internal static IntPtr InitializeSingle()
            {
#if UNITY_2020_2_OR_NEWER && !UNITY_DOTSRUNTIME
                return InitializeParallel();
#else
                if (s_JobReflectionDataSingle == IntPtr.Zero)
                    s_JobReflectionDataSingle = JobsUtility.CreateJobReflectionData(typeof(JobEntityBatchIndexWrapper<T>), typeof(T), JobType.Single, (ExecuteJobFunction)Execute);
                return s_JobReflectionDataSingle;
#endif
            }

            internal static IntPtr InitializeParallel()
            {
#if UNITY_2020_2_OR_NEWER && !UNITY_DOTSRUNTIME
                IntPtr result = s_ReflectionData.Data;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (result == IntPtr.Zero)
                    throw new InvalidOperationException("IJobEntityBatchWithIndex job reflection data has not been automatically computed - this is a bug");
#endif
                return result;
#else
                if (s_JobReflectionDataParallel == IntPtr.Zero)
                    s_JobReflectionDataParallel = JobsUtility.CreateJobReflectionData(typeof(JobEntityBatchIndexWrapper<T>), typeof(T), JobType.ParallelFor, (ExecuteJobFunction)Execute);
                return s_JobReflectionDataParallel;
#endif
            }

            public delegate void ExecuteJobFunction(
                ref JobEntityBatchIndexWrapper<T> jobIndexWrapper,
                IntPtr additionalPtr,
                IntPtr bufferRangePatchData,
                ref JobRanges ranges,
                int jobIndex);

            public static void Execute(
                ref JobEntityBatchIndexWrapper<T> jobIndexWrapper,
                IntPtr additionalPtr,
                IntPtr bufferRangePatchData,
                ref JobRanges ranges,
                int jobIndex)
            {
                ExecuteInternal(ref jobIndexWrapper, bufferRangePatchData, ref ranges, jobIndex);
            }

            internal unsafe static void ExecuteInternal(
                ref JobEntityBatchIndexWrapper<T> jobWrapper,
                IntPtr bufferRangePatchData,
                ref JobRanges ranges,
                int jobIndex)
            {
                ChunkIterationUtility.UnpackPrefilterData(jobWrapper.PrefilterData, out var filteredChunks, out var entityIndices, out var batchCount);

                bool isParallel = jobWrapper.IsParallel == 1;
                while (true)
                {
                    int beginBatchIndex = 0;
                    int endBatchIndex = batchCount;

                    // If we are running the job in parallel, steal some work.
                    if (isParallel)
                    {
                        // If we have no range to steal, exit the loop.
                        if (!JobsUtility.GetWorkStealingRange(ref ranges, jobIndex, out beginBatchIndex, out endBatchIndex))
                            break;
                    }

                    // Do the actual user work.
                    for (int batchIndex = beginBatchIndex; batchIndex < endBatchIndex; ++batchIndex)
                    {
                        var batch = filteredChunks[batchIndex];
                        Assert.IsTrue(batch.Count > 0); // Empty batches are expected to be skipped by the prefilter job!
                        var entityOffset = entityIndices[batchIndex];

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        if(isParallel)
                        {
                            JobsUtility.PatchBufferMinMaxRanges(bufferRangePatchData, UnsafeUtility.AddressOf(ref jobWrapper), entityOffset, batch.Count);
                        }
#endif
                        jobWrapper.JobData.Execute(batch, batchIndex, entityOffset);
                    }

                    // If we are not running in parallel, our job is done.
                    if (!isParallel)
                        break;
                }
            }
        }
    }

    [BurstCompile]
    unsafe struct PrefilterForJobEntityBatchWithIndex : IJobBurstSchedulable
    {
        [NativeDisableUnsafePtrRestriction] public UnsafeMatchingArchetypePtrList MatchingArchetypes;
        [NativeDisableUnsafePtrRestriction] public EntityComponentStore* EntityComponentStore;
        [NativeDisableUnsafePtrRestriction] public void* PrefilterData;

        public int FilteredChunkCount;
        public EntityQueryFilter Filter;
        public int BatchesPerChunk;

        public void Execute()
        {
            var batches = (ArchetypeChunk*)PrefilterData;
            var entityIndices = (int*)(batches + FilteredChunkCount * BatchesPerChunk);

            var filteredBatchCounter = 0;
            var entityIndexAggregate = 0;

            if (BatchesPerChunk == 1)
            {
                if (Filter.RequiresMatchesFilter)
                {
                    // one batch per chunk, filtering enabled
                    for (var m = 0; m < MatchingArchetypes.Length; ++m)
                    {
                        var match = MatchingArchetypes.Ptr[m];
                        if (match->Archetype->EntityCount <= 0)
                            continue;

                        var archetype = match->Archetype;
                        int chunkCount = archetype->Chunks.Count;
                        var chunkEntityCountArray = archetype->Chunks.GetChunkEntityCountArray();

                        for (int chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
                        {
                            var chunk = archetype->Chunks[chunkIndex];
                            if (match->ChunkMatchesFilter(chunkIndex, ref Filter))
                            {
                                var batch = new ArchetypeChunk(chunk, EntityComponentStore);
                                batches[filteredBatchCounter] = batch;
                                entityIndices[filteredBatchCounter] = entityIndexAggregate;

                                ++filteredBatchCounter;
                                entityIndexAggregate += chunkEntityCountArray[chunkIndex];
                            }
                        }
                    }
                }
                else
                {
                    // one batch per chunk, filtering disabled
                    for (var m = 0; m < MatchingArchetypes.Length; ++m)
                    {
                        var match = MatchingArchetypes.Ptr[m];
                        if (match->Archetype->EntityCount <= 0)
                            continue;

                        var archetype = match->Archetype;
                        int chunkCount = archetype->Chunks.Count;
                        var chunkEntityCountArray = archetype->Chunks.GetChunkEntityCountArray();

                        for (int chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
                        {
                            var chunk = archetype->Chunks[chunkIndex];
                            var batch = new ArchetypeChunk(chunk, EntityComponentStore);
                            batches[filteredBatchCounter] = batch;
                            entityIndices[filteredBatchCounter] = entityIndexAggregate;

                            ++filteredBatchCounter;
                            entityIndexAggregate += chunkEntityCountArray[chunkIndex];
                        }
                    }
                }
            }
            else
            {
                if (Filter.RequiresMatchesFilter)
                {
                    // 2+ batches per chunk, filtering enabled.
                    // This is the most general case; if only one code path survives, it should be this one.
                    for (var m = 0; m < MatchingArchetypes.Length; ++m)
                    {
                        var match = MatchingArchetypes.Ptr[m];
                        if (match->Archetype->EntityCount <= 0)
                            continue;

                        var archetype = match->Archetype;
                        int chunkCount = archetype->Chunks.Count;
                        var chunkEntityCountArray = archetype->Chunks.GetChunkEntityCountArray();

                        for (int chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
                        {
                            if (match->ChunkMatchesFilter(chunkIndex, ref Filter))
                            {
                                var chunk = archetype->Chunks[chunkIndex];
                                var chunkEntityCount = chunkEntityCountArray[chunkIndex];
                                for (int batchIndex = 0; batchIndex < BatchesPerChunk; ++batchIndex)
                                {
                                    if (ArchetypeChunk.EntityBatchFromChunk(chunk, chunkEntityCount, BatchesPerChunk,
                                        batchIndex, EntityComponentStore, out var batch))
                                    {
                                        batches[filteredBatchCounter] = batch;
                                        entityIndices[filteredBatchCounter] = entityIndexAggregate;

                                        ++filteredBatchCounter;
                                        entityIndexAggregate += batch.Count;
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    // 2+ batches per chunk, filtering disabled
                    for (var m = 0; m < MatchingArchetypes.Length; ++m)
                    {
                        var match = MatchingArchetypes.Ptr[m];
                        if (match->Archetype->EntityCount <= 0)
                            continue;

                        var archetype = match->Archetype;
                        int chunkCount = archetype->Chunks.Count;
                        var chunkEntityCountArray = archetype->Chunks.GetChunkEntityCountArray();

                        for (int chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
                        {
                            var chunk = archetype->Chunks[chunkIndex];
                            var chunkEntityCount = chunkEntityCountArray[chunkIndex];
                            for (int batchIndex = 0; batchIndex < BatchesPerChunk; ++batchIndex)
                            {
                                if (ArchetypeChunk.EntityBatchFromChunk(chunk, chunkEntityCount, BatchesPerChunk,
                                    batchIndex, EntityComponentStore, out var batch))
                                {
                                    batches[filteredBatchCounter] = batch;
                                    entityIndices[filteredBatchCounter] = entityIndexAggregate;

                                    ++filteredBatchCounter;
                                    entityIndexAggregate += batch.Count;
                                }
                            }
                        }
                    }
                }
            }

            var chunkCounter = entityIndices + FilteredChunkCount * BatchesPerChunk;
            *chunkCounter = filteredBatchCounter;
        }
    }

    [BurstCompile]
    unsafe struct PrefilterForJobEntityBatchWithIndex_EntityArray : IJobBurstSchedulable
    {
        [NativeDisableUnsafePtrRestriction] public UnsafeMatchingArchetypePtrList MatchingArchetypes;
        [NativeDisableUnsafePtrRestriction] public EntityComponentStore* EntityComponentStore;
        [NativeDisableUnsafePtrRestriction] public void* PrefilterData;

        public UnsafeList PrebuiltBatches;
        public UnsafeIntList PerBatchMatchingArchetypeIndex;
        public EntityQueryFilter Filter;

        public void Execute()
        {
            var inBatches = (ArchetypeChunk*) PrebuiltBatches.Ptr;
            var outBatches = (ArchetypeChunk*) PrefilterData;
            var outEntityIndices = (int*) (outBatches + PrebuiltBatches.Length);

            var filteredBatchCounter = 0;
            var entityIndexAggregate = 0;
            var isFiltering = Filter.RequiresMatchesFilter;

            for (int i = 0; i < PrebuiltBatches.Length; ++i)
            {
                var batch = inBatches[i];

                if (!isFiltering || batch.m_Chunk->MatchesFilter(MatchingArchetypes.Ptr[PerBatchMatchingArchetypeIndex.Ptr[i]], ref Filter))
                {
                    outBatches[filteredBatchCounter] = batch;
                    outEntityIndices[filteredBatchCounter] = entityIndexAggregate;

                    ++filteredBatchCounter;
                    entityIndexAggregate += batch.Count;
                }
            }

            var outChunkCounter = outEntityIndices + PrebuiltBatches.Length;
            *outChunkCounter = filteredBatchCounter;
        }
    }

    [BurstCompile]
    struct DummyJobEntityBatchWithIndex : IJobEntityBatchWithIndex
    {
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex, int indexOfFirstEntityInQuery)
        {
        }
    }
    [BurstCompile]
    static class DummyJobEntityBatchWithIndexScheduler
    {
        [BurstCompatible(RequiredUnityDefine = "UNITY_2020_2_OR_NEWER && !NET_DOTS")]
        public static void Schedule()
        {
            new DummyJobEntityBatch().Run(default(EntityQuery));
        }
    }

}
