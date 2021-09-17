using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
    public class EntitiesForEachWithFilter : LambdaJobsSourceGenerationIntegrationTest
    {
        readonly string _testSource = @"
using Unity.Entities;
using Unity.Collections;
partial class EntitiesForEachWithFilter : SystemBase
{
    public NativeArray<Entity> Array = default;
    protected override void OnUpdate()
    {
        Entities.WithFilter(Array).ForEach((ref Translation translation) => {{ translation.Value += 5; }}).Run();
    }
}

struct Translation : IComponentData { public float Value; }
";


        [Test]
        public void EntitiesForEachWithFilterTest()
        {
            RunTest(_testSource, new GeneratedType{Name = "EntitiesForEachWithFilter"});
        }
    }
}
