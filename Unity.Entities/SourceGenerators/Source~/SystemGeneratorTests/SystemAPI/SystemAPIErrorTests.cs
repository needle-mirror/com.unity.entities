using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Unity.Entities.SourceGen.SystemGenerator.SystemAPI;
using VerifyCS =
    Unity.Entities.SourceGenerators.Test.CSharpSourceGeneratorVerifier<
        Unity.Entities.SourceGen.SystemGenerator.SystemGenerator>;

namespace Unity.Entities.SourceGenerators;

[TestClass]
public class SystemAPIErrorTests
{
    [TestMethod]
    public async Task NO_SGICE_BUT_HAS_CSHARP_COMPILE_ERRORS()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;
            using static Unity.Entities.SystemAPI;
            partial struct SomeJobEntity : IJobEntity
            {
                public void Execute(in EcsTestData id){}
            }

            partial struct SomeSystem : ISystem {
                public void OnUpdate(ref SystemState state) {
                    var hadComp = {|#0:HasComponent<EcsTestData>|}();
                    new SomeJobEntity().ScheduleParallel({|#1:SystemAPI.Query<RefRO<EcsTestData>>().WithEntityAccess()|});
                }
            }";
        var expectedA = VerifyCS.CompilerError("CS7036").WithLocation(0);
        var expectedB = VerifyCS.CompilerError("CS1503").WithLocation(1);
        await VerifyCS.VerifySourceGeneratorAsync(source, expectedA, expectedB);
    }

    [TestMethod]
    public async Task NO_SGICE_BUT_HAS_CSHARP_COMPILE_ERRORS_2()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;

            partial struct SomeSystem : ISystem {
                public void OnUpdate(ref SystemState state) {
                    foreach (var VARIABLE in SystemAPI.Query<{|#0:RefRO<>|}>()) {}
                }
            }";
        var expectedA = VerifyCS.CompilerError("CS7003").WithLocation(0);
        await VerifyCS.VerifySourceGeneratorAsync(source, expectedA);
    }

    [TestMethod]
    public async Task NO_SGICE_BUT_HAS_CSHARP_COMPILE_ERRORS_3()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;

            partial struct SomeSystem : ISystem {
                struct Foo : IComponentData { }

                public void OnUpdate(ref SystemState state)
                {
                    foreach (var (a, b) in SystemAPI.{|#0:Query<Entity, RefRO<Foo>>|}().WithAll<Foo>())
                    {
                    }
                }
            }";
        var expectedA = VerifyCS.CompilerError("CS0315").WithLocation(0);
        await VerifyCS.VerifySourceGeneratorAsync(source, expectedA);
    }

    [TestMethod]
    public async Task NO_SGICE_BUT_HAS_CSHARP_COMPILE_ERRORS_4()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;

            partial struct SomeSystem : ISystem {
                struct Foo : IComponentData { }

                public void OnUpdate(ref SystemState state)
                {
                    {|#0:SystemAPI.Query<>|}();
                }
            }";
        var expectedA = VerifyCS.CompilerError("CS0305").WithLocation(0);
        var expectedB = VerifyCS.CompilerError("CS7003").WithLocation(0);
        await VerifyCS.VerifySourceGeneratorAsync(source, expectedA, expectedB);
    }

    [TestMethod]
    public async Task SGSA0001()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;
            using static Unity.Entities.SystemAPI;

            partial struct SomeSystem : ISystem {
                public void OnUpdate(ref SystemState state) {
                    Idk<EcsTestData>();
                }

                public void Idk<T>() where T:struct,IComponentData{
                    var hadComp = {|#0:HasComponent<T>(default)|};
                }
            }";
        var expected = VerifyCS.CompilerError(nameof(SystemApiContextErrors.SGSA0001)).WithLocation(0);
        await VerifyCS.VerifySourceGeneratorAsync(source, expected);
    }

    [TestMethod]
    public async Task SGSA0002()
    {
        const string source = @"
            using Unity.Entities;
            using Unity.Entities.Tests;
            using static Unity.Entities.SystemAPI;

            partial struct SomeSystem : ISystem {
                public void OnUpdate(ref SystemState state) {
                    var ro = false;
                    var lookup = {|#0:GetComponentLookup<EcsTestData>(ro)|};
                }
            }";
        var expected = VerifyCS.CompilerError(nameof(SystemApiContextErrors.SGSA0002)).WithLocation(0);
        await VerifyCS.VerifySourceGeneratorAsync(source, expected);
    }
}
