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
    internal static class BurstableCollectionsHacks
    {
        // TEMP copy from Collections so we can do this from Burst code
        [BurstCompile]
        internal unsafe struct WorkaroundUnsafeDisposeJob : IJobBurstSchedulable
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
    /// * <see cref="JobEntityBatchIndexExtensions.ScheduleParallel{T}(T, EntityQuery, JobHandle)"/>,
    /// * or <see cref="JobEntityBatchIndexExtensions.Run{T}(T, EntityQuery)"/>
    ///
    /// all the entities of each chunk are processed as
    /// a single batch. The <see cref="ArchetypeChunk"/> object passed to the `Execute` function of your job struct provides access
    /// to the components of all the entities in the chunk.
    ///
    /// Use <see cref="JobEntityBatchIndexExtensions.ScheduleParallel{T}(T, EntityQuery, ScheduleGranularity.Entity, NativeArray&lt;Entity&gt;, JobHandle)"/>
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
        /// function. If you use <see cref="JobEntityBatchIndexExtensions.ScheduleParallel{T}(T, EntityQuery, ScheduleGranularity.Entity, NativeArray&lt;Entity&gt;, JobHandle)"/>
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
    public static class JobEntityBatchIndexExtensions
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [NativeContainer]
        internal struct EntitySafetyHandle
        {
            internal AtomicSafetyHandle m_Safety;
        }
#endif
        internal unsafe struct JobEntityBatchIndexWrapper<T> where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
#pragma warning disable 414
            [ReadOnly] public EntitySafetyHandle safety;
#pragma warning restore
#endif
            public T JobData;

            [NativeDisableUnsafePtrRestriction]
            public void* PrefilterData;

            public int IsParallel;
        }

        /// <summary>
        /// This method is only to be called by automatically generated setup code.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static void EarlyJobInit<T>()
            where T : struct, IJobEntityBatchWithIndex
        {
            JobEntityBatchIndexProducer<T>.Initialize();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckReflectionDataCorrect(IntPtr reflectionData)
        {
            if (reflectionData == IntPtr.Zero)
                throw new InvalidOperationException("Reflection data was not set up by an Initialize() call");
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
        [Obsolete("The batchesPerChunk parameter has been replaced. Instead, use ScheduleGranularity.Chunk (the default) or ScheduleGranularity.Entity. (RemovedAfter 2021-08-10)")]
        public static unsafe JobHandle ScheduleParallel<T>(
            this T jobData,
            EntityQuery query,
            int batchesPerChunk,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
        {
            if (batchesPerChunk == 1)
                return ScheduleParallel(jobData, query, dependsOn);
            return ScheduleParallel(jobData, query, ScheduleGranularity.Entity, default, dependsOn);
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
        [Obsolete("The batchesPerChunk parameter has been replaced. Instead, use ScheduleGranularity.Chunk (the default) or ScheduleGranularity.Entity. (RemovedAfter 2021-08-10)")]
        public static unsafe JobHandle ScheduleParallelByRef<T>(
            this ref T jobData,
            EntityQuery query,
            int batchesPerChunk,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
        {
            if (batchesPerChunk == 1)
                return ScheduleParallelByRef(ref jobData, query, dependsOn);
            return ScheduleParallelByRef(ref jobData, query, ScheduleGranularity.Entity, default, dependsOn);
        }

        /// <summary>
        /// Adds an <see cref="IJobEntityBatchWithIndex"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <remarks>This scheduling variant processes each batch found in the entity array. Each
        /// batch can execute in parallel.</remarks>
        /// <param name="jobData">An <see cref="IJobEntityBatchWithIndex"/> instance.</param>
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
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
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
        [Obsolete("This function now takes a ScheduleGranularity parameter. If in doubt, pass ScheduleGranularity.Chunk. (RemovedAfter 2021-08-10)")]
        public static unsafe JobHandle ScheduleParallel<T>(
            this T jobData,
            EntityQuery query,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
        {
            if (!limitToEntityArray.IsCreated)
                return ScheduleParallel(jobData, query, dependsOn);
            return ScheduleParallel(jobData, query, ScheduleGranularity.Chunk, limitToEntityArray, dependsOn);
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
        /// <typeparam name="T">The specific <see cref="IJobEntityBatchWithIndex"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
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
        [Obsolete("This function now takes a ScheduleGranularity parameter. If in doubt, pass ScheduleGranularity.Chunk. (RemovedAfter 2021-08-10)")]
        public static unsafe JobHandle ScheduleParallelByRef<T>(
            this ref T jobData,
            EntityQuery query,
            NativeArray<Entity> limitToEntityArray,
            JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobEntityBatchWithIndex
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
        public static unsafe void RunByRef<T>(this ref T jobData, EntityQuery query, NativeArray<Entity> limitToEntityArray)
            where T : struct, IJobEntityBatchWithIndex
        {
            if (!limitToEntityArray.IsCreated)
                ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, false);
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, ScheduleGranularity.Chunk, false, limitToEntityArray);
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

        unsafe internal static void RunWithoutJobsInternal<T>(ref T jobData, Chunk** chunks, int chunkCount, EntityComponentStore* store)
            where T : struct, IJobEntityBatchWithIndex
        {
            int entitiesSeen = 0;
            for (int i = 0; i != chunkCount; i++)
            {
                var archetypeChunk = new ArchetypeChunk(chunks[i], store);
                jobData.Execute(archetypeChunk, i, entitiesSeen);
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

            var expectedBatchCount = query.CalculateChunkCount();

            var sizeOfArrayHeaders = sizeof(UnsafeList<int>) * 2;
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
                SkipSubChunkBatching = queryData->EnableableComponentsCountAll == 0 && queryData->EnableableComponentsCountNone == 0 ? 1 : 0
            };

            if (mode != ScheduleMode.Run)
                prefilterHandle = prefilterJob.Schedule(dependsOn);
            else
                prefilterJob.Run();


            JobEntityBatchIndexWrapper<T> jobEntityBatchIndexWrapper = new JobEntityBatchIndexWrapper<T>
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // All IJobEntityBatchWithIndex jobs have a EntityManager safety handle to ensure that BeforeStructuralChange throws an error if
                // jobs without any other safety handles are still running (haven't been synced).
                safety = new EntitySafetyHandle {m_Safety = queryImpl->SafetyHandles->GetEntityManagerSafetyHandle()},
#endif

                JobData = jobData,
                PrefilterData = prefilterDataPtr,

                IsParallel = isParallel ? 1 : 0
            };

            var reflectionData = JobEntityBatchIndexProducer<T>.reflectionData.Data;
            CheckReflectionDataCorrect(reflectionData);

            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobEntityBatchIndexWrapper),
                reflectionData, prefilterHandle, mode);

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
                return JobsUtility.ScheduleParallelForDeferArraySize(ref scheduleParams, 1, prefilterDataPtr, null);
            }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            }
            catch (InvalidOperationException e)
            {
                prefilterHandle.Complete();
                throw e;
            }
#endif
#else
            // We can't use try {} catch {} with 2020.2 as we will be burst compiling the schedule code.
            // Burst doesn't support exception handling.
            bool executedManaged = false;
            JobHandle result = default;
            FinalizeScheduleChecked(isParallel, prefilterHandle, prefilterDataPtr, ref scheduleParams, ref executedManaged, ref result);

            if (executedManaged)
                return result;

            return FinalizeScheduleNoExceptions(isParallel, prefilterDataPtr, ref scheduleParams);
#endif
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
            where T : struct, IJobEntityBatchWithIndex
        {
            var queryImpl = query._GetImpl();
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
                        prebuiltBatchList.Add(new ArchetypeChunk
                        {
                            m_Chunk = chunk,
                            m_EntityComponentStore = ecs,
                            m_BatchStartEntityIndex = entityInChunk.IndexInChunk,
                            m_BatchEntityCount = 1,
                        });
                        if (isFiltering || queryData->DoesQueryRequireBatching)
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
                SkipSubChunkBatching = queryData->EnableableComponentsCountAll == 0 && queryData->EnableableComponentsCountNone == 0 ? 1 : 0
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


            JobEntityBatchIndexWrapper<T> jobEntityBatchIndexWrapper = new JobEntityBatchIndexWrapper<T>
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // All IJobEntityBatchWithIndex jobs have a EntityManager safety handle to ensure that BeforeStructuralChange throws an error if
                // jobs without any other safety handles are still running (haven't been synced).
                safety = new EntitySafetyHandle {m_Safety = queryImpl->SafetyHandles->GetEntityManagerSafetyHandle()},
#endif

                JobData = jobData,
                PrefilterData = prefilterDataPtr,

                IsParallel = isParallel ? 1 : 0
            };

            var reflectionData = JobEntityBatchIndexProducer<T>.reflectionData.Data;
            CheckReflectionDataCorrect(reflectionData);

            var scheduleParams = new JobsUtility.JobScheduleParameters(UnsafeUtility.AddressOf(ref jobEntityBatchIndexWrapper),
                reflectionData, prefilterHandle, mode);

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
                return JobsUtility.ScheduleParallelForDeferArraySize(ref scheduleParams, 1, prefilterDataPtr, null);
            }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            }
            catch (InvalidOperationException e)
            {
                prefilterHandle.Complete();
                throw e;
            }
#endif
#else
            // We can't use try {} catch {} with 2020.2 as we will be burst compiling the schedule code.
            // Burst doesn't support exception handling.
            bool executedManaged = false;
            JobHandle result = default;
            FinalizeScheduleChecked(isParallel, prefilterHandle, prefilterDataPtr, ref scheduleParams, ref executedManaged, ref result);

            if (executedManaged)
                return result;

            return FinalizeScheduleNoExceptions(isParallel, prefilterDataPtr, ref scheduleParams);
#endif
        }

