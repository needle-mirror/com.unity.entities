using System.Collections.Generic;
using Unity.Entities;
using Unity.Entities.Conversion;
using UnityEngine;

namespace Unity.Entities.Tests
{
    public struct EntityRefTestData : IComponentData
    {
        public Entity Value;

        public EntityRefTestData(Entity value) => Value = value;

        public override string ToString() => Value.ToString();
    }

    [AddComponentMenu("")]
    public class EntityRefTestDataAuthoring : MonoBehaviour
    {
        public GameObject Value;
        public int        AdditionalEntityCount;
        public bool       DeclareLinkedEntityGroup;

        // Empty Update function makes it so that unity shows the UI for the checkbox.
        // We use it for testing stripping of components.
        // ReSharper disable once Unity.RedundantEventFunction
        void Update() {}
    }

    public class EntityRefTestDataBaker : Baker<EntityRefTestDataAuthoring>
    {
        public override void Bake(EntityRefTestDataAuthoring authoring)
        {
            var entity = GetEntity(authoring.Value);
            AddComponent(new EntityRefTestData {Value = entity});
            for (int i = 0; i != authoring.AdditionalEntityCount; i++)
            {
                var additional = CreateAdditionalEntity();
                AddComponent(additional, new EntityRefTestData {Value = entity});
            }

            //TODO: Needs to declare prefabs as well
        }
    }
}
