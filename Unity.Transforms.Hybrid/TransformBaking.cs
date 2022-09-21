using System;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;

[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(TransformBakingSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
internal partial class TransformBakingSystem : SystemBase
{
    [Flags]
    public enum RequestedTransformCategory
    {
        None                 = 0,
        LocalToWorld         = 1,
#if !ENABLE_TRANSFORM_V1
        Transform            = 2,
        PostTransformMatrix  = 4,
        Parent               = 8
#else
        TR                   = 2,
        Scale                = 4,
        Parent               = 8
#endif
    }

    EntityQuery               _Query;
    BakedRuntimeTransformsJob _Job;

    ProfilerMarker            _Playback = new ProfilerMarker("EntityCommandBuffer.Playback");

[BurstCompile]
    struct BakedRuntimeTransformsJob : IJobChunk
    {
        [ReadOnly]
        public ComponentTypeHandle<TransformAuthoring> Transform;
        public EntityTypeHandle                        Entities;

        public ComponentTypeHandle<LocalToWorld>       LocalToWorld;
#if !ENABLE_TRANSFORM_V1
        public ComponentTypeHandle<LocalToWorldTransform> LocalToWorldTransform;
        public ComponentTypeHandle<LocalToParentTransform> LocalToParentTransform;
        public ComponentTypeHandle<PostTransformMatrix> PostTransformMatrix;
#else
        public ComponentTypeHandle<Translation>        Translation;
        public ComponentTypeHandle<Rotation>           Rotation;
        public ComponentTypeHandle<NonUniformScale>    Scale;
#endif
        public ComponentTypeHandle<Parent>             Parent;

        public EntityQueryMask                         Static;
#if !ENABLE_TRANSFORM_V1
        public EntityQueryMask                         HasTransform;
        public EntityQueryMask                         HasPostTransformMatrix;
#else
        public EntityQueryMask                         HasTR;
        public EntityQueryMask                         HasScale;
#endif
        public EntityQueryMask                         HasParent;
        public EntityQueryMask                         HasLocalToWorld;

        public EntityCommandBuffer.ParallelWriter      Commands;
        public uint                                    ChangeVersion;

        public static bool HasFlag(RequestedTransformCategory category, RequestedTransformCategory field)
        {
            return (((uint) category & (uint) field) != 0);
        }

        public RequestedTransformCategory GetChunkCategory(ArchetypeChunk batchInChunk)
        {
            RequestedTransformCategory chunkCategory = RequestedTransformCategory.None;
#if !ENABLE_TRANSFORM_V1
            if (HasTransform.MatchesIgnoreFilter(batchInChunk))
                chunkCategory |= RequestedTransformCategory.Transform;

            if (HasPostTransformMatrix.MatchesIgnoreFilter(batchInChunk))
                chunkCategory |= RequestedTransformCategory.PostTransformMatrix;
#else
            if (HasTR.MatchesIgnoreFilter(batchInChunk))
                chunkCategory = RequestedTransformCategory.TR;

            if (HasScale.MatchesIgnoreFilter(batchInChunk))
                chunkCategory |= RequestedTransformCategory.Scale;
#endif

            if (HasParent.MatchesIgnoreFilter(batchInChunk))
                chunkCategory |= RequestedTransformCategory.Parent;

            if (HasLocalToWorld.MatchesIgnoreFilter(batchInChunk))
                chunkCategory |= RequestedTransformCategory.LocalToWorld;

            return chunkCategory;
        }

        public RequestedTransformCategory GetEntityCategory(TransformAuthoring transformAuthoring, bool isStatic)
        {
            RequestedTransformCategory value = RequestedTransformCategory.None;
            if (transformAuthoring.RuntimeTransformUsage != TransformUsageFlags.None && transformAuthoring.RuntimeTransformUsage != TransformUsageFlags.ManualOverride)
                value |= RequestedTransformCategory.LocalToWorld;

            if (!isStatic)
            {
                if ((transformAuthoring.RuntimeTransformUsage & (TransformUsageFlags.Default | TransformUsageFlags.ReadGlobalTransform | TransformUsageFlags.WriteGlobalTransform)) != 0)
                {
#if !ENABLE_TRANSFORM_V1
                    value |= RequestedTransformCategory.Transform;

                    if (!IsUniformScale(transformAuthoring.LocalScale))
                    {
                        value |= RequestedTransformCategory.PostTransformMatrix;
                    }
#else
                    value |= RequestedTransformCategory.TR;

                    float3 one = new float3(1f, 1f, 1f);
                    if (!transformAuthoring.LocalScale.Equals(one))
                    {
                        value |= RequestedTransformCategory.Scale;
                    }
#endif
                }

                if (transformAuthoring.RuntimeParent != Entity.Null)
                {
                    value |= RequestedTransformCategory.Parent;
                }
            }

            return value;
        }

#if !ENABLE_TRANSFORM_V1
        const float k_Tolerance = 0.001f;

        [BurstCompile]
        bool IsUniformScale(float3 scale)
        {
            return math.abs(scale.x - scale.y) <= k_Tolerance &&
                   math.abs(scale.x - scale.z) <= k_Tolerance &&
                   math.abs(scale.y - scale.z) <= k_Tolerance;
        }

        [BurstCompile]
        float GetUniformScale(float3 scale)
        {
            return math.max(scale.x, math.max(scale.y, scale.z));
        }
#else
#endif

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Assert.IsFalse(useEnabledMask);
            var transforms = chunk.GetNativeArray(Transform);
            var entities = chunk.GetNativeArray(Entities);

            //if (batchInChunk.DidOrderChange(ChangeVersion))
            //     Debug.Log($"Order: {batchInChunk.Archetype.ToString()}");
            //if (batchInChunk.DidChange(Transform, ChangeVersion))
            //    Debug.Log($"AuthoringChange: {batchInChunk.Archetype.ToString()}");

            var chunkCategory = GetChunkCategory(chunk);
            var isStatic = Static.MatchesIgnoreFilter(chunk);

            var localToWorlds = chunk.GetNativeArray(LocalToWorld);
#if !ENABLE_TRANSFORM_V1
            var localToWorldTransforms = chunk.GetNativeArray(LocalToWorldTransform);
            var localToParentTransforms = chunk.GetNativeArray(LocalToParentTransform);
            var postTransformMatrices = chunk.GetNativeArray(PostTransformMatrix);
#else
            var translations = chunk.GetNativeArray(Translation);
            var rotations = chunk.GetNativeArray(Rotation);
            var scales = chunk.GetNativeArray(Scale);
#endif
            var parents = chunk.GetNativeArray(Parent);

            for (int i = 0; i != transforms.Length; i++)
            {
                // We check if the component was already there by checking the chunk,
                // then we check the desired components in the entity. Based on that we add, remove or just update
                // the components in the entity

                var requestedMode = GetEntityCategory(transforms[i], isStatic);
                if ((transforms[i].RuntimeTransformUsage & TransformUsageFlags.ManualOverride) != 0)
                    continue;

                var entity = entities[i];
                bool chunkHasFlag, requestedHasFlag;

                // LocalToWorld
                chunkHasFlag = HasFlag(chunkCategory, RequestedTransformCategory.LocalToWorld);
                requestedHasFlag = HasFlag(requestedMode, RequestedTransformCategory.LocalToWorld);
                if (chunkHasFlag && requestedHasFlag)
                {
                    localToWorlds[i] = new LocalToWorld { Value = transforms[i].LocalToWorld };
                }
                else if (requestedHasFlag)
                {
                    Commands.AddComponent(unfilteredChunkIndex, entity, new LocalToWorld { Value = transforms[i].LocalToWorld });

                }
                else if (chunkHasFlag)
                {
                    Commands.RemoveComponent<LocalToWorld>(unfilteredChunkIndex, entity);
                }

#if !ENABLE_TRANSFORM_V1
                // Transform
                var chunkHasTransform = HasFlag(chunkCategory, RequestedTransformCategory.Transform);
                var chunkHasPostTransformMatrix = HasFlag(chunkCategory, RequestedTransformCategory.PostTransformMatrix);
                var chunkHasParent = HasFlag(chunkCategory, RequestedTransformCategory.Parent);
                var requestedHasTransform = HasFlag(requestedMode, RequestedTransformCategory.Transform);
                var requestedHasPostTransformMatrix = HasFlag(requestedMode, RequestedTransformCategory.PostTransformMatrix);
                var requestedHasParent = HasFlag(requestedMode, RequestedTransformCategory.Parent);

                var chunkHasLocalToWorld = chunkHasTransform;
                var chunkHasLocalToParent = chunkHasTransform && chunkHasParent;
                var requestedHasLocalToWorld = requestedHasTransform;
                var requestedHasLocalToParent = requestedHasTransform && requestedHasParent;

                var localUniformScale = GetUniformScale(transforms[i].LocalScale);

                if (chunkHasLocalToWorld && requestedHasLocalToWorld)
                {
                    localToWorldTransforms[i] = new LocalToWorldTransform { Value = UniformScaleTransform.FromPositionRotationScale(transforms[i].LocalPosition, transforms[i].LocalRotation, localUniformScale) };
                }
                else if (requestedHasLocalToWorld)
                {
                    Commands.AddComponent(unfilteredChunkIndex, entity, new LocalToWorldTransform() { Value = UniformScaleTransform.FromPositionRotationScale(transforms[i].LocalPosition, transforms[i].LocalRotation, localUniformScale) });
                }
                else if (chunkHasLocalToWorld)
                {
                    Commands.RemoveComponent<LocalToWorldTransform>(unfilteredChunkIndex, entity);
                }

                if (chunkHasLocalToParent && requestedHasLocalToParent)
                {
                    localToParentTransforms[i] = new LocalToParentTransform { Value = UniformScaleTransform.FromPositionRotationScale(transforms[i].LocalPosition, transforms[i].LocalRotation, localUniformScale) };
                }
                else if (requestedHasLocalToParent)
                {
                    Commands.AddComponent(unfilteredChunkIndex, entity, new LocalToParentTransform() { Value = UniformScaleTransform.FromPositionRotationScale(transforms[i].LocalPosition, transforms[i].LocalRotation, localUniformScale) });
                }
                else if (chunkHasLocalToParent)
                {
                    Commands.RemoveComponent<LocalToParentTransform>(unfilteredChunkIndex, entity);
                }

                if (chunkHasPostTransformMatrix && requestedHasPostTransformMatrix)
                {
                    var localToWorldMatrix = transforms[i].LocalToWorld;
                    var localToWorldTransform = UniformScaleTransform.FromMatrix(localToWorldMatrix);
                    var postTransformMatrix = math.mul(localToWorldTransform.ToInverseMatrix(), localToWorldMatrix);
                    postTransformMatrices[i] = new PostTransformMatrix { Value = postTransformMatrix };
                }
                else if (requestedHasPostTransformMatrix)
                {
                    var localToWorldMatrix = transforms[i].LocalToWorld;
                    var localToWorldTransform = UniformScaleTransform.FromMatrix(localToWorldMatrix);
                    var postTransformMatrix = math.mul(localToWorldTransform.ToInverseMatrix(), localToWorldMatrix);
                    Commands.AddComponent(unfilteredChunkIndex, entity, new PostTransformMatrix { Value = postTransformMatrix });
                }
                else if (chunkHasPostTransformMatrix)
                {
                    Commands.RemoveComponent<PostTransformMatrix>(unfilteredChunkIndex, entity);
                }
#else
                // Translation/Rotation
                chunkHasFlag = HasFlag(chunkCategory, RequestedTransformCategory.TR);
                requestedHasFlag = HasFlag(requestedMode, RequestedTransformCategory.TR);
                if (chunkHasFlag && requestedHasFlag)
                {
                    translations[i] = new Translation() { Value = transforms[i].LocalPosition };
                    rotations[i] = new Rotation() { Value = transforms[i].LocalRotation };
                }
                else if (requestedHasFlag)
                {
                    Commands.AddComponent(unfilteredChunkIndex, entity, new Translation { Value = transforms[i].LocalPosition });
                    Commands.AddComponent(unfilteredChunkIndex, entity, new Rotation { Value = transforms[i].LocalRotation });
                }
                else if (chunkHasFlag)
                {
                    Commands.RemoveComponent<Translation>(unfilteredChunkIndex, entity);
                    Commands.RemoveComponent<Rotation>(unfilteredChunkIndex, entity);
                }
#endif

#if !ENABLE_TRANSFORM_V1
#else
                // Scale
                chunkHasFlag = HasFlag(chunkCategory, RequestedTransformCategory.Scale);
                requestedHasFlag = HasFlag(requestedMode, RequestedTransformCategory.Scale);
                if (chunkHasFlag && requestedHasFlag)
                {
                    scales[i] = new NonUniformScale() { Value = transforms[i].LocalScale };
                }
                else if (requestedHasFlag)
                {
                    Commands.AddComponent(unfilteredChunkIndex, entity, new NonUniformScale { Value = transforms[i].LocalScale });
                }
                else if (chunkHasFlag)
                {
                    Commands.RemoveComponent<NonUniformScale>(unfilteredChunkIndex, entity);
                }
#endif
                // Parent
                chunkHasFlag = HasFlag(chunkCategory, RequestedTransformCategory.Parent);
                requestedHasFlag = HasFlag(requestedMode, RequestedTransformCategory.Parent);
                if (chunkHasFlag && requestedHasFlag)
                {
                    parents[i] = new Parent() { Value = transforms[i].RuntimeParent };
                }
                else if (requestedHasFlag)
                {
                    Commands.AddComponent(unfilteredChunkIndex, entity, new Parent { Value = transforms[i].RuntimeParent });
#if !ENABLE_TRANSFORM_V1
#else
                    Commands.AddComponent(unfilteredChunkIndex, entity, new LocalToParent { Value = float4x4.identity });
#endif
                }
                else if (chunkHasFlag)
                {
#if !ENABLE_TRANSFORM_V1
                    Commands.RemoveComponent<Parent>(unfilteredChunkIndex, entity);
#else
                    var types = new ComponentTypeSet(
                        ComponentType.ReadWrite<Parent>(),
                        ComponentType.ReadWrite<LocalToParent>());
                    Commands.RemoveComponent(unfilteredChunkIndex, entity, types);
#endif
                }
            }
        }
    }
    protected override void OnCreate()
    {
        base.OnCreate();
        _Query = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<TransformAuthoring>() },
            Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
        });

        // The BakedRuntimeTransformsJob only processes chunks with Transform authoring changes as well as structural changes that may have added / removed the Static tag component
        _Query.AddChangedVersionFilter(ComponentType.ReadOnly<TransformAuthoring>());
        _Query.AddOrderVersionFilter();

        _Job.Static = GetEntityQuery(new EntityQueryDesc
        {
            All = new []{ComponentType.ReadOnly<Static>()},
            // Intentionally leaving static prefabs out of this query, so we don't strip the hierarchy or transform components based on this
            Options = EntityQueryOptions.IncludeDisabledEntities
        }).GetEntityQueryMask();

