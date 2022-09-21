using UnityEngine;

namespace Unity.Entities.Tests
{
    [AddComponentMenu("")]
    public class BakingOnlyAdditionalEntityTestAuthoring : MonoBehaviour
    {
        public int SelfValue;

        public struct BakingOnlyEntityTestComponent : IComponentData
        {
            public int Value;
        }

        class Baker : Baker<BakingOnlyAdditionalEntityTestAuthoring>
        {
            public override void Bake(BakingOnlyAdditionalEntityTestAuthoring authoring)
            {
                var component = new BakingOnlyEntityTestComponent
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

