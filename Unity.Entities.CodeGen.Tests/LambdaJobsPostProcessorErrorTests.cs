using System;
using System.Linq;
using NUnit.Framework;
using Unity.Entities.CodeGen.Tests.LambdaJobs.Infrastructure;
using Unity.Collections;
using Unity.Entities.CodeGen.Tests.TestTypes;
using Unity.Entities.Tests;
using Unity.Jobs;
using UnityEngine;

namespace Unity.Entities.CodeGen.Tests
{
    [TestFixture]
    public class LambdaJobsPostProcessorErrorTests : LambdaJobsPostProcessorTestBase
    {
        [Test]
        public void LambdaTakingUnsupportedArgumentTest()
        {
            AssertProducesError(typeof(LambdaTakingUnsupportedArgument), nameof(UserError.DC0005));
        }

        class LambdaTakingUnsupportedArgument : TestJobComponentSystem
        {
            void Test()
            {
                Entities.ForEach(
                    (string whyAreYouPuttingAStringHereMakesNoSense) => { Console.WriteLine("Hello"); })
                    .Schedule(default);
            }
        }

        [Test]
        public void WithConflictingNameTest()
        {
            AssertProducesError(typeof(WithConflictingName), nameof(UserError.DC0003));
        }

        class WithConflictingName : TestJobComponentSystem
        {
            void Test()
            {
                Entities
                    .WithName("VeryCommonName")
                    .ForEach(
                        (ref Translation t) => {})
                    .Schedule(default);

                Entities
                    .WithName("VeryCommonName")
                    .ForEach(
                        (ref Translation t) => {})
                    .Schedule(default);
            }
        }

        [Test]
        public void WithNoneWithInvalidTypeTest()
        {
            AssertProducesError(typeof(WithNoneWithInvalidType), nameof(UserError.DC0052), "ANonIComponentDataClass");
        }

        class WithNoneWithInvalidType : TestJobComponentSystem
        {
            class ANonIComponentDataClass
            {
            }
            void Test()
            {
                Entities
                    .WithNone<ANonIComponentDataClass>()
                    .ForEach((in Boid translation) => {})
                    .Schedule(default);
            }
        }

        [Test]
        public void WithNoneWithInvalidGenericParameterTest()
        {
            AssertProducesError(typeof(WithNoneWithInvalidGenericParameter), nameof(UserError.DC0051), "TValue");
        }

        class WithNoneWithInvalidGenericParameter : TestJobComponentSystem
        {
            void Test<TValue>() where TValue : struct
            {
                Entities
                    .WithNone<TValue>()
                    .ForEach((in Boid translation) => {})
                    .Schedule(default);
            }
        }

        [Test]
        public void WithNoneWithInvalidGenericTypeTest()
        {
            AssertProducesError(typeof(WithNoneWithInvalidGenericType), nameof(UserError.DC0051), "GenericType`1");
        }

        class WithNoneWithInvalidGenericType : TestJobComponentSystem
        {
            struct GenericType<TValue> : IComponentData {}
            void Test()
            {
                Entities
                    .WithNone<GenericType<int>>()
                    .ForEach((in Boid translation) => {})
                    .Schedule(default);
            }
        }

        [Test]
        public void ParameterWithInvalidGenericParameterTest()
        {
            AssertProducesError(typeof(ParameterWithInvalidGenericParameter), nameof(UserError.DC0050), "TValue");
        }

        class ParameterWithInvalidGenericParameter : TestJobComponentSystem
        {
            void Test<TValue>() where TValue : struct
            {
                Entities
                    .ForEach((in TValue generic) => {})
                    .Schedule(default);
            }
        }

        [Test]
        public void ParameterWithInvalidGenericTypeTest()
        {
            AssertProducesError(typeof(ParameterWithInvalidGenericType), nameof(UserError.DC0050), "GenericType`1");
        }

        class ParameterWithInvalidGenericType : TestJobComponentSystem
        {
            struct GenericType<TValue> : IComponentData {}
            void Test()
            {
                Entities
                    .ForEach((in GenericType<int> generic) => {})
                    .Schedule(default);
            }
        }

        [Test]
        public void ParameterWithInvalidGenericDynamicBufferTypeTest()
        {
            AssertProducesError(typeof(ParameterWithInvalidGenericDynamicBufferType), nameof(UserError.DC0050), "GenericBufferType`1<Unity.Entities.Tests.EcsTestData>");
        }

        class ParameterWithInvalidGenericDynamicBufferType : TestJobComponentSystem
        {
            public struct GenericBufferType<T> : IBufferElementData where T : struct, IComponentData {}
            void Test()
            {
                Entities
                    .ForEach((ref DynamicBuffer<GenericBufferType<EcsTestData>> buffer) => {})
                    .Schedule(default);
            }
        }

        [Test]
        public void InGenericSystemTypeTest()
        {
            AssertProducesError(typeof(InGenericSystemType<>), nameof(UserError.DC0053), "InGenericSystemType`1");
        }

        public class InGenericSystemType<T> : SystemBase
            where T : struct, IComponentData
        {
            protected override void OnUpdate()
            {
                Entities.WithNone<T>().ForEach((Entity e, ref T n) => {}).Run();
            }
        }

        [Test]
        public void InGenericMethodThatCapturesTest()
        {
            AssertProducesError(typeof(InGenericMethodThatCapturesType), nameof(UserError.DC0054), "Test_LambdaJob0");
        }

        public class InGenericMethodThatCapturesType : SystemBase
        {
            void Test<T>()
            {
                var capture = 3.41f;
                Entities.ForEach((ref Translation translation) =>
                {
                    translation.Value = capture;
                }).Run();
            }

            protected override void OnUpdate()
            {
                Test<int>();
            }
        }

        [Test]
        public void WithReadOnly_IllegalArgument_Test()
        {
            AssertProducesError(typeof(WithReadOnly_IllegalArgument), nameof(UserError.DC0012));
        }

        class WithReadOnly_IllegalArgument : TestJobComponentSystem
        {
            void Test()
            {
                Entities
                    .WithReadOnly("stringLiteral")
                    .ForEach((in Boid translation) => {})
                    .Schedule(default);
            }
        }

        [Test]
        public void WithReadOnly_NonCapturedVariable_Test()
        {
            AssertProducesError(typeof(WithReadOnly_NonCapturedVariable), nameof(UserError.DC0012));
        }

        class WithReadOnly_NonCapturedVariable : TestJobComponentSystem
        {
            void Test()
            {
                var myNativeArray = new NativeArray<float>();

                Entities
                    .WithReadOnly(myNativeArray)
                    .ForEach((in Boid translation) => {})
                    .Schedule(default);
            }
        }

        [Test]
        public void WithDeallocateOnJobCompletion_WithRun_NonCapturedVariable_Test()
        {
            AssertProducesError(typeof(WithDeallocateOnJobCompletion_WithRun_NonCapturedVariable), nameof(UserError.DC0012));
        }

