using System;
using System.ComponentModel;
using Unity.Core;
using Unity.Mathematics;

namespace Unity.Entities
{

    public interface IFixedRateManager
    {
        bool ShouldGroupUpdate(ComponentSystemGroup group);
        float Timestep { get; set; }
    }

    public static class FixedRateUtils
    {
        /// <summary>
        /// Configure the given ComponentSystemGroup to update at a specific fixed rate, using the provided timestep.
        /// If the interval between the current time and the last update is bigger than the timestep,
        /// the group's systems will be updated more than once.
        /// </summary>
        /// <param name="group">The group whose UpdateCallback will be configured with a fixed timestep update call</param>
        /// <param name="timestep">The duration of each fixed timestep (in seconds). The timestep value will be clamped to the range [0.0001f ... 10.0f].</param>
        [Obsolete("Assign ComponentSystemGroup.FixedRateManager directly. (RemovedAfter 2020-12-26)")]
        public static void EnableFixedRateWithCatchUp(ComponentSystemGroup group, float timestep)
        {
            var manager = new FixedRateCatchUpManager(timestep);
            group.FixedRateManager = manager;
        }

        /// <summary>
        /// Configure the given ComponentSystemGroup to update at a specific fixed rate, using the provided timestep.
        /// The group will always be ticked exactly once, and the delta time during that update will be the given timestep.
        /// The group's elapsed time clock will drift from actual elapsed wall clock time.
        /// </summary>
        /// <param name="group">The group whose UpdateCallback will be configured with a fixed timestep update call</param>
        /// <param name="timestep">The duration of each fixed timestep (in seconds). The timestep valuewill be clamped to the range [0.0001f ... 10.0f].</param>
        [Obsolete("Assign ComponentSystemGroup.FixedRateManager directly. (RemovedAfter 2020-12-26)")]
        public static void EnableFixedRateSimple(ComponentSystemGroup group, float timestep)
        {
            var manager = new FixedRateSimpleManager(timestep);
            group.FixedRateManager = manager;
        }

        /// <summary>
        /// Disable fixed rate updates on the given group, by setting the UpdateCallback to null.
        /// </summary>
        /// <param name="group">The group whose UpdateCallback to set to null.</param>
        [Obsolete("Set ComponentSystemGroup.FixedRateManager to null. (RemovedAfter 2020-12-26)")]
        public static void DisableFixedRate(ComponentSystemGroup group)
        {
            group.FixedRateManager = null;
        }

        internal const float MinFixedDeltaTime = 0.0001f;
        internal const float MaxFixedDeltaTime = 10.0f;

        public class FixedRateSimpleManager : IFixedRateManager
        {
            float m_FixedTimestep;
            public float Timestep
            {
                get => m_FixedTimestep;
                set => m_FixedTimestep = math.clamp(value, MinFixedDeltaTime, MaxFixedDeltaTime);
            }
            double m_LastFixedUpdateTime;
            bool m_DidPushTime;

            public FixedRateSimpleManager(float fixedDeltaTime)
            {
                Timestep = fixedDeltaTime;
            }

            public bool ShouldGroupUpdate(ComponentSystemGroup group)
            {
                // if this is true, means we're being called a second or later time in a loop
                if (m_DidPushTime)
                {
                    group.World.PopTime();
                    m_DidPushTime = false;
                    return false;
                }

                group.World.PushTime(new TimeData(
                    elapsedTime: m_LastFixedUpdateTime,
                    deltaTime: m_FixedTimestep));

                m_LastFixedUpdateTime += m_FixedTimestep;

                m_DidPushTime = true;
                return true;
            }
        }

        public class FixedRateCatchUpManager : IFixedRateManager
        {
            // TODO: move this to World
            float m_MaximumDeltaTime;
            public float MaximumDeltaTime
            {
                get => m_MaximumDeltaTime;
                set => m_MaximumDeltaTime = math.max(value, m_FixedTimestep);
            }

            float m_FixedTimestep;
            public float Timestep
            {
                get => m_FixedTimestep;
                set
                {
                    m_FixedTimestep = math.clamp(value, MinFixedDeltaTime, MaxFixedDeltaTime);
                }
            }

            double m_LastFixedUpdateTime;
            long m_FixedUpdateCount;
            bool m_DidPushTime;
            double m_MaxFinalElapsedTime;

            public FixedRateCatchUpManager(float fixedDeltaTime)
            {
                Timestep = fixedDeltaTime;
            }

            public bool ShouldGroupUpdate(ComponentSystemGroup group)
            {
                float worldMaximumDeltaTime = group.World.MaximumDeltaTime;
                float maximumDeltaTime = math.max(worldMaximumDeltaTime, m_FixedTimestep);

                // if this is true, means we're being called a second or later time in a loop
                if (m_DidPushTime)
                {
                    group.World.PopTime();
                }
                else
                {
                    m_MaxFinalElapsedTime = m_LastFixedUpdateTime + maximumDeltaTime;
                }

                var finalElapsedTime = math.min(m_MaxFinalElapsedTime, group.World.Time.ElapsedTime);
                if (m_FixedUpdateCount == 0)
                {
                    // First update should always occur at t=0
                }
                else if (finalElapsedTime - m_LastFixedUpdateTime >= m_FixedTimestep)
                {
                    // Advance the timestep and update the system group
                    m_LastFixedUpdateTime += m_FixedTimestep;
                }
                else
                {
                    // No update is necessary at this time.
                    m_DidPushTime = false;
                    return false;
                }

                m_FixedUpdateCount++;

                group.World.PushTime(new TimeData(
                    elapsedTime: m_LastFixedUpdateTime,
                    deltaTime: m_FixedTimestep));

                m_DidPushTime = true;
                return true;
            }
        }
    }
}
