using System;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using System.Diagnostics;
using Unity.Burst.Intrinsics;
using Assert = UnityEngine.Assertions.Assert;

namespace Unity.Entities
{
    /// <summary>
    /// IJobEntityBatch is a type of [IJob] that iterates over a set of <see cref="ArchetypeChunk"/> instances,
    /// where each instance represents a contiguous batch of entities within a [chunk].
    ///
    /// [IJob]: xref:Unity.Jobs.IJob
    /// [chunk]: xref:concepts-archetypes#archetype-chunks
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
    /// * <see cref="JobEntityBatchExtensions.ScheduleParallel{T}(T, EntityQuery, JobHandle)"/>,
    /// * or <see cref="JobEntityBatchExtensions.Run{T}(T, EntityQuery)"/>
    ///
    /// all the entities of each chunk are processed as
    /// a single batch. The <see cref="ArchetypeChunk"/> object passed to the `Execute` function of your job struct provides access
    /// to the components of all the entities in the chunk.
    ///
    /// Use <see cref="JobEntityBatchExtensions.ScheduleParallel{T}(T, EntityQuery, ScheduleGranularity, NativeArray{Entity}, JobHandle)"/>
    /// to force each batch to contain only a single entity. This allows multiple worker threads to process the entities
    /// within a chunk concurrently, which may lead to better load balancing if the number of entities to process is relatively
    /// small and the amount of work per entity is relatively high. As always, you should profile your job to find the
    /// best arrangement for your specific application.
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
    /// [Using IJobEntityBatch]: xref:iterating-data-ijobentitybatch
    /// [chunks]: xref:concepts-archetypes#archetype-chunks
    /// </remarks>
    /// <seealso cref="JobEntityBatchExtensions"/>
    /// <seealso cref="IJobEntityBatchWithIndex"/>
    [JobProducerType(typeof(JobEntityBatchExtensions.JobEntityBatchProducer<>))]
    [Obsolete("This job type will be removed in Entities 1.0. Existing implementations should be migrated to IJobChunk. See the upgrade guide for details.")]
    public interface IJobEntityBatch
    {
        /// <summary>
        /// Implement the `Execute` function to perform a unit of work on an <see cref="ArchetypeChunk"/> representing
        /// a contiguous batch of entities within a chunk.
        /// </summary>
        /// <remarks>
        /// The chunks selected by the <see cref="EntityQuery"/> used to schedule the job are the input to your `Execute`
        /// function. If you use <see cref="JobEntityBatchExtensions.ScheduleParallel{T}(T, EntityQuery, ScheduleGranularity, NativeArray{Entity}, JobHandle)"/>
        /// to schedule the job, the entities in each matching chunk are distributed to worker threads individually,
        /// and the `Execute` function is called once for each batch (containing a single `Entity`). When you use one of the
        /// other scheduling or run methods, the `Execute` function is called once per matching chunk.
        ///
        /// If you are looking for an interface which provides the firstEntityIndex parameter, <see cref="IJobEntityBatchWithIndex"/>
        /// </remarks>
        /// <param name="batchInChunk">An object providing access to a batch of entities within a chunk.</param>
        /// <param name="batchIndex">The index of the current batch within the list of all batches in all chunks found by the
        /// job's <see cref="EntityQuery"/>. Note that batches are not necessarily processed in index order. These ids
        /// are not guaranteed to be contiguous or zero-based.</param>
        void Execute(ArchetypeChunk batchInChunk, int batchIndex);
    }

    /// <summary>
    /// Describes how the entities that match an `EntityQuery` should be distributed when a job is scheduled to
    /// run on multiple worker threads using `ScheduleParallel()`. In most cases, ScheduleGranularity.Chunk
    /// should be used.
    /// </summary>
    [Obsolete("This feature will be removed; the previous default chunk-level granularity will be restored. For entity-level granularity, use IJobParallelFor. (RemovedAfter Entities 1.0)")]
    public enum ScheduleGranularity
    {
        /// <summary>
        /// Entities are distributed to worker threads at the granularity of entire chunks. This is generally the
        /// safest and highest-performance approach, and is the default mode unless otherwise specified. The
        /// entities within the chunk can be processed in a a cache-friendly manner, and job queue contention is
        /// minimized.
        /// </summary>
        Chunk = 0,
        /// <summary>
        /// Entities are distributed to worker threads individually. This increases scheduling overhead and
        /// eliminates the cache-friendly benefits of chunk-level processing. However, it can lead to better
        /// load-balancing in cases where the number of entities being processed is relatively low, and the cost of
        /// processing each entity is high, as it allows the entities within a chunk to be distributed evenly across
        /// available worker threads.
        /// </summary>
        Entity = 1,
    }

