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
    internal static class BurstableCollectionsHacks
    {
        // TEMP copy from Collections so we can do this from Burst code
        [BurstCompile]
        internal unsafe struct WorkaroundUnsafeDisposeJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public void* Ptr;
            public Allocator Allocator;

            public void Execute()
            {
                if (Allocator > Allocator.None)
                {
                    var handle = (AllocatorManager.AllocatorHandle)Allocator;
                    AllocatorManager.Free(handle, Ptr);
                }
            }
        }

        internal static unsafe WorkaroundUnsafeDisposeJob MakeDisposeJob<T>(UnsafeList<T> list) where T: unmanaged
        {
            return new WorkaroundUnsafeDisposeJob { Ptr = list.Ptr, Allocator = (Allocator) list.Allocator.Value };
        }
    }

    /// <summary>
    /// IJobEntityBatchWithIndex is a variant of [IJobEntityBatch] that provides an additional indexOfFirstEntityInQuery parameter, which
    /// provides a per-batch index that is the aggregate of all previous batch counts.
    ///
    /// [IJob]: https://docs.unity3d.com/ScriptReference/Unity.Jobs.IJob.html
    /// [chunk]: xref:concepts-archetypes#archetype-chunks
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
    /// * <see cref="JobEntityBatchIndexExtensions.ScheduleParallel{T}(T, EntityQuery, JobHandle)"/>,
    /// * or <see cref="JobEntityBatchIndexExtensions.Run{T}(T, EntityQuery)"/>
    ///
    /// all the entities of each chunk are processed as
    /// a single batch. The <see cref="ArchetypeChunk"/> object passed to the `Execute` function of your job struct provides access
    /// to the components of all the entities in the chunk.
    ///
    /// Use <see cref="JobEntityBatchIndexExtensions.ScheduleParallel{T}(T, EntityQuery, ScheduleGranularity, NativeArray&lt;Entity&gt;, JobHandle)"/>
    /// to force each batch to contain only a single entity. This allows multiple worker threads to process the entities
    /// within a chunk concurrently, which may lead to better load balancing if the number of entities to process is relatively
    /// small and the amount of work per entity is relatively high. As always, you should profile your job to find the
    /// best arrangement for your specific application.
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
    /// [Using IJobEntityBatch]: xref:iterating-data-ijobentitybatch
    /// [chunks]: xref:concepts-archetypes#archetype-chunks
    /// </remarks>
    /// <seealso cref="JobEntityBatchIndexExtensions"/>
    /// <seealso cref="IJobEntityBatch"/>
    [JobProducerType(typeof(JobEntityBatchIndexExtensions.JobEntityBatchIndexProducer<>))]
    [Obsolete("This job type will be removed in a future release. Existing implementations should be migrated to IJobChunk. See the upgrade guide for details. (RemovedAfter Entities 1.0)")]
    public interface IJobEntityBatchWithIndex
    {
        /// <summary>
        /// Implement the `Execute` function to perform a unit of work on an <see cref="ArchetypeChunk"/> representing
        /// a contiguous batch of entities within a chunk.
        /// </summary>
        /// <remarks>
        /// The chunks selected by the <see cref="EntityQuery"/> used to schedule the job are the input to your `Execute`
        /// function. If you use <see cref="JobEntityBatchIndexExtensions.ScheduleParallel{T}(T, EntityQuery, ScheduleGranularity, NativeArray&lt;Entity&gt;, JobHandle)"/>
        /// to schedule the job, the entities in each matching chunk are distributed to worker threads individually,
        /// and the `Execute` function is called once for each batch (containing a single `Entity`). When you use one of the
        /// other scheduling or run methods, the `Execute` function is called once per matching chunk.
        /// </remarks>
        /// <param name="batchInChunk">An object providing access to a batch of entities within a chunk.</param>
        /// <param name="batchIndex">The index of the current batch within the list of all batches in all chunks found by the
        /// job's <see cref="EntityQuery"/>. Note that batches are not necessarily processed in index order. Guaranteed
        /// to be contiguous and zero-based.</param>
        /// <param name="indexOfFirstEntityInQuery">The index of the first entity in the current chunk within the list of all
        /// entities in all the chunks found by the Job's <see cref="EntityQuery"/>.</param>
        void Execute(ArchetypeChunk batchInChunk, int batchIndex, int indexOfFirstEntityInQuery);
    }

    /// <summary>
    /// Extensions for scheduling and running <see cref="IJobEntityBatchWithIndex"/> jobs.
    /// </summary>
    [Obsolete("IJobEntityBatchWithIndex is deprecated.")]
    public static class JobEntityBatchIndexExtensions
    {
        internal unsafe struct JobEntityBatchIndexWrapper<T> where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
#pragma warning disable 414
            [ReadOnly] public EntityQuerySafetyHandles safety;
#pragma warning restore
#endif
            public T JobData;

            [NativeDisableUnsafePtrRestriction]
            public void* PrefilterData;

            public int IsParallel;
        }

        /// <summary>
        /// Gathers and caches reflection data for the internal job system's managed bindings. Unity is responsible for calling this method - don't call it yourself.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <remarks>
        /// When the Jobs package is included in the project, Unity generates code to call EarlyJobInit at startup. This allows Burst compiled code to schedule jobs because the reflection part of initialization, which is not compatible with burst compiler constraints, has already happened in EarlyJobInit.
        ///
        /// __Note__: While the Jobs package code generator handles this automatically for all closed job types, you must register those with generic arguments (like IJobEntityBatchWithIndex&amp;lt;MyJobType&amp;lt;T&amp;gt;&amp;gt;) manually for each specialization with [[Unity.Jobs.RegisterGenericJobTypeAttribute]].
        /// </remarks>
        public static void EarlyJobInit<T>()
            where T : struct, IJobEntityBatchWithIndex
        {
            JobEntityBatchIndexProducer<T>.Initialize();
        }

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
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, false);
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatchWithIndex"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each matching chunk as a single batch. All chunks execute
        /// sequentially.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance. In this variant, the jobData is
        /// passed by reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleByRef<T>(
            this ref T jobData,
            EntityQuery query,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, false);
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
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAFter Entities 1.0)")]
        public static unsafe JobHandle Schedule<T>(
            this T jobData,
            EntityQuery query,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
        {
            if (!limitToEntityArray.IsCreated)
                return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, false);
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, ScheduleGranularity.Chunk, false, limitToEntityArray);
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatchWithIndex"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the entity array. All batches are processed
        /// sequentially.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance. In this variant, the jobData is
        /// passed by reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be processed.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAFter Entities 1.0)")]
        public static unsafe JobHandle ScheduleByRef<T>(
            this ref T jobData,
            EntityQuery query,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
        {
            if (!limitToEntityArray.IsCreated)
                return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, false);
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Single, ScheduleGranularity.Chunk, false, limitToEntityArray);
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatchWithIndex"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each matching chunk as a single batch. Each
        /// chunk can execute in parallel.</remarks>
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
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, true);
        }


        /// <summary>
        /// Adds an <see cref="IJobEntityBatchWithIndex"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each matching chunk as a single batch. Each
        /// chunk can execute in parallel.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance. In this variant, the jobData is
        /// passed by reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleParallelByRef<T>(
            this ref T jobData,
            EntityQuery query,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, true);
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatchWithIndex"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the entity array. Each
        /// batch can execute in parallel.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance.</param>
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
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAFter Entities 1.0)")]
        public static unsafe JobHandle ScheduleParallel<T>(
            this T jobData,
            EntityQuery query,
            ScheduleGranularity granularity,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
        {
            if (granularity == ScheduleGranularity.Chunk && !limitToEntityArray.IsCreated)
                return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, true);
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, granularity, true, limitToEntityArray);
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatchWithIndex"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the entity array. Each
        /// batch can execute in parallel.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance. In this variant, the jobData is
        /// passed by reference, which may be necessary for unusually large job structs.</param>
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
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAFter Entities 1.0)")]
        public static unsafe JobHandle ScheduleParallelByRef<T>(
            this ref T jobData,
            EntityQuery query,
            ScheduleGranularity granularity,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
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
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        public static unsafe void Run<T>(this T jobData, EntityQuery query)
            where T : struct, IJobEntityBatchWithIndex
        {
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, false);
        }

        /// <summary>
        /// Runs the job immediately on the current thread.
        /// </summary>
        /// <remarks>This scheduling variant processes each matching chunk as a single batch. All chunks execute
        /// sequentially on the current thread.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance. In this variant, the jobData is
        /// passed by reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        public static unsafe void RunByRef<T>(this ref T jobData, EntityQuery query)
            where T : struct, IJobEntityBatchWithIndex
        {
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, false);
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
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAFter Entities 1.0)")]
        public static unsafe void Run<T>(this T jobData, EntityQuery query, NativeArray<Entity> limitToEntityArray)
            where T : struct, IJobEntityBatchWithIndex
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
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance. In this variant, the jobData is
        /// passed by reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="limitToEntityArray">A list of entities to limit execution to. Only entities in the list will be processed.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        [Obsolete("The limitToEntityArray feature will be removed. As a replacement, add the entities to a NativeHashSet and use NativeHashSet.Contains(e) in the job as an early out. (RemovedAFter Entities 1.0)")]
        public static unsafe void RunByRef<T>(this ref T jobData, EntityQuery query, NativeArray<Entity> limitToEntityArray)
            where T : struct, IJobEntityBatchWithIndex
        {
            if (!limitToEntityArray.IsCreated)
                ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, false);
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, ScheduleGranularity.Chunk, false, limitToEntityArray);
        }

        // For now these are necessary/public because they are used by Entities.ForEach codegen,
        // once that has been upgraded, this will be removed
        internal static unsafe void RunWithoutJobsInternal<T>(ref T jobData, ref EntityQuery query)
            where T : struct, IJobEntityBatchWithIndex
        {
            // Complete any running jobs that would affect which chunks/entities match the query.
            // This sync may not be strictly necessary, if the caller doesn't care about filtering the query results.
            // But if they DO care, and they forget this sync, they'll have an undetected race condition. So, let's play it safe.
            query._GetImpl()->SyncFilterTypes();

            int entitiesSeen = 0;
            var chunkCache = query.GetCache(out var _);
            int chunkIndex = -1;
            v128 chunkEnabledMask = default;
            while (chunkCache.MoveNextChunk(ref chunkIndex, out var archetypeChunk, out var chunkEntityCount,
                       out byte chunkRequiresBatching, ref chunkEnabledMask))
            {
                if (chunkRequiresBatching == 0)
                {
                    jobData.Execute(archetypeChunk, chunkIndex, entitiesSeen);
                    entitiesSeen += chunkEntityCount;
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
                        jobData.Execute(archetypeChunk, batchIndex++, entitiesSeen);
                        entitiesSeen += archetypeChunk.m_BatchEntityCount;
                        batchStartIndex = batchEndIndex;
                    }
                }
            }
        }

        /// <summary>
        /// Runs the job without using the jobs API.
        /// </summary>
        /// <param name="jobData">The job to execute. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific IJobEntityBatch implementation type.</typeparam>
        internal static unsafe void RunByRefWithoutJobs<T>(this ref T jobData, EntityQuery query)
            where T : struct, IJobEntityBatchWithIndex
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

        private static unsafe void* AllocatePrefilterData(int expectedBatchCount, Allocator allocator, out UnsafeList<ArchetypeChunk>* outBatchList, out UnsafeList<int>* outIndexList)
        {
            var sizeOfArrayHeaders = sizeof(UnsafeList<int>) * 2;
            var prefilterDataPtr = (byte*)Memory.Unmanaged.Allocate(sizeOfArrayHeaders, 8, allocator);

            var ptr = prefilterDataPtr;
            outBatchList = (UnsafeList<ArchetypeChunk>*)ptr;
            ptr += sizeof(UnsafeList<int>);
            outIndexList = (UnsafeList<int>*) ptr;

            *outBatchList = new UnsafeList<ArchetypeChunk>(expectedBatchCount, allocator, NativeArrayOptions.UninitializedMemory);
            *outIndexList = new UnsafeList<int>(expectedBatchCount, allocator, NativeArrayOptions.UninitializedMemory);

            return prefilterDataPtr;
        }

        private static unsafe void UnpackPrefilterData(byte* prefilterDataPtr, out UnsafeList<ArchetypeChunk> outBatchList, out UnsafeList<int> outIndexList)
        {
            var ptr = prefilterDataPtr;
            outBatchList = *(UnsafeList<ArchetypeChunk>*)ptr;
            ptr += sizeof(UnsafeList<int>);
            outIndexList = *(UnsafeList<int>*) ptr;
        }

        internal static unsafe JobHandle ScheduleInternal<T>(
            ref T jobData,
            EntityQuery query,
            JobHandle dependsOn,
            ScheduleMode mode,
            bool isParallel)
            where T : struct, IJobEntityBatchWithIndex
        {
            var queryImpl = query._GetImpl();
            var queryData = queryImpl->_QueryData;
            var worldUpdateAllocator = queryImpl->_Access->m_WorldUnmanaged.UpdateAllocator.ToAllocator;

            var expectedBatchCount = query.CalculateChunkCountWithoutFiltering();

            var prefilterDataPtr = AllocatePrefilterData(expectedBatchCount, worldUpdateAllocator, out var batchList, out var indexList);

            var prefilterHandle = dependsOn;
            var prefilterJob = new PrefilterForJobEntityBatchWithIndex
            {
                ChunkList = queryData->GetMatchingChunkCache(),
                MatchingArchetypes = queryData->MatchingArchetypes,
                Filter = queryImpl->_Filter,
                EntityComponentStore = queryImpl->_Access->EntityComponentStore,
                OutPrefilteredBatches = batchList,
                OutPrefilteredBatchEntityIndices = indexList,
                SkipSubChunkBatching = queryData->DoesQueryRequireBatching == 0 ? 1 : 0
            };

            if (mode != ScheduleMode.Run)
                prefilterHandle = prefilterJob.Schedule(dependsOn);
            else
                prefilterJob.Run();

            // We can't use try {} catch {} with 2020.2 as we will be burst compiling the schedule code.
            // Burst doesn't support exception handling.
            bool executedManaged = false;
            JobHandle result = default;
            FinalizeScheduleChecked(isParallel, ref jobData, queryImpl, prefilterDataPtr, prefilterHandle, mode, prefilterDataPtr, ref executedManaged, ref result);

            if (executedManaged)
                return result;

            return FinalizeScheduleNoExceptions(isParallel, ref jobData, queryImpl, prefilterDataPtr, prefilterHandle, mode, prefilterDataPtr);
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
            where T : struct, IJobEntityBatchWithIndex
        {
            var queryImpl = query._GetImpl();
            // TODO(DOTS-5666): this path does not support enableable components
            if (queryImpl->_QueryData->DoesQueryRequireBatching != 0)
                throw new ArgumentException("EntityQuery objects with types that implement IEnableableComponent are not currently supported by this operation.");
            var queryData = queryImpl->_QueryData;
            var worldUpdateAllocator = queryImpl->_Access->m_WorldUnmanaged.UpdateAllocator.ToAllocator;

            var cachedChunks = queryData->GetMatchingChunkCache();
            int maxBatchCount = limitToEntityArray.IsCreated ? limitToEntityArray.Length : cachedChunks.Length;
            var prebuiltBatchList = new UnsafeList<ArchetypeChunk>(maxBatchCount, Allocator.TempJob);
            var perBatchMatchingArchetypeIndex = new UnsafeList<int>(maxBatchCount, Allocator.TempJob);

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
                        prebuiltBatchList.Add(new ArchetypeChunk
                        {
                            m_Chunk = chunk,
                            m_EntityComponentStore = ecs,
                            m_BatchStartEntityIndex = entityInChunk.IndexInChunk,
                            m_BatchEntityCount = 1,
                        });
                        if (isFiltering || queryData->DoesQueryRequireBatching != 0)
                        {
                            var matchingArchetypeIndex = EntityQueryManager.FindMatchingArchetypeIndexForArchetype(
                                ref matchingArchetypes,
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
                    int matchingArchetypeIndex =
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

            var prefilterDataPtr = AllocatePrefilterData(batchCount, worldUpdateAllocator, out var batchList, out var indexList);

            var prefilterHandle = dependsOn;
            var prefilterJob = new PrefilterForJobEntityBatchWithIndex_EntityArray
            {
                MatchingArchetypes = queryData->MatchingArchetypes,
                Filter = queryImpl->_Filter,
                PrebuiltBatches = prebuiltBatchList,
                PerBatchMatchingArchetypeIndex = perBatchMatchingArchetypeIndex,

                OutPrefilteredBatches = batchList,
                OutPrefilteredBatchEntityIndices = indexList,
                SkipSubChunkBatching = queryData->DoesQueryRequireBatching == 0 ? 1 : 0
            };

            var disposePrebuiltBatchJob = BurstableCollectionsHacks.MakeDisposeJob(prebuiltBatchList);
            var disposeMatchingArchetypeIndexJob = BurstableCollectionsHacks.MakeDisposeJob(perBatchMatchingArchetypeIndex);
            if (mode != ScheduleMode.Run)
            {
                prefilterHandle = prefilterJob.Schedule(dependsOn);
                // TODO: As an optimization, these two dispose jobs can run concurrently with the main job; they are
                // disposing data that's only used by the prefilter job.
                prefilterHandle = JobHandle.CombineDependencies(disposePrebuiltBatchJob.Schedule(prefilterHandle),
                    disposeMatchingArchetypeIndexJob.Schedule(prefilterHandle));
            }
            else
            {
                prefilterJob.Run();
                disposePrebuiltBatchJob.Run();
                disposeMatchingArchetypeIndexJob.Run();
            }

            // We can't use try {} catch {} with 2020.2 as we will be burst compiling the schedule code.
            // Burst doesn't support exception handling.
            bool executedManaged = false;
            JobHandle result = default;
            FinalizeScheduleChecked(isParallel, ref jobData, queryImpl, prefilterDataPtr, prefilterHandle, mode, prefilterDataPtr, ref executedManaged, ref result);

            if (executedManaged)
                return result;

            return FinalizeScheduleNoExceptions(isParallel, ref jobData, queryImpl, prefilterDataPtr, prefilterHandle, mode, prefilterDataPtr);
        }

        // Burst does not support exception handling.
        [BurstDiscard]
        private static unsafe void FinalizeScheduleChecked<T>(bool isParallel, ref T jobData, EntityQueryImpl* queryImpl, void* prefilterDataPtr, JobHandle prefilterHandle, ScheduleMode mode, void* prefilterData, ref bool executed, ref JobHandle result)
            where T : struct, IJobEntityBatchWithIndex
        {
            executed = true;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            try
            {
#endif
                result = FinalizeScheduleNoExceptions<T>(isParallel, ref jobData, queryImpl, prefilterDataPtr, prefilterHandle, mode, prefilterData);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            }
            catch (InvalidOperationException e)
            {
                prefilterHandle.Complete();
                throw e;
            }
#endif
        }

        private static unsafe JobHandle FinalizeScheduleNoExceptions<T>(bool isParallel, ref T jobData, EntityQueryImpl* queryImpl, void* prefilterDataPtr, JobHandle prefilterHandle, ScheduleMode mode, void* prefilterData)
            where T : struct, IJobEntityBatchWithIndex
        {
            JobEntityBatchIndexWrapper<T> jobEntityBatchIndexWrapper = new JobEntityBatchIndexWrapper<T>
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                safety = new EntityQuerySafetyHandles(queryImpl),
#endif
                JobData = jobData,
                PrefilterData = prefilterDataPtr,

                IsParallel = isParallel ? 1 : 0
            };
            JobEntityBatchIndexProducer<T>.Initialize();
            var reflectionData = JobEntityBatchIndexProducer<T>.reflectionData.Data;
            CollectionHelper.CheckReflectionDataCorrect<T>(reflectionData);

            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobEntityBatchIndexWrapper),
                reflectionData, prefilterHandle, mode);

            if (!isParallel)
            {
                return JobsUtility.Schedule(ref scheduleParams);
            }
            else
            {
                return JobsUtility.ScheduleParallelForDeferArraySize(ref scheduleParams, 1, prefilterData, null);
            }
        }

        internal struct JobEntityBatchIndexProducer<T>
            where T : struct, IJobEntityBatchWithIndex
        {
            internal static readonly SharedStatic<IntPtr> reflectionData = SharedStatic<IntPtr>.GetOrCreate<JobEntityBatchIndexProducer<T>>();

            [BurstDiscard]

            internal static void Initialize()
            {
                if (reflectionData.Data == IntPtr.Zero)
                    reflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(JobEntityBatchIndexWrapper<T>), typeof(T), (ExecuteJobFunction)Execute);
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
                UnpackPrefilterData((byte*)jobWrapper.PrefilterData, out var batchList, out var entityIndices);

                bool isParallel = jobWrapper.IsParallel == 1;
                while (true)
                {
                    int beginBatchIndex = 0;
                    int endBatchIndex = batchList.Length;

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
                        var batch = batchList[batchIndex];
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
    unsafe struct PrefilterForJobEntityBatchWithIndex : IJob
    {
        [NativeDisableUnsafePtrRestriction] public UnsafeCachedChunkList ChunkList;
        [NativeDisableUnsafePtrRestriction] public UnsafeMatchingArchetypePtrList MatchingArchetypes;
        [NativeDisableUnsafePtrRestriction] public EntityComponentStore* EntityComponentStore;

        [NativeDisableUnsafePtrRestriction] public UnsafeList<ArchetypeChunk>* OutPrefilteredBatches;
        [NativeDisableUnsafePtrRestriction] public UnsafeList<int>* OutPrefilteredBatchEntityIndices;

        public EntityQueryFilter Filter;
        public int SkipSubChunkBatching;

        public void Execute()
        {
            var entityIndexAggregate = 0;
            var chunkCache =
                new UnsafeChunkCache(Filter, SkipSubChunkBatching == 0, ChunkList, MatchingArchetypes.Ptr);
            int chunkIndex = -1;
            v128 chunkEnabledMask = default;
            while (chunkCache.MoveNextChunk(ref chunkIndex, out var archetypeChunk, out int chunkEntityCount, out byte chunkRequiresBatching,
                       ref chunkEnabledMask))
            {
                if (chunkRequiresBatching == 0)
                {
                    OutPrefilteredBatches->Add(archetypeChunk);
                    OutPrefilteredBatchEntityIndices->Add(entityIndexAggregate);
                    entityIndexAggregate += chunkEntityCount;
                }
                else
                {
                    int batchStartIndex = 0;
                    int batchEndIndex = 0;
                    while (EnabledBitUtility.GetNextRange(ref chunkEnabledMask, ref batchStartIndex,
                               ref batchEndIndex))
                    {
                        int batchEntityCount = batchEndIndex - batchStartIndex;
                        archetypeChunk.m_BatchStartEntityIndex = batchStartIndex;
                        archetypeChunk.m_BatchEntityCount = batchEntityCount;
                        OutPrefilteredBatches->Add(archetypeChunk);
                        OutPrefilteredBatchEntityIndices->Add(entityIndexAggregate);
                        entityIndexAggregate += batchEntityCount;
                        batchStartIndex = batchEndIndex;
                    }
                }
            }
        }
    }

    [BurstCompile]
    unsafe struct PrefilterForJobEntityBatchWithIndex_EntityArray : IJob
    {
        [NativeDisableUnsafePtrRestriction] public UnsafeMatchingArchetypePtrList MatchingArchetypes;

        [NativeDisableUnsafePtrRestriction] public UnsafeList<ArchetypeChunk>* OutPrefilteredBatches;
        [NativeDisableUnsafePtrRestriction] public UnsafeList<int>* OutPrefilteredBatchEntityIndices;

        public UnsafeList<ArchetypeChunk> PrebuiltBatches;
        public UnsafeList<int> PerBatchMatchingArchetypeIndex;
        public EntityQueryFilter Filter;
        public int SkipSubChunkBatching;

        public void Execute()
        {
            var inBatches = (ArchetypeChunk*) PrebuiltBatches.Ptr;

            var entityIndexAggregate = 0;
            var isFiltering = Filter.RequiresMatchesFilter;
            var skipSubChunkBatching = SkipSubChunkBatching == 1;

            for (int i = 0; i < PrebuiltBatches.Length; ++i)
            {
                var prebuiltBatch = inBatches[i];

                if (isFiltering && !prebuiltBatch.m_Chunk->MatchesFilter(MatchingArchetypes.Ptr[PerBatchMatchingArchetypeIndex.Ptr[i]], ref Filter))
                    continue;

                // TODO(DOTS-5666): skip entities that fail the enabled-bits check.
                // We can't do this at schedule time for safety reasons (we don't want Schedule to have to
                // block on existing jobs in order to safely read the chunk's enabled bits).
                // In fact, right now if skipSubChunkBatching is false, we just don't generate any batches at all...
                if (skipSubChunkBatching)
                {
                    OutPrefilteredBatches->Add(prebuiltBatch);
                    OutPrefilteredBatchEntityIndices->Add(entityIndexAggregate);
                    entityIndexAggregate += prebuiltBatch.Count;
                }
            }
        }
    }
}
