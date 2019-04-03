
namespace Unity.Entities.Editor
{
    
    internal class EntityListQuery
    {

        public ComponentGroup Group { get; }

        public EntityArchetypeQuery Query { get; }

        public EntityListQuery(ComponentGroup group)
        {
            this.Group = group;
        }

        public EntityListQuery(EntityArchetypeQuery query)
        {
            this.Query = query;
        }
    }

}

