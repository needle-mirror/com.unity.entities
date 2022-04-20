using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Transforms;
using UnityEngine;

namespace Unity.Entities.Tests.ForEachCodegen
{
    [TestFixture]
    public partial class JobEntityCodeGenTests : ECSTestsFixture
    {
        public enum ScheduleType
        {
            Run,
            Schedule,
            ScheduleParallel
        }

        const int k_EcsTestDataValue = 3;
        const int k_EcsTestData2Value = 4;
        const int k_EcsTestSharedCompValue = 5;
        const int k_DynamicBufferFirstItem = 18;
        const int k_DynamicBufferSecondItem = 19;

        MyTestSystem m_TestSystem;
        static Entity s_TestEntity;

        [SetUp]
        public void SetUp()
        {
            m_TestSystem = World.GetOrCreateSystem<MyTestSystem>();

            var entityArchetype = m_Manager.CreateArchetype(
                typeof(EcsTestData), typeof(EcsTestData2),
                typeof(EcsTestSharedComp), typeof(EcsIntElement),
                typeof(EcsTestTag));

            s_TestEntity = m_Manager.CreateEntity(entityArchetype);
            m_Manager.SetComponentData(s_TestEntity, new EcsTestData { value = k_EcsTestDataValue });
            m_Manager.SetComponentData(s_TestEntity, new EcsTestData2 { value0 = k_EcsTestData2Value });
            m_Manager.SetSharedComponentData(s_TestEntity, new EcsTestSharedComp { value = k_EcsTestSharedCompValue });

            var buffer = m_Manager.GetBuffer<EcsIntElement>(s_TestEntity);
            buffer.Add(k_DynamicBufferFirstItem);
            buffer.Add(k_DynamicBufferSecondItem);
        }

        #region AddTwoComponents

        [Test]
        public void AddTwoComponents([Values] ScheduleType scheduleType) => m_TestSystem.AddTwoComponents(scheduleType);

        #endregion

        #region AssignUniformValue

        [Test]
        public void AssignValue([Values] ScheduleType scheduleType) => m_TestSystem.AssignValue(scheduleType);

        #endregion

        #region WithTagParam

        [Test]
        public void WithTagParam([Values] ScheduleType scheduleType) => m_TestSystem.WithTagParam(scheduleType);

        #endregion

        #region WithAll

        public enum JobEntityTestsWithAll
        {
            StaticTag,
            StaticMultiple,
            DynamicTag
        }

        [Test]
        public void WithAll([Values] ScheduleType scheduleType, [Values] JobEntityTestsWithAll jobEntityTestsWithAll)
            => m_TestSystem.WithAll(scheduleType, jobEntityTestsWithAll);

        #endregion

        #region WithNone

        [Test]
        public void WithNone([Values] ScheduleType scheduleType, [Values] bool isStatic) => m_TestSystem.WithNone(scheduleType, isStatic);

        #endregion

        #region WithAny

        [Test]
        public void WithAny([Values] ScheduleType scheduleType, [Values] bool isStatic) => m_TestSystem.WithAny(scheduleType, isStatic);

        #endregion

        #region SharedComponent

        [Test]
        public void WithAllSharedComponent([Values] ScheduleType scheduleType, [Values] bool isStatic)
            => m_TestSystem.WithAllSharedComponent(scheduleType, isStatic);

        [Test]
        public void WithSharedComponentFilter([Values] ScheduleType scheduleType) => m_TestSystem.WithSharedComponentFilterDynamic(scheduleType);

        #endregion

        #region ChangeFilter

        public enum JobEntityTestsChangeFilter
        {
            StaticStructAttribute,
            StaticParameterAttribute,
            Dynamic
        }

        [Test]
        public void WithChangeFilter(
            [Values] ScheduleType scheduleTypeBeforeChange,
            [Values] ScheduleType scheduleTypeAfterChange,
            [Values] JobEntityTestsChangeFilter jobEntityTestsChangeFilter)
            => m_TestSystem.WithChangeFilter(scheduleTypeBeforeChange, scheduleTypeAfterChange, jobEntityTestsChangeFilter);

        [Test]
        public void WithFilterButNotQueryDoesntThrow([Values] ScheduleType scheduleType, [Values] bool isStatic)
            => m_TestSystem.WithFilterButNotQueryDoesntThrow(scheduleType, isStatic);

        #endregion

        #region EntityQueryOptions

        public enum JobEntityTestsEntityQueryOptions
        {
            StaticSingleAttribute,
            StaticMultipleAttribute
        }

