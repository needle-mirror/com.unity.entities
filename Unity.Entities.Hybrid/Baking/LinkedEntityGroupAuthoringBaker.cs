using UnityEngine;

namespace Unity.Entities.Hybrid.Baking
{
    [BakingType]
    internal struct LinkedEntityGroupBakingData : IBufferElementData
    {
        public Entity Value;

        public static implicit operator LinkedEntityGroupBakingData(Entity e)
        {
            return new LinkedEntityGroupBakingData {Value = e};
        }
    }

    internal class LinkedEntityGroupAuthoringBaker : Baker<LinkedEntityGroupAuthoring>
    {
        public override void Bake(LinkedEntityGroupAuthoring authoring)
        {
            //Retrieve all children and add their primary entities to the LinkedEntityGroup buffer
            var childrenGameObjects = GetChildren(true);

            var rootEntity = GetEntity(authoring);

            var buffer = AddBuffer<LinkedEntityGroupBakingData>(rootEntity);
            buffer.Add(rootEntity);

            foreach (var childGameObject in childrenGameObjects)
            {
                var childEntity = GetEntity(childGameObject);
                buffer.Add(childEntity);
            }
        }
    }
}
