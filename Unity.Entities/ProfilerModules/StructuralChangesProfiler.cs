#if ENABLE_PROFILER
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine.Profiling;

namespace Unity.Entities
{
    static partial class StructuralChangesProfiler
    {
        internal const string k_CategoryName = "Entities Structural Changes";
        internal const string k_CreateEntityCounterName = "Create Entity";
        internal const string k_DestroyEntityCounterName = "Destroy Entity";
        internal const string k_AddComponentCounterName = "Add Component";
        internal const string k_RemoveComponentCounterName = "Remove Component";
        internal const string k_SetSharedComponentCounterName = "Set Shared Component";

        sealed class SharedInit { internal static readonly SharedStatic<bool> Ref = SharedStatic<bool>.GetOrCreate<SharedInit>(); }
        sealed class SharedGuid { internal static readonly SharedStatic<Guid> Ref = SharedStatic<Guid>.GetOrCreate<SharedGuid>(); }
        sealed class SharedProfilerCategory { internal static readonly SharedStatic<ProfilerCategory> Ref = SharedStatic<ProfilerCategory>.GetOrCreate<SharedProfilerCategory>(); }
        sealed class SharedScopes { internal static readonly SharedStatic<UnsafeList<Scope>> Ref = SharedStatic<UnsafeList<Scope>>.GetOrCreate<SharedScopes>(); }
        sealed class SharedStructuralChangesData { internal static readonly SharedStatic<UnsafeList<StructuralChangeData>> Ref = SharedStatic<UnsafeList<StructuralChangeData>>.GetOrCreate<SharedStructuralChangesData>(); }
        sealed class SharedCreateEntityCounter { internal static readonly SharedStatic<TimeCounter> Ref = SharedStatic<TimeCounter>.GetOrCreate<SharedCreateEntityCounter>(); }
        sealed class SharedDestroyEntityCounter { internal static readonly SharedStatic<TimeCounter> Ref = SharedStatic<TimeCounter>.GetOrCreate<SharedDestroyEntityCounter>(); }
        sealed class SharedAddComponentCounter { internal static readonly SharedStatic<TimeCounter> Ref = SharedStatic<TimeCounter>.GetOrCreate<SharedAddComponentCounter>(); }
        sealed class SharedRemoveComponentCounter { internal static readonly SharedStatic<TimeCounter> Ref = SharedStatic<TimeCounter>.GetOrCreate<SharedRemoveComponentCounter>(); }
        sealed class SharedSetSharedComponentCounter { internal static readonly SharedStatic<TimeCounter> Ref = SharedStatic<TimeCounter>.GetOrCreate<SharedSetSharedComponentCounter>(); }

        static ref bool s_Initialized => ref SharedInit.Ref.Data;
        static ref Guid s_Guid => ref SharedGuid.Ref.Data;
        static ref ProfilerCategory s_Category => ref SharedProfilerCategory.Ref.Data;
        static ref UnsafeList<Scope> s_Scopes => ref SharedScopes.Ref.Data;
        static ref UnsafeList<StructuralChangeData> s_StructuralChanges => ref SharedStructuralChangesData.Ref.Data;
        static ref TimeCounter s_CreateEntityCounter => ref SharedCreateEntityCounter.Ref.Data;
        static ref TimeCounter s_DestroyEntityCounter => ref SharedDestroyEntityCounter.Ref.Data;
        static ref TimeCounter s_AddComponentCounter => ref SharedAddComponentCounter.Ref.Data;
        static ref TimeCounter s_RemoveComponentCounter => ref SharedRemoveComponentCounter.Ref.Data;
        static ref TimeCounter s_SetSharedComponentCounter => ref SharedSetSharedComponentCounter.Ref.Data;

#if UNITY_DOTSRUNTIME
        public static bool Enabled => Profiler.enabled;
#else
        public static bool Enabled => Profiler.enabled && Profiler.IsCategoryEnabled(SharedProfilerCategory.Ref.Data);
#endif
        public static Guid Guid => SharedGuid.Ref.Data;
        public static ProfilerCategory Category => SharedProfilerCategory.Ref.Data;

        public static void Initialize()
        {
            if (s_Initialized)
                return;

            s_Guid = new Guid("7e866afa654f4469aef462540c0192fa");
            s_Category = new ProfilerCategory(k_CategoryName);
            s_Scopes = new UnsafeList<Scope>(1, Allocator.Persistent);
            s_StructuralChanges = new UnsafeList<StructuralChangeData>(16, Allocator.Persistent);
            s_CreateEntityCounter = new TimeCounter(k_CreateEntityCounterName);
            s_DestroyEntityCounter = new TimeCounter(k_DestroyEntityCounterName);
            s_AddComponentCounter = new TimeCounter(k_AddComponentCounterName);
            s_RemoveComponentCounter = new TimeCounter(k_RemoveComponentCounterName);
            s_SetSharedComponentCounter = new TimeCounter(k_SetSharedComponentCounterName);

            s_Initialized = true;
        }

        public static void Shutdown()
        {
            if (!s_Initialized)
                return;

            s_StructuralChanges.Dispose();
            s_Scopes.Dispose();

            s_Initialized = false;
        }

        public unsafe static void Flush()
        {
            if (!s_Initialized || !Enabled)
                return;

            EntitiesProfiler.FlushFrameMetaData(in s_Guid, 0, ref s_StructuralChanges);
            s_Scopes.Clear();
        }
    }
}
#endif
