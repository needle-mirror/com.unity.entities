using NUnit.Framework;
using Unity.Entities.Hybrid.Tests;
using UnityEngine.LowLevel;

namespace Unity.Entities.Editor.Tests
{
    class ControlsTestFixture
    {
        PlayerLoopSystem m_PrevPlayerLoop;
        TestWithCustomDefaultGameObjectInjectionWorld m_CustomInjectionWorld;
        public World m_TestWorld;
        public SystemBase m_SystemA;
        public SystemBase m_SystemB;
        WorldProxyManager m_WorldProxyManager;
        public WorldProxy m_WorldProxy;

        [SetUp]
        public void SetUp()
        {
            m_PrevPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();
            m_CustomInjectionWorld.Setup();
            DefaultWorldInitialization.Initialize("Relationship Test world", false);
            m_TestWorld = World.DefaultGameObjectInjectionWorld;

            var group = m_TestWorld.GetOrCreateSystemManaged<SimulationSystemGroup>();
            m_SystemA = m_TestWorld.GetOrCreateSystemManaged<TestSystemsForControls.SystemA>();
            m_SystemB = m_TestWorld.GetOrCreateSystemManaged<TestSystemsForControls.SystemB>();
            group.AddSystemToUpdateList(m_SystemA);
            group.AddSystemToUpdateList(m_SystemB);
            group.SortSystems();

            m_WorldProxyManager = new WorldProxyManager();
            m_WorldProxyManager.CreateWorldProxiesForAllWorlds();
            m_WorldProxy = m_WorldProxyManager.GetWorldProxyForGivenWorld(m_TestWorld);
        }

        [TearDown]
        public void TearDown()
        {
            m_CustomInjectionWorld.TearDown();
            PlayerLoop.SetPlayerLoop(m_PrevPlayerLoop);
        }
    }
}
