using System;
using Unity.Assertions;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Unity.Entities
{
    internal unsafe partial struct EntityComponentStore
    {
        // ----------------------------------------------------------------------------------------------------------
        // PUBLIC
        // ----------------------------------------------------------------------------------------------------------

        public bool AddComponent(Entity entity, ComponentType type)
        {
            var dstChunk = GetChunkWithEmptySlotsWithAddedComponent(entity, type);
            if (dstChunk == null)
                return false;

            Move(entity, dstChunk);
            return true;
        }

        public void AddComponent(Entity entity, ComponentTypeSet componentTypeSet)
        {
            var chunk = GetChunk(entity);
            var newArchetype = GetArchetypeWithAddedComponents(chunk->Archetype, componentTypeSet);
            if (newArchetype == chunk->Archetype)  // none were removed
                return;
            var archetypeChunkFilter = GetArchetypeChunkFilterWithAddedComponents(chunk, newArchetype);
            Move(entity, ref archetypeChunkFilter);
        }

        public bool RemoveComponent(Entity entity, ComponentType type)
        {
            var dstChunk = GetChunkWithEmptySlotsWithRemovedComponent(entity, type);
            if (dstChunk == null)
                return false;

            Move(entity, dstChunk);
            return true;
        }

        public void RemoveComponent(Entity entity, ComponentTypeSet componentTypeSet)
        {
            var chunk = GetChunk(entity);
            var newArchetype = GetArchetypeWithRemovedComponents(chunk->Archetype, componentTypeSet);
            if (newArchetype == chunk->Archetype)  // none were removed
                return;
            var archetypeChunkFilter = GetArchetypeChunkFilterWithRemovedComponents(chunk, newArchetype);
            Move(entity, ref archetypeChunkFilter);
        }

        bool AddComponent(EntityBatchInChunk entityBatchInChunk, ComponentType componentType, int sharedComponentIndex = 0)
        {
            var srcChunk = entityBatchInChunk.Chunk;
            var archetypeChunkFilter = GetArchetypeChunkFilterWithAddedComponent(srcChunk, componentType, sharedComponentIndex);
            if (archetypeChunkFilter.Archetype == null)
                return false;

            Move(entityBatchInChunk, ref archetypeChunkFilter);
            return true;
        }

        bool AddComponents(EntityBatchInChunk entityBatchInChunk, ComponentTypeSet componentTypeSet)
        {
            var srcChunk = entityBatchInChunk.Chunk;

            var dstArchetype = GetArchetypeWithAddedComponents(srcChunk->Archetype, componentTypeSet);
            if (dstArchetype == srcChunk->Archetype)  // none were added
                return false;

            var archetypeChunkFilter = GetArchetypeChunkFilterWithAddedComponents(srcChunk, dstArchetype);
            if (archetypeChunkFilter.Archetype == null)
                return false;

            Move(entityBatchInChunk, ref archetypeChunkFilter);
            return true;
        }

        bool RemoveComponent(EntityBatchInChunk entityBatchInChunk, ComponentType componentType)
        {
            var srcChunk = entityBatchInChunk.Chunk;
            var archetypeChunkFilter = GetArchetypeChunkFilterWithRemovedComponent(srcChunk, componentType);
            if (archetypeChunkFilter.Archetype == null)
                return false;

            Move(entityBatchInChunk, ref archetypeChunkFilter);
            return true;
        }

        bool RemoveComponents(EntityBatchInChunk entityBatchInChunk, ComponentTypeSet componentTypeSet)
        {
            var srcChunk = entityBatchInChunk.Chunk;

            var dstArchetype = GetArchetypeWithRemovedComponents(srcChunk->Archetype, componentTypeSet);
            if (dstArchetype == srcChunk->Archetype)  // none were removed
                return false;

            var archetypeChunkFilter = GetArchetypeChunkFilterWithRemovedComponents(srcChunk, dstArchetype);
            if (archetypeChunkFilter.Archetype == null)
                return false;

            Move(entityBatchInChunk, ref archetypeChunkFilter);
            return true;
        }

        public void AddComponent(ArchetypeChunk* chunks, int chunkCount, ComponentType componentType, int sharedComponentIndex = 0)
        {
            Archetype* prevArchetype = null;
            Archetype* dstArchetype = null;
            int indexInTypeArray = 0;

            for (int i = 0; i < chunkCount; i++)
            {
                var chunk = chunks[i].m_Chunk;
                var srcArchetype = chunk->Archetype;
                if (prevArchetype != chunk->Archetype)
                {
                    dstArchetype = GetArchetypeWithAddedComponent(srcArchetype, componentType, &indexInTypeArray);
                    prevArchetype = chunk->Archetype;
                }

                if (dstArchetype == null)
                    continue;

                var archetypeChunkFilter = GetArchetypeChunkFilterWithAddedComponent(chunk, dstArchetype, indexInTypeArray, componentType, sharedComponentIndex);

                Move(chunk, ref archetypeChunkFilter);
            }
        }

        public void AddComponents(ArchetypeChunk* chunks, int chunkCount, ComponentTypeSet componentTypeSet)
        {
            Archetype* prevArchetype = null;
            Archetype* dstArchetype = null;

            for (int i = 0; i < chunkCount; i++)
            {
                var chunk = chunks[i].m_Chunk;
                var srcArchetype = chunk->Archetype;

                if (prevArchetype != srcArchetype)
                {
                    dstArchetype = GetArchetypeWithAddedComponents(srcArchetype, componentTypeSet);
                    prevArchetype = srcArchetype;
                }

                if (dstArchetype == srcArchetype)
                    continue;

                var archetypeChunkFilter = GetArchetypeChunkFilterWithAddedComponents(chunk, dstArchetype);

                Move(chunk, ref archetypeChunkFilter);
            }
        }

        public void RemoveComponent(ArchetypeChunk* chunks, int chunkCount, ComponentType componentType)
        {
            Archetype* prevArchetype = null;
            Archetype* dstArchetype = null;
            int indexInTypeArray = 0;

            for (int i = 0; i < chunkCount; i++)
            {
                var chunk = chunks[i].m_Chunk;
                var srcArchetype = chunk->Archetype;

                if (prevArchetype != chunk->Archetype)
                {
                    dstArchetype = GetArchetypeWithRemovedComponent(srcArchetype, componentType, &indexInTypeArray);
                    prevArchetype = chunk->Archetype;
                }

                if (dstArchetype == srcArchetype)
                    continue;

                var archetypeChunkFilter = GetArchetypeChunkFilterWithRemovedComponent(chunk, dstArchetype, indexInTypeArray, componentType);

                Move(chunk, ref archetypeChunkFilter);
            }
        }

        public void RemoveComponents(ArchetypeChunk* chunks, int chunkCount, ComponentTypeSet componentTypeSet)
        {
            Archetype* prevArchetype = null;
            Archetype* dstArchetype = null;

            for (int i = 0; i < chunkCount; i++)
            {
                var chunk = chunks[i].m_Chunk;
                var srcArchetype = chunk->Archetype;

                if (prevArchetype != chunk->Archetype)
                {
                    dstArchetype = GetArchetypeWithRemovedComponents(srcArchetype, componentTypeSet);
                    prevArchetype = chunk->Archetype;
                }

                if (dstArchetype == srcArchetype)
                    continue;

                var archetypeChunkFilter = GetArchetypeChunkFilterWithRemovedComponents(chunk, dstArchetype);

                Move(chunk, ref archetypeChunkFilter);
            }
        }

        public void AddComponent(UnsafeList<EntityBatchInChunk>* sortedEntityBatchList, ComponentType type, int existingSharedComponentIndex)
        {
            Assert.IsFalse(type.IsChunkComponent);

            // Reverse order so that batch indices do not change while iterating.
            for (int i = sortedEntityBatchList->Length - 1; i >= 0; i--)
                AddComponent(sortedEntityBatchList->Ptr[i], type, existingSharedComponentIndex);
        }

        public void AddComponents(UnsafeList<EntityBatchInChunk>* sortedEntityBatchList, ref ComponentTypeSet typeSet)
        {
            Assert.IsFalse(typeSet.ChunkComponentCount > 0);

            // Reverse order so that batch indices do not change while iterating.
            for (int i = sortedEntityBatchList->Length - 1; i >= 0; i--)
                AddComponents(sortedEntityBatchList->Ptr[i], typeSet);
        }

        public void RemoveComponents(UnsafeList<EntityBatchInChunk>* sortedEntityBatchList, ref ComponentTypeSet typeSet)
        {
            Assert.IsFalse(typeSet.ChunkComponentCount > 0);

            // Reverse order so that batch indices do not change while iterating.
            for (int i = sortedEntityBatchList->Length - 1; i >= 0; i--)
                RemoveComponents(sortedEntityBatchList->Ptr[i], typeSet);
        }

        public void RemoveComponent(UnsafeList<EntityBatchInChunk>* sortedEntityBatchList, ComponentType type)
        {
            Assert.IsFalse(type.IsChunkComponent);

            // Reverse order so that batch indices do not change while iterating.
            for (int i = sortedEntityBatchList->Length - 1; i >= 0; i--)
                RemoveComponent(sortedEntityBatchList->Ptr[i], type);
        }

        public void AddMultipleComponentsWithValidation(Entity entity, ComponentTypeSet componentTypeSet)
        {
            AssertCanAddComponents(entity, componentTypeSet);
            AddComponent(entity, componentTypeSet);
        }


        public void RemoveMultipleComponentsWithValidation(Entity entity, ComponentTypeSet componentTypeSet)
        {
            ValidateEntity(entity);
            AssertCanRemoveComponents(componentTypeSet);
            RemoveComponent(entity, componentTypeSet);
        }

        public void SetSharedComponentDataIndex(Entity entity, ComponentType componentType, int dstSharedComponentDataIndex)
        {
            var archetypeChunkFilter = GetArchetypeChunkFilterWithChangedSharedComponent(GetChunk(entity), componentType, dstSharedComponentDataIndex);
            if (archetypeChunkFilter.Archetype == null)
                return;

            ChunkDataUtility.SetSharedComponentDataIndex(entity, archetypeChunkFilter.Archetype, archetypeChunkFilter.SharedComponentValues, componentType.TypeIndex);
        }

        // Note previously called SetArchetype: SetArchetype is used internally to refer to the function which only creates the cross-reference between the
        // entity id and the archetype (m_ArchetypeByEntity). This is not "Setting" the archetype, it is moving the components to a different archetype.
        public void Move(Entity entity, Archetype* dstArchetype)
        {
            var archetypeChunkFilter = GetArchetypeChunkFilterWithChangedArchetype(GetChunk(entity), dstArchetype);
            if (archetypeChunkFilter.Archetype == null)
                return;

            Move(entity, ref archetypeChunkFilter);
        }

        public void Move(Entity entity, Archetype* archetype, SharedComponentValues sharedComponentValues)
        {
            var archetypeChunkFilter = new ArchetypeChunkFilter(archetype, sharedComponentValues);
            Move(entity, ref archetypeChunkFilter);
        }

        // ----------------------------------------------------------------------------------------------------------
        // INTERNAL
        // ----------------------------------------------------------------------------------------------------------

        void Move(Entity entity, Chunk* dstChunk)
        {
            var srcEntityInChunk = GetEntityInChunk(entity);
            var srcChunk = srcEntityInChunk.Chunk;
            var srcChunkIndex = srcEntityInChunk.IndexInChunk;
            var entityBatch = new EntityBatchInChunk { Chunk = srcChunk, Count = 1, StartIndex = srcChunkIndex };

            Move(entityBatch, dstChunk);
        }

        void Move(Entity entity, ref ArchetypeChunkFilter archetypeChunkFilter)
        {
            var srcEntityInChunk = GetEntityInChunk(entity);
            var entityBatch = new EntityBatchInChunk
                {Chunk = srcEntityInChunk.Chunk, Count = 1, StartIndex = srcEntityInChunk.IndexInChunk};

            Move(entityBatch, ref archetypeChunkFilter);
        }

        void Move(Chunk* srcChunk, ref ArchetypeChunkFilter archetypeChunkFilter)
        {
            if (archetypeChunkFilter.Archetype->CleanupComplete)
            {
                ChunkDataUtility.Deallocate(srcChunk);
                return;
            }

            var srcArchetype = srcChunk->Archetype;
            if (ChunkDataUtility.AreLayoutCompatible(srcArchetype, archetypeChunkFilter.Archetype))
            {
                fixed(int* sharedComponentValues = archetypeChunkFilter.SharedComponentValues)
                {
                    ChunkDataUtility.ChangeArchetypeInPlace(srcChunk, archetypeChunkFilter.Archetype, sharedComponentValues);
                }
                return;
            }

            var entityBatch = new EntityBatchInChunk { Chunk = srcChunk, Count = srcChunk->Count, StartIndex = 0 };
            Move(entityBatch, ref archetypeChunkFilter);
        }

        void Move(EntityBatchInChunk entityBatchInChunk, ref ArchetypeChunkFilter archetypeChunkFilter)
        {
            var cleanupComplete = archetypeChunkFilter.Archetype->CleanupComplete;

            var srcChunk = entityBatchInChunk.Chunk;
            var srcRemainingCount = entityBatchInChunk.Count;
            var startIndex = entityBatchInChunk.StartIndex;

            if ((srcRemainingCount == srcChunk->Count) && cleanupComplete)
            {
                ChunkDataUtility.Deallocate(srcChunk);
                return;
            }

            while (srcRemainingCount > 0)
            {
                var dstChunk = GetChunkWithEmptySlots(ref archetypeChunkFilter);
                var dstCount = Move(new EntityBatchInChunk { Chunk = srcChunk, Count = srcRemainingCount, StartIndex = startIndex }, dstChunk);
                srcRemainingCount -= dstCount;
            }
        }

        // ----------------------------------------------------------------------------------------------------------
        // Core, self-contained functions to change chunks. No other functions should actually move data from
        // one Chunk to another, or otherwise change the structure of a Chunk after creation.
        // ----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Move subset of chunk data into another chunk.
        /// </summary>
        /// <remarks>
        /// Chunks can be of same archetype (but differ by shared component values).
        /// </remarks>
        /// <returns>Returns the number moved. Caller handles if less than indicated in srcBatch.</returns>
        int Move(in EntityBatchInChunk srcBatch, Chunk* dstChunk)
        {
            var srcChunk = srcBatch.Chunk;
            var srcCount = math.min(dstChunk->UnusedCount, srcBatch.Count);
            var srcStartIndex = srcBatch.StartIndex + srcBatch.Count - srcCount;

            var partialSrcBatch = new EntityBatchInChunk
            {
                Chunk = srcChunk,
                StartIndex = srcStartIndex,
                Count = srcCount
            };

            ChunkDataUtility.Clone(partialSrcBatch, dstChunk);
            ChunkDataUtility.Remove(partialSrcBatch);

            return srcCount;
        }
    }
}
