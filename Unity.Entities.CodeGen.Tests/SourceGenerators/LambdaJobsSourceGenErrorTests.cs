using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.CodeGen.Tests;
using Unity.Entities.CodeGen.Tests.SourceGenerationTests;
using Unity.Entities.CodeGen.Tests.TestTypes;
using Unity.Entities.Tests;
using Unity.Jobs;

namespace Unity.Entities.CodeGen.SourceGenerators.Tests
{
    [TestFixture]
    public class LambdaJobsSourceGenErrorTests : SourceGenTests
    {
        protected override Type[] DefaultCompilationReferenceTypes { get; } =
    {
            typeof(SystemBase),
            typeof(JobHandle),
            typeof(Burst.BurstCompileAttribute),
            typeof(Mathematics.float3),
            typeof(ReadOnlyAttribute),
            typeof(Translation),
            typeof(EcsTestData),
            typeof(TranslationInAnotherAssembly)
        };

        protected override string[] DefaultUsings { get; } =
            { "System", "Unity.Entities", "Unity.Entities.Tests", "Unity.Entities.CodeGen.Tests", "Unity.Collections" };

        [Test]
        public void DC0003_WithConflictingName()
        {
            const string source = @"
                public partial class WithConflictingName : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        Entities.WithName(""VeryCommonName"").ForEach((ref Translation t) => {}).Schedule();
                        Entities.WithName(""VeryCommonName"").ForEach((ref Translation t) => {}).Schedule();
                    }
                }";

