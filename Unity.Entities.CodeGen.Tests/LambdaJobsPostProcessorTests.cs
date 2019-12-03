using System;
using NUnit.Framework;
using Unity.Entities.CodeGen.Tests.LambdaJobs.Infrastructure;
using Unity.Collections;
using Unity.Entities.CodeGen.Tests.TestTypes;
using UnityEngine;

namespace Unity.Entities.CodeGen.Tests
{
    [TestFixture]
    public class LambdaJobsPostProcessorTests : PostProcessorTestBase
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
                        (ref Translation t) => { })
                    .Schedule(default);
                
                Entities
                    .WithName("VeryCommonName")
                    .ForEach(
                        (ref Translation t) => { })
                    .Schedule(default);
            }
        }

        [Test]
        public void ConflictingWithNoneTest()
        {
            AssertProducesError(typeof(ConflictingWithNone), nameof(UserError.DC0015), "Translation");
        }

        class ConflictingWithNone : TestJobComponentSystem
        {
            void Test()
            {
                Entities
                    .WithNone<Translation>()
                    .ForEach((in Translation translation) => { })
                    .Schedule(default);
            }
        }

        
        
        [Test]
        public void ConflictingWithNoneBufferElementTest()
        {
            AssertProducesError(typeof(ConflictingWithNoneBufferElement), nameof(UserError.DC0015), "MyBufferFloat");
        }

        class ConflictingWithNoneBufferElement : TestJobComponentSystem
        {
            void Test()
            {
                Entities
                    .WithNone<MyBufferFloat>()
                    .ForEach((in DynamicBuffer<MyBufferFloat> myBuffer) => { })
                    .Run();
            }
        }


        [Test]
        public void ConflictingWithNoneAndWithAnyTest()
        {
            AssertProducesError(typeof(ConflictingWithNoneAndWithAny), nameof(UserError.DC0016), "Translation");
        }

        class ConflictingWithNoneAndWithAny : TestJobComponentSystem
        {
            void Test()
            {
                Entities
                    .WithNone<Translation>()
                    .WithAny<Translation, Velocity>()
                    .ForEach((in Boid translation) => { })
                    .Schedule(default);
            }
        }
        
        
        [Test]
        public void WithNoneWithInvalidTypeTest()
        {
            AssertProducesError(typeof(WithNoneWithInvalidType), nameof(UserError.DC0025), "ANonIComponentDataClass");
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
                    .ForEach((in Boid translation) => { })
                    .Schedule(default);
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
                    .ForEach((in Boid translation) => { })
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
                    .ForEach((in Boid translation) => { })
                    .Schedule(default);
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
                    .ForEach((string whoKnowsWhatThisMeans, in Boid translation) => { })
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
            AssertProducesError(typeof(CustomStructArgumentThatDoesntImplementSupportedInterface), nameof(UserError.DC0021), "parameter t has type ForgotToAddInterface. This type is not a");
        }

        class CustomStructArgumentThatDoesntImplementSupportedInterface : TestJobComponentSystem
        {
            struct ForgotToAddInterface
            {
                
            }
            
            void Test()
            {
                Entities
                    .ForEach((ref ForgotToAddInterface t) => { })
                    .Schedule(default);
            }
        }

             
        [Test]
        public void CaptureFromTwoScopesTest()
        {
            AssertProducesError(typeof(CaptureFromTwoScopes), nameof(UserError.DC0022), "It looks like you're capturing local variables from two different scopes in the method. This is not supported yet");
        }

        class CaptureFromTwoScopes : TestJobComponentSystem
        {
            void Test()
            {
                int outerScope = 1;
                {
                    int innerScope = 2;
                    Entities
                        .ForEach((ref Translation t) => { t.Value = outerScope + innerScope;})
                        .Schedule(default);
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
                    .ForEach((in Boid translation) => { })
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
                EntityQuery query = null;

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
    }
}