    /// <summary>
    /// Extensions for scheduling and running <see cref="IJobEntityBatch"/> jobs.
    /// </summary>
    [Obsolete("IJobEntityBatch is deprecated.")]
    public static class JobEntityBatchExtensions
    {
        internal struct JobEntityBatchWrapper<T> where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
#pragma warning disable 414
            [ReadOnly] public EntityQuerySafetyHandles safety;
#pragma warning restore
#endif
            public T JobData;

            public UnsafeMatchingArchetypePtrList MatchingArchetypes;
            public UnsafeCachedChunkList CachedChunks;
            public EntityQueryFilter Filter;

            public UnsafeList<ArchetypeChunk> PrebuiltBatchList;
            public UnsafeList<int> PrebuiltBatchListMatchingArchetypeIndices;

            public int IsParallel;
            public int UsePrebuiltBatchList;
            public int SkipSubChunkBatching;
        }

        /// <summary>
        /// Gathers and caches reflection data for the internal job system's managed bindings. Unity is responsible for calling this method - don't call it yourself.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <remarks>
        /// When the Jobs package is included in the project, Unity generates code to call EarlyJobInit at startup. This allows Burst compiled code to schedule jobs because the reflection part of initialization, which is not compatible with burst compiler constraints, has already happened in EarlyJobInit.
        ///
        /// __Note__: While the Jobs package code generator handles this automatically for all closed job types, you must register those with generic arguments (like IJobEntityBatch&amp;lt;MyJobType&amp;lt;T&amp;gt;&amp;gt;) manually for each specialization with [[Unity.Jobs.RegisterGenericJobTypeAttribute]].
        /// </remarks>
        public static void EarlyJobInit<T>()
            where T : struct, IJobEntityBatch
        {
            JobEntityBatchProducer<T>.Initialize();
        }

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
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, false);
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatch"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each matching chunk as a single batch. All chunks execute
        /// sequentially.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatch"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatch"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleByRef<T>(
            this ref T jobData,
            EntityQuery query,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, false);
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
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAfter Entities 1.0)")]
        public static unsafe JobHandle Schedule<T>(
            this T jobData,
            EntityQuery query,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
        {
            if (!limitToEntityArray.IsCreated)
                return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, false);
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, ScheduleGranularity.Chunk, false, limitToEntityArray);
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatch"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the entity array. All batches are processed
        /// sequentially.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatch"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be processed.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatch"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAFter Entities 1.0)")]
        public static unsafe JobHandle ScheduleByRef<T>(
            this ref T jobData,
            EntityQuery query,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
        {
            if (!limitToEntityArray.IsCreated)
                return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, false);
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, ScheduleGranularity.Chunk, false, limitToEntityArray);
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatch"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each matching chunk as a single batch. Each
        /// chunk can execute in parallel.</remarks>
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
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, true);
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatch"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each matching chunk as a single batch. Each
        /// chunk can execute in parallel.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatch"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatch"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleParallelByRef<T>(
            this ref T jobData,
            EntityQuery query,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, true);
        }


        /// <summary>
        /// Adds an <see cref="IJobEntityBatch"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the entity array. Each
        /// batch can execute in parallel.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatch"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="granularity">Specifies the the unit of work that will be processed by each worker thread.
        /// If `ScheduleGranularity.Chunk` is passed (the safe default),
        /// work is distributed at the level of whole chunks. This can lead to poor load balancing in cases where the
        /// number of chunks being processed is low (fewer than the number of available worker threads), and the cost to
        /// process each entity is high. In these cases, pass ScheduleGranularity.Entity
        /// to distribute work at the level of individual entities.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be
        /// processed. If limitToEntityArray.IsCreated is false (e.g. for a default-initialized array), this filtering
        /// is disabled, and the job will process all entities that match the query.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatch"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAFter Entities 1.0)")]
        public static unsafe JobHandle ScheduleParallel<T>(
            this T jobData,
            EntityQuery query,
            ScheduleGranularity granularity,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
        {
            if (granularity == ScheduleGranularity.Chunk && !limitToEntityArray.IsCreated)
                return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, true);
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, granularity, true, limitToEntityArray);
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatch"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the entity array. Each
        /// batch can execute in parallel.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatch"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="granularity">Specifies the the unit of work that will be processed by each worker thread.
        /// If ScheduleGranularity.Chunk is passed (the safe default),
        /// work is distributed at the level of whole chunks. This can lead to poor load balancing in cases where the
        /// number of chunks being processed is low (fewer than the number of available worker threads), and the cost to
        /// process each entity is high. In these cases, pass ScheduleGranularity.Entity
        /// to distribute work at the level of individual entities.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be
        /// processed. If limitToEntityArray.IsCreated is false (e.g. for a default-initialized array), this filtering
        /// is disabled, and the job will process all entities that match the query.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatch"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAFter Entities 1.0)")]
        public static unsafe JobHandle ScheduleParallelByRef<T>(
            this ref T jobData,
            EntityQuery query,
            ScheduleGranularity granularity,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
        {
            if (granularity == ScheduleGranularity.Chunk && !limitToEntityArray.IsCreated)
                return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, true);
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, granularity, true, limitToEntityArray);
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
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, false);
        }

        /// <summary>
        /// Runs the job immediately on the current thread.
        /// </summary>
        /// <remarks>This scheduling variant processes each matching chunk as a single batch. All chunks execute
        /// sequentially on the current thread.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatch"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatch"/> implementation type.</typeparam>
        public static unsafe void RunByRef<T>(this ref T jobData, EntityQuery query)
            where T : struct, IJobEntityBatch
        {
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, false);
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
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAFter Entities 1.0)")]
        public static unsafe void Run<T>(this T jobData, EntityQuery query, NativeArray<Entity> limitToEntityArray)
            where T : struct, IJobEntityBatch
        {
            if (!limitToEntityArray.IsCreated)
                ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, false);
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, ScheduleGranularity.Chunk, false, limitToEntityArray);
        }

        /// <summary>
        /// Runs the job immediately on the current thread.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the input array. All
        /// batches are processed sequentially on the current thread.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatch"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be processed.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatch"/> implementation type.</typeparam>
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAFter Entities 1.0)")]
        public static unsafe void RunByRef<T>(this ref T jobData, EntityQuery query, NativeArray<Entity> limitToEntityArray)
            where T : struct, IJobEntityBatch
        {
            if (!limitToEntityArray.IsCreated)
                ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, false);
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, ScheduleGranularity.Chunk, false, limitToEntityArray);
        }

        internal static unsafe void RunWithoutJobsInternal<T>(ref T jobData, ref EntityQuery query)
            where T : struct, IJobEntityBatch
        {
            var queryImpl = query._GetImpl();
            // Complete any running jobs that would affect which chunks/entities match the query.
            // This sync may not be strictly necessary, if the caller doesn't care about filtering the query results.
            // But if they DO care, and they forget this sync, they'll have an undetected race condition. So, let's play it safe.
            queryImpl->SyncFilterTypes();

            if (query.HasFilter() || queryImpl->_QueryData->DoesQueryRequireBatching != 0)
            {
                var chunkCache = query.GetCache(out _);
                int chunkIndex = -1;
                v128 chunkEnabledMask = default;
                while (chunkCache.MoveNextChunk(ref chunkIndex, out var archetypeChunk, out _,
                           out byte chunkRequiresBatching, ref chunkEnabledMask))
                {
                    if (chunkRequiresBatching == 0)
                    {
                        jobData.Execute(archetypeChunk, chunkIndex);
                    }
                    else
                    {
                        int batchIndex = 0;
                        int batchStartIndex = 0;
                        int batchEndIndex = 0;
                        while (EnabledBitUtility.GetNextRange(ref chunkEnabledMask, ref batchStartIndex,
                                   ref batchEndIndex))
                        {
                            archetypeChunk.m_BatchStartEntityIndex = batchStartIndex;
                            archetypeChunk.m_BatchEntityCount = batchEndIndex - batchStartIndex;
                            Assert.AreNotEqual(0, archetypeChunk.Count);
                            jobData.Execute(archetypeChunk, batchIndex++);
                            batchStartIndex = batchEndIndex;
                        }
                    }
                }
            }
            else
            {
                // Fast path for queries with no filtering and no batching
                var cachedChunkList = queryImpl->_QueryData->GetMatchingChunkCache();
                var chunkPtr = cachedChunkList.Ptr;
                int chunkCount = cachedChunkList.Length;
                ArchetypeChunk batch = new ArchetypeChunk(null, cachedChunkList.EntityComponentStore);
                for (int batchIndex = 0; batchIndex < chunkCount; ++batchIndex)
                {
                    batch.m_Chunk = chunkPtr[batchIndex];
                    Assert.AreNotEqual(0, batch.Count);
                    jobData.Execute(batch, batchIndex);
                }
            }
        }

        /// <summary>
        /// Runs the job without using the jobs API.
        /// </summary>
        /// <param name="jobData">The job to execute. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The EntityQuery to run over.</param>
        /// <typeparam name="T">The specific IJobEntityBatch implementation type.</typeparam>
        internal static unsafe void RunByRefWithoutJobs<T>(this ref T jobData, EntityQuery query)
            where T : struct, IJobEntityBatch
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            var queryImpl = query._GetImpl();
            queryImpl->_Access->DependencyManager->ForEachStructuralChange.BeginIsInForEach(queryImpl);
