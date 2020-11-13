using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

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
    public abstract class CompositeRotationSystem : JobComponentSystem
    {
        private EntityQuery m_Query;

        [BurstCompile]
        struct ToCompositeRotation : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<PostRotation> PostRotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<Rotation> RotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<RotationPivot> RotationPivotTypeHandle;
            [ReadOnly] public ComponentTypeHandle<RotationPivotTranslation> RotationPivotTranslationTypeHandle;
            public ComponentTypeHandle<CompositeRotation> CompositeRotationTypeHandle;
            public uint LastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var chunkRotationPivotTranslations = batchInChunk.GetNativeArray(RotationPivotTranslationTypeHandle);
                var chunkRotations = batchInChunk.GetNativeArray(RotationTypeHandle);
                var chunkPostRotation = batchInChunk.GetNativeArray(PostRotationTypeHandle);
                var chunkRotationPivots = batchInChunk.GetNativeArray(RotationPivotTypeHandle);
                var chunkCompositeRotations = batchInChunk.GetNativeArray(CompositeRotationTypeHandle);

                var hasRotationPivotTranslation = batchInChunk.Has(RotationPivotTranslationTypeHandle);
                var hasRotation = batchInChunk.Has(RotationTypeHandle);
                var hasPostRotation = batchInChunk.Has(PostRotationTypeHandle);
                var hasRotationPivot = batchInChunk.Has(RotationPivotTypeHandle);
                var count = batchInChunk.Count;

                var hasAnyRotation = hasRotation || hasPostRotation;

                // 000 - Invalid. Must have at least one.
                // 001
                if (!hasAnyRotation && !hasRotationPivotTranslation && hasRotationPivot)
                {
                    var didChange = batchInChunk.DidChange(RotationPivotTypeHandle, LastSystemVersion);
                    if (!didChange)
                        return;

                    // Only pivot? Doesn't do anything.
                    for (int i = 0; i < count; i++)
                        chunkCompositeRotations[i] = new CompositeRotation {Value = float4x4.identity};
                }
                // 010
                else if (!hasAnyRotation && hasRotationPivotTranslation && !hasRotationPivot)
                {
                    var didChange = batchInChunk.DidChange(RotationPivotTranslationTypeHandle, LastSystemVersion);
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
                    var didChange = batchInChunk.DidChange(RotationPivotTranslationTypeHandle, LastSystemVersion);
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
                        var didChange = batchInChunk.DidChange(RotationTypeHandle, LastSystemVersion);
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
                        var didChange = batchInChunk.DidChange(PostRotationTypeHandle, LastSystemVersion);
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
                        var didChange = batchInChunk.DidChange(PostRotationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationTypeHandle, LastSystemVersion);
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
                        var didChange = batchInChunk.DidChange(RotationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationPivotTypeHandle, LastSystemVersion);
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
                        var didChange = batchInChunk.DidChange(PostRotationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationPivotTypeHandle, LastSystemVersion);
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
                        var didChange = batchInChunk.DidChange(PostRotationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationPivotTypeHandle, LastSystemVersion);
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
                        var didChange = batchInChunk.DidChange(RotationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationPivotTranslationTypeHandle, LastSystemVersion);
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
                        var didChange = batchInChunk.DidChange(PostRotationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationPivotTranslationTypeHandle, LastSystemVersion);
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
                        var didChange = batchInChunk.DidChange(PostRotationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationPivotTranslationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationTypeHandle, LastSystemVersion);
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
                        var didChange = batchInChunk.DidChange(RotationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationPivotTranslationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationPivotTypeHandle, LastSystemVersion);
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
                        var didChange = batchInChunk.DidChange(PostRotationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationPivotTranslationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationPivotTypeHandle, LastSystemVersion);
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
                        var didChange = batchInChunk.DidChange(PostRotationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationPivotTranslationTypeHandle, LastSystemVersion) ||
                            batchInChunk.DidChange(RotationPivotTypeHandle, LastSystemVersion);
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

        protected override void OnCreate()
        {
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    typeof(CompositeRotation)
                },
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<Rotation>(),
                    ComponentType.ReadOnly<PostRotation>(),
                    ComponentType.ReadOnly<RotationPivot>(),
                    ComponentType.ReadOnly<RotationPivotTranslation>()
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var compositeRotationType = GetComponentTypeHandle<CompositeRotation>(false);
            var rotationType = GetComponentTypeHandle<Rotation>(true);
            var preRotationType = GetComponentTypeHandle<PostRotation>(true);
            var rotationPivotTranslationType = GetComponentTypeHandle<RotationPivotTranslation>(true);
            var rotationPivotType = GetComponentTypeHandle<RotationPivot>(true);

            var toCompositeRotationJob = new ToCompositeRotation
            {
                CompositeRotationTypeHandle = compositeRotationType,
                PostRotationTypeHandle = preRotationType,
                RotationTypeHandle = rotationType,
                RotationPivotTypeHandle = rotationPivotType,
                RotationPivotTranslationTypeHandle = rotationPivotTranslationType,
                LastSystemVersion = LastSystemVersion
            };
            var toCompositeRotationJobHandle = toCompositeRotationJob.ScheduleParallel(m_Query, 1, inputDeps);
            return toCompositeRotationJobHandle;
        }
    }
}
