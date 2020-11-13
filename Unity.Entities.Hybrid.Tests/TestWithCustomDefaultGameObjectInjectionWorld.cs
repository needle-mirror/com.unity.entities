using System;
using Unity.Entities;
using UnityEngine.LowLevel;

namespace Unity.Entities.Hybrid.Tests
{
    [Serializable]
    public struct TestWithCustomDefaultGameObjectInjectionWorld
    {
        public World PreviousGameObjectInjectionWorld;
        private bool _wasInPlayerLoop;

        public void Setup()
        {
            PreviousGameObjectInjectionWorld = World.DefaultGameObjectInjectionWorld;
            if (PreviousGameObjectInjectionWorld != null)
            {
                _wasInPlayerLoop = ScriptBehaviourUpdateOrder.IsWorldInCurrentPlayerLoop(PreviousGameObjectInjectionWorld);
                if (_wasInPlayerLoop)
                    ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(PreviousGameObjectInjectionWorld);
            }
            else
                _wasInPlayerLoop = false;

            World.DefaultGameObjectInjectionWorld = null;
        }

        public void TearDown()
        {
            if (World.DefaultGameObjectInjectionWorld != null)
                World.DefaultGameObjectInjectionWorld.Dispose();
            if (PreviousGameObjectInjectionWorld != null && !PreviousGameObjectInjectionWorld.IsCreated)
                PreviousGameObjectInjectionWorld = null;
            World.DefaultGameObjectInjectionWorld = PreviousGameObjectInjectionWorld;
            if (_wasInPlayerLoop)
            {
                ScriptBehaviourUpdateOrder.AddWorldToCurrentPlayerLoop(PreviousGameObjectInjectionWorld);
                _wasInPlayerLoop = false;
            }
        }
    }
}
