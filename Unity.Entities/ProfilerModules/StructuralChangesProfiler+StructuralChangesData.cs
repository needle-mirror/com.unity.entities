#if ENABLE_PROFILER
using Unity.Collections;

namespace Unity.Entities
{
    static partial class StructuralChangesProfiler
    {
        [GenerateTestsForBurstCompatibility(RequiredUnityDefine = "ENABLE_PROFILER")]
        public readonly struct StructuralChangeData
        {
            public readonly StructuralChangeType Type;
            public readonly long ElapsedNanoseconds;
            public readonly ulong WorldSequenceNumber;
            public readonly SystemHandle ExecutingSystem;

            public StructuralChangeData(StructuralChangeType type, long elapsed, ulong worldSeqNumber, in SystemHandle executingSystem)
            {
                Type = type;
                ElapsedNanoseconds = elapsed;
                WorldSequenceNumber = worldSeqNumber;
                ExecutingSystem = executingSystem;
            }
        }
    }
}
#endif
