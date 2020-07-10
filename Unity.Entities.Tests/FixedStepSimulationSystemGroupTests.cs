using System.Collections.Generic;
using NUnit.Framework;
using Unity.Core;
using Unity.Mathematics;

namespace Unity.Entities.Tests
{
    public class FixedStepSimulationSystemGroupTests : ECSTestsFixture
    {
        class RecordUpdateTimesSystem : SystemBase
        {
            public List<TimeData> Updates = new List<TimeData>();
            protected override void OnUpdate()
            {
                Updates.Add(World.Time);
            }
        }

        [Test]
        public void FixedStepSimulationSystemGroup_FirstUpdateAtTimeZero_OneUpdateAtZero()
        {
            var fixedSimGroup = World.CreateSystem<FixedStepSimulationSystemGroup>();
            var updateTimesSystem = World.CreateSystem<RecordUpdateTimesSystem>();
            fixedSimGroup.AddSystemToUpdateList(updateTimesSystem);
            fixedSimGroup.SortSystems();

            fixedSimGroup.Timestep = 1.0f;
            // The first fixed-timestep group update always includes an update at elapsedTime=0
            World.PushTime(new TimeData(0.0f, 0.01f));
            fixedSimGroup.Update();
            World.PopTime();
            CollectionAssert.AreEqual(updateTimesSystem.Updates,
                new[]
                {
                    new TimeData(0.0f, 1.0f),
                });
        }

        [Test]
        public void FixedStepSimulationSystemGroup_FirstUpdateAtTimeEpsilon_OneUpdateAtZero()
        {
            var fixedSimGroup = World.CreateSystem<FixedStepSimulationSystemGroup>();
            var updateTimesSystem = World.CreateSystem<RecordUpdateTimesSystem>();
            fixedSimGroup.AddSystemToUpdateList(updateTimesSystem);
            fixedSimGroup.SortSystems();

            fixedSimGroup.Timestep = 1.0f;
            // The first fixed-timestep group update always includes an update at elapsedTime=0,
            // even if the first elapsedTime we see is non-zero.
            World.PushTime(new TimeData(0.02f, 0.01f));
            fixedSimGroup.Update();
            World.PopTime();
            CollectionAssert.AreEqual(updateTimesSystem.Updates,
                new[]
                {
                    new TimeData(0.0f, 1.0f),
                });
        }

        [Test]
        public void FixedStepSimulationSystemGroup_LargeElapsedTime_UpdateTimesAreCorrect()
        {
            var fixedSimGroup = World.CreateSystem<FixedStepSimulationSystemGroup>();
            var updateTimesSystem = World.CreateSystem<RecordUpdateTimesSystem>();
            fixedSimGroup.AddSystemToUpdateList(updateTimesSystem);
            fixedSimGroup.SortSystems();

            fixedSimGroup.Timestep = 1.0f;
            fixedSimGroup.MaximumDeltaTime = 10.0f;
            // Simulate a large elapsed time since the previous frame. (the deltaTime here is irrelevant)
            World.PushTime(new TimeData(8.5f, 0.01f));
            fixedSimGroup.Update();
            World.PopTime();
            CollectionAssert.AreEqual(updateTimesSystem.Updates,
                new[]
                {
                    new TimeData(0.0f, 1.0f),
                    new TimeData(1.0f, 1.0f),
                    new TimeData(2.0f, 1.0f),
                    new TimeData(3.0f, 1.0f),
                    new TimeData(4.0f, 1.0f),
                    new TimeData(5.0f, 1.0f),
                    new TimeData(6.0f, 1.0f),
                    new TimeData(7.0f, 1.0f),
                    new TimeData(8.0f, 1.0f),
                });
        }

        [Test]
        public void FixedStepSimulationSystemGroup_ZeroElapsedTime_NoUpdates()
        {
            var fixedSimGroup = World.CreateSystem<FixedStepSimulationSystemGroup>();
            var updateTimesSystem = World.CreateSystem<RecordUpdateTimesSystem>();
            fixedSimGroup.AddSystemToUpdateList(updateTimesSystem);
            fixedSimGroup.SortSystems();

            fixedSimGroup.Timestep = 1.0f;
            fixedSimGroup.MaximumDeltaTime = 10.0f;
            // Simulate a large elapsed time since the previous frame. (the deltaTime here is irrelevant)
            World.PushTime(new TimeData(8.5f, 0.01f));
            fixedSimGroup.Update();
            updateTimesSystem.Updates.Clear();
            // A second update at the exact same time should not trigger an update
            fixedSimGroup.Update();
            World.PopTime();
            Assert.AreEqual(0, updateTimesSystem.Updates.Count);
        }

