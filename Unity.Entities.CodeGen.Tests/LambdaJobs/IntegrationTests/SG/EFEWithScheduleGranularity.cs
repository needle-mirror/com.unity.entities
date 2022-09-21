
using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
#if UNITY_2021_1_OR_NEWER
    [Ignore("2021.1 no longer supports UnityEditor.Scripting.Compilers.CSharpLanguage which these tests rely on.")]
#endif
    [TestFixture]
    public class EFEWithScheduleGranularity : LambdaJobsSourceGenerationIntegrationTest
    {
        readonly string _testSource = $@"
using System;
using Unity.Entities;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities.Tests;
using Unity.Entities.CodeGen.Tests;

partial class EFEWithScheduleGranularity : SystemBase
{{
    protected override void OnUpdate()
    {{
        Entities
            .WithScheduleGranularity(ScheduleGranularity.Chunk)
            .ForEach((ref Translation t) =>
            {{
                
            }}).ScheduleParallel();
        Entities
            .WithScheduleGranularity(ScheduleGranularity.Entity)
            .ForEach((ref Translation t) =>
            {{
                
            }}).ScheduleParallel();
    }}
}}";

        [Test]
        public void EFEWithScheduleGranularityTest()
        {

            RunTest(_testSource, new GeneratedType {Name = "EFEWithScheduleGranularity" });
        }
    }
}

