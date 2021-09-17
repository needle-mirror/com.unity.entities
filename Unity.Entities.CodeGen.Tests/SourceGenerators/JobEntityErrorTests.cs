#if !SYSTEM_SOURCEGEN_DISABLED
using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities.CodeGen;
using Unity.Entities.CodeGen.Tests;
#if !UNITY_EDITOR_LINUX
using Unity.Jobs;

namespace Unity.Entities.CodeGen.SourceGenerators.Tests
{
    [TestFixture]
    public class JobEntityErrorTests : SourceGenTests
    {
        protected override Type[] DefaultCompilationReferenceTypes { get; } =
        {
            typeof(BurstCompileAttribute),
            typeof(Translation),
            typeof(SystemBase),
            typeof(NativeArray<>),
            typeof(JobHandle)
        };

        protected override string[] DefaultUsings { get; } =
        {
            "System", "Unity.Entities", "Unity.Collections", "Unity.Entities.CodeGen.Tests", "Unity.Burst"
        };

        [Test]
        public void SGJE0003_InvalidValueTypesInExecuteMethod1()
        {
            const string source =
                @"public partial struct WithInvalidValueTypeParameters : IJobEntity
                {
                    void Execute(Entity entity, float invalidFloat)
                    {
                    }
                }

                public partial class TestSystem : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        Dependency = new WithInvalidValueTypeParameters().Schedule(Dependency);
                    }
                }";

