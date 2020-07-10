using System;
using System.Collections.Generic;
using Unity.Collections;

namespace Unity.Entities
{
    public unsafe partial struct EntityManager
    {
        [Obsolete("GetArchetypeChunkComponentType has been renamed to GetComponentTypeHandle (RemovedAfter 2020-08-01). (UnityUpgradable) -> GetComponentTypeHandle<T>(*)", false)]
        public ArchetypeChunkComponentType<T> GetArchetypeChunkComponentType<T>(bool isReadOnly)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var access = GetCheckedEntityDataAccess();
            var typeIndex = TypeManager.GetTypeIndex<T>();
            return new ArchetypeChunkComponentType<T>(
                access->DependencyManager->Safety.GetSafetyHandleForComponentTypeHandle(typeIndex, isReadOnly), isReadOnly,
                GlobalSystemVersion);
#else
            return new ArchetypeChunkComponentType<T>(isReadOnly, GlobalSystemVersion);
#endif
        }

        [Obsolete("GetArchetypeChunkComponentTypeDynamic has been renamed to GetDynamicComponentTypeHandle (RemovedAfter 2020-08-01). (UnityUpgradable) -> GetDynamicComponentTypeHandle(*)", false)]
        public ArchetypeChunkComponentTypeDynamic GetArchetypeChunkComponentTypeDynamic(ComponentType componentType)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var access = GetCheckedEntityDataAccess();
            return new ArchetypeChunkComponentTypeDynamic(componentType,
                access->DependencyManager->Safety.GetSafetyHandleForDynamicComponentTypeHandle(componentType.TypeIndex, componentType.AccessModeType == ComponentType.AccessMode.ReadOnly),
                GlobalSystemVersion);
#else
            return new ArchetypeChunkComponentTypeDynamic(componentType, GlobalSystemVersion);
#endif
        }

        [Obsolete("GetArchetypeChunkBufferType has been renamed to GetBufferTypeHandle (RemovedAfter 2020-08-01). (UnityUpgradable) -> GetBufferTypeHandle<T>(*)", false)]
        public ArchetypeChunkBufferType<T> GetArchetypeChunkBufferType<T>(bool isReadOnly)
            where T : struct, IBufferElementData
        {
            return GetCheckedEntityDataAccess()->GetArchetypeChunkBufferType<T>(isReadOnly);
        }

        [Obsolete("GetArchetypeChunkSharedComponentType has been renamed to GetSharedComponentTypeHandle (RemovedAfter 2020-08-01). (UnityUpgradable) -> GetSharedComponentTypeHandle<T>()", false)]
        public ArchetypeChunkSharedComponentType<T> GetArchetypeChunkSharedComponentType<T>()
            where T : struct, ISharedComponentData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var typeIndex = TypeManager.GetTypeIndex<T>();
            var access = GetCheckedEntityDataAccess();
            return new ArchetypeChunkSharedComponentType<T>(access->DependencyManager->Safety.GetSafetyHandleForSharedComponentTypeHandle(typeIndex));
#else
            return new ArchetypeChunkSharedComponentType<T>(false);
#endif
        }

        [Obsolete("GetArchetypeChunkEntityType has been renamed to GetEntityTypeHandle (RemovedAfter 2020-08-01). (UnityUpgradable) -> GetEntityTypeHandle()", false)]
        public ArchetypeChunkEntityType GetArchetypeChunkEntityType()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var access = GetCheckedEntityDataAccess();
            return new ArchetypeChunkEntityType(
                access->DependencyManager->Safety.GetSafetyHandleForEntityTypeHandle());
#else
            return new ArchetypeChunkEntityType(false);
#endif
        }
    }
}
