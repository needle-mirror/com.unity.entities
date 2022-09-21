using UnityEngine;

namespace Unity.Entities.Tests
{
    [AddComponentMenu("")]
    public class BakingOnlyPrimaryWithAdditionalEntitiesTestAuthoring : MonoBehaviour
    {
        public int SelfValue;

        public struct PrimaryBakeOnlyAdditionalEntityTestComponent: IComponentData
        {
            public int Value;
        }

        class Baker : Baker<BakingOnlyPrimaryWithAdditionalEntitiesTestAuthoring>
        {
            public override void Bake(BakingOnlyPrimaryWithAdditionalEntitiesTestAuthoring authoring)
            {
                var component = new PrimaryBakeOnlyAdditionalEntityTestComponent
                {
                    Value = authoring.SelfValue
                };
                AddComponent(component);

                var bakeOnlyEntity = CreateAdditionalEntity(TransformUsageFlags.Default, true);
                AddComponent(bakeOnlyEntity, component);

                var noBakeOnlyEntity = CreateAdditionalEntity();
                AddComponent(noBakeOnlyEntity, component);
            }
        }
    }
}


