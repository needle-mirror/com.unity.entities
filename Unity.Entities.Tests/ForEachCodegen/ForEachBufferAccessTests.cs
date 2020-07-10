using System;
using Unity.Burst;
using NUnit.Framework;
using Unity.Jobs;
#pragma warning disable 649

namespace Unity.Entities.Tests.ForEachCodegen
{
    class ForEachBufferAccessTests : ECSTestsFixture
    {
        SystemBase_TestSystem TestSystem;
        static Entity TestEntity1;
        static Entity TestEntity2;

        [SetUp]
        public void SetUp()
        {
            TestSystem = World.GetOrCreateSystem<SystemBase_TestSystem>();

            var myArch = m_Manager.CreateArchetype(
                ComponentType.ReadWrite<EcsTestDataEntity>(),
                ComponentType.ReadWrite<EcsTestData>());

            TestEntity1 = m_Manager.CreateEntity(myArch);
            TestEntity2 = m_Manager.CreateEntity(myArch);
            m_Manager.SetComponentData(TestEntity1, new EcsTestDataEntity() { value0 = 1, value1 = TestEntity2 });
            m_Manager.SetComponentData(TestEntity1, new EcsTestData() { value = 1 });
            var buffer1 = m_Manager.AddBuffer<EcsIntElement>(TestEntity1);
            buffer1.Add(1);
            m_Manager.SetComponentData(TestEntity2, new EcsTestDataEntity() { value0 = 2, value1 = TestEntity1 });
            m_Manager.SetComponentData(TestEntity2, new EcsTestData() { value = 2 });
            var buffer2 = m_Manager.AddBuffer<EcsIntElement>(TestEntity2);
            buffer2.Add(2);
        }

        internal enum ScheduleType
        {
            Run,
            Schedule,
            ScheduleParallel
        }

        public class SystemBase_TestSystem : SystemBase
        {
            protected override void OnUpdate() {}

            public void GetBufffer_GetsValueFromBuffer(Entity entity, ScheduleType scheduleType)
            {
                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        Entities.ForEach((ref EcsTestData td) => { td.value = GetBuffer<EcsIntElement>(entity)[0].Value; }).Run();
                        break;
                    case ScheduleType.Schedule:
                        Entities.ForEach((ref EcsTestData td) => { td.value = GetBuffer<EcsIntElement>(entity)[0].Value; }).Schedule();
                        break;
                    case ScheduleType.ScheduleParallel:
                        Entities.ForEach((ref EcsTestData td) => { td.value = GetBuffer<EcsIntElement>(entity)[0].Value; }).ScheduleParallel();
                        break;
                }

                Dependency.Complete();
            }

            public void GetBufferFromGetBufferFromEntity_GetsValueFromBuffer(Entity entity, ScheduleType scheduleType)
            {
                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        Entities.ForEach((ref EcsTestData td) => { td.value = GetBufferFromEntity<EcsIntElement>(true)[entity][0].Value; }).Run();
                        break;
                    case ScheduleType.Schedule:
                        Entities.ForEach((ref EcsTestData td) => { td.value = GetBufferFromEntity<EcsIntElement>(true)[entity][0].Value; }).Schedule();
                        break;
                    case ScheduleType.ScheduleParallel:
                        Entities.ForEach((ref EcsTestData td) => { td.value = GetBufferFromEntity<EcsIntElement>(true)[entity][0].Value; }).ScheduleParallel();
                        break;
                }

                Dependency.Complete();
            }

            public void AddToBufferThroughGetBufferFromEntity_AddsValueToBuffer(Entity entity, ScheduleType scheduleType)
            {
                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        Entities.ForEach((ref EcsTestDataEntity tde) =>
                        {
                            var bfe = GetBufferFromEntity<EcsIntElement>(false);
                            bfe[entity].Clear();
                            bfe[entity].Add(new EcsIntElement(){ Value = 2 });
                        }).Run();
                        break;
                    case ScheduleType.Schedule:
                        Entities.ForEach((ref EcsTestDataEntity tde) =>
                        {
                            var bfe = GetBufferFromEntity<EcsIntElement>(false);
                            bfe[entity].Clear();
                            bfe[entity].Add(new EcsIntElement(){ Value = 2 });
                        }).Schedule();
                        break;
                    case ScheduleType.ScheduleParallel:
                        Entities.ForEach((ref EcsTestDataEntity tde) =>
                        {
                            var bfe = GetBufferFromEntity<EcsIntElement>(false);
                            bfe[entity].Clear();
                            bfe[entity].Add(new EcsIntElement(){ Value = 2 });
                        }).ScheduleParallel();
                        break;
                }

