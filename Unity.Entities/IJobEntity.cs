// Please refer to the README.md document in the IJobEntity example in the Samples project for more information.

using System;
using Unity.Jobs;
using UnityEngine;

namespace Unity.Entities
{
    /// <summary>
    /// Any type which implements this interface and also contains an `Execute()` method (with any number of parameters)
    /// will trigger source generation of a corresponding IJobEntityBatch type. The generated IJobEntityBatch type in turn
    /// invokes the Execute() method on the IJobEntity type with the appropriate arguments.
    /// </summary>
    /// <remarks>
    /// If any SharedComponent, or ManagedComponent is part of the query, __EntityManager is generated.
    /// It's needed to access the components from the batch. This also means, that type of job has to run in main thread.
    /// </remarks>
    public interface IJobEntity {}

    /// <summary>
    /// Specifies that this int parameter is used as a way to get the EntityInQuery index.
    /// Usage: An int parameter found inside the execute method of a IJobEntity.
    /// </summary>
    /// <seealso cref="IJobEntity"/>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class EntityInQueryIndex : Attribute {}

    /// <summary>
    /// Specifies that this IJobEntity should include ALL ComponentTypes found as attributes of the IJobEntity
    /// </summary>
    /// <seealso cref="IJobEntity"/>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WithAllAttribute : Attribute
    {
        public WithAllAttribute(params Type[] types){}
    }

    /// <summary>
    /// Specifies that this IJobEntity should include NO ComponentTypes found as attributes of the IJobEntity
    /// </summary>
    /// <seealso cref="IJobEntity"/>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WithNoneAttribute : Attribute
    {
        public WithNoneAttribute(params Type[] types){}
    }

    /// <summary>
    /// Specifies that this IJobEntity should include ANY ComponentTypes found as attributes of the IJobEntity
    /// </summary>
    /// <seealso cref="IJobEntity"/>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WithAnyAttribute : Attribute
    {
        public WithAnyAttribute(params Type[] types){}
    }

    /// <summary>
    /// Specifies that this IJobEntity should only play if either of the ComponentTypes found as attributes of the IJobEntity has changed,
    /// </summary>
    /// <seealso cref="IJobEntity"/>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WithChangeFilterAttribute : Attribute
    {
        public WithChangeFilterAttribute(params Type[] types){}
    }

    /// <summary>
    /// Specifies that this IJobEntity should include a given EntityQueryOption found as attributes of the IJobEntity
    /// </summary>
    /// <seealso cref="IJobEntity"/>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class WithEntityQueryOptionsAttribute : Attribute
    {
        public WithEntityQueryOptionsAttribute(EntityQueryOptions option){}
        public WithEntityQueryOptionsAttribute(params EntityQueryOptions[] options){}
    }

    public static class IJobEntityExtensions
    {
        // Mirrors all of the schedule methods for IJobEntityBatch, except we must also have a version that takes no query as IJobEntity can generate the query for you
        // IJobEntityBatch method is first, follow by its No Query version
        // Currently missing the limitToEntityArray versions
        // These methods must all be replicated in the generated job struct to prevent compiler ambiguity

        // These methods keep the full type names so that it can be easily copy pasted into JobEntityDescriptionSourceFactor.cs when updated

        public static Unity.Jobs.JobHandle Schedule<T>(this T jobData, Unity.Entities.EntityQuery query, Unity.Jobs.JobHandle dependsOn = default(JobHandle)) where T : struct, IJobEntity => __ThrowCodeGenException();
        /// <summary>
        /// Schedules the given job to run on an arbitrary non-main thread. By copying the job data.
        /// </summary>
        /// <param name="jobData">The implemented IJobEntity job</param>
        /// <param name="dependsOn">A jobhandle dependency, if not specified then generated as `Dependency` property in System</param>
        /// <typeparam name="T">Type of IJobEntity implementation</typeparam>
        /// <returns>Jobhandle with dependency chain</returns>
        public static Unity.Jobs.JobHandle Schedule<T>(this T jobData, Unity.Jobs.JobHandle dependsOn = default(JobHandle)) where T : struct, IJobEntity => __ThrowCodeGenException();

