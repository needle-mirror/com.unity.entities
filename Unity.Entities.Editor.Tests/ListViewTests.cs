using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Entities.Tests;
using UnityEditor.IMGUI.Controls;

namespace Unity.Entities.Editor.Tests
{
    public class ListViewTests : ECSTestsFixture
    {
        private static void SetEntitySelection(Entity s)
        {
        }

        private World GetWorldSelection()
        {
            return World.Active;
        }

        private static void SetComponentGroupSelection(ComponentGroup group)
        {
        }

        private static ScriptBehaviourManager GetSystemSelection()
        {
            return currentSystem;
        }

        private static ScriptBehaviourManager currentSystem = null;

        private static void SetSystemSelection(ScriptBehaviourManager system, World world)
        {
        }

        private World World2;

        public override void Setup()
        {
            base.Setup();
            
            World2 = new World("Test World 2");

            World2.GetOrCreateManager<EntityManager>();
            World2.GetOrCreateManager<EmptySystem>();
            
            ScriptBehaviourUpdateOrder.UpdatePlayerLoop(World.AllWorlds.ToArray());
        }

        public override void TearDown()
        {
            World2.Dispose();
            World2 = null;
            
            base.TearDown();
            
            ScriptBehaviourUpdateOrder.UpdatePlayerLoop(World.Active);
        }

        [Test]
        public void EntityListView_ReloadWhenSettingNullGroup()
        {
            m_Manager.CreateEntity();
            var emptySystem = World.Active.GetOrCreateManager<EmptySystem>();
            
            var listView = new EntityListView(new TreeViewState(), null, SetEntitySelection, GetWorldSelection,
                GetSystemSelection);
            currentSystem = World.Active.GetExistingManager<EntityManager>();
            listView.SelectedComponentGroup = null;
            var rows = listView.GetRows();
            Assert.AreEqual(1, rows.Count);

            currentSystem = emptySystem;
            listView.SelectedComponentGroup = null;
            rows = listView.GetRows();
            Assert.AreEqual(0, rows.Count);
        }

        [Test]
        public void EntityListView_CanSetNullGroup()
        {
            var listView = new EntityListView(new TreeViewState(), null, SetEntitySelection, GetWorldSelection, GetSystemSelection);
            
            Assert.DoesNotThrow( () => listView.SelectedComponentGroup = null );
        }

        [Test]
        public void ComponentGroupListView_CanSetNullSystem()
        {
            var listView = new ComponentGroupListView(new TreeViewState(), EmptySystem, SetComponentGroupSelection, GetWorldSelection);

            Assert.DoesNotThrow(() => listView.SelectedSystem = null);
        }

        [Test]
        public void ComponentGroupListView_SortOrderExpected()
        {
            var typeList = new List<ComponentType>();
            var subtractive = ComponentType.Subtractive<EcsTestData>();
            var readWrite = ComponentType.Create<EcsTestData2>();
            var readOnly = ComponentType.ReadOnly<EcsTestData3>();
            
            typeList.Add(subtractive);
            typeList.Add(readOnly);
            typeList.Add(readWrite);
            typeList.Sort(ComponentGroupGUI.CompareTypes);
            
            Assert.AreEqual(readOnly, typeList[0]);
            Assert.AreEqual(readWrite, typeList[1]);
            Assert.AreEqual(subtractive, typeList[2]);
        }

        [Test]
        public void SystemListView_CanCreateWithNullWorld()
        {
            SystemListView listView;
            var states = new List<TreeViewState>();
            var stateNames = new List<string>();
            Assert.DoesNotThrow(() =>
            {
                listView = SystemListView.CreateList(states, stateNames, SetSystemSelection, () => null);
                listView.Reload();
            });
        }

        [Test]
        public void SystemListView_ShowExactlyWorldSystems()
        {
            var listView = new SystemListView(
                new TreeViewState(),
                new MultiColumnHeader(SystemListView.GetHeaderState()),
                (manager, world) => { },
                () => World.Active);
            var managerItems = listView.GetRows().Where(x => listView.managersById.ContainsKey(x.id)).Select(x => listView.managersById[x.id]);
            Assert.AreEqual(World.Active.BehaviourManagers.Count(), managerItems.Intersect(World.Active.BehaviourManagers).Count());
        }

        [Test]
        public void SystemListView_NullWorldShowsAllSystems()
        {
            var listView = new SystemListView(
                new TreeViewState(),
                new MultiColumnHeader(SystemListView.GetHeaderState()),
                (manager, world) => { },
                () => null);
            var managerItems = listView.GetRows().Where(x => listView.managersById.ContainsKey(x.id)).Select(x => listView.managersById[x.id]);
            var allManagers = new List<ScriptBehaviourManager>();
            foreach (var world in World.AllWorlds)
                allManagers.AddRange(world.BehaviourManagers);
            Assert.AreEqual(allManagers.Count, allManagers.Intersect(managerItems).Count());
        }
        
    }
}