        [Test]
        public void FixedStepSimulationSystemGroup_SmallElapsedTime_UpdateTimesAreCorrect()
        {
            var fixedSimGroup = World.CreateSystem<FixedStepSimulationSystemGroup>();
            var updateTimesSystem = World.CreateSystem<RecordUpdateTimesSystem>();
            fixedSimGroup.AddSystemToUpdateList(updateTimesSystem);
            fixedSimGroup.SortSystems();

            fixedSimGroup.Timestep = 1.0f;
            fixedSimGroup.MaximumDeltaTime = 10.0f;
            // Simulate a large elapsed time since the previous frame. (the deltaTime here is irrelevant)
            World.PushTime(new TimeData(8.5f, 0.01f));
            fixedSimGroup.Update();
            updateTimesSystem.Updates.Clear();
            // A small dt at this point should not trigger an update
            World.PushTime(new TimeData(8.8f, 0.3f));
            fixedSimGroup.Update();
            World.PopTime();
            Assert.AreEqual(0, updateTimesSystem.Updates.Count);
            // A second small dt results in enough accumulated elapsedTime to warrant a new update
            World.PushTime(new TimeData(9.1f, 0.3f));
            fixedSimGroup.Update();
            World.PopTime();
            CollectionAssert.AreEqual(updateTimesSystem.Updates,
                new[]
                {
                    new TimeData(9.0f, 1.0f),
                });
        }

        [Test]
        public void FixedStepSimulationSystemGroup_RuntimeTimestepChange_UpdateTimesAreCorrect()
        {
            var fixedSimGroup = World.CreateSystem<FixedStepSimulationSystemGroup>();
            var updateTimesSystem = World.CreateSystem<RecordUpdateTimesSystem>();
            fixedSimGroup.AddSystemToUpdateList(updateTimesSystem);
            fixedSimGroup.SortSystems();

            fixedSimGroup.Timestep = 1.0f;
            fixedSimGroup.MaximumDeltaTime = 10.0f;
            // Simulate several seconds at 1 update per second
            World.PushTime(new TimeData(4.6f, 0.01f));
            fixedSimGroup.Update();
            World.PopTime();
            // Switch to a shorter timestep in the middle of a long timestep.
            // The new dt should take effect starting from the most recent "last update time".
            fixedSimGroup.Timestep = 0.125f;
            World.PushTime(new TimeData(5.0f, 0.01f));
            fixedSimGroup.Update();
            World.PopTime();
            CollectionAssert.AreEqual(updateTimesSystem.Updates,
                new[]
                {
                    new TimeData(0.0f, 1.0f),
                    new TimeData(1.0f, 1.0f),
                    new TimeData(2.0f, 1.0f),
                    new TimeData(3.0f, 1.0f),
                    new TimeData(4.0f, 1.0f),
                    new TimeData(4.125f, 0.125f),
                    new TimeData(4.25f, 0.125f),
                    new TimeData(4.375f, 0.125f),
                    new TimeData(4.5f, 0.125f),
                    new TimeData(4.625f, 0.125f),
                    new TimeData(4.75f, 0.125f),
                    new TimeData(4.875f, 0.125f),
                    new TimeData(5.0f, 0.125f),
                });
        }

        [Test]
        public void FixedStepSimulationSystemGroup_TimestepMinMaxRange_IsValid()
        {
            Assert.Less(0.0f, FixedRateUtils.MinFixedDeltaTime, "minimum fixed timestep must be >0");
            Assert.LessOrEqual(FixedRateUtils.MinFixedDeltaTime, FixedRateUtils.MaxFixedDeltaTime,
                "minimum fixed timestep must be <= maximum fixed timestep");

        }

        [Test]
        public void FixedStepSimulationSystemGroup_TimestepTooLow_ClampedToMinimum()
        {
            var fixedSimGroup = World.CreateSystem<FixedStepSimulationSystemGroup>();
            fixedSimGroup.Timestep = 0.0f;
            Assert.AreEqual(FixedRateUtils.MinFixedDeltaTime, fixedSimGroup.Timestep);
        }

        [Test]
        public void FixedStepSimulationSystemGroup_TimestepTooHigh_ClampedToMaximum()
        {
            var fixedSimGroup = World.CreateSystem<FixedStepSimulationSystemGroup>();
            fixedSimGroup.Timestep = FixedRateUtils.MaxFixedDeltaTime + 1.0f;
            Assert.AreEqual(FixedRateUtils.MaxFixedDeltaTime, fixedSimGroup.Timestep);
        }

