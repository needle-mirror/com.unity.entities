using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    public struct EntityStorageInfo
    {
        public ArchetypeChunk Chunk;
        public int IndexInChunk;
    }

    /// <summary>
    /// A [NativeContainer] that provides access to information about how Entities are stored. <see cref="Entity"/>.
    /// </summary>
    /// <remarks>
    /// StorageInfoFromEntity is a native container that provides access to information about how Entities are stored.
    /// You can use StorageInfoFromEntity to look up data associated with one entity while iterating over a
    /// different set of entities.
    ///
    /// To get a StorageInfoFromEntity, call <see cref="ComponentSystemBase.GetStorageInfoFromEntity"/>.
    ///
    /// Pass a StorageInfoFromEntity container to a job by defining a public field of the appropriate type
    /// in your IJob implementation. You can safely read from StorageInfoFromEntity in any job, and the StorageInfoFromEntity
    /// will never write data.
    ///
    /// If you would like to access an entity's storage information outside of a job, consider using the
    /// <see cref="EntityManager"/> methods <see cref="EntityManager.GetStorageInfo"/> instead, to avoid the overhead
    /// of creating a StorageInfoFromEntity object.
    ///
    /// [NativeContainer]: https://docs.unity3d.com/ScriptReference/Unity.Collections.LowLevel.Unsafe.NativeContainerAttribute
    /// [NativeContainerIsReadOnly]: https://docs.unity3d.com/ScriptReference/Unity.Collections.LowLevel.Unsafe.NativeContainerIsReadOnlyAttribute.html
    /// </remarks>
    [NativeContainer]
    [NativeContainerIsReadOnly]
    public unsafe struct StorageInfoFromEntity
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        readonly AtomicSafetyHandle      m_Safety;
#endif
        [NativeDisableUnsafePtrRestriction]
        readonly EntityComponentStore*   m_EntityComponentStore;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal StorageInfoFromEntity(EntityComponentStore* entityComponentStoreComponentStore, AtomicSafetyHandle safety)
        {
            m_Safety = safety;
            m_EntityComponentStore = entityComponentStoreComponentStore;
        }

#else
        internal StorageInfoFromEntity(EntityComponentStore* entityComponentStoreComponentStore)
        {
            m_EntityComponentStore = entityComponentStoreComponentStore;
        }

#endif

        /// <summary>
        /// Reports whether the specified <see cref="Entity"/> instance still refers to a valid entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <returns>True if the entity exists and is valid, and returns false if
        /// the Entity instance refers to an entity that has been destroyed.</returns>
        public bool Exists(Entity entity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            return m_EntityComponentStore->Exists(entity);
        }

        /// <summary>
        /// Gets an <see cref="EntityStorageInfo"/> for the specified entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <exception cref="System.ArgumentException">Thrown if T is zero-size.</exception>
        public EntityStorageInfo this[Entity entity]
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                m_EntityComponentStore->AssertEntitiesExist(&entity, 1);

                var entityInChunk = m_EntityComponentStore->GetEntityInChunk(entity);

                return new EntityStorageInfo
                {
                    Chunk = new ArchetypeChunk(entityInChunk.Chunk, m_EntityComponentStore),
                    IndexInChunk = entityInChunk.IndexInChunk
                };
            }
        }
    }
}