#endif
            RunWithoutJobsInternal(ref jobData, ref query);
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            queryImpl->_Access->DependencyManager->ForEachStructuralChange.EndIsInForEach();
#endif
        }

        /// <summary>
        /// Runs the job without using the jobs API.
        /// </summary>
        /// <param name="jobData">The job to execute.</param>
        /// <param name="query">The EntityQuery to run over.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be processed.</param>
        /// <typeparam name="T">The specific IJobEntityBatch implementation type.</typeparam>
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAFter Entities 1.0)")]
        unsafe internal static void RunWithoutJobs<T>(this T jobData, EntityQuery query, NativeArray<Entity> limitToEntityArray)
            where T : struct, IJobEntityBatch
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            var impl = query._GetImpl();
            impl->_Access->DependencyManager->ForEachStructuralChange.BeginIsInForEach(impl);
            RunWithoutJobsInternal(ref jobData, ref query, (Entity*)limitToEntityArray.GetUnsafeReadOnlyPtr(), limitToEntityArray.Length);
            impl->_Access->DependencyManager->ForEachStructuralChange.EndIsInForEach();
#else
            RunWithoutJobsInternal(ref jobData, ref query, (Entity*)limitToEntityArray.GetUnsafeReadOnlyPtr(), limitToEntityArray.Length);
