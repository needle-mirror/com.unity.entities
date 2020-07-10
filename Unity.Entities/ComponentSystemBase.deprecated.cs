using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Unity.Entities
{
    public abstract unsafe partial class ComponentSystemBase
    {
        [Obsolete("GetArchetypeChunkComponentType has been renamed to GetComponentTypeHandle (RemovedAfter 2020-08-01). (UnityUpgradable) -> GetComponentTypeHandle<T>(*)", false)]
        public ArchetypeChunkComponentType<T> GetArchetypeChunkComponentType<T>(bool isReadOnly = false) where T : struct, IComponentData
        {
            AddReaderWriter(isReadOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>());
            return EntityManager.GetArchetypeChunkComponentType<T>(isReadOnly);
        }

        [Obsolete("GetArchetypeChunkComponentTypeDynamic has been renamed to GetDynamicComponentTypeHandle (RemovedAfter 2020-08-01). (UnityUpgradable) -> GetDynamicComponentTypeHandle(*)", false)]
        public ArchetypeChunkComponentTypeDynamic GetArchetypeChunkComponentTypeDynamic(ComponentType componentType)
        {
            AddReaderWriter(componentType);
            return EntityManager.GetArchetypeChunkComponentTypeDynamic(componentType);
        }

        [Obsolete("GetArchetypeChunkBufferType has been renamed to GetBufferTypeHandle (RemovedAfter 2020-08-01). (UnityUpgradable) -> GetBufferTypeHandle<T>(*)", false)]
        public ArchetypeChunkBufferType<T> GetArchetypeChunkBufferType<T>(bool isReadOnly = false)
            where T : struct, IBufferElementData
        {
            AddReaderWriter(isReadOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>());
            return EntityManager.GetArchetypeChunkBufferType<T>(isReadOnly);
        }

        [Obsolete("GetArchetypeChunkSharedComponentType has been renamed to GetSharedComponentTypeHandle (RemovedAfter 2020-08-01). (UnityUpgradable) -> GetSharedComponentTypeHandle<T>()", false)]
        public ArchetypeChunkSharedComponentType<T> GetArchetypeChunkSharedComponentType<T>()
            where T : struct, ISharedComponentData
        {
            return EntityManager.GetArchetypeChunkSharedComponentType<T>();
        }

        [Obsolete("GetArchetypeChunkEntityType has been renamed to GetEntityTypeHandle (RemovedAfter 2020-08-01). (UnityUpgradable) -> GetEntityTypeHandle()", false)]
        public ArchetypeChunkEntityType GetArchetypeChunkEntityType()
        {
            return EntityManager.GetArchetypeChunkEntityType();
        }
    }
}