#if !ENABLE_TRANSFORM_V1
        _Job.HasTransform = GetEntityQuery(new EntityQueryDesc
        {
            All = new []{ComponentType.ReadOnly<LocalToWorldTransform>()},
            Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
        }).GetEntityQueryMask();

        _Job.HasPostTransformMatrix = GetEntityQuery(new EntityQueryDesc
        {
            All = new []{ComponentType.ReadOnly<PostTransformMatrix>()},
            Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
        }).GetEntityQueryMask();
#else
        _Job.HasTR = GetEntityQuery(new EntityQueryDesc
        {
            All = new []{ComponentType.ReadOnly<Translation>(), ComponentType.ReadOnly<Rotation>()},
            Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
        }).GetEntityQueryMask();

        _Job.HasScale = GetEntityQuery(new EntityQueryDesc
        {
            All = new []{ComponentType.ReadOnly<NonUniformScale>()},
            Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
        }).GetEntityQueryMask();
#endif

        _Job.HasParent = GetEntityQuery(new EntityQueryDesc
        {
            All = new []{ComponentType.ReadOnly<Parent>()},
            Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
        }).GetEntityQueryMask();

        _Job.HasLocalToWorld = GetEntityQuery(new EntityQueryDesc
        {
            All = new []{ComponentType.ReadOnly<LocalToWorld>()},
            Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
        }).GetEntityQueryMask();
    }

    protected override void OnUpdate()
    {
        var buf = new EntityCommandBuffer(Allocator.TempJob);
        _Job.Transform = GetComponentTypeHandle<TransformAuthoring>(true);
        _Job.Entities = GetEntityTypeHandle();
        _Job.LocalToWorld = GetComponentTypeHandle<LocalToWorld>();
        _Job.Parent = GetComponentTypeHandle<Parent>();
#if !ENABLE_TRANSFORM_V1
        _Job.LocalToWorldTransform = GetComponentTypeHandle<LocalToWorldTransform>();
        _Job.LocalToParentTransform = GetComponentTypeHandle<LocalToParentTransform>();
        _Job.PostTransformMatrix = GetComponentTypeHandle<PostTransformMatrix>();
#else
        _Job.Translation = GetComponentTypeHandle<Translation>();
        _Job.Rotation = GetComponentTypeHandle<Rotation>();
        _Job.Scale = GetComponentTypeHandle<NonUniformScale>();
#endif
        _Job.ChangeVersion = LastSystemVersion;
        _Job.Commands = buf.AsParallelWriter();
        Dependency = _Job.ScheduleParallelByRef(_Query, Dependency);

        CompleteDependency();

        using (_Playback.Auto())
        {
            buf.Playback(EntityManager);
        }

        buf.Dispose();
    }
}
