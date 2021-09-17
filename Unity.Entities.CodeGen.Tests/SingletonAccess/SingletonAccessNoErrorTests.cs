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
    public class SingletonAccessNoErrorTests : SourceGenTests
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
        public void NoError_SingletonAccessWithProperty()
        {
            const string source = @"
                public struct ComponentWithProp : IComponentData
                {
                    public static ComponentWithProp Default => new ComponentWithProp {value = -1};

                    public int value;
                }

                public partial class RotationSpeedSystem_ForEach : SystemBase
                {
                    protected override void OnUpdate()
                    {
                        SetSingleton(ComponentWithProp.Default);
                    }
                }";

            AssertProducesNoError(source, null, true);
        }
    }
}