#if !UNITY_DOTSRUNTIME
        // Burst does not support exception handling.
        [BurstDiscard]
        private static unsafe void FinalizeScheduleChecked(bool isParallel, JobHandle prefilterHandle, void* prefilterData, ref JobsUtility.JobScheduleParameters scheduleParams, ref bool executed, ref JobHandle result)
        {
            executed = true;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            try
            {
#endif
                result = FinalizeScheduleNoExceptions(isParallel, prefilterData, ref scheduleParams);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            }
            catch (InvalidOperationException e)
            {
                prefilterHandle.Complete();
                throw e;
            }
#endif
        }

        private static unsafe JobHandle FinalizeScheduleNoExceptions(bool isParallel, void* prefilterData, ref JobsUtility.JobScheduleParameters scheduleParams)
        {
            if (!isParallel)
            {
                return JobsUtility.Schedule(ref scheduleParams);
            }
            else
            {
                return JobsUtility.ScheduleParallelForDeferArraySize(ref scheduleParams, 1, prefilterData, null);
            }
        }
#endif
        internal struct JobEntityBatchIndexProducer<T>
            where T : struct, IJobEntityBatchWithIndex
        {
            internal static readonly SharedStatic<IntPtr> reflectionData = SharedStatic<IntPtr>.GetOrCreate<JobEntityBatchIndexProducer<T>>();

            [Preserve]

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
    unsafe struct PrefilterForJobEntityBatchWithIndex : IJobBurstSchedulable
    {
        [NativeDisableUnsafePtrRestriction] public UnsafeCachedChunkList ChunkList;
        [NativeDisableUnsafePtrRestriction] public UnsafeMatchingArchetypePtrList MatchingArchetypes;
        [NativeDisableUnsafePtrRestriction] public EntityComponentStore* EntityComponentStore;

        [NativeDisableUnsafePtrRestriction] public UnsafeList<ArchetypeChunk>* OutPrefilteredBatches;
        [NativeDisableUnsafePtrRestriction] public UnsafeList<int>* OutPrefilteredBatchEntityIndices;

        public EntityQueryFilter Filter;
        public int SkipSubChunkBatching;

        [SkipLocalsInit]
        public void Execute()
        {
            var entityIndexAggregate = 0;
            var chunkCount = ChunkList.Length;
            var matchPtrs = MatchingArchetypes.Ptr;
            var requiresFilter = Filter.RequiresMatchesFilter;

            if (SkipSubChunkBatching != 0)
            {
                if (requiresFilter)
                {
                    // sub-chunk batching disabled, filtering enabled
                    int archetypeCount = MatchingArchetypes.Length;
                    for (var m = 0; m < archetypeCount; ++m)
                    {
                        var match = matchPtrs[m];
                        if (match->Archetype->EntityCount <= 0)
                            continue;

                        var archetype = match->Archetype;
                        int archetypeChunkCount = archetype->Chunks.Count;
                        var chunkEntityCountArray = archetype->Chunks.GetChunkEntityCountArray();

                        for (int chunkIndex = 0; chunkIndex < archetypeChunkCount; ++chunkIndex)
                        {
                            var chunk = archetype->Chunks[chunkIndex];
                            if (match->ChunkMatchesFilter(chunkIndex, ref Filter))
                            {
                                var batch = new ArchetypeChunk(chunk, EntityComponentStore);
                                OutPrefilteredBatches->Add(batch);
                                OutPrefilteredBatchEntityIndices->Add(entityIndexAggregate);
                                entityIndexAggregate += chunkEntityCountArray[chunkIndex];
                            }
                        }
                    }
                }
                else
                {
                    // sub-chunk batching disabled, filtering disabled (fastest path)
                    OutPrefilteredBatches->Resize(chunkCount);
                    OutPrefilteredBatchEntityIndices->Resize(chunkCount);
                    var outBatches = OutPrefilteredBatches->Ptr;
                    var outIndices = OutPrefilteredBatchEntityIndices->Ptr;
                    for (int chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
                    {
                        var chunk = ChunkList.Ptr[chunkIndex];
                        outBatches[chunkIndex] = new ArchetypeChunk(chunk, EntityComponentStore);
                        outIndices[chunkIndex] = entityIndexAggregate;
                        entityIndexAggregate += chunk->Count;
                    }
                }
            }
            else
            {
                // Sub-chunk batching enabled
                var stackAllocatedBatchList = stackalloc ArchetypeChunk[ChunkIterationUtility.kMaxBatchesPerChunk];
                for (int chunkIndex = 0; chunkIndex < chunkCount; ++chunkIndex)
                {
                    var chunk = ChunkList.Ptr[chunkIndex];
                    var match = matchPtrs[(*ChunkList.PerChunkMatchingArchetypeIndex)[chunkIndex]];
                    var chunkEntityCountArray = match->Archetype->Chunks.GetChunkEntityCountArray();

                    if (requiresFilter && !match->ChunkMatchesFilter(chunk->ListIndex, ref Filter))
                        continue;

                    var chunkRequiresBatching =
                        ChunkIterationUtility.DoesChunkRequireBatching(chunk, match, out var skipChunk);
                    if (skipChunk)
                        continue;

                    if (chunkRequiresBatching)
                    {
                        ChunkIterationUtility.FindBatchesForChunk(chunk, match, EntityComponentStore,
                            stackAllocatedBatchList, out var batchCount);

                        for (int batchIndex = 0; batchIndex < batchCount; ++batchIndex)
                        {
                            var batch = stackAllocatedBatchList[batchIndex];
                            OutPrefilteredBatches->Add(batch);
                            OutPrefilteredBatchEntityIndices->Add(entityIndexAggregate);
                            entityIndexAggregate += batch.Count;
                        }
                    }
                    else
                    {
                        var batch = new ArchetypeChunk(chunk, EntityComponentStore);
                        OutPrefilteredBatches->Add(batch);
                        OutPrefilteredBatchEntityIndices->Add(entityIndexAggregate);
                        entityIndexAggregate += chunkEntityCountArray[chunk->ListIndex];
                    }
                }
            }
        }
    }

    [BurstCompile]
    unsafe struct PrefilterForJobEntityBatchWithIndex_EntityArray : IJobBurstSchedulable
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

                if (skipSubChunkBatching)
                {
                    OutPrefilteredBatches->Add(prebuiltBatch);
                    OutPrefilteredBatchEntityIndices->Add(entityIndexAggregate);
                    entityIndexAggregate += prebuiltBatch.Count;
                }
            }
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