        [Test]
        public void WithEntityQueryOptions([Values] ScheduleType scheduleType, [Values] JobEntityTestsEntityQueryOptions jobEntityTestsEntityQueryOptions)
            => m_TestSystem.WithEntityQueryOptions(scheduleType, jobEntityTestsEntityQueryOptions);

        #endregion

        #region Combinatorial

        [Test]
        public void WithJobAndThenJobEntity([Values] ScheduleType scheduleType) => m_TestSystem.WithJobAndThenJobEntity(scheduleType);

        [Test]
        public void WithTypeHandle([Values] ScheduleType scheduleType) => m_TestSystem.WithTypeHandle(scheduleType);

        [Test]
        public void MultipleInNestedUsing([Values] ScheduleType scheduleTypeAssign, [Values] ScheduleType scheduleTypeIncrement)
            => m_TestSystem.MultipleInNestedUsing(scheduleTypeAssign, scheduleTypeIncrement);

        #endregion

        #region Buffer

        [Test]
        public void AddToDynamicBuffer([Values] ScheduleType scheduleType) => m_TestSystem.AddToDynamicBuffer(scheduleType);

        [Test]
        public void ModifyDynamicBuffer([Values] ScheduleType scheduleType) => m_TestSystem.MultiplyAllDynamicBufferValues(scheduleType);

        [Test]
        public void SumAllOfBufferIntoEcsTestData([Values] ScheduleType scheduleType) => m_TestSystem.SumAllOfBufferIntoEcsTestData(scheduleType);

        #endregion

        #region EntityInQueryIndex

        [Test]
        public void EntityInQueryIndex([Values] ScheduleType scheduleType) => m_TestSystem.EntityInQueryIndex(scheduleType);

        #endregion

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        #region ManagedComponents

        [Test]
        public void UnityEngineComponent() => m_TestSystem.UnityEngineComponent();

        [Test]
        public void UnityEngineObject() => m_TestSystem.UnityEngineGameObject();

        [Test]
        public void UnityEngineScriptableObject() => m_TestSystem.UnityEngineScriptableObject();

        [Test]
        public void ManyManagedComponents() => m_TestSystem.ManyManagedComponents();
        #endregion
#endif

        #region Safety
        [Test]
        public void JobDebuggerSafetyThrows([Values] ScheduleType scheduleType) => m_TestSystem.JobDebuggerSafetyThrows(scheduleType);

        #endregion

        partial class MyTestSystem : SystemBase
        {
            protected override void OnCreate() {}

            #region AddTwoComponents
            partial struct AddTwoComponentsJob : IJobEntity
            {
                public void Execute(ref EcsTestData e1, in EcsTestData2 e2) => e1.value += e2.value0;
            }

            public void AddTwoComponents(ScheduleType scheduleType)
            {
                var job = new AddTwoComponentsJob();

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                Assert.AreEqual(k_EcsTestDataValue + k_EcsTestData2Value, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);

            }
            #endregion

            #region AssignUniformValue

            partial struct AssignValueJob : IJobEntity
            {
                public int Value;
                public void Execute(ref EcsTestData e1, in EcsTestData2 e2) => e1.value = Value;
            }