            AssertProducesError(source, "SGJE0003", "invalidFloat");
        }

        [Test]
        public void SGJE0003_InvalidValueTypesInExecuteMethod2()
        {
            const string source = @"
                public partial class PlayerVehicleControlSystem : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        var job = new IllegalThrustJob { DeltaTime = Time.DeltaTime };
                        job.ScheduleParallel();
                    }
                }

                partial struct IllegalThrustJob : IJobEntity
                {
                    public float DeltaTime;

                    public void Execute(int NotAValidParam, ref Translation translation)
                    {
                        translation.Value *= DeltaTime;
                    }
                }";
            AssertProducesError(source, "SGJE0003", "The parameter 'NotAValidParam' of type int will be ignored");
        }

        [Test]
        public void SGJE0004_NonPartialType()
        {
            const string source =
                @"public struct NonPartialJobEntity : IJobEntity
                {
                    void Execute(Entity entity)
                    {
                    }
                }

                public partial class TestSystem : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        Dependency = new NonPartialJobEntity().Schedule(Dependency);
                    }
                }";

            AssertProducesError(source, "SGJE0004", "NonPartialJobEntity");
        }

        [Test]
        public void SGJE0006_NonIntegerEntityInQueryParameter()
        {
            const string source =
                @"public partial struct NonIntegerEntityInQueryParameter : IJobEntity
                {
                    void Execute(Entity entity, [EntityInQueryIndex] bool notInteger)
                    {
                    }
                }

                public partial class TestSystem : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        Dependency = new NonIntegerEntityInQueryParameter().Schedule(Dependency);
                    }
                }";

            AssertProducesError(source, "SGJE0006", "NonIntegerEntityInQueryParameter");
        }

        [Test]
        public void SGJE0007_TooManyIntegerEntityInQueryParameters()
        {
            const string source =
                @"public partial struct TooManyIntegerEntityInQueryParameters : IJobEntity
                {
                    void Execute(Entity entity, [EntityInQueryIndex] int first, [EntityInQueryIndex] int second) {}
                }

                public partial class TestSystem : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        Dependency = new TooManyIntegerEntityInQueryParameters().Schedule(Dependency);
                    }
                }";

            AssertProducesError(source, "SGJE0007", "TooManyIntegerEntityInQueryParameters");
        }

        [Test]
        public void SGJE0008_NoExecuteMethod()
        {
            const string source =
                @"public partial struct NoExecuteMethod : IJobEntity
                {
                    void NotExecuting(Entity entity) {}
                }

                public partial class TestSystem : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        new NoExecuteMethod().Schedule();
                    }
                }";

            AssertProducesError(source, "SGJE0008", "NoExecuteMethod");
        }

        [Test]
        public void SGJE0008_TooManyExecuteMethods()
        {
            const string source =
                @"public partial struct TooManyExecuteMethods : IJobEntity
                {
                    void Execute(Entity entity){}
                    void Execute([EntityInQueryIndex] int index){}
                    void Execute(){}
                }

                public partial class TestSystem : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        var instance = new TooManyExecuteMethods();
                        Dependency = instance.Schedule(Dependency);
                    }
                }";

            AssertProducesError(source, "SGJE0008", "TooManyExecuteMethods");
        }

        [Test]
        public void SGJE0008_MoreThanOneUserDefinedExecuteMethods()
        {
            const string source = @"
                public partial class TooManyUserDefinedExecuteMethods : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        new ThrustJob().ScheduleParallel();
                    }

                    struct NonIJobEntityStruct
                    {
                        public void Execute() {}
                        public void Execute(int someVal) {}
                    }

                    partial struct ThrustJob : IJobEntity
                    {
                        public void Execute(ref Translation translation) {}
                        public void Execute(int someVal) {}
                    }
                }";

            AssertProducesError(source, "SGJE0008", "TooManyUserDefinedExecuteMethods");
        }

        [Test]
        public void SGJE0009_ContainsReferenceTypeFields()
        {
            const string source = @"
                public partial class PlayerVehicleControlSystem : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        var job = new ThrustJobWithReferenceTypeField { DeltaTime = Time.DeltaTime, JobName = ""MyThrustJob"" };
                        Dependency = job.ScheduleParallel(Dependency);
                    }
                }

                partial struct ThrustJobWithReferenceTypeField : IJobEntity
                {
                    public float DeltaTime;
                    public string JobName;

                    public void Execute(ref Translation translation)
                    {
                        translation.Value *= (5f + DeltaTime);
                    }
                }";
            AssertProducesError(source, "SGJE0009", "ThrustJobWithReferenceTypeField contains non-value type fields.");
        }

        [Test]
        public void SGJE0010_UnsupportedParameterTypeUsed()
        {
            const string source = @"
                public partial class PlayerVehicleControlSystem : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        var job = new IllegalThrustJob { DeltaTime = Time.DeltaTime };
                        job.ScheduleParallel();
                    }
                }

                partial struct IllegalThrustJob : IJobEntity
                {
                    public float DeltaTime;

                    public void Execute(ref Translation translation, IllegalClass illegalClass)
                    {
                        translation.Value *= illegalClass.IllegalValue * DeltaTime;
                    }
                }

                class IllegalClass
                {
                    public int IllegalValue { get; private set; } = 42;
                }";
            AssertProducesError(source, "SGJE0010", "IJobEntity.Execute() parameter 'illegalClass' of type IllegalClass is not supported.");
        }

        /* @Todo: Re-enable tests and make the errors
        [Test]1
        public void DC0068_MissingExecuteMethod()
        {
            const string source = @"
                public partial class PlayerVehicleControlSystem : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        Entities.ForEach(new MisspelledThrustJob { DeltaTime = Time.DeltaTime }).ScheduleParallel();
                    }
                }

                struct MisspelledThrustJob : IJobEntitiesForEach
                {
                    public float DeltaTime;

                    public void Executee(ref Translation translation) // Note the typo here.
                    {
                        translation.Value *= (5f + DeltaTime);
                    }
                }";

            AssertProducesError(
                source,
                "DC0068",
                "No 'MisspelledThrustJob' type found that both 1) implements the IJobEntitiesForEach interface and 2) contains an Execute() method.");
        }

        [Test]
        public void DC0003_WithConflictingName()
        {
            const string source = @"
                public partial class WithConflictingName : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        Entities.WithName(""SameName"").ForEach(new OkThrustJob { DeltaTime = 0.5f }).Schedule();
                        Entities.WithName(""SameName"").ForEach((ref Translation t) => {}).Schedule();
                    }
                }

                struct OkThrustJob : IJobEntitiesForEach
                {
                    public float DeltaTime;

                    public void Execute(ref Translation translation)
                    {
                        translation.Value *= (5f + DeltaTime);
                    }
                }";

            AssertProducesError(source, "nameof(UserError.DC0003)");
        }

        [Test]
        public void DC0029_LambdaJobThatHasNestedEntitiesJobForEachInvocationTest()
        {
            const string source = @"
            partial class LambdaThatHasNestedLambda : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                        .WithoutBurst()
                        .ForEach((Entity e1, ref Translation t1) =>
                        {
                            Entities
                                .WithoutBurst()
                                .ForEach(new OkThrustJob { DeltaTime = 0.1f + t1.Value }).Run();
                        }).Run();
                }
            }

            struct OkThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";

            AssertProducesError(source, "nameof(UserError.DC0029)", shouldContains: "Entities.ForEach Lambda expression has a nested Entities.ForEach(IJobEntitiesForEach job) invocation.");
        }

        [Test]
        public void DC0009_UsingConstructionMultipleTimes()
        {
            const string source = @"
                public partial class WithConflictingName : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        Entities
                            .WithName(""Cannot"")
                            .WithName(""Decide"")
                            .ForEach(new OkThrustJob { DeltaTime = 0.5f })
                            .Schedule();
                    }
                }

                struct OkThrustJob : IJobEntitiesForEach
                {
                    public float DeltaTime;

                    public void Execute(ref Translation translation)
                    {
                        translation.Value *= (5f + DeltaTime);
                    }
                }";

            AssertProducesError(source, "nameof(UserError.DC0009)");
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
                        .ForEach(new OkThrustJob { DeltaTime = 0.5f })
                        .Schedule();
                }
            }

            struct OkThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";

            AssertProducesError(source, nameof(UserError.DC0010));
        }

        [Test]
        public void DC0011_WithoutScheduleInvocationTest()
        {
            const string source = @"
            partial class WithoutScheduleInvocation : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach(new OkThrustJob { DeltaTime = 0.5f });
                }
            }

            struct OkThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";
            AssertProducesError(source, "nameof(UserError.DC0011)");
        }

        [Test]
        public void DC0019_UseSharedComponentDataUsingSchedule()
        {
            const string source = @"
            partial class SharedComponentDataUsingSchedule : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                        .ForEach(new OkThrustJob { DeltaTime = 0.5f })
                        .Schedule();
                }
            }

            struct MySharedComponentData : ISharedComponentData {}

            struct OkThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation, in MySharedComponentData mySharedComponentData)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";

            AssertProducesError(source, "DC0019", "MySharedComponentData");
        }

        [Test]
        public void DC0020_SharedComponentDataReceivedByRef()
        {
            const string source = @"
            partial class SharedComponentDataReceivedByRef : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                        .WithoutBurst()
                        .ForEach(new IllegalThrustJob { DeltaTime = 0.5f })
                        .Run();
                }
            }

            struct MySharedComponentData : ISharedComponentData {}

            struct IllegalThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation, ref MySharedComponentData mySharedComponentData)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";

            AssertProducesError(source, "DC0020", "MySharedComponentData");
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void DC0023_ManagedComponentInBurstJobTest()
        {
            const string source = @"
            partial class ManagedComponentInBurstJobTest : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach(new OkThrustJob { DeltaTime = 0.5f }).Run();
                }
            }
            class ManagedComponent : IComponentData, IEquatable<ManagedComponent>
            {
                public bool Equals(ManagedComponent other) => false;
                public override bool Equals(object obj) => false;
                public override int GetHashCode() =>  0;
            }

            struct OkThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation, in ManagedComponent managedComponent)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";
            AssertProducesError(source, "nameof(UserError.DC0023)");
        }

        [Test]
        public void DC0023_ManagedComponentInSchedule()
        {
            const string source = @"
            partial class ManagedComponentInSchedule : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.ForEach(new OkThrustJob { DeltaTime = 0.5f }).Schedule();
                }
            }
            class ManagedComponent : IComponentData, IEquatable<ManagedComponent>
            {
                public bool Equals(ManagedComponent other) => false;
                public override bool Equals(object obj) => false;
                public override int GetHashCode() =>  0;
            }

            struct OkThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation, in ManagedComponent managedComponent)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";
            AssertProducesError(source, "nameof(UserError.DC0023)");
        }

        [Test]
        public void DC0024_ManagedComponentByReference()
        {
            const string source = @"
            partial class ManagedComponentInSchedule : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities.WithoutBurst().ForEach(new IllegalThrustJob { DeltaTime = 0.5f }).Run();
                }
            }
            class ManagedComponent : IComponentData, IEquatable<ManagedComponent>
            {
                public bool Equals(ManagedComponent other) => false;
                public override bool Equals(object obj) => false;
                public override int GetHashCode() =>  0;
            }

            struct IllegalThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation, ref ManagedComponent managedComponent)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";

            AssertProducesError(source, "nameof(UserError.DC0024)");
        }
#endif

        [Test]
        public void DC0026_WithAllWithSharedFilterTest()
        {
            const string source = @"
            partial class WithAllWithSharedFilter : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                        .WithAll<MySharedComponentData>()
                        .WithSharedComponentFilter(new MySharedComponentData() { Value = 3 })
                        .ForEach(new OkThrustJob { DeltaTime = 0.5f })
                        .Schedule();
                }
            }

            struct MySharedComponentData : ISharedComponentData { public int Value; }

            struct OkThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";

            AssertProducesError(source, "nameof(UserError.DC0026)", "MySharedComponentData");
        }

        */
        [Test]
        public void SGJE0012_IncorrectUsageOfBufferIsDetected()
        {
            const string source = @"
            partial class PlayerVehicleControlSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    var job = new IllegalThrustJob { DeltaTime = 0.5f };
                    job.Schedule();
                }
            }

            partial struct IllegalThrustJob : IJobEntity
            {
                public float DeltaTime;

                public void Execute(ref Translation translation, MyBufferFloat myBufferFloat)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";

            AssertProducesError(source, "SGJE0012", shouldContains: "MyBufferFloat implements IBufferElementData and must be used as DynamicBuffer<MyBufferFloat>.");
        }/*

        [Test]
        public void DC0059_IJobEntitiesForEachWithBurstCompileAttribute()
        {
            const string source = @"
            partial class PlayerVehicleControlSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                        .ForEach(new IllegalThrustJob { DeltaTime = 0.5f })
                        .Schedule();
                }
            }

            [BurstCompile]
            struct IllegalThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";

            AssertProducesError(source, "DC0059", shouldContains: "IJobEntitiesForEach types may not have the [Unity.Burst.BurstCompile] attribute.");
        }

        [Test]
        public void DC0062_WithNativeDisableParallelForRestrictionInvoked()
        {
            const string source = @"
            partial class PlayerVehicleControlSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    var targetPositionFromEntity = GetComponentDataFromEntity<TargetPosition>();
                    Entities
                        .WithNativeDisableParallelForRestriction(targetPositionFromEntity)
                        .ForEach(new OkThrustJob { DeltaTime = 0.5f })
                        .Schedule();
                }
            }

            public struct TargetPosition : IComponentData
            {
                public float Value;
            }

            struct OkThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";

            AssertProducesError(source, "DC0062");
        }

        [Test]
        public void DC0062_WithStructuralChangesInvoked()
        {
            const string source = @"
            partial class PlayerVehicleControlSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    Entities
                        .WithoutBurst()
                        .WithStructuralChanges()
                        .ForEach(new OkThrustJob { DeltaTime = 0.5f }).Run();
                }
            }

            struct OkThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";

            AssertProducesError(source, "DC0062");
        }

        [Test]
        public void DC0062_WithReadOnlyInvoked()
        {
            const string source = @"
            partial class PlayerVehicleControlSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    var cellIndices = new NativeArray<int>(10, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                    Entities
                        .WithReadOnly(cellIndices)
                        .ForEach(new OkThrustJob { DeltaTime = 0.5f })
                        .Run();
                }
            }

            struct OkThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";

            AssertProducesError(source, "DC0062");
        }

        [Test]
        public void DC0062_WithDisposeOnCompletionInvoked()
        {
            const string source = @"
            partial class PlayerVehicleControlSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    var cellIndices = new NativeArray<int>(10, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                    Entities
                        .WithDisposeOnCompletion(cellIndices)
                        .ForEach(new OkThrustJob { DeltaTime = 0.5f })
                        .Run();
                }
            }

            struct OkThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";

            AssertProducesError(source, "DC0062");
        }

        [Test]
        public void DC0062_WithNativeDisableContainerSafetyRestrictionInvoked()
        {
            const string source = @"
            partial class PlayerVehicleControlSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    var cellIndices = new NativeArray<int>(10, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                    Entities
                        .WithNativeDisableContainerSafetyRestriction(cellIndices)
                        .ForEach(new OkThrustJob { DeltaTime = 0.5f })
                        .Run();
                }
            }

            struct OkThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";

            AssertProducesError(source, "DC0062");
        }

        [Test]
        public void DC0062_WithNativeDisableUnsafePtrRestrictionInvoked()
        {
            const string source = @"
            partial class PlayerVehicleControlSystem : SystemBase
            {
                protected override unsafe void OnUpdate()
                {
                    byte* innerRawPtr = (byte*)IntPtr.Zero;

                    Entities
                        .WithNativeDisableUnsafePtrRestriction(innerRawPtr)
                        .ForEach(new OkThrustJob { DeltaTime = 0.5f })
                        .Run();
                }
            }

            struct OkThrustJob : IJobEntitiesForEach
            {
                public float DeltaTime;

                public void Execute(ref Translation translation)
                {
                    translation.Value *= (5f + DeltaTime);
                }
            }";

            AssertProducesError(source, "DC0062");
        }

        [Test]
        public void DC0070_IJobEntitiesForEach_IllegalDuplicateTypesUsedInExecuteMethod()
        {
            const string source = @"
                partial class PlayerVehicleControlSystem : SystemBase
                {
                    protected override unsafe void OnUpdate()
                    {
                        byte* innerRawPtr = (byte*)IntPtr.Zero;
                        Entities
                            .ForEach(new IllegalThrustJob { DeltaTime = 0.5f })
                            .Run();
                    }
                }
                struct IllegalThrustJob : IJobEntitiesForEach
                {
                    public float DeltaTime;
                    public void Execute(ref Translation translation1, in Translation translation2)
                    {
                        translation1.Value *= (5f + DeltaTime - translation2.Value);
                    }
                }";

            AssertProducesError(source, "DC0070", nameof(Translation));
        }*/
    }
}
#endif
#endif
