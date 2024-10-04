using System;
using Unity.Properties;
using UnityEngine.Profiling;

namespace Unity.Entities.Editor
{
    struct ScheduledSystemData
    {
        public readonly SystemCategory Category;
        public readonly int ParentIndex;
        public readonly SystemHandle WorldSystemHandle;
        public int ChildIndex;
        public int ChildCount;

        public int[] UpdateBeforeIndices;
        public int[] UpdateAfterIndices;

        public readonly string NicifiedDisplayName;
        public readonly string TypeName;
        public readonly string FullName;
        public readonly string Namespace;

        public Recorder Recorder;
        public ComponentSystemBase Managed;

        public ScheduledSystemData(ComponentSystemBase system, int parentIndex) // managed systems
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));

            Managed = system;
            WorldSystemHandle = system.SystemHandle;

            Category = SystemUtils.GetSystemCategory(system);

            var systemType = system.GetType();
            NicifiedDisplayName = ContentUtilities.NicifySystemTypeName(systemType);
            TypeName = TypeUtility.GetTypeDisplayName(systemType);
            FullName = systemType.FullName;
            Namespace = systemType.Namespace;

            ParentIndex = parentIndex;
            ChildIndex = 0;
            ChildCount = 0;

            UpdateAfterIndices = Array.Empty<int>();
            UpdateBeforeIndices = Array.Empty<int>();
            Recorder = Recorder.Get($"{system.World?.Name ?? "none"} {FullName}");
        }

        public unsafe ScheduledSystemData(SystemHandle system, World world, int parentIndex) // unmanaged systems
        {
            if (system == SystemHandle.Null)
                throw new ArgumentNullException(nameof(system));
            if (world == null || !world.IsCreated)
                throw new ArgumentNullException(nameof(world));

            var unmanagedWorld = world.Unmanaged;
            if (!unmanagedWorld.IsCreated)
                throw new InvalidOperationException("WorldUnmanaged is not created");

            var systemState = unmanagedWorld.ResolveSystemStateChecked(system);
            if (systemState == null)
                throw new NullReferenceException("SystemState is null");

            Managed = null;
            WorldSystemHandle = system;

            Category = SystemCategory.Unmanaged;

            var systemType = SystemBaseRegistry.GetStructType(systemState->UnmanagedMetaIndex);
            NicifiedDisplayName = ContentUtilities.NicifySystemTypeName(systemType);
            TypeName = TypeUtility.GetTypeDisplayName(systemType);
            FullName = systemType.FullName;
            Namespace = systemType.Namespace;

            ParentIndex = parentIndex;
            ChildIndex = 0;
            ChildCount = 0;

            UpdateAfterIndices = Array.Empty<int>();
            UpdateBeforeIndices = Array.Empty<int>();
            Recorder = Recorder.Get($"{world.Name ?? "none"} {FullName}");
        }
    }
}