        class WithDeallocateOnJobCompletion_WithRun_NonCapturedVariable : TestJobComponentSystem
        {
            void Test()
            {
                var myNativeArray = new NativeArray<float>();

                Entities
                    .WithDisposeOnCompletion(myNativeArray)
                    .ForEach((in Boid translation) => {})
                    .Run();
            }
        }

        [Test]
        public void WithUnsupportedParameterTest()
        {
            AssertProducesError(typeof(WithUnsupportedParameter), nameof(UserError.DC0005));
        }

        class WithUnsupportedParameter : TestJobComponentSystem
        {
            void Test()
            {
                Entities
                    .ForEach((string whoKnowsWhatThisMeans, in Boid translation) => {})
                    .Schedule(default);
            }
        }

        [Test]
        public void WithCapturedReferenceTypeTest()
        {
            AssertProducesError(typeof(WithCapturedReferenceType), nameof(UserError.DC0004));
        }

        class WithCapturedReferenceType : TestJobComponentSystem
        {
            class CapturedClass
            {
                public float value;
            }

            void Test()
            {
                var capturedClass = new CapturedClass() {value = 3.0f};
                Entities
                    .ForEach((ref Translation t) => { t.Value = capturedClass.value; })
                    .Schedule(default);
            }
        }

        [Test]
        public void NestedScopeWithNonLambdaJobLambdaTest()
        {
            AssertProducesNoError(typeof(NestedScopeWithNonLambdaJobLambda));
        }

        class NestedScopeWithNonLambdaJobLambda : TestJobComponentSystem
        {
            void Test()
            {
                var outerValue = 3.0f;
                {
                    var innerValue = 3.0f;
                    Entities
                        .ForEach((ref Translation t) => { t.Value = outerValue + innerValue; })
                        .Schedule(default);
                }

                DoThing(() => { outerValue = 4.0f; });
            }

            void DoThing(Action action)
            {
                action();
            }
        }

        [Test]
        public void CaptureFieldInLocalCapturingLambdaTest()
        {
            AssertProducesError(typeof(CaptureFieldInLocalCapturingLambda), nameof(UserError.DC0001), "myfield");
        }

        class CaptureFieldInLocalCapturingLambda : TestJobComponentSystem
        {
            private int myfield = 123;

            void Test()
            {
                int also_capture_local = 1;
                Entities
                    .ForEach((ref Translation t) => { t.Value = myfield + also_capture_local; })
                    .Schedule(default);
            }
        }

        [Test]
        public void InvokeBaseMethodInBurstLambdaTest()
        {
            AssertProducesError(typeof(InvokeBaseMethodInBurstLambda), nameof(UserError.DC0002), "get_EntityManager");
        }

        class InvokeBaseMethodInBurstLambda : TestJobComponentSystem
        {
            void Test()
            {
                int version = 0;
                Entities.ForEach((ref Translation t) => { version = base.EntityManager.Version; }).Run();
            }
        }

        [Test]
        public void InvokeExtensionMethodOnRefTypeInBurstLambdaTest()
        {
            AssertProducesError(typeof(InvokeExtensionMethodOnRefTypeInBurstLambda), nameof(UserError.DC0002), nameof(TestJobComponentSystemExtensionMethods.MyTestExtension));
        }

        class InvokeExtensionMethodOnRefTypeInBurstLambda : TestJobComponentSystem
        {
            void Test()
            {
                Entities.ForEach((ref Translation t) => { this.MyTestExtension(); }).Run();
            }
        }

        [Test]
        public void InvokeInterfaceExtensionMethodOnRefTypeInBurstLambdaTest()
        {
            AssertProducesError(typeof(InvokeInterfaceExtensionMethodOnRefTypeInBurstLambda), nameof(UserError.DC0002), nameof(TestJobComponentSystemExtensionMethods.MyTestExtensionInterface));
        }

        class InvokeInterfaceExtensionMethodOnRefTypeInBurstLambda : TestJobComponentSystem, TestJobComponentSystemExtensionMethods.ITestInterface
        {
            void Test()
            {
                Entities.ForEach((ref Translation t) => { this.MyTestExtensionInterface(); }).Run();
            }
        }

        [Test]
        public void InvokeStaticFunctionOnRefTypeInBurstLambdaTest()
        {
            AssertProducesError(typeof(InvokeStaticFunctionOnRefTypeInBurstLambda), nameof(UserError.DC0002), nameof(InvokeStaticFunctionOnRefTypeInBurstLambda.ActualProblem));
        }

        class InvokeStaticFunctionOnRefTypeInBurstLambda : TestJobComponentSystem
        {
            void Test()
            {
                Entities.ForEach((ref Translation t) =>
                {
                    NotAProblem();
                    ActualProblem(this);
                }).Run();
            }

            static void NotAProblem() {}
            internal static void ActualProblem(JobComponentSystem s) {}
        }

        [Test]
        public void InvokeStaticFunctionOnValueTypeInBurstLambdaTest()
        {
            AssertProducesError(typeof(InvokeStaticFunctionOnValueTypeInBurstLambda), nameof(UserError.DC0002), nameof(InvokeStaticFunctionOnValueTypeInBurstLambda.ActualProblem));
        }

        class InvokeStaticFunctionOnValueTypeInBurstLambda : TestJobComponentSystem
        {
            void Test()
            {
                Entities.ForEach((ref Translation t) =>
                {
                    NotAProblem(t.Value);
                    ActualProblem();
                }).Run();
            }

            static void NotAProblem(float x) {}
            internal void ActualProblem() {}
        }

        [Test]
        public void UseSharedComponentData_UsingSchedule_ProducesError()
        {
            AssertProducesError(typeof(SharedComponentDataUsingSchedule), nameof(UserError.DC0019), "MySharedComponentData");
        }

        class SharedComponentDataUsingSchedule : TestJobComponentSystem
        {
            struct MySharedComponentData : ISharedComponentData
            {
            }

            void Test()
            {
                Entities
                    .ForEach((MySharedComponentData mydata) => {})
                    .Schedule(default);
            }
        }

        [Test]
        public void SharedComponentDataReceivedByRef_ProducesError()
        {
            AssertProducesError(typeof(SharedComponentDataReceivedByRef), nameof(UserError.DC0020), "MySharedComponentData");
        }

        class SharedComponentDataReceivedByRef : TestJobComponentSystem
        {
            struct MySharedComponentData : ISharedComponentData
            {
            }

            void Test()
            {
                Entities
                    .WithoutBurst()
                    .ForEach((ref MySharedComponentData mydata) => {})
                    .Run();
            }
        }

        [Test]
        public void CustomStructArgumentThatDoesntImplementSupportedInterfaceTest()
        {
            AssertProducesError(typeof(CustomStructArgumentThatDoesntImplementSupportedInterface), nameof(UserError.DC0021), "parameter 't' has type ForgotToAddInterface. This type is not a");
        }