#endif
        }

        /// <summary>
        /// Runs the job without using the jobs API.
        /// </summary>
        /// <param name="jobData">The job to execute. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The EntityQuery to run over.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be processed.</param>
        /// <typeparam name="T">The specific IJobEntityBatch implementation type.</typeparam>
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAFter Entities 1.0)")]
        unsafe internal static void RunByRefWithoutJobs<T>(this ref T jobData, EntityQuery query, NativeArray<Entity> limitToEntityArray)
            where T : struct, IJobEntityBatch
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            var impl = query._GetImpl();
            impl->_Access->DependencyManager->ForEachStructuralChange.BeginIsInForEach(impl);
            RunWithoutJobsInternal(ref jobData, ref query, (Entity*)limitToEntityArray.GetUnsafeReadOnlyPtr(), limitToEntityArray.Length);
            impl->_Access->DependencyManager->ForEachStructuralChange.EndIsInForEach();
#else
            RunWithoutJobsInternal(ref jobData, ref query, (Entity*)limitToEntityArray.GetUnsafeReadOnlyPtr(), limitToEntityArray.Length);
#endif
        }

        // For now these are necessary/public because they are used by Entities.ForEach codegen,
        // once that has been upgraded, this will be removed
        internal static unsafe void RunWithoutJobsInternal<T>(ref T jobData, ref EntityQuery query, Entity* limitToEntityArray, int limitToEntityArrayLength)
            where T : struct, IJobEntityBatch
        {
            // TODO(DOTS-5666): this path does not support enableable components
            if (query._GetImpl()->_QueryData->DoesQueryRequireBatching != 0)
                throw new ArgumentException("EntityQuery objects with types that implement IEnableableComponent are not currently supported by this operation.");
            using var prebuiltBatchList = new UnsafeList<ArchetypeChunk>(0, Allocator.TempJob);
            ChunkIterationUtility.FindFilteredBatchesForEntityArrayWithQuery(
                query._GetImpl(),
                limitToEntityArray, limitToEntityArrayLength,
                &prebuiltBatchList);

            ArchetypeChunk* chunks = prebuiltBatchList.Ptr;
            int chunkCounts = prebuiltBatchList.Length;
            for (int i = 0; i != chunkCounts; i++)
                jobData.Execute(chunks[i], i);
        }

        internal static unsafe JobHandle ScheduleInternal<T>(
            ref T jobData,
            EntityQuery query,
            JobHandle dependsOn,
            ScheduleMode mode,
            bool isParallel)
            where T : struct, IJobEntityBatch
        {
            var queryImpl = query._GetImpl();
            var queryData = queryImpl->_QueryData;
            var cachedChunks = queryData->GetMatchingChunkCache();
            var batchCount = cachedChunks.Length;

            JobEntityBatchWrapper<T> jobEntityBatchWrapper = new JobEntityBatchWrapper<T>
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                safety = new EntityQuerySafetyHandles(queryImpl),
#endif
                MatchingArchetypes = queryData->MatchingArchetypes,
                CachedChunks = cachedChunks,
                Filter = queryImpl->_Filter,

                JobData = jobData,
                IsParallel = isParallel ? 1 : 0,

                UsePrebuiltBatchList = 0,
                SkipSubChunkBatching = queryData->DoesQueryRequireBatching == 0 ? 1 : 0
            };
            JobEntityBatchProducer<T>.Initialize();
            var reflectionData = JobEntityBatchProducer<T>.reflectionData.Data;
            CollectionHelper.CheckReflectionDataCorrect<T>(reflectionData);

            var scheduleParams = new JobsUtility.JobScheduleParameters(
                UnsafeUtility.AddressOf(ref jobEntityBatchWrapper),
                reflectionData,
                dependsOn,
                mode);

            var result = default(JobHandle);
            if (!isParallel)
            {
                result = JobsUtility.Schedule(ref scheduleParams);
            }
            else
            {
                // TODO(DOTS-5740): pick a better innerloopBatchCount
                result = JobsUtility.ScheduleParallelFor(ref scheduleParams, batchCount, 1);
            }

            return result;
        }

        // Slower variant that handles cases where the batch list needs to be pre-built on the main thread at schedule time.
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAFter Entities 1.0)")]
        internal static unsafe JobHandle ScheduleInternal<T>(
            ref T jobData,
            EntityQuery query,
            JobHandle dependsOn,
            ScheduleMode mode,
            ScheduleGranularity granularity,
            bool isParallel,
            NativeArray<Entity> limitToEntityArray)
            where T : struct, IJobEntityBatch
        {
            var queryImpl = query._GetImpl();
            // TODO(DOTS-5666): this path does not support enableable components
            if (queryImpl->_QueryData->DoesQueryRequireBatching != 0)
                throw new ArgumentException("EntityQuery objects with types that implement IEnableableComponent are not currently supported by this operation.");
            var queryData = queryImpl->_QueryData;

            var cachedChunks = queryData->GetMatchingChunkCache();

            var prebuiltBatchList = default(UnsafeList<ArchetypeChunk>);
            var perBatchMatchingArchetypeIndex = default(UnsafeList<int>);

            int maxBatchCount = limitToEntityArray.IsCreated ? limitToEntityArray.Length : cachedChunks.Length;
            prebuiltBatchList = new UnsafeList<ArchetypeChunk>(maxBatchCount, Allocator.TempJob);
            perBatchMatchingArchetypeIndex = new UnsafeList<int>(maxBatchCount, Allocator.TempJob);

            bool isFiltering = queryImpl->_Filter.RequiresMatchesFilter;
            var matchingArchetypes = queryData->MatchingArchetypes;
            var ecs = queryImpl->_Access->EntityComponentStore;
            if (limitToEntityArray.IsCreated)
            {
                // Limiting execution to a specific array of entities produces a list of batches containing only
                // those entities.

                // Forces the creation of an EntityQueryMask, which is necessary to filter batches.
                var access = queryImpl->_Access;
                access->EntityQueryManager->GetEntityQueryMask(queryData, access->EntityComponentStore);

                if (granularity == ScheduleGranularity.Chunk)
                {
                    ChunkIterationUtility.FindBatchesForEntityArrayWithQuery(
                        access->EntityComponentStore,
                        queryData,
                        queryImpl->_Filter.RequiresMatchesFilter || queryData->DoesQueryRequireBatching != 0,
                        (Entity*) limitToEntityArray.GetUnsafePtr(),
                        limitToEntityArray.Length,
                        &prebuiltBatchList,
                        &perBatchMatchingArchetypeIndex);
                }
                else if (granularity == ScheduleGranularity.Entity)
                {
                    // Walk the limitToEntityArray and generate a batch for each entity
                    var mask = queryData->EntityQueryMask;
                    foreach (var entity in limitToEntityArray)
                    {
                        if (!mask.MatchesIgnoreFilter(entity))
                            continue;
                        var entityInChunk = ecs->GetEntityInChunk(entity);
                        var chunk = entityInChunk.Chunk;
                        var chunkIndex = chunk->ListIndex;
                        prebuiltBatchList.Add(new ArchetypeChunk
                        {
                            m_Chunk = chunk,
                            m_EntityComponentStore = ecs,
                            m_BatchStartEntityIndex = entityInChunk.IndexInChunk,
                            m_BatchEntityCount = 1,
                        });
                        if (isFiltering)
                        {
                            var matchingArchetypeIndex =
                                EntityQueryManager.FindMatchingArchetypeIndexForArchetype(ref matchingArchetypes,
                                    chunk->Archetype);
                            perBatchMatchingArchetypeIndex.Add(matchingArchetypeIndex);
                        }
                    }
                }
            }
            else
            {
                Assert.AreEqual(ScheduleGranularity.Entity, granularity,
                    "This code path should never be taken; the simpler ScheduleInternal() variant handles this case.");
                // Construct a pre-build batch list, with one batch per entity.
                // All filtering (both chunk- and entity-level) is still performed on worker threads at execution time.

                for(int chunkIndex=0; chunkIndex<cachedChunks.Length; ++chunkIndex)
                {
                    var chunk = cachedChunks.Ptr[chunkIndex];

                    Assert.AreNotEqual(0, chunk->Count);

                    int matchingArchetypeIndex = matchingArchetypeIndex =
                        EntityQueryManager.FindMatchingArchetypeIndexForArchetype(ref matchingArchetypes,
                            chunk->Archetype);

                    int chunkEntityCount = chunk->Count;
                    for (int entityInChunk = 0; entityInChunk < chunkEntityCount; ++entityInChunk)
                    {
                        prebuiltBatchList.Add(new ArchetypeChunk
                        {
                            m_Chunk = chunk,
                            m_EntityComponentStore = ecs,
                            m_BatchStartEntityIndex = entityInChunk,
                            m_BatchEntityCount = 1,
                        });
                        perBatchMatchingArchetypeIndex.Add(matchingArchetypeIndex);
                    }
                }
            }

            var batchCount = prebuiltBatchList.Length;

            JobEntityBatchWrapper<T> jobEntityBatchWrapper = new JobEntityBatchWrapper<T>
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                safety = new EntityQuerySafetyHandles(queryImpl),
#endif
                MatchingArchetypes = queryData->MatchingArchetypes,
                CachedChunks = cachedChunks,
                Filter = queryImpl->_Filter,

                JobData = jobData,
                IsParallel = isParallel ? 1 : 0,
                SkipSubChunkBatching = 0, // limitToEntityArray path never takes the fast path

                UsePrebuiltBatchList = 1,
                PrebuiltBatchList = prebuiltBatchList,
                PrebuiltBatchListMatchingArchetypeIndices = perBatchMatchingArchetypeIndex
            };
            JobEntityBatchProducer<T>.Initialize();
            var reflectionData = JobEntityBatchProducer<T>.reflectionData.Data;
            CollectionHelper.CheckReflectionDataCorrect<T>(reflectionData);

            var scheduleParams = new JobsUtility.JobScheduleParameters(
                UnsafeUtility.AddressOf(ref jobEntityBatchWrapper),
                reflectionData,
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

            result = BurstableCollectionsHacks.MakeDisposeJob(prebuiltBatchList).Schedule(result);
            result = BurstableCollectionsHacks.MakeDisposeJob(perBatchMatchingArchetypeIndex).Schedule(result);

            return result;
        }

        internal struct JobEntityBatchProducer<T>
            where T : struct, IJobEntityBatch
        {
            internal static readonly SharedStatic<IntPtr> reflectionData = SharedStatic<IntPtr>.GetOrCreate<JobEntityBatchProducer<T>>();

            [BurstDiscard]
            internal static void Initialize()
            {
                if (reflectionData.Data == IntPtr.Zero)
                    reflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(JobEntityBatchWrapper<T>), typeof(T), (ExecuteJobFunction)Execute);
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
                var chunkCache = new UnsafeChunkCache(jobWrapper.Filter,
                    jobWrapper.SkipSubChunkBatching == 0,
                    jobWrapper.CachedChunks, jobWrapper.MatchingArchetypes.Ptr);

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
                            // TODO(DOTS-5666): skip entities that fail the enabled-bits check.
                            // We can't do this at schedule time for safety reasons (we don't want Schedule to have to
                            // block on existing jobs in order to safely read the chunk's enabled bits).

                            Assert.AreNotEqual(0, batch.Count);
                            jobWrapper.JobData.Execute(batch, batchIndex);
                        }
                    }
                    else
                    {
                        if (jobWrapper.SkipSubChunkBatching != 0 && !isFiltering)
                        {
                            var chunkPtr = chunks.Ptr;
                            ArchetypeChunk batch = new ArchetypeChunk(null, chunks.EntityComponentStore);
                            for (int batchIndex = beginBatchIndex; batchIndex < endBatchIndex; ++batchIndex)
                            {
                                batch.m_Chunk = chunkPtr[batchIndex];
                                Assert.AreNotEqual(0, batch.Count);
                                jobWrapper.JobData.Execute(batch, batchIndex);
                            }
                        }
                        else
                        {
                            // Update chunkCache range
                            chunkCache.Length = endBatchIndex;
                            int chunkIndex = beginBatchIndex - 1;

                            v128 chunkEnabledMask = default;
                            while (chunkCache.MoveNextChunk(ref chunkIndex, out var archetypeChunk, out var chunkEntityCount,
                                       out byte chunkRequiresBatching, ref chunkEnabledMask))
                            {
                                if (chunkRequiresBatching == 0)
                                {
                                    // TODO(DOTS-5401): chunkIndex is not a unique batch index
                                    jobWrapper.JobData.Execute(archetypeChunk, chunkIndex);
                                }
                                else
                                {
                                    // TODO(DOTS-5401): batchIndex is not a unique batch index
                                    int batchIndex = 0;
                                    int batchStartIndex = 0;
                                    int batchEndIndex = 0;
                                    while (EnabledBitUtility.GetNextRange(ref chunkEnabledMask, ref batchStartIndex,
                                               ref batchEndIndex))
                                    {
                                        archetypeChunk.m_BatchStartEntityIndex = batchStartIndex;
                                        archetypeChunk.m_BatchEntityCount = batchEndIndex - batchStartIndex;
                                        Assert.AreNotEqual(0, archetypeChunk.Count);
                                        jobWrapper.JobData.Execute(archetypeChunk, batchIndex++);
                                        batchStartIndex = batchEndIndex;
                                    }
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
}
