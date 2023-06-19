using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
using System.Linq;
#endif

namespace Unity.Entities
{
    [BurstCompile]
    internal unsafe partial struct EntityComponentStore
    {
        const string k_JournalingDisabledMsg = "Entities Journaling may be able to help determine more information. Please enable Entities Journaling for a more helpful error message.";

        // ----------------------------------------------------------------------------------------------------------
        // PUBLIC
        // ----------------------------------------------------------------------------------------------------------

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void CheckInternalConsistency(object[] managedComponentData)
        {
            Assert.IsTrue(ManagedChangesTracker.Empty);
            var managedComponentIndices = new UnsafeBitArray(m_ManagedComponentIndex, Allocator.Temp);
            var usedSequenceNumbers = new UnsafeParallelHashSet<ulong>(512, Allocator.Temp);

            Assert.IsTrue(managedComponentData.Length >= m_ManagedComponentIndex);
            for (int i = m_ManagedComponentIndex; i < managedComponentData.Length; ++i)
                Assert.IsNull(managedComponentData[i]);

            EntityComponentStore* selfPtr;
            fixed(EntityComponentStore* self = &this) { selfPtr = self; }  // This is safe - we're allocated on the native heap

            // Iterate by archetype
            var entityCountByArchetype = 0;
            for (var i = 0; i < m_Archetypes.Length; ++i)
            {
                var archetype = m_Archetypes.Ptr[i];
                int managedTypeBegin = archetype->FirstManagedComponent;
                int managedTypeEnd = archetype->ManagedComponentsEnd;
                Assert.AreEqual((IntPtr)selfPtr, (IntPtr)archetype->EntityComponentStore);

                for (int indexInArchetype = 0; indexInArchetype < archetype->TypesCount; ++indexInArchetype)
                {
                    int memoryOrderIndexInArchetype = archetype->TypeIndexInArchetypeToMemoryOrderIndex[indexInArchetype];
                    Assert.AreEqual(indexInArchetype, archetype->TypeMemoryOrderIndexToIndexInArchetype[memoryOrderIndexInArchetype]);
                }

                var countInArchetype = 0;
                for (var j = 0; j < archetype->Chunks.Count; ++j)
                {
                    var chunk = archetype->Chunks[j];
                    Assert.IsTrue(chunk->Archetype == archetype);
                    Assert.IsTrue(chunk->Capacity >= chunk->Count);
                    Assert.AreEqual(chunk->Count, archetype->Chunks.GetChunkEntityCount(j));
                    Assert.AreNotEqual(0, chunk->Count);

                    var chunkEntities = (Entity*)chunk->Buffer;
                    AssertEntitiesExist(chunkEntities, chunk->Count);

                    if (chunk->SequenceNumber == 0)
                        throw new ArgumentException("Sequence number must not be 0");
                    if (!usedSequenceNumbers.Add(chunk->SequenceNumber))
                        throw new ArgumentException("Sequence number must be unique");

                    if (chunk->Count < chunk->Capacity)
                    {
                        if (archetype->NumSharedComponents == 0)
                        {
                            Assert.IsTrue(chunk->ListWithEmptySlotsIndex >= 0 && chunk->ListWithEmptySlotsIndex < archetype->ChunksWithEmptySlots.Length);
                            Assert.IsTrue(chunk == archetype->ChunksWithEmptySlots.Ptr[chunk->ListWithEmptySlotsIndex]);
                        }
                        else
                        {
                            Assert.IsTrue(archetype->FreeChunksBySharedComponents.Contains(chunk));
                        }
                    }

                    countInArchetype += chunk->Count;

                    if (chunk->Archetype->HasChunkHeader) // Chunk entities with chunk components are not supported
                    {
                        Assert.IsFalse(chunk->Archetype->HasChunkComponents);
                    }

                    Assert.AreEqual(chunk->Archetype->HasChunkComponents, chunk->metaChunkEntity != Entity.Null);
                    if (chunk->metaChunkEntity != Entity.Null)
                    {
                        var chunkHeaderTypeIndex = TypeManager.GetTypeIndex<ChunkHeader>();
                        AssertEntitiesExist(&chunk->metaChunkEntity, 1);
                        AssertEntityHasComponent(chunk->metaChunkEntity, chunkHeaderTypeIndex);
                        var chunkHeader =
                            *(ChunkHeader*)GetComponentDataWithTypeRO(chunk->metaChunkEntity,
                                chunkHeaderTypeIndex);
                        Assert.IsTrue(chunk == chunkHeader.ArchetypeChunk.m_Chunk);
                        var metaChunk = GetChunk(chunk->metaChunkEntity);
                        Assert.IsTrue(metaChunk->Archetype == chunk->Archetype->MetaChunkArchetype);
                        Assert.IsTrue(chunkHeader.ArchetypeChunk.m_EntityComponentStore == selfPtr);
                    }

                    for (int iType = managedTypeBegin; iType < managedTypeEnd; ++iType)
                    {
                        var managedIndicesInChunk = (int*)(chunk->Buffer + archetype->Offsets[iType]);
                        for (int ie = 0; ie < chunk->Count; ++ie)
                        {
                            var index = managedIndicesInChunk[ie];
                            if (index == 0)
                                continue;

                            Assert.AreEqual(managedComponentData[index].GetType(), TypeManager.GetType(archetype->Types[iType].TypeIndex));
                            Assert.IsTrue(index < m_ManagedComponentIndex, "Managed component index in chunk is out of range.");
                            Assert.IsFalse(managedComponentIndices.IsSet(index), "Managed component index is used multiple times.");
                            managedComponentIndices.Set(index, true);
                        }
                    }
                }

                Assert.AreEqual(countInArchetype, archetype->EntityCount);

                AssertPaddingBitsAreZeroForArchetype(archetype);
                AssertEnabledBitsHierarchyIsCorrectForArchetype(archetype);

                entityCountByArchetype += countInArchetype;
            }

            usedSequenceNumbers.Dispose();

            for (int i = 0; i < m_ManagedComponentIndex; ++i)
                Assert.AreEqual(managedComponentData[i] != null, managedComponentIndices.IsSet(i));

            var freeManagedIndices = (int*)m_ManagedComponentFreeIndex.Ptr;
            var freeManagedCount = m_ManagedComponentFreeIndex.Length / sizeof(int);

            for (int i = 0; i < freeManagedCount; ++i)
            {
                var index = freeManagedIndices[i];
                Assert.IsTrue(0 < index && index < m_ManagedComponentIndex, "Managed component index in free list is out of range.");
                Assert.IsFalse(managedComponentIndices.IsSet(index), "Managed component was marked as free but is used in chunk.");
                managedComponentIndices.Set(index, true);
            }

            Assert.IsTrue(m_ManagedComponentIndex - 1 == 0 || managedComponentIndices.TestAll(1, m_ManagedComponentIndex - 1), "Managed component index has leaked.");
            managedComponentIndices.Dispose();

            // Iterate by free list
            Assert.IsTrue(m_EntityInChunkByEntity[m_NextFreeEntityIndex].Chunk == null);

            var entityCountByFreeList = EntitiesCapacity;
            int freeIndex = m_NextFreeEntityIndex;
            while (freeIndex != -1)
            {
                Assert.IsTrue(m_EntityInChunkByEntity[freeIndex].Chunk == null);
                Assert.IsTrue(freeIndex < EntitiesCapacity);

                freeIndex = m_EntityInChunkByEntity[freeIndex].IndexInChunk;

                entityCountByFreeList--;
            }

            // iterate by entities
            var entityCountByEntities = 0;
            var entityType = TypeManager.GetTypeIndex<Entity>();
            for (var i = 0; i != EntitiesCapacity; i++)
            {
                var chunk = m_EntityInChunkByEntity[i].Chunk;
                if (chunk == null)
                    continue;

                entityCountByEntities++;
                var archetype = m_ArchetypeByEntity[i];
                Assert.AreEqual((IntPtr)archetype, (IntPtr)chunk->Archetype);
                Assert.AreEqual(entityType, archetype->Types[0].TypeIndex);
                Assert.IsTrue(m_EntityInChunkByEntity[i].IndexInChunk < m_EntityInChunkByEntity[i].Chunk->Count);
                var entity = *(Entity*)ChunkDataUtility.GetComponentDataRO(m_EntityInChunkByEntity[i].Chunk,
                    m_EntityInChunkByEntity[i].IndexInChunk, 0);
                Assert.AreEqual(i, entity.Index);
                Assert.AreEqual(m_VersionByEntity[i], entity.Version);

                Assert.IsTrue(Exists(entity));
            }


            Assert.AreEqual(entityCountByEntities, entityCountByArchetype);

            // Enabling this fails SerializeEntitiesWorksWithBlobAssetReferences.
            // There is some special entity 0 usage in the serialization code.

            // @TODO: Review with simon looks like a potential leak?
            //Assert.AreEqual(entityCountByEntities, entityCountByFreeList);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public static void AssertAllEntitiesCopied(EntityComponentStore* lhs, EntityComponentStore* rhs)
        {
            Assert.IsTrue(rhs->EntitiesCapacity >= lhs->EntitiesCapacity);
            var rhsEntities = rhs->m_EntityInChunkByEntity;
            var lhsEntities = lhs->m_EntityInChunkByEntity;

            int capacity = lhs->EntitiesCapacity;
            for (int i = 0; i != capacity; i++)
            {
                if (lhsEntities[i].Chunk == null && rhsEntities[i].Chunk == null)
                    continue;

                if (lhsEntities[i].IndexInChunk != rhsEntities[i].IndexInChunk)
                    Assert.AreEqual(lhsEntities[i].IndexInChunk, rhsEntities[i].IndexInChunk);
            }
            Assert.AreEqual(lhs->m_NextFreeEntityIndex, rhs->m_NextFreeEntityIndex);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void ValidateEntity(Entity entity)
        {
            if (Hint.Unlikely(entity.Index < 0))
                throw new ArgumentException(
                    $"All entities created using EntityCommandBuffer.CreateEntity must be realized via playback(). One of the entities is still deferred (Index: {entity.Index}).");
            if (Hint.Unlikely((uint)entity.Index >= (uint)EntitiesCapacity))
                throw new ArgumentException(
                    "An Entity index is larger than the capacity of the EntityManager. This means the entity was created by a different world or the entity.Index got corrupted or incorrectly assigned and it may not be used on this EntityManager.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        void AssertArchetypeComponents(ComponentTypeInArchetype* types, int count)
        {
            if (count < 1)
                throw new ArgumentException($"Invalid component count");

            // NOTE: LookUpCache / ComponentLookup uses short for the IndexInArchetype cache
            if (count >= short.MaxValue)
                throw new ArgumentException($"Archetypes can have a maximum of {short.MaxValue} components.");

            if (types[0].TypeIndex == TypeIndex.Null)
                throw new ArgumentException($"Component type may not be null");
            if (types[0].TypeIndex != m_EntityType)
                throw new ArgumentException($"The Entity ID must always be the first component");

            for (var i = 1; i < count; i++)
            {
                if (types[i - 1].TypeIndex == types[i].TypeIndex)
                    throw new ArgumentException(
                        $"It is not allowed to have two components of the same type on the same entity. ({types[i - 1]} and {types[i]})");

                var SizeInChunk = GetTypeInfo(types[i].TypeIndex).SizeInChunk;
                if (SizeInChunk > ushort.MaxValue)
                    throw new ArgumentException($"IComponentData {types[i]} is too large. SizeOf may not be larger than {ushort.MaxValue}");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanCreateArchetype(ComponentType* componentTypes, int componentTypeCount)
        {
            var entityTypeInfo = GetTypeInfo(m_EntityType);
            var archetypeInstanceSize = GetComponentArraySize(entityTypeInfo.SizeInChunk, 1);
            for (int i = 0; i < componentTypeCount; i++)
            {
                var componentTypeInfo = GetTypeInfo(componentTypes[i].TypeIndex);
                var componentInstanceSize = GetComponentArraySize(componentTypeInfo.SizeInChunk, 1);
                archetypeInstanceSize += componentInstanceSize;
            }
            var chunkDataSize = Chunk.GetChunkBufferSize();
            if (archetypeInstanceSize > chunkDataSize)
                throw new ArgumentException($"Archetype too large to fit in chunk. Instance size {archetypeInstanceSize} bytes.  Maximum chunk size {chunkDataSize}. Components in new archetype: {AggregateNewArchetypesComponentTypes(componentTypes, componentTypeCount)}");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertEntitiesExist(Entity* entities, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var entity = entities + i;

                ValidateEntity(*entity);

                int index = entity->Index;
                var exists = m_VersionByEntity[index] == entity->Version &&
                    m_EntityInChunkByEntity[index].Chunk != null;
                if (!exists)
                    throw new ArgumentException(
                        "All entities passed to EntityManager must exist. One of the entities has already been destroyed or was never created."  + AppendDestroyedEntityRecordError(*entity));
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertValidEntities(Entity* entities, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var entity = entities + i;

                ValidateEntity(*entity);
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertEntityHasComponent(Entity entity, ComponentType componentType)
        {
            if (HasComponent(entity, componentType))
                return;

            if (!Exists(entity))
                throw new ArgumentException("The entity does not exist." + AppendDestroyedEntityRecordError(entity));

            throw new ArgumentException($"A component with type:{componentType} has not been added to the entity." + AppendRemovedComponentRecordError(entity, componentType));
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertEntityHasComponent(NativeArray<Entity> entities, ComponentType componentType)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (HasComponent(entity, componentType))
                    continue;

                if (!Exists(entity))
                    throw new ArgumentException("The entity does not exist."  + AppendDestroyedEntityRecordError(entity));

                throw new ArgumentException($"A component with type:{componentType} has not been added to the entity." + AppendRemovedComponentRecordError(entity, componentType));
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertEntityHasComponent(Entity entity, TypeIndex componentTypeIndex)
        {
            AssertEntityHasComponent(entity, ComponentType.FromTypeIndex(componentTypeIndex));
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertEntityHasComponent(Entity entity, TypeIndex componentTypeIndex, ref LookupCache typeLookupCache)
        {
            if (Hint.Likely(HasComponent(entity, componentTypeIndex, ref typeLookupCache)))
                return;

            if (Hint.Unlikely(!Exists(entity)))
                throw new ArgumentException("The entity does not exist." + AppendDestroyedEntityRecordError(entity));

            throw new ArgumentException($"A component with type:{componentTypeIndex} has not been added to the entity." + AppendRemovedComponentRecordError(entity, ComponentType.FromTypeIndex(componentTypeIndex)));
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertNonEmptyArchetypesHaveComponent(UnsafeMatchingArchetypePtrList archetypeList, ComponentType componentType)
        {
            int archetypeCount = archetypeList.Length;
            var ptrs = archetypeList.Ptr;
            TypeIndex typeIndex = componentType.TypeIndex;
            for (int i = 0; i < archetypeCount; i++)
            {
                if (Hint.Unlikely(ptrs[i]->Archetype->EntityCount > 0 && ChunkDataUtility.GetIndexInTypeArray(ptrs[i]->Archetype, typeIndex) == -1))
                    throw new ArgumentException($"Archetype does not have component {componentType}");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertComponentIsUnmanaged(TypeIndex componentTypeIndex)
        {
            if (TypeManager.IsManagedType(componentTypeIndex))
                throw new ArgumentException($"Component type is managed. Can not get pointer to data");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanAddComponent(Archetype* archetype, ComponentType componentType)
        {
            if (Hint.Unlikely(componentType == m_EntityComponentType))
                throw new ArgumentException("Cannot add Entity as a component.");

            if (ChunkDataUtility.GetIndexInTypeArray(archetype, componentType.TypeIndex) != -1)
                return; // archetype already has the component, so "add" will be a no-op.

            if (Hint.Unlikely(componentType.IsSharedComponent && (archetype->NumSharedComponents == kMaxSharedComponentCount)))
                throw new InvalidOperationException($"Cannot add more than {kMaxSharedComponentCount} SharedComponent's to a single Archetype. Attempting to add '{componentType}'. Archetype already contains types ({AggregateArchetypeComponentTypes(archetype)}).");

            var componentTypeInfo = GetTypeInfo(componentType.TypeIndex);
            var componentInstanceSize = GetComponentArraySize(componentTypeInfo.SizeInChunk, 1);
            var archetypeInstanceSize = archetype->InstanceSizeWithOverhead + componentInstanceSize;
            var chunkDataSize = Chunk.GetChunkBufferSize();
            if (Hint.Unlikely(archetypeInstanceSize > chunkDataSize))
                throw new InvalidOperationException($"Entity archetype component data is too large. Previous archetype size per instance {archetype->InstanceSizeWithOverhead} bytes. Attempting to add component '{componentType}' with size {componentInstanceSize} bytes. Maximum chunk size {chunkDataSize}.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanAddComponents(Archetype* archetype, in ComponentTypeSet componentTypeSet)
        {
            int totalComponentInstanceSize = 0;
            for (int i = 0; i < componentTypeSet.Length; i++)
            {
                var type = componentTypeSet.GetComponentType(i);

                if (type == m_EntityComponentType)
                    throw new ArgumentException("Cannot add Entity as a component.");

                var componentTypeInfo = GetTypeInfo(type.TypeIndex);
                totalComponentInstanceSize += GetComponentArraySize(componentTypeInfo.SizeInChunk, 1);
            }

            int numSharedComponents = componentTypeSet.m_masks.SharedComponents;
            if (numSharedComponents + archetype->NumSharedComponents > kMaxSharedComponentCount)
                throw new InvalidOperationException($"Cannot add more than {kMaxSharedComponentCount} SharedComponent's to a single Archetype. Attempting to add types {AggregateComponentTypes(componentTypeSet)}. Archetype already contains types ({AggregateArchetypeComponentTypes(archetype)}).");

            var archetypeInstanceSize = archetype->InstanceSizeWithOverhead + totalComponentInstanceSize;
            var chunkDataSize = Chunk.GetChunkBufferSize();
            if (archetypeInstanceSize > chunkDataSize)
                throw new InvalidOperationException($"Entity archetype component data is too large. Previous archetype size per instance {archetype->InstanceSizeWithOverhead} bytes. Attempting to add multiple components ({AggregateComponentTypes(componentTypeSet)}) with a combined size {totalComponentInstanceSize} bytes. Maximum chunk size {chunkDataSize}. Archetype already contains types ({AggregateArchetypeComponentTypes(archetype)}).");
        }

        static string AggregateComponentTypes(in ComponentTypeSet componentTypeSet)
        {
            var allTypes = $"{componentTypeSet.Length}: ";
            for (var i = 0; i < componentTypeSet.Length; i++)
            {
                var type = componentTypeSet.GetComponentType(i);
                allTypes += $"'{type}'";
                if (i < componentTypeSet.Length - 1) allTypes += ", ";
            }
            return allTypes;
        }

        static string AggregateArchetypeComponentTypes(Archetype* archetype)
        {
            var numTypes = archetype->TypesCount;
            var allTypes = $"{numTypes}: ";
            for (var i = 0; i < numTypes; i++)
            {
                allTypes += $"'{TypeManager.GetType(archetype->Types[i].TypeIndex)}'";
                if (i < numTypes - 1) allTypes += ", ";
            }
            return allTypes;
        }

        static string AggregateNewArchetypesComponentTypes(ComponentType* componentTypes, int componentTypeCount)
        {
            var allTypes = $"{componentTypeCount}: ";
            for (var i = 0; i < componentTypeCount; i++)
            {
                var type = componentTypes[i].TypeIndex;
                allTypes += $"'{TypeManager.GetType(type)}'";
                if (i < componentTypeCount - 1) allTypes += ", ";
            }
            return allTypes;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanAddComponent(Entity entity, ComponentType componentType)
        {
            if (!Exists(entity))
                throw new InvalidOperationException("The entity does not exist."  + AppendDestroyedEntityRecordError(entity));

            AssertCanAddComponent(GetArchetype(entity), componentType);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanAddComponent(Entity entity, TypeIndex componentType)
        {
            AssertCanAddComponent(entity, ComponentType.FromTypeIndex(componentType));
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanAddComponent(EntityQueryImpl* queryImpl, ComponentType componentType)
        {
            fixed (EntityComponentStore* pThis = &this)
            {
                if (Hint.Unlikely((ulong)queryImpl->_Access->EntityComponentStore != (ulong)pThis))
                    throw new InvalidOperationException("Provided query belongs to a different World.");
            }
            var archetypeList = queryImpl->_QueryData->MatchingArchetypes;
            int archetypeCount = archetypeList.Length;
            var ptrs = archetypeList.Ptr;
            for (int i = 0; i < archetypeCount; i++)
                AssertCanAddComponent(ptrs[i]->Archetype, componentType);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanAddComponents(UnsafeMatchingArchetypePtrList archetypeList, in ComponentTypeSet componentTypeSet)
        {
            int newShared = 0;
            int totalNewComponentSize = 0;
            for (int i = 0; i < componentTypeSet.Length; i++)
            {
                var componentType = componentTypeSet.GetComponentType(i);
                if (componentType == m_EntityComponentType)
                    throw new ArgumentException("Cannot add Entity as a component.");
                if (componentType.IsSharedComponent)
                    newShared++;
                totalNewComponentSize += GetComponentArraySize(GetTypeInfo(componentType.TypeIndex).SizeInChunk, 1);
            }

            int archetypeCount = archetypeList.Length;
            var ptrs = archetypeList.Ptr;
            for (int i = 0; i < archetypeCount; i++)
            {
                var archetype = ptrs[i]->Archetype;
                if ((archetype->NumSharedComponents + newShared) > kMaxSharedComponentCount)
                    throw new InvalidOperationException($"Cannot add more than {kMaxSharedComponentCount} SharedComponent's to a single Archetype. Attempting to add types {AggregateComponentTypes(componentTypeSet)}. Archetype already contains types ({AggregateArchetypeComponentTypes(archetype)}).");
                if ((archetype->InstanceSizeWithOverhead + totalNewComponentSize) > Chunk.GetChunkBufferSize())
                    throw new InvalidOperationException($"Entity archetype component data is too large. Previous archetype size per instance {archetype->InstanceSizeWithOverhead} bytes. Attempting to add components {AggregateComponentTypes(componentTypeSet)} with total size {totalNewComponentSize} bytes. Maximum chunk size {Chunk.GetChunkBufferSize()}. Archetype already contains types ({AggregateArchetypeComponentTypes(archetype)}).");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanAddComponents(Entity entity, in ComponentTypeSet typeSet)
        {
            if (!Exists(entity))
                throw new InvalidOperationException("The entity does not exist."  + AppendDestroyedEntityRecordError(entity));

            AssertCanAddComponents(GetArchetype(entity), typeSet);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanAddComponents(NativeArray<Entity> entities, in ComponentTypeSet typeSet)
        {
            for (int i = 0; i < entities.Length; ++i)
            {
                var entity = entities[i];
                AssertCanAddComponents(entity, typeSet);
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanAddComponent(NativeArray<Entity> entities, ComponentType type)
        {
            for (int i = 0; i < entities.Length; ++i)
            {
                var entity = entities[i];
                AssertCanAddComponent(entity, type);
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanAddComponent(NativeList<EntityBatchInChunk> batches, ComponentType componentType)
        {
            Archetype* archetype = null;
            for (int i = 0; i < batches.Length; i++)
            {
                var nextArchetype = batches[i].Chunk->Archetype;
                if (nextArchetype != archetype)
                {
                    if (archetype != null)
                        AssertCanAddComponent(nextArchetype, componentType);
                    archetype = nextArchetype;
                }
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanRemoveComponent(ComponentType componentType)
        {
            if (componentType == m_EntityComponentType)
                throw new ArgumentException("Cannot remove Entity as a component. Use DestroyEntity if you want to delete Entity and all associated components.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanRemoveComponents(in ComponentTypeSet typeSet)
        {
            for (int i = 0; i < typeSet.Length; ++i)
                AssertCanRemoveComponent(ComponentType.FromTypeIndex(typeSet.GetTypeIndex(i)));
        }


        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        void AssertWillDestroyAllInLinkedEntityGroup(UnsafeList<ChunkAndEnabledMask> chunksToProcess,
            ref BufferTypeHandle<LinkedEntityGroup> linkedGroupTypeHandle, bool queryHasEnableableComponents)
        {
            var sortedChunksToProcess = new UnsafeList<ChunkAndEnabledMask>(chunksToProcess.Length, Allocator.Temp);
            sortedChunksToProcess.CopyFrom(chunksToProcess);
            sortedChunksToProcess.Sort();
            var sortedChunkPtrs = new UnsafeList<ulong>(sortedChunksToProcess.Length, Allocator.Temp);
            for (int i = 0, chunkCount = sortedChunksToProcess.Length; i < chunkCount; ++i)
            {
                sortedChunkPtrs.AddNoResize((ulong)sortedChunksToProcess[i].Chunk);
            }

            fixed (EntityComponentStore* pThis = &this)
            {
                for (int i = 0, chunkCount = chunksToProcess.Length; i < chunkCount; ++i)
                {
                    var archetypeChunk = new ArchetypeChunk
                        { m_Chunk = chunksToProcess[i].Chunk, m_EntityComponentStore = pThis };
                    if (!archetypeChunk.Has(ref linkedGroupTypeHandle))
                        continue;

                    var chunkLegBuffers = archetypeChunk.GetBufferAccessor(ref linkedGroupTypeHandle);
                    for (int b = 0, bufferCount = chunkLegBuffers.Length; b < bufferCount; b++)
                    {
                        var buffer = chunkLegBuffers[b];
                        var legEntities = (Entity*)buffer.GetUnsafeReadOnlyPtr();
                        for (int e = 0, entityCount = buffer.Length; e < entityCount; e++)
                        {
                            var referencedEntity = legEntities[e];
                            if (!Exists(referencedEntity))
                                continue;
                            var referencedEntityInChunk = GetEntityInChunk(referencedEntity);
                            int sortedChunkIndex = sortedChunkPtrs.BinarySearch((ulong)referencedEntityInChunk.Chunk);
                            if (Hint.Likely(sortedChunkIndex >= 0))
                            {
                                // referencedEntity's chunk matches the query.
                                if (queryHasEnableableComponents && sortedChunksToProcess[sortedChunkIndex].UseEnabledMask != 0)
                                {
                                    // TODO: we only care about the lowest bit of the result, so computing the high 64 bits of the result here is wasted work.
                                    v128 shiftedMask = EnabledBitUtility.ShiftRight(sortedChunksToProcess[sortedChunkIndex].EnabledMask,
                                        referencedEntityInChunk.IndexInChunk);
                                    if (Hint.Unlikely((shiftedMask.ULong0 & 0x1) == 0))
                                    {
                                        // referencedChunk matches the query, but referencedEntity is not enabled in this query & will not be deleted
                                        var chunkEntities = (Entity*)chunksToProcess[i].Chunk->Buffer;
                                        ThrowDestroyEntityError(chunkEntities[b], referencedEntity);
                                    }
                                }
                            }
                            else
                            {
                                // referencedChunk isn't in chunksToProcess at all, and won't be destroyed.
                                var chunkEntities = (Entity*)chunksToProcess[i].Chunk->Buffer;
                                ThrowDestroyEntityError(chunkEntities[b], referencedEntity);
                            }
                        }
                    }
                }
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void ThrowDestroyEntityError(Entity errorEntity, Entity errorReferencedEntity)
        {
            ThrowDestroyEntityErrorFancy(errorEntity, errorReferencedEntity);
            throw new ArgumentException($"DestroyEntity(EntityQuery query) is destroying an entity which contains a LinkedEntityGroup with an entity not included in the query. If you want to destroy entities using a query all linked entities must be contained in the query.. For more detail, disable Burst compilation.");
        }

        [BurstDiscard]
        private void ThrowDestroyEntityErrorFancy(Entity errorEntity, Entity errorReferencedEntity)
        {
#if !DOTS_DISABLE_DEBUG_NAMES
            string errorEntityName = Exists(errorEntity) ? GetName(errorEntity) : "(Deleted)";
            string errorReferencedEntityName = Exists(errorReferencedEntity) ? GetName(errorReferencedEntity) : "(Deleted)";
            throw new ArgumentException($"DestroyEntity(EntityQuery query) is destroying entity {errorEntity} '{errorEntityName}' which contains a LinkedEntityGroup and the entity {errorReferencedEntity} '{errorReferencedEntityName}' in that group is not included in the query. If you want to destroy entities using a query all linked entities must be contained in the query..");
#endif
        }


        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public static void AssertArchetypeDoesNotRemoveCleanupComponents(Archetype* src, Archetype* dst)
        {
            int o = 0;
            int n = 0;

            for (; o < src->TypesCount && n < dst->TypesCount;)
            {
                var srcType = src->Types[o].TypeIndex;
                var dstType = dst->Types[n].TypeIndex;
                if (srcType == dstType)
                {
                    o++;
                    n++;
                }
                else if (dstType > srcType)
                {
                    if (src->Types[o].IsCleanupComponent)
                        throw new System.ArgumentException(
                            $"Cleanup components may not be removed via SetArchetype: {src->Types[o]}");
                    o++;
                }
                else
                    n++;
            }

            for (; o < src->TypesCount; o++)
            {
                if (src->Types[o].IsCleanupComponent)
                    throw new System.ArgumentException($"Cleanup components may not be removed via SetArchetype: {src->Types[o]}");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void CheckCanAddChunkComponent(NativeArray<ArchetypeChunk> chunkArray, ComponentType componentType, ref bool result)
        {
            var chunks = (ArchetypeChunk*)chunkArray.GetUnsafeReadOnlyPtr();

            for (int i = 0; i < chunkArray.Length; ++i)
            {
                var chunk = chunks[i].m_Chunk;
                if (ChunkDataUtility.GetIndexInTypeArray(chunk->Archetype, componentType.TypeIndex) != -1)
                {
                    result = false;
                    return;
                }
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void ThrowDuplicateChunkComponentError(ComponentType componentType)
        {
            throw new ArgumentException($"A chunk component with type:{componentType} has already been added to the chunk.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanInstantiateEntities(Entity srcEntity, Entity* outputEntities, int instanceCount)
        {
            if (HasComponent(srcEntity, m_LinkedGroupType))
            {
                var header = (BufferHeader*)GetComponentDataWithTypeRO(srcEntity, m_LinkedGroupType);
                var entityPtr = (Entity*)BufferHeader.GetElementPointer(header);
                var entityCount = header->Length;

                if (entityCount == 0 || entityPtr[0] != srcEntity)
                    throw new ArgumentException("LinkedEntityGroup[0] must always be the Entity itself.");

                for (int i = 0; i < entityCount; i++)
                {
                    if (!Exists(entityPtr[i]))
                        throw new ArgumentException(
                            $"The srcEntity's LinkedEntityGroup references an entity that is invalid. (Entity at index {i} on the LinkedEntityGroup.)");

                    var archetype = GetArchetype(entityPtr[i]);
                    if (archetype->InstantiateArchetype == null)
                        throw new ArgumentException(
                            $"The srcEntity's LinkedEntityGroup references an entity that has already been destroyed. (Entity at index {i} on the LinkedEntityGroup. Only cleanup components are left on the entity)");
                }
            }
            else
            {
                if (!Exists(srcEntity))
                    throw new ArgumentException("srcEntity is not a valid entity");

                var srcArchetype = GetArchetype(srcEntity);
                if (srcArchetype->InstantiateArchetype == null)
                    throw new ArgumentException(
                        "srcEntity is not instantiable because it has already been destroyed. (Only cleanup components are left on it)");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public void AssertCanInstantiateEntities(Entity* srcEntity, int entityCount, bool removePrefab)
        {
            for (int i = 0; i < entityCount; i++)
            {
                if (!Exists(srcEntity[i]))
                    throw new ArgumentException(
                        $"The srcEntity[{i}] references an entity that is invalid." + AppendDestroyedEntityRecordError(*srcEntity));

                var archetype = GetArchetype(srcEntity[i]);
                if ((removePrefab ? archetype->InstantiateArchetype : archetype->CopyArchetype) == null)
                    throw new ArgumentException(
                        $"The srcEntity[{i}] references an entity that has already been destroyed. (Only cleanup components are left on the entity)");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public static void AssertValidEntityQuery(EntityQuery query, EntityComponentStore* store)
        {
            var e = query._GetImpl()->_Access->EntityComponentStore;
            if (e != store)
            {
                AssertValidEntityQuery(e, store);
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public static void AssertValidEntityQuery(EntityComponentStore* queryStore, EntityComponentStore* store)
        {
            if (queryStore != store)
            {
                if (queryStore ==  null)
                    throw new System.InvalidOperationException("The EntityQuery has been disposed and can no longer be used.");
                else
                    throw new System.InvalidOperationException("EntityQuery is associated with a different world");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public static void AssertValidArchetype(EntityComponentStore* queryStore, EntityArchetype archetype)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (archetype.Archetype == null || archetype.Archetype->EntityComponentStore != queryStore)
            {
                if (archetype.Valid)
                    throw new System.ArgumentException("The EntityArchetype has not been allocated");
                else
                    throw new System.ArgumentException("The EntityArchetype was not created by this EntityManager");
            }
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public static void AssertPaddingBitsAreZeroForArchetype(Archetype* archetype)
        {
            for (int chunkIndex = 0; chunkIndex < archetype->Chunks.Count; ++chunkIndex)
            {
                var chunk = archetype->Chunks[chunkIndex];

                for (int enableableTypeIndex = 0; enableableTypeIndex < archetype->EnableableTypesCount; ++enableableTypeIndex)
                {
                    var typeIndexInArchetype = archetype->EnableableTypeIndexInArchetype[enableableTypeIndex];
                    int memoryOrderIndexInArchetype = archetype->TypeIndexInArchetypeToMemoryOrderIndex[typeIndexInArchetype];

                    v128 mask = *archetype->Chunks.GetComponentEnabledMaskArrayForTypeInChunk(memoryOrderIndexInArchetype, chunkIndex);
                    var paddingBits = EnabledBitUtility.ShiftRight(mask, chunk->Count);
                    int enabledCount = EnabledBitUtility.countbits(paddingBits);
                    if (enabledCount != 0)
                        Assert.AreEqual(0, enabledCount,
                        $"enabled bits padding check failed: chunk={chunkIndex} memoryOrderIndex={memoryOrderIndexInArchetype} mask={mask.ULong1:X16}:{mask.ULong0:X16} entityCount={chunk->Count} archetype={*archetype}");
                }
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        public static void AssertEnabledBitsHierarchyIsCorrectForArchetype(Archetype* archetype)
        {
            for (int chunkIndex = 0; chunkIndex < archetype->Chunks.Count; ++chunkIndex)
            {
                var chunk = archetype->Chunks[chunkIndex];

                for (int enableableTypeIndex = 0; enableableTypeIndex < archetype->EnableableTypesCount; ++enableableTypeIndex)
                {
                    var typeIndexInArchetype = archetype->EnableableTypeIndexInArchetype[enableableTypeIndex];
                    int memoryOrderIndexInArchetype = archetype->TypeIndexInArchetypeToMemoryOrderIndex[typeIndexInArchetype];
                    v128 mask = *archetype->Chunks.GetComponentEnabledMaskArrayForTypeInChunk(memoryOrderIndexInArchetype, chunkIndex);
                    int storedDisabledCount = archetype->Chunks.GetChunkDisabledCountForType(memoryOrderIndexInArchetype, chunkIndex);
                    int actualDisabledCount = chunk->Count - EnabledBitUtility.countbits(mask);
                    if (actualDisabledCount != storedDisabledCount)
                        Assert.AreEqual(actualDisabledCount, storedDisabledCount,
                        $"enabled bits hierarchical mismatch: chunk={chunkIndex} memoryOrderIndex={memoryOrderIndexInArchetype} mask={mask.ULong1:X16}:{mask.ULong0:X16} entityCount={chunk->Count} storedCount={storedDisabledCount} archetype={*archetype}");
                }
            }
        }

        // ----------------------------------------------------------------------------------------------------------
        // INTERNAL
        // ----------------------------------------------------------------------------------------------------------

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        internal void AssertNotZeroSizedComponent(TypeIndex typeIndex)
        {
            if (Hint.Unlikely(typeIndex.IsZeroSized))
                throw new System.ArgumentException(
                    $"This operation can not be called with a zero sized component ({typeIndex}).");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        internal static void AssertComponentSizeMatches(TypeIndex typeIndex, int size)
        {
            if (Hint.Unlikely(TypeManager.GetTypeInfo(typeIndex).SizeInChunk != size))
                throw new System.ArgumentException(
                    $"SetComponentData can not be called with a zero sized component ({typeIndex}) and must have same size as sizeof(T).");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        internal static void AssertComponentEnableable(TypeIndex typeIndex)
        {
            if (Hint.Unlikely(!typeIndex.IsEnableable))
                throw new System.ArgumentException(
                    $"Component Enabled Bits APIs (SetComponentEnabled, IsComponentEnabled, etc) can not be called with a component type ({typeIndex}) that does not implement IEnableableComponent");
        }

        internal static string AppendDestroyedEntityRecordError(Entity e)
        {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            if (!EntitiesJournaling.Enabled)
                return " " + k_JournalingDisabledMsg;

            // We want the most recent record view that is a DestroyEntity with this entity to report back to the user
            var records = EntitiesJournaling.GetRecords(EntitiesJournaling.Ordering.Descending)
                .WithRecordType(EntitiesJournaling.RecordType.DestroyEntity)
                .WithEntity(e);

            foreach (var record in records)
            {
                var executingSystemName = record.ExecutingSystem.Name;
                var executingSystemMessage = string.Empty;
                if (!string.IsNullOrEmpty(executingSystemName))
                    executingSystemMessage += $" by system {executingSystemName}";

                var originSystemName = record.OriginSystem.Name;
                var originSystemMessage = string.Empty;
                if (!string.IsNullOrEmpty(originSystemName))
                    originSystemMessage = $" This command was requested from system {originSystemName}.";

                return $" {e} was previously destroyed{executingSystemMessage} in world {record.World.Name}." + originSystemMessage;
            }
#endif
            return default;
        }

        internal static string AppendRemovedComponentRecordError(Entity e, ComponentType type)
        {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
            if (!EntitiesJournaling.Enabled)
                return " " + k_JournalingDisabledMsg;

            // We want the most recent record view that is a RemoveComponent with this entity and component type to report back to the user
            var records = EntitiesJournaling.GetRecords(EntitiesJournaling.Ordering.Descending)
                .WithRecordType(EntitiesJournaling.RecordType.RemoveComponent)
                .WithComponentType(type)
                .WithEntity(e);

            foreach (var record in records)
            {
                var executingSystemName = record.ExecutingSystem.Name;
                var executingSystemMessage = string.Empty;
                if (!string.IsNullOrEmpty(executingSystemName))
                    executingSystemMessage += $" by system {executingSystemName}";

                var originSystemName = record.OriginSystem.Name;
                var originSystemMessage = string.Empty;
                if (!string.IsNullOrEmpty(originSystemName))
                    originSystemMessage = $" This command was requested from system {originSystemName}.";

                return $" Component {type} was removed from {e} previously{executingSystemMessage} in world {record.World.Name}." + originSystemMessage;
            }
#endif
            return default;
        }
    }
}
