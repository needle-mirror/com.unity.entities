using System;
using NUnit.Framework;
using Unity.Entities.Tests;
using UnityEditor;
using UnityEngine;

namespace Unity.Entities.Editor.Tests
{
    public class EntityDebuggerTests : ECSTestsFixture
    {

        private EntityDebugger m_Window;
        private ComponentSystem m_System;
        private ComponentGroup m_ComponentGroup;
        private Entity m_Entity;
        
        class SingleGroupSystem : ComponentSystem
        {
            
#pragma warning disable 0169 // "never used" warning
            struct Group
            {
                private int Length;
                private ComponentDataArray<EcsTestData> testDatas;
            }

            [Inject] private Group entities;
#pragma warning restore 0169
            
            protected override void OnUpdate()
            {
                throw new NotImplementedException();
            }
        }

        private static void CloseAllDebuggers()
        {
            var windows = Resources.FindObjectsOfTypeAll<EntityDebugger>();
            foreach (var window in windows)
                window.Close();
        }

        public override void Setup()
        {
            base.Setup();

            CloseAllDebuggers();
            
            m_Window = EditorWindow.GetWindow<EntityDebugger>();

            m_System = World.Active.GetOrCreateManager<SingleGroupSystem>();

            m_ComponentGroup = m_System.ComponentGroups[0];

            m_Entity = m_Manager.CreateEntity(typeof(EcsTestData));
        }

        public override void TearDown()
        {
            CloseAllDebuggers();
            
            base.TearDown();
        }

        [Test]
        public void EntityDebugger_SetAllSelections()
        {
            
            EntityDebugger.SetAllSelections(World.Active, m_System, m_ComponentGroup, m_Entity);
            
            Assert.AreEqual(World.Active, m_Window.WorldSelection);
            Assert.AreEqual(m_System, m_Window.SystemSelection);
            Assert.AreEqual(m_ComponentGroup, m_Window.ComponentGroupSelection);
            Assert.AreEqual(m_Entity, m_Window.EntitySelection);
        }

        [Test]
        public void EntityDebugger_RememberSelections()
        {
            
            EntityDebugger.SetAllSelections(World.Active, m_System, m_ComponentGroup, m_Entity);
            
            m_Window.SetWorldSelection(null, true);
            
            m_Window.SetWorldSelection(World.Active, true);
            
            Assert.AreEqual(World.Active, m_Window.WorldSelection);
            Assert.AreEqual(m_System, m_Window.SystemSelection);
            Assert.AreEqual(m_ComponentGroup, m_Window.ComponentGroupSelection);
            Assert.AreEqual(m_Entity, m_Window.EntitySelection);
        }

        [Test]
        public void EntityDebugger_SetAllEntitiesFilter()
        {
            var components = new ComponentType[] {ComponentType.Create<EcsTestData>() };
            var componentGroup = World.Active.GetExistingManager<EntityManager>().CreateComponentGroup(components);
            
            m_Window.SetWorldSelection(World.Active, true);
            m_Window.SetSystemSelection(null, true, true);
            m_Window.SetAllEntitiesFilter(componentGroup);
            Assert.AreEqual(componentGroup, m_Window.ComponentGroupSelection);
            
            m_Window.SetComponentGroupSelection(null, true, true);
            m_Window.SetSystemSelection(World.Active.GetExistingManager<EntityManager>(), true, true);
            m_Window.SetAllEntitiesFilter(componentGroup);
            Assert.AreEqual(componentGroup, m_Window.ComponentGroupSelection);
            
            m_Window.SetSystemSelection(m_System, true, true);
            m_Window.SetAllEntitiesFilter(componentGroup);
            Assert.AreNotEqual(componentGroup, m_Window.ComponentGroupSelection);
        }

        [Test]
        public void EntityDebugger_StylesIntact()
        {
            Assert.IsNotNull(EntityDebuggerStyles.ComponentRequired);
            Assert.IsNotNull(EntityDebuggerStyles.ComponentSubtractive);
            Assert.IsNotNull(EntityDebuggerStyles.ComponentReadOnly);
            Assert.IsNotNull(EntityDebuggerStyles.ComponentReadWrite);
            
            Assert.IsNotNull(EntityDebuggerStyles.ComponentRequired.normal.background);
            Assert.IsNotNull(EntityDebuggerStyles.ComponentSubtractive.normal.background);
            Assert.IsNotNull(EntityDebuggerStyles.ComponentReadOnly.normal.background);
            Assert.IsNotNull(EntityDebuggerStyles.ComponentReadWrite.normal.background);
        }
        
    }
}
