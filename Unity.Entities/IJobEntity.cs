// Please refer to the README.md document in the IJobEntity example in the Samples project for more information.

using System;
using Unity.Jobs;
using UnityEngine;

namespace Unity.Entities
{
    /// <summary>
    /// Any type which implements this interface and also contains an `Execute()` method (with any number of parameters)
    /// will trigger source generation of a corresponding IJobEntityBatch or IJobEntity type. The generated job in turn
    /// invokes the Execute() method on the IJobEntity type with the appropriate arguments.
    /// </summary>
    /// <remarks>
    /// If any SharedComponent, or ManagedComponent is part of the query, __EntityManager is generated.
    /// It's needed to access the components from the batch. This also means, that type of job has to run in main thread.
    /// </remarks>
    public interface IJobEntity {}

    /// <summary>
    /// Specifies that this int parameter is used as a way to get the packed entity index inside the current query.
    /// Usage: An int parameter found inside the execute method of a IJobEntity.
    /// </summary>
    /// <seealso cref="IJobEntity"/>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class EntityInQueryIndex : Attribute {}

    /// <summary>
    /// Specifies that this int parameter is used as the entity index inside the current chunk.
    /// Usage: An int parameter found inside the execute method of a IJobEntity.
    /// </summary>
    /// <seealso cref="IJobEntity"/>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class EntityIndexInChunk : Attribute {}

    /// <summary>
    /// Specifies that this int parameter is used as the chunk index inside the current query.
    /// Usage: An int parameter found inside the execute method of a IJobEntity.
    /// </summary>
    /// <remarks>
    /// Efficient sort-key for <see cref="EntityCommandBuffer"/>.
    /// </remarks>
    /// <seealso cref="IJobEntity"/>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class ChunkIndexInQuery : Attribute {}

    /// <summary>
    /// Specifies that this IJobEntity should include ALL ComponentTypes found as attributes of the IJobEntity
    /// </summary>
    /// <seealso cref="IJobEntity"/>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WithAllAttribute : Attribute
    {
        /// <summary>
        /// Specifies that this IJobEntity should include ALL ComponentTypes found as attributes of the IJobEntity
        /// </summary>
        /// <param name="types">The required component types</param>
        public WithAllAttribute(params Type[] types){}
    }

    /// <summary>
    /// Specifies that this IJobEntity should include NO ComponentTypes found as attributes of the IJobEntity
    /// </summary>
    /// <seealso cref="IJobEntity"/>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WithNoneAttribute : Attribute
    {
        /// <summary>
        /// Specifies that this IJobEntity should include NO ComponentTypes found as attributes of the IJobEntity
        /// </summary>
        /// <param name="types">The excluded component types</param>
        public WithNoneAttribute(params Type[] types){}
    }

    /// <summary>
    /// Specifies that this IJobEntity should include ANY ComponentTypes found as attributes of the IJobEntity
    /// </summary>
    /// <seealso cref="IJobEntity"/>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WithAnyAttribute : Attribute
    {
        /// <summary>
        /// Specifies that this IJobEntity should include ANY ComponentTypes found as attributes of the IJobEntity
        /// </summary>
        /// <param name="types">The optional component types</param>
        public WithAnyAttribute(params Type[] types){}
    }

    /// <summary>
    /// Specifies that this IJobEntity should only process a chunk if any of the ComponentTypes found as attributes of the IJobEntity have changed.
    /// </summary>
    /// <seealso cref="IJobEntity"/>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WithChangeFilterAttribute : Attribute
    {
        /// <summary>
        /// Specifies that this IJobEntity should only process a chunk if any of the ComponentTypes found as attributes of the IJobEntity have changed.
        /// </summary>
        /// <param name="types">The component types for which change filtering should be enabled</param>
        public WithChangeFilterAttribute(params Type[] types){}
    }

    /// <summary>
    /// Specifies that this IJobEntity should include a given EntityQueryOption found as attributes of the IJobEntity
    /// </summary>
    /// <seealso cref="IJobEntity"/>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WithEntityQueryOptionsAttribute : Attribute
    {
        /// <summary>
        /// Specifies that this IJobEntity should include a given EntityQueryOption found as attributes of the IJobEntity
        /// </summary>
        /// <param name="option">The query options</param>
        public WithEntityQueryOptionsAttribute(EntityQueryOptions option){}
        /// <summary>
        /// Specifies that this IJobEntity should include a given EntityQueryOption found as attributes of the IJobEntity
        /// </summary>
        /// <param name="options">The query options</param>
        public WithEntityQueryOptionsAttribute(params EntityQueryOptions[] options){}
    }

