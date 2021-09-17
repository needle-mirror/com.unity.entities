
using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
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

