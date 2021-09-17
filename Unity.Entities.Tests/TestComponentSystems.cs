using Unity.Jobs;

namespace Unity.Entities.Tests
{
    [AlwaysUpdateSystem]
    public partial class TestEcsChangeSystem : SystemBase
    {
        public int NumChanged;
        EntityQuery ChangeGroup;
        protected override void OnCreate()
        {
            ChangeGroup = GetEntityQuery(typeof(EcsTestData));
            ChangeGroup.SetChangedVersionFilter(typeof(EcsTestData));
        }

        protected override void OnUpdate()
        {
            NumChanged = ChangeGroup.CalculateEntityCount();
        }
    }
}
