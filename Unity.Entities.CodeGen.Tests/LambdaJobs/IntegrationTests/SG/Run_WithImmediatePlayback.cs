using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
#if UNITY_2021_1_OR_NEWER
    [Ignore("2021.1 no longer supports UnityEditor.Scripting.Compilers.CSharpLanguage which these tests rely on.")]
#endif
    [TestFixture]
    class Run_WithImmediatePlayback : LambdaJobsSourceGenerationIntegrationTest
    {
        private const string Code =
            @"using Unity.Entities;
            using Unity.Mathematics;

            public struct WidgetSpawner : IComponentData
            {
                public Entity WidgetPrefabEntity;
            }

            public struct Translation : IComponentData
            {
                public float3 Value;
            }

            public struct Health : IComponentData
            {
                public float Score;
            }

            public partial class Run_WithImmediatePlayback : SystemBase
            {
                protected override void OnUpdate()
                {
                    var random = new Random();

                    Entities
                        .WithImmediatePlayback()
                        .ForEach(
                            (Entity entity, EntityCommandBuffer buffer, in WidgetSpawner spawner, in Translation translation) =>
                            {
                                var widget = buffer.Instantiate(spawner.WidgetPrefabEntity);
                                buffer.AddComponent(widget, new Health { Score = random.NextFloat() });
                                buffer.SetComponent(widget, new Translation { Value = translation.Value - random.NextFloat3() });
                                buffer.RemoveComponent<Health>(widget);
                                buffer.DestroyEntity(widget);
                            })
                        .Run();
                }
            }";

        [Test]
        public void Run_WithImmediatePlaybackTest()
        {
            RunTest(Code, new GeneratedType {Name = "Run_WithImmediatePlayback"});
        }
    }
}
