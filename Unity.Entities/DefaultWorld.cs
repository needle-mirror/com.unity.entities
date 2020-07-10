using System;
using System.Collections.Generic;
using UnityEngine.Scripting;

namespace Unity.Entities
{
    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public class BeginInitializationEntityCommandBufferSystem : EntityCommandBufferSystem {}

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderLast = true)]
    public class EndInitializationEntityCommandBufferSystem : EntityCommandBufferSystem {}

    public class InitializationSystemGroup : ComponentSystemGroup
    {
        [Preserve]
        public InitializationSystemGroup()
        {
            UseLegacySortOrder = false;

        }
    }

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true)]
    public class BeginFixedStepSimulationEntityCommandBufferSystem : EntityCommandBufferSystem {}

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderLast = true)]
    public class EndFixedStepSimulationEntityCommandBufferSystem : EntityCommandBufferSystem {}

    /// <summary>
    /// This system group is configured by default to use a fixed timestep for the duration of its
    /// updates.
    /// </summary>
    /// <remarks>
    /// The value of `Time.ElapsedTime` and `Time.DeltaTime` will be temporarily overriden
    /// while this group is updating. The systems in this group will update as many times as necessary
    /// at the fixed timestep in order to "catch up" to the actual elapsed time since the previous frame.
    /// The default fixed timestep is 1/60 seconds. This value can be overriden at runtime by modifying
    /// the system group's `Timestep` property.
    /// </remarks>
    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(BeginSimulationEntityCommandBufferSystem))]
    public class FixedStepSimulationSystemGroup : ComponentSystemGroup
    {
        private FixedRateUtils.FixedRateCatchUpManager _fixedRateManager;

        /// <summary>
        /// Set the timestep use by this group, in seconds. The default value is 1/60 seconds.
        /// This value will be clamped to the range [0.0001f ... 10.0f].
        /// </summary>
        public float Timestep
        {
            get => _fixedRateManager.Timestep;
            set => _fixedRateManager.Timestep = value;
        }

        /// <summary>
        /// Set the maximum DeltaTime that will be processed by this group in a single call to Update().
        /// If the actual DeltaTime is larger, the remaining time will be processed during the group's next Update.
        /// This helps maintain a minimum frame rate after a large frame time spike, by spreading out the recovery over
        /// multiple frames.
        /// The value is expressed in seconds. The default value is 1/3rd seconds. Recommended values are 1/10th and 1/3rd seconds.
        /// This value can never be lower than the current Timestep value.
        /// </summary>
        public float MaximumDeltaTime
        {
            get => _fixedRateManager.MaximumDeltaTime;
            set => _fixedRateManager.MaximumDeltaTime = value;
        }

        [Preserve]
        public FixedStepSimulationSystemGroup()
        {
            UseLegacySortOrder = false;

            float defaultFixedTimestep = 1.0f / 60.0f;
            _fixedRateManager = new FixedRateUtils.FixedRateCatchUpManager(defaultFixedTimestep);
            UpdateCallback = _fixedRateManager.UpdateCallback;
        }
    }



    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public class BeginSimulationEntityCommandBufferSystem : EntityCommandBufferSystem {}

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public class EndSimulationEntityCommandBufferSystem : EntityCommandBufferSystem {}

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateBefore(typeof(EndSimulationEntityCommandBufferSystem))]
    public class LateSimulationSystemGroup : ComponentSystemGroup {}

    public class SimulationSystemGroup : ComponentSystemGroup
    {
        [Preserve]
        public SimulationSystemGroup()
        {
            UseLegacySortOrder = false;
        }
    }

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst =  true)]
    public class BeginPresentationEntityCommandBufferSystem : EntityCommandBufferSystem {}

    public class PresentationSystemGroup : ComponentSystemGroup
    {
        [Preserve]
        public PresentationSystemGroup()
        {
            UseLegacySortOrder = false;
        }
    }
}