    /// <summary>
    /// Extension methods for IJobEntity job type
    /// </summary>
    public static class IJobEntityExtensions
    {
        // Mirrors all of the schedule methods for IJobChunk and IJobEntityBatch,
        // except we must also have a version that takes no query as IJobEntity can generate the query for you
        // and there are also one's that don't take a JobHandle so that the built-in Dependency properties in systems
        // can be used.
        // These methods must all be replicated in the generated job struct to prevent compiler ambiguity
        // These methods keep the full type names so that it can be easily copy pasted into JobEntityDescriptionSourceFactor.cs when updated

        #region Schedule

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static JobHandle Schedule<T>(this T jobData, JobHandle dependsOn)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static JobHandle ScheduleByRef<T>(this ref T jobData, JobHandle dependsOn)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static JobHandle Schedule<T>(this T jobData, EntityQuery query, JobHandle dependsOn)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static JobHandle ScheduleByRef<T>(this ref T jobData, EntityQuery query, JobHandle dependsOn)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <remarks>This job automatically uses the system's Dependency property as the input and output dependency.</remarks>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static void Schedule<T>(this T jobData)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <remarks>This job automatically uses the system's Dependency property as the input and output dependency.</remarks>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static void ScheduleByRef<T>(this ref T jobData)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <remarks>This job automatically uses the system's Dependency property as the input and output dependency.</remarks>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static void Schedule<T>(this T jobData, EntityQuery query)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <remarks>This job automatically uses the system's Dependency property as the input and output dependency.</remarks>
        public static void ScheduleByRef<T>(this ref T jobData, EntityQuery query)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        #endregion

        #region ScheduleParallel

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static JobHandle ScheduleParallel<T>(this T jobData, JobHandle dependsOn)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static JobHandle ScheduleParallelByRef<T>(this ref T jobData, JobHandle dependsOn)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static JobHandle ScheduleParallel<T>(this T jobData, EntityQuery query, JobHandle dependsOn)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled jobs that could constrain this job.
        /// A job that writes to a component cannot run in parallel with other jobs that read or write that component.
        /// Jobs that only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static JobHandle ScheduleParallelByRef<T>(this ref T jobData, EntityQuery query, JobHandle dependsOn)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <remarks>This job automatically uses the system's Dependency property as the input and output dependency.</remarks>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static void ScheduleParallel<T>(this T jobData)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <remarks>This job automatically uses the system's Dependency property as the input and output dependency.</remarks>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static void ScheduleParallelByRef<T>(this ref T jobData)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <remarks>This job automatically uses the system's Dependency property as the input and output dependency.</remarks>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static void ScheduleParallel<T>(this T jobData, EntityQuery query)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Adds an <see cref="IJobEntity"/> instance to the job scheduler queue for parallel execution.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        /// <remarks>This job automatically uses the system's Dependency property as the input and output dependency.</remarks>
        /// <remarks>Can't schedule managed components or managed shared components, use Run instead.</remarks>
        public static void ScheduleParallelByRef<T>(this ref T jobData, EntityQuery query)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        #endregion

        #region Run

        /// <summary>
        /// Runs the job immediately on the current thread.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        public static void Run<T>(this T jobData)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Runs the job immediately on the current thread.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        public static void RunByRef<T>(this ref T jobData)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Runs the job immediately on the current thread.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        public static void Run<T>(this T jobData, EntityQuery query)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        /// <summary>
        /// Runs the job immediately on the current thread.
        /// </summary>
        /// <param name="jobData">An <see cref="IJobEntity"/> instance. In this variant, the jobData is passed by
        /// reference, which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific <see cref="IJobEntity"/> implementation type.</typeparam>
        public static void RunByRef<T>(this ref T jobData, EntityQuery query)
            where T : struct, IJobEntity  => throw InternalCompilerInterface.ThrowCodeGenException();

        #endregion
    }
}
