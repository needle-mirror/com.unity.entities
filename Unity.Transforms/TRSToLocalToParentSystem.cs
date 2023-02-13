using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

#if !ENABLE_TRANSFORM_V1
#else

/* **************
   COPY AND PASTE
   **************
 * TRSLocalToWorldSystem and TRSLocalToParentSystem are copy-and-paste.
 * Any changes to one must be copied to the other.
 * The only differences are:
 *   - s/LocalToWorld/LocalToParent/g
 *   - Add variation for ParentScaleInverse
*/

namespace Unity.Transforms
{
    // LocalToParent = Translation * Rotation * NonUniformScale
    // (or) LocalToParent = Translation * CompositeRotation * NonUniformScale
    // (or) LocalToParent = Translation * Rotation * Scale
    // (or) LocalToParent = Translation * CompositeRotation * Scale
    // (or) LocalToParent = Translation * Rotation * CompositeScale
    // (or) LocalToParent = Translation * CompositeRotation * CompositeScale
    // (or) LocalToParent = Translation * ParentScaleInverse * Rotation * NonUniformScale
    // (or) LocalToParent = Translation * ParentScaleInverse * CompositeRotation * NonUniformScale
    // (or) LocalToParent = Translation * ParentScaleInverse * Rotation * Scale
    // (or) LocalToParent = Translation * ParentScaleInverse * CompositeRotation * Scale
    // (or) LocalToParent = Translation * ParentScaleInverse * Rotation * CompositeScale
    // (or) LocalToParent = Translation * ParentScaleInverse * CompositeRotation * CompositeScale

    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    public partial struct TRSToLocalToParentSystem : ISystem
    {
        private EntityQuery m_Query;

        ComponentTypeHandle<Rotation> RotationType;
        ComponentTypeHandle<CompositeRotation> CompositeRotationType;
        ComponentTypeHandle<Translation> TranslationType;
        ComponentTypeHandle<NonUniformScale> NonUniformScaleType;
        ComponentTypeHandle<Scale> ScaleType;
        ComponentTypeHandle<CompositeScale> CompositeScaleType;
        ComponentTypeHandle<ParentScaleInverse> ParentScaleInverseType;
        ComponentTypeHandle<LocalToParent> LocalToParentType;

        [BurstCompile]
        struct TRSToLocalToParent : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<Rotation> RotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<CompositeRotation> CompositeRotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<Translation> TranslationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<NonUniformScale> NonUniformScaleTypeHandle;
            [ReadOnly] public ComponentTypeHandle<Scale> ScaleTypeHandle;
            [ReadOnly] public ComponentTypeHandle<CompositeScale> CompositeScaleTypeHandle;
            [ReadOnly] public ComponentTypeHandle<ParentScaleInverse> ParentScaleInverseTypeHandle;
            public ComponentTypeHandle<LocalToParent> LocalToParentTypeHandle;
            public uint LastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);

                bool changed =
                    chunk.DidOrderChange(LastSystemVersion) ||
                    chunk.DidChange(ref TranslationTypeHandle, LastSystemVersion) ||
                    chunk.DidChange(ref RotationTypeHandle, LastSystemVersion) ||
                    chunk.DidChange(ref CompositeRotationTypeHandle, LastSystemVersion) ||
                    chunk.DidChange(ref ScaleTypeHandle, LastSystemVersion) ||
                    chunk.DidChange(ref NonUniformScaleTypeHandle, LastSystemVersion) ||
                    chunk.DidChange(ref CompositeScaleTypeHandle, LastSystemVersion) ||
                    chunk.DidChange(ref ParentScaleInverseTypeHandle, LastSystemVersion);
                if (!changed)
                {
                    return;
                }

