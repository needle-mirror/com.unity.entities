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
    /// IJobEntityBatch is a type of [IJob] that iterates over a set of <see cref="ArchetypeChunk"/> instances,
    /// where each instance represents a contiguous batch of entities within a [chunk].
    ///
    /// [IJob]: xref:Unity.Jobs.IJob
    /// [chunk]: xref:ecs-concepts#chunk
    /// </summary>
    /// <remarks>
    /// Schedule or run an IJobEntityBatch job inside the <see cref="SystemBase.OnUpdate"/> function of a
    /// <see cref="SystemBase"/> implementation. When the system schedules or runs an IJobEntityBatch job, it uses
    /// the specified <see cref="EntityQuery"/> to select a set of [chunks]. These selected chunks are divided into
    /// batches of entities. A batch is a contiguous set of entities, always stored in the same chunk. The job
    /// struct's `Execute` function is called for each batch.
    ///
    /// When you schedule or run the job with one of the following methods:
    /// * <see cref="JobEntityBatchExtensions.Schedule{T}"/>,
    /// * <see cref="JobEntityBatchExtensions.ScheduleParallel{T}(T, EntityQuery, int, JobHandle)"/>,
    /// * or <see cref="JobEntityBatchExtensions.Run{T}(T, EntityQuery)"/>
    ///
    /// all the entities of each chunk are processed as
    /// a single batch. The <see cref="ArchetypeChunk"/> object passed to the `Execute` function of your job struct provides access
    /// to the components of all the entities in the chunk.
    ///
    /// Use <see cref="JobEntityBatchExtensions.ScheduleParallel{T}(T, EntityQuery, int, JobHandle)"/> to divide
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
    /// To pass data to your Execute function (beyond the `Execute` parameters), add public fields to the IJobEntityBatch
    /// struct declaration and set those fields immediately before scheduling the job. You must always pass the
    /// component type information for any components that the job reads or writes using a field of type,
    /// <seealso cref="ComponentTypeHandle{T}"/>. Get this type information by calling the appropriate
    /// <seealso cref="ComponentSystemBase.GetComponentTypeHandle{T}"/> function for the type of
    /// component.
    ///
    /// For more information see [Using IJobEntityBatch].
    /// <example>
    /// <code source="../DocCodeSamples.Tests/IJobEntityBatchExamples.cs" region="basic-ijobentitybatch" title="IJobEntityBatch Example"/>
    /// </example>
    ///
    /// If you are looking for an interface which provides the firstEntityIndex parameter, <see cref="IJobEntityBatchWithIndex"/>
    ///
    /// [Using IJobEntityBatch]: xref:ecs-ijobentitybatch
    /// [chunks]: xref:ecs-concepts#chunk
    /// </remarks>
    /// <seealso cref="JobEntityBatchExtensions"/>
    /// <seealso cref="IJobEntityBatchWithIndex"/>
    [JobProducerType(typeof(JobEntityBatchExtensions.JobEntityBatchProducer<>))]
    public interface IJobEntityBatch
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
        ///
        /// If you are looking for an interface which provides the firstEntityIndex parameter, <see cref="IJobEntityBatchWithIndex"/>
        /// </remarks>
        /// <param name="batchInChunk">An object providing access to a batch of entities within a chunk.</param>
        /// <param name="batchIndex">The index of the current batch within the list of all batches in all chunks found by the
        /// job's <see cref="EntityQuery"/>. If the batch count is one, this list contains one entry for each selected chunk; if
        /// the batch count is two, the list contains two entries per chunk; and so on. Note that batches are not processed in
        /// index order, except by chance.</param>
        void Execute(ArchetypeChunk batchInChunk, int batchIndex);
    }

    /// <summary>
    /// Extensions for scheduling and running <see cref="IJobEntityBatch"/> jobs.
    /// </summary>
    public static class JobEntityBatchExtensions
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [NativeContainer]
        internal struct EntitySafetyHandle
        {
            internal AtomicSafetyHandle m_Safety;
        }