        class CustomStructArgumentThatDoesntImplementSupportedInterface : TestJobComponentSystem
        {
            struct ForgotToAddInterface
            {
            }

            void Test()
            {
                Entities
                    .ForEach((ref ForgotToAddInterface t) => {})
                    .Schedule(default);
            }
        }


        [Test]
        public void CaptureFromMultipleScopesTest()
        {
            AssertProducesNoError(typeof(CaptureFromMultipleScopes));
        }

        class CaptureFromMultipleScopes : TestJobComponentSystem
        {
            void Test()
            {
                int scope1 = 1;
                {
                    int scope2 = 2;
                    {
                        int scope3 = 3;
                        Entities
                            .ForEach((ref Translation t) => { t.Value = scope1 + scope2 + scope3;})
                            .Schedule(default);
                    }
                }
            }
        }

        [Test]
        public void CaptureFieldInNonLocalCapturingLambdaTest()
        {
            AssertProducesError(typeof(CaptureFieldInNonLocalCapturingLambda), nameof(UserError.DC0001), "myfield");
        }

        class CaptureFieldInNonLocalCapturingLambda : TestJobComponentSystem
        {
            private int myfield = 123;

            void Test()
            {
                Entities
                    .ForEach((ref Translation t) => { t.Value = myfield; })
                    .Schedule(default);
            }
        }

        [Test]
        public void CaptureFieldByRefTest()
        {
            AssertProducesError(typeof(CaptureFieldByRef), nameof(UserError.DC0001), "m_MyField");
        }

        class CaptureFieldByRef : TestJobComponentSystem
        {
            int m_MyField = 123;

            void Test()
            {
                Entities
                    .ForEach((ref Translation t) => { NotAProblem(ref m_MyField); })
                    .Schedule(default);
            }

            static void NotAProblem(ref int a) {}
        }

        [Test]
        public void InvokeInstanceMethodInCapturingLambdaTest()
        {
            AssertProducesError(typeof(InvokeInstanceMethodInCapturingLambda), nameof(UserError.DC0002));
        }

        class InvokeInstanceMethodInCapturingLambda : TestJobComponentSystem
        {
            public object GetSomething(int i) => default;

            void Test()
            {
                int also_capture_local = 1;
                Entities
                    .ForEach((ref Translation t) => { GetSomething(also_capture_local); })
                    .Schedule(default);
            }
        }

        [Test]
        public void InvokeInstanceMethodInNonCapturingLambdaTest()
        {
            AssertProducesError(typeof(InvokeInstanceMethodInNonCapturingLambda), nameof(UserError.DC0002));
        }

        class InvokeInstanceMethodInNonCapturingLambda : TestJobComponentSystem
        {
            public object GetSomething(int i) => default;

            void Test()
            {
                Entities
                    .ForEach((ref Translation t) => { GetSomething(3); })
                    .Schedule(default);
            }
        }


        [Test]
        public void LocalFunctionThatWritesBackToCapturedLocalTest()
        {
            AssertProducesError(typeof(LocalFunctionThatWritesBackToCapturedLocal), nameof(UserError.DC0013));
        }

        class LocalFunctionThatWritesBackToCapturedLocal : TestJobComponentSystem
        {
            void Test()
            {
                int capture_me = 123;
                Entities
                    .ForEach((ref Translation t) =>
                {
                    void MyLocalFunction()
                    {
                        capture_me++;
                    }

                    MyLocalFunction();
                }).Schedule(default);
            }
        }

        [Test]
        public void LambdaThatWritesBackToCapturedLocalTest()
        {
            AssertProducesError(typeof(LambdaThatWritesBackToCapturedLocal), nameof(UserError.DC0013));
        }

        class LambdaThatWritesBackToCapturedLocal : TestJobComponentSystem
        {
            void Test()
            {
                int capture_me = 123;
                Entities
                    .ForEach((ref Translation t) => { capture_me++; }).Schedule(default);
            }
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void ManagedComponentInBurstJobTest()
        {
            AssertProducesError(typeof(ManagedComponentInBurstJob), nameof(UserError.DC0023));
        }

        class ManagedComponent : IComponentData, IEquatable<ManagedComponent>
        {
            public bool Equals(ManagedComponent other) => false;
            public override bool Equals(object obj) => false;
            public override int GetHashCode() =>  0;
        }

        class ManagedComponentInBurstJob : TestJobComponentSystem
        {
            void Test()
            {
                Entities.ForEach((ManagedComponent t) => {}).Run();
            }
        }

        public void ManagedComponentInScheduleTest()
        {
            AssertProducesError(typeof(ManagedComponentInSchedule), nameof(UserError.DC0023));
        }

        class ManagedComponentInSchedule : TestJobComponentSystem
        {
            void Test()
            {
                Entities.ForEach((ManagedComponent t) => {}).Schedule(default);
            }
        }

        [Test]
        public void ManagedComponentByReferenceTest()
        {
            AssertProducesError(typeof(ManagedComponentByReference), nameof(UserError.DC0024));
        }

        class ManagedComponentByReference : TestJobComponentSystem
        {
            void Test()
            {
                Entities.WithoutBurst().ForEach((ref ManagedComponent t) => {}).Run();
            }
        }
#endif

        [Test]
        public void WithAllWithSharedFilterTest()
        {
            AssertProducesError(typeof(WithAllWithSharedFilter), nameof(UserError.DC0026), "MySharedComponentData");
        }

        class WithAllWithSharedFilter : TestJobComponentSystem
        {
            struct MySharedComponentData : ISharedComponentData
            {
                public int Value;
            }

            void Test()
            {
                Entities
                    .WithAll<MySharedComponentData>()
                    .WithSharedComponentFilter(new MySharedComponentData() { Value = 3 })
                    .ForEach((in Boid translation) => {})
                    .Schedule(default);
            }
        }


        [Test]
        public void WithSwitchStatementTest()
        {
            AssertProducesNoError(typeof(WithSwitchStatement));
        }

        class WithSwitchStatement : TestJobComponentSystem
        {
            struct AbilityControl : IComponentData
            {
                public enum State
                {
                    Idle,
                    Active,
                    Cooldown
                }

                public State behaviorState;
            }

            void Test()
            {
                Entities.WithAll<Translation>()
                    .ForEach((Entity entity, ref AbilityControl abilityCtrl) =>
                    {
                        switch (abilityCtrl.behaviorState)
                        {
                            case AbilityControl.State.Idle:
                                abilityCtrl.behaviorState = AbilityControl.State.Active;
                                break;
                            case AbilityControl.State.Active:
                                abilityCtrl.behaviorState = AbilityControl.State.Cooldown;
                                break;
                            case AbilityControl.State.Cooldown:
                                abilityCtrl.behaviorState = AbilityControl.State.Idle;
                                break;
                        }
                    }).Run();
            }
        }

