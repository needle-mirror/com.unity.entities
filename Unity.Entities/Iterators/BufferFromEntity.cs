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
        [NativeDisableUnsafePtrRestriction] private readonly EntityComponentStore* m_EntityComponentStore;
        private readonly int m_TypeIndex;
        private readonly bool m_IsReadOnly;
        readonly uint                    m_GlobalSystemVersion;
        int                              m_InternalCapacity;

        LookupCache                      m_Cache;


#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal BufferFromEntity(int typeIndex, EntityComponentStore* entityComponentStoreComponentStore, bool isReadOnly,
                                  AtomicSafetyHandle safety, AtomicSafetyHandle arrayInvalidationSafety)
        {
            m_Safety0 = safety;
            m_ArrayInvalidationSafety = arrayInvalidationSafety;
            m_SafetyReadOnlyCount = isReadOnly ? 2 : 0;
            m_SafetyReadWriteCount = isReadOnly ? 0 : 2;
            m_TypeIndex = typeIndex;
            m_EntityComponentStore = entityComponentStoreComponentStore;
            m_IsReadOnly = isReadOnly;
            m_Cache = default;
            m_GlobalSystemVersion = entityComponentStoreComponentStore->GlobalSystemVersion;

            if (!TypeManager.IsBuffer(m_TypeIndex))
                throw new ArgumentException(
                    $"GetComponentBufferArray<{typeof(T)}> must be IBufferElementData");

            m_InternalCapacity = TypeManager.GetTypeInfo<T>().BufferCapacity;
        }

#else
        internal BufferFromEntity(int typeIndex, EntityComponentStore* entityComponentStoreComponentStore, bool isReadOnly)
        {
            m_TypeIndex = typeIndex;
            m_EntityComponentStore = entityComponentStoreComponentStore;
            m_IsReadOnly = isReadOnly;
            m_Cache = default;
            m_GlobalSystemVersion = entityComponentStoreComponentStore->GlobalSystemVersion;
            m_InternalCapacity = TypeManager.GetTypeInfo<T>().BufferCapacity;
        }

#endif

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
            return m_EntityComponentStore->HasComponent(entity, m_TypeIndex);
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
            var chunk = m_EntityComponentStore->GetChunk(entity);

            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(chunk->Archetype, m_TypeIndex);
            if (typeIndexInArchetype == -1) return false;
            var chunkVersion = chunk->GetChangeVersion(typeIndexInArchetype);

            return ChangeVersionUtility.DidChange(chunkVersion, version);
        }

        public DynamicBuffer<T> this[Entity entity]
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // Note that this check is only for the lookup table into the entity manager
                // The native array performs the actual read only / write only checks
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety0);

                m_EntityComponentStore->AssertEntityHasComponent(entity, m_TypeIndex);
#endif

                var header = (m_IsReadOnly)?
                    (BufferHeader*)m_EntityComponentStore->GetComponentDataWithTypeRO(entity, m_TypeIndex, ref m_Cache) :
                    (BufferHeader*)m_EntityComponentStore->GetComponentDataWithTypeRW(entity, m_TypeIndex, m_GlobalSystemVersion, ref m_Cache);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                return new DynamicBuffer<T>(header, m_Safety0, m_ArrayInvalidationSafety, m_IsReadOnly, false, 0, m_InternalCapacity);
#else
                return new DynamicBuffer<T>(header, m_InternalCapacity);
#endif
            }
        }
    }
}
