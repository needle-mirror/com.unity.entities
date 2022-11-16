using System;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

#if !ENABLE_TRANSFORM_V1
#else

namespace Unity.Transforms
{
    [Serializable]
    [WriteGroup(typeof(LocalToWorld))]
    [WriteGroup(typeof(LocalToParent))]
    [WriteGroup(typeof(ParentScaleInverse))]
    public struct CompositeScale : IComponentData
    {
        public float4x4 Value;
    }


    [Serializable]
    [WriteGroup(typeof(CompositeScale))]
    public struct ScalePivot : IComponentData
    {
        public float3 Value;
    }

    [Serializable]
    [WriteGroup(typeof(CompositeScale))]
    public struct ScalePivotTranslation : IComponentData
    {
        public float3 Value;
    }

    // CompositeScale = ScalePivotTranslation * ScalePivot * Scale * ScalePivot^-1
    // (or) CompositeScale = ScalePivotTranslation * ScalePivot * NonUniformScale * ScalePivot^-1
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    public partial struct CompositeScaleSystem : ISystem
    {
        private EntityQuery m_Query;

        [BurstCompile]
        struct ToCompositeScale : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<Scale> ScaleTypeHandle;
            [ReadOnly] public ComponentTypeHandle<NonUniformScale> NonUniformScaleTypeHandle;
            [ReadOnly] public ComponentTypeHandle<ScalePivot> ScalePivotTypeHandle;
            [ReadOnly] public ComponentTypeHandle<ScalePivotTranslation> ScalePivotTranslationTypeHandle;
            public ComponentTypeHandle<CompositeScale> CompositeScaleTypeHandle;
            public uint LastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);

                var chunkScalePivotTranslations = chunk.GetNativeArray(ref ScalePivotTranslationTypeHandle);
                var chunkScales = chunk.GetNativeArray(ref ScaleTypeHandle);
                var chunkNonUniformScale = chunk.GetNativeArray(ref NonUniformScaleTypeHandle);
                var chunkScalePivots = chunk.GetNativeArray(ref ScalePivotTypeHandle);
                var chunkCompositeScales = chunk.GetNativeArray(ref CompositeScaleTypeHandle);

                var hasScalePivotTranslation = chunkScalePivotTranslations.IsCreated;
                var hasScale = chunkScales.IsCreated;
                var hasNonUniformScale = chunkNonUniformScale.IsCreated;
                var hasScalePivot = chunkScalePivots.IsCreated;
                var count = chunk.Count;

                var hasAnyScale = hasScale || hasNonUniformScale;

