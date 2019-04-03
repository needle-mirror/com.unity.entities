using Unity.Jobs;

namespace Unity.Entities.Tests
{
    [DisableAutoCreation]
    public class TestEcsChangeSystem : JobComponentSystem
    {
        public int NumChanged;
        ComponentGroup ChangeGroup;
        protected override void OnCreateManager()
        {
            ChangeGroup = GetComponentGroup(typeof(EcsTestData));
            ChangeGroup.SetFilterChanged(typeof(EcsTestData));
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            NumChanged = ChangeGroup.CalculateLength();
            return inputDeps;
        }
    }
}
