using NUnit.Framework;

namespace Unity.Entities.Tests.Conversion
{
    class ConversionSystemTests : ConversionTestFixtureBase
    {
        [UpdateInGroup(typeof(GameObjectConversionGroup))]
        [WorldSystemFilter(WorldSystemFilterFlags.GameObjectConversion)]
        private class Group1 : ComponentSystemGroup {}

        [UpdateInGroup(typeof(GameObjectConversionGroup))]
        [UpdateBefore(typeof(Group1))]
        [WorldSystemFilter(WorldSystemFilterFlags.GameObjectConversion)]
        private class Group2 : ComponentSystemGroup {}

        [UpdateInGroup(typeof(Group1))]
        private class System1 : GameObjectConversionSystem
        {
            public static int CounterRead;
            protected override void OnUpdate()
            {
                CounterRead = s_Counter++;
            }
        }

        [UpdateInGroup(typeof(Group2))]
        private class System2 : GameObjectConversionSystem
        {
            public static int CounterRead;
            protected override void OnUpdate()
            {
                CounterRead = s_Counter++;
            }
        }

        private static int s_Counter = 0;

        [Test]
        public void GameObjectConversion_SupportsSystemGroups()
        {
            var systems = new[]
            {
                typeof(Group1),
                typeof(Group2),
                typeof(System1),
                typeof(System2),
            };
            System1.CounterRead = 0;
            System2.CounterRead = 0;

            var settings = MakeDefaultSettings();
            settings.ExtraSystems = systems;
            GameObjectConversionUtility.ConvertGameObjectHierarchy(CreateGameObject(), settings);
            // System1 should update after System2 because their groups Group1 and Group2 are setup such that Group2 updates
            // before Group1.
            Assert.Greater(System1.CounterRead, System2.CounterRead);
        }
    }
}
