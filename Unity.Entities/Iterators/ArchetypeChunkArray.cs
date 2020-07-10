using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.CodeGeneratedJobForEach;
using Unity.Mathematics;

namespace Unity.Entities
{
    /// <summary>
    /// A block of unmanaged memory containing the components for entities sharing the same
    /// <see cref="Archetype"/>.
    /// </summary>
    [DebuggerTypeProxy(typeof(ArchetypeChunkDebugView))]
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public unsafe struct ArchetypeChunk : IEquatable<ArchetypeChunk>
    {
        [FieldOffset(0)]
        [NativeDisableUnsafePtrRestriction] internal Chunk* m_Chunk;
        [FieldOffset(8)]
        [NativeDisableUnsafePtrRestriction] internal EntityComponentStore* m_EntityComponentStore;

        [FieldOffset(16)] internal int m_BatchStartEntityIndex;
        [FieldOffset(20)] internal int m_BatchEntityCount;

        private const int kUseChunkCount = 0;

        /// <summary>
        /// If the ArchetypeChunk is not sub-batched, returns the number of entities in the chunk. Otherwise, returns the number of entities referenced by this batch.
        /// </summary>
        public int Count => math.select(m_BatchEntityCount, m_Chunk->Count, m_BatchEntityCount == kUseChunkCount && m_Chunk != null);

        /// <summary>
        /// If the ArchetypeChunk is sub-batched, returns the number of entities referenced by this batch.
        /// </summary>
        public int BatchEntityCount => m_BatchEntityCount;

        /// <summary>
        /// The number of entities currently stored in the chunk.
        /// </summary>
        public int ChunkEntityCount => m_Chunk->Count;

        /// <summary>
        /// The number of entities that can fit in this chunk.
        /// </summary>
        /// <remarks>The capacity of a chunk depends on the size of the components making up the
        /// <see cref="Archetype"/> of the entities stored in the chunk.</remarks>
        public int Capacity => m_Chunk->Capacity;
        /// <summary>
        /// Whether this chunk is exactly full.
        /// </summary>
        public bool Full => Count == Capacity;

        internal static void CalculateBatchSizeAndStartIndex(int chunkEntityCount, int batchesPerChunk, int batchIndexInChunk, out int batchCount, out int startIndex)
        {
            var minEntitiesInBatch = chunkEntityCount / batchesPerChunk;
            var remainder = chunkEntityCount % batchesPerChunk;
            var maxEntitiesInBatch = minEntitiesInBatch + 1;

            batchCount = math.select(minEntitiesInBatch, minEntitiesInBatch + 1, batchIndexInChunk < remainder);// divide up batches equally
            startIndex = maxEntitiesInBatch * math.min(batchIndexInChunk, remainder) + minEntitiesInBatch * math.max(0, batchIndexInChunk - remainder);
        }

        internal static ArchetypeChunk EntityBatchFromChunk(Chunk* chunk, int batchesPerChunk, int batchIndexInChunk, EntityComponentStore* entityComponentStore)
        {
            CalculateBatchSizeAndStartIndex(chunk->Count, batchesPerChunk, batchIndexInChunk, out var batchCount, out var startIndex);

            return new ArchetypeChunk
            {
                m_Chunk = chunk,
                m_EntityComponentStore = entityComponentStore,
                m_BatchStartEntityIndex = startIndex,
                m_BatchEntityCount = batchCount
            };
        }

        internal ArchetypeChunk(Chunk* chunk, EntityComponentStore* entityComponentStore)
        {
            m_Chunk = chunk;
            m_EntityComponentStore = entityComponentStore;
            m_BatchEntityCount = kUseChunkCount;
            m_BatchStartEntityIndex = 0;
        }

        /// <summary>
        /// Two ArchetypeChunk instances are equal if they reference the same block of chunk and entity component store memory.
        /// </summary>
        /// <param name="lhs">An ArchetypeChunk</param>
        /// <param name="rhs">Another ArchetypeChunk</param>
        /// <returns>True, if both ArchetypeChunk instances reference the same memory, or both contain null memory
        /// references.</returns>
        public static bool operator==(ArchetypeChunk lhs, ArchetypeChunk rhs)
        {
            return lhs.m_Chunk == rhs.m_Chunk && lhs.m_EntityComponentStore == rhs.m_EntityComponentStore;
        }

        /// <summary>
        /// Two ArchetypeChunk instances are only equal if they reference the same block of chunk and entity component store memory.
        /// </summary>
        /// <param name="lhs">An ArchetypeChunk</param>
        /// <param name="rhs">Another ArchetypeChunk</param>
        /// <returns>True, if the ArchetypeChunk instances reference different blocks of memory.</returns>
        public static bool operator!=(ArchetypeChunk lhs, ArchetypeChunk rhs)
        {
            return lhs.m_Chunk != rhs.m_Chunk || lhs.m_EntityComponentStore != rhs.m_EntityComponentStore;
        }

        /// <summary>
        /// Two ArchetypeChunk instances are equal if they reference the same block of chunk memory.
        /// </summary>
        /// <param name="compare">An object</param>
        /// <returns>True if <paramref name="compare"/> is an `ArchetypeChunk` instance that references the same memory,
        /// or both contain null memory references; otherwise false.</returns>
        public override bool Equals(object compare)
        {
            return this == (ArchetypeChunk)compare;
        }

        /// <summary>
        /// Computes a hashcode to support hash-based collections.
        /// </summary>
        /// <returns>The computed hash.</returns>
        public override int GetHashCode()
        {
            UIntPtr chunkAddr   = (UIntPtr)m_Chunk;
            long    chunkHiHash = ((long)chunkAddr) >> 15;
            int     chunkHash   = (int)chunkHiHash;
            return chunkHash;
        }

        /// <summary>
        /// The archetype of the entities stored in this chunk.
        /// </summary>
        /// <remarks>All entities in a chunk must have the same <see cref="Archetype"/>.</remarks>
        public EntityArchetype Archetype
        {
            get
            {
                return new EntityArchetype()
                {
                    Archetype = m_Chunk->Archetype,
                    #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    _DebugComponentStore =  m_EntityComponentStore
                    #endif
                };
            }
        }

        /// <summary>
        /// A special "null" ArchetypeChunk that you can use to test whether ArchetypeChunk instances are valid.
        /// </summary>
        /// <remarks>An ArchetypeChunk struct that refers to a chunk of memory that has been freed will be equal to
        /// this "null" ArchetypeChunk instance.</remarks>
        public static ArchetypeChunk Null => new ArchetypeChunk();

        /// <summary>
        /// Two ArchetypeChunk instances are equal if they reference the same block of chunk and entity component store memory.
        /// </summary>
        /// <param name="archetypeChunk">Another ArchetypeChunk instance</param>
        /// <returns>True, if both ArchetypeChunk instances reference the same memory or both contain null memory
        /// references.</returns>
        public bool Equals(ArchetypeChunk archetypeChunk)
        {
            return m_Chunk == archetypeChunk.m_Chunk && m_EntityComponentStore == archetypeChunk.m_EntityComponentStore;
        }

        /// <summary>
        /// The number of shared components in the archetype associated with this chunk.
        /// </summary>
        /// <returns>The shared component count.</returns>
        public int NumSharedComponents()
        {
            return m_Chunk->Archetype->NumSharedComponents;
        }

        /// <summary>
        /// Reports whether this ArchetypeChunk instance is invalid.
        /// </summary>
        /// <returns>True, if no <see cref="Archetype"/> is associated with the this ArchetypeChunk
        /// instance.</returns>
        public bool Invalid()
        {
            return m_Chunk->Archetype == null;
        }

        /// <summary>
        /// Reports whether this ArchetypeChunk is locked.
        /// </summary>
        /// <seealso cref="EntityManager.LockChunk(ArchetypeChunk"/>
        /// <seealso cref="EntityManager.UnlockChunk(ArchetypeChunk"/>
        /// <returns>True, if locked.</returns>

        [Obsolete("Locked has been deprecated, and is always false. (RemovedAfter 2020-06-05)")]
        public bool Locked()
        {
            return false;
        }

        /// <summary>
        /// Provides a native array interface to entity instances stored in this chunk.
        /// </summary>
        /// <remarks>The native array returned by this method references existing data, not a copy.</remarks>
        /// <param name="entityTypeHandle">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetEntityTypeHandle"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.</param>
        /// <returns>A native array containing the entities in the chunk.</returns>
        public NativeArray<Entity> GetNativeArray(EntityTypeHandle entityTypeHandle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(entityTypeHandle.m_Safety);
#endif
            var archetype = m_Chunk->Archetype;
            var buffer = m_Chunk->Buffer;
            var length = Count;
            var startOffset = archetype->Offsets[0] + m_BatchStartEntityIndex * archetype->SizeOfs[0];
            var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(buffer + startOffset, length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, entityTypeHandle.m_Safety);
#endif
            return result;
        }

        /// <summary>
        /// Reports whether the data in any of IComponentData components in the chunk, of the type identified by
        /// <paramref name="chunkComponentTypeHandle"/>, could have changed since the specified version.
        /// </summary>
        /// <remarks>
        /// When you access a component in a chunk with write privileges, the ECS framework updates the change version
        /// of that component type to the current <see cref="EntityManager.GlobalSystemVersion"/> value. Since every
        /// system stores the global system version in its <see cref="ComponentSystemBase.LastSystemVersion"/> field
        /// when it updates, you can compare these two versions with this function in order to determine whether
        /// the data of components in this chunk could have changed since the last time that system ran.
        ///
        /// Note that for efficiency, the change version applies to whole chunks not individual entities. The change
        /// version is updated even when another job or system that has declared write access to a component does
        /// not actually change the component value.</remarks>
        /// <param name="chunkComponentTypeHandle">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetComponentTypeHandle{T}"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.
        /// </param>
        /// <param name="version">The version to compare. In a system, this parameter should be set to the
        /// current <see cref="ComponentSystemBase.LastSystemVersion"/> at the time the job is run or
        /// scheduled.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <returns>True, if the version number stored in the chunk for this component is more recent than the version
        /// passed to the <paramref name="version"/> parameter.</returns>
        public bool DidChange<T>(ComponentTypeHandle<T> chunkComponentTypeHandle, uint version) where T :
#if UNITY_DISABLE_MANAGED_COMPONENTS
        struct,
#endif
        IComponentData
        {
            return ChangeVersionUtility.DidChange(GetChangeVersion(chunkComponentTypeHandle), version);
        }

        /// <summary>
        /// Reports whether the data in any of IComponentData components in the chunk, of the type identified by
        /// <paramref name="chunkComponentType"/>, could have changed since the specified version.
        /// </summary>
        /// <remarks>
        /// When you access a component in a chunk with write privileges, the ECS framework updates the change version
        /// of that component type to the current <see cref="EntityManager.GlobalSystemVersion"/> value. Since every
        /// system stores the global system version in its <see cref="ComponentSystemBase.LastSystemVersion"/> field
        /// when it updates, you can compare these two versions with this function in order to determine whether
        /// the data of components in this chunk could have changed since the last time that system ran.
        ///
        /// Note that for efficiency, the change version applies to whole chunks not individual entities. The change
        /// version is updated even when another job or system that has declared write access to a component does
        /// not actually change the component value.</remarks>
        /// <param name="chunkComponentType">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetDynamicComponentTypeHandle"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.
        /// </param>
        /// <param name="version">The version to compare. In a system, this parameter should be set to the
        /// current <see cref="ComponentSystemBase.LastSystemVersion"/> at the time the job is run or
        /// scheduled.</param>
        /// <typeparam name="T">The component type.</typeparam>
        /// <returns>True, if the version number stored in the chunk for this component is more recent than the version
        /// passed to the <paramref name="version"/> parameter.</returns>
        public bool DidChange(DynamicComponentTypeHandle chunkComponentType, uint version)
        {
            return ChangeVersionUtility.DidChange(GetChangeVersion(chunkComponentType), version);
        }

        /// <summary>
        /// Reports whether any of the data in dynamic buffer components in the chunk, of the type identified by
        /// <paramref name="chunkBufferTypeHandle"/>, could have changed since the specified version.
        /// </summary>
        /// <remarks>
        /// When you access a component in a chunk with write privileges, the ECS framework updates the change version
        /// of that component type to the current <see cref="EntityManager.GlobalSystemVersion"/> value. Since every
        /// system stores the global system version in its <see cref="ComponentSystemBase.LastSystemVersion"/> field
        /// when it updates, you can compare these two versions with this function in order to determine whether
        /// the data of components in this chunk could have changed since the last time that system ran.
        ///
        /// Note that for efficiency, the change version applies to whole chunks not individual entities. The change
        /// version is updated even when another job or system that has declared write access to a component does
        /// not actually change the component value.</remarks>
        /// <param name="chunkBufferTypeHandle">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetBufferTypeHandle{T}"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.</param>
        /// <param name="version">The version to compare. In a system, this parameter should be set to the
        /// current <see cref="ComponentSystemBase.LastSystemVersion"/> at the time the job is run or
        /// scheduled.</param>
        /// <typeparam name="T">The data type of the elements in the dynamic buffer.</typeparam>
        /// <returns>True, if the version number stored in the chunk for this component is more recent than the version
        /// passed to the <paramref name="version"/> parameter.</returns>
        public bool DidChange<T>(BufferTypeHandle<T> chunkBufferTypeHandle, uint version) where T : struct, IBufferElementData
        {
            return ChangeVersionUtility.DidChange(GetChangeVersion(chunkBufferTypeHandle), version);
        }

        /// <summary>
        /// Reports whether the value of shared components associated with the chunk, of the type identified by
        /// <paramref name="chunkSharedComponentData"/>, could have changed since the specified version.
        /// </summary>
        /// <remarks>
        /// Shared components behave differently than other types of components in terms of change versioning because
        /// changing the value of a shared component can move an entity to a different chunk. If the change results
        /// in an entity moving to a different chunk, then only the order version is updated (for both the original and
        /// the receiving chunk). If you change the shared component value for all entities in a chunk at once, the
        /// change version for that chunk is updated. The order version is unaffected.
        ///
        /// Note that for efficiency, the change version applies to whole chunks not individual entities. The change
        /// version is updated even when another job or system that has declared write access to a component does
        /// not actually change the component value.</remarks>
        /// <typeparam name="T">The data type of the shared component.</typeparam>
        /// <param name="chunkSharedComponentData">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetSharedComponentTypeHandle{T}"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.</param>
        /// <param name="version">The version to compare. In a system, this parameter should be set to the
        /// current <see cref="ComponentSystemBase.LastSystemVersion"/> at the time the job is run or
        /// scheduled.</param>
        /// <returns>True, if the version number stored in the chunk for this component is more recent than the version
        /// passed to the <paramref name="version"/></returns>
        public bool DidChange<T>(SharedComponentTypeHandle<T> chunkSharedComponentData, uint version) where T : struct, ISharedComponentData
        {
            return ChangeVersionUtility.DidChange(GetChangeVersion(chunkSharedComponentData), version);
        }

        [Obsolete("Use GetChangeVersion instead. GetComponentVersion will be (RemovedAfter 2020-05-19). (UnityUpgradable) -> GetChangeVersion(*)")]
        public uint GetComponentVersion<T>(ComponentTypeHandle<T> chunkComponentTypeHandle)
            where T : IComponentData
        {
            return GetChangeVersion(chunkComponentTypeHandle);
        }

        [Obsolete("Use GetChangeVersion instead. GetComponentVersion will be (RemovedAfter 2020-05-19). (UnityUpgradable) -> GetChangeVersion(*)")]
        public uint GetComponentVersion<T>(BufferTypeHandle<T> chunkBufferTypeHandle)
            where T : struct, IBufferElementData
        {
            return GetChangeVersion(chunkBufferTypeHandle);
        }

        [Obsolete("Use GetChangeVersion instead. GetComponentVersion will be (RemovedAfter 2020-05-19). (UnityUpgradable) -> GetChangeVersion(*)")]
        public uint GetComponentVersion<T>(SharedComponentTypeHandle<T> chunkSharedComponentData)
            where T : struct, ISharedComponentData
        {
            return GetChangeVersion(chunkSharedComponentData);
        }

        /// <summary>
        /// Gets the change version number assigned to the specified type of component in this chunk.
        /// </summary>
        /// <remarks>
        /// Every time a system accesses components in a chunk, the system updates the change version of any
        /// component types to which it has write access with the current
        /// <see cref="ComponentSystemBase.GlobalSystemVersion"/>. (A system updates the version whether or not you
        /// actually write any component data -- always specify read-only access when possible.)
        ///
        /// You can use the change version to filter out entities that have not changed since the last time a system ran.
        /// Implement change filtering using one of the following:
        ///
        /// - [Entities.ForEach.WithChangeFilter(ComponentType)](xref:Unity.Entities.SystemBase.Entities)
        /// - <see cref="EntityQuery.AddChangedVersionFilter(ComponentType)"/>
        /// - <see cref="ArchetypeChunk.DidChange{T}(ComponentTypeHandle{T}, uint)"/> in an <see cref="IJobChunk"/> job.
        ///
        /// Note that change versions are stored at the chunk level. Thus when you use change filtering, the query system
        /// excludes or includes whole chunks not individual entities.
        /// </remarks>
        /// <param name="chunkComponentTypeHandle">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetComponentTypeHandle{T}"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.</param>
        /// <typeparam name="T">The data type of component T.</typeparam>
        /// <returns>The current version number of the specified component, which is the version set the last time a system
        /// accessed a component of that type in this chunk with write privileges. Returns 0 if the chunk does not contain
        /// a component of the specified type.</returns>
        public uint GetChangeVersion<T>(ComponentTypeHandle<T> chunkComponentTypeHandle)
            where T : IComponentData
        {
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(m_Chunk->Archetype, chunkComponentTypeHandle.m_TypeIndex);
            if (typeIndexInArchetype == -1) return 0;
            return m_Chunk->GetChangeVersion(typeIndexInArchetype);
        }

        /// <summary>
        /// Gets the change version number assigned to the specified type of component in this chunk.
        /// </summary>
        /// <remarks>
        /// Every time a system accesses components in a chunk, the system updates the change version of any
        /// component types to which it has write access with the current
        /// <see cref="ComponentSystemBase.GlobalSystemVersion"/>. (A system updates the version whether or not you
        /// actually write any component data -- always specify read-only access when possible.)
        ///
        /// You can use the change version to filter out entities that have not changed since the last time a system ran.
        /// Implement change filtering using one of the following:
        ///
        /// - [Entities.ForEach.WithChangeFilter(ComponentType)](xref:Unity.Entities.SystemBase.Entities)
        /// - <see cref="EntityQuery.AddChangedVersionFilter(ComponentType)"/>
        /// - <see cref="ArchetypeChunk.DidChange{T}(ComponentTypeHandle{T}, uint)"/> in an <see cref="IJobChunk"/> job.
        ///
        /// Note that change versions are stored at the chunk level. Thus when you use change filtering, the query system
        /// excludes or includes whole chunks not individual entities.
        /// </remarks>
        /// <param name="chunkComponentType">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetDynamicComponentTypeHandle"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.</param>
        /// <returns>The current version number of the specified component, which is the version set the last time a system
        /// accessed a component of that type in this chunk with write privileges. Returns 0 if the chunk does not contain
        /// a component of the specified type.</returns>
        public uint GetChangeVersion(DynamicComponentTypeHandle chunkComponentType)
        {
            ChunkDataUtility.GetIndexInTypeArray(m_Chunk->Archetype, chunkComponentType.m_TypeIndex, ref chunkComponentType.m_TypeLookupCache);
            int typeIndexInArchetype = chunkComponentType.m_TypeLookupCache;
            if (typeIndexInArchetype == -1) return 0;
            return m_Chunk->GetChangeVersion(typeIndexInArchetype);
        }

        /// <summary>
        /// Gets the change version number assigned to the specified type of dynamic buffer component in this chunk.
        /// </summary>
        /// <remarks>
        /// Every time a system accesses components in a chunk, the system updates the change version of any
        /// component types to which it has write access with the current
        /// <see cref="ComponentSystemBase.GlobalSystemVersion"/>. (A system updates the version whether or not you
        /// actually write any component data -- always specify read-only access when possible.)
        ///
        /// You can use the change version to filter out entities that have not changed since the last time a system ran.
        /// Implement change filtering using one of the following:
        ///
        /// - [Entities.ForEach.WithChangeFilter(ComponentType)](xref:Unity.Entities.SystemBase.Entities)
        /// - <see cref="EntityQuery.AddChangedVersionFilter(ComponentType)"/>
        /// - <see cref="ArchetypeChunk.DidChange{T}(ComponentTypeHandle{T}, uint)"/> in an <see cref="IJobChunk"/> job.
        ///
        /// Note that change versions are stored at the chunk level. Thus if you use change filtering, the query system
        /// excludes or includes whole chunks not individual entities.
        /// </remarks>
        /// <param name="chunkBufferTypeHandle">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetBufferTypeHandle{T}"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.</param>
        /// <typeparam name="T">The data type of component T.</typeparam>
        /// <returns>The current version number of the specified dynamic buffer type, which is the version set the last time a system
        /// accessed a buffer component of that type in this chunk with write privileges. Returns 0 if the chunk does not contain
        /// a buffer component of the specified type.</returns>
        public uint GetChangeVersion<T>(BufferTypeHandle<T> chunkBufferTypeHandle)
            where T : struct, IBufferElementData
        {
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(m_Chunk->Archetype, chunkBufferTypeHandle.m_TypeIndex);
            if (typeIndexInArchetype == -1) return 0;
            return m_Chunk->GetChangeVersion(typeIndexInArchetype);
        }

        /// <summary>
        /// Gets the change version number assigned to the specified type of shared component in this chunk.
        /// </summary>
        /// <remarks>
        /// Shared components behave differently than other types of components in terms of change versioning because
        /// changing the value of a shared component can move an entity to a different chunk. If the change results
        /// in an entity moving to a different chunk, then only the order version is updated (for both the original and
        /// the receiving chunk). If you change the shared component value for all entities in a chunk at once,
        /// the entities remain in their current chunk. The change version for that chunk is updated and the order
        /// version is unaffected.
        /// </remarks>
        /// <param name="chunkSharedComponentData">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetSharedComponentTypeHandle{T}"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.</param>
        /// <typeparam name="T">The data type of shared component T.</typeparam>
        /// <returns>The current version number of the specified shared component, which is the version set the last time a system
        /// accessed a component of that type in this chunk with write privileges. Returns 0 if the chunk does not contain
        /// a shared component of the specified type.</returns>
        public uint GetChangeVersion<T>(SharedComponentTypeHandle<T> chunkSharedComponentData)
            where T : struct, ISharedComponentData
        {
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(m_Chunk->Archetype, chunkSharedComponentData.m_TypeIndex);
            if (typeIndexInArchetype == -1) return 0;
            return m_Chunk->GetChangeVersion(typeIndexInArchetype);
        }

        /// <summary>
        /// Reports whether a structural change has occured in this chunk since the specified version.
        /// </summary>
        /// <remarks>
        /// Typically, you set the <paramref name="version"/> parameter to the
        /// <see cref="ComponentSystemBase.LastSystemVersion"/> of a system to detect whether the order version
        /// has changed since the last time that system ran.
        /// </remarks>
        /// <param name="version">The version number to compare. </param>
        /// <returns>True, if the order version number has changed since the specified version.</returns>
        public bool DidOrderChange(uint version)
        {
            return ChangeVersionUtility.DidChange(GetOrderVersion(), version);
        }

        /// <summary>
        /// Gets the order version number assigned to this chunk.
        /// </summary>
        /// <remarks>
        /// Every time you perform a structural change affecting a chunk, the ECS framework updates the order
        /// version of the chunk to the current <see cref="ComponentSystemBase.GlobalSystemVersion"/> value.
        /// Structural changes include adding and removing entities, adding or removing the component of an
        /// entity, and changing the value of a shared component (except when you change the value for all entities
        /// in a chunk at the same time).
        /// </remarks>
        /// <returns>The current order version of this chunk.</returns>
        public uint GetOrderVersion()
        {
            return m_Chunk->GetOrderVersion();
        }

        /// <summary>
        /// Gets the value of a chunk component.
        /// </summary>
        /// <param name="chunkComponentTypeHandle">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetComponentTypeHandle{T}"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.</param>
        /// <typeparam name="T">The data type of the chunk component.</typeparam>
        /// <returns>A copy of the chunk component.</returns>
        public T GetChunkComponentData<T>(ComponentTypeHandle<T> chunkComponentTypeHandle)
            where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(chunkComponentTypeHandle.m_Safety);
#endif
            m_EntityComponentStore->AssertEntityHasComponent(m_Chunk->metaChunkEntity, chunkComponentTypeHandle.m_TypeIndex);
            var ptr = m_EntityComponentStore->GetComponentDataWithTypeRO(m_Chunk->metaChunkEntity, chunkComponentTypeHandle.m_TypeIndex);
            T value;
            UnsafeUtility.CopyPtrToStructure(ptr, out value);
            return value;
        }

