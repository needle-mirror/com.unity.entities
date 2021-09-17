#if ENABLE_PROFILER
using System;
using Unity.Collections;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine.Profiling;

namespace Unity.Entities
{
    static partial class StructuralChangesProfiler
    {
        [BurstCompatible(RequiredUnityDefine = "ENABLE_PROFILER")]
        public readonly unsafe struct Scope : IDisposable
        {
            readonly bool m_Initialized;
            readonly long m_StartTimestamp;
            readonly StructuralChangeType m_Type;
            readonly TimeCounter m_Counter;
            readonly ulong m_WorldSequenceNumber;
            readonly SystemHandleUntyped m_ExecutingSystem;

            public Scope(StructuralChangeType type, TimeCounter counter, WorldUnmanaged world)
            {
                m_Initialized = true;
                m_StartTimestamp = ProfilerUnsafeUtility.Timestamp;
                m_Type = type;
                m_Counter = counter;
                m_WorldSequenceNumber = world.SequenceNumber;
                m_ExecutingSystem = world.ExecutingSystem;
            }

            public void Dispose()
            {
                if (!m_Initialized)
                    return;

                var elapsed = GetElapsedNanoseconds(m_StartTimestamp);
                m_Counter.Value += elapsed;

                SharedStructuralChangesData.Ref.Data.Add(new StructuralChangeData(m_Type, elapsed, m_WorldSequenceNumber, m_ExecutingSystem));
            }
        }

        [BurstCompatible(RequiredUnityDefine = "ENABLE_PROFILER")]
        public static Scope BeginCreateEntity(WorldUnmanaged world) =>
            Initialized && Profiler.enabled ? new Scope(StructuralChangeType.CreateEntity, SharedCreateEntityCounter.Ref.Data, world) : default;

        [BurstCompatible(RequiredUnityDefine = "ENABLE_PROFILER")]
        public static Scope BeginDestroyEntity(WorldUnmanaged world) =>
            Initialized && Profiler.enabled ? new Scope(StructuralChangeType.DestroyEntity, SharedDestroyEntityCounter.Ref.Data, world) : default;

        [BurstCompatible(RequiredUnityDefine = "ENABLE_PROFILER")]
        public static Scope BeginAddComponent(WorldUnmanaged world) =>
            Initialized && Profiler.enabled ? new Scope(StructuralChangeType.AddComponent, SharedAddComponentCounter.Ref.Data, world) : default;

        [BurstCompatible(RequiredUnityDefine = "ENABLE_PROFILER")]
        public static Scope BeginRemoveComponent(WorldUnmanaged world) =>
            Initialized && Profiler.enabled ? new Scope(StructuralChangeType.RemoveComponent, SharedRemoveComponentCounter.Ref.Data, world) : default;

        static long GetElapsedNanoseconds(long startTimestamp)
        {
            var now = ProfilerUnsafeUtility.Timestamp;
            var conversionRatio = ProfilerUnsafeUtility.TimestampToNanosecondsConversionRatio;
            return (now - startTimestamp) * conversionRatio.Numerator / conversionRatio.Denominator;
        }
    }
}
#endif
