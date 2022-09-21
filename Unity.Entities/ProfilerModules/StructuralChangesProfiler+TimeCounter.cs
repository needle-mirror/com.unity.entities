#if ENABLE_PROFILER
using Unity.Collections;
using Unity.Profiling;

namespace Unity.Entities
{
    static partial class StructuralChangesProfiler
    {
        [GenerateTestsForBurstCompatibility(RequiredUnityDefine = "ENABLE_PROFILER")]
        public readonly struct TimeCounter
        {
            readonly ProfilerCounterValue<long> m_Counter;

            public long Value
            {
                get => m_Counter.Value;
                set => m_Counter.Value = value;
            }

            [ExcludeFromBurstCompatTesting("Takes managed string")]
            public TimeCounter(string name)
            {
                m_Counter = new ProfilerCounterValue<long>(s_Category, name, ProfilerMarkerDataUnit.TimeNanoseconds,
                    ProfilerCounterOptions.FlushOnEndOfFrame | ProfilerCounterOptions.ResetToZeroOnFlush);
            }
        }
    }
}
#endif
