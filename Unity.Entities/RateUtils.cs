using System;
using System.ComponentModel;
using System.Diagnostics;
using Unity.Core;
using Unity.Mathematics;

namespace Unity.Entities
{
    [Obsolete("This interface has been renamed to IRateManager (RemovedAfter DOTS 1.0)", true)]
    public interface IFixedRateManager
    {
        bool ShouldGroupUpdate(ComponentSystemGroup group);
        float Timestep { get; set; }
    }
    public interface IRateManager
    {
        bool ShouldGroupUpdate(ComponentSystemGroup group);
        float Timestep { get; set; }
    }

    public static class RateUtils
    {
        internal const float MinFixedDeltaTime = 0.0001f;
        internal const float MaxFixedDeltaTime = 10.0f;

        public class FixedRateSimpleManager : IRateManager
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

        public class FixedRateCatchUpManager : IRateManager
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

        /// <summary>
        /// A <see cref="IRateManager"/> implementation providing a variable update rate in milliseconds.
        /// </summary>
        public class VariableRateManager : IRateManager
        {
            /// <summary>
            /// The minimum allowed update rate in Milliseconds
            /// </summary>
            private const uint MinUpdateRateMS = 1;

            /// <summary>
            /// Should the world have <see cref="TimeData"/> pushed to it?
            /// </summary>
            private readonly bool m_ShouldPushToWorld;

            /// <summary>
            /// A cached copy of <see cref="Stopwatch.Frequency"/> as a <see cref="float"/>.
            /// </summary>
            /// <remarks>This is used explicitly when trying to calculate the <see cref="m_Timestep"/>.</remarks>
            private readonly float m_TicksPerSecond = Stopwatch.Frequency;

            /// <summary>
            ///     The required number of ticks to trigger an update when compared against <see cref="m_TickCount"/>
            ///     during <see cref="ShouldGroupUpdate"/>.
            /// </summary>
            private readonly long m_UpdateRate;

            /// <summary>
            /// The latest polled ticks from the timer mechanism.
            /// </summary>
            private long m_CurrentTimestamp;

            /// <summary>
            /// The elapsed time which the rate manager has operated.
            /// </summary>
            /// <remarks>
            ///     This does not have any protection against rollover issues, and is only updated if
            ///     <see cref="m_ShouldPushToWorld"/> is toggled.
            /// </remarks>
            private double m_ElapsedTime;

            /// <summary>
            /// The previous iterations ticks from the timer mechanism.
            /// </summary>
            private long m_PreviousTimestamp;

            /// <summary>
            /// Was <see cref="TimeData"/> pushed to the world?
            /// </summary>
            private bool m_DidPushTime;

            /// <summary>
            /// An accumulator of ticks observed during <see cref="ShouldGroupUpdate"/>.
            /// </summary>
            private long m_TickCount;

            /// <summary>
            /// The calculated delta time between updates.
            /// </summary>
            private float m_Timestep;

            /// <summary>
            /// Construct a <see cref="VariableRateManager"/> with a given Millisecond update rate.
            /// </summary>
            /// <remarks>
            ///     Utilizes an accumulator where when it exceeds the indicated tick count, triggers the update and
            ///     resets the counter.
            /// </remarks>
            /// <param name="updateRateInMS">
            ///     The update rate for the manager in Milliseconds, if the value is less then
            ///     <see cref="MinUpdateRateMS"/> it will be set to it.
            /// </param>
            /// <param name="pushToWorld">
            ///     Should <see cref="TimeData"/> be pushed onto the world? If systems inside of this group do not
            ///     require the use of the <see cref="World.Time"/>, a minor performance gain can be made setting this
            ///     to false.
            /// </param>
            public VariableRateManager(uint updateRateInMS = 66, bool pushToWorld = true)
            {
                // Ensure update rate is valid
                if (updateRateInMS < MinUpdateRateMS)
                {
                    updateRateInMS = MinUpdateRateMS;
                }

                // Cache our update rate in ticks
                m_UpdateRate = (long)(updateRateInMS * (Stopwatch.Frequency / 1000f));
                m_ShouldPushToWorld = pushToWorld;

                // Initialize our time data
                m_CurrentTimestamp = Stopwatch.GetTimestamp();
                m_PreviousTimestamp = m_CurrentTimestamp;

                // Make sure that the first call updates
                m_TickCount = m_UpdateRate;
            }

            /// <summary>
            /// Determines if the group should be updated this invoke.
            /// </summary>
            /// <remarks>The while loop happens once.</remarks>
            public bool ShouldGroupUpdate(ComponentSystemGroup @group)
            {
                // We're going to use the internal ticks to ensure this works in worlds without time systems.
                m_CurrentTimestamp = Stopwatch.GetTimestamp();

                // Calculate the difference between our current timestamp and the previous, but also account for the
                // possibility that the value may have rolled over.
                long difference;
                if (m_CurrentTimestamp < m_PreviousTimestamp)
                {
                    // Rollover protection
                    difference = (long.MaxValue - m_PreviousTimestamp) + m_CurrentTimestamp;
                }
                else
                {
                    difference = m_CurrentTimestamp - m_PreviousTimestamp;
                }

                // Save/increment
                m_PreviousTimestamp = m_CurrentTimestamp;
                m_TickCount += difference;

                // Remove that time we pushed on the world
                if (m_ShouldPushToWorld && m_DidPushTime)
                {
                    @group.World.PopTime();
                    m_DidPushTime = false;
                }

                // We haven't elapsed enough ticks, thus false.
                if (m_TickCount < m_UpdateRate)
                {
                    return false;
                }

                // Calculate what we believe is the delta elapsed since our last reset
                m_Timestep = m_TickCount / m_TicksPerSecond;

                // Push the current world time
                if (m_ShouldPushToWorld)
                {
                    m_ElapsedTime += m_Timestep;
                    group.World.PushTime(new TimeData(
                        elapsedTime: m_ElapsedTime,
                        deltaTime: m_Timestep));
                    m_DidPushTime = true;
                }

                // Reset tick count
                m_TickCount = 0;
                return true;
            }

            /// <inheritdoc />
            public float Timestep
            {
                get => m_Timestep;
                set => m_Timestep = value;
            }
        }
    }
}