            public void AssignValue(ScheduleType scheduleType)
            {
                const int valueToAssign = 7;

                var job = new AssignValueJob { Value = valueToAssign };
                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                Assert.AreEqual(valueToAssign, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            #endregion

            #region WithTagParam

            partial struct WithTagParamJob : IJobEntity
            {
                public int Value;
                public void Execute(ref EcsTestData e1, in EcsTestTag testTag) => e1.value = Value;
            }

            public void WithTagParam(ScheduleType scheduleType)
            {
                const int valueToAssign = 5;
                var job = new WithTagParamJob {Value = valueToAssign};

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                Assert.AreEqual(valueToAssign, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            #endregion

            #region WithAll

            public void WithAll(ScheduleType scheduleType, JobEntityTestsWithAll jobEntityTestsWithAll)
            {
                switch (jobEntityTestsWithAll)
                {
                    case JobEntityTestsWithAll.StaticTag:
                        WithAllStaticTag(scheduleType);
                        break;
                    case JobEntityTestsWithAll.StaticMultiple:
                        WithAllStaticMultiple(scheduleType);
                        break;
                    case JobEntityTestsWithAll.DynamicTag:
                        WithAllDynamic(scheduleType);
                        break;
                }
            }

            [WithAll(typeof(EcsTestSharedComp))]
            [WithAll(typeof(EcsTestTag), typeof(EcsTestData2))]
            partial struct WithAllStaticMultipleJob : IJobEntity
            {
                public int Value;
                public void Execute(ref EcsTestData e1) => e1.value = Value;
            }

            void WithAllStaticMultiple(ScheduleType scheduleType)
            {
                const int valueToAssign = 5;

                var entityNotMatchingQuery = EntityManager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));

                var job = new WithAllStaticTagJob {Value = valueToAssign};

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }
                Assert.AreEqual(valueToAssign, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
                Assert.AreEqual(default(EcsTestData), EntityManager.GetComponentData<EcsTestData>(entityNotMatchingQuery));
            }

            [WithAll(typeof(EcsTestTag))]
            partial struct WithAllStaticTagJob : IJobEntity
            {
                public int Value;
                public void Execute(ref EcsTestData e1, in EcsTestData2 e2) => e1.value = Value;
            }

            void WithAllStaticTag(ScheduleType scheduleType)
            {
                const int valueToAssign = 5;
                var job = new WithAllStaticTagJob {Value = valueToAssign};
                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }
                Assert.AreEqual(valueToAssign, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            partial struct WithAllDynamicJob : IJobEntity
            {
                public int Value;
                public void Execute(ref EcsTestData e1, in EcsTestData2 e2) => e1.value = Value;
            }

            void WithAllDynamic(ScheduleType scheduleType)
            {
                var query = GetEntityQuery(typeof(EcsTestTag), typeof(EcsTestData), typeof(EcsTestData2));

                const int valueToAssign = 5;
                var job = new WithAllDynamicJob {Value = valueToAssign};
                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run(query);
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule(query).Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel(query).Complete();
                        break;
                }
                Assert.AreEqual(valueToAssign, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }
            #endregion

            #region WithNone

            public void WithNone(ScheduleType scheduleType, bool isStatic)
            {
                if(isStatic)
                    WithNoneStatic(scheduleType);
                else
                    WithNoneDynamic(scheduleType);
            }

            [WithNone(typeof(EcsTestData2))]
            partial struct WithNoneStaticJob : IJobEntity
            {
                public int IncrementBy;
                public void Execute(ref EcsTestData e1) => e1.value += IncrementBy;
            }

            void WithNoneStatic(ScheduleType scheduleType)
            {
                var job = new WithNoneStaticJob { IncrementBy = 1 };

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                Assert.AreEqual(k_EcsTestDataValue, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value); // Nothing changed
            }

            partial struct WithNoneDynamicJob : IJobEntity
            {
                public int IncrementBy;
                public void Execute(ref EcsTestData e1) => e1.value += IncrementBy;
            }

            void WithNoneDynamic(ScheduleType scheduleType)
            {
                var query = GetEntityQuery(new EntityQueryDesc
                {
                    All = new ComponentType[]{typeof(EcsTestData)},
                    None = new ComponentType[]{typeof(EcsTestData2)}
                });
                var job = new WithNoneDynamicJob { IncrementBy = 1 };

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run(query);
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule(query).Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel(query).Complete();
                        break;
                }

                Assert.AreEqual(k_EcsTestDataValue, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value); // Nothing changed
            }

            #endregion

            #region WithAny

            public void WithAny(ScheduleType scheduleType, bool isStatic)
            {
                if (isStatic)
                    WithAnyStatic(scheduleType);
                else
                    WithAnyDynamic(scheduleType);
            }

            [WithAny(typeof(EcsTestData3))]
            partial struct WithAnyStaticJob : IJobEntity
            {
                public int IncrementBy;
                public void Execute(ref EcsTestData e1) => e1.value += IncrementBy;
            }

            void WithAnyStatic(ScheduleType scheduleType)
            {
                var job = new WithAnyStaticJob { IncrementBy = 1 };


                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                Assert.AreEqual(k_EcsTestDataValue, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value); // Nothing changed
            }

            partial struct WithAnyDynamicJob : IJobEntity
            {
                public int IncrementBy;
                public void Execute(ref EcsTestData e1) => e1.value += IncrementBy;
            }

            void WithAnyDynamic(ScheduleType scheduleType)
            {
                var query = GetEntityQuery(new EntityQueryDesc
                {
                    All = new ComponentType[]{typeof(EcsTestData)},
                    Any = new ComponentType[]{typeof(EcsTestData3)}
                });
                var job = new WithAnyDynamicJob { IncrementBy = 1 };

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run(query);
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule(query).Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel(query).Complete();
                        break;
                }

                Assert.AreEqual(k_EcsTestDataValue, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value); // Nothing changed
            }

            #endregion

            #region SharedComponent

            public void WithAllSharedComponent(ScheduleType scheduleType, bool isStatic)
            {
                if (isStatic)
                    WithAllSharedComponentStatic(scheduleType);
                else
                    WithAllSharedComponentDynamic(scheduleType);
            }

            partial struct WithAllSharedComponentDynamicJob : IJobEntity
            {
                public int IncrementBy;
                public void Execute(ref EcsTestData e1) => e1.value += IncrementBy;
            }

            void WithAllSharedComponentDynamic(ScheduleType scheduleType)
            {
                const int incrementBy = 1;

                var query = GetEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp));
                var job = new WithAllSharedComponentDynamicJob { IncrementBy = incrementBy };

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run(query);
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule(query).Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel(query).Complete();
                        break;
                }