            AssertProducesError(source, "DC0003");
        }

        [Test]
        public void DC0004_JobWithCodeCapturingFieldInSystem()
        {
            const string source = @"
            partial class JobWithCodeCapturingFieldInSystem_System : SystemBase
            {
                public int _someField;
                protected override void OnUpdate()
                {
                    Job
                        .WithCode(() => { _someField = 123; })
                        .Run();
                }
            }";

            AssertProducesError(source, "DC0004"/*, "_someField"*/);
        }

        [Test]
        public void DC0004_WithGetComponentAndCaptureOfThisTest()
        {
            const string source = @"
            partial class WithGetComponentAndCaptureOfThis : SystemBase
            {
                float someField = 3.0f;

                protected override void OnUpdate()
                {
                    Entities.ForEach((ref Translation translation) =>
                        {
                            var vel = GetComponent<Velocity>(default);
                            translation = new Translation() {Value = someField * vel.Value};
                        }).Schedule();
                }
            }";

            AssertProducesError(source, "DC0004"/*, "someField"*/);
        }

        [Test]
        public void DC0004_WithGetComponentAndCaptureOfThisAndVarTest()
        {
            const string source = @"
            partial class WithGetComponentAndCaptureOfThisAndVar : SystemBase
            {
                float someField = 3.0f;

                protected override void OnUpdate()
                {
                    float someVar = 2.0f;
                    Entities.ForEach((ref Translation translation) =>
                        {
                            var vel = GetComponent<Velocity>(default);
                            translation = new Translation() {Value = someField * vel.Value * someVar};
                        }).Schedule();
                }
            }";

            AssertProducesError(source, "DC0004"/*, "someField"*/);
        }

        [Test]
        public void DC0004_CaptureFieldInNonLocalCapturingLambdaTest()
        {
            const string source = @"
            partial class CaptureFieldInNonLocalCapturingLambda : SystemBase
            {
                private int myfield = 123;

                protected override void OnUpdate()
                {
                    Entities
                        .ForEach((ref Translation t) => { t.Value = myfield; })
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0004"/*,  "myfield"*/);
        }

        [Test]
        public void DC0004_CaptureFieldByRefTest()
        {
            const string source = @"
            partial class CaptureFieldByRef : SystemBase
            {
                int m_MyField = 123;

                protected override void OnUpdate()
                {
                    Entities
                        .ForEach((ref Translation t) =>{ NotAProblem(ref m_MyField); })
                        .Schedule();
                }

                static void NotAProblem(ref int a) {}
            }";

            AssertProducesError(source, "DC0004"/*, "m_MyField"*/);
        }

        [Test]
        public void DC0004_InvokeInstanceMethodInCapturingLambdaTest()
        {
            const string source = @"
            partial class InvokeInstanceMethodInCapturingLambda : SystemBase
            {
                public object GetSomething(int i) => default;

                protected override void OnUpdate()
                {
                    int also_capture_local = 1;
                    Entities
                        .ForEach((ref Translation t) => { GetSomething(also_capture_local); })
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0004");
        }

        [Test]
        public void DC0004_InvokeInstanceMethodInNonCapturingLambdaTest()
        {
            const string source = @"
            partial class InvokeInstanceMethodInNonCapturingLambda : SystemBase
            {
                public object GetSomething(int i) => default;

                protected override void OnUpdate()
                {
                    Entities
                        .ForEach((ref Translation t) => { GetSomething(3); })
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0004");
        }


        [Test]
        public void DC0004_CallsMethodInComponentSystemBaseTest()
        {
            const string source = @"
            partial class CallsMethodInComponentSystemBase : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                        .ForEach((ref Translation t) => { var targetDistance = Time.DeltaTime; })
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0004"/*, , "Time"*/);
        }

        [Test]
        public void DC0004_WithCapturedReferenceType()
        {
            const string source = @"
                public partial class WithCapturedReferenceType : SystemBase
                {
                    class CapturedClass
                    {
                        public float value;
                    }

                    protected override void OnUpdate()
                    {
                        var capturedClass = new CapturedClass() {value = 3.0f};
                        Entities
                            .ForEach((ref Translation t) => { t.Value = capturedClass.value; })
                            .Schedule();
                    }
                }";

            AssertProducesError(source, "DC0004");
        }

        [Test] // This should use DC0001 but we don't have a good way to do that with source generators yet
        public void DC0004_CaptureFieldInLocalCapturingLambdaTest()
        {
            const string source = @"
                public partial class CaptureFieldInLocalCapturingLambda : TestSystemBase
                {
                    private int field = 123;

                    protected override void OnUpdate()
                    {
                        int also_capture_local = 1;
                        Entities
                            .ForEach((ref Translation t) => { t.Value = field + also_capture_local; })
                            .Schedule();
                    }
                }";

            AssertProducesError(source, "DC0004"/*, "field"*/);
        }

        [Test]
        public void DC0004_InvokeBaseMethodInBurstLambdaTest()
        {
            const string source = @"
                public partial class InvokeBaseMethodInBurstLambdaTest : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        int version = 0;
                        Entities.ForEach((ref Translation t) => { version = EntityManager.Version; }).Run();
                    }
                }";

            AssertProducesError(source, "DC0004" /*, "get_EntityManager"*/);
        }

        [Test]
        public void DC0005_WithUnsupportedParameter()
        {
            const string source = @"
                public partial class WithUnsupportedParameter : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        Entities
                            .ForEach((string whoKnowsWhatThisMeans, in Boid translation) => {})
                            .Schedule();
                    }
                }";

            AssertProducesError(source, "DC0005");
        }

        [Test]
        public void DC0008_WithBurstWithNonLiteral()
        {
            const string source = @"
            partial class WithBurstWithNonLiteral : SystemBase
            {
                protected override void OnUpdate()
                {
                    var floatMode = Unity.Burst.FloatMode.Deterministic;
                    Entities
                        .WithBurst(floatMode)
                        .ForEach((in Boid translation) => {})
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0008", "WithBurst");
        }

        [Test]
        public void DC0009_UsingConstructionMultipleTimes()
        {
            const string source = @"
            partial class UsingConstructionMultipleTimes : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                        .WithName(""Cannot"")
                        .WithName(""Make up my mind"")
                        .ForEach((in Boid translation) => {})
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0009", "WithName");
        }

        [Test]
        public void DC0010_ControlFlowInsideWithChainTest()
        {
            const string source = @"
            partial class ControlFlowInsideWithChainSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    var maybe = false;
                    Entities
                        .WithName(maybe ? ""One"" : ""Two"")
                        .ForEach(
                            (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                            {
                                translation.Value += velocity.Value;
                            })
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0010");
        }

        [Test]
        public void DC0011_WithoutScheduleInvocationTest()
        {
            const string source = @"
            partial class WithoutScheduleInvocation : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value;
                        });
                }
            }";
            AssertProducesError(source, "DC0011");
        }

        [Test]
        public void DC0012_WithReadOnly_IllegalArgument_Test()
        {
            const string source = @"
            partial class WithReadOnly_IllegalArgument : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                        .WithReadOnly(""stringLiteral"")
                        .ForEach((in Boid translation) => {})
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0012", "stringLiteral");
        }

        [Test]
        public void DC0012_WithReadOnly_NonCapturedVariable_Test()
        {
            const string source = @"
            partial class WithReadOnly_NonCapturedVariable : SystemBase
            {
                protected override void OnUpdate()
                {
                    var myNativeArray = new NativeArray<float>();

                    Entities
                        .WithReadOnly(myNativeArray)
                        .ForEach((in Boid translation) => {})
                        .Schedule();
                }
            }";
            AssertProducesError(source, "DC0012", "myNativeArray");
        }

        [Test]
        public void DC0012_WithDisposeOnCompletion_WithRun_NonCapturedVariable_Test()
        {
            const string source = @"
            partial class WithDisposeOnCompletion_WithRun_NonCapturedVariable : SystemBase
            {
                protected override void OnUpdate()
                {
                    var myNativeArray = new NativeArray<float>();

                    Entities
                        .WithDisposeOnCompletion(myNativeArray)
                        .ForEach((in Boid translation) => {})
                        .Run();
                }
            }";

            AssertProducesError(source, "DC0012", "WithDisposeOnCompletion");
        }

        [Test]
        public void DC0013_LocalFunctionThatWritesBackToCapturedLocalTest()
        {
            const string source = @"
            partial class LocalFunctionThatWritesBackToCapturedLocal : SystemBase
            {
                protected override void OnUpdate()
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
                    var test = capture_me;
                }
            }";
            AssertProducesError(source, "DC0013", "capture_me");
        }

        [Test]
        public void DC0013_LambdaThatWritesBackToCapturedLocalTest()
        {
            const string source = @"
            partial class LambdaThatWritesBackToCapturedLocal : SystemBase
            {
                protected override void OnUpdate()
                {
                    int capture_me = 123;
                    Entities.ForEach((ref Translation t) => { capture_me++; }).Schedule();
                    var test = capture_me;
                }
            }";
            AssertProducesError(source, "DC0013", "capture_me");
        }

        [Test]
        public void DC0014_UseOfUnsupportedIntParamInLambda()
        {
            const string source = @"
            partial class UseOfUnsupportedIntParamInLambda : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((int NotAValidParam, ref Translation t) => { }).Schedule();
                }
            }";
            AssertProducesError(source, "DC0014", "NotAValidParam");
        }

        [Test]
        public void DC0223_UseSharedComponentDataUsingSchedule()
        {
            const string source = @"
            partial class SharedComponentDataUsingSchedule : SystemBase
            {
                struct MySharedComponentData : ISharedComponentData {}

                protected override void OnUpdate()
                {
                    Entities
                        .ForEach((MySharedComponentData mydata) => {})
                        .Schedule();
                }
            }";
            AssertProducesError(source, "DC0223", "MySharedComponentData");
        }

        [Test]
        public void DC0020_SharedComponentDataReceivedByRef()
        {
            const string source = @"
            partial class SharedComponentDataReceivedByRef : SystemBase
            {
                struct MySharedComponentData : ISharedComponentData {}

                protected override void OnUpdate()
                {
                    Entities
                        .WithoutBurst()
                        .ForEach((ref MySharedComponentData mydata) => {})
                        .Run();
                }
            }";
            AssertProducesError(source, "DC0020", "MySharedComponentData");
        }

        [Test]
        public void DC0021_CustomStructArgumentThatDoesntImplementSupportedInterfaceTest()
        {
            const string source = @"
            partial class CustomStructArgumentThatDoesntImplementSupportedInterface : SystemBase
            {
                struct ForgotToAddInterface {}

                protected override void OnUpdate()
                {
                    Entities
                        .ForEach((ref ForgotToAddInterface t) => {})
                        .Schedule();
                }
            }";
            AssertProducesError(source, "DC0021", "parameter 't' has type ForgotToAddInterface. This type is not a");
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS

        [Test]
        public void DC0223_ManagedComponentInBurstJobTest()
        {
            const string source = @"
            partial class ManagedComponentInBurstJobTest : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((ManagedComponent t) => {}).Run();
                }
            }
            class ManagedComponent : IComponentData, IEquatable<ManagedComponent>
            {
                public bool Equals(ManagedComponent other) => false;
                public override bool Equals(object obj) => false;
                public override int GetHashCode() =>  0;
            }";

            AssertProducesError(source, "DC0223");
        }

        [Test]
        public void DC0223_ManagedComponentInSchedule()
        {
            const string source = @"
            partial class ManagedComponentInSchedule : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((ManagedComponent t) => {}).Schedule();
                }
            }
            class ManagedComponent : IComponentData, IEquatable<ManagedComponent>
            {
                public bool Equals(ManagedComponent other) => false;
                public override bool Equals(object obj) => false;
                public override int GetHashCode() =>  0;
            }";

            AssertProducesError(source, "DC0223");
        }

        [Test]
        public void DC0024_ManagedComponentByReference()
        {
            const string source = @"
            partial class ManagedComponentByReference : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.WithoutBurst().ForEach((ref ManagedComponent t) => {}).Run();
                }
            }
            class ManagedComponent : IComponentData, IEquatable<ManagedComponent>
            {
                public bool Equals(ManagedComponent other) => false;
                public override bool Equals(object obj) => false;
                public override int GetHashCode() =>  0;
            }";

            AssertProducesError(source, "DC0024");
        }