        [Test]
        public void HasTypesInAnotherAssemblyTest()
        {
            AssertProducesNoError(typeof(HasTypesInAnotherAssembly));
        }

        class HasTypesInAnotherAssembly : TestJobComponentSystem
        {
            void Test()
            {
                Entities
                    .WithAll<BoidInAnotherAssembly>()
                    .WithNone<TranslationInAnotherAssembly>()
                    .WithReadOnly(new VelocityInAnotherAssembly() { Value = 3.0f })
                    .WithAny<AccelerationInAnotherAssembly>()
                    .WithoutBurst()
                    .ForEach((ref RotationInAnotherAssembly a) => {}).Run();
            }
        }

        [Test]
        public void LambdaThatMakesNonExplicitStructuralChangesTest()
        {
            AssertProducesError(typeof(LambdaThatMakesNonExplicitStructuralChanges), nameof(UserError.DC0027));
        }

        class LambdaThatMakesNonExplicitStructuralChanges : TestJobComponentSystem
        {
            void Test()
            {
                float delta = 0.0f;
                Entities
                    .WithoutBurst()
                    .ForEach((Entity entity, ref Translation t) =>
                    {
                        float blah = delta + 1.0f;
                        EntityManager.RemoveComponent<Translation>(entity);
                    }).Run();
            }
        }

        [Test]
        public void LambdaThatMakesStructuralChangesWithScheduleTest()
        {
            AssertProducesError(typeof(LambdaThatMakesStructuralChangesWithSchedule), nameof(UserError.DC0028));
        }

        class LambdaThatMakesStructuralChangesWithSchedule : TestJobComponentSystem
        {
            void Test()
            {
                float delta = 0.0f;
                Entities.WithoutBurst()
                    .WithStructuralChanges()
                    .ForEach((Entity entity, ref Translation t) =>
                    {
                        float blah = delta + 1.0f;
                        EntityManager.RemoveComponent<Translation>(entity);
                    }).Schedule(default);
            }
        }

        [Test]
        public void LambdaThatHasNestedLambdaTest()
        {
            AssertProducesError(typeof(LambdaThatHasNestedLambda), nameof(UserError.DC0029));
        }

        class LambdaThatHasNestedLambda : TestJobComponentSystem
        {
            void Test()
            {
                float delta = 0.0f;
                Entities
                    .WithoutBurst()
                    .ForEach((Entity e1, ref Translation t1) =>
                    {
                        Entities
                            .WithoutBurst()
                            .ForEach((Entity e2, ref Translation t2) => { delta += 1.0f; }).Run();
                    }).Run();
            }
        }

        [Test]
        public void LambdaThatTriesToStoreNonValidEntityQueryVariableTest()
        {
            AssertProducesError(typeof(LambdaThatTriesToStoreNonValidEntityQueryVariable), nameof(UserError.DC0031));
        }

        class LambdaThatTriesToStoreNonValidEntityQueryVariable : TestJobComponentSystem
        {
            class EntityQueryHolder
            {
                public EntityQuery m_Query;
            }

            void Test()
            {
                EntityQueryHolder entityQueryHolder = new EntityQueryHolder();

                float delta = 0.0f;
                Entities
                    .WithStoreEntityQueryInField(ref entityQueryHolder.m_Query)
                    .ForEach((Entity e2, ref Translation t2) => { delta += 1.0f; }).Run();
            }
        }

        [Test]
        public void LambdaThatTriesToStoreLocalEntityQueryVariableTest()
        {
            AssertProducesError(typeof(LambdaThatTriesToStoreLocalEntityQueryVariable), nameof(UserError.DC0031));
        }

        class LambdaThatTriesToStoreLocalEntityQueryVariable : TestJobComponentSystem
        {
            void Test()
            {
                EntityQuery query = default;

                float delta = 0.0f;
                Entities
                    .WithStoreEntityQueryInField(ref query)
                    .ForEach((Entity e2, ref Translation t2) => { delta += 1.0f; }).Run();
            }
        }

#if !NET_DOTS
        [Test]
        public void LambdaInSystemWithExecuteAlwaysTest()
        {
            AssertProducesWarning(typeof(LambdaInSystemWithExecuteAlways), nameof(UserError.DC0032));
        }

        [ExecuteAlways]
        class LambdaInSystemWithExecuteAlways : TestJobComponentSystem
        {
            void Test()
            {
                float delta = 0.0f;
                Entities
                    .ForEach((Entity e1, ref Translation t1) =>
                {
                    delta += 1.0f;
                }).Run();
            }
        }
#endif

        [Test]
        public void CallsMethodInComponentSystemBaseTest()
        {
            AssertProducesError(typeof(CallsMethodInComponentSystemBase), nameof(UserError.DC0002), "Time");
        }

        class CallsMethodInComponentSystemBase : TestJobComponentSystem
        {
            void Test()
            {
                Entities
                    .ForEach((ref Translation t) => { var targetDistance = Time.DeltaTime; })
                    .Schedule(default);
            }
        }


        [Test]
        public void IncorrectUsageOfBufferIsDetected()
        {
            AssertProducesError(typeof(IncorrectUsageOfBuffer), nameof(UserError.DC0033), "MyBufferFloat");
        }

        class IncorrectUsageOfBuffer : TestJobComponentSystem
        {
            void Test()
            {
                Entities
                    .ForEach((MyBufferFloat f) => {})
                    .Schedule(default);
            }
        }

        [Test]
        public void CorrectUsageOfBufferIsNotDetected()
        {
            AssertProducesNoError(typeof(CorrectUsageOfBuffer));
        }

        class CorrectUsageOfBuffer : TestJobComponentSystem
        {
            void Test()
            {
                Entities
                    .ForEach((DynamicBuffer<MyBufferFloat> f) => {})
                    .Schedule(default);
            }
        }


        [Test]
        public void ParameterNamesHaveStablePath()
        {
            var methodToAnalyze = MethodDefinitionForOnlyMethodOf(typeof(ParameterNamesPath));
            var forEachDescriptionConstructions = LambdaJobDescriptionConstruction.FindIn(methodToAnalyze);
            var(jobStructForLambdaJob, diagnosticMessages) = LambdaJobsPostProcessor.Rewrite(methodToAnalyze, forEachDescriptionConstructions.First());

            Assert.IsEmpty(diagnosticMessages);

            const string valueProviderFieldName = "_lambdaParameterValueProviders";
            var valueProvidersField = jobStructForLambdaJob.TypeDefinition.Fields.FirstOrDefault(f => f.Name == valueProviderFieldName);
            Assert.IsNotNull(valueProvidersField, $"Could not find field {valueProviderFieldName} in generated lambda job!");

            const string parameterFieldName = "forParameter_floatBuffer";
            var parameterField = valueProvidersField.FieldType.Resolve().Fields.FirstOrDefault(f => f.Name == parameterFieldName);
            Assert.IsNotNull(parameterField, $"Could not find field {valueProviderFieldName}.{parameterFieldName} in generated lambda job!");

            const string typeFieldName = "_typeHandle";
            var typeField = parameterField.FieldType.Resolve().Fields.FirstOrDefault(f => f.Name == typeFieldName);
            Assert.IsNotNull(typeField, $"Could not find field {valueProviderFieldName}.{parameterFieldName}.{typeFieldName} in generated lambda job!");
        }

