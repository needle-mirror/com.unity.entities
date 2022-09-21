namespace Unity.Entities
{
    [ConverterVersion("joe", 5)]
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.EntitySceneOptimizations)]
    internal partial class RemoveEntityInBakeDestroyEntitiesSystem : SystemBase
    {
        EntityQuery _DestroyRemoveEntityInBake;
        protected override void OnCreate()
        {
            _DestroyRemoveEntityInBake = GetEntityQuery(
                new EntityQueryDesc()
                {
                    Any = new ComponentType[] { ComponentType.ReadOnly<RemoveUnusedEntityInBake>(), ComponentType.ReadOnly<BakingOnlyEntity>() },
                    Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
                });
        }

        protected override void OnUpdate()
        {
            EntityManager.DestroyEntity(_DestroyRemoveEntityInBake);
        }
    }
}