                var chunkTranslations = chunk.GetNativeArray(ref TranslationTypeHandle);
                var chunkNonUniformScales = chunk.GetNativeArray(ref NonUniformScaleTypeHandle);
                var chunkScales = chunk.GetNativeArray(ref ScaleTypeHandle);
                var chunkCompositeScales = chunk.GetNativeArray(ref CompositeScaleTypeHandle);
                var chunkRotations = chunk.GetNativeArray(ref RotationTypeHandle);
                var chunkCompositeRotations = chunk.GetNativeArray(ref CompositeRotationTypeHandle);
                var chunkLocalToParent = chunk.GetNativeArray(ref LocalToParentTypeHandle);
                var chunkParentScaleInverses = chunk.GetNativeArray(ref ParentScaleInverseTypeHandle);
                var hasTranslation = chunkTranslations.IsCreated;
                var hasCompositeRotation = chunkCompositeRotations.IsCreated;
                var hasRotation = chunkRotations.IsCreated;
                var hasAnyRotation = hasCompositeRotation || hasRotation;
                var hasNonUniformScale = chunkNonUniformScales.IsCreated;
                var hasScale = chunkScales.IsCreated;
                var hasCompositeScale = chunkCompositeScales.IsCreated;
                var hasAnyScale = hasScale || hasNonUniformScale || hasCompositeScale;
                var hasParentScaleInverse = chunkParentScaleInverses.IsCreated;
                var count = chunk.Count;

                // #todo jump table when burst supports function pointers