        class ParameterNamesPath : TestJobComponentSystem
        {
            void Test()
            {
                Entities
                    .WithName("ParameterNamesTest")
                    .ForEach((DynamicBuffer<MyBufferFloat> floatBuffer) => {})
                    .Schedule(default);
            }
        }

        struct StructWithNativeContainer
        {
            public NativeArray<int> array;
        }

        struct StructWithStructWithNativeContainer
        {
            public StructWithNativeContainer innerStruct;
        }

        struct StructWithPrimitiveType
        {
            public int field;
        }

        [Test]
        public void ReadOnlyWarnsAboutArgumentType_CorrectReadOnlyUsageWithNativeContainer()
        {
            AssertProducesNoError(typeof(CorrectReadOnlyUsageWithNativeContainer));
        }

        [Test]
        public void ReadOnlyWarnsAboutArgumentType_CorrectReadOnlyUsageWithStruct()
        {
            AssertProducesNoError(typeof(CorrectReadOnlyUsageWithStruct));
        }

        [Test]
        public void ReadOnlyWarnsAboutArgumentType_IncorrectReadOnlyUsageWithStruct()
        {
            AssertProducesError(typeof(IncorrectReadOnlyUsageWithStruct), nameof(UserError.DC0034), "structWithPrimitiveType");
        }

        [Test]
        public void ReadOnlyWarnsAboutArgumentType_IncorrectReadOnlyUsageWithPrimitiveType()
        {
            AssertProducesError(typeof(IncorrectReadOnlyUsageWithPrimitiveType), nameof(UserError.DC0034), "myVar");
        }

        class CorrectReadOnlyUsageWithNativeContainer : TestJobComponentSystem
        {
            void Test()
            {
                NativeArray<int> array = default;
                Entities.WithReadOnly(array).ForEach((ref Translation t) => { t.Value += array[0]; }).Schedule(default);
            }
        }

        class CorrectReadOnlyUsageWithStruct : TestJobComponentSystem
        {
            void Test()
            {
                StructWithNativeContainer structWithNativeContainer = default;
                Entities.WithReadOnly(structWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithNativeContainer.array[0];
                }).Schedule(default);

                StructWithStructWithNativeContainer structWithStructWithNativeContainer = default;
                Entities.WithReadOnly(structWithStructWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithStructWithNativeContainer.innerStruct.array[0];
                }).Schedule(default);
            }
        }

        class IncorrectReadOnlyUsageWithStruct : TestJobComponentSystem
        {
            void Test()
            {
                StructWithPrimitiveType structWithPrimitiveType = default;
                Entities.WithReadOnly(structWithPrimitiveType).ForEach((ref Translation t) => { t.Value += structWithPrimitiveType.field; }).Schedule(default);
            }
        }

        class IncorrectReadOnlyUsageWithPrimitiveType : TestJobComponentSystem
        {
            void Test()
            {
                int myVar = 0;
                Entities.WithReadOnly(myVar).ForEach((ref Translation t) => { t.Value += myVar; }).Schedule(default);
            }
        }

        [Test]
        public void DeallocateOnJobCompletionWarnsAboutArgumentType_CorrectDeallocateOnJobCompletionUsageWithNativeContainer()
        {
            AssertProducesNoError(typeof(CorrectDeallocateOnJobCompletionUsageWithNativeContainer));
        }
        [Test]
        public void DeallocateOnJobCompletionWarnsAboutArgumentType_CorrectDeallocateOnJobCompletionUsageWithStruct()
        {
            AssertProducesNoError(typeof(CorrectDeallocateOnJobCompletionUsageWithStruct));
        }
        [Test]
        public void DeallocateOnJobCompletionWarnsAboutArgumentType_IncorrectDeallocateOnJobCompletionUsageWithStruct()
        {
            AssertProducesError(typeof(IncorrectDeallocateOnJobCompletionUsageWithStruct), nameof(UserError.DC0035), "structWithPrimitiveType");
        }
        [Test]
        public void DeallocateOnJobCompletionWarnsAboutArgumentType_IncorrectDeallocateOnJobCompletionUsageWithPrimitiveType()
        {
            AssertProducesError(typeof(IncorrectDeallocateOnJobCompletionUsageWithPrimitiveType), nameof(UserError.DC0035), "myVar");
        }

        class CorrectDeallocateOnJobCompletionUsageWithNativeContainer : TestJobComponentSystem
        {
            void Test()
            {
                NativeArray<int> array = default;
                Entities.WithReadOnly(array).WithDisposeOnCompletion(array).ForEach((ref Translation t) => { t.Value += array[0]; }).Schedule(default);
            }
        }

        class CorrectDeallocateOnJobCompletionUsageWithStruct : TestJobComponentSystem
        {
            void Test()
            {
                StructWithNativeContainer structWithNativeContainer = default;
                Entities.WithDisposeOnCompletion(structWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithNativeContainer.array[0];
                }).Schedule(default);

                StructWithStructWithNativeContainer structWithStructWithNativeContainer = default;
                Entities.WithDisposeOnCompletion(structWithStructWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithStructWithNativeContainer.innerStruct.array[0];
                }).Schedule(default);
            }
        }

#pragma warning disable 0618
        class IncorrectDeallocateOnJobCompletionUsageWithStruct : TestJobComponentSystem
        {
            void Test()
            {
                StructWithPrimitiveType structWithPrimitiveType = default;
                structWithPrimitiveType.field = default;
                Entities.WithDeallocateOnJobCompletion(structWithPrimitiveType).ForEach((ref Translation t) => { t.Value += structWithPrimitiveType.field; }).Schedule(default);
            }
        }

        class IncorrectDeallocateOnJobCompletionUsageWithPrimitiveType : TestJobComponentSystem
        {
            void Test()
            {
                int myVar = 0;
                Entities.WithDeallocateOnJobCompletion(myVar).ForEach((ref Translation t) => { t.Value += myVar; }).Schedule(default);
            }
        }
