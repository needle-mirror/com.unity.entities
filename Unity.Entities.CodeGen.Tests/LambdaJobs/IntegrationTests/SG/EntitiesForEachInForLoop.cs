using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
    public class EntitiesForEachInForLoop : LambdaJobsSourceGenerationIntegrationTest
    {
        readonly string _testSource = $@"
using Unity.Entities;
using Unity.Entities.CodeGen.Tests;

partial class EntitiesForEachInForLoop : SystemBase
{{
    protected static T EnsureNotOptimizedAway<T>(T x) {{ return x; }}

    protected override void OnUpdate()
    {{
        int captureMe = 3;

        for (int i = 0; i != 3; i++)
        {{
            int innerCapture = 4;

            Entities
                .WithoutBurst()
                .ForEach((ref Translation translation) => translation.Value += captureMe + innerCapture)
                .Run();

            EnsureNotOptimizedAway(captureMe);
            EnsureNotOptimizedAway(innerCapture);
        }}
    }}
}}";

        [Test]
        public void EntitiesForEachInForLoopTest()
        {
            RunTest(_testSource, new GeneratedType {Name = "EntitiesForEachInForLoop"});
        }
    }
}
