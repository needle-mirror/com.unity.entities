#if ENABLE_PROFILER
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine.Profiling;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.Entities
{
    static partial class EntitiesProfiler
    {
        static bool s_Initialized;
        sealed class SharedStaticData { internal static readonly SharedStatic<StaticData> Ref = SharedStatic<StaticData>.GetOrCreate<SharedStaticData>(); }

        public static Guid Guid => SharedStaticData.Ref.Data.Guid;

        [NotBurstCompatible]
        public static void Initialize()
        {
            if (s_Initialized)
                return;

            // Initialize static data
            SharedStaticData.Ref.Data = new StaticData(16, 1024, 1024);

            // Initialize profiler modules
            StructuralChangesProfiler.Initialize();
            MemoryProfiler.Initialize();

            // Register global events
            World.WorldCreated += OnWorldCreated;
            World.SystemCreated += OnSystemCreated;
#if UNITY_EDITOR
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                RuntimeApplication.PostFrameUpdate += Update;
            else
                EditorApplication.update += Update;
#else
            RuntimeApplication.PostFrameUpdate += Update;
#endif

            s_Initialized = true;
        }

        [NotBurstCompatible]
        public static void Shutdown()
        {
            if (!s_Initialized)
                return;

            // Unregister global events
#if UNITY_EDITOR
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.update -= Update;
            else
                RuntimeApplication.PostFrameUpdate -= Update;
#else
            RuntimeApplication.PostFrameUpdate -= Update;
#endif
            World.SystemCreated -= OnSystemCreated;
            World.WorldCreated -= OnWorldCreated;

            // Shutdown profiler modules
            MemoryProfiler.Shutdown();
            StructuralChangesProfiler.Shutdown();

            // Dispose static data
            SharedStaticData.Ref.Data.Dispose();

            s_Initialized = false;
        }

        [NotBurstCompatible]
        public static void OnWorldCreated(World world) => SharedStaticData.Ref.Data.AddWorld(world);

        [NotBurstCompatible]
        public static void OnSystemCreated(World world, ComponentSystemBase system) => SharedStaticData.Ref.Data.AddSystem(world, system);

        [BurstCompatible(RequiredUnityDefine = "ENABLE_PROFILER")]
        public static unsafe void ArchetypeAdded(Archetype* archetype) => SharedStaticData.Ref.Data.AddArchetype(archetype);

        internal static void Update()
        {
            MemoryProfiler.Update();
            StructuralChangesProfiler.Flush();
            SharedStaticData.Ref.Data.Flush();
        }

        internal static unsafe void FlushSessionMetaData<T>(in Guid guid, int tag, ref UnsafeList<T> list) where T : unmanaged
        {
            if (!Profiler.enabled || list.IsEmpty)
                return;

            Profiler.EmitSessionMetaData(guid, tag, list.AsNativeArray());
            list.Clear();
        }

        internal static unsafe void FlushFrameMetaData<T>(in Guid guid, int tag, ref UnsafeList<T> list) where T : unmanaged
        {
            if (!Profiler.enabled || list.IsEmpty)
                return;

            Profiler.EmitFrameMetaData(guid, tag, list.AsNativeArray());
            list.Clear();
        }

        static unsafe NativeArray<T> AsNativeArray<T>(this UnsafeList<T> list) where T : unmanaged
        {
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(list.Ptr, list.Length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
            return array;
        }

        static FixedString128Bytes ToFixedString128(this string value)
        {
            return value.Substring(0, math.min(value.Length, FixedString128Bytes.UTF8MaxLengthInBytes));
        }
    }
}
#endif
