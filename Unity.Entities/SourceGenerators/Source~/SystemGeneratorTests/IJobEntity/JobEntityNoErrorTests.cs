using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using VerifyCS =
    Unity.Entities.SourceGenerators.Test.CSharpIncrementalGeneratorVerifier<
        Unity.Entities.SourceGen.JobEntityGenerator.JobEntityGenerator>;

namespace Unity.Entities.SourceGenerators;

[TestClass]
public class JobEntityNoErrorTests
{
    [TestMethod]
    public async Task JobWithIdenticallyNamedComponentsInDifferentNamespaces()
    {
        const string source = @"
            using Unity.Entities;

            namespace NamespaceA
            {
                public struct Component : IComponentData {}
                public struct EnableableComponent : IComponentData, IEnableableComponent {}
                public struct SharedComponent : ISharedComponentData {}
            }

            namespace NamespaceB
            {
                public struct Component : IComponentData {}
                public struct EnableableComponent : IComponentData, IEnableableComponent {}
                public struct SharedComponent : ISharedComponentData {}
            }

            partial struct Job : IJobEntity
            {
                void Execute(
                    RefRO<NamespaceA.Component> compA,
                    RefRO<NamespaceB.Component> compB,
                    EnabledRefRO<NamespaceA.EnableableComponent> enabledRefRoA,
                    EnabledRefRO<NamespaceB.EnableableComponent> enabledRefRoB,
                    NamespaceA.SharedComponent sharedComponentA,
                    NamespaceB.SharedComponent sharedComponentB)
                {
                }
            }";
        await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task JobWithEnableableBufferElement()
    {
        const string source = @"
            using Unity.Entities;

            public partial struct JobWithEnableableBufferElement : IJobEntity
            {
                public void Execute(EnabledRefRO<Unity.Entities.Tests.EcsTestBufferElementEnableable> refRO) {}
            }";
        await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task NO_SGICE_BUT_HAS_CSHARP_COMPILE_ERRORS()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;
            using static Unity.Entities.SystemAPI;
            partial struct SomeJobEntity : IJobEntity
            {
                public void Execute(ref DynamicBuffer<{|#0:NotADefinedType|}> someBuffer){}
            }";
        var expectedA = VerifyCS.CompilerError("CS0246").WithLocation(0);
        await VerifyCS.VerifySourceGeneratorAsync(source, expectedA);
    }

    [TestMethod]
    public async Task InnerNamespaceUsing()
    {
        const string source = @"
            using Unity.Entities;
            namespace SomeNameSpace {
                public partial struct SomeJob : IJobEntity {
                    public void Execute() {}
                }
            }";
            await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task JobInStruct()
    {
        const string source = @"
            using Unity.Entities;
            public partial struct SomeOuter {
                public partial struct SomeJob : IJobEntity {
                    public void Execute() {}
                }
            }";
            await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task JobInClass()
    {
        const string source = @"
            using Unity.Entities;
            public partial class SomeOuter {
                public partial struct SomeJob : IJobEntity {
                    public void Execute() {}
                }
            }";
            await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task RefWrapperParamsWorkWithTagComponents()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;

            public partial class SomeOuter {
                public partial struct SomeJob : IJobEntity {
                    public void Execute(RefRO<EcsTestDataEnableable> refRO, RefRW<EcsTestDataEnableable1> refRW, EnabledRefRO<EcsTestDataEnableable2> enabledRefRO, EnabledRefRW<EcsTestDataEnableable3> enabledRefRW) {}
                }
            }";
        await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task TwoJobs()
    {
        const string source = @"
            using Unity.Entities;
            public partial struct SomeOuter {
                public partial struct SomeJobA : IJobEntity {
                    public void Execute() {}
                }
                public partial struct SomeJobB : IJobEntity {
                    public void Execute() {}
                }
            }";
            await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task JobWithAspectNestedPrimitives()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;
            partial struct JobWithAspectLookup : IJobEntity
            {
                public EcsTestAspect.Lookup Lookup;
                public JobWithAspectLookup(EcsTestAspect.Lookup a) { Lookup = a; }
                void Execute() { }
            }
            partial struct JobWithNestedAspectLookup : IJobEntity
            {
                public struct Nested
                {
                    public EcsTestAspect.Lookup Lookup;
                    public Nested(EcsTestAspect.Lookup a) { Lookup = a; }
                }
                public Nested NestedLookup;
                public JobWithNestedAspectLookup(Nested a) { NestedLookup = a; }
                void Execute() { }
            }
            partial struct JobWithAspectTypeHandle : IJobEntity
            {
                public EcsTestAspect.TypeHandle TypeHandle;
                public JobWithAspectTypeHandle(EcsTestAspect.TypeHandle a) { TypeHandle = a; }
                void Execute() { }
            }
            partial struct JobWithNestedAspectTypeHandle : IJobEntity
            {
                public struct Nested
                {
                    public EcsTestAspect.TypeHandle TypeHandle;
                    public Nested(EcsTestAspect.TypeHandle a) { TypeHandle = a; }
                }
                public Nested NestedTypeHandle;
                public JobWithNestedAspectTypeHandle(Nested a) { NestedTypeHandle = a; }
                void Execute() { }
            }";
        await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task ParameterTypesWithValidAccessibility()
    {
        const string source = @"
            using Unity.Entities;

            struct NonPublicComponent : IComponentData
            {
                public float Value;
            }

            partial class NonPublicClass
            {
                public partial struct TestJob : IJobEntity
                {
                    void Execute(in NonPublicComponent {|#0:component|})
                    {
                    }
                }
            }

            public partial struct TestSystem : ISystem
            {
                public void OnUpdate(ref SystemState state) => new NonPublicClass.TestJob().Schedule();
            }";

        await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task UsingInPreProccessorStatements()
    {
        const string source = @"
                namespace SomeNamespace {
                    using Unity.Entities;
                    using Unity.Entities.Tests;
                    #if UNITY_EDITOR
                    using Unity.Mathematics;
                    #endif

                    partial struct SomeJob : IJobEntity {
                        public void Execute(in EcsTestData data) {}
                    }
                }";

        await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task PreProcessorStatementInMiddleOfUsings()
    {
        const string source = @"
                using Unity.Entities;
                #if !UNITY_EDITOR
                using Unity.Mathematics;
                #endif
                using Unity.Entities.Tests;

                partial struct SomeJob : IJobEntity {
                    public void Execute(in EcsTestData data) {}
                }";

        await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    #region System

    [TestMethod]
    public async Task SystemInnerNamespaceUsing()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;
            namespace SomeNameSpace {
                public partial struct SomeSystem : ISystem {
                    public void OnUpdate(ref SystemState state){
                        SystemAPI.GetSingletonRW<EcsTestData>();
                    }
                }
            }";
            await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task SystemInStruct()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;
            public partial struct SomeOuter {
                public partial struct SomeSystemInner : ISystem {
                    public void OnUpdate(ref SystemState state){
                        SystemAPI.GetSingletonRW<EcsTestData>();
                    }
                }
            }";
            await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    [TestMethod]
    public async Task SystemInClass()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;
            public partial class SomeOuter {
                public partial struct SomeSystemInner : ISystem {
                    public void OnUpdate(ref SystemState state){
                        SystemAPI.GetSingletonRW<EcsTestData>();
                    }
                }
            }";
            await VerifyCS.VerifySourceGeneratorAsync(source);
    }

    #endregion
}
