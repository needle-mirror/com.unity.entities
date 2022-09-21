using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
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
    // LocalToWorld = Translation * Rotation * NonUniformScale
    // (or) LocalToWorld = Translation * CompositeRotation * NonUniformScale
    // (or) LocalToWorld = Translation * Rotation * Scale
    // (or) LocalToWorld = Translation * CompositeRotation * Scale
    // (or) LocalToWorld = Translation * Rotation * CompositeScale
    // (or) LocalToWorld = Translation * CompositeRotation * CompositeScale
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    public partial struct TRSToLocalToWorldSystem : ISystem
    {
        private EntityQuery m_Query;

        ComponentTypeHandle<Rotation> RotationType;
        ComponentTypeHandle<CompositeRotation> CompositeRotationType;
        ComponentTypeHandle<Translation> TranslationType;
        ComponentTypeHandle<NonUniformScale> NonUniformScaleType;
        ComponentTypeHandle<Scale> ScaleType;
        ComponentTypeHandle<CompositeScale> CompositeScaleType;
        ComponentTypeHandle<LocalToWorld> LocalToWorldType;

        [BurstCompile]
        struct TRSToLocalToWorld : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<Rotation> RotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<CompositeRotation> CompositeRotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<Translation> TranslationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<NonUniformScale> NonUniformScaleTypeHandle;
            [ReadOnly] public ComponentTypeHandle<Scale> ScaleTypeHandle;
            [ReadOnly] public ComponentTypeHandle<CompositeScale> CompositeScaleTypeHandle;
            public ComponentTypeHandle<LocalToWorld> LocalToWorldTypeHandle;
            public uint LastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);

                bool changed =
                    chunk.DidOrderChange(LastSystemVersion) ||
                    chunk.DidChange(TranslationTypeHandle, LastSystemVersion) ||
                    chunk.DidChange(NonUniformScaleTypeHandle, LastSystemVersion) ||
                    chunk.DidChange(ScaleTypeHandle, LastSystemVersion) ||
                    chunk.DidChange(CompositeScaleTypeHandle, LastSystemVersion) ||
                    chunk.DidChange(RotationTypeHandle, LastSystemVersion) ||
                    chunk.DidChange(CompositeRotationTypeHandle, LastSystemVersion);
                if (!changed)
                {
                    return;
                }

                var chunkTranslations = chunk.GetNativeArray(TranslationTypeHandle);
                var chunkNonUniformScales = chunk.GetNativeArray(NonUniformScaleTypeHandle);
                var chunkScales = chunk.GetNativeArray(ScaleTypeHandle);
                var chunkCompositeScales = chunk.GetNativeArray(CompositeScaleTypeHandle);
                var chunkRotations = chunk.GetNativeArray(RotationTypeHandle);
                var chunkCompositeRotations = chunk.GetNativeArray(CompositeRotationTypeHandle);
                var chunkLocalToWorld = chunk.GetNativeArray(LocalToWorldTypeHandle);
                var hasTranslation = chunk.Has(TranslationTypeHandle);
                var hasCompositeRotation = chunk.Has(CompositeRotationTypeHandle);
                var hasRotation = chunk.Has(RotationTypeHandle);
                var hasAnyRotation = hasCompositeRotation || hasRotation;
                var hasNonUniformScale = chunk.Has(NonUniformScaleTypeHandle);
                var hasScale = chunk.Has(ScaleTypeHandle);
                var hasCompositeScale = chunk.Has(CompositeScaleTypeHandle);
                var hasAnyScale = hasScale || hasNonUniformScale || hasCompositeScale;
                var count = chunk.Count;

                // #todo jump table when burst supports function pointers

                if (!hasAnyRotation)
                {
                    // 00 = invalid (must have at least one)
                    // 01
                    if (!hasTranslation && hasAnyScale)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            var scale = hasNonUniformScale ? float4x4.Scale(chunkNonUniformScales[i].Value) : (hasScale ? float4x4.Scale(new float3(chunkScales[i].Value)) : chunkCompositeScales[i].Value);

                            chunkLocalToWorld[i] = new LocalToWorld
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

                            chunkLocalToWorld[i] = new LocalToWorld
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
                            var scale = hasNonUniformScale ? float4x4.Scale(chunkNonUniformScales[i].Value) : (hasScale ? float4x4.Scale(new float3(chunkScales[i].Value)) : chunkCompositeScales[i].Value);
                            var translation = chunkTranslations[i].Value;

                            chunkLocalToWorld[i] = new LocalToWorld
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

                            chunkLocalToWorld[i] = new LocalToWorld
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
                            var scale = hasNonUniformScale ? float4x4.Scale(chunkNonUniformScales[i].Value) : (hasScale ? float4x4.Scale(new float3(chunkScales[i].Value)) : chunkCompositeScales[i].Value);

                            chunkLocalToWorld[i] = new LocalToWorld
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

                            chunkLocalToWorld[i] = new LocalToWorld
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
                            var scale = hasNonUniformScale ? float4x4.Scale(chunkNonUniformScales[i].Value) : (hasScale ? float4x4.Scale(new float3(chunkScales[i].Value)) : chunkCompositeScales[i].Value);

                            chunkLocalToWorld[i] = new LocalToWorld
                            {
                                Value = math.mul(math.mul(float4x4.Translate(translation), rotation) , scale)
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

                            chunkLocalToWorld[i] = new LocalToWorld
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
                            var scale = hasNonUniformScale ? float4x4.Scale(chunkNonUniformScales[i].Value) : (hasScale ? float4x4.Scale(new float3(chunkScales[i].Value)) : chunkCompositeScales[i].Value);

                            chunkLocalToWorld[i] = new LocalToWorld
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

                            chunkLocalToWorld[i] = new LocalToWorld
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
                            var scale = hasNonUniformScale ? float4x4.Scale(chunkNonUniformScales[i].Value) : (hasScale ? float4x4.Scale(new float3(chunkScales[i].Value)) : chunkCompositeScales[i].Value);

                            chunkLocalToWorld[i] = new LocalToWorld
                            {
                                Value = math.mul(new float4x4(rotation, translation), scale)
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
                .WithAllRW<LocalToWorld>()
                .WithAny<NonUniformScale>()
                .WithAny<Scale>()
                .WithAny<Rotation>()
                .WithAny<CompositeRotation>()
                .WithAny<CompositeScale>()
                .WithAny<Translation>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup);
            m_Query = state.GetEntityQuery(builder);

            RotationType = state.GetComponentTypeHandle<Rotation>(true);
            CompositeRotationType = state.GetComponentTypeHandle<CompositeRotation>(true);
            TranslationType = state.GetComponentTypeHandle<Translation>(true);
            NonUniformScaleType = state.GetComponentTypeHandle<NonUniformScale>(true);
            ScaleType = state.GetComponentTypeHandle<Scale>(true);
            CompositeScaleType = state.GetComponentTypeHandle<CompositeScale>(true);
            LocalToWorldType = state.GetComponentTypeHandle<LocalToWorld>(false);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
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
            LocalToWorldType.Update(ref state);

            var trsToLocalToWorldJob = new TRSToLocalToWorld()
            {
                RotationTypeHandle = RotationType,
                CompositeRotationTypeHandle = CompositeRotationType,
                TranslationTypeHandle = TranslationType,
                ScaleTypeHandle = ScaleType,
                NonUniformScaleTypeHandle = NonUniformScaleType,
                CompositeScaleTypeHandle = CompositeScaleType,
                LocalToWorldTypeHandle = LocalToWorldType,
                LastSystemVersion = state.LastSystemVersion
            };
            state.Dependency = trsToLocalToWorldJob.ScheduleParallel(m_Query, state.Dependency);
        }
    }
}

#endif