#pragma warning restore 0618

        [Test]
        public void DisableContainerSafetyRestrictionWarnsAboutArgumentType_CorrectDisableContainerSafetyRestrictionUsageWithNativeContainer()
        {
            AssertProducesNoError(typeof(CorrectDisableContainerSafetyRestrictionUsageWithNativeContainer));
        }
        [Test]
        public void DisableContainerSafetyRestrictionWarnsAboutArgumentType_CorrectDisableContainerSafetyRestrictionUsageWithStruct()
        {
            AssertProducesNoError(typeof(CorrectDisableContainerSafetyRestrictionUsageWithStruct));
        }
        [Test]
        public void DisableContainerSafetyRestrictionWarnsAboutArgumentType_IncorrectDisableContainerSafetyRestrictionUsageWithStruct()
        {
            AssertProducesError(typeof(IncorrectDisableContainerSafetyRestrictionUsageWithStruct), nameof(UserError.DC0036), "structWithPrimitiveType");
        }
        [Test]
        public void DisableContainerSafetyRestrictionWarnsAboutArgumentType_IncorrectDisableContainerSafetyRestrictionUsageWithPrimitiveType()
        {
            AssertProducesError(typeof(IncorrectDisableContainerSafetyRestrictionUsageWithPrimitiveType), nameof(UserError.DC0036), "myVar");
        }

        class CorrectDisableContainerSafetyRestrictionUsageWithNativeContainer : TestJobComponentSystem
        {
            void Test()
            {
                NativeArray<int> array = default;
                Entities.WithReadOnly(array).WithNativeDisableContainerSafetyRestriction(array).ForEach((ref Translation t) => { t.Value += array[0]; }).Schedule(default);
            }
        }

        class CorrectDisableContainerSafetyRestrictionUsageWithStruct : TestJobComponentSystem
        {
            void Test()
            {
                StructWithNativeContainer structWithNativeContainer = default;
                structWithNativeContainer.array = default;
                Entities.WithNativeDisableContainerSafetyRestriction(structWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithNativeContainer.array[0];
                }).Schedule(default);

                StructWithStructWithNativeContainer structWithStructWithNativeContainer = default;
                Entities.WithNativeDisableContainerSafetyRestriction(structWithStructWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithStructWithNativeContainer.innerStruct.array[0];
                }).Schedule(default);
            }
        }

        class IncorrectDisableContainerSafetyRestrictionUsageWithStruct : TestJobComponentSystem
        {
            void Test()
            {
                StructWithPrimitiveType structWithPrimitiveType = default;
                structWithPrimitiveType.field = default;
                Entities.WithNativeDisableContainerSafetyRestriction(structWithPrimitiveType).ForEach((ref Translation t) =>
                {
                    t.Value += structWithPrimitiveType.field;
                }).Schedule(default);
            }
        }

        class IncorrectDisableContainerSafetyRestrictionUsageWithPrimitiveType : TestJobComponentSystem
        {
            void Test()
            {
                int myVar = 0;
                Entities.WithNativeDisableContainerSafetyRestriction(myVar).ForEach((ref Translation t) => { t.Value += myVar; }).Schedule(default);
            }
        }

        [Test]
        public void DisableParallelForRestrictionWarnsAboutArgumentType_CorrectDisableParallelForRestrictionUsageWithNativeContainer()
        {
            AssertProducesNoError(typeof(CorrectDisableParallelForRestrictionUsageWithNativeContainer));
        }
        [Test]
        public void DisableParallelForRestrictionWarnsAboutArgumentType_CorrectDisableParallelForRestrictionUsageWithStruct()
        {
            AssertProducesNoError(typeof(CorrectDisableParallelForRestrictionUsageWithStruct));
        }
        [Test]
        public void DisableParallelForRestrictionWarnsAboutArgumentType_IncorrectDisableParallelForRestrictionUsageWithStruct()
        {
            AssertProducesError(typeof(IncorrectDisableParallelForRestrictionUsageWithStruct), nameof(UserError.DC0037), "structWithPrimitiveType");
        }
        [Test]
        public void DisableParallelForRestrictionWarnsAboutArgumentType_IncorrectDisableParallelForRestrictionUsageWithPrimitiveType()
        {
            AssertProducesError(typeof(IncorrectDisableParallelForRestrictionUsageWithPrimitiveType), nameof(UserError.DC0037), "myVar");
        }

        class CorrectDisableParallelForRestrictionUsageWithNativeContainer : TestJobComponentSystem
        {
            void Test()
            {
                NativeArray<int> array = default;
                Entities.WithNativeDisableParallelForRestriction(array).ForEach((ref Translation t) => { t.Value += array[0]; }).Schedule(default);
            }
        }

        class CorrectDisableParallelForRestrictionUsageWithStruct : TestJobComponentSystem
        {
            void Test()
            {
                StructWithNativeContainer structWithNativeContainer = default;
                structWithNativeContainer.array = default;
                Entities.WithNativeDisableParallelForRestriction(structWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithNativeContainer.array[0];
                }).Schedule(default);

                StructWithStructWithNativeContainer structWithStructWithNativeContainer = default;
                Entities.WithNativeDisableParallelForRestriction(structWithStructWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithStructWithNativeContainer.innerStruct.array[0];
                }).Schedule(default);
            }
        }

        class IncorrectDisableParallelForRestrictionUsageWithStruct : TestJobComponentSystem
        {
            void Test()
            {
                StructWithPrimitiveType structWithPrimitiveType = default;
                structWithPrimitiveType.field = default;
                Entities.WithNativeDisableParallelForRestriction(structWithPrimitiveType).ForEach((ref Translation t) =>
                {
                    t.Value += structWithPrimitiveType.field;
                }).Schedule(default);
            }
        }

        class IncorrectDisableParallelForRestrictionUsageWithPrimitiveType : TestJobComponentSystem
        {
            void Test()
            {
                int myVar = 0;
                Entities.WithNativeDisableParallelForRestriction(myVar).ForEach((ref Translation t) => { t.Value += myVar; }).Schedule(default);
            }
        }

        [Test]
        public void AttributesErrorWhenUsedOnUserTypeFields()
        {
            AssertProducesError(typeof(CaptureFieldInUserStructLambda), nameof(UserError.DC0038), "UserStruct.Array");
        }

        class CaptureFieldInUserStructLambda : TestJobComponentSystem
        {
            void Test()
            {
                var localStruct = new UserStruct() { Array = default };
                Entities
                    .WithReadOnly(localStruct.Array)
                    .ForEach((ref Translation t) => { t.Value += localStruct.Array[0]; })
                    .Schedule(default);
            }

            struct UserStruct
            {
                public NativeArray<int> Array;
            }
        }

        [Test]
        public void GetComponentDataFromEntityWithMethodAsParam_ProducesError()
        {
            AssertProducesError(typeof(GetComponentDataFromEntityWithMethodAsParam), nameof(UserError.DC0048), "GetComponentDataFromEntity");
        }

        class GetComponentDataFromEntityWithMethodAsParam : TestSystemBase
        {
            static bool MethodThatReturnsBool() => false;
            void Test()
            {
                Entities
                    .ForEach((Entity entity, in Translation tde) =>
                {
                    GetComponentDataFromEntity<Velocity>(MethodThatReturnsBool());
                }).Run();
            }
        }

        [Test]
        public void GetComponentDataFromEntityWithVarAsParam_ProducesError()
        {
            AssertProducesError(typeof(GetComponentDataFromEntityWithVarAsParam), nameof(UserError.DC0049), "GetComponentDataFromEntity");
        }

        class GetComponentDataFromEntityWithVarAsParam : TestSystemBase
        {
            void Test()
            {
                var localBool = false;
                Entities.ForEach((Entity entity, in Translation tde) => { GetComponentDataFromEntity<Velocity>(localBool); }).Run();
            }
        }

        [Test]
        public void GetComponentDataFromEntityWithArgAsParam_ProducesError()
        {
            AssertProducesError(typeof(GetComponentDataFromEntityWithArgAsParam), nameof(UserError.DC0049), "GetComponentDataFromEntity");
        }

        class GetComponentDataFromEntityWithArgAsParam : TestSystemBase
        {
            void Test(bool argBool)
            {
                Entities.ForEach((Entity entity, in Translation tde) => { GetComponentDataFromEntity<Velocity>(argBool); }).Run();
            }
        }

        [Test]
        public void ControlFlowInsideWithChainTest()
        {
            AssertProducesError(typeof(ControlFlowInsideWithChainSystem), nameof(UserError.DC0010));
        }

        public class ControlFlowInsideWithChainSystem : JobComponentSystem
        {
            public bool maybe;

            protected override JobHandle OnUpdate(JobHandle inputDependencies)
            {
                return Entities
                    .WithName(maybe ? "One" : "Two")
                    .ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value;
                        })
                    .Schedule(inputDependencies);
            }
        }

        [Test]
        public void UsingConstructionMultipleTimesThrows()
        {
            AssertProducesError(typeof(UseConstructionMethodMultipleTimes), nameof(UserError.DC0009), "WithName");
        }

        public class UseConstructionMethodMultipleTimes : JobComponentSystem
        {
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                return Entities
                    .WithName("Cannot")
                    .WithName("Make up my mind")
                    .ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value;
                        })
                    .Schedule(inputDeps);
            }
        }

        [Test]
        public void InvalidJobNamesThrow_InvalidJobNameWithSpaces()
        {
            AssertProducesError(typeof(InvalidJobNameWithSpaces), nameof(UserError.DC0043), "WithName");
        }

        [Test]
        public void InvalidJobNamesThrow_InvalidJobNameStartsWithDigit()
        {
            AssertProducesError(typeof(InvalidJobNameStartsWithDigit), nameof(UserError.DC0043), "WithName");
        }
        [Test]
        public void InvalidJobNamesThrow_InvalidJobNameCompilerReservedName()
        {
            AssertProducesError(typeof(InvalidJobNameCompilerReservedName), nameof(UserError.DC0043), "WithName");
        }

        public class InvalidJobNameWithSpaces : JobComponentSystem
        {
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                return Entities
                    .WithName("This name may not contain spaces")
                    .ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value;
                        })
                    .Schedule(inputDeps);
            }
        }

        public class InvalidJobNameStartsWithDigit : JobComponentSystem
        {
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                return Entities
                    .WithName("1job")
                    .ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value;
                        })
                    .Schedule(inputDeps);
            }
        }

        public class InvalidJobNameCompilerReservedName : JobComponentSystem
        {
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                return Entities
                    .WithName("__job")
                    .ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value;
                        })
                    .Schedule(inputDeps);
            }
        }

        [Test]
        public void ForgotToAddForEachTest()
        {
            AssertProducesError(typeof(ForgotToAddForEach), nameof(UserError.DC0006));
        }

        class ForgotToAddForEach : TestJobComponentSystem
        {
            void Test()
            {
                Entities
                    .WithAny<Translation>()
                    .Schedule(default);
            }
        }


        [Test]
        public void WithoutScheduleInvocationTest()
        {
            AssertProducesError(typeof(WithoutScheduleInvocation), nameof(UserError.DC0011));
        }

        public class WithoutScheduleInvocation : JobComponentSystem
        {
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                Entities.ForEach(
                    (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                    {
                        translation.Value += velocity.Value;
                    });
                return default;
            }
        }

        [Test]
        public void WithLambdaStoredInFieldTest()
        {
            AssertProducesError(typeof(WithLambdaStoredInFieldSystem), nameof(UserError.DC0044));
        }

        public class WithLambdaStoredInFieldSystem : JobComponentSystem
        {
            UniversalDelegates.R<Translation> _translationAction;

            protected override JobHandle OnUpdate(JobHandle inputDependencies)
            {
                _translationAction = (ref Translation t) => {};
                return Entities.ForEach(_translationAction).Schedule(inputDependencies);
            }
        }

        [Test]
        public void WithLambdaStoredInVariableTest()
        {
            AssertProducesError(typeof(WithLambdaStoredInVariableSystem), nameof(UserError.DC0044));
        }

        public class WithLambdaStoredInVariableSystem : JobComponentSystem
        {
            protected override JobHandle OnUpdate(JobHandle inputDependencies)
            {
                UniversalDelegates.R<Translation> translationAction = (ref Translation t) => {};
                return Entities.ForEach(translationAction).Schedule(inputDependencies);
            }
        }

        [Test]
        public void WithLambdaStoredInArgTest()
        {
            AssertProducesError(typeof(WithLambdaStoredInArgSystem), nameof(UserError.DC0044));
        }

        public class WithLambdaStoredInArgSystem : JobComponentSystem
        {
            JobHandle Test(UniversalDelegates.R<Translation> action)
            {
                return Entities.ForEach(action).Schedule(default);
            }

            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                return Test((ref Translation t) => {});
            }
        }

        [Test]
        public void WithLambdaReturnedFromMethodTest()
        {
            AssertProducesError(typeof(WithLambdaReturnedFromMethodSystem), nameof(UserError.DC0044));
        }

        public class WithLambdaReturnedFromMethodSystem : JobComponentSystem
        {
            protected override JobHandle OnUpdate(JobHandle inputDeps)
            {
                return Entities.ForEach(GetAction()).Schedule(default);
            }

            static UniversalDelegates.R<Translation> GetAction()
            {
                return (ref Translation t) => {};
            }
        }

        [Test]
        public void WithGetComponentAndCaptureOfThisTest()
        {
            AssertProducesError(typeof(WithGetComponentAndCaptureOfThis), nameof(UserError.DC0001), "someField");
        }

        public class WithGetComponentAndCaptureOfThis : SystemBase
        {
            float someField = 3.0f;

            protected override void OnUpdate()
            {
                Entities
                    .ForEach(
                    (ref Translation translation) =>
                    {
                        var vel = GetComponent<Velocity>(default);
                        translation = new Translation() {Value = someField * vel.Value};
                    })
                    .Schedule();
            }
        }

        [Test]
        public void WithGetComponentAndCaptureOfThisAndVarTest()
        {
            AssertProducesError(typeof(WithGetComponentAndCaptureOfThisAndVar), nameof(UserError.DC0001), "someField");
        }

        public class WithGetComponentAndCaptureOfThisAndVar : SystemBase
        {
            float someField = 3.0f;

            protected override void OnUpdate()
            {
                float someVar = 2.0f;
                Entities
                    .ForEach(
                    (ref Translation translation) =>
                    {
                        var vel = GetComponent<Velocity>(default);
                        translation = new Translation() {Value = someField * vel.Value * someVar};
                    })
                    .Schedule();
            }
        }

        [Test]
        public void GetComponentWithConditionTest()
        {
            AssertProducesError(typeof(GetComponentWithCondition), nameof(UserError.DC0045), "GetComponent");
        }

        public class GetComponentWithCondition : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities.ForEach((Entity entity, ref Translation tde) =>
                {
                    Entity e1 = default, e2 = default;
                    tde.Value += GetComponent<Velocity>(tde.Value > 1 ? e1 : e2).Value;
                }).Schedule();
            }
        }

        [Test]
        public void SetComponentWithPermittedAliasTest()
        {
            AssertProducesNoError(typeof(SetComponentWithPermittedAlias));
        }

        public class SetComponentWithPermittedAlias : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities.ForEach((Entity e, in Translation data) => {
                    GetComponent<Translation>(e);
                }).Run();
            }
        }

        [Test]
        public void SetComponentWithNotPermittedParameterThatAliasesTestTest()
        {
            AssertProducesError(typeof(SetComponentWithNotPermittedParameterThatAliasesTest), nameof(UserError.DC0047), "Translation");
        }

        public class SetComponentWithNotPermittedParameterThatAliasesTest : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities.ForEach((Entity e, ref Translation data) => {
                    var translation = GetComponent<Translation>(e);
                }).Run();
            }
        }

        [Test]
        public void SetComponentWithNotPermittedComponentAccessThatAliasesTest()
        {
            AssertProducesError(typeof(SetComponentWithNotPermittedComponentAccessThatAliases), nameof(UserError.DC0046), "SetComponent");
        }

        public class SetComponentWithNotPermittedComponentAccessThatAliases : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities.ForEach((Entity e, in Translation data) => {
                    SetComponent(e, new Translation());
                }).Run();
            }
        }


        [Test]
        public void DuplicateComponentInQueryDoesNotProduceError_Test()
        {
            AssertProducesNoError(typeof(DuplicateComponentInQueryDoesNotProduceError_System));
        }

        public class DuplicateComponentInQueryDoesNotProduceError_System : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities.WithAll<Translation>().ForEach((Entity entity, ref Translation rotation) => { }).Run();
            }
        }

        [Test]
        public void ComponentPassedByValueGeneratesWarning_Test()
        {
            AssertProducesWarning(typeof(ComponentPassedByValueGeneratesWarning_System), nameof(UserError.DC0055));
        }

        public class ComponentPassedByValueGeneratesWarning_System : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities.ForEach((Translation translation) => { }).Run();
            }
        }

        [Test]
        public void JobWithCodeAndStructuralChanges_Test()
        {
            AssertProducesError(typeof(JobWithCodeAndStructuralChanges_System), nameof(UserError.DC0057));
        }

        public class JobWithCodeAndStructuralChanges_System : SystemBase
        {
            protected override void OnUpdate()
            {
                Job.WithStructuralChanges().WithCode(() =>{ }).Run();
            }
        }

        [Test]
        public void InvalidWithNoneComponentGeneratesError_Test_WithNone_WithAll()
        {
            AssertProducesError(typeof(InvalidWithNoneInWithAllComponentGeneratesError_System), nameof(UserError.DC0056),
                nameof(LambdaJobQueryConstructionMethods.WithNone), nameof(LambdaJobQueryConstructionMethods.WithAll));
        }
        [Test]
        public void InvalidWithNoneComponentGeneratesError_Test_WithNone_WithAny()
        {
            AssertProducesError(typeof(InvalidWithNoneInWithAnyComponentGeneratesError_System), nameof(UserError.DC0056),
                nameof(LambdaJobQueryConstructionMethods.WithNone), nameof(LambdaJobQueryConstructionMethods.WithAny));
        }
        [Test]
        public void InvalidWithNoneComponentGeneratesError_Test_WithNone_LambdaParameter()
        {
            AssertProducesError(typeof(InvalidWithNoneInLambdaParamComponentGeneratesError_System), nameof(UserError.DC0056),
                nameof(LambdaJobQueryConstructionMethods.WithNone), "lambda parameter");
        }

        public class InvalidWithNoneInWithAllComponentGeneratesError_System : SystemBase {
            protected override void OnUpdate() { Entities.WithNone<Translation>().WithAll<Translation>().ForEach((Entity entity) => { }).Run(); }
        }
        public class InvalidWithNoneInWithAnyComponentGeneratesError_System : SystemBase {
            protected override void OnUpdate() { Entities.WithNone<Translation>().WithAny<Translation>().ForEach((Entity entity) => { }).Run(); }
        }
        public class InvalidWithNoneInLambdaParamComponentGeneratesError_System : SystemBase {
            protected override void OnUpdate() { Entities.WithNone<Translation>().ForEach((ref Translation translation) => { }).Run(); }
        }

        [Test]
        public void InvalidWithAnyComponentGeneratesError_Test_WithAny_WithAll()
        {
            AssertProducesError(typeof(InvalidWithAnyInWithAllComponentGeneratesError_System), nameof(UserError.DC0056),
                nameof(LambdaJobQueryConstructionMethods.WithAny), nameof(LambdaJobQueryConstructionMethods.WithAll));
        }
        [Test]
        public void InvalidWithAnyComponentGeneratesError_Test_WithAny_LambdaParameter()
        {
            AssertProducesError(typeof(InvalidWithAnyInLambdaParamComponentGeneratesError_System), nameof(UserError.DC0056),
                nameof(LambdaJobQueryConstructionMethods.WithAny), "lambda parameter");
        }

        public class InvalidWithAnyInWithAllComponentGeneratesError_System : SystemBase {
            protected override void OnUpdate() { Entities.WithAny<Translation>().WithAll<Translation>().ForEach((Entity entity) => { }).Run(); }
        }
        public class InvalidWithAnyInLambdaParamComponentGeneratesError_System : SystemBase {
            protected override void OnUpdate() { Entities.WithAny<Translation>().ForEach((ref Translation translation) => { }).Run(); }
        }
    }

    public static class TestJobComponentSystemExtensionMethods
    {
        public interface ITestInterface {}

        public static void MyTestExtension(this TestJobComponentSystem test) {}
        public static void MyTestExtensionInterface(this ITestInterface test) {}
    }
}
