using UnityEngine;

namespace Unity.Entities.Tests
{
    [AddComponentMenu("")]
    public class BakingTypeTestAuthoring : MonoBehaviour
    {
        public int SelfValue;
        public DependsOnComponentTransitiveTestAuthoring Dependency;
        [BakingType]
        public struct BakingTypeTestComponent : IComponentData
        {
            public int Value;
        }

        class Baker : Baker<BakingTypeTestAuthoring>
        {
            public override void Bake(BakingTypeTestAuthoring authoring)
            {
                AddComponent(new BakingTypeTestComponent
                {
                    Value = 0
                });
            }
        }
    }


}

