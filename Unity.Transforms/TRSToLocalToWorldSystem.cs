using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

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
    public abstract class TRSToLocalToWorldSystem : JobComponentSystem
    {
        private EntityQuery m_Query;

        [BurstCompile]
        struct TRSToLocalToWorld : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<Rotation> RotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<CompositeRotation> CompositeRotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<Translation> TranslationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<NonUniformScale> NonUniformScaleTypeHandle;
            [ReadOnly] public ComponentTypeHandle<Scale> ScaleTypeHandle;
            [ReadOnly] public ComponentTypeHandle<CompositeScale> CompositeScaleTypeHandle;
            public ComponentTypeHandle<LocalToWorld> LocalToWorldTypeHandle;
            public uint LastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                bool changed =
                    batchInChunk.DidOrderChange(LastSystemVersion) ||
                    batchInChunk.DidChange(TranslationTypeHandle, LastSystemVersion) ||
                    batchInChunk.DidChange(NonUniformScaleTypeHandle, LastSystemVersion) ||
                    batchInChunk.DidChange(ScaleTypeHandle, LastSystemVersion) ||
                    batchInChunk.DidChange(CompositeScaleTypeHandle, LastSystemVersion) ||
                    batchInChunk.DidChange(RotationTypeHandle, LastSystemVersion) ||
                    batchInChunk.DidChange(CompositeRotationTypeHandle, LastSystemVersion);
                if (!changed)
                {
                    return;
                }

                var chunkTranslations = batchInChunk.GetNativeArray(TranslationTypeHandle);
                var chunkNonUniformScales = batchInChunk.GetNativeArray(NonUniformScaleTypeHandle);
                var chunkScales = batchInChunk.GetNativeArray(ScaleTypeHandle);
                var chunkCompositeScales = batchInChunk.GetNativeArray(CompositeScaleTypeHandle);
                var chunkRotations = batchInChunk.GetNativeArray(RotationTypeHandle);
                var chunkCompositeRotations = batchInChunk.GetNativeArray(CompositeRotationTypeHandle);
                var chunkLocalToWorld = batchInChunk.GetNativeArray(LocalToWorldTypeHandle);
                var hasTranslation = batchInChunk.Has(TranslationTypeHandle);
                var hasCompositeRotation = batchInChunk.Has(CompositeRotationTypeHandle);
                var hasRotation = batchInChunk.Has(RotationTypeHandle);
                var hasAnyRotation = hasCompositeRotation || hasRotation;
                var hasNonUniformScale = batchInChunk.Has(NonUniformScaleTypeHandle);
                var hasScale = batchInChunk.Has(ScaleTypeHandle);
                var hasCompositeScale = batchInChunk.Has(CompositeScaleTypeHandle);
                var hasAnyScale = hasScale || hasNonUniformScale || hasCompositeScale;
                var count = batchInChunk.Count;

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

        protected override void OnCreate()
        {
            m_Query = GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    typeof(LocalToWorld)
                },
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<NonUniformScale>(),
                    ComponentType.ReadOnly<Scale>(),
                    ComponentType.ReadOnly<Rotation>(),
                    ComponentType.ReadOnly<CompositeRotation>(),
                    ComponentType.ReadOnly<CompositeScale>(),
                    ComponentType.ReadOnly<Translation>()
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var rotationType = GetComponentTypeHandle<Rotation>(true);
            var compositeRotationType = GetComponentTypeHandle<CompositeRotation>(true);
            var translationType = GetComponentTypeHandle<Translation>(true);
            var nonUniformScaleType = GetComponentTypeHandle<NonUniformScale>(true);
            var scaleType = GetComponentTypeHandle<Scale>(true);
            var compositeScaleType = GetComponentTypeHandle<CompositeScale>(true);
            var localToWorldType = GetComponentTypeHandle<LocalToWorld>(false);
            var trsToLocalToWorldJob = new TRSToLocalToWorld()
            {
                RotationTypeHandle = rotationType,
                CompositeRotationTypeHandle = compositeRotationType,
                TranslationTypeHandle = translationType,
                ScaleTypeHandle = scaleType,
                NonUniformScaleTypeHandle = nonUniformScaleType,
                CompositeScaleTypeHandle = compositeScaleType,
                LocalToWorldTypeHandle = localToWorldType,
                LastSystemVersion = LastSystemVersion
            };
            var trsToLocalToWorldJobHandle = trsToLocalToWorldJob.ScheduleParallel(m_Query, 1, inputDeps);
            return trsToLocalToWorldJobHandle;
        }
    }
}
