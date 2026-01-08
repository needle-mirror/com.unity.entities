using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using VerifyCS =
    Unity.Entities.SourceGenerators.Test.CSharpSourceGeneratorVerifier<
        Unity.Entities.SourceGen.SystemGenerator.SystemGenerator>;

namespace Unity.Entities.SourceGenerators
{
    [TestClass]
    public class SystemGeneratorNoErrorTests
    {
        [TestMethod]
        public async Task MultipleUserWrittenSystemPartsWithGeneratedQueriesSystem()
        {
            const string source = @"
                using Unity.Entities;
                using Unity.Entities.Tests;
                using Unity.Mathematics;
                using Unity.Burst;

                partial class MultipleUserWrittenSystemPartsWithGeneratedQueriesSystem : SystemBase
                {
                    // Query 1
                    public EntityQuery UseSystemAPIQueryBuilder() =>
                        SystemAPI.QueryBuilder().WithAll<EcsTestData>().WithNone<EcsTestData2>().Build();

                    protected override void OnUpdate()
                    {
                    }
                }

                partial class MultipleUserWrittenSystemPartsWithGeneratedQueriesSystem : SystemBase
                {
                    public void UseBulkOperations()
                    {
                        Entities.WithAll<EcsTestData3>().DestroyEntity(); // Query 2
                        Entities.WithNone<EcsTestData2>().WithAll<EcsTestData>().DestroyEntity(); // Same as Query 1
                    }
                }

                partial class MultipleUserWrittenSystemPartsWithGeneratedQueriesSystem : SystemBase
                {
                    public void UseIdiomaticCSharpForEachs()
                    {
                        // Query 3
                        foreach (var _ in SystemAPI.Query<RefRO<EcsTestData4>>())
                        {
                        }
                        // Same as Query 2
                        foreach (var _ in SystemAPI.Query<RefRO<EcsTestData3>>())
                        {
                        }
                    }
                }";
            await VerifyCS.VerifySourceGeneratorAsync(source);
        }

        [TestMethod]
        public async Task ExpressionBody()
        {
            const string source = @"
                using Unity.Entities;
                using Unity.Mathematics;
                using Unity.Burst;
                using Unity.Entities.Tests;

                public partial struct SomeSystem : ISystem {
                    public void OnUpdate(ref SystemState state) => SystemAPI.GetSingletonRW<EcsTestData>();
                }";
            await VerifyCS.VerifySourceGeneratorAsync(source);
        }

        [TestMethod]
        public async Task ExpressionBodyWithReturn()
        {
            const string source = @"
                using Unity.Entities;
                using Unity.Mathematics;
                using Unity.Burst;
                using Unity.Entities.Tests;

                public partial struct SomeSystem : ISystem {
                    RefRW<EcsTestData> GetData(ref SystemState state) => SystemAPI.GetSingletonRW<EcsTestData>();
                    public void OnUpdate(ref SystemState state){}
                }";
            await VerifyCS.VerifySourceGeneratorAsync(source);
        }

        [TestMethod]
        public async Task MultiplePartialUnsafe()
        {
            const string sourceA = @"
                using Unity.Entities;

                public partial struct TestSystem1 : ISystem
                {
                    public void OnUpdate(ref SystemState state)
                    {
                        // Just need a SystemAPI call here
                        var t = SystemAPI.Time;
                        Unsafe(ref state);
                    }
                }";

            const string sourceB = @"
                using Unity.Entities;
                using Unity.Burst.Intrinsics;

                public unsafe partial struct TestSystem1
                {
                    private void Unsafe(ref SystemState state)
                    {
                        var t = SystemAPI.Time.DeltaTime; // And a second SystemAPI call here

                        // This will error because the unsafe on the struct is being ignored
                        // Assets\Script\Assembly\TestSystem1.Partial.cs(15,19): error CS0214: Pointers and fixed size buffers may only be used in an unsafe context
                        var ptr = &t;

                        // Assets\Script\Assembly\TestSystem1.Partial.cs(17,9): error CS0103: The name 'Debug' does not exist in the current context
                        var test = new v128(); // This will error because the namespaces are ignored
                    }
                }";

            await VerifyCS.VerifySourceGeneratorAsync(new []
            {
                sourceA,
                sourceB
            });
        }
    }
}