                // 000 - Invalid. Must have at least one.
                // 001
                if (!hasAnyScale && !hasScalePivotTranslation && hasScalePivot)
                {
                    var didChange = chunk.DidChange(ref ScalePivotTypeHandle, LastSystemVersion);
                    if (!didChange)
                        return;

                    // Only pivot? Doesn't do anything.
                    for (int i = 0; i < count; i++)
                        chunkCompositeScales[i] = new CompositeScale {Value = float4x4.identity};
                }
                // 010
                else if (!hasAnyScale && hasScalePivotTranslation && !hasScalePivot)
                {
                    var didChange = chunk.DidChange(ref ScalePivotTranslationTypeHandle, LastSystemVersion);
                    if (!didChange)
                        return;

                    for (int i = 0; i < count; i++)
                    {
                        var translation = chunkScalePivotTranslations[i].Value;

                        chunkCompositeScales[i] = new CompositeScale
                        {Value = float4x4.Translate(translation)};
                    }
                }
                // 011
                else if (!hasAnyScale && hasScalePivotTranslation && hasScalePivot)
                {
                    var didChange = chunk.DidChange(ref ScalePivotTranslationTypeHandle, LastSystemVersion);
                    if (!didChange)
                        return;

                    // Pivot without scale doesn't affect anything. Only translation.
                    for (int i = 0; i < count; i++)
                    {
                        var translation = chunkScalePivotTranslations[i].Value;

                        chunkCompositeScales[i] = new CompositeScale
                        {Value = float4x4.Translate(translation)};
                    }
                }
                // 100
                else if (hasAnyScale && !hasScalePivotTranslation && !hasScalePivot)
                {
                    // Has both valid input, but Scale overwrites.
                    if (hasScale)
                    {
                        var didChange = chunk.DidChange(ref ScaleTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var scale = chunkScales[i].Value;
                            chunkCompositeScales[i] = new CompositeScale {Value = float4x4.Scale(scale)};
                        }
                    }
                    else // if (hasNonUniformScale)
                    {
                        var didChange = chunk.DidChange(ref NonUniformScaleTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var scale = chunkNonUniformScale[i].Value;
                            chunkCompositeScales[i] = new CompositeScale {Value = float4x4.Scale(scale)};
                        }
                    }
                }
                // 101
                else if (hasAnyScale && !hasScalePivotTranslation && hasScalePivot)
                {
                    // Has both valid input, but Scale overwrites.
                    if (hasScale)
                    {
                        var didChange = chunk.DidChange(ref ScaleTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref ScalePivotTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var scale = chunkScales[i].Value;
                            var pivot = chunkScalePivots[i].Value;
                            var inversePivot = -1.0f * pivot;

                            chunkCompositeScales[i] = new CompositeScale
                            {
                                Value = math.mul(math.mul(float4x4.Translate(pivot), float4x4.Scale(scale)),
                                    float4x4.Translate(inversePivot))
                            };
                        }
                    }
                    else // if (hasNonUniformScalee)
                    {
                        var didChange = chunk.DidChange(ref NonUniformScaleTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref ScalePivotTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var scale = chunkNonUniformScale[i].Value;
                            var pivot = chunkScalePivots[i].Value;
                            var inversePivot = -1.0f * pivot;

                            chunkCompositeScales[i] = new CompositeScale
                            {
                                Value = math.mul(math.mul(float4x4.Translate(pivot), float4x4.Scale(scale)),
                                    float4x4.Translate(inversePivot))
                            };
                        }
                    }
                }
                // 110
                else if (hasAnyScale && hasScalePivotTranslation && !hasScalePivot)
                {
                    // Has both valid input, but Scale overwrites.
                    if (hasScale)
                    {
                        var didChange = chunk.DidChange(ref ScaleTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref ScalePivotTranslationTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var translation = chunkScalePivotTranslations[i].Value;
                            var scale = chunkScales[i].Value;

                            chunkCompositeScales[i] = new CompositeScale
                            {Value = math.mul(float4x4.Translate(translation), float4x4.Scale(scale))};
                        }
                    }
                    else // if (hasNonUniformScale)
                    {
                        var didChange = chunk.DidChange(ref NonUniformScaleTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref ScalePivotTranslationTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var translation = chunkScalePivotTranslations[i].Value;
                            var scale = chunkNonUniformScale[i].Value;

                            chunkCompositeScales[i] = new CompositeScale
                            {Value = math.mul(float4x4.Translate(translation), float4x4.Scale(scale))};
                        }
                    }
                }
                // 111
                else if (hasAnyScale && hasScalePivotTranslation && hasScalePivot)
                {
                    // Has both valid input, but Scale overwrites.
                    if (hasScale)
                    {
                        var didChange = chunk.DidChange(ref ScaleTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref ScalePivotTranslationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref ScalePivotTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var translation = chunkScalePivotTranslations[i].Value;
                            var scale = chunkScales[i].Value;
                            var pivot = chunkScalePivots[i].Value;
                            var inversePivot = -1.0f * pivot;

                            chunkCompositeScales[i] = new CompositeScale
                            {
                                Value = math.mul(float4x4.Translate(translation),
                                    math.mul(math.mul(float4x4.Translate(pivot), float4x4.Scale(scale)),
                                        float4x4.Translate(inversePivot)))
                            };
                        }
                    }
                    else // if (hasNonUniformScale)
                    {
                        var didChange = chunk.DidChange(ref NonUniformScaleTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref ScalePivotTranslationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref ScalePivotTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var translation = chunkScalePivotTranslations[i].Value;
                            var scale = chunkNonUniformScale[i].Value;
                            var pivot = chunkScalePivots[i].Value;
                            var inversePivot = -1.0f * pivot;

                            chunkCompositeScales[i] = new CompositeScale
                            {
                                Value = math.mul(float4x4.Translate(translation),
                                    math.mul(math.mul(float4x4.Translate(pivot), float4x4.Scale(scale)),
                                        float4x4.Translate(inversePivot)))
                            };
                        }
                    }
                }
            }
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<CompositeScale>()
                .WithAny<Scale, NonUniformScale, ScalePivot, ScalePivotTranslation>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup);
            m_Query = state.GetEntityQuery(builder);

            CompositeScaleType = state.GetComponentTypeHandle<CompositeScale>(false);
            ScaleType = state.GetComponentTypeHandle<Scale>(true);
            NonUniformScaleType = state.GetComponentTypeHandle<NonUniformScale>(true);
            ScalePivotTranslationType = state.GetComponentTypeHandle<ScalePivotTranslation>(true);
            ScalePivotType = state.GetComponentTypeHandle<ScalePivot>(true);
        }

        ComponentTypeHandle<NonUniformScale> NonUniformScaleType;
        ComponentTypeHandle<Scale> ScaleType;
        ComponentTypeHandle<CompositeScale> CompositeScaleType;
        ComponentTypeHandle<ScalePivotTranslation> ScalePivotTranslationType;
        private ComponentTypeHandle<ScalePivot> ScalePivotType;

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            CompositeScaleType.Update(ref state);
            ScaleType.Update(ref state);
            NonUniformScaleType.Update(ref state);
            ScalePivotTranslationType.Update(ref state);
            ScalePivotType.Update(ref state);

            var toCompositeScaleJob = new ToCompositeScale
            {
                CompositeScaleTypeHandle = CompositeScaleType,
                NonUniformScaleTypeHandle = NonUniformScaleType,
                ScaleTypeHandle = ScaleType,
                ScalePivotTypeHandle = ScalePivotType,
                ScalePivotTranslationTypeHandle = ScalePivotTranslationType,
                LastSystemVersion = state.LastSystemVersion
            };
            state.Dependency = toCompositeScaleJob.ScheduleParallel(m_Query, state.Dependency);
        }
    }
}

#endif
