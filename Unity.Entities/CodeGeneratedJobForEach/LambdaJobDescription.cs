using System;
using Unity.Burst;
using Unity.Entities.CodeGeneratedJobForEach;
using Unity.Jobs;

[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public delegate object Invalid_ForEach_Signature_See_ForEach_Documentation_For_Rules_And_Restrictions(object o);

namespace Unity.Entities.CodeGeneratedJobForEach
{
    [AttributeUsage(AttributeTargets.Parameter)]
    internal class AllowDynamicValueAttribute : Attribute { }

    public interface ILambdaJobDescription { }
    public interface ILambdaJobExecutionDescription { }
    public interface ILambdaSingleJobExecutionDescription { }
    public interface ISupportForEachWithUniversalDelegate { }
    public interface ISingleJobDescription {}

    public struct ForEachLambdaJobDescription : ILambdaJobDescription, ILambdaJobExecutionDescription, ISupportForEachWithUniversalDelegate
    {
        //this overload exists here with the sole purpose of being able to give the user a not-totally-horrible
        //experience when they try to use an unsupported lambda signature. When this happens, the C# compiler
        //will go through its overload resolution, take the first candidate, and explain the user why the users
        //lambda is incompatible with that first candidates' parametertype.  We put this method here, instead
        //of with the other .ForEach overloads, to make sure this is the overload that the c# compiler will pick
        //when generating its compiler error.  If we didn't do that, it might pick a completely unrelated .ForEach
        //extention method, like the one for IJobChunk.
        //
        //The only communication channel we have to the user to guide them to figuring out what their problem is
        //is the name of the expected delegate type, as the c# compiler will put that in its compiler error message.
        //so we take this very unconventional approach here of encoding a message for the user in that type name,
        //that is easily googlable, so they will end up at a documentation page that describes why some lambda
        //signatures are compatible, and why some aren't, and what to do about that.
        //
        //the reason the delegate type is in the global namespace, is that it makes for a cleaner error message
        //it's marked with an attribute to prevent it from showing up in intellisense.
        public void ForEach(Invalid_ForEach_Signature_See_ForEach_Documentation_For_Rules_And_Restrictions ed)
        {
        }
    }
    public struct LambdaSingleJobDescription : ILambdaJobDescription, ILambdaSingleJobExecutionDescription, ISingleJobDescription
    {
    }
    public struct LambdaJobChunkDescription : ILambdaJobDescription, ILambdaJobExecutionDescription
    {
    }
}

namespace Unity.Entities
{
    public static class LambdaJobDescriptionConstructionMethods
    {
        [AttributeUsage(AttributeTargets.Method)]
        internal class AllowMultipleInvocationsAttribute : Attribute {}

        public static TDescription WithoutBurst<TDescription>(this TDescription description) where TDescription : ILambdaJobDescription => description;

        public static TDescription WithBurst<TDescription>(this TDescription description, FloatMode floatMode = FloatMode.Default, FloatPrecision floatPrecision = FloatPrecision.Standard, bool synchronousCompilation = false) where TDescription : ILambdaJobDescription => description;
        public static TDescription WithName<TDescription>(this TDescription description, string name) where TDescription : ILambdaJobDescription => description;
        public static TDescription WithStructuralChanges<TDescription>(this TDescription description) where TDescription : ILambdaJobDescription => description;

        [AllowMultipleInvocations]
        public static TDescription WithReadOnly<TDescription, TCapturedVariableType>(this TDescription description, [AllowDynamicValue] TCapturedVariableType capturedVariable) where TDescription : ILambdaJobDescription => description;

        [AllowMultipleInvocations]
        public static TDescription WithDisposeOnCompletion<TDescription, TCapturedVariableType>(this TDescription description, [AllowDynamicValue] TCapturedVariableType capturedVariable) where TDescription : ILambdaJobDescription => description;
        [AllowMultipleInvocations]
        public static TDescription WithNativeDisableContainerSafetyRestriction<TDescription, TCapturedVariableType>(this TDescription description, [AllowDynamicValue] TCapturedVariableType capturedVariable) where TDescription : ILambdaJobDescription => description;
        [AllowMultipleInvocations]
        public static unsafe TDescription WithNativeDisableUnsafePtrRestriction<TDescription, TCapturedVariableType>(this TDescription description, [AllowDynamicValue] TCapturedVariableType* capturedVariable) where TDescription : ILambdaJobDescription where TCapturedVariableType : unmanaged => description;
        [AllowMultipleInvocations]
        public static TDescription WithNativeDisableParallelForRestriction<TDescription, TCapturedVariableType>(this TDescription description, [AllowDynamicValue] TCapturedVariableType capturedVariable) where TDescription : ILambdaJobDescription => description;

        /// <summary>
        /// Specifies the the unit of work that will be processed by each worker thread.
        /// Must be used with ScheduleParallel. 
        /// </summary>
        /// <typeparam name="TDescription"></typeparam>
        /// <param name="description"></param>
        /// <param name="granularity">
        /// If <see cref="ScheduleGranularity.Chunk"/> is passed (the safe default),
        /// work is distributed at the level of whole chunks. This can lead to poor load balancing in cases where the
        /// number of chunks being processed is low (fewer than the number of available worker threads), and the cost to
        /// process each entity is high. In these cases, pass <see cref="ScheduleGranularity.Entity"/>
        /// to distribute work at the level of individual entities.</param>
        /// <returns></returns>
        public static TDescription WithScheduleGranularity<TDescription>(this TDescription description, ScheduleGranularity granularity) where TDescription : ILambdaJobDescription => description;
    }

    public static class LambdaJobDescriptionExecutionMethods
    {
        public static JobHandle Schedule<TDescription>(this TDescription description, [AllowDynamicValue] JobHandle dependency) where TDescription : ILambdaJobExecutionDescription => ThrowCodeGenException();
        public static JobHandle ScheduleParallel<TDescription>(this TDescription description, [AllowDynamicValue] JobHandle dependency) where TDescription : ILambdaJobExecutionDescription => ThrowCodeGenException();

        public static void Schedule<TDescription>(this TDescription description) where TDescription : ILambdaJobExecutionDescription => ThrowCodeGenException();
        public static void ScheduleParallel<TDescription>(this TDescription description) where TDescription : ILambdaJobExecutionDescription => ThrowCodeGenException();
        public static void Run<TDescription>(this TDescription description) where TDescription : ILambdaJobExecutionDescription => ThrowCodeGenException();

        static JobHandle ThrowCodeGenException() => throw new Exception("This SystemBase method should have been replaced by codegen");
    }

    public static class LambdaSingleJobDescriptionExecutionMethods
    {
        public static JobHandle Schedule<TDescription>(this TDescription description, [AllowDynamicValue] JobHandle dependency) where TDescription : ILambdaSingleJobExecutionDescription => ThrowCodeGenException();

        public static void Schedule<TDescription>(this TDescription description) where TDescription : ILambdaSingleJobExecutionDescription => ThrowCodeGenException();
        public static void Run<TDescription>(this TDescription description) where TDescription : ILambdaSingleJobExecutionDescription => ThrowCodeGenException();

        static JobHandle ThrowCodeGenException() => throw new Exception("This SystemBase method should have been replaced by codegen");
    }

    public static class LambdaSingleJobDescriptionConstructionMethods
    {
        public delegate void WithCodeAction();
        public static TDescription WithCode<TDescription>(this TDescription description,  [AllowDynamicValue] WithCodeAction code)
            where TDescription : ISingleJobDescription => description;
    }

    public static class LambdaJobChunkDescriptionConstructionMethods
    {
        public delegate void JobChunkDelegate(ArchetypeChunk chunk, int chunkIndex, int queryIndexOfFirstEntityInChunk);
        public static LambdaJobChunkDescription ForEach(this LambdaJobChunkDescription description,  [AllowDynamicValue] JobChunkDelegate code) => description;
    }

    public static class LambdaJobChunkDescription_SetSharedComponent
    {
        public static LambdaJobChunkDescription SetSharedComponentFilterOnQuery<T>(LambdaJobChunkDescription description, T sharedComponent, EntityQuery query) where T : struct, ISharedComponentData
        {
            query.SetSharedComponentFilter(sharedComponent);
            return description;
        }
    }

    public static class ForEachLambdaJobDescription_SetSharedComponent
    {
        public static TDescription SetSharedComponentFilterOnQuery<TDescription, T>(this TDescription description, T sharedComponent, EntityQuery query)
            where TDescription : struct, ISupportForEachWithUniversalDelegate
            where T : struct, ISharedComponentData
        {
            query.SetSharedComponentFilter(sharedComponent);
            return description;
        }
    }
}

public static partial class LambdaForEachDescriptionConstructionMethods
{
    public static TDescription ThrowCodeGenException<TDescription>() => throw new Exception("This method should have been replaced by codegen");
    public static void ThrowCodeGenInvalidMethodCalledException() => throw new Exception("This method was replaced during post-processing and should not have been called.  Please file a bug with us!");
}
