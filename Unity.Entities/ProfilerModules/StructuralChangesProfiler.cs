#if ENABLE_PROFILER
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;

namespace Unity.Entities
{
    static partial class StructuralChangesProfiler
    {
        sealed class SharedInit { internal static readonly SharedStatic<bool> Ref = SharedStatic<bool>.GetOrCreate<SharedInit>(); }
        sealed class SharedGuid { internal static readonly SharedStatic<Guid> Ref = SharedStatic<Guid>.GetOrCreate<SharedGuid>(); }
        sealed class SharedProfilerCategory { internal static readonly SharedStatic<ProfilerCategory> Ref = SharedStatic<ProfilerCategory>.GetOrCreate<SharedProfilerCategory>(); }
        sealed class SharedStructuralChangesData { internal static readonly SharedStatic<UnsafeList<StructuralChangeData>> Ref = SharedStatic<UnsafeList<StructuralChangeData>>.GetOrCreate<SharedStructuralChangesData>(); }
        sealed class SharedCreateEntityCounter { internal static readonly SharedStatic<TimeCounter> Ref = SharedStatic<TimeCounter>.GetOrCreate<SharedCreateEntityCounter>(); }
        sealed class SharedDestroyEntityCounter { internal static readonly SharedStatic<TimeCounter> Ref = SharedStatic<TimeCounter>.GetOrCreate<SharedDestroyEntityCounter>(); }
        sealed class SharedAddComponentCounter { internal static readonly SharedStatic<TimeCounter> Ref = SharedStatic<TimeCounter>.GetOrCreate<SharedAddComponentCounter>(); }
        sealed class SharedRemoveComponentCounter { internal static readonly SharedStatic<TimeCounter> Ref = SharedStatic<TimeCounter>.GetOrCreate<SharedRemoveComponentCounter>(); }

        static ref bool Initialized => ref SharedInit.Ref.Data;
        public static Guid Guid => SharedGuid.Ref.Data;
        public static ProfilerCategory Category => SharedProfilerCategory.Ref.Data;
        public static TimeCounter CreateEntityCounter => SharedCreateEntityCounter.Ref.Data;
        public static TimeCounter DestroyEntityCounter => SharedDestroyEntityCounter.Ref.Data;
        public static TimeCounter AddComponentCounter => SharedAddComponentCounter.Ref.Data;
        public static TimeCounter RemoveComponentCounter => SharedRemoveComponentCounter.Ref.Data;

        [NotBurstCompatible]
        public static void Initialize()
        {
            if (Initialized)
                return;

            SharedGuid.Ref.Data = new Guid("7e866afa654f4469aef462540c0192fa");
            SharedProfilerCategory.Ref.Data = new ProfilerCategory("Entities Structural Changes");
            SharedStructuralChangesData.Ref.Data = new UnsafeList<StructuralChangeData>(1, Allocator.Persistent);
            SharedCreateEntityCounter.Ref.Data = new TimeCounter("Create Entity");
            SharedDestroyEntityCounter.Ref.Data = new TimeCounter("Destroy Entity");
            SharedAddComponentCounter.Ref.Data = new TimeCounter("Add Component");
            SharedRemoveComponentCounter.Ref.Data = new TimeCounter("Remove Component");

            Initialized = true;
        }

        [NotBurstCompatible]
        public static void Shutdown()
        {
            if (!Initialized)
                return;

            SharedStructuralChangesData.Ref.Data.Dispose();

            Initialized = false;
        }

        public unsafe static void Flush()
        {
            if (!Initialized)
                return;

            EntitiesProfiler.FlushFrameMetaData(in SharedGuid.Ref.Data, 0, ref SharedStructuralChangesData.Ref.Data);
        }
    }
}
#endif