#endif
        internal struct JobEntityBatchWrapper<T> where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
#pragma warning disable 414
            [ReadOnly] public EntitySafetyHandle safety;
#pragma warning restore
#endif
            public T JobData;

            public UnsafeMatchingArchetypePtrList MatchingArchetypes;
            public UnsafeCachedChunkList CachedChunks;
            public EntityQueryFilter Filter;

            public UnsafeList PrebuiltBatchList;
            public UnsafeIntList PrebuiltBatchListMatchingArchetypeIndices;

            public int JobsPerChunk;
            public int IsParallel;
            public int UsePrebuiltBatchList;
        }

#if UNITY_2020_2_OR_NEWER && !UNITY_DOTSRUNTIME
        /// <summary>
        /// This method is only to be called by automatically generated setup code.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static void EarlyJobInit<T>()
            where T : struct, IJobEntityBatch
        {
            JobEntityBatchProducer<T>.CreateReflectionData();
        }
#endif

        /// <summary>
        /// Adds an <see cref="IJobEntityBatch"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each matching chunk as a single batch. All chunks execute
        /// sequentially.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatch"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatch"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle Schedule<T>(
            this T jobData,
            EntityQuery query,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
        {
#if UNITY_2020_2_OR_NEWER
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, 1, false);
#else
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Batched, 1, false);
#endif
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatch"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the entity array. All batches are processed
        /// sequentially.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatch"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be processed.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatch"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle Schedule<T>(
            this T jobData,
            EntityQuery query,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
        {
#if UNITY_2020_2_OR_NEWER
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, 1, false, limitToEntityArray);
#else
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Batched, 1, false, limitToEntityArray);
#endif
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatch"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each matching chunk as a single batch. Each
        /// chunk can execute in parallel. This scheduling method is equivalent to calling
        /// <see cref="JobEntityBatchExtensions.ScheduleParallel{T}(T, EntityQuery, int, JobHandle)"/>
        /// with the batchesPerChunk parameter set to 1.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatch"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatch"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleParallel<T>(
            this T jobData,
            EntityQuery query,
            int batchesPerChunk = 1,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
        {
#if UNITY_2020_2_OR_NEWER
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, batchesPerChunk, true);
#else
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Batched, batchesPerChunk, true);
#endif
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatch"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the entity array. Each
        /// batch can execute in parallel.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatch"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be processed.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatch"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleParallel<T>(
            this T jobData,
            EntityQuery query,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
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
        /// <param name="jobData">An <see cref="IJobEntityBatch"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatch"/> implementation type.</typeparam>
        public static unsafe void Run<T>(this T jobData, EntityQuery query)
            where T : struct, IJobEntityBatch
        {
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, 1, false);
        }

        /// <summary>
        /// Runs the job immediately on the current thread.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the input array. All
        /// batches are processed sequentially on the current thread.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatch"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be processed.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatch"/> implementation type.</typeparam>
        public static unsafe void Run<T>(this T jobData, EntityQuery query, NativeArray<Entity> limitToEntityArray)
            where T : struct, IJobEntityBatch
        {
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, 1, false, limitToEntityArray);
        }

        public static void RunWithoutJobsInternal<T>(ref T jobData, ref ArchetypeChunkIterator chunkIterator)
            where T : struct, IJobEntityBatch
        {
            var chunkCount = 0;
            while (chunkIterator.MoveNext())
            {
                var archetypeChunk = chunkIterator.CurrentArchetypeChunk;
                jobData.Execute(archetypeChunk, chunkCount);
                chunkCount++;
            }
        }


        /// <summary>
        /// Runs the job without using the jobs API.
        /// </summary>
        /// <param name="jobData">The job to execute.</param>
        /// <param name="query">The EntityQuery to run over.</param>
        /// <typeparam name="T">The specific IJobEntityBatch implementation type.</typeparam>
        unsafe public static void RunWithoutJobs<T>(ref T jobData, EntityQuery query)
            where T : struct, IJobEntityBatch
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

        /// <summary>
        /// Runs the job without using the jobs API.
        /// </summary>
        /// <param name="jobData">The job to execute.</param>
        /// <param name="query">The EntityQuery to run over.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be processed.</param>
        /// <typeparam name="T">The specific IJobEntityBatch implementation type.</typeparam>
        unsafe public static void RunWithoutJobs<T>(ref T jobData, EntityQuery query, NativeArray<Entity> limitToEntityArray)
            where T : struct, IJobEntityBatch
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var access = query._GetImpl()->_Access;
            try
            {
                access->DependencyManager->IsInForEachDisallowStructuralChange++;
                RunWithoutJobsInternal(ref jobData, ref query, (Entity*)limitToEntityArray.GetUnsafeReadOnlyPtr(), limitToEntityArray.Length);
            }
            finally
            {
                access->DependencyManager->IsInForEachDisallowStructuralChange--;
            }