                Dependency.Complete();
            }

            static int GetBufferValueByMethod(BufferFromEntity<EcsIntElement> bfe, Entity entity)
            {
                return bfe[entity][0].Value;
            }

            public void GetBufferThroughGetBufferFromEntityPassedToMethod_GetsValueFromBuffer(Entity entity)
            {
                Entities.ForEach((ref EcsTestDataEntity td) => { td.value0 = GetBufferValueByMethod(GetBufferFromEntity<EcsIntElement>(true), entity); }).Run();
            }

            static void AddToBufferByMethod(BufferFromEntity<EcsIntElement> bfe, Entity entity, int value)
            {
                bfe[entity].Clear();
                bfe[entity].Add(new EcsIntElement() { Value = value });
            }

            public void AddToBufferThroughGetBufferFromEntityPassedToMethod_AddsToBuffer(Entity entity)
            {
                Entities.ForEach((ref EcsTestDataEntity td) => { AddToBufferByMethod(GetBufferFromEntity<EcsIntElement>(false), entity, 2); }).Run();
            }

            public void MultipleGetBuffers_GetsValuesFromBuffers()
            {
                Entities.ForEach((ref EcsTestDataEntity tde) =>
                {
                    tde.value0 = GetBuffer<EcsIntElement>(tde.value1)[0].Value + GetBuffer<EcsIntElement>(tde.value1)[0].Value;
                }).Schedule();
                Dependency.Complete();
            }

            public void GetSameBufferInTwoEntitiesForEach_GetsValueFromBuffer()
            {
                Entities.ForEach((Entity entity, ref EcsTestDataEntity tde) =>
                {
                    tde.value0 += GetBuffer<EcsIntElement>(tde.value1)[0].Value;
                    tde.value0 += GetBuffer<EcsIntElement>(tde.value1)[0].Value;
                }).Schedule();
                Entities.ForEach((Entity entity, ref EcsTestDataEntity tde) =>
                {
                    tde.value0 += GetBuffer<EcsIntElement>(tde.value1)[0].Value;
                    tde.value0 += GetBuffer<EcsIntElement>(tde.value1)[0].Value;
                }).Schedule();
                Dependency.Complete();
            }

            public void GetBufferOnOtherSystemInVar_GetsValueFromBuffer(Entity entity)
            {
                var otherSystem = new SystemBase_TestSystem();
                Entities.ForEach((ref EcsTestData td) => { td.value = otherSystem.GetBuffer<EcsIntElement>(entity)[0].Value; }).Schedule();
                Dependency.Complete();
            }

            SystemBase_TestSystem otherSystemField;
            public void GetBufferOnOtherSystemInField_GetsValueFromBuffer(Entity entity)
            {
                var systemField = otherSystemField;
                Entities.ForEach((ref EcsTestData td) => { td.value = systemField.GetBuffer<EcsIntElement>(entity)[0].Value; }).Schedule();
                Dependency.Complete();
            }

            public void BufferAccessInEntitiesForEachWithNestedCaptures_BufferAccessWorks()
            {
                var outerCapture = 20;
                {
                    var innerCapture = 10;
                    Entities
                        .ForEach((Entity entity, in EcsTestDataEntity tde) =>
                    {
                        var buffer = GetBuffer<EcsIntElement>(tde.value1);
                        var val = buffer[0].Value;
                        buffer.Clear();
                        buffer.Add(new EcsIntElement() { Value = val * innerCapture * outerCapture });
                    }).Run();
                }
            }

            public void GetBufferFromEntityInEntitiesForEachWithNestedCaptures_BufferAccessWorks()
            {
                var outerCapture = 2;
                {
                    var innerCapture = 10;
                    Entities
                        .ForEach((Entity entity, in EcsTestDataEntity tde) =>
                    {
                        outerCapture = 10;

                        var bfeRead = GetBufferFromEntity<EcsIntElement>(true);
                        var val = bfeRead[tde.value1][0].Value;
                        var bfeWrite = GetBufferFromEntity<EcsIntElement>(false);
                        bfeWrite[entity].Clear();

                        bfeWrite[entity].Add(new EcsIntElement() { Value = val * innerCapture * outerCapture });
                    }).Run();
                }
            }

            public void BufferAccessMethodsExpandILPastShortBranchDistance_CausesNoExceptionsAndRuns()
            {
                Entities
                    .ForEach((Entity e, ref EcsTestData data) =>
                {
                    var a = 0;
                    if (data.value < 100)
                    {
                        if (GetBufferFromEntity<EcsIntElement>().HasComponent(e))
                            a++;
                        if (GetBufferFromEntity<EcsIntElement>().HasComponent(e))
                            a++;
                        if (GetBufferFromEntity<EcsIntElement>().HasComponent(e))
                            a++;
                        if (GetBufferFromEntity<EcsIntElement>().HasComponent(e))
                            a++;
                        if (GetBufferFromEntity<EcsIntElement>().HasComponent(e))
                            a++;
                        if (GetBufferFromEntity<EcsIntElement>().HasComponent(e))
                            a++;
                    }
                    data.value = a;
                }).Run();
            }

            public static bool StaticMethod()
            {
                return true;
            }

            public JobHandle CallsBufferAccessMethodAndExecutesStaticMethodWithBurst_CompilesAndRuns()
            {
                if (StaticMethod())
                {
                    return Entities
                        .WithBurst(FloatMode.Default, FloatPrecision.Standard, true)
                        .ForEach((in EcsTestDataEntity tde) =>
                        {
                            if (StaticMethod())
                            {
                                var buffer = GetBuffer<EcsIntElement>(tde.value1);
                                buffer.Clear();
                                buffer.Add(new EcsIntElement() { Value = 42 });
                            }
                        }).Schedule(default);
                }

                return default;
            }
        }

        [Test]
        public void GetBuffer_GetsValueFromBuffer([Values(ScheduleType.Run, ScheduleType.Schedule)] ScheduleType scheduleType)
        {
            TestSystem.GetBufffer_GetsValueFromBuffer(TestEntity2, scheduleType);
            Assert.AreEqual(2, m_Manager.GetComponentData<EcsTestData>(TestEntity1).value);
        }

        [Test]
        public void GetBuffer_Throws([Values(ScheduleType.ScheduleParallel)] ScheduleType scheduleType)
        {
            Assert.Throws<InvalidOperationException>(() => TestSystem.GetBufffer_GetsValueFromBuffer(TestEntity2, scheduleType));
        }

        [Test]
        public void GetBufferFromGetBufferFromEntity_GetsValueFromBuffer([Values] ScheduleType scheduleType)
        {
            TestSystem.GetBufferFromGetBufferFromEntity_GetsValueFromBuffer(TestEntity2, scheduleType);
            Assert.AreEqual(2, m_Manager.GetComponentData<EcsTestData>(TestEntity1).value);
        }

        [Test]
        public void AddToBufferThroughGetBufferFromEntity_AddsValueToBuffer([Values(ScheduleType.Run, ScheduleType.Schedule)] ScheduleType scheduleType)
        {
            TestSystem.AddToBufferThroughGetBufferFromEntity_AddsValueToBuffer(TestEntity1, scheduleType);
            Assert.AreEqual(2, m_Manager.GetBuffer<EcsIntElement>(TestEntity1)[0].Value);
        }

        [Test]
        public void AddToBufferThroughGetBufferFromEntity_Throws([Values(ScheduleType.ScheduleParallel)] ScheduleType scheduleType)
        {
            Assert.Throws<InvalidOperationException>(() => TestSystem.AddToBufferThroughGetBufferFromEntity_AddsValueToBuffer(TestEntity1, scheduleType));
        }

        [Test]
        public void GetBufferThroughGetBufferFromEntityPassedToMethod_GetsValueFromBuffer()
        {
            TestSystem.GetBufferThroughGetBufferFromEntityPassedToMethod_GetsValueFromBuffer(TestEntity2);
            Assert.AreEqual(2, m_Manager.GetComponentData<EcsTestDataEntity>(TestEntity1).value0);
        }

        [Test]
        public void AddToBufferThroughGetBufferFromEntityPassedToMethod_AddsToBuffer()
        {
            TestSystem.AddToBufferThroughGetBufferFromEntityPassedToMethod_AddsToBuffer(TestEntity1);
            Assert.AreEqual(2, m_Manager.GetBuffer<EcsIntElement>(TestEntity1)[0].Value);
        }

        [Test]
        public void MultipleGetBuffers_GetsValuesFromBuffers()
        {
            TestSystem.MultipleGetBuffers_GetsValuesFromBuffers();
            Assert.AreEqual(4, m_Manager.GetComponentData<EcsTestDataEntity>(TestEntity1).value0);
        }

        [Test]
        public void GetSameBufferInTwoEntitiesForEach_GetsValueFromBuffer()
        {
            TestSystem.GetSameBufferInTwoEntitiesForEach_GetsValueFromBuffer();
            Assert.AreEqual(9, m_Manager.GetComponentData<EcsTestDataEntity>(TestEntity1).value0);
        }

        [Test]
        public void GetBufferOnOtherSystemInVar_GetsValueFromBuffer()
        {
            TestSystem.GetBufferOnOtherSystemInVar_GetsValueFromBuffer(TestEntity2);
            Assert.AreEqual(2, m_Manager.GetComponentData<EcsTestData>(TestEntity1).value);
        }

        [Test]
        public void GetBufferOnOtherSystemInField_GetsValueFromBuffer()
        {
            TestSystem.GetBufferOnOtherSystemInField_GetsValueFromBuffer(TestEntity2);
            Assert.AreEqual(2, m_Manager.GetComponentData<EcsTestData>(TestEntity1).value);
        }

        [Test]
        public void BufferAccessInEntitiesForEachWithNestedCaptures_BufferAccessWorks()
        {
            TestSystem.BufferAccessInEntitiesForEachWithNestedCaptures_BufferAccessWorks();
            Assert.AreEqual(200, m_Manager.GetBuffer<EcsIntElement>(TestEntity1)[0].Value);
        }

        [Test]
        public void GetBufferFromEntityInEntitiesForEachWithNestedCaptures_BufferAccessWorks()
        {
            TestSystem.GetBufferFromEntityInEntitiesForEachWithNestedCaptures_BufferAccessWorks();
            Assert.AreEqual(200, m_Manager.GetBuffer<EcsIntElement>(TestEntity1)[0].Value);
        }

        // This test is to check that patched component access methods don't expand IL incorrectly past the limits of
        // short branch instructions.  We should call SimplifyMacros on the cloned method to ensure that we aren't
        // using short branch instructions.  If that is not happening this test will fail.
        [Test]
        public void BufferAccessMethodsExpandILPastShortBranchDistance_CausesNoExceptionsAndRuns()
        {
            TestSystem.BufferAccessMethodsExpandILPastShortBranchDistance_CausesNoExceptionsAndRuns();
            Assert.AreEqual(6, m_Manager.GetComponentData<EcsTestData>(TestEntity1).value);
        }

        [Test]
        public void CallsBufferAccessMethodAndExecutesStaticMethodWithBurst_CompilesAndRuns()
        {
            TestSystem.CallsBufferAccessMethodAndExecutesStaticMethodWithBurst_CompilesAndRuns().Complete();
            Assert.AreEqual(42, m_Manager.GetBuffer<EcsIntElement>(TestEntity1)[0].Value);
        }
    }
}
