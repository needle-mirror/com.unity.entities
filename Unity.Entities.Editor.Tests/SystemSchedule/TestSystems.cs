namespace Unity.Entities.Editor.Tests
{
    [UpdateInGroup(typeof(SystemScheduleTestGroup))]
    [UpdateBefore(typeof(SystemScheduleTestSystem2))]
    class SystemScheduleTestSystem1 : ComponentSystem
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
    class SystemScheduleTestSystem2 : ComponentSystem
    {
        protected override void OnUpdate()
        {
        }
    }

    struct SystemScheduleTestUnmanagedSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
        }

        public void OnDestroy(ref SystemState state)
        {
        }

        public void OnUpdate(ref SystemState state)
        {
        }
    }

    class SystemScheduleTestGroup : ComponentSystemGroup
    {
        protected override void OnUpdate()
        {
        }
    }

    class SystemScheduleTestSystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
        }
    }
}
