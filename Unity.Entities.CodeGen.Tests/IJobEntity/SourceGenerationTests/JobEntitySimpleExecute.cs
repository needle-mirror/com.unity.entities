using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
#if UNITY_2021_1_OR_NEWER
    [Ignore("2021.1 no longer supports UnityEditor.Scripting.Compilers.CSharpLanguage which these tests rely on.")]
#endif
    [TestFixture]
    public class JobEntitySimpleExecute : JobEntitySourceGenerationTests
    {
        private const string Code =
             @"using Unity.Entities;
               using Unity.Mathematics;
               using Unity.Transforms;
               using UnityEngine;

               public partial struct MyEntityJob : IJobEntity
               {
                    public float DeltaTime;

                    public void Execute(ref Rotation rotation, in RotationSpeed_ForEach speed)
                    {
                        rotation.Value =
                            math.mul(
                                math.normalize(rotation.Value),
                                quaternion.AxisAngle(math.up(), speed.RadiansPerSecond * DeltaTime));
                    }
               }

               public struct Rotation : IComponentData
               {
                    public quaternion Value;
               }

               public struct RotationSpeed_ForEach : IComponentData
               {
                    public float RadiansPerSecond;
               }

               public partial class JobEntity_SimpleSystem : SystemBase
               {
                    protected override void OnUpdate()
                    {
                        var job = new MyEntityJob { DeltaTime = Time.DeltaTime };
                        Dependency = job.ScheduleParallel(Dependency);
                    }
               }";

        [Test]
        public void JobEntitySimpleExecuteTest()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "JobEntity_SimpleSystem"
                });
        }
    }
}
