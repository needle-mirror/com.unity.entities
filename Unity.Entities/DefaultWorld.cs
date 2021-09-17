using System;
using System.Collections.Generic;
using Unity.Jobs.LowLevel.Unsafe;
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
        }

        protected override void OnUpdate()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS && !UNITY_DOTSRUNTIME
            JobsUtility.ClearSystemIds();
#endif
            base.OnUpdate();
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
        /// <summary>
        /// Set the timestep use by this group, in seconds. The default value is 1/60 seconds.
        /// This value will be clamped to the range [0.0001f ... 10.0f].
        /// </summary>
        public float Timestep
        {
            get => RateManager != null ? RateManager.Timestep : 0;
            set
            {
                if (RateManager != null)
                    RateManager.Timestep = value;
            }
        }

        [Preserve]
        public FixedStepSimulationSystemGroup()
        {
            float defaultFixedTimestep = 1.0f / 60.0f;
            RateManager = new RateUtils.FixedRateCatchUpManager(defaultFixedTimestep);
        }
    }

    
    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(VariableRateSimulationSystemGroup), OrderFirst = true)]
    public class BeginVariableRateSimulationEntityCommandBufferSystem : EntityCommandBufferSystem {}

    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(VariableRateSimulationSystemGroup), OrderLast = true)]
    public class EndVariableRateSimulationEntityCommandBufferSystem : EntityCommandBufferSystem {}

    /// <summary>
    /// This system group is configured by default to use a variable update rate of ~15fps (66ms).
    /// </summary>
    /// <remarks>
    /// The value of `Time.ElapsedTime` and `Time.DeltaTime` will be temporarily overriden
    /// while this group is updating to the value total elapsed time since the previous update.
    /// You can configure the update rate manually by replacing the <see cref="IRateManager"/>.
    /// </remarks>
    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(BeginSimulationEntityCommandBufferSystem))]
    public class VariableRateSimulationSystemGroup : ComponentSystemGroup
    {
        /// <summary>
        /// The timestep use by this group, in seconds. This value will reflect the total elapsed time since the last update.
        /// </summary>
        public float Timestep
        {
            get => RateManager != null ? RateManager.Timestep : 0;
        }

        [Preserve]
        public VariableRateSimulationSystemGroup()
        {
            RateManager = new RateUtils.VariableRateManager();            
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
        }
    }
}
