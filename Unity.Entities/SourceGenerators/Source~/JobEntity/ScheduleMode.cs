#nullable enable
using System;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.Entities.SourceGen.JobEntity
{
    enum ScheduleMode
    {
        Run,
        RunByRef,
        Schedule,
        ScheduleByRef,
        ScheduleParallel,
        ScheduleParallelByRef,
    }

    static class ScheduleModeHelpers {
        internal static string GetScheduleMethodWithArguments(this ScheduleMode scheduleMode, bool hasManagedComponent, bool needsEntityIndexInQuery) =>
            scheduleMode switch
            {
                ScheduleMode.Schedule => "Schedule(job, entityQuery, dependency)",
                ScheduleMode.ScheduleByRef => "ScheduleByRef(ref job, entityQuery, dependency)",
                ScheduleMode.ScheduleParallel when needsEntityIndexInQuery => "ScheduleParallel(job, entityQuery, dependency, baseEntityIndexArray)",
                ScheduleMode.ScheduleParallel => "ScheduleParallel(job, entityQuery, dependency)",
                ScheduleMode.ScheduleParallelByRef when needsEntityIndexInQuery => "ScheduleParallelByRef(ref job, entityQuery, dependency, baseEntityIndexArray)",
                ScheduleMode.ScheduleParallelByRef => "ScheduleParallelByRef(ref job, entityQuery, dependency)",
                ScheduleMode.Run when hasManagedComponent => "RunWithoutJobs(ref job, entityQuery)",
                ScheduleMode.Run => "Run(job, entityQuery)",
                ScheduleMode.RunByRef when hasManagedComponent => "RunByRefWithoutJobs(ref job, entityQuery)",
                ScheduleMode.RunByRef => "RunByRef(ref job, entityQuery)",
                _ => throw new ArgumentOutOfRangeException()
            };

        // scheduleType 0:Run, 1:Schedule, 2:ScheduleParallel
        internal static int GetScheduleTypeAsNumber(this ScheduleMode scheduleMode)
            => (int) scheduleMode / 2;

        internal static ScheduleMode GetScheduleModeFromNameOfMemberAccess(MemberAccessExpressionSyntax memberAccessExpressionSyntax)
            => memberAccessExpressionSyntax.Name.Identifier.ValueText switch
                {
                    "Run" => ScheduleMode.Run,
                    "RunByRef" => ScheduleMode.RunByRef,
                    "Schedule" => ScheduleMode.Schedule,
                    "ScheduleByRef" => ScheduleMode.ScheduleByRef,
                    "ScheduleParallel" => ScheduleMode.ScheduleParallel,
                    "ScheduleParallelByRef" => ScheduleMode.ScheduleParallelByRef,
                    _ => throw new ArgumentOutOfRangeException()
                };
        public static bool IsSchedule(this ScheduleMode mode) => mode == ScheduleMode.Schedule || mode == ScheduleMode.ScheduleByRef;
        public static bool IsRun(this ScheduleMode mode) => mode == ScheduleMode.Run || mode == ScheduleMode.RunByRef;
        public static bool IsByRef(this ScheduleMode mode) => mode == ScheduleMode.RunByRef || mode == ScheduleMode.ScheduleByRef || mode == ScheduleMode.ScheduleParallelByRef;
    }
}
