using System;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine.Scripting;
using System.Diagnostics;
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
    /// * <see cref="JobEntityBatchExtensions.ScheduleParallel{T}(T, EntityQuery, JobHandle)"/>,
    /// * or <see cref="JobEntityBatchExtensions.Run{T}(T, EntityQuery)"/>
    ///
    /// all the entities of each chunk are processed as
    /// a single batch. The <see cref="ArchetypeChunk"/> object passed to the `Execute` function of your job struct provides access
    /// to the components of all the entities in the chunk.
    ///
    /// Use <see cref="JobEntityBatchExtensions.ScheduleParallel{T}(T, EntityQuery, ScheduleGranularity.Entity, NativeArray&lt;Entity&gt;, JobHandle)"/>
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
        /// function. If you use <see cref="JobEntityBatchExtensions.ScheduleParallel{T}(T, EntityQuery, ScheduleGranularity.Entity, NativeArray&lt;Entity&gt;, JobHandle)"/>
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
    /// run on multiple worker threads using `ScheduleParallel()`. In most cases, <see cref="ScheduleGranularity.Chunk"/>
    /// should be used.
    /// </summary>
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

            public UnsafeList<ArchetypeChunk> PrebuiltBatchList;
            public UnsafeList<int> PrebuiltBatchListMatchingArchetypeIndices;

            public int IsParallel;
            public int UsePrebuiltBatchList;
            public int SkipSubChunkBatching;
        }

        /// <summary>
        /// This method is only to be called by automatically generated setup code.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static void EarlyJobInit<T>()
            where T : struct, IJobEntityBatch
        {
            JobEntityBatchProducer<T>.Initialize();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckReflectionDataCorrect(IntPtr reflectionData)
        {
            if (reflectionData == IntPtr.Zero)
                throw new InvalidOperationException("Reflection data was not set up by a call to Initialize()");
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
        [Obsolete("The batchesPerChunk parameter has been replaced. Instead, use ScheduleGranularity.Chunk (the default) or ScheduleGranularity.Entity. (RemovedAfter 2021-08-10)")]
        public static unsafe JobHandle ScheduleParallel<T>(
            this T jobData,
            EntityQuery query,
            int batchesPerChunk,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
        {
            if (batchesPerChunk == 1)
                return ScheduleParallel(jobData, query, dependsOn);
            return ScheduleParallel(jobData, query, ScheduleGranularity.Entity, default, dependsOn);
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
        [Obsolete("The batchesPerChunk parameter has been replaced. Instead, use ScheduleGranularity.Chunk (the default) or ScheduleGranularity.Entity. (RemovedAfter 2021-08-10)")]
        public static unsafe JobHandle ScheduleParallelByRef<T>(
            this ref T jobData,
            EntityQuery query,
            int batchesPerChunk,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
        {
            if (batchesPerChunk == 1)
                return ScheduleParallelByRef(ref jobData, query, dependsOn);
            return ScheduleParallelByRef(ref jobData, query, ScheduleGranularity.Entity, default, dependsOn);
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatch"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the entity array. Each
        /// batch can execute in parallel.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatch"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="granularity">Specifies the the unit of work that will be processed by each worker thread.
        /// If <see cref="ScheduleGranularity.Chunk"/> is passed (the safe default),
        /// work is distributed at the level of whole chunks. This can lead to poor load balancing in cases where the
        /// number of chunks being processed is low (fewer than the number of available worker threads), and the cost to
        /// process each entity is high. In these cases, pass <see cref="ScheduleGranularity.Entity"/>
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
        [Obsolete("This function now takes a ScheduleGranularity parameter. If in doubt, pass ScheduleGranularity.Chunk. (RemovedAfter 2021-08-10)")]
        public static unsafe JobHandle ScheduleParallel<T>(
            this T jobData,
            EntityQuery query,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
        {
            if (!limitToEntityArray.IsCreated)
                return ScheduleParallel(jobData, query, dependsOn);
            return ScheduleParallel(jobData, query, ScheduleGranularity.Chunk, limitToEntityArray, dependsOn);
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
        /// If <see cref="ScheduleGranularity.Chunk"/> is passed (the safe default),
        /// work is distributed at the level of whole chunks. This can lead to poor load balancing in cases where the
        /// number of chunks being processed is low (fewer than the number of available worker threads), and the cost to
        /// process each entity is high. In these cases, pass <see cref="ScheduleGranularity.Entity"/>
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
        [Obsolete("This function now takes a ScheduleGranularity parameter. If in doubt, pass ScheduleGranularity.Chunk. (RemovedAfter 2021-08-10)")]
        public static unsafe JobHandle ScheduleParallelByRef<T>(
            this ref T jobData,
            EntityQuery query,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatch
        {
            if (!limitToEntityArray.IsCreated)
                return ScheduleParallelByRef(ref jobData, query, dependsOn);
            return ScheduleParallelByRef(ref jobData, query, ScheduleGranularity.Chunk, limitToEntityArray, dependsOn);
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
        public static unsafe void RunByRef<T>(this ref T jobData, EntityQuery query, NativeArray<Entity> limitToEntityArray)
            where T : struct, IJobEntityBatch
        {
            if (!limitToEntityArray.IsCreated)
                ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, false);
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, ScheduleGranularity.Chunk, false, limitToEntityArray);
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

        unsafe internal static void RunWithoutJobsInternal<T>(ref T jobData, Chunk** chunks, int chunkCount, EntityComponentStore* store)
            where T : struct, IJobEntityBatch
        {
            for (int i =0; i != chunkCount;i++)
                jobData.Execute(new ArchetypeChunk(chunks[i], store), i);
        }


        /// <summary>
        /// Runs the job without using the jobs API.
        /// </summary>
        /// <param name="jobData">The job to execute.</param>
        /// <param name="query">The EntityQuery to run over.</param>
        /// <typeparam name="T">The specific IJobEntityBatch implementation type.</typeparam>
        public static unsafe void RunWithoutJobs<T>(ref T jobData, EntityQuery query)
            where T : struct, IJobEntityBatch
        {
            if (query.HasFilter())
            {
                // Filtered queries still use the slower, ArchetypeChunkIterator path
                var chunkIterator = query.GetArchetypeChunkIterator();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var access = query._GetImpl()->_Access;
                try
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange++;
                    RunWithoutJobsInternal(ref jobData, ref chunkIterator);
                }
                finally
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange--;
                }
#else
                RunWithoutJobsInternal(ref jobData, ref chunkIterator);
#endif
            }
            else
            {
                // Unfiltered queries can use the cached matching chunk list
                var impl = query._GetImpl();
                var matching = impl->_QueryData->GetMatchingChunkCache();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var access = query._GetImpl()->_Access;
                try
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange++;
                    RunWithoutJobsInternal(ref jobData, matching.Ptr, matching.Length, matching.EntityComponentStore);
                }
                finally
                {
                    access->DependencyManager->IsInForEachDisallowStructuralChange--;
                }
#else
                RunWithoutJobsInternal(ref jobData, matching.Ptr, matching.Length, matching.EntityComponentStore);
#endif
            }
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

        public static unsafe void RunWithoutJobsInternal<T>(ref T jobData, ref EntityQuery query, IntPtr limitToEntityArrayPtr, int limitToEntityArrayLength)
            where T : struct, IJobEntityBatch
        {
            RunWithoutJobsInternal(ref jobData, ref query, (Entity*)limitToEntityArrayPtr, limitToEntityArrayLength);
        }

        public static unsafe void RunWithoutJobsInternal<T>(ref T jobData, ref EntityQuery query, Entity* limitToEntityArray, int limitToEntityArrayLength)
            where T : struct, IJobEntityBatch
        {
            var prebuiltBatchList = new UnsafeList<ArchetypeChunk>(0, Allocator.TempJob);
            try
            {
                ChunkIterationUtility.FindFilteredBatchesForEntityArrayWithQuery(
                    query._GetImpl(),
                    limitToEntityArray, limitToEntityArrayLength,
                    &prebuiltBatchList);

                ArchetypeChunk* chunks = prebuiltBatchList.Ptr;
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
                // All IJobEntityBatch jobs have a EntityManager safety handle to ensure that BeforeStructuralChange throws an error if
                // jobs without any other safety handles are still running (haven't been synced).
                safety = new EntitySafetyHandle {m_Safety = queryImpl->SafetyHandles->GetEntityManagerSafetyHandle()},
#endif

                MatchingArchetypes = queryData->MatchingArchetypes,
                CachedChunks = cachedChunks,
                Filter = queryImpl->_Filter,

                JobData = jobData,
                IsParallel = isParallel ? 1 : 0,

                UsePrebuiltBatchList = 0,
                SkipSubChunkBatching = queryData->EnableableComponentsCountAll == 0 && queryData->EnableableComponentsCountNone == 0 ? 1 : 0
            };

            var reflectionData = JobEntityBatchProducer<T>.reflectionData.Data;
            CheckReflectionDataCorrect(reflectionData);

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

            return result;
        }

        // Slower variant that handles cases where the batch list needs to be pre-built on the main thread at schedule time.
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
                        queryImpl->_Filter.RequiresMatchesFilter || queryData->DoesQueryRequireBatching,
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
                        if (!mask.Matches(entity))
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
                for(int chunkIndex=0; chunkIndex<cachedChunks.Length; ++chunkIndex)
                {
                    var chunk = cachedChunks.Ptr[chunkIndex];

                    Assert.AreNotEqual(0, chunk->Count);
                    int matchingArchetypeIndex = -1;
                    if (isFiltering)
                    {
                        matchingArchetypeIndex =
                            EntityQueryManager.FindMatchingArchetypeIndexForArchetype(ref matchingArchetypes,
                                chunk->Archetype);
                    }

                    if (granularity == ScheduleGranularity.Chunk)
                    {
                        Assert.IsTrue(false,
                            "This code path should never be taken; the simpler ScheduleInternal() variant handles this case.");
                    }
                    else if (granularity == ScheduleGranularity.Entity)
                    {
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
                            if (isFiltering)
                                perBatchMatchingArchetypeIndex.Add(matchingArchetypeIndex);
                        }
                    }
                }
            }

            var batchCount = prebuiltBatchList.Length;

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
                IsParallel = isParallel ? 1 : 0,
                SkipSubChunkBatching = 0, // limitToEntityArray path never takes the fast path

                UsePrebuiltBatchList = 1,
                PrebuiltBatchList = prebuiltBatchList,
                PrebuiltBatchListMatchingArchetypeIndices = perBatchMatchingArchetypeIndex
            };

            var reflectionData = JobEntityBatchProducer<T>.reflectionData.Data;
            CheckReflectionDataCorrect(reflectionData);

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

            [Preserve]
            internal static void Initialize()
            {
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

            [SkipLocalsInit]
            internal unsafe static void ExecuteInternal(
                ref JobEntityBatchWrapper<T> jobWrapper,
                IntPtr bufferRangePatchData,
                ref JobRanges ranges,
                int jobIndex)
            {
                var chunks = jobWrapper.CachedChunks;
                var ecs = chunks.EntityComponentStore;
                var prebuiltBatches = (ArchetypeChunk*)jobWrapper.PrebuiltBatchList.Ptr;

                // Preallocate
                var stackAllocatedBatchList = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];

                bool skipSubChunkBatching = jobWrapper.SkipSubChunkBatching == 1;
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
                        for (int batchIndex = beginBatchIndex; batchIndex < endBatchIndex; ++batchIndex)
                        {
                            var chunkIndex = batchIndex;
                            var chunk = chunks.Ptr[chunkIndex];
                            var match = jobWrapper.MatchingArchetypes.Ptr[chunks.PerChunkMatchingArchetypeIndex->Ptr[chunkIndex]];

                            if (isFiltering && !chunk->MatchesFilter(match, ref jobWrapper.Filter))
                                continue;

                            // If we can always take the fast path, just execute the chunk now
                            if (skipSubChunkBatching)
                            {
                                var batch = new ArchetypeChunk(chunk, chunks.EntityComponentStore);
                                Assert.AreNotEqual(0, batch.Count);
                                jobWrapper.JobData.Execute(batch, batchIndex);

                                continue;
                            }

                            // If we can't always take the fast path, figure out if we can for this chunk
                            var chunkShouldBatch = ChunkIterationUtility.DoesChunkRequireBatching(chunk, match, out var shouldSkipChunk);

                            // Chunks are skipped if we know nothing inside will be processed
                            if (shouldSkipChunk)
                                continue;

                            if (chunkShouldBatch)
                            {
                                ChunkIterationUtility.FindBatchesForChunk(chunk, match, ecs, stackAllocatedBatchList, out var batchCount);

                                for (int i = 0; i < batchCount; ++i)
                                {
                                    var batch = stackAllocatedBatchList[i];
                                    Assert.AreNotEqual(0, batch.Count);
                                    jobWrapper.JobData.Execute(batch, i);
                                }
                            }
                            else
                            {
                                var batch = new ArchetypeChunk(chunk, chunks.EntityComponentStore);
                                Assert.AreNotEqual(0, batch.Count);
                                jobWrapper.JobData.Execute(batch, batchIndex);
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
        // This thing *should* be Burst compatible but isn't due to UnsafeList.Dispose() using IJob that
        // contains an access to a static IntPtr which is not burst compatible.
        // It will become burst compatible as soon as we make UnsafeList.Dispose() use IJobBurstSchedulable.
        [NotBurstCompatible]
        public static void Schedule()
        {
            new DummyJobEntityBatch().Run(default(EntityQuery));
        }
    }
}