                if (hasParentScaleInverse)
                {
                    if (!hasAnyRotation)
                    {
                        // 00 = invalid (must have at least one)
                        // 01
                        if (!hasTranslation && hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var parentScaleInverse = chunkParentScaleInverses[i].Value;
                                var scale = hasNonUniformScale
                                    ? float4x4.Scale(chunkNonUniformScales[i].Value)
                                    : (hasScale
                                        ? float4x4.Scale(new float3(chunkScales[i].Value))
                                        : chunkCompositeScales[i].Value);

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(parentScaleInverse, scale)
                                };
                            }
                        }
                        // 10
                        else if (hasTranslation && !hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var parentScaleInverse = chunkParentScaleInverses[i].Value;
                                var translation = chunkTranslations[i].Value;

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(float4x4.Translate(translation), parentScaleInverse)
                                };
                            }
                        }
                        // 11
                        else if (hasTranslation && hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var parentScaleInverse = chunkParentScaleInverses[i].Value;
                                var scale = hasNonUniformScale
                                    ? float4x4.Scale(chunkNonUniformScales[i].Value)
                                    : (hasScale
                                        ? float4x4.Scale(new float3(chunkScales[i].Value))
                                        : chunkCompositeScales[i].Value);
                                var translation = chunkTranslations[i].Value;

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(math.mul(float4x4.Translate(translation), parentScaleInverse),
                                        scale)
                                };
                            }
                        }
                    }
                    else if (hasCompositeRotation)
                    {
                        // 00
                        if (!hasTranslation && !hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var parentScaleInverse = chunkParentScaleInverses[i].Value;
                                var rotation = chunkCompositeRotations[i].Value;

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(parentScaleInverse, rotation)
                                };
                            }
                        }
                        // 01
                        else if (!hasTranslation && hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var parentScaleInverse = chunkParentScaleInverses[i].Value;
                                var rotation = chunkCompositeRotations[i].Value;
                                var scale = hasNonUniformScale
                                    ? float4x4.Scale(chunkNonUniformScales[i].Value)
                                    : (hasScale
                                        ? float4x4.Scale(new float3(chunkScales[i].Value))
                                        : chunkCompositeScales[i].Value);

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(parentScaleInverse, math.mul(rotation, scale))
                                };
                            }
                        }
                        // 10
                        else if (hasTranslation && !hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var parentScaleInverse = chunkParentScaleInverses[i].Value;
                                var rotation = chunkCompositeRotations[i].Value;
                                var translation = chunkTranslations[i].Value;

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(math.mul(float4x4.Translate(translation), parentScaleInverse),
                                        rotation)
                                };
                            }
                        }
                        // 11
                        else if (hasTranslation && hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var parentScaleInverse = chunkParentScaleInverses[i].Value;
                                var rotation = chunkCompositeRotations[i].Value;
                                var translation = chunkTranslations[i].Value;
                                var scale = hasNonUniformScale
                                    ? float4x4.Scale(chunkNonUniformScales[i].Value)
                                    : (hasScale
                                        ? float4x4.Scale(new float3(chunkScales[i].Value))
                                        : chunkCompositeScales[i].Value);

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(
                                        math.mul(math.mul(float4x4.Translate(translation), parentScaleInverse),
                                            rotation), scale)
                                };
                            }
                        }
                    }
                    else // if (hasRotation) -- Only in same WriteGroup if !hasCompositeRotation
                    {
                        // 00
                        if (!hasTranslation && !hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var parentScaleInverse = chunkParentScaleInverses[i].Value;
                                var rotation = chunkRotations[i].Value;

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(parentScaleInverse, new float4x4(rotation, float3.zero))
                                };
                            }
                        }
                        // 01
                        else if (!hasTranslation && hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var parentScaleInverse = chunkParentScaleInverses[i].Value;
                                var rotation = chunkRotations[i].Value;
                                var scale = hasNonUniformScale
                                    ? float4x4.Scale(chunkNonUniformScales[i].Value)
                                    : (hasScale
                                        ? float4x4.Scale(new float3(chunkScales[i].Value))
                                        : chunkCompositeScales[i].Value);

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(parentScaleInverse,
                                        math.mul(new float4x4(rotation, float3.zero), scale))
                                };
                            }
                        }
                        // 10
                        else if (hasTranslation && !hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var parentScaleInverse = chunkParentScaleInverses[i].Value;
                                var rotation = chunkRotations[i].Value;
                                var translation = chunkTranslations[i].Value;

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(math.mul(float4x4.Translate(translation), parentScaleInverse),
                                        new float4x4(rotation, new float3(0.0f)))
                                };
                            }
                        }
                        // 11
                        else if (hasTranslation && hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var parentScaleInverse = chunkParentScaleInverses[i].Value;
                                var rotation = chunkRotations[i].Value;
                                var translation = chunkTranslations[i].Value;
                                var scale = hasNonUniformScale
                                    ? float4x4.Scale(chunkNonUniformScales[i].Value)
                                    : (hasScale
                                        ? float4x4.Scale(new float3(chunkScales[i].Value))
                                        : chunkCompositeScales[i].Value);

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(
                                        math.mul(math.mul(float4x4.Translate(translation), parentScaleInverse),
                                            new float4x4(rotation, new float3(0.0f))), scale)
                                };
                            }
                        }
                    }
                }
                else // (!hasParentScaleInverse)
                {
                    if (!hasAnyRotation)
                    {
                        // 00 = invalid (must have at least one)
                        // 01
                        if (!hasTranslation && hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var scale = hasNonUniformScale
                                    ? float4x4.Scale(chunkNonUniformScales[i].Value)
                                    : (hasScale
                                        ? float4x4.Scale(new float3(chunkScales[i].Value))
                                        : chunkCompositeScales[i].Value);

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = scale
                                };
                            }
                        }
                        // 10
                        else if (hasTranslation && !hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var translation = chunkTranslations[i].Value;

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = float4x4.Translate(translation)
                                };
                            }
                        }
                        // 11
                        else if (hasTranslation && hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var scale = hasNonUniformScale
                                    ? float4x4.Scale(chunkNonUniformScales[i].Value)
                                    : (hasScale
                                        ? float4x4.Scale(new float3(chunkScales[i].Value))
                                        : chunkCompositeScales[i].Value);
                                var translation = chunkTranslations[i].Value;

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(float4x4.Translate(translation), scale)
                                };
                            }
                        }
                    }
                    else if (hasCompositeRotation)
                    {
                        // 00
                        if (!hasTranslation && !hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var rotation = chunkCompositeRotations[i].Value;

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = rotation
                                };
                            }
                        }
                        // 01
                        else if (!hasTranslation && hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var rotation = chunkCompositeRotations[i].Value;
                                var scale = hasNonUniformScale
                                    ? float4x4.Scale(chunkNonUniformScales[i].Value)
                                    : (hasScale
                                        ? float4x4.Scale(new float3(chunkScales[i].Value))
                                        : chunkCompositeScales[i].Value);

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(rotation, scale)
                                };
                            }
                        }
                        // 10
                        else if (hasTranslation && !hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var rotation = chunkCompositeRotations[i].Value;
                                var translation = chunkTranslations[i].Value;

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(float4x4.Translate(translation), rotation)
                                };
                            }
                        }
                        // 11
                        else if (hasTranslation && hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var rotation = chunkCompositeRotations[i].Value;
                                var translation = chunkTranslations[i].Value;
                                var scale = hasNonUniformScale
                                    ? float4x4.Scale(chunkNonUniformScales[i].Value)
                                    : (hasScale
                                        ? float4x4.Scale(new float3(chunkScales[i].Value))
                                        : chunkCompositeScales[i].Value);

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(math.mul(float4x4.Translate(translation), rotation), scale)
                                };
                            }
                        }
                    }
                    else // if (hasRotation) -- Only in same WriteGroup if !hasCompositeRotation
                    {
                        // 00
                        if (!hasTranslation && !hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var rotation = chunkRotations[i].Value;

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = new float4x4(rotation, float3.zero)
                                };
                            }
                        }
                        // 01
                        else if (!hasTranslation && hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var rotation = chunkRotations[i].Value;
                                var scale = hasNonUniformScale
                                    ? float4x4.Scale(chunkNonUniformScales[i].Value)
                                    : (hasScale
                                        ? float4x4.Scale(new float3(chunkScales[i].Value))
                                        : chunkCompositeScales[i].Value);

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(new float4x4(rotation, float3.zero), scale)
                                };
                            }
                        }
                        // 10
                        else if (hasTranslation && !hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var rotation = chunkRotations[i].Value;
                                var translation = chunkTranslations[i].Value;

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = new float4x4(rotation, translation)
                                };
                            }
                        }
                        // 11
                        else if (hasTranslation && hasAnyScale)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var rotation = chunkRotations[i].Value;
                                var translation = chunkTranslations[i].Value;
                                var scale = hasNonUniformScale
                                    ? float4x4.Scale(chunkNonUniformScales[i].Value)
                                    : (hasScale
                                        ? float4x4.Scale(new float3(chunkScales[i].Value))
                                        : chunkCompositeScales[i].Value);

                                chunkLocalToParent[i] = new LocalToParent
                                {
                                    Value = math.mul(new float4x4(rotation, translation), scale)
                                };
                            }
                        }
                    }
                }
            }
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<LocalToParent>()
                .WithAny<NonUniformScale>()
                .WithAny<Scale>()
                .WithAny<Rotation>()
                .WithAny<CompositeRotation>()
                .WithAny<CompositeScale>()
                .WithAny<Translation>()
                .WithAny<ParentScaleInverse>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup);
            m_Query = state.GetEntityQuery(builder);

            RotationType = state.GetComponentTypeHandle<Rotation>(true);
            CompositeRotationType = state.GetComponentTypeHandle<CompositeRotation>(true);
            TranslationType = state.GetComponentTypeHandle<Translation>(true);
            NonUniformScaleType = state.GetComponentTypeHandle<NonUniformScale>(true);
            ScaleType = state.GetComponentTypeHandle<Scale>(true);
            CompositeScaleType = state.GetComponentTypeHandle<CompositeScale>(true);
            ParentScaleInverseType = state.GetComponentTypeHandle<ParentScaleInverse>(true);
            LocalToParentType = state.GetComponentTypeHandle<LocalToParent>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            RotationType.Update(ref state);
            CompositeRotationType.Update(ref state);
            TranslationType.Update(ref state);
            NonUniformScaleType.Update(ref state);
            ScaleType.Update(ref state);
            CompositeScaleType.Update(ref state);
            ParentScaleInverseType.Update(ref state);
            LocalToParentType.Update(ref state);

            var trsToLocalToParentJob = new TRSToLocalToParent()
            {
                RotationTypeHandle = RotationType,
                CompositeRotationTypeHandle = CompositeRotationType,
                TranslationTypeHandle = TranslationType,
                ScaleTypeHandle = ScaleType,
                NonUniformScaleTypeHandle = NonUniformScaleType,
                CompositeScaleTypeHandle = CompositeScaleType,
                ParentScaleInverseTypeHandle = ParentScaleInverseType,
                LocalToParentTypeHandle = LocalToParentType,
                LastSystemVersion = state.LastSystemVersion
            };
            state.Dependency = trsToLocalToParentJob.ScheduleParallel(m_Query, state.Dependency);
        }
    }
}

#endif
