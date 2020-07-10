using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    [Obsolete("ArchetypeChunkComponentType<T> has been renamed to ComponentTypeHandle<T> (RemovedAfter 2020-08-01). (UnityUpgradable) -> ComponentTypeHandle<T>", false)]
    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct ArchetypeChunkComponentType<T>
    {
        internal readonly int m_TypeIndex;
        internal readonly uint m_GlobalSystemVersion;
        internal readonly bool m_IsReadOnly;
        internal readonly bool m_IsZeroSized;

        public uint GlobalSystemVersion => m_GlobalSystemVersion;
        public bool IsReadOnly => m_IsReadOnly;

#pragma warning disable 0414
        private readonly int m_Length;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private readonly int m_MinIndex;
        private readonly int m_MaxIndex;
        internal readonly AtomicSafetyHandle m_Safety;
#endif
#pragma warning restore 0414

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal ArchetypeChunkComponentType(AtomicSafetyHandle safety, bool isReadOnly, uint globalSystemVersion)
#else
        internal ArchetypeChunkComponentType(bool isReadOnly, uint globalSystemVersion)
#endif
        {
            m_Length = 1;
            m_TypeIndex = TypeManager.GetTypeIndex<T>();
            m_IsZeroSized = TypeManager.GetTypeInfo(m_TypeIndex).IsZeroSized;
            m_GlobalSystemVersion = globalSystemVersion;
            m_IsReadOnly = isReadOnly;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_MinIndex = 0;
            m_MaxIndex = 0;
            m_Safety = safety;
#endif
        }
    }

    [Obsolete("ArchetypeChunkComponentTypeDynamic has been renamed to DynamicComponentTypeHandle (RemovedAfter 2020-08-01). (UnityUpgradable) -> DynamicComponentTypeHandle", false)]
    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct ArchetypeChunkComponentTypeDynamic
    {
        internal readonly int m_TypeIndex;
        internal readonly uint m_GlobalSystemVersion;
        internal readonly bool m_IsReadOnly;
        internal readonly bool m_IsZeroSized;
        public short m_TypeLookupCache;

        public uint GlobalSystemVersion => m_GlobalSystemVersion;
        public bool IsReadOnly => m_IsReadOnly;

#pragma warning disable 0414
        private readonly int m_Length;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private readonly int m_MinIndex;
        private readonly int m_MaxIndex;
        internal readonly AtomicSafetyHandle m_Safety;
#endif
#pragma warning restore 0414

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal ArchetypeChunkComponentTypeDynamic(ComponentType componentType, AtomicSafetyHandle safety, uint globalSystemVersion)
#else
        internal ArchetypeChunkComponentTypeDynamic(ComponentType componentType, uint globalSystemVersion)
#endif
        {
            m_Length = 1;
            m_TypeIndex = componentType.TypeIndex;
            m_IsZeroSized = TypeManager.GetTypeInfo(m_TypeIndex).IsZeroSized;
            m_GlobalSystemVersion = globalSystemVersion;
            m_IsReadOnly = componentType.AccessModeType == ComponentType.AccessMode.ReadOnly;
            m_TypeLookupCache = 0;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_MinIndex = 0;
            m_MaxIndex = 0;
            m_Safety = safety;
#endif
        }
    }

    [Obsolete("ArchetypeChunkBufferType<T> has been renamed to BufferTypeHandle<T> (RemovedAfter 2020-08-01). (UnityUpgradable) -> BufferTypeHandle<T>", false)]
    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct ArchetypeChunkBufferType<T>
        where T : struct, IBufferElementData
    {
        internal readonly int m_TypeIndex;
        internal readonly uint m_GlobalSystemVersion;
        internal readonly bool m_IsReadOnly;

        public uint GlobalSystemVersion => m_GlobalSystemVersion;
        public bool IsReadOnly => m_IsReadOnly;

#pragma warning disable 0414
        private readonly int m_Length;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private readonly int m_MinIndex;
        private readonly int m_MaxIndex;

        internal AtomicSafetyHandle m_Safety0;
        internal AtomicSafetyHandle m_Safety1;
        internal int m_SafetyReadOnlyCount;
        internal int m_SafetyReadWriteCount;
#endif
#pragma warning restore 0414

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal ArchetypeChunkBufferType(AtomicSafetyHandle safety, AtomicSafetyHandle arrayInvalidationSafety, bool isReadOnly, uint globalSystemVersion)
#else
        internal ArchetypeChunkBufferType(bool isReadOnly, uint globalSystemVersion)
#endif
        {
            m_Length = 1;
            m_TypeIndex = TypeManager.GetTypeIndex<T>();
            m_GlobalSystemVersion = globalSystemVersion;
            m_IsReadOnly = isReadOnly;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_MinIndex = 0;
            m_MaxIndex = 0;
            m_Safety0 = safety;
            m_Safety1 = arrayInvalidationSafety;
            m_SafetyReadOnlyCount = isReadOnly ? 2 : 0;
            m_SafetyReadWriteCount = isReadOnly ? 0 : 2;
#endif
        }
    }

    [Obsolete("ArchetypeChunkSharedComponentType<T> has been renamed to SharedComponentTypeHandle<T> (RemovedAfter 2020-08-01). (UnityUpgradable) -> SharedComponentTypeHandle<T>", false)]
    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct ArchetypeChunkSharedComponentType<T>
        where T : struct, ISharedComponentData
    {
        internal readonly int m_TypeIndex;

#pragma warning disable 0414
        private readonly int m_Length;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private readonly int m_MinIndex;
        private readonly int m_MaxIndex;
        internal readonly AtomicSafetyHandle m_Safety;
#endif
#pragma warning restore 0414

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal ArchetypeChunkSharedComponentType(AtomicSafetyHandle safety)
#else
        internal unsafe ArchetypeChunkSharedComponentType(bool unused)
#endif
        {
            m_Length = 1;
            m_TypeIndex = TypeManager.GetTypeIndex<T>();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_MinIndex = 0;
            m_MaxIndex = 0;
            m_Safety = safety;
#endif
        }
    }

    [Obsolete("ArchetypeChunkEntityType has been renamed to EntityTypeHandle (RemovedAfter 2020-08-01). (UnityUpgradable) -> EntityTypeHandle", false)]
    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct ArchetypeChunkEntityType
    {
#pragma warning disable 0414
        private readonly int m_Length;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private readonly int m_MinIndex;
        private readonly int m_MaxIndex;
        internal readonly AtomicSafetyHandle m_Safety;
#endif
#pragma warning restore 0414

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal ArchetypeChunkEntityType(AtomicSafetyHandle safety)
#else
        internal unsafe ArchetypeChunkEntityType(bool unused)
#endif
        {
            m_Length = 1;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_MinIndex = 0;
            m_MaxIndex = 0;
            m_Safety = safety;
#endif
        }
    }

