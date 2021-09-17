using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.CodeGen.Tests;
using Unity.Entities.CodeGen.Tests.TestTypes;
using Unity.Entities.Tests;
using Unity.Jobs;

namespace Unity.Entities.CodeGen.SourceGenerators.Tests
{
    [TestFixture]
    public class LambdaJobsSourceGenNoErrorTests : SourceGenTests
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
            typeof(NativeArray<>),
            typeof(TranslationInAnotherAssembly),
            typeof(BoidInAnotherAssembly)
        };

        protected override string[] DefaultUsings { get; } =
        {
            "System", "Unity.Entities", "Unity.Entities.CodeGen.Tests", "Unity.Entities.CodeGen.Tests.TestTypes", "Unity.Collections", "Unity.Entities.Tests", "Unity.Burst"
        };

        [Test]
        public void NoError_NoEntitiesForEach()
        {
            const string source =
                @"
                public partial class GhostSendSystem : SystemBase
                {
                    private new static SerializeJob32 Entities => new SerializeJob32();

                    [BurstCompile]
                    unsafe struct SerializeJob32
                    {
                        public void Execute(int idx)
                        {
                        }
                    }

                    protected override void OnUpdate()
                    {
                        Entities.Execute(idx: 1);
                    }
                }";

            AssertProducesNoError(source, null, true);
        }

        [Test]
        public void NoError_NoJobWithCode()
        {
            const string source =
                @"
                public partial class GhostSendSystem : SystemBase
                {
                    private new static SerializeJob32 Job => new SerializeJob32();

                    [BurstCompile]
                    unsafe struct SerializeJob32
                    {
                        public void Execute(int idx)
                        {
                        }
                    }

                    protected override void OnUpdate()
                    {
                        Job.Execute(idx: 1);
                    }
                }";

            AssertProducesNoError(source, null, true);
        }

        [Test]
        public void NoError_WithUnsupportedParameter()
        {
            const string source = @"
                public partial class NestedScopeWithNonLambdaJobLambda : SystemBase
                {
                    protected override void OnUpdate()
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
                }";

            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_CaptureFromMultipleScopes()
        {
            const string source = @"
                partial class CaptureFromMultipleScopes : SystemBase
                {
                    protected override void OnUpdate()
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
                }";

            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_LocalFunctionThatReturnsValue()
        {
            const string source = @"
                partial class LocalFunctionThatReturnsValueTest : SystemBase
                {
                    struct SomeReturnType {}
                    protected override void OnUpdate()
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
                }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_MultipleEntitiesForEachWithRunCallingMethodInCapturedStruct()
        {
            const string source = @"
                partial class MultipleEntitiesForEachWithRunCallingMethodInCapturedStruct : SystemBase
                {
                    public struct NetDebug { public void DebugLog(string msg) { UnityEngine.Debug.Log(msg); } }
                    protected override void OnUpdate()
                    {
                        var netDebug = new NetDebug();
                        Entities.ForEach((Entity entity) =>
                        {
                            var dbgMsg = 3;
                            netDebug.DebugLog(String.Format(""{0}"", dbgMsg));
                        }).Schedule(Dependency);

                        Entities.ForEach((Entity entity) =>
                        {
                            var dbgMsg = 3;
                            netDebug.DebugLog(String.Format(""{0}"", dbgMsg));
                        }).Schedule();
                    }
                }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_WithSwitchStatement()
        {
            const string source = @"
            partial class WithSwitchStatement : SystemBase
            {
                struct AbilityControl : IComponentData
                {
                    public enum State { Idle, Active, Cooldown }
                    public State behaviorState;
                }

                protected override void OnUpdate()
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
            }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_HasTypesInAnotherAssembly()
        {
            const string source = @"
            partial class HasTypesInAnotherAssembly : SystemBase
            {
                protected override void OnUpdate()
                {
                    var structWithNativeArrayInAnotherAssembly = new StructWithNativeArrayInAnotherAssembly();
                    Entities
                        .WithAll<BoidInAnotherAssembly>()
                        .WithNone<TranslationInAnotherAssembly>()
                        .WithReadOnly(structWithNativeArrayInAnotherAssembly)
                        .WithAny<AccelerationInAnotherAssembly>()
                        .WithoutBurst()
                        .ForEach((ref RotationInAnotherAssembly a) => { var val = structWithNativeArrayInAnotherAssembly.Array[0]; }).Run();
                }
            }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_CorrectUsageOfBufferIsNotDetected()
        {
            const string source = @"
            partial class CorrectUsageOfBuffer : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                        .ForEach((DynamicBuffer<MyBufferFloat> f) => {})
                        .Schedule();
                }
            }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_ReadOnlyWarnsAboutArgumentType_CorrectReadOnlyUsageWithNativeContainer()
        {
            const string source = @"
            partial class CorrectReadOnlyUsageWithNativeContainer : SystemBase
            {
                protected override void OnUpdate()
                {
                    NativeArray<int> array = default;
                    Entities.WithReadOnly(array).ForEach((ref Translation t) => { t.Value += array[0]; }).Schedule();
                }
            }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_ReadOnlyWarnsAboutArgumentType_CorrectReadOnlyUsageWithStruct()
        {
            const string source = @"
            struct StructWithNativeContainer            { public NativeArray<int> array; }
            struct StructWithStructWithNativeContainer  { public StructWithNativeContainer innerStruct; }

            partial class CorrectReadOnlyUsageWithStruct : SystemBase
            {
                protected override void OnUpdate()
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
            }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_DeallocateOnJobCompletionWarnsAboutArgumentType_CorrectDeallocateOnJobCompletionUsageWithNativeContainer()
        {
            const string source = @"
            partial class CorrectDeallocateOnJobCompletionUsageWithNativeContainer : SystemBase
            {
                protected override void OnUpdate()
                {
                    NativeArray<int> array = default;
                    Entities.WithReadOnly(array).WithDisposeOnCompletion(array).ForEach((ref Translation t) => { t.Value += array[0]; }).Schedule();
                }
            }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_DeallocateOnJobCompletionWarnsAboutArgumentType_CorrectDeallocateOnJobCompletionUsageWithStruct()
        {
            const string source = @"
            struct StructWithNativeContainer            { public NativeArray<int> array; }
            struct StructWithStructWithNativeContainer  { public StructWithNativeContainer innerStruct; }
            partial class CorrectDeallocateOnJobCompletionUsageWithStruct : SystemBase
            {
                protected override void OnUpdate()
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
            }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_DisableContainerSafetyRestrictionWarnsAboutArgumentType_CorrectDisableContainerSafetyRestrictionUsageWithNativeContainer()
        {
            const string source = @"
            partial class CorrectDisableContainerSafetyRestrictionUsageWithNativeContainer : SystemBase
            {
                protected override void OnUpdate()
                {
                    NativeArray<int> array = default;
                    Entities.WithReadOnly(array).WithNativeDisableContainerSafetyRestriction(array).ForEach((ref Translation t) => { t.Value += array[0]; }).Schedule();
                }
            }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_DisableContainerSafetyRestrictionWarnsAboutArgumentType_CorrectDisableContainerSafetyRestrictionUsageWithStruct()
        {
            const string source = @"
            struct StructWithNativeContainer            { public NativeArray<int> array; }
            struct StructWithStructWithNativeContainer  { public StructWithNativeContainer innerStruct; }
            partial class CorrectDisableContainerSafetyRestrictionUsageWithStruct : SystemBase
            {
                protected override void OnUpdate()
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
            }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_DisableParallelForRestrictionWarnsAboutArgumentType_CorrectDisableParallelForRestrictionUsageWithNativeContainer()
        {
            const string source = @"
            partial class CorrectDisableParallelForRestrictionUsageWithNativeContainer : SystemBase
            {
                protected override void OnUpdate()
                {
                    NativeArray<int> array = default;
                    Entities.WithNativeDisableParallelForRestriction(array).ForEach((ref Translation t) => { t.Value += array[0]; }).Schedule();
                }
            }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_DisableParallelForRestrictionWarnsAboutArgumentType_CorrectDisableParallelForRestrictionUsageWithStruct()
        {
            const string source = @"
            struct StructWithNativeContainer            { public NativeArray<int> array; }
            struct StructWithStructWithNativeContainer  { public StructWithNativeContainer innerStruct; }
            partial class CorrectDisableParallelForRestrictionUsageWithStruct : SystemBase
            {
                protected override void OnUpdate()
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
            }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_SetComponentWithPermittedAlias()
        {
            const string source = @"
            public partial class SetComponentWithPermittedAlias : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((Entity e, in Translation data) => {
                        var trans = GetComponent<Translation>(e);
                    }).Run();
                }
            }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_DuplicateComponentInQueryDoesNotProduceError()
        {
            const string source = @"
            public partial class DuplicateComponentInQueryDoesNotProduceError_System : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.WithAll<Translation>().ForEach((Entity entity, ref Translation rotation) => { }).Run();
                }
            }";
            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_EntitiesForEachWithoutUsings()
        {
            // Needed for access to extension methods (without giving access to Entities.ForEach)
            const string extensionMethodStubs = @"
            public static class LambdaJobDescriptionExecutionMethods
            {
                public static void Schedule<TDescription>(this TDescription description) where TDescription : struct {}
                public static void ScheduleParallel<TDescription>(this TDescription description) where TDescription : struct {}
                public static void Run<TDescription>(this TDescription description) where TDescription : struct {}
                public static TDescription WithStructuralChanges<TDescription>(this TDescription description) where TDescription : struct { return description; }
                public static TDescription WithoutBurst<TDescription>(this TDescription description) where TDescription : struct { return description; }
            }";

            const string source = @"
            public partial class EntitiesForEachWithoutUsings : Unity.Entities.SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach((Unity.Entities.Entity entity, ref Unity.Entities.CodeGen.Tests.Translation translation) => { }).Run();
                    Entities.ForEach((Unity.Entities.Entity entity, ref Unity.Entities.CodeGen.Tests.Translation translation) => { }).Schedule();
                    Entities.ForEach((Unity.Entities.Entity entity, ref Unity.Entities.CodeGen.Tests.Translation translation) => { }).ScheduleParallel();
                    Entities.WithStructuralChanges().WithoutBurst()
                        .ForEach((Unity.Entities.Entity entity, ref Unity.Entities.CodeGen.Tests.Translation translation) => { }).Run();
                }
            }";

            AssertProducesNoError(extensionMethodStubs + source, overrideDefaultUsings: Array.Empty<string>());
        }

        [Test]
        public void NoError_EntitiesForEachWithUnsafeBlock()
        {
            const string source =
                @"
                public partial class NoError_EntitiesForEachWithUnsafeBlock : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        unsafe
                        {
                            int someInt = 3;
                            int *somePtr = &someInt;
                            Entities.WithAll<Translation>().ForEach((Entity entity, ref Translation rotation) => {
                                var someVal = *somePtr;
                            }).Run();
                        }
                    }
                }";

            AssertProducesNoError(source, null, true);
        }

        [Test]
        public void NoError_LocalFunctionThatReturnsValueByRef()
        {
            const string source = @"
            partial class LocalFunctionThatReturnsValueByRef : SystemBase
            {
                SomeReturnType someValue = new SomeReturnType();
                struct SomeReturnType {}

                protected override void OnUpdate()
                {
                    Entities
                        .WithoutBurst()
                        .ForEach((ref Translation t) =>
                        {
                            ref SomeReturnType LocalFunctionThatReturnsValueByRef()
                            {
                                return ref someValue;
                            }

                            var valByRef = LocalFunctionThatReturnsValueByRef();
                        }).Run();
                }
            }";

            AssertProducesNoError(source);
        }

        [Test]
        public void NoError_WithScheduleGranularity()
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
                        }).ScheduleParallel();
                }
            }";

            AssertProducesNoError(source, null, true);
        }

        [Test]
        public void NoError_ConstantEnumUsedInEntitiesForEach()
        {
            const string source = @"
            partial class SomeSystem : SystemBase
            {
                [System.Flags]
                public enum SomeFlagsMask : uint
                {
                    NONE = 0x0,
                    ONE = 0x1,
                }

                protected override void OnUpdate()
                {
                    Entities.ForEach(() =>
                        {
                            SomeFlagsMask someFlags = default;
                            const SomeFlagsMask otherFlags = SomeFlagsMask.ONE;
                            someFlags &= otherFlags;
                        })
                        .ScheduleParallel();
                }
            }";

            AssertProducesNoError(source, null, true);
        }

        [Test]
        public void NoError_UseOfOtherEntitiesIdentifier()
        {
            const string source = @"
            namespace MyCompany
            {
                namespace Entities
                {
                    public static class Time
                    {
                        public static float DeltaTime;
                    }
                }
            }

            public partial class SomeSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.WithStructuralChanges().ForEach(() => {
                        var delta = MyCompany.Entities.Time.DeltaTime;
                    }).Run();
                }
            }";

            AssertProducesNoError(source, null, true);
        }

        [Test]
        public void NoError_InheritingSystemsWithSameMethod()
        {
            const string source = @"
            namespace Common
            {
                public partial class SomeSystemInClassHierarchy : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        Entities.ForEach(() => {}).ScheduleParallel();
                    }
                }
            }

            public partial class SomeSystemInClassHierarchy : Common.SomeSystemInClassHierarchy
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach(() => {}).ScheduleParallel();
                }
            }";

            AssertProducesNoError(source, null, true);
        }

        [Test]
        public void NoError_TwoSystemsInSameFileName()
        {
            const string source1 =
                @"
                public partial class System1 : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        Entities.ForEach((Entity e) => { }).Run();
                    }
                }";

            const string source2 =
                @"
                public partial class System2 : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        Entities.ForEach((Entity e) => { }).Run();
                    }
                }";

            AssertProducesNoError(null, true, ("Dir1/System.cs", source1), ("Dir2/System.cs", source2));
        }
    }
}
