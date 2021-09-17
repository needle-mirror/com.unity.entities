using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    [BurstCompile]
    [GenerateBurstMonoInterop("StructuralChange")]
    unsafe partial struct StructuralChange
    {
        [BurstMonoInteropMethod(MakePublic = true)]
        static void _AddComponentEntitiesBatch(EntityComponentStore* entityComponentStore, UnsafeList<EntityBatchInChunk>* entityBatchList, int typeIndex)
        {
            entityComponentStore->AddComponent(entityBatchList, ComponentType.FromTypeIndex(typeIndex), 0);
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _AddComponentsEntitiesBatch(EntityComponentStore* entityComponentStore, UnsafeList<EntityBatchInChunk>* entityBatchList, ref ComponentTypes types)
        {
            entityComponentStore->AddComponents(entityBatchList, ref types);
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static bool _AddComponentEntity(EntityComponentStore* entityComponentStore, Entity* entity, int typeIndex)
        {
            return entityComponentStore->AddComponent(*entity, ComponentType.FromTypeIndex(typeIndex));
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _AddComponentsEntity(EntityComponentStore* entityComponentStore, Entity* entity, ref ComponentTypes types)
        {
            entityComponentStore->AddComponents(*entity, types);
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _AddComponentChunks(EntityComponentStore* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, int typeIndex)
        {
            entityComponentStore->AddComponent(chunks, chunkCount, ComponentType.FromTypeIndex(typeIndex));
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _AddComponentsChunks(EntityComponentStore* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, ref ComponentTypes types)
        {
            entityComponentStore->AddComponents(chunks, chunkCount, types);
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static bool _RemoveComponentEntity(EntityComponentStore* entityComponentStore, Entity* entity, int typeIndex)
        {
            return entityComponentStore->RemoveComponent(*entity, ComponentType.FromTypeIndex(typeIndex));
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _RemoveComponentsEntity(EntityComponentStore* entityComponentStore, Entity* entity, ref ComponentTypes types)
        {
            entityComponentStore->RemoveComponents(*entity, types);
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _RemoveComponentEntitiesBatch(EntityComponentStore* entityComponentStore, UnsafeList<EntityBatchInChunk>* entityBatchList, int typeIndex)
        {
            entityComponentStore->RemoveComponent(entityBatchList, ComponentType.FromTypeIndex(typeIndex));
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _RemoveComponentsEntitiesBatch(EntityComponentStore* entityComponentStore, UnsafeList<EntityBatchInChunk>* entityBatchList, ref ComponentTypes types)
        {
            entityComponentStore->RemoveComponents(entityBatchList, ref types);
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _RemoveComponentChunks(EntityComponentStore* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, int typeIndex)
        {
            entityComponentStore->RemoveComponent(chunks, chunkCount, ComponentType.FromTypeIndex(typeIndex));
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _RemoveComponentsChunks(EntityComponentStore* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, ref ComponentTypes types)
        {
            entityComponentStore->RemoveComponents(chunks, chunkCount, types);
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _AddSharedComponentChunks(EntityComponentStore* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, int componentTypeIndex, int sharedComponentIndex)
        {
            entityComponentStore->AddComponent(chunks, chunkCount, ComponentType.FromTypeIndex(componentTypeIndex), sharedComponentIndex);
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _MoveEntityArchetype(EntityComponentStore* entityComponentStore, Entity* entity, void* dstArchetype)
        {
            entityComponentStore->Move(*entity, (Archetype*)dstArchetype);
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _SetChunkComponent(EntityComponentStore* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, void* componentData, int componentTypeIndex)
        {
            entityComponentStore->SetChunkComponent(chunks, chunkCount, componentData, componentTypeIndex);
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _CreateEntity(EntityComponentStore* entityComponentStore, void* archetype, Entity* outEntities, int count)
        {
            entityComponentStore->CreateEntities((Archetype*)archetype, outEntities, count);
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _DestroyEntity(EntityComponentStore* entityComponentStore, Entity* entities, int count)
        {
            entityComponentStore->DestroyEntities(entities, count);
        }

        [BurstMonoInteropMethod(MakePublic = true)]
        static void _InstantiateEntities(EntityComponentStore* entityComponentStore, Entity* srcEntity, Entity* outputEntities, int instanceCount)
        {
            entityComponentStore->InstantiateEntities(*srcEntity, outputEntities, instanceCount);
        }
    }
}
