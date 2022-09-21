using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
#if UNITY_2021_1_OR_NEWER
    [Ignore("2021.1 no longer supports UnityEditor.Scripting.Compilers.CSharpLanguage which these tests rely on.")]
#endif
    [TestFixture]
    public class EntitiesForEachNonCapturing : LambdaJobsSourceGenerationIntegrationTest
    {
        readonly string _testSource = $@"
using Unity.Entities;
using Unity.Mathematics;
using Unity.Burst;

partial class EntitiesForEachNonCapturing : SystemBase
{{
    protected override void OnUpdate()
    {{
        Entities.ForEach((ref Translation translation, ref TagComponent1 tag1, in TagComponent2 tag2) => {{ translation.Value += 5; }}).Run();
    }}
}}

struct Translation : IComponentData {{ public float Value; }}
struct TagComponent1 : IComponentData {{}}
struct TagComponent2 : IComponentData {{}}";

        [Test]
        public void EntitiesForEachNonCapturingTest()
        {
            RunTest(_testSource, new GeneratedType{Name = "EntitiesForEachNonCapturing"});
        }
    }
}