        [Test]
        public void FixedStepSimulationSystemGroup_TimestepInValidRange_NotClamped()
        {
            var fixedSimGroup = World.CreateSystem<FixedStepSimulationSystemGroup>();
            float validTimestep =
                math.lerp(FixedRateUtils.MinFixedDeltaTime, FixedRateUtils.MaxFixedDeltaTime, 0.5f);
            fixedSimGroup.Timestep = validTimestep;
                Assert.AreEqual(validTimestep, fixedSimGroup.Timestep);
        }

        [Test]
        public void FixedStepSimulationSystemGroup_DisableFixedTimestep_GroupUpdatesOnce()
        {
            var fixedSimGroup = World.CreateSystem<FixedStepSimulationSystemGroup>();
            var updateTimesSystem = World.CreateSystem<RecordUpdateTimesSystem>();
            fixedSimGroup.AddSystemToUpdateList(updateTimesSystem);
            fixedSimGroup.SortSystems();

            fixedSimGroup.Timestep = 1.0f;
            FixedRateUtils.DisableFixedRate(fixedSimGroup);
            // Simulate a large elapsed time since the previous frame
            World.PushTime(new TimeData(8.5f, 0.01f));
            fixedSimGroup.Update();
            World.PopTime();
            // with fixed timestep disabled, the group should see the same elapsed/delta times as the World.
            CollectionAssert.AreEqual(updateTimesSystem.Updates,
                new[]
                {
                    new TimeData(8.5f, 0.01f),
                });
        }

        [Test]
        public void FixedStepSimulationSystemGroup_ChangeMaximumDeltaTime_Works()
        {
            var fixedSimGroup = World.CreateSystem<FixedStepSimulationSystemGroup>();
            fixedSimGroup.MaximumDeltaTime = 0.1f;
            Assert.AreEqual(0.1f, fixedSimGroup.MaximumDeltaTime, 0.0001f);
            fixedSimGroup.MaximumDeltaTime = 0.3f;
            Assert.AreEqual(0.3f, fixedSimGroup.MaximumDeltaTime, 0.0001f);
        }

        [Test]
        public void FixedStepSimulationSystemGroup_MaximumDeltaTimeTooLow_ClampedToTimestep()
        {
            var fixedSimGroup = World.CreateSystem<FixedStepSimulationSystemGroup>();
            fixedSimGroup.Timestep = 0.2f;
            fixedSimGroup.MaximumDeltaTime = 0.1f;
            Assert.AreEqual(0.2f, fixedSimGroup.MaximumDeltaTime, 0.0001f);
        }

        [Test]
        public void FixedStepSimulationSystemGroup_ElapsedTimeExceedsMaximumDeltaTime_GradualRecovery()
        {
            var fixedSimGroup = World.CreateSystem<FixedStepSimulationSystemGroup>();
            var updateTimesSystem = World.CreateSystem<RecordUpdateTimesSystem>();
            fixedSimGroup.AddSystemToUpdateList(updateTimesSystem);
            fixedSimGroup.SortSystems();

            float dt = 0.125f;
            fixedSimGroup.Timestep = dt;
            fixedSimGroup.MaximumDeltaTime = 2*dt;
            // Simulate a frame spike
            // The recovery should be spread over several frames; instead of 8 ticks after the first Update(),
            // we should see at most two ticks per update until the group catches up to the elapsed time.
            World.PushTime(new TimeData(7*dt, 0.01f));
            fixedSimGroup.Update();
            CollectionAssert.AreEqual(updateTimesSystem.Updates,
                new[]
                {
                    new TimeData(0*dt, dt), // first Update() always ticks at t=0
                    new TimeData(1*dt, dt),
                    new TimeData(2*dt, dt),
                });
            updateTimesSystem.Updates.Clear();

            fixedSimGroup.Update();
            CollectionAssert.AreEqual(updateTimesSystem.Updates,
                new[]
                {
                    new TimeData(3*dt, dt),
                    new TimeData(4*dt, dt),
                });
            updateTimesSystem.Updates.Clear();

            fixedSimGroup.Update();
            CollectionAssert.AreEqual(updateTimesSystem.Updates,
                new[]
                {
                    new TimeData(5*dt, dt),
                    new TimeData(6*dt, dt),
                });
            updateTimesSystem.Updates.Clear();

            // Now that we've caught up, the next Update() should trigger only one tick.
            fixedSimGroup.Update();
            CollectionAssert.AreEqual(updateTimesSystem.Updates,
                new[]
                {
                    new TimeData(7*dt, dt),
                });
            updateTimesSystem.Updates.Clear();
            World.PopTime();
        }
    }
}
