using System;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    public enum ScheduleMode
    {
        ScheduleParallel,
        Schedule,
        Run
    }

    public enum BurstFloatMode
    {
        Default,
        Strict,
        Deterministic,
        Fast,
    }

    public enum BurstFloatPrecision
    {
        Standard,
        High,
        Medium,
        Low,
    }

    public enum SingletonAccessType
    {
        GetSingleton,
        GetSingletonEntity,
        Set,
        None
    }
}