#endif

        [Test]
        public void DC0025_SystemWithDefinedOnCreateForCompiler()
        {
            const string source = @"
            partial class SystemWithDefinedOnCreateForCompiler : SystemBase
            {
                protected override void OnCreateForCompiler() {}

                protected override void OnUpdate() {
                    Entities.ForEach((in Boid translation) => {}).Schedule();
                }
            }";

            AssertProducesError(source, "DC0025", "SystemWithDefinedOnCreateForCompiler");
        }

        [Test]
        public void DC0026_WithAllWithSharedFilterTest()
        {
            const string source = @"
            partial class WithAllWithSharedFilter : SystemBase
            {
                struct MySharedComponentData : ISharedComponentData { public int Value; }

                protected override void OnUpdate()
                {
                    Entities
                        .WithAll<MySharedComponentData>()
                        .WithSharedComponentFilter(new MySharedComponentData() { Value = 3 })
                        .ForEach((in Boid translation) => {})
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0026", "MySharedComponentData");
        }

        [Test]
        public void DC0027_LambdaThatMakesNonExplicitStructuralChangesTest()
        {
            const string source = @"
            partial class LambdaThatMakesNonExplicitStructuralChanges : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                        .WithoutBurst()
                        .ForEach((Entity entity, ref Translation t) =>
                        {
                            EntityManager.RemoveComponent<Translation>(entity);
                        }).Run();
                }
            }";

            AssertProducesError(source, "DC0027");
        }

        [Test]
        public void DC0027_LambdaThatMakesNonExplicitStructuralChangesTestFromCapturedEntityManager()
        {
            const string source = @"
            partial class LambdaThatMakesNonExplicitStructuralChangesTestFromCapturedEntityManager : SystemBase
            {
                protected override void OnUpdate()
                {
                    var em = EntityManager;
                    Entities
                        .WithoutBurst()
                        .ForEach((ref Translation t) =>
                        {
                            em.CreateEntity();
                        }).Run();
                }
            }";

            AssertProducesError(source, "DC0027");
        }


        [Test]
        public void DC0027_LambdaThatMakesStructuralChangesWithScheduleTest()
        {
            const string source = @"
            partial class LambdaThatMakesStructuralChangesWithSchedule : SystemBase
            {
                protected override void OnUpdate()
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
            }";

            AssertProducesError(source, "DC0027");
        }

        [Test]
        public void DC0029_LambdaThatHasNestedLambdaTest()
        {
            const string source = @"
            partial class LambdaThatHasNestedLambda : SystemBase
            {
                protected override void OnUpdate()
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
            }";

            AssertProducesError(source, "DC0029");
        }

        [Test]
        public void DC0031_LambdaThatTriesToStoreNonValidEntityQueryVariableTest()
        {
            const string source = @"
            partial class LambdaThatTriesToStoreNonValidEntityQueryVariable : SystemBase
            {
                partial class EntityQueryHolder
                {
                    public EntityQuery m_Query;
                }

                protected override void OnUpdate()
                {
                    EntityQueryHolder entityQueryHolder = new EntityQueryHolder();

                    float delta = 0.0f;
                    Entities
                        .WithStoreEntityQueryInField(ref entityQueryHolder.m_Query)
                        .ForEach((Entity e2, ref Translation t2) => { delta += 1.0f; }).Run();
                }
            }";

            AssertProducesError(source, "DC0031");
        }

        [Test]
        public void DC0031_LambdaThatTriesToStoreLocalEntityQueryVariableTest()
        {
            const string source = @"
            partial class LambdaThatTriesToStoreLocalEntityQueryVariable : SystemBase
            {
                protected override void OnUpdate()
                {
                    EntityQuery query = default;

                    float delta = 0.0f;
                    Entities
                        .WithStoreEntityQueryInField(ref query)
                        .ForEach((Entity e2, ref Translation t2) => { delta += 1.0f; }).Run();
                }
            }";

            AssertProducesError(source, "DC0031");
        }

        [Test]
        public void DC0033_IncorrectUsageOfBufferIsDetected()
        {
            const string source = @"
            partial class IncorrectUsageOfBuffer : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                        .ForEach((MyBufferFloat f) => {})
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0033", "MyBufferFloat");
        }

        [Test]
        public void DC0034_ReadOnlyWarnsAboutArgumentType_IncorrectReadOnlyUsageWithStruct()
        {
            const string source = @"
            partial class IncorrectReadOnlyUsageWithStruct : SystemBase
            {
                struct StructWithPrimitiveType { public int field; }
                protected override void OnUpdate()
                {
                    StructWithPrimitiveType structWithPrimitiveType = default;
                    structWithPrimitiveType.field = default;
                    Entities
                        .WithReadOnly(structWithPrimitiveType)
                        .ForEach((ref Translation t) => { t.Value += structWithPrimitiveType.field; })
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0034", "structWithPrimitiveType");
        }

        [Test]
        public void DC0034_ReadOnlyWarnsAboutArgumentType_IncorrectReadOnlyUsageWithPrimitiveType()
        {
            const string source = @"
            partial class IncorrectReadOnlyUsageWithPrimitiveType : SystemBase
            {
                protected override void OnUpdate()
                {
                    var myVar = 0;
                    Entities
                        .WithReadOnly(myVar)
                        .ForEach((ref Translation t) => { t.Value += myVar; })
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0034", "myVar");
        }

        [Test]
        public void DC0036_DisableContainerSafetyRestrictionWarnsAboutArgumentType_IncorrectDisableContainerSafetyRestrictionUsageWithStruct()
        {
            const string source = @"
            partial class IncorrectDisableContainerSafetyRestrictionUsageWithStruct : SystemBase
            {
                struct StructWithPrimitiveType { public int field; }
                protected override void OnUpdate()
                {
                    StructWithPrimitiveType structWithPrimitiveType = default;
                    structWithPrimitiveType.field = default;
                    Entities
                        .WithNativeDisableContainerSafetyRestriction(structWithPrimitiveType)
                        .ForEach((ref Translation t) =>
                        {
                            t.Value += structWithPrimitiveType.field;
                        }).Schedule();
                }
            }";

            AssertProducesError(source, "DC0036", "structWithPrimitiveType");
        }

        [Test]
        public void DC0036_DisableContainerSafetyRestrictionWarnsAboutArgumentType_IncorrectDisableContainerSafetyRestrictionUsageWithPrimitiveType()
        {
            const string source = @"
            partial class IncorrectDisableContainerSafetyRestrictionUsageWithPrimitiveType : SystemBase
            {
                protected override void OnUpdate()
                {
                    var myVar = 0;
                    Entities
                        .WithNativeDisableContainerSafetyRestriction(myVar)
                        .ForEach((ref Translation t) => { t.Value += myVar; })
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0036", "myVar");
        }

        [Test]
        public void DC0037_DisableParallelForRestrictionWarnsAboutArgumentType_IncorrectDisableParallelForRestrictionUsageWithStruct()
        {
            const string source = @"
            partial class IncorrectDisableParallelForRestrictionUsageWithStruct : SystemBase
            {
                struct StructWithPrimitiveType { public int field; }
                protected override void OnUpdate()
                {
                    StructWithPrimitiveType structWithPrimitiveType = default;
                    structWithPrimitiveType.field = default;
                    Entities
                        .WithNativeDisableParallelForRestriction(structWithPrimitiveType)
                        .ForEach((ref Translation t) => { t.Value += structWithPrimitiveType.field; }).Schedule();
                }
            }";

            AssertProducesError(source, "DC0037", "structWithPrimitiveType");
        }

        [Test]
        public void DC0037_DisableParallelForRestrictionWarnsAboutArgumentType_IncorrectDisableParallelForRestrictionUsageWithPrimitiveType()
        {
            const string source = @"
            partial class IncorrectDisableParallelForRestrictionUsageWithPrimitiveType : SystemBase
            {
                protected override void OnUpdate()
                {
                    var myVar = 0;
                    Entities
                        .WithNativeDisableParallelForRestriction(myVar)
                        .ForEach((ref Translation t) => { t.Value += myVar; }).Schedule();
                }
            }";

            AssertProducesError(source, "DC0037", "myVar");
        }




        [Test]
        public void DC0043_InvalidJobNamesThrow_InvalidJobNameWithSpaces()
        {
            const string source = @"
            partial class InvalidJobNameWithSpaces : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                    .WithName(""This name may not contain spaces"")
                    .ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value;
                        })
                    .Schedule();
                }
            }";

            AssertProducesError(source, "DC0043", "WithName");
        }

        [Test]
        public void DC0043_InvalidJobNamesThrow_InvalidJobNameStartsWithDigit()
        {
            const string source = @"
            partial class InvalidJobNameStartsWithDigit : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                    .WithName(""1job"")
                    .ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value;
                        })
                    .Schedule();
                }
            }";

            AssertProducesError(source, "DC0043", "WithName");
        }

        [Test]
        public void DC0043_InvalidJobNamesThrow_InvalidJobNameCompilerReservedName()
        {
            const string source = @"
            partial class InvalidJobNameCompilerReservedName : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                    .WithName(""__job"")
                    .ForEach(
                        (ref Translation translation, ref Boid boid, in Velocity velocity) =>
                        {
                            translation.Value += velocity.Value;
                        })
                    .Schedule();
                }
            }";

            AssertProducesError(source, "DC0043", "__job");
        }

        [Test]
        public void DC0044_WithLambdaStoredInFieldTest()
        {
            const string source = @"
            partial class WithLambdaStoredInFieldSystem : SystemBase
            {
                Unity.Entities.UniversalDelegates.R<Translation> _translationAction;

                protected override void OnUpdate()
                {
                    _translationAction = (ref Translation t) => {};
                    Entities.ForEach(_translationAction).Schedule();
                }
            }";

            AssertProducesError(source, "DC0044");
        }

        [Test]
        public void DC0044_WithLambdaStoredInVariableTest()
        {
            const string source = @"
            partial class WithLambdaStoredInVariableSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    Unity.Entities.UniversalDelegates.R<Translation> translationAction = (ref Translation t) => {};
                    Entities.ForEach(translationAction).Schedule();
                }
            }";

            AssertProducesError(source, "DC0044");
        }

        [Test]
        public void DC0044_WithLambdaStoredInArgTest()
        {
            const string source = @"
            partial class WithLambdaStoredInArgSystem : SystemBase
            {
                void Test(Unity.Entities.UniversalDelegates.R<Translation> action)
                {
                    Entities.ForEach(action).Schedule();
                }

                protected override void OnUpdate()
                {
                    Test((ref Translation t) => {});
                }
            }";

            AssertProducesError(source, "DC0044");
        }

        [Test]
        public void DC0044_WithLambdaReturnedFromMethodTest()
        {
            const string source = @"
            partial class WithLambdaReturnedFromMethodSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach(GetAction()).Schedule();
                }

                static Unity.Entities.UniversalDelegates.R<Translation> GetAction()
                {
                    return (ref Translation t) => {};
                }
            }";

            AssertProducesError(source, "DC0044");
        }

        [Test]
        public void DC0046_SetComponentWithNotPermittedComponentAccessThatAliasesTest()
        {
            const string source = @"
            partial class SetComponentWithNotPermittedComponentAccessThatAliases : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((Entity e, in Translation data) => {
                        SetComponent(e, new Translation());
                    }).Run();
                }
            }";

            AssertProducesError(source, "DC0046", "SetComponent");
        }

        [Test]
        public void DC0047_SetComponentWithNotPermittedParameterThatAliasesTestTest()
        {
            const string source = @"
            partial class SetComponentWithNotPermittedParameterThatAliasesTest : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((Entity e, ref Translation data) => {
                        var translation = GetComponent<Translation>(e);
                    }).Run();
                }
            }";

            AssertProducesError(source, "DC0047", "Translation");
        }

        [Test]
        public void DC0050_ParameterWithInvalidGenericParameterTest()
        {
            const string source = @"
            partial class SomeClass<TValue> where TValue : struct
            {
                partial class ParameterWithInvalidGenericParameter : SystemBase
                {
                    protected override void OnUpdate() {}

                    void Test()
                    {
                        Entities
                            .ForEach((in TValue generic) => {})
                            .Schedule();
                    }
                }
            }";

            AssertProducesError(source, "DC0050", "TValue");
        }

        [Test]
        public void DC0050_ParameterWithInvalidGenericTypeTest()
        {
            const string source = @"
            partial class ParameterWithInvalidGenericType : SystemBase
            {
                struct GenericType<TValue> : IComponentData {}

                protected override void OnUpdate()
                {
                    Entities
                        .ForEach((in GenericType<int> generic) => {})
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0050", "GenericType");
        }

        [Test]
        public void DC0050_ParameterWithInvalidGenericDynamicBufferTypeTest()
        {
            const string source = @"
            partial class ParameterWithInvalidGenericDynamicBufferType : SystemBase
            {
                public struct GenericBufferType<T> : IBufferElementData where T : struct, IComponentData {}

                protected override void OnUpdate()
                {
                    Entities
                        .ForEach((ref DynamicBuffer<GenericBufferType<EcsTestData>> buffer) => {})
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0050", "GenericBufferType");
        }

        [Test]
        public void DC0051_WithNoneWithInvalidGenericParameterTest()
        {
            const string source = @"
            partial class SomeClass<TValue> where TValue : struct
            {
                partial class WithNoneWithInvalidGenericParameter : SystemBase
                {
                    protected override void OnUpdate() {}

                    void Test()
                    {
                        Entities
                            .WithNone<TValue>()
                            .ForEach((in Boid translation) => {})
                            .Schedule();
                    }
                }
            }";

            AssertProducesError(source, "DC0051", "TValue");
        }

        [Test]
        public void DC0051_WithNoneWithInvalidGenericTypeTest()
        {
            const string source = @"
            partial class WithNoneWithInvalidGenericType : SystemBase
            {
                struct GenericType<TValue> : IComponentData {}
                protected override void OnUpdate()
                {
                    Entities
                        .WithNone<GenericType<int>>()
                        .ForEach((in Boid translation) => {})
                        .Schedule();
                }
            }";
            AssertProducesError(source, "DC0051", "GenericType");
        }

        [Test]
        public void DC0052_WithNoneWithInvalidTypeTest()
        {
            const string source = @"
            partial class WithNoneWithInvalidType : SystemBase
            {
                class ANonIComponentDataClass
                {
                }
                protected override void OnUpdate()
                {
                    Entities
                        .WithNone<ANonIComponentDataClass>()
                        .ForEach((in Boid translation) => {})
                        .Schedule();
                }
            }";

            AssertProducesError(source, "DC0052", "ANonIComponentDataClass");
        }

        [Test]
        public void DC0053_InGenericSystemTypeTest()
        {
            const string source = @"
            partial class InGenericSystemType<T> : SystemBase where T : struct, IComponentData
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((Entity e) => {}).Run();
                }
            }";

            AssertProducesError(source, "DC0053", "InGenericSystemType");
        }

        [Test]
        public void DC0054_InGenericMethodThatCapturesTest()
        {
            const string source = @"
            partial class InGenericMethodThatCapturesType : SystemBase
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
            }";

            AssertProducesError(source, "DC0054", "Test");
        }

        [Test]
        public void DC0055_ComponentPassedByValueGeneratesWarning_Test()
        {
            const string source = @"
            partial class ComponentPassedByValueGeneratesWarning_System : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((Translation translation) => { }).Run();
                }
            }";

            AssertProducesWarning(source, "DC0055");
        }

        [Test]
        public void DC0056_InvalidWithNoneComponentGeneratesError_Test_WithNone_WithAll()
        {
            const string source = @"
            partial class InvalidWithNoneInWithAllComponentGeneratesError_System : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.WithNone<Translation>().WithAll<Translation>().ForEach((Entity entity) => { }).Run();
                }
            }";

            AssertProducesError(source, "DC0056", nameof(LambdaJobQueryConstructionMethods.WithNone), nameof(LambdaJobQueryConstructionMethods.WithAll));
        }

        [Test]
        public void DC0056_InvalidWithNoneComponentGeneratesError_Test_WithNone_WithAny()
        {
            const string source = @"
            partial class InvalidWithNoneInWithAnyComponentGeneratesError_System : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.WithNone<Translation>().WithAny<Translation>().ForEach((Entity entity) => { }).Run();
                }
            }";

            AssertProducesError(source, "DC0056", nameof(LambdaJobQueryConstructionMethods.WithNone), nameof(LambdaJobQueryConstructionMethods.WithAny));
        }

        [Test]
        public void DC0056_InvalidWithNoneComponentGeneratesError_Test_WithNone_LambdaParameter()
        {
            const string source = @"
            partial class InvalidWithNoneInLambdaParamComponentGeneratesError_System : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.WithNone<Translation>().ForEach((ref Translation translation) => { }).Run();
                }
            }";

            AssertProducesError(source, "DC0056", nameof(LambdaJobQueryConstructionMethods.WithNone), "lambda parameter");
        }

        [Test]
        public void DC0056_InvalidWithAnyComponentGeneratesError_Test_WithAny_WithAll()
        {
            const string source = @"
            partial class InvalidWithAnyInWithAllComponentGeneratesError_System : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.WithAny<Translation>().WithAll<Translation>().ForEach((Entity entity) => { }).Run();
                }
            }";

            AssertProducesError(source, "DC0056", nameof(LambdaJobQueryConstructionMethods.WithAny), nameof(LambdaJobQueryConstructionMethods.WithAll));
        }

        [Test]
        public void DC0056_InvalidWithAnyComponentGeneratesError_Test_WithAny_LambdaParameter()
        {
            const string source = @"
            partial class InvalidWithAnyInLambdaParamComponentGeneratesError_System : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.WithAny<Translation>().ForEach((ref Translation translation) => { }).Run();
                }
            }";

            AssertProducesError(source, "DC0056", nameof(LambdaJobQueryConstructionMethods.WithAny), "lambda parameter");
        }

        [Test]
        public void DC0057_JobWithCodeAndStructuralChanges_Test()
        {
            const string source = @"
            partial class JobWithCodeAndStructuralChanges_System : SystemBase
            {
                protected override void OnUpdate()
                {
                    Job.WithStructuralChanges().WithCode(() =>{ }).Run();
                }
            }";

            AssertProducesError(source, "DC0057");
        }

        [Test]
        public void DC0058_EntitiesForEachNotInPartialClass()
        {
            const string source = @"
            class EntitiesForEachNotInPartialClass : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((ref Translation translation) => {}).Schedule();
                }
            }";
            AssertProducesError(source, "DC0058", "EntitiesForEachNotInPartialClass");
        }

        [Test]
        public void DC0059_GetComponentDataFromEntityWithMethodAsParam_ProducesError()
        {
            const string source = @"
            partial class GetComponentDataFromEntityWithMethodAsParam : SystemBase
            {
                static bool MethodThatReturnsBool() => false;
                protected override void OnUpdate()
                {
                    Entities
                        .ForEach((Entity entity, in Translation tde) =>
                        {
                            GetComponentDataFromEntity<Velocity>(MethodThatReturnsBool());
                        }).Run();
                }
            }";
            AssertProducesError(source, "DC0059", "GetComponentDataFromEntity");
        }

        [Test]
        public void DC0059_GetComponentDataFromEntityWithVarAsParam_ProducesError()
        {
            const string source = @"
            partial class GetComponentDataFromEntityWithVarAsParam : SystemBase
            {
                protected override void OnUpdate()
                {
                    var localBool = false;
                    Entities.ForEach((Entity entity, in Translation tde) => { GetComponentDataFromEntity<Velocity>(localBool); }).Run();
                }
            }";
            AssertProducesError(source, "DC0059", "GetComponentDataFromEntity");
        }

        [Test]
        public void DC0059_GetComponentDataFromEntityWithArgAsParam_ProducesError()
        {
            const string source = @"
            partial class GetComponentDataFromEntityWithArgAsParam : SystemBase
            {
                protected override void OnUpdate() {}
                void Test(bool argBool)
                {
                    Entities.ForEach((Entity entity, in Translation tde) => { GetComponentDataFromEntity<Velocity>(argBool); }).Run();
                }
            }";

            AssertProducesError(source, "DC0059", "GetComponentDataFromEntity");
        }

        [Test]
        public void DC0060_EntitiesForEachInAssemblyNotReferencingBurst()
        {
            const string source = @"
            partial class EntitiesForEachInAssemblyNotReferencingBurst : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((Entity entity, in Translation tde) => { }).Run();
                }
            }";

            Type[] compilationReferenceTypes = { typeof(SystemBase), typeof(JobHandle), typeof(EcsTestData), typeof(ReadOnlyAttribute), typeof(Translation) };
            AssertProducesError(source, "DC0060", new[] { "Test" }, compilationReferenceTypes);
        }

        [Test]
        public void DC0063_SetComponentInScheduleParallel()
        {
            const string source = @"
            partial class SetComponentInScheduleParallel : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((Entity entity) =>
                    {
                        SetComponent(entity, new Translation());
                    }).ScheduleParallel();
                }
            }";

            AssertProducesError(source, "DC0063", new[] { "SetComponent", "Translation" });
        }

        [Test]
        public void DC0063_GetBufferInScheduleParallel()
        {
            const string source = @"
            partial class GetBufferInScheduleParallel : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((Entity entity) => {
                        var value = GetBuffer<EcsIntElement>(entity)[0].Value;
                    }).ScheduleParallel();
                }
            }";

            AssertProducesError(source, "DC0063", new[] { "GetBuffer", "EcsIntElement" });
        }

        [Test]
        public void DC0063_GetComponentDataFromEntityInScheduleParallel()
        {
            const string source = @"
            partial class GetComponentDataFromEntityInScheduleParallel : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((Entity entity) => {
                        var cdfe = GetComponentDataFromEntity<EcsTestData>(false);
                    }).ScheduleParallel();
                }
            }";

            AssertProducesError(source, "DC0063", new[] { "GetComponentDataFromEntity", "EcsTestData" });
        }

        [Test]
        public void DC0070_EntitiesForEach_IllegalDuplicateTypesUsed()
        {
            const string source = @"
            partial class DuplicateIComponentDataTypes : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((Entity entity, in Translation translation1, in Translation translation2) => { }).Run();
                }
            }";

            AssertProducesError(source, "DC0070", nameof(Translation));
        }


        [Test]
        public void DC0073_WithScheduleGranularity_Run()
        {
            const string source = @"
            partial class TestWithScheduleGranularity : SystemBase
            {

                protected override void OnUpdate()
                {
                    Entities
                        .WithScheduleGranularity(ScheduleGranularity.Chunk)
                        .ForEach((ref Translation t) =>
                        {
                        }).Run();
                }
            }";

            AssertProducesError(source, "DC0073");
        }
        [Test]
        public void DC0073_WithScheduleGranularity_Schedule()
        {
            const string source = @"
            partial class TestWithScheduleGranularity : SystemBase
            {

                protected override void OnUpdate()
                {
                    Entities
                        .WithScheduleGranularity(ScheduleGranularity.Chunk)
                        .ForEach((ref Translation t) =>
                        {
                        }).Schedule();
                }
            }";

            AssertProducesError(source, "DC0073");
        }
    }
}
