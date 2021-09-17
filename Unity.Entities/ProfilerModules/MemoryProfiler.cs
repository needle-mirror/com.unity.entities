#if ENABLE_PROFILER
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine.Profiling;

namespace Unity.Entities
{
    static partial class MemoryProfiler
    {
        static bool s_Initialized;

        sealed class SharedGuid { internal static readonly SharedStatic<Guid> Ref = SharedStatic<Guid>.GetOrCreate<SharedGuid>(); }
        sealed class SharedProfilerCategory { internal static readonly SharedStatic<ProfilerCategory> Ref = SharedStatic<ProfilerCategory>.GetOrCreate<SharedProfilerCategory>(); }
        sealed class SharedAllocatedBytesCounter { internal static readonly SharedStatic<BytesCounter> Ref = SharedStatic<BytesCounter>.GetOrCreate<SharedAllocatedBytesCounter>(); }
        sealed class SharedUnusedBytesCounter { internal static readonly SharedStatic<BytesCounter> Ref = SharedStatic<BytesCounter>.GetOrCreate<SharedUnusedBytesCounter>(); }

        public static Guid Guid => SharedGuid.Ref.Data;
        public static ProfilerCategory Category => SharedProfilerCategory.Ref.Data;
        public static BytesCounter AllocatedBytesCounter => SharedAllocatedBytesCounter.Ref.Data;
        public static BytesCounter UnusedBytesCounter => SharedUnusedBytesCounter.Ref.Data;

        [NotBurstCompatible]
        public static void Initialize()
        {
            if (s_Initialized)
                return;

            SharedGuid.Ref.Data = new Guid("d1589a720beb45b78a4087311ae83a2c");
            SharedProfilerCategory.Ref.Data = new ProfilerCategory("Entities Memory");
            SharedAllocatedBytesCounter.Ref.Data = new BytesCounter("Allocated Memory");
            SharedUnusedBytesCounter.Ref.Data = new BytesCounter("Unused Memory");

            s_Initialized = true;
        }

        [NotBurstCompatible]
        public static void Shutdown()
        {
            if (!s_Initialized)
                return;

            s_Initialized = false;
        }

        [NotBurstCompatible]
        public static void Update()
        {
            if (!s_Initialized || !Profiler.enabled)
                return;

            for (var i = 0; i < World.All.Count; ++i)
            {
                var world = World.All[i];
                if (!world.IsCreated)
                    continue;

                // If world is in exclusive transaction, postpone to next frame
                if (!world.EntityManager.CanBeginExclusiveEntityTransaction())
                    continue;

                // Get or create recording system on-demand (only if profiling)
                var system = world.GetOrCreateSystem<RecordingSystem>();
                if (system == null)
                    return;

                // Manually update recording system
                system.Update();
            }
        }
    }
}
#endif
