using UnityEngine;

namespace Unity.Entities.Tests
{
    [AddComponentMenu("")]
    public class TempBakingPerformanceAuthoring : MonoBehaviour { public int Field; }

    [TemporaryBakingType]
    public struct TempBakingPerformanceComponent : IComponentData { public UnityObjectRef<TempBakingPerformanceAuthoring> component; }

    public struct TempBakingPerformanceSharedComp : ISharedComponentData
    {
        public int value;

        public TempBakingPerformanceSharedComp(int inValue)
        {
            value = inValue;
        }
    }

    class TempBakingPerformanceComponentSystem : Baker<TempBakingPerformanceAuthoring>
    {
        public override void Bake(TempBakingPerformanceAuthoring authoring)
        {
            AddSharedComponent(new TempBakingPerformanceSharedComp() { value = authoring.Field });
            AddComponent(new TempBakingPerformanceComponent() { component = authoring });
        }
    }
}
