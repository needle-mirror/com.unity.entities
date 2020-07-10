using System.Collections.Generic;
using NUnit.Framework;

namespace Unity.Entities.Editor.Tests
{
    public struct JustComponentNonExclude : IComponentData {}
    public struct ZeroSizedComponent : IComponentData {}
    public struct NonZeroSizedComponent : IComponentData
    {
        public float Value;
    }

    public class ExclusionGroupSampleSystem : ComponentSystem
    {
        public EntityQuery Group1;
        public EntityQuery Group2;

        protected override void OnCreate()
        {
            Group1 = GetEntityQuery(typeof(JustComponentNonExclude), ComponentType.Exclude<ZeroSizedComponent>());
            Group2 = GetEntityQuery(typeof(JustComponentNonExclude), ComponentType.Exclude<NonZeroSizedComponent>());
        }

        protected override void OnUpdate()
        {
            throw new System.NotImplementedException();
        }
    }

    public class EntityQueryGUITests
    {
        [Test]
        public void EntityQueryGUI_ExcludedTypesUnaffectedByLength()
        {
            using (var world = new World("Test"))
            {
                var system = world.CreateSystem<ExclusionGroupSampleSystem>();
                var ui1 = new EntityQueryGUIControl(system.Group1.GetQueryTypes(), system.Group1.GetReadAndWriteTypes(), false);
                Assert.AreEqual(EntityDebuggerStyles.ComponentExclude, ui1.styles[1]);
                var ui2 = new EntityQueryGUIControl(system.Group2.GetQueryTypes(), system.Group2.GetReadAndWriteTypes(), false);
                Assert.AreEqual(EntityDebuggerStyles.ComponentExclude, ui2.styles[1]);
            }
        }

        [Test]
        public void EntityQueryGUI_ZeroComponentsHasZeroHeight()
        {
            var ui = new EntityQueryGUIControl(new List<ComponentType>(), true);
            ui.UpdateSize(100f);
            Assert.AreEqual(0, ui.Height);
        }
    }
}