        /// <summary>
        /// Sets the value of a chunk component.
        /// </summary>
        /// <param name="chunkComponentTypeHandle">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetComponentTypeHandle{T}"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.</param>
        /// <typeparam name="T">The data type of the chunk component.</typeparam>
        /// <param name="value">A struct of type T containing the new values for the chunk component.</param>
        public void SetChunkComponentData<T>(ComponentTypeHandle<T> chunkComponentTypeHandle, T value)
            where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(chunkComponentTypeHandle.m_Safety);
#endif
            m_EntityComponentStore->AssertEntityHasComponent(m_Chunk->metaChunkEntity, chunkComponentTypeHandle.m_TypeIndex);
            var ptr = m_EntityComponentStore->GetComponentDataWithTypeRW(m_Chunk->metaChunkEntity, chunkComponentTypeHandle.m_TypeIndex, m_EntityComponentStore->GlobalSystemVersion);
            UnsafeUtility.CopyStructureToPtr(ref value, ptr);
        }

        /// <summary>
        /// Gets the index into the array of unique values for the specified shared component.
        /// </summary>
        /// <remarks>
        /// Because shared components can contain managed types, you can only access the value index of a shared component
        /// inside a job, not the value itself. The index value indexes the array returned by
        /// <see cref="EntityManager.GetAllUniqueSharedComponentData{T}(List{T})"/>. If desired, you can create a native
        /// array that mirrors your unique value list, but which contains only unmanaged, blittable data and pass that
        /// into an <see cref="IJobChunk"/> job. The unique value list and a specific index is only valid until a
        /// structural change occurs.
        /// </remarks>
        /// <param name="chunkSharedComponentData">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetSharedComponentTypeHandle{T}"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.</param>
        /// <typeparam name="T">The data type of the shared component.</typeparam>
        /// <returns>The index value, or -1 if the chunk does not contain a shared component of the specified type.</returns>
        public int GetSharedComponentIndex<T>(SharedComponentTypeHandle<T> chunkSharedComponentData)
            where T : struct, ISharedComponentData
        {
            var archetype = m_Chunk->Archetype;
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(archetype, chunkSharedComponentData.m_TypeIndex);
            if (typeIndexInArchetype == -1) return -1;

            var chunkSharedComponentIndex = typeIndexInArchetype - archetype->FirstSharedComponent;
            var sharedComponentIndex = m_Chunk->GetSharedComponentValue(chunkSharedComponentIndex);
            return sharedComponentIndex;
        }

        /// <summary>
        /// Gets the current value of a shared component.
        /// </summary>
        /// <remarks>You cannot call this function inside a job.</remarks>
        /// <param name="chunkSharedComponentData">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetSharedComponentTypeHandle{T}"/> immediately
        /// before calling this function.</param>
        /// <param name="entityManager">An EntityManager instance.</param>
        /// <typeparam name="T">The data type of the shared component.</typeparam>
        /// <returns>The shared component value.</returns>
        public T GetSharedComponentData<T>(SharedComponentTypeHandle<T> chunkSharedComponentData, EntityManager entityManager)
            where T : struct, ISharedComponentData
        {
            return entityManager.GetSharedComponentData<T>(GetSharedComponentIndex(chunkSharedComponentData));
        }

        /// <summary>
        /// Reports whether this chunk contains the specified component type.
        /// </summary>
        /// <remarks>When an <see cref="EntityQuery"/> includes optional components (using
        /// <see cref="EntityQueryDesc.Any"/>), some chunks returned by the query may contain such components and some
        /// may not. Use this function to determine whether or not the current chunk contains one of these optional
        /// component types.</remarks>
        /// <param name="chunkComponentTypeHandle">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetComponentTypeHandle{T}"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.
        /// </param>
        /// <typeparam name="T">The data type of the component.</typeparam>
        /// <returns>True, if this chunk contains an array of the specified component type.</returns>
        public bool Has<T>(ComponentTypeHandle<T> chunkComponentTypeHandle)
            where T :