        public static Unity.Jobs.JobHandle ScheduleByRef<T>(this T jobData, Unity.Entities.EntityQuery query, Unity.Jobs.JobHandle dependsOn = default(JobHandle)) where T : struct, IJobEntity => __ThrowCodeGenException();

        /// <summary>
        /// Schedules the given job to run on an arbitrary non-main thread. By referencing the job data.
        /// </summary>
        /// <param name="jobData">The implemented IJobEntity job</param>
        /// <param name="dependsOn">A jobhandle dependency, if not specified then generated as `Dependency` property in System</param>
        /// <typeparam name="T">Type of IJobEntity implementation</typeparam>
        /// <returns>Jobhandle with dependency chain</returns>
        public static Unity.Jobs.JobHandle ScheduleByRef<T>(this T jobData, Unity.Jobs.JobHandle dependsOn = default(JobHandle)) where T : struct, IJobEntity => __ThrowCodeGenException();

        public static Unity.Jobs.JobHandle ScheduleParallel<T>(this T jobData, Unity.Entities.EntityQuery query, Unity.Jobs.JobHandle dependsOn = default(JobHandle)) where T : struct, IJobEntity => __ThrowCodeGenException();

        /// <summary>
        /// Schedules the given job to run on in parallel on multiple thread(s). By copying the job data.
        /// </summary>
        /// <param name="jobData">The implemented IJobEntity job</param>
        /// <param name="dependsOn">A jobhandle dependency, if not specified then generated as `Dependency` property in System</param>
        /// <typeparam name="T">Type of IJobEntity implementation</typeparam>
        /// <returns>Jobhandle with dependency chain</returns>
        public static Unity.Jobs.JobHandle ScheduleParallel<T>(this T jobData, Unity.Jobs.JobHandle dependsOn = default(JobHandle)) where T : struct, IJobEntity => __ThrowCodeGenException();

        public static Unity.Jobs.JobHandle ScheduleParallelByRef<T>(this T jobData, Unity.Entities.EntityQuery query, Unity.Jobs.JobHandle dependsOn = default(JobHandle)) where T : struct, IJobEntity => __ThrowCodeGenException();

        /// <summary>
        /// Schedules the given job to run on in parallel on multiple thread(s). By referencing the job data.
        /// </summary>
        /// <param name="jobData">The implemented IJobEntity job</param>
        /// <param name="dependsOn">A jobhandle dependency, if not specified then generated as `Dependency` property in System</param>
        /// <typeparam name="T">Type of IJobEntity implementation</typeparam>
        /// <returns>Jobhandle with dependency chain</returns>
        public static Unity.Jobs.JobHandle ScheduleParallelByRef<T>(this T jobData, Unity.Jobs.JobHandle dependsOn = default(JobHandle)) where T : struct, IJobEntity => __ThrowCodeGenException();

        public static void Run<T>(this T jobData, Unity.Entities.EntityQuery query) where T : struct, IJobEntity => __ThrowCodeGenException();

        /// <summary>
        /// Runs the given job on the main thread. By copying the job data.
        /// </summary>
        /// <param name="jobData">The implemented IJobEntity job</param>
        /// <param name="dependsOn">A jobhandle dependency, if not specified then generated as `Dependency` property in System</param>
        /// <typeparam name="T">Type of IJobEntity implementation</typeparam>
        /// <returns>Jobhandle with dependency chain</returns>
        public static void Run<T>(this T jobData) where T : struct, IJobEntity => __ThrowCodeGenException();

        public static void RunByRef<T>(this T jobData, Unity.Entities.EntityQuery query) where T : struct, IJobEntity => __ThrowCodeGenException();

        /// <summary>
        /// Runs the given job on the main thread. By referencing the job data.
        /// </summary>
        /// <param name="jobData">The implemented IJobEntity job</param>
        /// <param name="dependsOn">A jobhandle dependency, if not specified then generated as `Dependency` property in System</param>
        /// <typeparam name="T">Type of IJobEntity implementation</typeparam>
        /// <returns>Jobhandle with dependency chain</returns>
        public static void RunByRef<T>(this T jobData) where T : struct, IJobEntity => __ThrowCodeGenException();

        static Unity.Jobs.JobHandle __ThrowCodeGenException() => throw new Exception("This method should have been replaced by source gen.");

    }
}
