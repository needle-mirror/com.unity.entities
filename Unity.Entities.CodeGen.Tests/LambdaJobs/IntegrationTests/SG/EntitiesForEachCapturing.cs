using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
#if UNITY_2021_1_OR_NEWER
    [Ignore("2021.1 no longer supports UnityEditor.Scripting.Compilers.CSharpLanguage which these tests rely on.")]
#endif
    [TestFixture]
    public class EntitiesForEachCapturing : LambdaJobsSourceGenerationIntegrationTest
    {
        readonly string _testSource = $@"
using System;
using Unity.Entities;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities.Tests;
using Unity.Entities.CodeGen.Tests;

partial class EntitiesForEachCapturing : SystemBase
{{
    EntityQuery m_Query;

    AllocatorHelper<RewindableAllocator> m_AllocatorHelper;
    protected ref RewindableAllocator RwdAllocator => ref m_AllocatorHelper.Allocator;

    protected override void OnCreate()
    {{
        m_AllocatorHelper = new AllocatorHelper<RewindableAllocator>(Allocator.Persistent);
        m_AllocatorHelper.Allocator.Initialize(128 * 1024, true);
    }}

    protected override void OnDestroy()
    {{
        m_AllocatorHelper.Allocator.Dispose();
        m_AllocatorHelper.Dispose();
    }}

    protected override unsafe void OnUpdate()
    {{
        var innerCapturedFloats = CollectionHelper.CreateNativeArray<float>(1, RwdAllocator.ToAllocator, NativeArrayOptions.UninitializedMemory);
        innerCapturedFloats[0] = 456;
        byte* innerRawPtr = (byte*)IntPtr.Zero;
        float innerScopeFloat = 2.0f;

        Entities
                .WithBurst(FloatMode.Deterministic, FloatPrecision.High, true)
                .WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities)
                .WithChangeFilter<Translation>()
                .WithNone<Boid>()
                .WithAll<Velocity>()
                .WithReadOnly(innerCapturedFloats)
                .WithNativeDisableContainerSafetyRestriction(innerCapturedFloats)
                .WithNativeDisableUnsafePtrRestriction(innerRawPtr)
                .WithStoreEntityQueryInField(ref m_Query)
                .ForEach(
                    (int entityInQueryIndex,
                        Entity myEntity,
                        DynamicBuffer<MyBufferInt> myBufferInts,
                        ref Translation translation, in Acceleration acceleration, in DynamicBuffer<MyBufferFloat> myBufferFloat) =>
                    {{
                        EcsTestData LocalMethodThatReturnsValue()
                        {{
                            return default;
                        }}

                        LocalMethodThatReturnsValue();
                        translation.Value += (innerCapturedFloats[2] + acceleration.Value + entityInQueryIndex + myEntity.Version + myBufferInts[2].Value + innerScopeFloat + myBufferFloat[0].Value);
                        Console.Write(innerRawPtr->ToString());
                    }})
                .Schedule();
        }}
}}";

        [Test]
        public void EntitiesForEachCapturingTest()
        {
            RunTest(_testSource, new GeneratedType {Name = "EntitiesForEachCapturing"});
        }
    }
}
