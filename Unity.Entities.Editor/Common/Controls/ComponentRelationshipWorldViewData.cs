using System;

namespace Unity.Entities.Editor
{
    readonly struct ComponentRelationshipWorldViewData
    {
        public readonly World World;
        public readonly QueryWithEntitiesViewData QueryWithEntitiesViewData;
        public readonly ComponentMatchingSystems ComponentMatchingSystems;

        public ComponentRelationshipWorldViewData(World world, Type componentType)
        {
            World = world;
            var entityQueryDesc = new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    componentType
                },
                Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludePrefab
            };

            QueryWithEntitiesViewData = new QueryWithEntitiesViewData(world, world.EntityManager.CreateEntityQuery(entityQueryDesc));
            ComponentMatchingSystems = new ComponentMatchingSystems(world, componentType);
        }
    }
}