                Assert.AreEqual(k_EcsTestDataValue + incrementBy, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            [WithAll(typeof(EcsTestSharedComp))]
            partial struct WithAllSharedComponentStaticJob : IJobEntity
            {
                public int IncrementBy;
                public void Execute(ref EcsTestData e1) => e1.value += IncrementBy;
            }

            void WithAllSharedComponentStatic(ScheduleType scheduleType)
            {
                const int incrementBy = 1;

                var job = new WithAllSharedComponentStaticJob {IncrementBy = incrementBy};

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                Assert.AreEqual(k_EcsTestDataValue + incrementBy, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            partial struct WithSharedComponentFilterDynamicJob : IJobEntity
            {
                public int IncrementBy;
                public void Execute(ref EcsTestData e1) => e1.value += IncrementBy;
            }

            public void WithSharedComponentFilterDynamic(ScheduleType scheduleType)
            {
                const int incrementBy = 1;

                var query = GetEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp));
                query.SetSharedComponentFilter(new EcsTestSharedComp(k_EcsTestSharedCompValue));

                var job = new WithSharedComponentFilterDynamicJob { IncrementBy = incrementBy };

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                Assert.AreEqual(k_EcsTestDataValue + incrementBy, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            #endregion

            #region ChangeFilter

            public void WithChangeFilter(ScheduleType scheduleTypeBeforeChange, ScheduleType scheduleTypeAfterChange, JobEntityTestsChangeFilter jobEntityTestsChangeFilter)
            {
                switch (jobEntityTestsChangeFilter)
                {
                    case JobEntityTestsChangeFilter.StaticStructAttribute:
                        WithChangeFilterStaticStructAttribute(scheduleTypeBeforeChange, scheduleTypeAfterChange);
                        break;

                    case JobEntityTestsChangeFilter.StaticParameterAttribute:
                        WithChangeFilterStaticParameterAttribute(scheduleTypeBeforeChange, scheduleTypeAfterChange);
                        break;

                    case JobEntityTestsChangeFilter.Dynamic:
                        WithChangeFilterDynamic(scheduleTypeBeforeChange, scheduleTypeAfterChange);
                        break;
                }
            }

            [WithChangeFilter(typeof(EcsTestData))]
            partial struct WithChangeFilterStaticStructAttributeJob : IJobEntity
            {
                public int IncrementBy;
                public void Execute(ref EcsTestData e1) => e1.value += IncrementBy;
            }

            void WithChangeFilterStaticStructAttribute(ScheduleType scheduleTypeBeforeChange, ScheduleType scheduleTypeAfterChange)
            {
                const int incrementBy = 1;

                var job = new WithChangeFilterStaticStructAttributeJob { IncrementBy = incrementBy };

                switch (scheduleTypeBeforeChange)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }
                AfterUpdateVersioning();
                BeforeUpdateVersioning();
                // Should not run
                switch (scheduleTypeAfterChange)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                Assert.AreEqual(k_EcsTestDataValue + incrementBy, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            partial struct WithChangeFilterStaticParameterAttributeJob : IJobEntity
            {
                public int IncrementBy;
                public void Execute([WithChangeFilter] ref EcsTestData e1) => e1.value += IncrementBy;
            }

            void WithChangeFilterStaticParameterAttribute(ScheduleType scheduleTypeBeforeChange, ScheduleType scheduleTypeAfterChange)
            {
                const int incrementBy = 1;

                var job = new WithChangeFilterStaticParameterAttributeJob { IncrementBy = incrementBy };

                switch (scheduleTypeBeforeChange)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }
                AfterUpdateVersioning();
                BeforeUpdateVersioning();
                // Should not run
                switch (scheduleTypeAfterChange)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                Assert.AreEqual(k_EcsTestDataValue + incrementBy, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            partial struct WithChangeFilterDynamicJob : IJobEntity
            {
                public int IncrementBy;
                public void Execute(ref EcsTestData e1) => e1.value += IncrementBy;
            }

            void WithChangeFilterDynamic(ScheduleType scheduleTypeBeforeChange, ScheduleType scheduleTypeAfterChange)
            {
                const int incrementBy = 1;

                var query = GetEntityQuery(typeof(EcsTestData));
                query.SetChangedVersionFilter(typeof(EcsTestData));
                var job = new WithChangeFilterDynamicJob { IncrementBy = incrementBy };

                switch (scheduleTypeBeforeChange)
                {
                    case ScheduleType.Run:
                        job.Run(query);
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule(query).Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel(query).Complete();
                        break;
                }

                AfterUpdateVersioning();
                BeforeUpdateVersioning();

                switch (scheduleTypeAfterChange)
                {
                    case ScheduleType.Run:
                        job.Run(query);
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule(query).Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel(query).Complete();
                        break;
                }

                Assert.AreEqual(k_EcsTestDataValue + incrementBy, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            public void WithFilterButNotQueryDoesntThrow(ScheduleType scheduleType, bool isStatic)
            {
                if (isStatic)
                    WithFilterButNotQueryStatic(scheduleType);
                else
                    WithFilterButNotQueryDynamic(scheduleType);
            }

            [WithChangeFilter(typeof(Translation))]
            partial struct WithFilterButNotQueryStaticJob : IJobEntity { public void Execute(Entity _) {} }
            void WithFilterButNotQueryStatic(ScheduleType scheduleType)
            {
                Assert.DoesNotThrow(() =>
                {
                    var job = new WithFilterButNotQueryStaticJob();
                    switch (scheduleType)
                    {
                        case ScheduleType.Run:
                            job.Run();
                            break;
                        case ScheduleType.Schedule:
                            job.Schedule().Complete();
                            break;
                        case ScheduleType.ScheduleParallel:
                            job.ScheduleParallel().Complete();
                            break;
                    }
                });
            }

            partial struct WithFilterButNotQueryDynamicJob : IJobEntity { public void Execute(Entity _) {} }

            void WithFilterButNotQueryDynamic(ScheduleType scheduleType)
            {
                Assert.DoesNotThrow(() =>
                {
                    var query = GetEntityQuery(new EntityQueryDesc {All = new ComponentType[] {typeof(Translation)}});
                    query.SetChangedVersionFilter(typeof(Translation));
                    var job = new WithFilterButNotQueryDynamicJob();

                    switch (scheduleType)
                    {
                        case ScheduleType.Run:
                            job.Run(query);
                            break;
                        case ScheduleType.Schedule:
                            job.Schedule(query).Complete();
                            break;
                        case ScheduleType.ScheduleParallel:
                            job.ScheduleParallel(query).Complete();
                            break;
                    }
                });
            }

            #endregion

            #region EntityQueryOptions

            [WriteGroup(typeof(EcsTestData))]
            struct EcsTestDataWriteGroup : IComponentData {}

            public void WithEntityQueryOptions(ScheduleType scheduleType, JobEntityTestsEntityQueryOptions jobEntityTestsEntityQueryOptions)
            {
                switch (jobEntityTestsEntityQueryOptions)
                {
                    case JobEntityTestsEntityQueryOptions.StaticSingleAttribute:
                        WithEntityQueryOptionsStaticSingleAttribute(scheduleType);
                        break;
                    case JobEntityTestsEntityQueryOptions.StaticMultipleAttribute:
                        WithEntityQueryOptionsStaticMultipleAttribute(scheduleType);
                        break;
                }
            }

            [WithEntityQueryOptions(EntityQueryOptions.IncludeDisabled | EntityQueryOptions.FilterWriteGroup, EntityQueryOptions.IncludePrefab)]
            partial struct WithEntityQueryOptionsStaticSingleAttributeJob : IJobEntity
            {
                public int AssignValue;
                public void Execute(ref EcsTestData testData) => testData.value = AssignValue;
            }

            void WithEntityQueryOptionsStaticSingleAttribute(ScheduleType scheduleType)
            {
                const int assignValue = 2;

                EntityManager.AddComponents(s_TestEntity, new ComponentTypes(typeof(Disabled), typeof(Prefab)));
                var writeGroupEntity = EntityManager.CreateEntity(typeof(EcsTestData), typeof(EcsTestDataWriteGroup));

                var job = new WithEntityQueryOptionsStaticSingleAttributeJob {AssignValue = assignValue};

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                Assert.AreEqual(default(EcsTestData),EntityManager.GetComponentData<EcsTestData>(writeGroupEntity));
                Assert.AreEqual(assignValue,EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            [WithEntityQueryOptions(EntityQueryOptions.IncludePrefab)]
            [WithEntityQueryOptions(EntityQueryOptions.IncludeDisabled | EntityQueryOptions.FilterWriteGroup)]
            partial struct WithEntityQueryOptionsStaticMultipleAttributeJob : IJobEntity
            {
                public int AssignValue;
                public void Execute(ref EcsTestData testData) => testData.value = AssignValue;
            }

            void WithEntityQueryOptionsStaticMultipleAttribute(ScheduleType scheduleType)
            {
                const int assignValue = 2;

                EntityManager.AddComponents(s_TestEntity, new ComponentTypes(typeof(Disabled), typeof(Prefab)));
                var writeGroupEntity = EntityManager.CreateEntity(typeof(EcsTestData), typeof(EcsTestDataWriteGroup));

                var job = new WithEntityQueryOptionsStaticMultipleAttributeJob {AssignValue = assignValue};

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                Assert.AreEqual(default(EcsTestData),EntityManager.GetComponentData<EcsTestData>(writeGroupEntity));
                Assert.AreEqual(assignValue,EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            #endregion

            #region Combinatorial
            partial struct WithJobAndThenJobEntityJob : IJobEntity
            {
                public int IncrementBy;
                public void Execute(ref EcsTestData e1) => e1.value += IncrementBy;
            }

            public void WithJobAndThenJobEntity(ScheduleType scheduleType)
            {
                var incrementBy = 3;
                const int overwrittenIncrement = 1;

                Job.WithCode(() => { incrementBy = overwrittenIncrement; }).Run();

                var job = new WithJobAndThenJobEntityJob { IncrementBy = incrementBy };

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                Assert.AreEqual(k_EcsTestDataValue + overwrittenIncrement, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            partial struct ComponentTypeHandleJob : IJobEntity
            {
                [ReadOnly] public ComponentTypeHandle<EcsTestData2> testDataTypeHandle;
                public void Execute(ref EcsTestData data)
                {
                    data.value = (int) testDataTypeHandle.GlobalSystemVersion;
                }
            }

            public void WithTypeHandle(ScheduleType scheduleType)
            {
                var job = new ComponentTypeHandleJob
                {
                    testDataTypeHandle = GetComponentTypeHandle<EcsTestData2>(true)
                };

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                Assert.AreEqual((int)EntityManager.GlobalSystemVersion, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            partial struct MultipleInNestedUsingAssignJob : IJobEntity
            {
                public int Value;
                public void Execute(ref EcsTestData e1, in EcsTestData2 e2) => e1.value = Value;
            }

            partial struct MultipleInNestedUsingIncrementJob : IJobEntity
            {
                public int IncrementBy;
                public void Execute(ref EcsTestData e1) => e1.value += IncrementBy;
            }

            public void MultipleInNestedUsing(ScheduleType scheduleTypeAssign, ScheduleType scheduleTypeIncrement)
            {
                const int valueToAssign = 5;
                const int incrementBy = 5;

                using (var refStartPos = CollectionHelper.CreateNativeArray<int>(10, World.UpdateAllocator.ToAllocator))
                {
                    using (var refEndPos = new NativeArray<int>(10, Allocator.TempJob))
                    {
                        var assignJob = new MultipleInNestedUsingAssignJob { Value = refEndPos[0] + refStartPos[0] + valueToAssign };
                        var incrementJob = new MultipleInNestedUsingIncrementJob { IncrementBy = incrementBy };

                        switch (scheduleTypeAssign)
                        {
                            case ScheduleType.Run:
                                assignJob.Run();
                                break;
                            case ScheduleType.Schedule:
                                assignJob.Schedule().Complete();
                                break;
                            case ScheduleType.ScheduleParallel:
                                assignJob.ScheduleParallel().Complete();
                                break;
                        }

                        switch (scheduleTypeIncrement)
                        {
                            case ScheduleType.Run:
                                incrementJob.Run();
                                break;
                            case ScheduleType.Schedule:
                                incrementJob.Schedule().Complete();
                                break;
                            case ScheduleType.ScheduleParallel:
                                incrementJob.ScheduleParallel().Complete();
                                break;
                        }
                    }
                }

                Assert.AreEqual(valueToAssign + incrementBy, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            #endregion

            #region Buffer

            partial struct AddItemToDynamicBuffer : IJobEntity
            {
                public int AddThisToBuffer;
                public void Execute(DynamicBuffer<EcsIntElement> dynamicBuffer) => dynamicBuffer.Add(AddThisToBuffer);
            }

            public void AddToDynamicBuffer(ScheduleType scheduleType)
            {
                const int value = 4;

                var job = new AddItemToDynamicBuffer { AddThisToBuffer = value };
                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                var buffer = EntityManager.GetBuffer<EcsIntElement>(s_TestEntity);

                Assert.AreEqual(3, buffer.Length);
                CollectionAssert.AreEqual(new[] {k_DynamicBufferFirstItem, k_DynamicBufferSecondItem, value}, buffer.Reinterpret<int>().AsNativeArray());
            }

            partial struct MultiplyAllDynamicBufferValuesJob : IJobEntity
            {
                public int Multiplier;
                public void Execute(ref EcsTestData e1, DynamicBuffer<EcsIntElement> dynamicBuffer)
                {
                    for (var i = 0; i < dynamicBuffer.Length; ++i)
                        dynamicBuffer[i] = dynamicBuffer[i].Value * Multiplier;
                }
            }

            public void MultiplyAllDynamicBufferValues(ScheduleType scheduleType)
            {
                const int multiplier = 2;

                var job = new MultiplyAllDynamicBufferValuesJob { Multiplier = multiplier };

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                var buffer = EntityManager.GetBuffer<EcsIntElement>(s_TestEntity);
                CollectionAssert.AreEqual(new[] {k_DynamicBufferFirstItem * multiplier, k_DynamicBufferSecondItem * multiplier}, buffer.Reinterpret<int>().AsNativeArray());
            }

            static int SumBufferElements(DynamicBuffer<EcsIntElement> buf)
            {
                var total = 0;
                for (var i = 0; i < buf.Length; i++)
                    total += buf[i].Value;
                return total;
            }

            partial struct SumAllDynamicBufferValues : IJobEntity
            {
                public void Execute(ref EcsTestData e1, DynamicBuffer<EcsIntElement> dynamicBuffer) => e1.value = SumBufferElements(dynamicBuffer);
            }

            public void SumAllOfBufferIntoEcsTestData(ScheduleType scheduleType)
            {
                var job = new SumAllDynamicBufferValues();

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                Assert.AreEqual(k_DynamicBufferFirstItem + k_DynamicBufferSecondItem, EntityManager.GetComponentData<EcsTestData>(s_TestEntity).value);
            }

            #endregion

            #region EntityInQueryIndex

            partial struct CompareEntityQueryIndex : IJobEntity
            {
                [NativeDisableParallelForRestriction] public NativeArray<int> Successes;
                [NativeSetThreadIndex] int m_NativeThreadIndex;
                public void Execute([EntityInQueryIndex]int entityInQueryIndex, in EcsTestData value) => Successes[m_NativeThreadIndex] += entityInQueryIndex == value.value ? 1 : 0;
            }

            public void EntityInQueryIndex(ScheduleType scheduleType)
            {
                var entityArchetype = EntityManager.CreateArchetype(typeof(EcsTestData));

                using var entities = EntityManager.CreateEntity(entityArchetype, 10, Allocator.Temp);
                for (var index = 0; index < entities.Length; index++)
                    EntityManager.SetComponentData(entities[index], new EcsTestData {value = index+1});

                var job = new CompareEntityQueryIndex{Successes = new NativeArray<int>(JobsUtility.MaxJobThreadCount, Allocator.TempJob)};

                switch (scheduleType)
                {
                    case ScheduleType.Run:
                        job.Run();
                        break;
                    case ScheduleType.Schedule:
                        job.Schedule().Complete();
                        break;
                    case ScheduleType.ScheduleParallel:
                        job.ScheduleParallel().Complete();
                        break;
                }

                // No LINQ as this test should also work for DOTS Runtime
                var sum = 0;
                for (var i = 0; i < job.Successes.Length; i++)
                    sum += job.Successes[i];

                Assert.AreEqual(10,sum);
                job.Successes.Dispose();
            }

            #endregion

#if !UNITY_DISABLE_MANAGED_COMPONENTS
            #region ManagedComponents

            public partial struct UnityEngineComponentJob : IJobEntity
            {
                void Execute(Transform transform) => transform.position = Vector3.up;
            }

            public void UnityEngineComponent()
            {
                var go = new GameObject("Original");
                var ghostEntity = EntityManager.CreateEntity();
                EntityManager.AddComponentObject(ghostEntity, go.transform);
                new UnityEngineComponentJob().Run();
                Assert.AreEqual(go.transform.position, Vector3.up);
            }

            public partial struct UnityEngineGameObjectJob : IJobEntity
            {
                void Execute(GameObject go) => go.name = "Changed";
            }

            public void UnityEngineGameObject()
            {
                var go = new GameObject("Original");
                var ghostEntity = EntityManager.CreateEntity();
                EntityManager.AddComponentObject(ghostEntity, go);
                new UnityEngineGameObjectJob().Run();
                Assert.AreEqual(go.name, "Changed");
            }

            public class TestScriptableObject : ScriptableObject
            {
                public int value;
            }

            public partial struct UnityEngineScriptableObjectJob : IJobEntity
            {
                void Execute(TestScriptableObject so) => so.value = 1;
            }

            public void UnityEngineScriptableObject()
            {
                var so = ScriptableObject.CreateInstance<TestScriptableObject>();
                var ghostEntity = EntityManager.CreateEntity();
                EntityManager.AddComponentObject(ghostEntity, so);
                new UnityEngineScriptableObjectJob().Run();
                Assert.AreEqual(so.value, 1);
            }

            public partial struct ManyManagedComponentsJob : IJobEntity
            {
                public NativeReference<int> Count;

                void Execute(EcsTestManagedComponent t0, EcsTestManagedComponent2 t1, EcsTestManagedComponent3 t2, EcsTestManagedComponent4 t3)
                {
                    Assert.AreEqual("SomeString", t0.value);
                    Assert.AreEqual("SomeString2", t1.value2);
                    Assert.AreEqual("SomeString3", t2.value3);
                    Assert.AreEqual("SomeString4", t3.value4);

                    Count.Value++;
                }
            }

            public void ManyManagedComponents()
            {
                var entity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(entity, new EcsTestManagedComponent { value = "SomeString" });
                EntityManager.AddComponentData(entity, new EcsTestManagedComponent2 { value2 = "SomeString2" });
                EntityManager.AddComponentData(entity, new EcsTestManagedComponent3 { value3 = "SomeString3" });
                EntityManager.AddComponentData(entity, new EcsTestManagedComponent4 { value4 = "SomeString4" });

                var job = new ManyManagedComponentsJob{Count = new NativeReference<int>(0,Allocator.TempJob)};
                job.Run();
                Assert.AreEqual(1, job.Count.Value);
                job.Count.Dispose();
            }

            #endregion
#endif

            #region Safety

            partial struct JobDebuggerSafetyThrowsJob : IJobEntity
            {
                public int IncrementBy;
                public void Execute(ref EcsTestData e1) => e1.value += IncrementBy;
            }

            public void JobDebuggerSafetyThrows(ScheduleType scheduleType)
            {
                var jobHandle = new JobDebuggerSafetyThrowsJob{IncrementBy = 1}.Schedule(Dependency);

                Assert.Throws<InvalidOperationException>(() =>
                {
                    switch (scheduleType)
                    {
                        case ScheduleType.Run:
                            new JobDebuggerSafetyThrowsJob {IncrementBy = 1}.Run();
                            break;
                        case ScheduleType.Schedule:
                            new JobDebuggerSafetyThrowsJob {IncrementBy = 1}.Schedule();
                            break;
                        case ScheduleType.ScheduleParallel:
                            new JobDebuggerSafetyThrowsJob {IncrementBy = 1}.ScheduleParallel();
                            break;
                    }
                });
                Dependency.Complete();
                jobHandle.Complete();
            }

            void JobDebuggerSafetyThrowsDoJob(ScheduleType scheduleType)
            {

            }

            #endregion

            protected override void OnUpdate() { }
        }
    }
}

// Todo: Re-enable this once Generic passed IJE works as well as NonSystem scheduling
// internal static class JobEntityTestExtensions
// {
//     internal static void ScheduleFromScheduleType<T>(this T job, JobEntityCodeGenTests.ScheduleType scheduleType) where T : struct, IJobEntity
//     {
//         switch (scheduleType)
//         {
//             case JobEntityCodeGenTests.ScheduleType.Run:
//                 job.Run();
//                 break;
//             case JobEntityCodeGenTests.ScheduleType.Schedule:
//                 job.Schedule().Complete();
//                 break;
//             case JobEntityCodeGenTests.ScheduleType.ScheduleParallel:
//                 job.ScheduleParallel().Complete();
//                 break;
//         }
//     }
//
//     internal static void ScheduleFromScheduleType<T>(this T job, EntityQuery query, JobEntityCodeGenTests.ScheduleType scheduleType) where T : struct, IJobEntity
//     {
//         switch (scheduleType)
//         {
//             case JobEntityCodeGenTests.ScheduleType.Run:
//                 job.Run(query);
//                 break;
//             case JobEntityCodeGenTests.ScheduleType.Schedule:
//                 job.Schedule(query).Complete();
//                 break;
//             case JobEntityCodeGenTests.ScheduleType.ScheduleParallel:
//                 job.ScheduleParallel(query).Complete();
//                 break;
//         }
//     }
// }
