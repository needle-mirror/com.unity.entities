using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    [NativeContainer]
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct BufferFromEntity<T> where T : struct, IBufferElementData
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private readonly AtomicSafetyHandle m_Safety0;
        private readonly AtomicSafetyHandle m_ArrayInvalidationSafety;
        private int m_SafetyReadOnlyCount;
        private int m_SafetyReadWriteCount;
#endif
        [NativeDisableUnsafePtrRestriction] private readonly EntityDataAccess* m_Access;
        private readonly int m_TypeIndex;
        private readonly bool m_IsReadOnly;
        readonly uint                    m_GlobalSystemVersion;
        int                              m_InternalCapacity;

        LookupCache                      m_Cache;


#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal BufferFromEntity(int typeIndex, EntityDataAccess* access, bool isReadOnly,
                                  AtomicSafetyHandle safety, AtomicSafetyHandle arrayInvalidationSafety)
        {
            m_Safety0 = safety;
            m_ArrayInvalidationSafety = arrayInvalidationSafety;
            m_SafetyReadOnlyCount = isReadOnly ? 2 : 0;
            m_SafetyReadWriteCount = isReadOnly ? 0 : 2;
            m_TypeIndex = typeIndex;
            m_Access = access;
            m_IsReadOnly = isReadOnly;
            m_Cache = default;
            m_GlobalSystemVersion = access->EntityComponentStore->GlobalSystemVersion;

            if (!TypeManager.IsBuffer(m_TypeIndex))
                throw new ArgumentException(
                    $"GetComponentBufferArray<{typeof(T)}> must be IBufferElementData");

            m_InternalCapacity = TypeManager.GetTypeInfo<T>().BufferCapacity;
        }

#else
        internal BufferFromEntity(int typeIndex, EntityDataAccess* access, bool isReadOnly)
        {
            m_TypeIndex = typeIndex;
            m_Access = access;
            m_IsReadOnly = isReadOnly;
            m_Cache = default;
            m_GlobalSystemVersion = access->EntityComponentStore->GlobalSystemVersion;
            m_InternalCapacity = TypeManager.GetTypeInfo<T>().BufferCapacity;
        }

#endif

        /// <summary>
        /// Retrieves the buffer components associated with the specified <see cref="Entity"/>, if it exists. Then reports if the instance still refers to a valid entity and that it has a
        /// buffer component of type T.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// /// <param name="bufferData">The buffer component of type T for the given entity, if it exists.</param>
        /// <returns>True if the entity has a buffer component of type T, and false if it does not.</returns>
        /// <remarks>To report if the provided entity has a buffer component of type T, this function confirms
        /// whether the <see cref="EntityArchetype"/> of the provided entity includes buffer components of type T.
        /// </remarks>
        public bool TryGetBuffer(Entity entity, out DynamicBuffer<T> bufferData)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety0);
#endif
            var ecs = m_Access->EntityComponentStore;
            var hasComponent = ecs->HasComponent(entity, m_TypeIndex, ref m_Cache);

            if (hasComponent)
            {
                var header = (m_IsReadOnly)?
                    (BufferHeader*)ecs->GetComponentDataWithTypeRO(entity, m_TypeIndex, ref m_Cache) :
                    (BufferHeader*)ecs->GetComponentDataWithTypeRW(entity, m_TypeIndex, m_GlobalSystemVersion, ref m_Cache);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                bufferData =  new DynamicBuffer<T>(header, m_Safety0, m_ArrayInvalidationSafety, m_IsReadOnly, false, 0, m_InternalCapacity);
#else
                bufferData = new DynamicBuffer<T>(header, m_InternalCapacity);
#endif
            }
            else
            {
                bufferData = default;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reports whether the specified <see cref="Entity"/> instance still refers to a valid entity and that it has a
        /// buffer component of type T.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <returns>True if the entity has a buffer component of type T, and false if it does not. Also returns false if
        /// the Entity instance refers to an entity that has been destroyed.</returns>
        /// <remarks>To report if the provided entity has a buffer component of type T, this function confirms
        /// whether the <see cref="EntityArchetype"/> of the provided entity includes buffer components of type T.
        /// </remarks>
        public bool HasComponent(Entity entity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety0);
#endif
            var ecs = m_Access->EntityComponentStore;
            return ecs->HasComponent(entity, m_TypeIndex);
        }

        /// <summary>
        /// Reports whether any of IBufferElementData components of the type T, in the chunk containing the
        /// specified <see cref="Entity"/>, could have changed.
        /// </summary>
        /// <remarks>
        /// Note that for efficiency, the change version applies to whole chunks not individual entities. The change
        /// version is incremented even when another job or system that has declared write access to a component does
        /// not actually change the component value.</remarks>
        /// <param name="entity">The entity.</param>
        /// <param name="version">The version to compare. In a system, this parameter should be set to the
        /// current <see cref="Unity.Entities.ComponentSystemBase.LastSystemVersion"/> at the time the job is run or
        /// scheduled.</param>
        /// <returns>True, if the version number stored in the chunk for this component is more recent than the version
        /// passed to the <paramref name="version"/> parameter.</returns>
        public bool DidChange(Entity entity, uint version)
        {
            var ecs = m_Access->EntityComponentStore;
            var chunk = ecs->GetChunk(entity);

            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(chunk->Archetype, m_TypeIndex);
            if (typeIndexInArchetype == -1) return false;
            var chunkVersion = chunk->GetChangeVersion(typeIndexInArchetype);

            return ChangeVersionUtility.DidChange(chunkVersion, version);
        }

        public DynamicBuffer<T> this[Entity entity]
        {
            get
            {
                var ecs = m_Access->EntityComponentStore;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // Note that this check is only for the lookup table into the entity manager
                // The native array performs the actual read only / write only checks
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety0);

                ecs->AssertEntityHasComponent(entity, m_TypeIndex);
#endif

                var header = (m_IsReadOnly)?
                    (BufferHeader*)ecs->GetComponentDataWithTypeRO(entity, m_TypeIndex, ref m_Cache) :
                    (BufferHeader*)ecs->GetComponentDataWithTypeRW(entity, m_TypeIndex, m_GlobalSystemVersion, ref m_Cache);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                return new DynamicBuffer<T>(header, m_Safety0, m_ArrayInvalidationSafety, m_IsReadOnly, false, 0, m_InternalCapacity);
#else
                return new DynamicBuffer<T>(header, m_InternalCapacity);
#endif
            }
        }

        internal bool IsComponentEnabled(Entity entity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            // Note that this check is only for the lookup table into the entity manager
            // The native array performs the actual read only / write only checks
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety0);
#endif
            return m_Access->IsComponentEnabled(entity, m_TypeIndex);
        }

        internal void SetComponentEnabled(Entity entity, bool value)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            // Note that this check is only for the lookup table into the entity manager
            // The native array performs the actual read only / write only checks
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety0);
#endif
            m_Access->SetComponentEnabled(entity, m_TypeIndex, value);
        }
    }
}
