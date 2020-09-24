using Unity.Core;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace Unity.Entities
{
    [Preserve]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public class UpdateWorldTimeSystem : ComponentSystem
    {
        private bool hasTickedOnce = false;

        protected override void OnUpdate()
        {
            var currentElapsedTime = Time.ElapsedTime;
            var deltaTime = math.min(UnityEngine.Time.deltaTime, World.MaximumDeltaTime);
            World.SetTime(new TimeData(
                elapsedTime: hasTickedOnce ? (currentElapsedTime + deltaTime) : currentElapsedTime,
                deltaTime: deltaTime
            ));
            hasTickedOnce = true;
        }
    }
}
