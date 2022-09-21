using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
#if UNITY_2021_1_OR_NEWER
    [Ignore("2021.1 no longer supports UnityEditor.Scripting.Compilers.CSharpLanguage which these tests rely on.")]
#endif
    [TestFixture]
    public class SimpleJob : LambdaJobsSourceGenerationIntegrationTest
    {
        readonly string _testSource = $@"
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;

partial class SimpleJob : SystemBase
{{
    protected override void OnUpdate()
    {{
        var myCapturedFloats = new NativeArray<float>();
        var myCapturedInt = 3;

        Job.WithName(""AJobForRunning"").WithCode(() => {{ myCapturedFloats[0] += 1; }}).Run();
        Job.WithName(""AJobThatWrites"").WithCode(() => {{ myCapturedInt = 1; }}).Run();
        Job.WithName(""AJobThatReads"").WithCode(() => {{ EnsureNotOptimizedAway(myCapturedInt); }}).Run();

        Job.WithName(""AJobForScheduling"").WithCode(() =>
        {{
            for (int i = 0; i != myCapturedFloats.Length; i++)
                myCapturedFloats[i] *= 2 + myCapturedInt;
        }}).Schedule();
    }}

    protected static T EnsureNotOptimizedAway<T>(T x) {{ return x; }}
}}";

        [Test]
        public void SimpleJobTest()
        {
            RunTest(_testSource, new GeneratedType {Name = "SimpleJob"});
        }
    }
}
