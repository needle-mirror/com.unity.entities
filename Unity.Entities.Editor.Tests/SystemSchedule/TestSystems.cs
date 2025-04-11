namespace Unity.Entities.Editor.Tests
{
    [DisableAutoCreation]
    public partial class ManualCreationSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(SystemScheduleTestGroup))]
    [UpdateBefore(typeof(SystemScheduleTestSystem2))]
    partial class SystemScheduleTestSystem1 : SystemBase
    {
        EntityQuery m_Group;

        protected override void OnUpdate()
        {
        }

        protected override void OnCreate()
        {
            m_Group = GetEntityQuery(typeof(SystemScheduleTestData1), ComponentType.ReadOnly<SystemScheduleTestData2>());
        }

        protected override void OnDestroy()
        {
        }
    }

    [UpdateInGroup(typeof(SystemScheduleTestGroup))]
    [UpdateAfter(typeof(SystemScheduleTestSystem1))]
    partial class SystemScheduleTestSystem2 : SystemBase
    {
        protected override void OnUpdate()
        {
        }
    }

    partial struct SystemScheduleTestUnmanagedSystem : ISystem
    {
    }

    partial class SystemScheduleTestGroup : ComponentSystemGroup
    {
        protected override void OnUpdate()
        {
        }
    }

    partial class SystemScheduleTestSystem : SystemBase
    {
        protected override void OnUpdate()
        {
        }
    }
}
