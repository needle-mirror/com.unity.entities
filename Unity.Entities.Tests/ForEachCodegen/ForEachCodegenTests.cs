using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.CodeGeneratedJobForEach;
using Unity.Jobs;
using Unity.Transforms;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
#if !UNITY_PORTABLE_TEST_RUNNER
using System.Linq;
#endif

namespace Unity.Entities.Tests.ForEachCodegen
{
    [TestFixture]
    public partial class ForEachCodegenTests : ECSTestsFixture
    {
        MyTestSystem TestSystem;
        Entity TestEntity;
        MyImplicitProfilingDoesntClearStackSystem ImplicitProfilingDoesntClearStackSystem;

        [SetUp]
        public void SetUp()
        {
            TestSystem = World.GetOrCreateSystem<MyTestSystem>();
            ImplicitProfilingDoesntClearStackSystem = World.GetOrCreateSystem<MyImplicitProfilingDoesntClearStackSystem>();

            var myArch = m_Manager.CreateArchetype(
                ComponentType.ReadWrite<EcsTestData>(),
                ComponentType.ReadWrite<EcsTestData2>(),
                ComponentType.ReadWrite<EcsTestSharedComp>(),
                ComponentType.ReadWrite<EcsTestSharedComp2>(),
                ComponentType.ReadWrite<EcsIntElement>(),
                ComponentType.ReadWrite<EcsTestTag>());

            TestEntity = m_Manager.CreateEntity(myArch);
            m_Manager.SetComponentData(TestEntity, new EcsTestData() { value = 3});
            m_Manager.SetComponentData(TestEntity, new EcsTestData2() { value0 = 4});
            var buffer = m_Manager.GetBuffer<EcsIntElement>(TestEntity);
            buffer.Add(new EcsIntElement {Value = 18});
            buffer.Add(new EcsIntElement {Value = 19});
            m_Manager.SetSharedComponentData(TestEntity, new EcsTestSharedComp() { value = 5 });
            m_Manager.SetSharedComponentData(TestEntity, new EcsTestSharedComp2() { value0 = 11, value1 = 13 });
        }

