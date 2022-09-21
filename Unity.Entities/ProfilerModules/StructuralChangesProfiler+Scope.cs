#if ENABLE_PROFILER
using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Profiling.LowLevel.Unsafe;

namespace Unity.Entities
{
    static partial class StructuralChangesProfiler
    {
        [GenerateTestsForBurstCompatibility(RequiredUnityDefine = "ENABLE_PROFILER")]
        public readonly unsafe struct Scope
        {
            readonly bool m_Initialized;
            readonly long m_StartTimestamp;
            readonly StructuralChangeType m_Type;
            readonly ulong m_WorldSequenceNumber;
            readonly SystemHandle m_ExecutingSystem;
            readonly TimeCounter m_Counter;

            public Scope(StructuralChangeType type, in WorldUnmanaged world)
            {
                m_StartTimestamp = ProfilerUnsafeUtility.Timestamp;
                m_Type = type;
                m_WorldSequenceNumber = world.SequenceNumber;
                m_ExecutingSystem = world.ExecutingSystem;
                switch (type)
                {
                    case StructuralChangeType.CreateEntity:
                        m_Counter = s_CreateEntityCounter;
                        break;
                    case StructuralChangeType.DestroyEntity:
                        m_Counter = s_DestroyEntityCounter;
                        break;
                    case StructuralChangeType.AddComponent:
                        m_Counter = s_AddComponentCounter;
                        break;
                    case StructuralChangeType.RemoveComponent:
                        m_Counter = s_RemoveComponentCounter;
                        break;
                    case StructuralChangeType.SetSharedComponent:
                        m_Counter = s_SetSharedComponentCounter;
                        break;
                    default:
                        throw new NotImplementedException($"Structural change type {type} not implemented.");
                }
                m_Initialized = true;
            }

            public void Flush()
            {
                if (!m_Initialized)
                    return;

                var elapsed = GetElapsedNanoseconds(m_StartTimestamp);
                m_Counter.Value += elapsed;

                s_StructuralChanges.Add(new StructuralChangeData(m_Type, elapsed, m_WorldSequenceNumber, in m_ExecutingSystem));
            }
        }

        [GenerateTestsForBurstCompatibility(RequiredUnityDefine = "ENABLE_PROFILER")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Begin(StructuralChangeType structuralChangeType, in WorldUnmanaged world)
        {
            if (!s_Initialized) // Enabled test is done at call site
                return;

            s_Scopes.Add(new Scope(structuralChangeType, in world));
        }

        [GenerateTestsForBurstCompatibility(RequiredUnityDefine = "ENABLE_PROFILER")]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void End()
        {
            if (!s_Initialized) // Enabled test is done at call site
                return;

            if (s_Scopes.Length > 0)
            {
                var lastIndex = s_Scopes.Length - 1;
                s_Scopes.ElementAt(lastIndex).Flush();
                s_Scopes.RemoveAt(lastIndex);
            }
        }

        static long GetElapsedNanoseconds(long startTimestamp)
        {
            var now = ProfilerUnsafeUtility.Timestamp;
            var conversionRatio = ProfilerUnsafeUtility.TimestampToNanosecondsConversionRatio;
            return (now - startTimestamp) * conversionRatio.Numerator / conversionRatio.Denominator;
        }
    }
}
#endif
