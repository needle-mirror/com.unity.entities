#if !ROSLYN_SOURCEGEN_ENABLED // Still need to add error reporting for lambda jobs source generation
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

        class LambdaTakingUnsupportedArgument : TestSystemBase
        {
            void Test()
            {
                Entities.ForEach(
                    (string whyAreYouPuttingAStringHereMakesNoSense) => { Console.WriteLine("Hello"); })
                    .Schedule();
            }
        }

        [Test]
        public void WithConflictingNameTest()
        {
            AssertProducesError(typeof(WithConflictingName), nameof(UserError.DC0003));
        }

        class WithConflictingName : TestSystemBase
        {
            void Test()
            {
                Entities
                    .WithName("VeryCommonName")
                    .ForEach(
                        (ref Translation t) => {})
                    .Schedule();

                Entities
                    .WithName("VeryCommonName")
                    .ForEach(
                        (ref Translation t) => {})
                    .Schedule();
            }
        }

        [Test]
        public void WithNoneWithInvalidTypeTest()
        {
            AssertProducesError(typeof(WithNoneWithInvalidType), nameof(UserError.DC0052), "ANonIComponentDataClass");
        }

        class WithNoneWithInvalidType : TestSystemBase
        {
            class ANonIComponentDataClass
            {
            }
            void Test()
            {
                Entities
                    .WithNone<ANonIComponentDataClass>()
                    .ForEach((in Boid translation) => {})
                    .Schedule();
            }
        }

        [Test]
        public void WithNoneWithInvalidGenericParameterTest()
        {
            AssertProducesError(typeof(WithNoneWithInvalidGenericParameter), nameof(UserError.DC0051), "TValue");
        }

        class WithNoneWithInvalidGenericParameter : TestSystemBase
        {
            void Test<TValue>() where TValue : struct
            {
                Entities
                    .WithNone<TValue>()
                    .ForEach((in Boid translation) => {})
                    .Schedule();
            }
        }

        [Test]
        public void WithNoneWithInvalidGenericTypeTest()
        {
            AssertProducesError(typeof(WithNoneWithInvalidGenericType), nameof(UserError.DC0051), "GenericType`1");
        }

        class WithNoneWithInvalidGenericType : TestSystemBase
        {
            struct GenericType<TValue> : IComponentData {}
            void Test()
            {
                Entities
                    .WithNone<GenericType<int>>()
                    .ForEach((in Boid translation) => {})
                    .Schedule();
            }
        }

        [Test]
        public void ParameterWithInvalidGenericParameterTest()
        {
            AssertProducesError(typeof(ParameterWithInvalidGenericParameter), nameof(UserError.DC0050), "TValue");
        }

        class ParameterWithInvalidGenericParameter : TestSystemBase
        {
            void Test<TValue>() where TValue : struct
            {
                Entities
                    .ForEach((in TValue generic) => {})
                    .Schedule();
            }
        }

        [Test]
        public void ParameterWithInvalidGenericTypeTest()
        {
            AssertProducesError(typeof(ParameterWithInvalidGenericType), nameof(UserError.DC0050), "GenericType`1");
        }

        class ParameterWithInvalidGenericType : TestSystemBase
        {
            struct GenericType<TValue> : IComponentData {}
            void Test()
            {
                Entities
                    .ForEach((in GenericType<int> generic) => {})
                    .Schedule();
            }
        }

        [Test]
        public void ParameterWithInvalidGenericDynamicBufferTypeTest()
        {
            AssertProducesError(typeof(ParameterWithInvalidGenericDynamicBufferType), nameof(UserError.DC0050), "GenericBufferType`1<Unity.Entities.Tests.EcsTestData>");
        }

        class ParameterWithInvalidGenericDynamicBufferType : TestSystemBase
        {
            public struct GenericBufferType<T> : IBufferElementData where T : struct, IComponentData {}
            void Test()
            {
                Entities
                    .ForEach((ref DynamicBuffer<GenericBufferType<EcsTestData>> buffer) => {})
                    .Schedule();
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

        class WithReadOnly_IllegalArgument : TestSystemBase
        {
            void Test()
            {
                Entities
                    .WithReadOnly("stringLiteral")
                    .ForEach((in Boid translation) => {})
                    .Schedule();
            }
        }

        [Test]
        public void WithReadOnly_NonCapturedVariable_Test()
        {
            AssertProducesError(typeof(WithReadOnly_NonCapturedVariable), nameof(UserError.DC0012));
        }

        class WithReadOnly_NonCapturedVariable : TestSystemBase
        {
            void Test()
            {
                var myNativeArray = new NativeArray<float>();

                Entities
                    .WithReadOnly(myNativeArray)
                    .ForEach((in Boid translation) => {})
                    .Schedule();
            }
        }

        [Test]
        public void WithDeallocateOnJobCompletion_WithRun_NonCapturedVariable_Test()
        {
            AssertProducesError(typeof(WithDeallocateOnJobCompletion_WithRun_NonCapturedVariable), nameof(UserError.DC0012));
        }

        class WithDeallocateOnJobCompletion_WithRun_NonCapturedVariable : TestSystemBase
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

        class WithUnsupportedParameter : TestSystemBase
        {
            void Test()
            {
                Entities
                    .ForEach((string whoKnowsWhatThisMeans, in Boid translation) => {})
                    .Schedule();
            }
        }

        [Test]
        public void WithCapturedReferenceTypeTest()
        {
            AssertProducesError(typeof(WithCapturedReferenceType), nameof(UserError.DC0004));
        }

        class WithCapturedReferenceType : TestSystemBase
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
                    .Schedule();
            }
        }

        [Test]
        public void NestedScopeWithNonLambdaJobLambdaTest()
        {
            AssertProducesNoError(typeof(NestedScopeWithNonLambdaJobLambda));
        }

        class NestedScopeWithNonLambdaJobLambda : TestSystemBase
        {
            void Test()
            {
                var outerValue = 3.0f;
                {
                    var innerValue = 3.0f;
                    Entities
                        .ForEach((ref Translation t) => { t.Value = outerValue + innerValue; })
                        .Schedule();
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

        class CaptureFieldInLocalCapturingLambda : TestSystemBase
        {
            private int myfield = 123;

            void Test()
            {
                int also_capture_local = 1;
                Entities
                    .ForEach((ref Translation t) => { t.Value = myfield + also_capture_local; })
                    .Schedule();
            }
        }

        [Test]
        public void InvokeBaseMethodInBurstLambdaTest()
        {
            AssertProducesError(typeof(InvokeBaseMethodInBurstLambda), nameof(UserError.DC0002), "get_EntityManager");
        }

        class InvokeBaseMethodInBurstLambda : TestSystemBase
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
            AssertProducesError(typeof(InvokeExtensionMethodOnRefTypeInBurstLambda), nameof(UserError.DC0002), nameof(TestSystemBaseExtensionMethods.MyTestExtension));
        }

        class InvokeExtensionMethodOnRefTypeInBurstLambda : TestSystemBase
        {
            void Test()
            {
                Entities.ForEach((ref Translation t) => { this.MyTestExtension(); }).Run();
            }
        }

        [Test]
        public void InvokeInterfaceExtensionMethodOnRefTypeInBurstLambdaTest()
        {
            AssertProducesError(typeof(InvokeInterfaceExtensionMethodOnRefTypeInBurstLambda), nameof(UserError.DC0002), nameof(TestSystemBaseExtensionMethods.MyTestExtensionInterface));
        }

        class InvokeInterfaceExtensionMethodOnRefTypeInBurstLambda : TestSystemBase, TestSystemBaseExtensionMethods.ITestInterface
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

        class InvokeStaticFunctionOnRefTypeInBurstLambda : TestSystemBase
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
            internal static void ActualProblem(SystemBase s) {}
        }

        [Test]
        public void InvokeStaticFunctionOnValueTypeInBurstLambdaTest()
        {
            AssertProducesError(typeof(InvokeStaticFunctionOnValueTypeInBurstLambda), nameof(UserError.DC0002), nameof(InvokeStaticFunctionOnValueTypeInBurstLambda.ActualProblem));
        }

        class InvokeStaticFunctionOnValueTypeInBurstLambda : TestSystemBase
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

        class SharedComponentDataUsingSchedule : TestSystemBase
        {
            struct MySharedComponentData : ISharedComponentData
            {
            }

            void Test()
            {
                Entities
                    .ForEach((MySharedComponentData mydata) => {})
                    .Schedule();
            }
        }

        [Test]
        public void SharedComponentDataReceivedByRef_ProducesError()
        {
            AssertProducesError(typeof(SharedComponentDataReceivedByRef), nameof(UserError.DC0020), "MySharedComponentData");
        }

        class SharedComponentDataReceivedByRef : TestSystemBase
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

        class CustomStructArgumentThatDoesntImplementSupportedInterface : TestSystemBase
        {
            struct ForgotToAddInterface
            {
            }

            void Test()
            {
                Entities
                    .ForEach((ref ForgotToAddInterface t) => {})
                    .Schedule();
            }
        }


        [Test]
        public void CaptureFromMultipleScopesTest()
        {
            AssertProducesNoError(typeof(CaptureFromMultipleScopes));
        }

        class CaptureFromMultipleScopes : TestSystemBase
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
                            .Schedule();
                    }
                }
            }
        }

        [Test]
        public void CaptureFieldInNonLocalCapturingLambdaTest()
        {
            AssertProducesError(typeof(CaptureFieldInNonLocalCapturingLambda), nameof(UserError.DC0001), "myfield");
        }

        class CaptureFieldInNonLocalCapturingLambda : TestSystemBase
        {
            private int myfield = 123;

            void Test()
            {
                Entities
                    .ForEach((ref Translation t) => { t.Value = myfield; })
                    .Schedule();
            }
        }

        [Test]
        public void CaptureFieldByRefTest()
        {
            AssertProducesError(typeof(CaptureFieldByRef), nameof(UserError.DC0001), "m_MyField");
        }

        class CaptureFieldByRef : TestSystemBase
        {
            int m_MyField = 123;

            void Test()
            {
                Entities
                    .ForEach((ref Translation t) => { NotAProblem(ref m_MyField); })
                    .Schedule();
            }

            static void NotAProblem(ref int a) {}
        }

        [Test]
        public void InvokeInstanceMethodInCapturingLambdaTest()
        {
            AssertProducesError(typeof(InvokeInstanceMethodInCapturingLambda), nameof(UserError.DC0002));
        }

        class InvokeInstanceMethodInCapturingLambda : TestSystemBase
        {
            public object GetSomething(int i) => default;

            void Test()
            {
                int also_capture_local = 1;
                Entities
                    .ForEach((ref Translation t) => { GetSomething(also_capture_local); })
                    .Schedule();
            }
        }

        [Test]
        public void InvokeInstanceMethodInNonCapturingLambdaTest()
        {
            AssertProducesError(typeof(InvokeInstanceMethodInNonCapturingLambda), nameof(UserError.DC0002));
        }

        class InvokeInstanceMethodInNonCapturingLambda : TestSystemBase
        {
            public object GetSomething(int i) => default;

            void Test()
            {
                Entities
                    .ForEach((ref Translation t) => { GetSomething(3); })
                    .Schedule();
            }
        }


        [Test]
        public void LocalFunctionThatWritesBackToCapturedLocalTest()
        {
            AssertProducesError(typeof(LocalFunctionThatWritesBackToCapturedLocal), nameof(UserError.DC0013));
        }

        class LocalFunctionThatWritesBackToCapturedLocal : TestSystemBase
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
                }).Schedule();
            }
        }

        [Test]
        public void LocalFunctionThatReturnsValueTest()
        {
            AssertProducesNoError(typeof(LocalFunctionThatReturnsValue));
        }

        class LocalFunctionThatReturnsValue : TestSystemBase
        {
            struct SomeReturnType {}
            void Test()
            {
                Entities
                    .ForEach((ref Translation t) =>
                    {
                        SomeReturnType LocalFunctionThatReturnsValue()
                        {
                            return default;
                        }

                        var val = LocalFunctionThatReturnsValue();
                    }).Schedule();
            }
        }

        [Test]
        public void LocalFunctionThatReturnsValueByRefTest()
        {
            AssertProducesNoError(typeof(LocalFunctionThatReturnsValueByRef));
        }

        class LocalFunctionThatReturnsValueByRef : TestSystemBase
        {
            SomeReturnType someValue = new SomeReturnType();

            struct SomeReturnType {}
            void Test()
            {
                Entities
                    .ForEach((ref Translation t) =>
                    {
                        ref SomeReturnType LocalFunctionThatReturnsValueByRef()
                        {
                            return ref someValue;
                        }

                        var valByRef = LocalFunctionThatReturnsValueByRef();
                    }).Schedule();
            }
        }

        [Test]
        public void LambdaThatWritesBackToCapturedLocalTest()
        {
            AssertProducesError(typeof(LambdaThatWritesBackToCapturedLocal), nameof(UserError.DC0013));
        }

        class LambdaThatWritesBackToCapturedLocal : TestSystemBase
        {
            void Test()
            {
                int capture_me = 123;
                Entities
                    .ForEach((ref Translation t) => { capture_me++; }).Schedule();
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

        class ManagedComponentInBurstJob : TestSystemBase
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

        class ManagedComponentInSchedule : TestSystemBase
        {
            void Test()
            {
                Entities.ForEach((ManagedComponent t) => {}).Schedule();
            }
        }

        [Test]
        public void ManagedComponentByReferenceTest()
        {
            AssertProducesError(typeof(ManagedComponentByReference), nameof(UserError.DC0024));
        }

        class ManagedComponentByReference : TestSystemBase
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

        class WithAllWithSharedFilter : TestSystemBase
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
                    .Schedule();
            }
        }


        [Test]
        public void WithSwitchStatementTest()
        {
            AssertProducesNoError(typeof(WithSwitchStatement));
        }

        class WithSwitchStatement : TestSystemBase
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

        class HasTypesInAnotherAssembly : TestSystemBase
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

        class LambdaThatMakesNonExplicitStructuralChanges : TestSystemBase
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

        class LambdaThatMakesStructuralChangesWithSchedule : TestSystemBase
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
                    }).Schedule();
            }
        }

        [Test]
        public void LambdaThatHasNestedLambdaTest()
        {
            AssertProducesError(typeof(LambdaThatHasNestedLambda), nameof(UserError.DC0029));
        }

        class LambdaThatHasNestedLambda : TestSystemBase
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

        class LambdaThatTriesToStoreNonValidEntityQueryVariable : TestSystemBase
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

        class LambdaThatTriesToStoreLocalEntityQueryVariable : TestSystemBase
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
        class LambdaInSystemWithExecuteAlways : TestSystemBase
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

        class CallsMethodInComponentSystemBase : TestSystemBase
        {
            void Test()
            {
                Entities
                    .ForEach((ref Translation t) => { var targetDistance = Time.DeltaTime; })
                    .Schedule();
            }
        }


        [Test]
        public void IncorrectUsageOfBufferIsDetected()
        {
            AssertProducesError(typeof(IncorrectUsageOfBuffer), nameof(UserError.DC0033), "MyBufferFloat");
        }

        class IncorrectUsageOfBuffer : TestSystemBase
        {
            void Test()
            {
                Entities
                    .ForEach((MyBufferFloat f) => {})
                    .Schedule();
            }
        }

        [Test]
        public void CorrectUsageOfBufferIsNotDetected()
        {
            AssertProducesNoError(typeof(CorrectUsageOfBuffer));
        }

        class CorrectUsageOfBuffer : TestSystemBase
        {
            void Test()
            {
                Entities
                    .ForEach((DynamicBuffer<MyBufferFloat> f) => {})
                    .Schedule();
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

        class ParameterNamesPath : TestSystemBase
        {
            void Test()
            {
                Entities
                    .WithName("ParameterNamesTest")
                    .ForEach((DynamicBuffer<MyBufferFloat> floatBuffer) => {})
                    .Schedule();
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

        class CorrectReadOnlyUsageWithNativeContainer : TestSystemBase
        {
            void Test()
            {
                NativeArray<int> array = default;
                Entities.WithReadOnly(array).ForEach((ref Translation t) => { t.Value += array[0]; }).Schedule();
            }
        }

        class CorrectReadOnlyUsageWithStruct : TestSystemBase
        {
            void Test()
            {
                StructWithNativeContainer structWithNativeContainer = default;
                Entities.WithReadOnly(structWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithNativeContainer.array[0];
                }).Schedule();

                StructWithStructWithNativeContainer structWithStructWithNativeContainer = default;
                Entities.WithReadOnly(structWithStructWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithStructWithNativeContainer.innerStruct.array[0];
                }).Schedule();
            }
        }

        class IncorrectReadOnlyUsageWithStruct : TestSystemBase
        {
            void Test()
            {
                StructWithPrimitiveType structWithPrimitiveType = default;
                Entities.WithReadOnly(structWithPrimitiveType).ForEach((ref Translation t) => { t.Value += structWithPrimitiveType.field; }).Schedule();
            }
        }

        class IncorrectReadOnlyUsageWithPrimitiveType : TestSystemBase
        {
            void Test()
            {
                int myVar = 0;
                Entities.WithReadOnly(myVar).ForEach((ref Translation t) => { t.Value += myVar; }).Schedule();
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

        class CorrectDeallocateOnJobCompletionUsageWithNativeContainer : TestSystemBase
        {
            void Test()
            {
                NativeArray<int> array = default;
                Entities.WithReadOnly(array).WithDisposeOnCompletion(array).ForEach((ref Translation t) => { t.Value += array[0]; }).Schedule();
            }
        }

        class CorrectDeallocateOnJobCompletionUsageWithStruct : TestSystemBase
        {
            void Test()
            {
                StructWithNativeContainer structWithNativeContainer = default;
                Entities.WithDisposeOnCompletion(structWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithNativeContainer.array[0];
                }).Schedule();

                StructWithStructWithNativeContainer structWithStructWithNativeContainer = default;
                Entities.WithDisposeOnCompletion(structWithStructWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithStructWithNativeContainer.innerStruct.array[0];
                }).Schedule();
            }
        }

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

        class CorrectDisableContainerSafetyRestrictionUsageWithNativeContainer : TestSystemBase
        {
            void Test()
            {
                NativeArray<int> array = default;
                Entities.WithReadOnly(array).WithNativeDisableContainerSafetyRestriction(array).ForEach((ref Translation t) => { t.Value += array[0]; }).Schedule();
            }
        }

        class CorrectDisableContainerSafetyRestrictionUsageWithStruct : TestSystemBase
        {
            void Test()
            {
                StructWithNativeContainer structWithNativeContainer = default;
                structWithNativeContainer.array = default;
                Entities.WithNativeDisableContainerSafetyRestriction(structWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithNativeContainer.array[0];
                }).Schedule();

                StructWithStructWithNativeContainer structWithStructWithNativeContainer = default;
                Entities.WithNativeDisableContainerSafetyRestriction(structWithStructWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithStructWithNativeContainer.innerStruct.array[0];
                }).Schedule();
            }
        }

        class IncorrectDisableContainerSafetyRestrictionUsageWithStruct : TestSystemBase
        {
            void Test()
            {
                StructWithPrimitiveType structWithPrimitiveType = default;
                structWithPrimitiveType.field = default;
                Entities.WithNativeDisableContainerSafetyRestriction(structWithPrimitiveType).ForEach((ref Translation t) =>
                {
                    t.Value += structWithPrimitiveType.field;
                }).Schedule();
            }
        }

        class IncorrectDisableContainerSafetyRestrictionUsageWithPrimitiveType : TestSystemBase
        {
            void Test()
            {
                int myVar = 0;
                Entities.WithNativeDisableContainerSafetyRestriction(myVar).ForEach((ref Translation t) => { t.Value += myVar; }).Schedule();
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

        class CorrectDisableParallelForRestrictionUsageWithNativeContainer : TestSystemBase
        {
            void Test()
            {
                NativeArray<int> array = default;
                Entities.WithNativeDisableParallelForRestriction(array).ForEach((ref Translation t) => { t.Value += array[0]; }).Schedule();
            }
        }

        class CorrectDisableParallelForRestrictionUsageWithStruct : TestSystemBase
        {
            void Test()
            {
                StructWithNativeContainer structWithNativeContainer = default;
                structWithNativeContainer.array = default;
                Entities.WithNativeDisableParallelForRestriction(structWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithNativeContainer.array[0];
                }).Schedule();

                StructWithStructWithNativeContainer structWithStructWithNativeContainer = default;
                Entities.WithNativeDisableParallelForRestriction(structWithStructWithNativeContainer).ForEach((ref Translation t) =>
                {
                    t.Value += structWithStructWithNativeContainer.innerStruct.array[0];
                }).Schedule();
            }
        }

        class IncorrectDisableParallelForRestrictionUsageWithStruct : TestSystemBase
        {
            void Test()
            {
                StructWithPrimitiveType structWithPrimitiveType = default;
                structWithPrimitiveType.field = default;
                Entities.WithNativeDisableParallelForRestriction(structWithPrimitiveType).ForEach((ref Translation t) =>
                {
                    t.Value += structWithPrimitiveType.field;
                }).Schedule();
            }
        }

        class IncorrectDisableParallelForRestrictionUsageWithPrimitiveType : TestSystemBase
        {
            void Test()
            {
                int myVar = 0;
                Entities.WithNativeDisableParallelForRestriction(myVar).ForEach((ref Translation t) => { t.Value += myVar; }).Schedule();
            }
        }

        [Test]
        public void AttributesErrorWhenUsedOnUserTypeFields()
        {
            AssertProducesError(typeof(CaptureFieldInUserStructLambda), nameof(UserError.DC0038), "UserStruct.Array");
        }

        class CaptureFieldInUserStructLambda : TestSystemBase
        {
            void Test()
            {
                var localStruct = new UserStruct() { Array = default };
                Entities
                    .WithReadOnly(localStruct.Array)
                    .ForEach((ref Translation t) => { t.Value += localStruct.Array[0]; })
                    .Schedule();
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

        public class ControlFlowInsideWithChainSystem : SystemBase
        {
            public bool maybe;

            protected override void OnUpdate()
            {
                Entities
                    .WithName(maybe ? "One" : "Two")
                    .ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value;
                        })
                    .Schedule();
            }
        }

        [Test]
        public void UsingConstructionMultipleTimesThrows()
        {
            AssertProducesError(typeof(UseConstructionMethodMultipleTimes), nameof(UserError.DC0009), "WithName");
        }

        public class UseConstructionMethodMultipleTimes : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .WithName("Cannot")
                    .WithName("Make up my mind")
                    .ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value;
                        })
                    .Schedule();
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

        public class InvalidJobNameWithSpaces : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .WithName("This name may not contain spaces")
                    .ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value;
                        })
                    .Schedule();
            }
        }

        public class InvalidJobNameStartsWithDigit : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .WithName("1job")
                    .ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value;
                        })
                    .Schedule();
            }
        }

        public class InvalidJobNameCompilerReservedName : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .WithName("__job")
                    .ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value;
                        })
                    .Schedule();
            }
        }

        [Test]
        public void ForgotToAddForEachTest()
        {
            AssertProducesError(typeof(ForgotToAddForEach), nameof(UserError.DC0006));
        }

        class ForgotToAddForEach : TestSystemBase
        {
            void Test()
            {
                Entities
                    .WithAny<Translation>()
                    .Schedule();
            }
        }


        [Test]
        public void WithoutScheduleInvocationTest()
        {
            AssertProducesError(typeof(WithoutScheduleInvocation), nameof(UserError.DC0011));
        }

        public class WithoutScheduleInvocation : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities.ForEach(
                    (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                    {
                        translation.Value += velocity.Value;
                    });
            }
        }

        [Test]
        public void WithLambdaStoredInFieldTest()
        {
            AssertProducesError(typeof(WithLambdaStoredInFieldSystem), nameof(UserError.DC0044));
        }

        public class WithLambdaStoredInFieldSystem : SystemBase
        {
            UniversalDelegates.R<Translation> _translationAction;

            protected override void OnUpdate()
            {
                _translationAction = (ref Translation t) => {};
                Entities.ForEach(_translationAction).Schedule();
            }
        }

        [Test]
        public void WithLambdaStoredInVariableTest()
        {
            AssertProducesError(typeof(WithLambdaStoredInVariableSystem), nameof(UserError.DC0044));
        }

        public class WithLambdaStoredInVariableSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                UniversalDelegates.R<Translation> translationAction = (ref Translation t) => {};
                Entities.ForEach(translationAction).Schedule();
            }
        }

        [Test]
        public void WithLambdaStoredInArgTest()
        {
            AssertProducesError(typeof(WithLambdaStoredInArgSystem), nameof(UserError.DC0044));
        }

        public class WithLambdaStoredInArgSystem : SystemBase
        {
            void Test(UniversalDelegates.R<Translation> action)
            {
                Entities.ForEach(action).Schedule();
            }

            protected override void OnUpdate()
            {
                Test((ref Translation t) => {});
            }
        }

        [Test]
        public void WithLambdaReturnedFromMethodTest()
        {
            AssertProducesError(typeof(WithLambdaReturnedFromMethodSystem), nameof(UserError.DC0044));
        }

        public class WithLambdaReturnedFromMethodSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities.ForEach(GetAction()).Schedule();
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

        public class JobWithCodeCapturingFieldInSystem_System : SystemBase
        {
            public int _someField;
            protected override void OnUpdate()
            {
                Job.WithCode(() =>
                {
                    _someField = 123;
                }).Run();
            }
        }

        [Test]
        public void JobWithCodeCapturingFieldInSystem()
        {
            AssertProducesError(typeof(JobWithCodeCapturingFieldInSystem_System), nameof(UserError.DC0001), "_someField");
        }
    }

    public static class TestSystemBaseExtensionMethods
    {
        public interface ITestInterface {}

        public static void MyTestExtension(this TestSystemBase test) {}
        public static void MyTestExtensionInterface(this ITestInterface test) {}
    }
}
#endif
