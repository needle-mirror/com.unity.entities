#if ENABLE_PROFILER
using Unity.Collections;
using Unity.Profiling;

namespace Unity.Entities
{
    static partial class StructuralChangesProfiler
    {
        [BurstCompatible(RequiredUnityDefine = "ENABLE_PROFILER")]
        public readonly struct TimeCounter
        {
            readonly ProfilerCounterValue<long> m_Counter;
            readonly FixedString32Bytes m_Name;

            public long Value
            {
                get => m_Counter.Value;
                set => m_Counter.Value = value;
            }

            [NotBurstCompatible]
            public string Name => m_Name.ToString();

            [NotBurstCompatible]
            public TimeCounter(string name)
            {
                m_Name = name;
                m_Counter = new ProfilerCounterValue<long>(SharedProfilerCategory.Ref.Data, name, ProfilerMarkerDataUnit.TimeNanoseconds,
                    ProfilerCounterOptions.FlushOnEndOfFrame | ProfilerCounterOptions.ResetToZeroOnFlush);
            }
        }
    }
}
#endif
