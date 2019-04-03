#if !UNITY_ZEROPLAYER
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Unity.Entities.Tests
{
    class WorldDebuggingToolsTests : ECSTestsFixture
    {

        [DisableAutoCreation]
        class RegularSystem : ComponentSystem
        {
            public ComponentGroup entities;
            
            protected override void OnUpdate()
            {
                throw new NotImplementedException();
            }

            protected override void OnCreateManager()
            {
                entities = GetComponentGroup(ComponentType.ReadWrite<EcsTestData>());
            }
        }

        [DisableAutoCreation]
        class ExcludeSystem : ComponentSystem
        {
            public ComponentGroup entities;
            
            protected override void OnUpdate()
            {
                throw new NotImplementedException();
            }
            
            protected override void OnCreateManager()
            {
                entities = GetComponentGroup(
                    ComponentType.ReadWrite<EcsTestData>(), 
                    ComponentType.Exclude<EcsTestData2>());
            }
        }

        [Test]
        public void SystemInclusionList_MatchesComponents()
        {
            var system = World.Active.GetOrCreateManager<RegularSystem>();
            
            var entity = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));

            var matchList = new List<Tuple<ScriptBehaviourManager, List<ComponentGroup>>>();
            
            WorldDebuggingTools.MatchEntityInComponentGroups(World.Active, entity, matchList);
            
            Assert.AreEqual(1, matchList.Count);
            Assert.AreEqual(system, matchList[0].Item1);
            Assert.AreEqual(system.ComponentGroups[0], matchList[0].Item2[0]);
        }

        [Test]
        public void SystemInclusionList_IgnoresSubtractedComponents()
        {
            World.Active.GetOrCreateManager<ExcludeSystem>();
            
            var entity = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));

            var matchList = new List<Tuple<ScriptBehaviourManager, List<ComponentGroup>>>();
            
            WorldDebuggingTools.MatchEntityInComponentGroups(World.Active, entity, matchList);
            
            Assert.AreEqual(0, matchList.Count);
        }
        
    }
}
#endif