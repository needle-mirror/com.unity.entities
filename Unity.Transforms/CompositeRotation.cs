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
    public struct CompositeRotation : IComponentData
    {
        public float4x4 Value;
    }

    [Serializable]
    [WriteGroup(typeof(CompositeRotation))]
    public struct PostRotation : IComponentData
    {
        public quaternion Value;
    }

    [Serializable]
    [WriteGroup(typeof(CompositeRotation))]
    public struct RotationPivot : IComponentData
    {
        public float3 Value;
    }

    [Serializable]
    [WriteGroup(typeof(CompositeRotation))]
    public struct RotationPivotTranslation : IComponentData
    {
        public float3 Value;
    }

    // CompositeRotation = RotationPivotTranslation * RotationPivot * Rotation * PostRotation * RotationPivot^-1
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    public partial struct CompositeRotationSystem : ISystem
    {
        private EntityQuery m_Query;

        ComponentTypeHandle<CompositeRotation> CompositeRotationType;
        ComponentTypeHandle<Rotation> RotationType;
        ComponentTypeHandle<PostRotation> PostRotationType;
        ComponentTypeHandle<RotationPivotTranslation> RotationPivotTranslationType;
        ComponentTypeHandle<RotationPivot> RotationPivotType;

        [BurstCompile]
        struct ToCompositeRotation : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<PostRotation> PostRotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<Rotation> RotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<RotationPivot> RotationPivotTypeHandle;
            [ReadOnly] public ComponentTypeHandle<RotationPivotTranslation> RotationPivotTranslationTypeHandle;
            public ComponentTypeHandle<CompositeRotation> CompositeRotationTypeHandle;
            public uint LastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);

                var chunkRotationPivotTranslations = chunk.GetNativeArray(ref RotationPivotTranslationTypeHandle);
                var chunkRotations = chunk.GetNativeArray(ref RotationTypeHandle);
                var chunkPostRotation = chunk.GetNativeArray(ref PostRotationTypeHandle);
                var chunkRotationPivots = chunk.GetNativeArray(ref RotationPivotTypeHandle);
                var chunkCompositeRotations = chunk.GetNativeArray(ref CompositeRotationTypeHandle);

                var hasRotationPivotTranslation = chunkRotationPivotTranslations.IsCreated;
                var hasRotation = chunkRotations.IsCreated;
                var hasPostRotation = chunkPostRotation.IsCreated;
                var hasRotationPivot = chunkRotationPivots.IsCreated;
                var count = chunk.Count;

                var hasAnyRotation = hasRotation || hasPostRotation;

                // 000 - Invalid. Must have at least one.
                // 001
                if (!hasAnyRotation && !hasRotationPivotTranslation && hasRotationPivot)
                {
                    var didChange = chunk.DidChange(ref RotationPivotTypeHandle, LastSystemVersion);
                    if (!didChange)
                        return;

                    // Only pivot? Doesn't do anything.
                    for (int i = 0; i < count; i++)
                        chunkCompositeRotations[i] = new CompositeRotation {Value = float4x4.identity};
                }
                // 010
                else if (!hasAnyRotation && hasRotationPivotTranslation && !hasRotationPivot)
                {
                    var didChange = chunk.DidChange(ref RotationPivotTranslationTypeHandle, LastSystemVersion);
                    if (!didChange)
                        return;

                    for (int i = 0; i < count; i++)
                    {
                        var translation = chunkRotationPivotTranslations[i].Value;

                        chunkCompositeRotations[i] = new CompositeRotation
                        {Value = float4x4.Translate(translation)};
                    }
                }
                // 011
                else if (!hasAnyRotation && hasRotationPivotTranslation && hasRotationPivot)
                {
                    var didChange = chunk.DidChange(ref RotationPivotTranslationTypeHandle, LastSystemVersion);
                    if (!didChange)
                        return;

                    // Pivot without rotation doesn't affect anything. Only translation.
                    for (int i = 0; i < count; i++)
                    {
                        var translation = chunkRotationPivotTranslations[i].Value;

                        chunkCompositeRotations[i] = new CompositeRotation
                        {Value = float4x4.Translate(translation)};
                    }
                }
                // 100
                else if (hasAnyRotation && !hasRotationPivotTranslation && !hasRotationPivot)
                {
                    // 00 - Not valid
                    // 01
                    if (!hasPostRotation && hasRotation)
                    {
                        var didChange = chunk.DidChange(ref RotationTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var rotation = chunkRotations[i].Value;

                            chunkCompositeRotations[i] = new CompositeRotation
                            {Value = new float4x4(rotation, float3.zero)};
                        }
                    }
                    // 10
                    else if (hasPostRotation && !hasRotation)
                    {
                        var didChange = chunk.DidChange(ref PostRotationTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var rotation = chunkPostRotation[i].Value;

                            chunkCompositeRotations[i] = new CompositeRotation
                            {Value = new float4x4(rotation, float3.zero)};
                        }
                    }
                    // 11
                    else if (hasPostRotation && hasRotation)
                    {
                        var didChange = chunk.DidChange(ref PostRotationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var rotation = math.mul(chunkRotations[i].Value, chunkPostRotation[i].Value);

                            chunkCompositeRotations[i] = new CompositeRotation
                            {Value = new float4x4(rotation, float3.zero)};
                        }
                    }
                }
                // 101
                else if (hasAnyRotation && !hasRotationPivotTranslation && hasRotationPivot)
                {
                    // 00 - Not valid
                    // 01
                    if (!hasPostRotation && hasRotation)
                    {
                        var didChange = chunk.DidChange(ref RotationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationPivotTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var rotation = chunkRotations[i].Value;
                            var pivot = chunkRotationPivots[i].Value;
                            var inversePivot = -1.0f * pivot;

                            chunkCompositeRotations[i] = new CompositeRotation
                            {Value = math.mul(new float4x4(rotation, pivot), float4x4.Translate(inversePivot))};
                        }
                    }
                    // 10
                    else if (hasPostRotation && !hasRotation)
                    {
                        var didChange = chunk.DidChange(ref PostRotationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationPivotTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var rotation = chunkPostRotation[i].Value;
                            var pivot = chunkRotationPivots[i].Value;
                            var inversePivot = -1.0f * pivot;

                            chunkCompositeRotations[i] = new CompositeRotation
                            {Value = math.mul(new float4x4(rotation, pivot), float4x4.Translate(inversePivot))};
                        }
                    }
                    // 11
                    else if (hasPostRotation && hasRotation)
                    {
                        var didChange = chunk.DidChange(ref PostRotationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationPivotTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var rotation = chunkPostRotation[i].Value;
                            var pivot = chunkRotationPivots[i].Value;
                            var inversePivot = -1.0f * pivot;

                            chunkCompositeRotations[i] = new CompositeRotation
                            {Value = math.mul(new float4x4(rotation, pivot), float4x4.Translate(inversePivot))};
                        }
                    }
                }
                // 110
                else if (hasAnyRotation && hasRotationPivotTranslation && !hasRotationPivot)
                {
                    // 00 - Not valid
                    // 01
                    if (!hasPostRotation && hasRotation)
                    {
                        var didChange = chunk.DidChange(ref RotationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationPivotTranslationTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var translation = chunkRotationPivotTranslations[i].Value;
                            var rotation = chunkRotations[i].Value;

                            chunkCompositeRotations[i] = new CompositeRotation
                            {Value = new float4x4(rotation, translation)};
                        }
                    }
                    // 10
                    else if (hasPostRotation && !hasRotation)
                    {
                        var didChange = chunk.DidChange(ref PostRotationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationPivotTranslationTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var translation = chunkRotationPivotTranslations[i].Value;
                            var rotation = chunkRotations[i].Value;

                            chunkCompositeRotations[i] = new CompositeRotation
                            {Value = new float4x4(rotation, translation)};
                        }
                    }
                    // 11
                    else if (hasPostRotation && hasRotation)
                    {
                        var didChange = chunk.DidChange(ref PostRotationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationPivotTranslationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var translation = chunkRotationPivotTranslations[i].Value;
                            var rotation = math.mul(chunkRotations[i].Value, chunkPostRotation[i].Value);

                            chunkCompositeRotations[i] = new CompositeRotation
                            {Value = new float4x4(rotation, translation)};
                        }
                    }
                }
                // 111
                else if (hasAnyRotation && hasRotationPivotTranslation && hasRotationPivot)
                {
                    // 00 - Not valid
                    // 01
                    if (!hasPostRotation && hasRotation)
                    {
                        var didChange = chunk.DidChange(ref RotationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationPivotTranslationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationPivotTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var translation = chunkRotationPivotTranslations[i].Value;
                            var rotation = chunkRotations[i].Value;
                            var pivot = chunkRotationPivots[i].Value;
                            var inversePivot = -1.0f * pivot;

                            chunkCompositeRotations[i] = new CompositeRotation
                            {
                                Value = math.mul(float4x4.Translate(translation),
                                    math.mul(new float4x4(rotation, pivot), float4x4.Translate(inversePivot)))
                            };
                        }
                    }
                    // 10
                    else if (hasPostRotation && !hasRotation)
                    {
                        var didChange = chunk.DidChange(ref PostRotationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationPivotTranslationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationPivotTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var translation = chunkRotationPivotTranslations[i].Value;
                            var rotation = chunkPostRotation[i].Value;
                            var pivot = chunkRotationPivots[i].Value;
                            var inversePivot = -1.0f * pivot;

                            chunkCompositeRotations[i] = new CompositeRotation
                            {
                                Value = math.mul(float4x4.Translate(translation),
                                    math.mul(new float4x4(rotation, pivot), float4x4.Translate(inversePivot)))
                            };
                        }
                    }
                    // 11
                    else if (hasPostRotation && hasRotation)
                    {
                        var didChange = chunk.DidChange(ref PostRotationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationPivotTranslationTypeHandle, LastSystemVersion) ||
                            chunk.DidChange(ref RotationPivotTypeHandle, LastSystemVersion);
                        if (!didChange)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            var translation = chunkRotationPivotTranslations[i].Value;
                            var rotation = math.mul(chunkRotations[i].Value, chunkPostRotation[i].Value);
                            var pivot = chunkRotationPivots[i].Value;
                            var inversePivot = -1.0f * pivot;

                            chunkCompositeRotations[i] = new CompositeRotation
                            {
                                Value = math.mul(float4x4.Translate(translation),
                                    math.mul(new float4x4(rotation, pivot), float4x4.Translate(inversePivot)))
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
                .WithAllRW<CompositeRotation>()
                .WithAny<Rotation, PostRotation, RotationPivot, RotationPivotTranslation>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup);
            m_Query = state.GetEntityQuery(builder);

            CompositeRotationType = state.GetComponentTypeHandle<CompositeRotation>(false);
            RotationType = state.GetComponentTypeHandle<Rotation>(true);
            PostRotationType = state.GetComponentTypeHandle<PostRotation>(true);
            RotationPivotTranslationType = state.GetComponentTypeHandle<RotationPivotTranslation>(true);
            RotationPivotType   = state.GetComponentTypeHandle<RotationPivot>(true);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            CompositeRotationType.Update(ref state);
            RotationType.Update(ref state);
            PostRotationType.Update(ref state);
            RotationPivotTranslationType.Update(ref state);
            RotationPivotType.Update(ref state);

            var toCompositeRotationJob = new ToCompositeRotation
            {
                CompositeRotationTypeHandle = CompositeRotationType,
                PostRotationTypeHandle = PostRotationType,
                RotationTypeHandle = RotationType,
                RotationPivotTypeHandle = RotationPivotType,
                RotationPivotTranslationTypeHandle = RotationPivotTranslationType,
                LastSystemVersion = state.LastSystemVersion
            };
            state.Dependency = toCompositeRotationJob.ScheduleParallel(m_Query, state.Dependency);
        }
    }
}

#endif