#else
            RunWithoutJobsInternal(ref jobData, ref query, (Entity*)limitToEntityArray.GetUnsafeReadOnlyPtr(), limitToEntityArray.Length);
#endif
        }

        public static unsafe void RunWithoutJobsInternal<T>(ref T jobData, ref EntityQuery query, Entity* limitToEntityArray, int limitToEntityArrayLength)
            where T : struct, IJobEntityBatch
        {
            var prebuiltBatchList = new UnsafeList(Allocator.TempJob);
            try
            {
                ChunkIterationUtility.FindFilteredBatchesForEntityArrayWithQuery(
                    query._GetImpl(),
                    limitToEntityArray, limitToEntityArrayLength,
                    ref prebuiltBatchList);

                ArchetypeChunk* chunks = (ArchetypeChunk*)prebuiltBatchList.Ptr;
                int chunkCounts = prebuiltBatchList.Length;
                for (int i = 0; i != chunkCounts; i++)
                    jobData.Execute(chunks[i], i);
            }
            finally
            {
                prebuiltBatchList.Dispose();
            }
        }

        internal static unsafe JobHandle ScheduleInternal<T>(
            ref T jobData,
            EntityQuery query,
            JobHandle dependsOn,
            ScheduleMode mode,
            int batchesPerChunk,
            bool isParallel = true,
            NativeArray<Entity> limitToEntityArray = default(NativeArray<Entity>))
            where T : struct, IJobEntityBatch
        {
            var queryImpl = query._GetImpl();
            var queryData = queryImpl->_QueryData;

            var cachedChunks = queryData->GetMatchingChunkCache();

            // Don't schedule the job if there are no chunks to work on
            var chunkCount = cachedChunks.Length;

            var useEntityArray = limitToEntityArray.IsCreated;
            var prebuiltBatchList = default(UnsafeList);
            var perBatchMatchingArchetypeIndex = default(UnsafeIntList);

            var batchCount = chunkCount * batchesPerChunk;

            if (useEntityArray)
            {
                prebuiltBatchList = new UnsafeList(Allocator.TempJob);
                perBatchMatchingArchetypeIndex = new UnsafeIntList(0, Allocator.TempJob);

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

            JobEntityBatchWrapper<T> jobEntityBatchWrapper = new JobEntityBatchWrapper<T>
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // All IJobEntityBatch jobs have a EntityManager safety handle to ensure that BeforeStructuralChange throws an error if
                // jobs without any other safety handles are still running (haven't been synced).
                safety = new EntitySafetyHandle {m_Safety = queryImpl->SafetyHandles->GetEntityManagerSafetyHandle()},
#endif

                MatchingArchetypes = queryData->MatchingArchetypes,
                CachedChunks = cachedChunks,
                Filter = queryImpl->_Filter,

                JobData = jobData,
                JobsPerChunk = batchesPerChunk,
                IsParallel = isParallel ? 1 : 0,

                UsePrebuiltBatchList = useEntityArray ? 1: 0,
                PrebuiltBatchList = prebuiltBatchList,
                PrebuiltBatchListMatchingArchetypeIndices = perBatchMatchingArchetypeIndex
            };

            var scheduleParams = new JobsUtility.JobScheduleParameters(
                UnsafeUtility.AddressOf(ref jobEntityBatchWrapper),
                isParallel
                    ? JobEntityBatchProducer<T>.InitializeParallel()
                    : JobEntityBatchProducer<T>.InitializeSingle(),
                dependsOn,
                mode);

            var result = default(JobHandle);
            if (!isParallel)
            {
                result = JobsUtility.Schedule(ref scheduleParams);
            }
            else
            {
                result = JobsUtility.ScheduleParallelFor(ref scheduleParams, batchCount, 1);
            }

            if (useEntityArray)
            {
                result = prebuiltBatchList.Dispose(result);
                result = perBatchMatchingArchetypeIndex.Dispose(result);
            }

            return result;
        }

        internal struct JobEntityBatchProducer<T>
            where T : struct, IJobEntityBatch
        {
#if UNITY_2020_2_OR_NEWER && !UNITY_DOTSRUNTIME
            internal static readonly SharedStatic<IntPtr> s_ReflectionData = SharedStatic<IntPtr>.GetOrCreate<T>();

            internal static void CreateReflectionData()
            {
                s_ReflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(JobEntityBatchWrapper<T>), typeof(T), (ExecuteJobFunction)Execute);
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
                    s_JobReflectionDataSingle = JobsUtility.CreateJobReflectionData(typeof(JobEntityBatchWrapper<T>), typeof(T), JobType.Single, (ExecuteJobFunction)Execute);
                return s_JobReflectionDataSingle;
#endif
            }

            internal static IntPtr InitializeParallel()
            {
#if UNITY_2020_2_OR_NEWER && !UNITY_DOTSRUNTIME
                IntPtr result = s_ReflectionData.Data;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (result == IntPtr.Zero)
                    throw new InvalidOperationException($"IJobEntityBatch error: job reflection data has not been initialized for this job. Generic jobs must either be fully qualified in normal code or be registered with `[assembly:RegisterGenericJobType(typeof(...))]`. See https://docs.unity3d.com/Packages/com.unity.entities@latest?subfolder=/manual/ecs_generic_jobs.html");
#endif
                return result;
#else
                if (s_JobReflectionDataParallel == IntPtr.Zero)
                    s_JobReflectionDataParallel = JobsUtility.CreateJobReflectionData(typeof(JobEntityBatchWrapper<T>), typeof(T), JobType.ParallelFor, (ExecuteJobFunction)Execute);
                return s_JobReflectionDataParallel;
#endif
            }

            public delegate void ExecuteJobFunction(
                ref JobEntityBatchWrapper<T> jobWrapper,
                IntPtr additionalPtr,
                IntPtr bufferRangePatchData,
                ref JobRanges ranges,
                int jobIndex);

            public static void Execute(
                ref JobEntityBatchWrapper<T> jobWrapper,
                IntPtr additionalPtr,
                IntPtr bufferRangePatchData,
                ref JobRanges ranges,
                int jobIndex)
            {
                ExecuteInternal(ref jobWrapper, bufferRangePatchData, ref ranges, jobIndex);
            }

            internal unsafe static void ExecuteInternal(
                ref JobEntityBatchWrapper<T> jobWrapper,
                IntPtr bufferRangePatchData,
                ref JobRanges ranges,
                int jobIndex)
            {
                var chunks = jobWrapper.CachedChunks;
                var prebuiltBatches = (ArchetypeChunk*)jobWrapper.PrebuiltBatchList.Ptr;

                bool isParallel = jobWrapper.IsParallel == 1;
                bool isFiltering = jobWrapper.Filter.RequiresMatchesFilter;
                while (true)
                {
                    int beginBatchIndex = 0;
                    int endBatchIndex = jobWrapper.UsePrebuiltBatchList == 1 ? jobWrapper.PrebuiltBatchList.Length : chunks.Length;

                    // If we are running the job in parallel, steal some work.
                    if (isParallel)
                    {
                        // If we have no range to steal, exit the loop.
                        if (!JobsUtility.GetWorkStealingRange(ref ranges, jobIndex, out beginBatchIndex, out endBatchIndex))
                            break;

                        JobsUtility.PatchBufferMinMaxRanges(bufferRangePatchData, UnsafeUtility.AddressOf(ref jobWrapper), 0, 0);
                    }

                    // Do the actual user work.
                    if (jobWrapper.UsePrebuiltBatchList == 1)
                    {
                        for (int batchIndex = beginBatchIndex; batchIndex < endBatchIndex; ++batchIndex)
                        {
                            var batch = prebuiltBatches[batchIndex];

                            if (isFiltering && !batch.m_Chunk->MatchesFilter(jobWrapper.MatchingArchetypes.Ptr[jobWrapper.PrebuiltBatchListMatchingArchetypeIndices.Ptr[batchIndex]], ref jobWrapper.Filter))
                                continue;

                            Assert.AreNotEqual(0, batch.Count);
                            jobWrapper.JobData.Execute(batch, batchIndex);
                        }
                    }
                    else
                    {
                        if (jobWrapper.JobsPerChunk == 1)
                        {
                            // 1 batch per chunk, with/without filtering
                            for (int batchIndex = beginBatchIndex; batchIndex < endBatchIndex; ++batchIndex)
                            {
                                var chunkIndex = batchIndex;
                                var chunk = chunks.Ptr[chunkIndex];

                                if (isFiltering && !chunk->MatchesFilter(jobWrapper.MatchingArchetypes.Ptr[chunks.PerChunkMatchingArchetypeIndex.Ptr[chunkIndex]], ref jobWrapper.Filter))
                                    continue;

                                var batch = new ArchetypeChunk(chunk, chunks.EntityComponentStore);
                                Assert.AreNotEqual(0, batch.Count);
                                jobWrapper.JobData.Execute(batch, batchIndex);
                            }
                        }
                        else
                        {
                            // 2+ batches per chunk, with/without filtering
                            // This is the most general case; if only one code path survives, it should be this one.
                            for (int batchIndex = beginBatchIndex; batchIndex < endBatchIndex; ++batchIndex)
                            {
                                var chunkIndex = batchIndex / jobWrapper.JobsPerChunk;
                                var batchIndexInChunk = batchIndex % jobWrapper.JobsPerChunk;
                                var chunk = chunks.Ptr[chunkIndex];

                                if (isFiltering && !chunk->MatchesFilter(
                                    jobWrapper.MatchingArchetypes.Ptr[
                                        chunks.PerChunkMatchingArchetypeIndex.Ptr[chunkIndex]],
                                    ref jobWrapper.Filter))
                                    continue;

                                if (ArchetypeChunk.EntityBatchFromChunk(chunk, chunk->Count, jobWrapper.JobsPerChunk,
                                    batchIndexInChunk, chunks.EntityComponentStore, out var batch))
                                {
                                    jobWrapper.JobData.Execute(batch, batchIndex);
                                }
                            }
                        }
                    }

                    // If we are not running in parallel, our job is done.
                    if (!isParallel)
                        break;
                }
            }
        }
    }


    // Burst-compatibility tests
    [BurstCompile]
    struct DummyJobEntityBatch : IJobEntityBatch
    {
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
        }
    }
    [BurstCompile]
    static class DummyJobEntityBatchScheduler
    {
        [BurstCompatible(RequiredUnityDefine = "UNITY_2020_2_OR_NEWER && !NET_DOTS")]
        public static void Schedule()
        {
            new DummyJobEntityBatch().Run(default(EntityQuery));
        }
    }
}