#if UNITY_DISABLE_MANAGED_COMPONENTS
        struct,
#endif
        IComponentData
        {
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(m_Chunk->Archetype, chunkComponentTypeHandle.m_TypeIndex);
            return (typeIndexInArchetype != -1);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="chunkComponentType"></param>
        /// <returns></returns>
        public bool Has(DynamicComponentTypeHandle chunkComponentType)
        {
            ChunkDataUtility.GetIndexInTypeArray(m_Chunk->Archetype, chunkComponentType.m_TypeIndex, ref chunkComponentType.m_TypeLookupCache);
            return (chunkComponentType.m_TypeLookupCache != -1);
        }

        /// <summary>
        /// Reports whether this chunk contains a chunk component of the specified component type.
        /// </summary>
        /// <remarks>When an <see cref="EntityQuery"/> includes optional components used as chunk
        /// components (with <see cref="EntityQueryDesc.Any"/>), some chunks returned by the query may have these chunk
        /// components and some may not. Use this function to determine whether or not the current chunk contains one of
        /// these optional component types as a chunk component.</remarks>
        /// <param name="chunkComponentTypeHandle">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetComponentTypeHandle{T}"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.
        /// </param>
        /// <typeparam name="T">The data type of the chunk component.</typeparam>
        /// <returns>True, if this chunk contains a chunk component of the specified type.</returns>
        public bool HasChunkComponent<T>(ComponentTypeHandle<T> chunkComponentTypeHandle)
            where T : struct, IComponentData
        {
            var metaChunkArchetype = m_Chunk->Archetype->MetaChunkArchetype;
            if (metaChunkArchetype == null)
                return false;
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(m_Chunk->Archetype->MetaChunkArchetype, chunkComponentTypeHandle.m_TypeIndex);
            return (typeIndexInArchetype != -1);
        }

        /// <summary>
        /// Reports whether this chunk contains a shared component of the specified component type.
        /// </summary>
        /// <remarks>When an <see cref="EntityQuery"/> includes optional components used as shared
        /// components (with <see cref="EntityQueryDesc.Any"/>), some chunks returned by the query may have these shared
        /// components and some may not. Use this function to determine whether or not the current chunk contains one of
        /// these optional component types as a shared component.</remarks>
        /// <param name="chunkComponentTypeHandle">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetSharedComponentTypeHandle{T}"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.
        /// </param>
        /// <typeparam name="T">The data type of the shared component.</typeparam>
        /// <returns>True, if this chunk contains a shared component of the specified type.</returns>
        public bool Has<T>(SharedComponentTypeHandle<T> chunkComponentTypeHandle)
            where T : struct, ISharedComponentData
        {
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(m_Chunk->Archetype, chunkComponentTypeHandle.m_TypeIndex);
            return (typeIndexInArchetype != -1);
        }

        /// <summary>
        /// Reports whether this chunk contains a dynamic buffer containing the specified component type.
        /// </summary>
        /// <remarks>When an <see cref="EntityQuery"/> includes optional dynamic buffer types
        /// (with <see cref="EntityQueryDesc.Any"/>), some chunks returned by the query may have these dynamic buffers
        /// components and some may not. Use this function to determine whether or not the current chunk contains one of
        /// these optional dynamic buffers.</remarks>
        /// <param name="chunkBufferTypeHandle">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetBufferTypeHandle{T}"/> immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.</param>
        /// <typeparam name="T">The data type of the component stored in the dynamic buffer.</typeparam>
        /// <returns>True, if this chunk contains an array of the dynamic buffers containing the specified component type.</returns>
        public bool Has<T>(BufferTypeHandle<T> chunkBufferTypeHandle)
            where T : struct, IBufferElementData
        {
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(m_Chunk->Archetype, chunkBufferTypeHandle.m_TypeIndex);
            return (typeIndexInArchetype != -1);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckZeroSizedComponentData<T>(ComponentTypeHandle<T> chunkComponentType)
        {
            if (chunkComponentType.m_IsZeroSized)
                throw new ArgumentException($"ArchetypeChunk.GetNativeArray<{typeof(T)}> cannot be called on zero-sized IComponentData");
        }

        /// <summary>
        /// Provides a native array interface to components stored in this chunk.
        /// </summary>
        /// <remarks>The native array returned by this method references existing data, not a copy.</remarks>
        /// <param name="chunkComponentTypeHandle">An object containing type and job safety information. Create this
        /// object by calling <see cref="ComponentSystemBase.GetComponentTypeHandle{T}(bool)"/>immediately
        /// before scheduling a job. Pass the object to a job using a public field you define as part of the job struct.</param>
        /// <typeparam name="T">The data type of the component.</typeparam>
        /// <exception cref="ArgumentException">If you call this function on a "tag" component type (which is an empty
        /// component with no fields).</exception>
        /// <returns>A native array containing the components in the chunk.</returns>
        public NativeArray<T> GetNativeArray<T>(ComponentTypeHandle<T> chunkComponentTypeHandle)
            where T : struct, IComponentData
        {
            CheckZeroSizedComponentData(chunkComponentTypeHandle);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(chunkComponentTypeHandle.m_Safety);
#endif
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(m_Chunk->Archetype, chunkComponentTypeHandle.m_TypeIndex);
            if (typeIndexInArchetype == -1)
            {
                var emptyResult =
                    NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(null, 0, 0);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref emptyResult, chunkComponentTypeHandle.m_Safety);
#endif
                return emptyResult;
            }

            byte* ptr = (chunkComponentTypeHandle.IsReadOnly)
                ? ChunkDataUtility.GetComponentDataRO(m_Chunk, 0, typeIndexInArchetype)
                : ChunkDataUtility.GetComponentDataRW(m_Chunk, 0, typeIndexInArchetype, chunkComponentTypeHandle.GlobalSystemVersion);
            var archetype = m_Chunk->Archetype;
            var length = Count;
            var batchStartOffset = m_BatchStartEntityIndex * archetype->SizeOfs[typeIndexInArchetype];
            var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(ptr + batchStartOffset, length, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, chunkComponentTypeHandle.m_Safety);
#endif
            return result;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckZeroSizedGetDynamicComponentDataArrayReinterpret<T>(DynamicComponentTypeHandle chunkComponentType)
        {
            if (chunkComponentType.m_IsZeroSized)
                throw new ArgumentException($"ArchetypeChunk.GetDynamicComponentDataArrayReinterpret<{typeof(T)}> cannot be called on zero-sized IComponentData");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckComponentSizeMatches(DynamicComponentTypeHandle chunkComponentType, int typeSize, int expectedTypeSize)
        {
            if (typeSize != expectedTypeSize)
                throw new InvalidOperationException($"Dynamic chunk component type {TypeManager.GetType(chunkComponentType.m_TypeIndex)} (size = {typeSize}) size does not equal {expectedTypeSize}. Component size must match with expectedTypeSize.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckCannotBeAliasedDueToSizeConstraints<T>(DynamicComponentTypeHandle chunkComponentType, int outTypeSize, int outLength, int byteLen, int length)
        {
            if (outTypeSize * outLength != byteLen)
            {
                throw new InvalidOperationException($"Dynamic chunk component type {TypeManager.GetType(chunkComponentType.m_TypeIndex)} (array length {length}) and {typeof(T)} cannot be aliased due to size constraints. The size of the types and lengths involved must line up.");
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="chunkComponentType"></param>
        /// <param name="expectedTypeSize"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public NativeArray<T> GetDynamicComponentDataArrayReinterpret<T>(DynamicComponentTypeHandle chunkComponentType, int expectedTypeSize)
            where T : struct
        {
            CheckZeroSizedGetDynamicComponentDataArrayReinterpret<T>(chunkComponentType);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(chunkComponentType.m_Safety);
#endif
            var archetype = m_Chunk->Archetype;
            ChunkDataUtility.GetIndexInTypeArray(m_Chunk->Archetype, chunkComponentType.m_TypeIndex, ref chunkComponentType.m_TypeLookupCache);
            var typeIndexInArchetype = chunkComponentType.m_TypeLookupCache;
            if (typeIndexInArchetype == -1)
            {
                var emptyResult =
                    NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(null, 0, 0);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref emptyResult, chunkComponentType.m_Safety);
#endif
                return emptyResult;
            }

            var typeSize = archetype->SizeOfs[typeIndexInArchetype];
            var length = Count;
            var byteLen = length * typeSize;
            var outTypeSize = UnsafeUtility.SizeOf<T>();
            var outLength = byteLen / outTypeSize;

            CheckComponentSizeMatches(chunkComponentType, typeSize, expectedTypeSize);
            CheckCannotBeAliasedDueToSizeConstraints<T>(chunkComponentType, outTypeSize, outLength, byteLen, length);

            byte* ptr = (chunkComponentType.IsReadOnly)
                ? ChunkDataUtility.GetComponentDataRO(m_Chunk, 0, typeIndexInArchetype)
                : ChunkDataUtility.GetComponentDataRW(m_Chunk, 0, typeIndexInArchetype, chunkComponentType.GlobalSystemVersion);

            var batchStartOffset = m_BatchStartEntityIndex * archetype->SizeOfs[typeIndexInArchetype];
            var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(ptr + batchStartOffset, outLength, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, chunkComponentType.m_Safety);
#endif
            return result;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="componentTypeHandle"></param>
        /// <param name="manager"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public ManagedComponentAccessor<T> GetManagedComponentAccessor<T>(ComponentTypeHandle<T> componentTypeHandle, EntityManager manager)
            where T : class
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(componentTypeHandle.m_Safety);
#endif
            var archetype = m_Chunk->Archetype;
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(m_Chunk->Archetype, componentTypeHandle.m_TypeIndex);

            NativeArray<int> indexArray;
            if (typeIndexInArchetype == -1)
            {
                indexArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(null, 0, 0);
            }
            else
            {

                byte* ptr = ChunkDataUtility.GetComponentDataRW(m_Chunk, 0, typeIndexInArchetype, componentTypeHandle.GlobalSystemVersion);
                var length = Count;
                var batchStartOffset = m_BatchStartEntityIndex * archetype->SizeOfs[typeIndexInArchetype];
                indexArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(ptr + batchStartOffset, length, Allocator.None);
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref indexArray, componentTypeHandle.m_Safety);
#endif

            return new ManagedComponentAccessor<T>(indexArray, manager);
        }

#if UNITY_SKIP_UPDATES_WITH_VALIDATION_SUITE
        [Obsolete("GetComponentObjects has been renamed to GetManagedComponentAccessor. (RemovedAfter 2020-08-01). -- please remove the UNITY_SKIP_UPDATES_WITH_VALIDATION_SUITE define in the Unity.Entities assembly definition file if this message is unexpected and you want to attempt an automatic upgrade.", false)]
#else
        [Obsolete("GetComponentObjects has been renamed to GetManagedComponentAccessor. (RemovedAfter 2020-08-01). (UnityUpgradable) -> GetManagedComponentAccessor<T>(*)", false)]
#endif
        public ArchetypeChunkComponentObjects<T> GetComponentObjects<T>(ArchetypeChunkComponentType<T> componentTypeHandle, EntityManager manager)
            where T : class
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(componentTypeHandle.m_Safety);
#endif
            var archetype = m_Chunk->Archetype;
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(m_Chunk->Archetype, componentTypeHandle.m_TypeIndex);

            NativeArray<int> indexArray;
            if (typeIndexInArchetype == -1)
            {
                indexArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(null, 0, 0);
            }
            else
            {

                byte* ptr = ChunkDataUtility.GetComponentDataRW(m_Chunk, 0, typeIndexInArchetype, componentTypeHandle.GlobalSystemVersion);
                var length = Count;
                var batchStartOffset = m_BatchStartEntityIndex * archetype->SizeOfs[typeIndexInArchetype];
                indexArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(ptr + batchStartOffset, length, Allocator.None);
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref indexArray, componentTypeHandle.m_Safety);
#endif

            return new ArchetypeChunkComponentObjects<T>(indexArray, manager);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="bufferComponentTypeHandle"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public BufferAccessor<T> GetBufferAccessor<T>(BufferTypeHandle<T> bufferComponentTypeHandle)
            where T : struct, IBufferElementData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(bufferComponentTypeHandle.m_Safety0);
#endif
            var archetype = m_Chunk->Archetype;
            var typeIndex = bufferComponentTypeHandle.m_TypeIndex;
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);
            if (typeIndexInArchetype == -1)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                return new BufferAccessor<T>(null, 0, 0, true, bufferComponentTypeHandle.m_Safety0, bufferComponentTypeHandle.m_Safety1, 0);
#else
                return new BufferAccessor<T>(null, 0, 0, 0);
#endif
            }

            int internalCapacity = archetype->BufferCapacities[typeIndexInArchetype];

            byte* ptr = (bufferComponentTypeHandle.IsReadOnly)
                ? ChunkDataUtility.GetComponentDataRO(m_Chunk, 0, typeIndexInArchetype)
                : ChunkDataUtility.GetComponentDataRW(m_Chunk, 0, typeIndexInArchetype, bufferComponentTypeHandle.GlobalSystemVersion);

            var length = Count;
            int stride = archetype->SizeOfs[typeIndexInArchetype];
            var batchStartOffset = m_BatchStartEntityIndex * stride;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            return new BufferAccessor<T>(ptr + batchStartOffset, length, stride, bufferComponentTypeHandle.IsReadOnly, bufferComponentTypeHandle.m_Safety0, bufferComponentTypeHandle.m_Safety1, internalCapacity);
#else
            return new BufferAccessor<T>(ptr + batchStartOffset, length, stride, internalCapacity);
#endif
        }

#if ENABLE_DOTS_COMPILER_CHUNKS
        public ChunkEntitiesDescription Entities => throw new ArgumentException("Using chunk.Entities is only possible inside a entityQuery.Chunks.ForEach() lambda job.");
#endif
    }

    /// <summary>
    ///
    /// </summary>
    public struct ChunkEntitiesDescription : ISupportForEachWithUniversalDelegate
    {
    }

    /// <summary>
    ///
    /// </summary>
    [ChunkSerializable]
    public struct ChunkHeader : ISystemStateComponentData
    {
        public ArchetypeChunk ArchetypeChunk;

        public static unsafe ChunkHeader Null
        {
            get
            {
                return new ChunkHeader {ArchetypeChunk = new ArchetypeChunk(null, null)};
            }
        }
    }

    /// <summary>
    ///
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [NativeContainer]
    public unsafe struct BufferAccessor<T>
        where T : struct, IBufferElementData
    {
        [NativeDisableUnsafePtrRestriction]
        private byte* m_BasePointer;
        private int m_Length;
        private int m_Stride;
        private int m_InternalCapacity;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private bool m_IsReadOnly;
#endif

        public int Length => m_Length;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety0;
        private AtomicSafetyHandle m_ArrayInvalidationSafety;

#pragma warning disable 0414 // assigned but its value is never used
        private int m_SafetyReadOnlyCount;
        private int m_SafetyReadWriteCount;
#pragma warning restore 0414

#endif

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        /// <summary>
        ///
        /// </summary>
        /// <param name="basePointer"></param>
        /// <param name="length"></param>
        /// <param name="stride"></param>
        /// <param name="readOnly"></param>
        /// <param name="safety"></param>
        /// <param name="arrayInvalidationSafety"></param>
        /// <param name="internalCapacity"></param>
        public BufferAccessor(byte* basePointer, int length, int stride, bool readOnly, AtomicSafetyHandle safety, AtomicSafetyHandle arrayInvalidationSafety, int internalCapacity)
        {
            m_BasePointer = basePointer;
            m_Length = length;
            m_Stride = stride;
            m_Safety0 = safety;
            m_ArrayInvalidationSafety = arrayInvalidationSafety;
            m_IsReadOnly = readOnly;
            m_SafetyReadOnlyCount = readOnly ? 2 : 0;
            m_SafetyReadWriteCount = readOnly ? 0 : 2;
            m_InternalCapacity = internalCapacity;
        }

#else
        public BufferAccessor(byte* basePointer, int length, int stride, int internalCapacity)
        {
            m_BasePointer = basePointer;
            m_Length = length;
            m_Stride = stride;
            m_InternalCapacity = internalCapacity;
        }

#endif

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void AssertIndexInRange(int index)
        {
            if (index < 0 || index >= Length)
                throw new InvalidOperationException($"index {index} out of range in LowLevelBufferAccessor of length {Length}");
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="index"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public DynamicBuffer<T> this[int index]
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety0);
#endif
                AssertIndexInRange(index);
                BufferHeader* hdr = (BufferHeader*)(m_BasePointer + index * m_Stride);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                return new DynamicBuffer<T>(hdr, m_Safety0, m_ArrayInvalidationSafety, m_IsReadOnly, false, 0, m_InternalCapacity);
#else
                return new DynamicBuffer<T>(hdr, m_InternalCapacity);
#endif
            }
        }
    }

    /// <summary>
    ///
    /// </summary>
    public unsafe struct ArchetypeChunkArray
    {
        /// <summary>
        ///
        /// </summary>
        /// <param name="chunks"></param>
        /// <returns></returns>
        static public int CalculateEntityCount(NativeArray<ArchetypeChunk> chunks)
        {
            int entityCount = 0;
            for (var i = 0; i < chunks.Length; i++)
            {
                entityCount += chunks[i].Count;
            }

            return entityCount;
        }
    }

    /// <summary>
    ///
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct ComponentTypeHandle<T>
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
        internal ComponentTypeHandle(AtomicSafetyHandle safety, bool isReadOnly, uint globalSystemVersion)