        [Test]
        public void SimplestCase()
        {
            TestSystem.SimplestCase();
            Assert.AreEqual(7, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void MatchingMethodInDerivedSystem()
        {
            TestSystem.MatchingMethodInDerivedSystem();
            Assert.AreEqual(7, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WithConstant()
        {
            TestSystem.WithConstant();
            Assert.AreEqual(7, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WithTagComponent()
        {
            TestSystem.WithTagComponent();
            Assert.AreEqual(5, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WithTagComponentReadOnly()
        {
            TestSystem.WithTagComponentReadOnly();
            Assert.AreEqual(5, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WithAllSharedComponent()
        {
            TestSystem.WithAllSharedComponentData();
            Assert.AreEqual(4, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WithSharedComponentFilter()
        {
            TestSystem.WithSharedComponentFilter();
            Assert.AreEqual(4, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WithSharedComponentFilterTwoParameters()
        {
            TestSystem.WithSharedComponentFilterTwoParameters();
            Assert.AreEqual(4, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WithSharedComponentFilterTwoParametersNoMatch()
        {
            TestSystem.WithSharedComponentFilterTwoParametersNoMatch();
            Assert.AreEqual(3, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WithSharedComponentFilterCapturingDirectly()
        {
            TestSystem.WithSharedComponentFilterCapturingDirectly();
            Assert.AreEqual(4, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WithSharedComponentFilterCapturingInExpression()
        {
            TestSystem.WithSharedComponentFilterCapturingInExpression();
            Assert.AreEqual(4, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WithChangeFilter()
        {
            TestSystem.WithChangeFilter();
            Assert.AreEqual(4, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WithJobAndThenEntitiesForEach()
        {
            TestSystem.WithJobAndThenEntitiesForEach();
            Assert.AreEqual(6, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void StoresEntityQueryInField()
        {
            var entityCountFromQuery = TestSystem.m_StoredQuery.CalculateEntityCount();
            var entityCountFromJob = TestSystem.StoresEntityQueryInField();
            Assert.AreEqual(entityCountFromQuery, entityCountFromJob);
        }

        [Test]
        public void AddToDynamicBuffer()
        {
            TestSystem.AddToDynamicBuffer();
            var buffer = m_Manager.GetBuffer<EcsIntElement>(TestEntity);
            Assert.AreEqual(3, buffer.Length);
            CollectionAssert.AreEqual(new[] {18, 19, 4}, buffer.Reinterpret<int>().AsNativeArray());
        }

        [Test]
        public void ModifyDynamicBuffer()
        {
            TestSystem.ModifyDynamicBuffer();
            var buffer = m_Manager.GetBuffer<EcsIntElement>(TestEntity);
            CollectionAssert.AreEqual(new[] {18 * 2, 19 * 2}, buffer.Reinterpret<int>().AsNativeArray());
        }

        [Test]
        public void IterateExistingDynamicBufferReadOnly()
        {
            TestSystem.IterateExistingDynamicBufferReadOnly();
            Assert.AreEqual(18 + 19, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void IterateExistingDynamicBuffer_NoModifier()
        {
            TestSystem.IterateExistingDynamicBuffer_NoModifier();
            Assert.AreEqual(18 + 19 + 20, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WithNone()
        {
            TestSystem.WithNone();
            AssertNothingChanged();
        }

        [Test]
        public void WithAny_DoesntExecute_OnEntityWithoutThatComponent()
        {
            TestSystem.WithAny_DoesntExecute_OnEntityWithoutThatComponent();
            AssertNothingChanged();
        }

        [Test]
        public void ExecuteLocalFunctionThatCapturesTest()
        {
            TestSystem.ExecuteLocalFunctionInLambdaThatCaptures();
            Assert.AreEqual(9, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void FirstCapturingSecondNotCapturingTest()
        {
            TestSystem.FirstCapturingSecondNotCapturing();
            Assert.AreEqual(9, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void FirstNotCapturingThenCapturingTest()
        {
            TestSystem.FirstNotCapturingThenCapturing();
            Assert.AreEqual(9, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void ImplicitProfilingDoesntClearStack()
        {
            ImplicitProfilingDoesntClearStackSystem.ImplicitProfilingDoesntClearStack();
            Assert.AreEqual(true, ImplicitProfilingDoesntClearStackSystem.WasEntityMatched());
        }

        struct EntityInQueryValue : IComponentData { public int Value; }

        [Test]
        public void UseEntityInQueryIndex()
        {
            var myArch = m_Manager.CreateArchetype(ComponentType.ReadWrite<EntityInQueryValue>(), ComponentType.ReadWrite<EcsTestSharedComp>());
            using (var entities = TestSystem.EntityManager.CreateEntity(myArch, 10, Allocator.Temp))
            {
                var val = 0;
                foreach (var entity in entities)
                {
                    TestSystem.EntityManager.SetComponentData(entity, new EntityInQueryValue() {Value = val});
                    TestSystem.EntityManager.SetSharedComponentData(entity, new EcsTestSharedComp() {value = val});
                    val++;
                }
            }
            Assert.IsTrue(TestSystem.UseEntityInQueryIndex());
        }

        [Test]
        public void InvokeMethodWhoseLocalsLeakTest()
        {
            TestSystem.InvokeMethodWhoseLocalsLeak();
            Assert.AreEqual(9, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void RunInsideLoopCapturingLoopConditionTest()
        {
            TestSystem.RunInsideLoopCapturingLoopCondition();
            Assert.AreEqual(103, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void WriteBackToLocalTest()
        {
            Assert.AreEqual(11, TestSystem.WriteBackToLocal());
        }

        internal struct MySharedComponentData : ISharedComponentData
        {
            public int Value;
        }

        [Test]
        public void CaptureAndOperateOnReferenceTypeTest()
        {
            Assert.AreEqual("Hello there Sailor!", TestSystem.CaptureAndOperateOnReferenceType());
        }

#if !UNITY_PORTABLE_TEST_RUNNER
// https://unity3d.atlassian.net/browse/DOTSR-1432
// OrderBy isn't supported in DOTS-Runtime
        [Test]
        public void IterateSharedComponentDataTest()
        {
            var entity1 = TestSystem.EntityManager.CreateEntity(ComponentType.ReadWrite<MySharedComponentData>());
            var entity2 = TestSystem.EntityManager.CreateEntity(ComponentType.ReadWrite<MySharedComponentData>());

            TestSystem.EntityManager.SetSharedComponentData(entity1, new MySharedComponentData() {Value = 1});
            TestSystem.EntityManager.SetSharedComponentData(entity2, new MySharedComponentData() {Value = 2});

            var observedDatas = TestSystem.IterateSharedComponentData();

            Assert.AreEqual(2, observedDatas.Count);

            var sorted = observedDatas.OrderBy(o => o.Value).ToArray();
            Assert.AreEqual(1, sorted[0].Value);
            Assert.AreEqual(2, sorted[1].Value);
        }

#endif

        public struct MyBufferElementData : IBufferElementData
        {
            public int Value;
        }

        [Test]
        public void WithBufferElementAsQueryFilterTest()
        {
            var entity1 = TestSystem.EntityManager.CreateEntity(ComponentType.ReadWrite<MyBufferElementData>());
            var entity2 = TestSystem.EntityManager.CreateEntity();

            var observedEntities = TestSystem.BufferElementAsQueryFilter();

            CollectionAssert.Contains(observedEntities, entity2);
            CollectionAssert.DoesNotContain(observedEntities, entity1);
        }

        [Test]
        public void InvokeInstanceMethodWhileCapturingNothingTest()
        {
            var result = TestSystem.InvokeInstanceMethodWhileCapturingNothing();
            Assert.AreEqual(124, result);
        }

        [Test]
        public void CaptureFieldAndLocalNoBurstAndRunTest()
        {
            var result = TestSystem.CaptureFieldAndLocalNoBurstAndRun();
            Assert.AreEqual(124, result);
        }

        [Test]
        public void CaptureFromMultipleScopesAndRunTest()
        {
            TestSystem.CaptureFromMultipleScopesAndRun();
        }

        [Test]
        public void CaptureFromMultipleScopesAndScheduleTest()
        {
            TestSystem.CaptureFromMultipleScopesAndSchedule();
            Assert.AreEqual(6, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void CaptureInnerAndOuterStructAndRunTest()
        {
            TestSystem.CaptureInnerAndOuterStructAndRun();
            Assert.AreEqual(9, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void CaptureInnerAndOuterValueAndScheduleTest()
        {
            TestSystem.CaptureInnerAndOuterValueAndSchedule();
            Assert.AreEqual(6, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void MultipleEntitiesForEachInNestedDisplayClassesTest()
        {
            TestSystem.MultipleEntitiesForEachInNestedDisplayClasses();
            Assert.AreEqual(4, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

        [Test]
        public void MultipleCapturingEntitiesForEachInNestedUsingStatementsTest()
        {
            TestSystem.MultipleCapturingEntitiesForEachInNestedUsingStatements();
            Assert.AreEqual(10, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void ManyManagedComponents()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponentData(entity, new EcsTestManagedComponent() { value = "SomeString" });
            m_Manager.AddComponentData(entity, new EcsTestManagedComponent2() { value2 = "SomeString2" });
            m_Manager.AddComponentData(entity, new EcsTestManagedComponent3() { value3 = "SomeString3" });
            m_Manager.AddComponentData(entity, new EcsTestManagedComponent4() { value4 = "SomeString4" });
            TestSystem.Many_ManagedComponents();
        }

#endif

#if !UNITY_DOTSRUNTIME
        [Test]
        public void UseUnityEngineComponent()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponent<Camera>(entity);
            m_Manager.SetComponentObject(entity, ComponentType.ReadWrite<Camera>(), Camera.main);
            (Camera reportedCamera, Entity reportedEntity) = TestSystem.IterateEntitiesWithCameraComponent();
            Assert.AreEqual(Camera.main, reportedCamera);
            Assert.AreEqual(entity, reportedEntity);
        }

        [Test]
        public void UnityEngineObjectAsLambdaParam()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponent<ParticleSystem>(entity);
            TestSystem.UnityEngineObjectAsLambdaParam();
        }

        [Test]
        public void UnityEngineObjectAsWithParam()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.AddComponent<ParticleSystem>(entity);
            TestSystem.UnityEngineObjectAsWithParam();
        }
#endif

        [Test]
        public void JobDebuggerSafetyThrowsInRun()
        {
            var jobHandle = TestSystem.ScheduleEcsTestData();
            Assert.Throws<InvalidOperationException>(() => { TestSystem.RunEcsTestData(); });
            jobHandle.Complete();
        }

        [Test]
        public void JobDebuggerSafetyThrowsInSchedule()
        {
            var jobHandle = TestSystem.ScheduleEcsTestData();
            Assert.Throws<InvalidOperationException>(() => { TestSystem.ScheduleEcsTestData(); });
            jobHandle.Complete();
        }

        [Test]
        public void ForEachWithCustomDelegateTypeWithMoreThan8Parameters()
        {
            TestSystem.RunForEachWithCustomDelegateTypeWithMoreThan8Parameters();
        }

        [Test]
        public void ResolveDuplicateFieldsInEntityQuery()
        {
            using (var builder = new EntityQueryDescBuilder(Allocator.Temp))
            {
                builder.AddAll(ComponentType.ReadWrite<EcsTestData>());
                builder.FinalizeQuery();
                Assert.IsTrue(TestSystem.m_ResolvedQuery.CompareQuery(builder));
            }
        }

        [Test]
        public void RunWithFilterButNotQueryDoesNotThrow()
        {
            Assert.DoesNotThrow(TestSystem.RunWithFilterButNotQuery);
        }

        [Test]
        public void RunWithNativeArrayForEachCreatingTryFinallyBlockDoesNotThrow()
        {
            Assert.DoesNotThrow(TestSystem.RunWithNativeArrayForEachCreatingTryFinallyBlock);
        }

        [Test]
        public void RunWithUsingCreatingTryFinallyBlockDoesNotThrow()
        {
            Assert.DoesNotThrow(TestSystem.RunWithUsingCreatingTryFinallyBlock);
        }

        [Test]
        public void RunWithStructuralChangesAndTagComponentByRef()
        {
            TestSystem.RunWithStructuralChangesAndTagComponentByRef();
        }

        [Test]
        public void RunWithUsingEntityCommandBuffer()
        {
            TestSystem.RunWithUsingEntityCommandBuffer();
        }

        [Test]
        public void DoNotExecuteCodeRemovedWithDirective()
        {
            TestSystem.DoNotExecuteCodeRemovedWithDirective();
        }

        [Test]
        public void DoExecuteCodeInElseDirective()
        {
            TestSystem.DoExecuteCodeInElseDirective();
        }

        [Test]
        public void ExecuteLocalFunction()
        {
            TestSystem.ExecuteLocalFunction();
        }

        [Test]
        public void ExecuteLocalFunctionThatCaptures()
        {
            TestSystem.ExecuteLocalFunctionThatCaptures();
        }

        [Test]
        public void ExecuteLocalFunctionWithSameFunctionNameInClass()
        {
            TestSystem.ExecuteLocalFunctionWithSameFunctionNameInClass();
        }

        [Test]
        public void ExecuteLocalFunctionAlsoInvokedInMethod()
        {
            TestSystem.ExecuteLocalFunctionAlsoInvokedInMethod();
        }

        [Test]
        public void ExecuteLocalFunctionThatAccessesEntity()
        {
            TestSystem.ExecuteLocalFunctionThatAccessesEntity();
        }

        [Test]
        public void UseOfConstantFloatValuesInLambda()
        {
            TestSystem.UseOfConstantFloatValuesInLambda();
        }

        [Test]
        public void RunWithNoParametersCountEntities()
        {
            Assert.AreEqual(1, TestSystem.RunWithNoParametersCountEntities());
        }

        [Test]
        public void RunWithNoParametersAndNoQueryCountEntities()
        {
            Assert.AreEqual(1, TestSystem.RunWithNoParametersAndNoQueryCountEntities());
        }

        [Test]
        public void RunWithNoParametersCountEntitiesNoBurst()
        {
            Assert.AreEqual(1, TestSystem.RunWithNoParametersCountEntitiesNoBurst());
        }

        [Test]
        public void RunWithNoParametersAndNoQueryCountEntitiesNoBurst()
        {
            Assert.AreEqual(1, TestSystem.RunWithNoParametersAndNoQueryCountEntitiesNoBurst());
        }

        [Test]
        public void MethodsWithSameName()
        {
            TestSystem.MethodsWithSameName();

            TestSystem.MethodsWithSameName(0);

            var val = 0;
            TestSystem.MethodsWithSameName(ref val);

            int val1 = 1, val2 = 2;
            val = TestSystem.MethodsWithSameName(in val1, ref val2, out var val3);
            val = val3;

            MyTestSystem.GenericTypeWithTwoTypeParams<int, int> genericType = default;
            TestSystem.MethodsWithSameName(genericType);
        }

        [Test]
        public void MethodsWithNullableParameter()
        {
            int? blah = 3;
            TestSystem.MethodsWithNullableParameter(blah);
        }

        [Test]
        public void SystemWithinSystem()
        {
            var system = World.GetOrCreateSystem<MyTestSystem.SomeInnerSystem>();
            Assert.DoesNotThrow(() => system.SomeMethodThatShouldRun());
        }

        partial class ParentTestSystem  : SystemBase
        {
            protected override void OnUpdate() { }

            public virtual void MatchingMethodInDerivedSystem()
            {
                Entities.ForEach((ref EcsTestData e1, in EcsTestData2 e2) => { e1.value += 2 * e2.value0;}).Schedule();
                Dependency.Complete();
            }
        }

        partial class ParentTestSystemWithoutEntitiesForEach : ParentTestSystem
        {
            protected override void OnUpdate() { }
        }

        partial class MyTestSystem : ParentTestSystemWithoutEntitiesForEach
        {
            public EntityQuery m_StoredQuery;
            public EntityQuery m_ResolvedQuery;

            public void SimplestCase()
            {
                Entities.ForEach((ref EcsTestData e1, in EcsTestData2 e2) => { e1.value += e2.value0;}).Schedule();
                Dependency.Complete();
            }

            public override void MatchingMethodInDerivedSystem()
            {
                Entities.ForEach((ref EcsTestData e1, in EcsTestData2 e2) => { e1.value += e2.value0;}).Schedule();
                Dependency.Complete();
            }

            public void WithConstant()
            {
                const int constVal = 7;
                Entities.ForEach((ref EcsTestData e1, in EcsTestData2 e2) => { e1.value = constVal;}).Schedule();
                Dependency.Complete();
            }

            public void WithTagComponent()
            {
                Entities.ForEach((ref EcsTestData e1, ref EcsTestTag e2) => { e1.value = 5;}).Schedule();
                Dependency.Complete();
            }

            public void WithTagComponentReadOnly()
            {
                Entities.ForEach((ref EcsTestData e1, in EcsTestTag e2) => { e1.value = 5;}).Schedule();
                Dependency.Complete();
            }

            public void WithNone()
            {
                int multiplier = 1;
                Entities
                    .WithNone<EcsTestData2>()
                    .ForEach((ref EcsTestData e1) => { e1.value += multiplier;})
                    .Schedule();
                Dependency.Complete();
            }

            public void WithAny_DoesntExecute_OnEntityWithoutThatComponent()
            {
                int multiplier = 1;
                Entities
                    .WithAny<EcsTestData3>()
                    .ForEach((ref EcsTestData e1) => { e1.value += multiplier;})
                    .Schedule();
                Dependency.Complete();
            }

            public void WithAllSharedComponentData()
            {
                int multiplier = 1;
                Entities
                    .WithAll<EcsTestSharedComp>()
                    .ForEach((ref EcsTestData e1) => { e1.value += multiplier;})
                    .Schedule();
                Dependency.Complete();
            }

            public void WithSharedComponentFilter()
            {
                int multiplier = 1;
                Entities
                    .WithSharedComponentFilter(new EcsTestSharedComp() { value = 5 })
                    .ForEach((ref EcsTestData e1) => { e1.value += multiplier;})
                    .Schedule();
                Dependency.Complete();
            }

            public void WithSharedComponentFilterTwoParameters()
            {
                int multiplier = 1;
                Entities
                    .WithSharedComponentFilter(new EcsTestSharedComp() { value = 5 }, new EcsTestSharedComp2() { value0 = 11, value1 = 13 })
                    .ForEach((ref EcsTestData e1) => { e1.value += multiplier;})
                    .Schedule();
                Dependency.Complete();
            }

            public void WithSharedComponentFilterTwoParametersNoMatch()
            {
                int multiplier = 1;
                Entities
                    .WithSharedComponentFilter(new EcsTestSharedComp() { value = 5 }, new EcsTestSharedComp2() { value0 = 11, value1 = 0 })
                    .ForEach((ref EcsTestData e1) => { e1.value += multiplier;})
                    .Schedule();
                Dependency.Complete();
            }

            public void WithSharedComponentFilterCapturingDirectly()
            {
                const int multiplier = 1;
                const int value = 5;
                var sharedComponent = new EcsTestSharedComp() {value = value};

                Entities
                    .WithSharedComponentFilter(sharedComponent)
                    .ForEach((ref EcsTestData e1) => { e1.value += multiplier;})
                    .Schedule();
                Dependency.Complete();
            }

            public void WithSharedComponentFilterCapturingInExpression()
            {
                const int multiplier = 1;
                const int value = 5;

                Entities
                    .WithSharedComponentFilter(new EcsTestSharedComp() { value = value })
                    .ForEach((ref EcsTestData e1) => { e1.value += multiplier;})
                    .Schedule();
                Dependency.Complete();
            }

            public void WithChangeFilter()
            {
                int multiplier = 1;

                // GlobalSystemVersion starts at 1
                // 3 + 1 = 4, bump version number to 1
                Entities
                    .WithChangeFilter<EcsTestData>()
                    .ForEach((ref EcsTestData e1) => { e1.value += multiplier;})
                    .Run();

                AfterUpdateVersioning();  // sets last version to current version (1)
                BeforeUpdateVersioning(); // increments version and sets all queries to last version (1)

                // Shouldn't run, version matches system version (1)
                Entities
                    .WithChangeFilter<EcsTestData>()
                    .ForEach((ref EcsTestData e1) => { e1.value += multiplier;})
                    .Run();
            }

            public void WithJobAndThenEntitiesForEach()
            {
                int multiplier = 1;

                Job.WithCode(() => { multiplier = 3; }).Run();

                Entities
                    .ForEach((ref EcsTestData e1) => { e1.value += multiplier;})
                    .Schedule();
                Dependency.Complete();
            }

            public int StoresEntityQueryInField()
            {
                int count = 0;

                Entities
                    .WithStoreEntityQueryInField(ref m_StoredQuery)
                    .ForEach((ref EcsTestData e1) => { count++; })
                    .Run();

                return count;
            }

            public void AddToDynamicBuffer()
            {
                Entities
                    .ForEach((ref EcsTestData e1, ref DynamicBuffer<EcsIntElement> buf) =>
                    {
                        buf.Add(4);
                    })
                    .Schedule();
                Dependency.Complete();
            }

            public void ModifyDynamicBuffer()
            {
                Entities
                    .ForEach((ref EcsTestData e1, ref DynamicBuffer<EcsIntElement> buf) =>
                    {
                        for (int i = 0; i < buf.Length; ++i) buf[i] = buf[i].Value * 2;
                    })
                    .Schedule();
                Dependency.Complete();
            }

            public void IterateExistingDynamicBufferReadOnly()
            {
                Entities
                    .ForEach((ref EcsTestData e1, in DynamicBuffer<EcsIntElement> buf) =>
                    {
                        e1.value = SumOfBufferElements(buf);
                    })
                    .Schedule();
                Dependency.Complete();
            }

            public void IterateExistingDynamicBuffer_NoModifier()
            {
                Entities
                    .ForEach((DynamicBuffer<EcsIntElement> buf, ref EcsTestData e1) =>
                    {
                        buf.Add(20);
                        e1.value = SumOfBufferElements(buf);
                    })
                    .Schedule();
                Dependency.Complete();
            }

            static int SumOfBufferElements(DynamicBuffer<EcsIntElement> buf)
            {
                int total = 0;
                for (int i = 0; i != buf.Length; i++)
                    total += buf[i].Value;
                return total;
            }

            public void CaptureFromMultipleScopesAndRun()
            {
                int scope1 = 1;
                {
                    int scope2 = 2;
                    {
                        int scope3 = 3;
                        Entities
                        .ForEach((ref EcsTestData e1) =>
                        {
                            var sum = scope1 + scope2 + scope3;
                            scope1 = sum;
                            scope2 = -sum;
                            scope3 = 321;
                        })
                        .Run();

                        Assert.AreEqual(-6, scope2);
                        Assert.AreEqual(6, scope1);
                        Assert.AreEqual(321, scope3);
                    }
                }
            }

            public void CaptureFromMultipleScopesAndSchedule()
            {
                int scope1 = 1;
                {
                    int scope2 = 2;
                    {
                        int scope3 = 3;
                        Entities
                            .ForEach((ref EcsTestData e1) =>
                            {
                                e1.value = scope1 + scope2 + scope3;
                            })
                            .Schedule();
                    }
                }
                Dependency.Complete();
            }

            public void ExecuteLocalFunctionInLambdaThatCaptures()
            {
                int capture_from_outer_scope = 1;
                Entities
                    .ForEach((ref EcsTestData e1) =>
                    {
                        int capture_from_delegate_scope = 8;
                        int MyLocalFunction()
                        {
                            return capture_from_outer_scope + capture_from_delegate_scope;
                        }
                        e1.value = MyLocalFunction();
                    })
                    .Schedule();
                Dependency.Complete();
            }

            public void FirstCapturingSecondNotCapturing()
            {
                int capturedValue = 3;
                var job1 = Entities.ForEach((ref EcsTestData e1) => e1.value = capturedValue).Schedule(Dependency);
                Dependency = Entities.ForEach((ref EcsTestData e1) => e1.value *= 3).Schedule(job1);
                Dependency.Complete();
            }

            public void FirstNotCapturingThenCapturing()
            {
                int capturedValue = 3;
                var job1 = Entities.ForEach((ref EcsTestData e1) => e1.value = 3).Schedule(Dependency);
                Dependency = Entities.ForEach((ref EcsTestData e1) => e1.value *= capturedValue).Schedule(job1);
                Dependency.Complete();
            }

            public void InvokeMethodWhoseLocalsLeak()
            {
                var normalDelegate = MethodWhoseLocalsLeak();
                Assert.AreEqual(8, normalDelegate());
            }

            public Func<int> MethodWhoseLocalsLeak()
            {
                int capturedValue = 3;
                Entities.ForEach((ref EcsTestData e1) => e1.value *= capturedValue).Schedule(default).Complete();
                int someOtherValue = 8;
                return () => someOtherValue;
            }

            public bool UseEntityInQueryIndex()
            {
                var success = true;

                Entities.WithoutBurst().ForEach((int entityInQueryIndex, in EntityInQueryValue eiqv) =>
                {
                    if (eiqv.Value != entityInQueryIndex)
                        success = false;
                }).Run();

                return success;
            }

            public JobHandle ScheduleEcsTestData()
            {
                int multiplier = 1;
                return Entities
                    .ForEach((ref EcsTestData e1) => { e1.value += multiplier;})
                    .Schedule(default);
            }

            public void RunEcsTestData()
            {
                int multiplier = 1;
                Entities
                    .ForEach((ref EcsTestData e1) => { e1.value += multiplier; })
                    .Run();
            }

            public void RunInsideLoopCapturingLoopCondition()
            {
                int variable = 10;
                for (int i = 0; i != variable; i++)
                {
                    Entities
                        .ForEach((ref EcsTestData e1) => { e1.value += variable; })
                        .Run();
                }
            }

            public int WriteBackToLocal()
            {
                int variable = 10;
                Entities.ForEach((ref EcsTestData e1) => { variable++; }).Run();
                return variable;
            }

            public string CaptureAndOperateOnReferenceType()
            {
                string myString = "Hello";
                Entities.WithoutBurst().ForEach((ref EcsTestData e1) => myString += " there Sailor!").Run();
                return myString;
            }

            public List<MySharedComponentData> IterateSharedComponentData()
            {
                var result = new List<MySharedComponentData>();

                Entities.WithoutBurst().ForEach((MySharedComponentData data) => { result.Add(data); }).Run();
                return result;
            }

            public List<Entity> BufferElementAsQueryFilter()
            {
                var result = new List<Entity>();
                Entities
                    .WithoutBurst()
                    .WithNone<MyBufferElementData>()
                    .ForEach((Entity e) => { result.Add(e); })
                    .Run();
                return result;
            }

            void MyInstanceMethod()
            {
                myField++;
            }

            private int myField;
            public int InvokeInstanceMethodWhileCapturingNothing()
            {
                myField = 123;
                Entities.WithoutBurst().ForEach((Entity e) => { MyInstanceMethod(); }).Run();
                return myField;
            }

            private int mySpecialField = 123;
            public int CaptureFieldAndLocalNoBurstAndRun()
            {
                int localValue = 1;
                Entities.WithoutBurst().ForEach((Entity e) => mySpecialField += localValue).Run();
                return mySpecialField;
            }

            struct CaptureStruct
            {
                public int Value;
            }
            public void CaptureInnerAndOuterStructAndRun()
            {
                var outter = new CaptureStruct() { Value = 1 };
                {
                    var inner = new CaptureStruct() {Value = 3};
                    outter.Value = 2;
                    int multiplier = 1;
                    Entities
                        .ForEach((ref EcsTestData e1) =>
                        {
                            e1.value += multiplier + outter.Value + inner.Value;
                        })
                        .Run();
                }
            }

            public void CaptureInnerAndOuterValueAndSchedule()
            {
                int outerCapure = 3;
                {
                    int innerCapture = 2;
                    Entities
                        .ForEach((ref EcsTestData testData) =>
                        {
                            testData.value = outerCapure * innerCapture;
                        })
                        .Schedule();
                }
                Dependency.Complete();
            }

            public void MultipleEntitiesForEachInNestedDisplayClasses()
            {
                var cap1 = 1;
                {
                    var cap2 = 2;
                    Entities.ForEach((ref EcsTestData testData) => {
                        testData.value = cap1 + cap2;
                    }).Run();

                    Entities.ForEach((ref EcsTestData testData) =>
                    {
                        testData.value += cap1;
                    }).Run();
                }
            }

            public void MultipleCapturingEntitiesForEachInNestedUsingStatements()
            {
                JobHandle jobHandle = default;
                var time = 5;

                using (var refStartPos = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(10, ref World.UpdateAllocator))
                {
                    using (var refEndPos = CollectionHelper.CreateNativeArray<int, RewindableAllocator>(10, ref World.UpdateAllocator))
                    {
                        jobHandle = Entities
                            .ForEach((ref EcsTestData testData) =>
                                testData.value = refEndPos[0] + refStartPos[0] + time)
                            .Schedule(jobHandle);

                        jobHandle = Entities
                            .ForEach((ref EcsTestData testData) =>
                                testData.value += time)
                            .Schedule(jobHandle);
                        jobHandle.Complete();
                    }
                }
            }



#if !UNITY_DISABLE_MANAGED_COMPONENTS
            public void Many_ManagedComponents()
            {
                var counter = 0;
                Entities.WithoutBurst().ForEach(
                    (EcsTestManagedComponent t0, EcsTestManagedComponent2 t1, EcsTestManagedComponent3 t2,
                        in EcsTestManagedComponent4 t3) =>
                    {
                        Assert.AreEqual("SomeString", t0.value);
                        Assert.AreEqual("SomeString2", t1.value2);
                        Assert.AreEqual("SomeString3", t2.value3);
                        Assert.AreEqual("SomeString4", t3.value4);
                        counter++;
                    }).Run();
                Assert.AreEqual(1, counter);
            }

#endif

#if !UNITY_DOTSRUNTIME
            public (Camera, Entity) IterateEntitiesWithCameraComponent()
            {
                (Camera camera, Entity entity)result = default;
                Entities.WithoutBurst().ForEach((Camera camera, Entity e) =>
                {
                    result.camera = camera;
                    result.entity = e;
                }).Run();
                return result;
            }

            public void UnityEngineObjectAsLambdaParam()
            {
                var count = 0;
                Entities.WithoutBurst().ForEach((ParticleSystem ps) =>
                {
                    count++;
                }).Run();
                Assert.AreEqual(1, count);
            }

            public void UnityEngineObjectAsWithParam()
            {
                var count = 0;
                Entities.WithoutBurst().WithAll<ParticleSystem>().ForEach((Entity _) =>
                {
                    count++;
                }).Run();
                Assert.AreEqual(1, count);
            }
#endif
            public void RunForEachWithCustomDelegateTypeWithMoreThan8Parameters()
            {
                int grabbedData = -1;
                Entities.ForEach((Entity e0, Entity e1, Entity e2, Entity e3, Entity e4, Entity e5, Entity e6, Entity e7, Entity e8, Entity e9, Entity e10, in EcsTestData data) =>
                {
                    grabbedData = data.value;
                }).Run();
                Assert.AreEqual(3,  grabbedData);
            }

            // Not invoked, only used to store query in field with WithStoreEntityQueryInField
            public void ResolveDuplicateFieldsInEntityQuery()
            {
                Entities
                    .WithAll<EcsTestData>()
                    .WithStoreEntityQueryInField(ref m_ResolvedQuery)
                    .ForEach((ref EcsTestData e1) => { })
                    .Run();
            }

            public void RunWithFilterButNotQuery()
            {
                Entities.WithChangeFilter<Translation>().ForEach((Entity entity) => { }).Run();
            }

            public int RunWithNoParametersCountEntities()
            {
                int counter = 0;
                Entities.WithAll<EcsTestData>().ForEach(() =>
                {
                    counter++;
                }).Run();
                return counter;
            }

            public int RunWithNoParametersAndNoQueryCountEntities()
            {
                int counter = 0;
                Entities.ForEach(() =>
                {
                    counter++;
                }).Run();
                return counter;
            }

            public int RunWithNoParametersCountEntitiesNoBurst()
            {
                int counter = 0;
                Entities.WithoutBurst().WithAll<EcsTestData>().ForEach(() =>
                {
                    counter++;
                }).Run();
                return counter;
            }

            public int RunWithNoParametersAndNoQueryCountEntitiesNoBurst()
            {
                int counter = 0;
                Entities.WithoutBurst().ForEach(() =>
                {
                    counter++;
                }).Run();
                return counter;
            }

            public void RunWithNativeArrayForEachCreatingTryFinallyBlock()
            {
                var array = new NativeArray<bool>(10, Allocator.Temp);
                int counter = 0;

                Entities.WithoutBurst().ForEach((ref EcsTestData data) =>
                {
                    foreach (bool item in array)
                        if (!item) counter++;
                }).Run();
                array.Dispose();

                Assert.AreEqual(10, counter);
            }

            public void RunWithUsingCreatingTryFinallyBlock()
            {
                int counter = 0;

                Entities.WithoutBurst().ForEach((ref EcsTestData data) =>
                {
                    using (var array = new NativeArray<bool>(10, Allocator.Temp))
                    {
                        foreach (bool item in array)
                            if (!item) counter++;
                    }
                }).Run();

                Assert.AreEqual(10, counter);
            }

            public void RunWithStructuralChangesAndTagComponentByRef()
            {
                var value = false;
                Entities
                    .WithStructuralChanges()
                    .ForEach((ref EcsTestTag data) =>
                    {
                        value = true;
                    })
                    .Run();

                Assert.IsTrue(value);
            }

            public void RunWithUsingEntityCommandBuffer()
            {
                var startEntities = EntityManager.GetAllEntities().Length;

                using (var outerCommandBuffer = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
                {
                    using (var innerCommandBuffer = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
                    {
                        Entities
                            .ForEach((Entity entity) =>
                            {
                                innerCommandBuffer.CreateEntity();
                                outerCommandBuffer.CreateEntity();
                            }).Run();

                        innerCommandBuffer.Playback(EntityManager);
                    }
                    outerCommandBuffer.Playback(EntityManager);
                }

                Assert.AreEqual(startEntities * 3, EntityManager.GetAllEntities().Length);
            }

            public void DoNotExecuteCodeRemovedWithDirective()
            {
                var someBool = true;
                Entities
                    .ForEach((Entity entity) =>
                    {
#if SOME_UNDEFINED_SYMBOL
                        someBool = false;
#endif
                    }).Run();

                Assert.IsTrue(someBool);
            }

            public void DoExecuteCodeInElseDirective()
            {
                var someBool = false;
                Entities
                    .WithStructuralChanges()
                    .ForEach((Entity entity) =>
                    {
#if SOME_UNDEFINED_SYMBOL
#else
                        someBool = true;
#endif
                    }).Run();

                Assert.IsTrue(someBool);
            }

            public void ExecuteLocalFunction()
            {
                var value = 0;
                int LocalFunction() { return 3; }

                Entities.ForEach((Entity entity) =>
                {
                    value = LocalFunction();
                }).Run();

                Assert.AreEqual(3, value);
            }

            public void ExecuteLocalFunctionThatCaptures()
            {
                var value = 0;
                var capturedValue = 3;
                int LocalFunction() { return capturedValue; }

                Entities.ForEach((Entity entity) =>
                {
                    value = LocalFunction();
                }).Run();

                Assert.AreEqual(3, value);
            }

            int LocalFunctionWithSameNameInClass() { return 5; }
            public void ExecuteLocalFunctionWithSameFunctionNameInClass()
            {
                var value = 0;
                int LocalFunctionWithSameNameInClass() { return 3; }

                Entities.WithoutBurst().ForEach((Entity entity) =>
                {
                    value = LocalFunctionWithSameNameInClass() * this.LocalFunctionWithSameNameInClass();
                }).Run();

                Assert.AreEqual(15, value);
            }

            public void ExecuteLocalFunctionAlsoInvokedInMethod()
            {
                var value = 0;
                int LocalFunction() { return 3; }

                Entities.ForEach((Entity entity) => { value = LocalFunction(); }).Run();

                Assert.AreEqual(9, value * LocalFunction());
            }

            public void ExecuteLocalFunctionThatAccessesEntity()
            {
                var value = 0;
                var dataFromEntity = GetComponentDataFromEntity<EcsTestData>();
                int SomeLocalFunc(Entity entity) { return dataFromEntity[entity].value; }

                Entities.ForEach((Entity entity, in EcsTestData data) => {
                    value = SomeLocalFunc(entity);
                }).Run();

                Assert.AreEqual(3, value);
            }

            static bool TakesFloat(float val) => (val > 0.0);
            public void UseOfConstantFloatValuesInLambda()
            {
                var testVal = false;
                const float testFloat = 1.0f;
                Entities.ForEach((Entity e) =>
                {
                    const float deltaAngleEpsilon = math.PI / 32;
                    if (math.abs(testFloat) > deltaAngleEpsilon)
                    {
                        const float constVal = 1.0f;
                        testVal = TakesFloat(constVal);
                    }
                }).Run();

                Assert.AreEqual(true, testVal);
            }

            public void MethodsWithSameName()
            {
                var value = 0;
                Entities.ForEach((Entity entity, in EcsTestData data) => { value = 3; }).Run();
                Assert.AreEqual(3, value);
            }

            public void MethodsWithSameName(int someVal)
            {
                var value = 0;
                Entities.ForEach((Entity entity, in EcsTestData data) => { value = 3; }).Run();
                Assert.AreEqual(3, value);
            }

            public void MethodsWithSameName(ref int someVal)
            {
                var value = 0;
                Entities.ForEach((Entity entity, in EcsTestData data) => { value = 3; }).Run();
                Assert.AreEqual(3, value);
            }

            public int MethodsWithSameName(in int someVal1, ref int someVal2, out int someVal3)
            {
                var value = someVal1 * someVal2;
                Entities.ForEach((Entity entity, in EcsTestData data) => { value = 3; }).Run();
                someVal3 = value;
                Assert.AreEqual(3, value);
                return value;
            }

            public struct GenericTypeWithTwoTypeParams<TKey, TValue> { }
            public void MethodsWithSameName(GenericTypeWithTwoTypeParams<int, int> genericType)
            {
                var value = 0;
                Entities.ForEach((Entity entity, in EcsTestData data) => { value = 3; }).Run();
                Assert.AreEqual(3, value);
            }

            public void MethodsWithNullableParameter(int? someVal)
            {
                var value = 0;
                Entities.ForEach((Entity entity, in EcsTestData data) => { value = 3; }).Run();
                Assert.AreEqual(3, value);
            }

            protected override void OnUpdate() { }

            public partial class SomeInnerSystem : SystemBase
            {
                public void SomeMethodThatShouldRun()
                {
                    Entities.ForEach(()=>{}).Run();
                }

                protected override void OnUpdate() => throw new NotImplementedException();
            }
        }

        // test for a stack underflow introduced by an ILPP introduced try/finally clearing the
        // runtime stack - http://fogbugz.unity3d.com/f/cases/1287367/
        // this test is very codegen specific.
        struct MyImplicitProfilingDoesntClearStackTestTag : IComponentData
        {
        };

        class MyImplicitProfilingDoesntClearStackFoo
        {
            public Entity entity;
        };

        partial class MyImplicitProfilingDoesntClearStackSystem : ParentTestSystemWithoutEntitiesForEach
        {
            public MyImplicitProfilingDoesntClearStackFoo m_Foo = null;
            public bool m_WasEntityMatched = false;

            protected override void OnCreate()
            {
                m_WasEntityMatched = false;
                m_Foo = new MyImplicitProfilingDoesntClearStackFoo();
            }

            protected override void OnUpdate() { }

            public void ImplicitProfilingDoesntClearStack()
            {
                MyImplicitProfilingDoesntClearStackFoo testComponent = m_Foo;
                Entity matchedEntity = Entity.Null;

                Entities.WithAll<MyImplicitProfilingDoesntClearStackTestTag>().ForEach((Entity entity) =>
                {
                    matchedEntity = entity;
                }).Run();

                testComponent.entity = matchedEntity;
                m_WasEntityMatched = true;
            }

            public bool WasEntityMatched() { return m_WasEntityMatched; }
        }

        // !!! Move this test over to LambdaJobPostProcessorSourceGenErrorTests as part of the source generator error handling PR
        /*
        [Test]
        public void SystemWithMultipleUsesOfEntitiesTest()
        {
            AssertProducesNoError(typeof(SystemWithMultipleUsesOfEntities));
        }
        */
        partial class SystemWithMultipleUsesOfEntities : SystemBase
        {
            new NativeArray<Entity> Entities = new NativeArray<Entity>();

            struct JobWithEntityField : IJob
            {
                [ReadOnly] NativeArray<Entity> Entities;
                public JobWithEntityField(NativeArray<Entity> entities) { Entities = entities; }

                public void Execute()
                {
                    foreach (var entity in Entities) { Debug.Log(entity); }
                }
            }

            void Test()
            {
                var value = Unity.Entities.Serialization.SerializeUtility.CurrentFileFormatVersion;
                Debug.Log(value);
                foreach (var entity in Entities) { Debug.Log(entity); }
            }

            protected override void OnUpdate() { }
        }

        void AssertNothingChanged() => Assert.AreEqual(3, m_Manager.GetComponentData<EcsTestData>(TestEntity).value);
    }
}


static class BringYourOwnDelegate
{
    [EntitiesForEachCompatible]
    public delegate void CustomForEachDelegate<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T0 t0, T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, T8 t8, T9 t9, T10 t10, in T11 t11);

    public static TDescription ForEach<TDescription, T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(this TDescription description, CustomForEachDelegate<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> codeToRun)
        where TDescription : struct, ISupportForEachWithUniversalDelegate =>
        LambdaForEachDescriptionConstructionMethods.ThrowCodeGenException<TDescription>();
}
