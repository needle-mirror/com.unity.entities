using System;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.CodeGen.Tests;
using Unity.Entities.CodeGen.Tests.TestTypes;
using Unity.Entities.Tests;
using Unity.Jobs;

namespace Unity.Entities.CodeGen.SourceGenerators.Tests
{
    [TestFixture]
    public class ForEachISystemErrorTests : SourceGenTests
    {
        protected override Type[] DefaultCompilationReferenceTypes { get; } =
        {
            typeof(ISystem),
            typeof(JobHandle),
            typeof(Burst.BurstCompileAttribute),
            typeof(Mathematics.float3),
            typeof(ReadOnlyAttribute),
            typeof(Translation),
            typeof(EcsTestData),
            typeof(TranslationInAnotherAssembly)
        };

        protected override string[] DefaultUsings { get; } =
            {"System", "Unity.Entities", "Unity.Entities.Tests", "Unity.Entities.CodeGen.Tests", "Unity.Collections"};

        [Test]
        public void DC0071_ISystem_EFE_WithSharedComponentFilter()
        {
            const string source = @"
                public partial struct WithSharedComponentFilter : ISystem
                {
                    public void OnCreate(ref SystemState state) {}
                    public void OnDestroy(ref SystemState state) {}

                    public void OnUpdate(ref SystemState state)
                    {
                        state.Entities.WithSharedComponentFilter(new EcsTestSharedComp { value = 3 }).ForEach((ref Translation t) => {}).Schedule();
                    }
                }";

            AssertProducesError(source, "DC0071");
        }

        [Test]
        public void DC0071_ISystem_EFE_WithoutBurst()
        {
            const string source = @"
                public partial struct WithoutBurst : ISystem
                {
                    public void OnCreate(ref SystemState state) {}
                    public void OnDestroy(ref SystemState state) {}

                    public void OnUpdate(ref SystemState state)
                    {
                        state.Entities.WithoutBurst().ForEach((ref Translation t) => {}).Schedule();
                    }
                }";

            AssertProducesError(source, "DC0071");
        }

        [Test]
        public void DC0071_ISystem_EFE_WithStructuralChanges()
        {
            const string source = @"
                public partial struct WithStructuralChanges : ISystem
                {
                    public void OnCreate(ref SystemState state) {}
                    public void OnDestroy(ref SystemState state) {}

                    public void OnUpdate(ref SystemState state)
                    {
                        state.Entities.WithStructuralChanges().ForEach((ref Translation t) => {}).Run();
                    }
                }";

            AssertProducesError(source, "DC0071");
        }

        [Test]
        public void DC0223_ISystem_EFE_WithSharedComponentParam()
        {
            const string source = @"
                public partial struct WithSharedComponentParam : ISystem
                {
                    public void OnCreate(ref SystemState state) {}
                    public void OnDestroy(ref SystemState state) {}

                    public void OnUpdate(ref SystemState state)
                    {
                        state.Entities.ForEach((EcsTestSharedComp t) => {}).Run();
                    }
                }";

            AssertProducesError(source, "DC0223");
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void DC0223_ISystem_EFE_WithManagedComponentParam()
        {
            const string source = @"
                public partial struct WithManagedComponentParam : ISystem
                {
                    public void OnCreate(ref SystemState state) {}
                    public void OnDestroy(ref SystemState state) {}

                    public void OnUpdate(ref SystemState state)
                    {
                        state.Entities.ForEach((EcsTestManagedDataEntity t) => {}).Run();
                    }
                }";

            AssertProducesError(source, "DC0223");
        }
#endif

        [Test]
        public void DC0072_ISystem_EFE_WithNoStateParameter()
        {
            const string source = @"
                public partial struct WithSharedComponentParam : ISystem
                {
                    public void OnCreate(ref SystemState state) {}
                    public void OnDestroy(ref SystemState state) {}

                    public void OnUpdate(ref SystemState state)
                    {
                        Entities.ForEach((ref Translation t) => {}).Run();
                    }
                }";

            AssertProducesError(source, "DC0072", Enumerable.Empty<string>(), null, false, true);
        }
    }
}