#else
        internal ComponentTypeHandle(bool isReadOnly, uint globalSystemVersion)
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

    /// <summary>
    ///
    /// </summary>
    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct DynamicComponentTypeHandle
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
        internal DynamicComponentTypeHandle(ComponentType componentType, AtomicSafetyHandle safety, uint globalSystemVersion)
#else
        internal DynamicComponentTypeHandle(ComponentType componentType, uint globalSystemVersion)
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

    /// <summary>
    ///
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct BufferTypeHandle<T>
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
        internal BufferTypeHandle(AtomicSafetyHandle safety, AtomicSafetyHandle arrayInvalidationSafety, bool isReadOnly, uint globalSystemVersion)
#else
        internal BufferTypeHandle(bool isReadOnly, uint globalSystemVersion)
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

    /// <summary>
    ///
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct SharedComponentTypeHandle<T>
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
        internal SharedComponentTypeHandle(AtomicSafetyHandle safety)
#else
        internal unsafe SharedComponentTypeHandle(bool unused)
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

    /// <summary>
    ///
    /// </summary>
    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public struct EntityTypeHandle
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
        internal EntityTypeHandle(AtomicSafetyHandle safety)
#else
        internal unsafe EntityTypeHandle(bool unused)
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

    /// <summary>
    ///
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ManagedComponentAccessor<T>
        where T : class
    {
        /// <summary>
        ///
        /// </summary>
        NativeArray<int> m_IndexArray;
        EntityComponentStore* m_EntityComponentStore;
        ManagedComponentStore m_ManagedComponentStore;

        unsafe internal ManagedComponentAccessor(NativeArray<int> indexArray, EntityManager entityManager)
        {
            var access = entityManager.GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var mcs = access->ManagedComponentStore;

            m_IndexArray = indexArray;
            m_EntityComponentStore = ecs;
            m_ManagedComponentStore = mcs;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="index"></param>
        /// <exception cref="IndexOutOfRangeException"></exception>
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