#if UNITY_SKIP_UPDATES_WITH_VALIDATION_SUITE
    [Obsolete("ArchetypeChunkComponentObjects<T> has been renamed to ManagedComponentAccessor<T> (RemovedAfter 2020-08-01). -- please remove the UNITY_SKIP_UPDATES_WITH_VALIDATION_SUITE define in the Unity.Entities assembly definition file if this message is unexpected and you want to attempt an automatic upgrade.", false)]
#else
    [Obsolete("ArchetypeChunkComponentObjects<T> has been renamed to ManagedComponentAccessor<T> (RemovedAfter 2020-08-01). (UnityUpgradable) -> ManagedComponentAccessor<T>", false)]
#endif
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ArchetypeChunkComponentObjects<T>
        where T : class
    {
        /// <summary>
        ///
        /// </summary>
        NativeArray<int> m_IndexArray;
        EntityComponentStore* m_EntityComponentStore;
        ManagedComponentStore m_ManagedComponentStore;

        unsafe internal ArchetypeChunkComponentObjects(NativeArray<int> indexArray, EntityManager entityManager)
        {
            var access = entityManager.GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var mcs = access->ManagedComponentStore;

            m_IndexArray = indexArray;
            m_EntityComponentStore = ecs;
            m_ManagedComponentStore = mcs;
        }

        public unsafe T this[int index]
        {
            get
            {
                // Direct access to the m_ManagedComponentData for fast iteration
                // we can not cache m_ManagedComponentData directly since it can be reallocated
                return (T)m_ManagedComponentStore.m_ManagedComponentData[m_IndexArray[index]];
            }

            set
            {
                var iManagedComponent = m_IndexArray[index];
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (value != null && typeof(T) != value.GetType())
                    throw new ArgumentException($"Assigning component value is of type: {value.GetType()} but the expected component type is: {typeof(T)}");
#endif
                m_ManagedComponentStore.UpdateManagedComponentValue(&iManagedComponent, value, ref *m_EntityComponentStore);
                m_IndexArray[index] = iManagedComponent;
            }
        }

        public int Length => m_IndexArray.Length;
    }
}
