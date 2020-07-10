using NUnit.Framework;
using Unity.Entities.Hybrid.Tests;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;

namespace Unity.Entities.Tests
{
    public class WorldScriptUpdateOrderTests
    {
        TestWithCustomDefaultGameObjectInjectionWorld m_DefaultWorld = default;
        PlayerLoopSystem m_PrevPlayerLoop;

        [SetUp]
        public void Setup()
        {
            m_PrevPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();
            m_DefaultWorld.Setup();
        }

        [Test, Explicit]
        public void AddRemoveScriptUpdate()
        {
            DefaultWorldInitialization.Initialize("Test World", true);

            var newWorld = new World("WorldA");
            newWorld.CreateSystem<InitializationSystemGroup>();
            newWorld.CreateSystem<SimulationSystemGroup>();
            newWorld.CreateSystem<PresentationSystemGroup>();
            Assert.IsFalse(ScriptBehaviourUpdateOrder.IsWorldInCurrentPlayerLoop(newWorld));

            ScriptBehaviourUpdateOrder.AddWorldToCurrentPlayerLoop(newWorld);
            Assert.IsTrue(ScriptBehaviourUpdateOrder.IsWorldInCurrentPlayerLoop(newWorld));

            PlayerLoop.SetPlayerLoop(PlayerLoop.GetDefaultPlayerLoop()); // TODO(DOTS-2283): Shouldn't stomp default player loop here
            Assert.IsFalse(ScriptBehaviourUpdateOrder.IsWorldInCurrentPlayerLoop(newWorld));

            var playerLoop = PlayerLoop.GetDefaultPlayerLoop(); // TODO(DOTS-2283): Shouldn't stomp default player loop here
            ScriptBehaviourUpdateOrder.AddWorldToPlayerLoop(World.DefaultGameObjectInjectionWorld, ref playerLoop);
            PlayerLoop.SetPlayerLoop(playerLoop);
            Assert.IsTrue(ScriptBehaviourUpdateOrder.IsWorldInCurrentPlayerLoop(World.DefaultGameObjectInjectionWorld));
        }

        [TearDown]
        public void TearDown()
        {
            m_DefaultWorld.TearDown();
            PlayerLoop.SetPlayerLoop(m_PrevPlayerLoop);
        }
    }
}
