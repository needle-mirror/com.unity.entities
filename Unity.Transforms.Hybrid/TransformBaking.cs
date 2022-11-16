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
        PostTransformScale   = 4,
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
        public ComponentTypeHandle<WorldTransform>      WorldTransform;
        public ComponentTypeHandle<LocalTransform>      LocalTransform;
        public ComponentTypeHandle<PostTransformScale>  PostTransformScale;
#else
        public ComponentTypeHandle<Translation>        Translation;
        public ComponentTypeHandle<Rotation>           Rotation;
        public ComponentTypeHandle<NonUniformScale>    Scale;
#endif
        public ComponentTypeHandle<Parent>             Parent;

        public EntityQueryMask                         Static;
#if !ENABLE_TRANSFORM_V1
        public EntityQueryMask                         HasTransform;
        public EntityQueryMask                         HasPostTransformScale;
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

            if (HasPostTransformScale.MatchesIgnoreFilter(batchInChunk))
                chunkCategory |= RequestedTransformCategory.PostTransformScale;
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
                        value |= RequestedTransformCategory.PostTransformScale;
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
#else
#endif

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            Assert.IsFalse(useEnabledMask);
            var transforms = chunk.GetNativeArray(ref Transform);
            var entities = chunk.GetNativeArray(Entities);

            //if (batchInChunk.DidOrderChange(ChangeVersion))
            //     Debug.Log($"Order: {batchInChunk.Archetype.ToString()}");
            //if (batchInChunk.DidChange(Transform, ChangeVersion))
            //    Debug.Log($"AuthoringChange: {batchInChunk.Archetype.ToString()}");

            var chunkCategory = GetChunkCategory(chunk);
            var isStatic = Static.MatchesIgnoreFilter(chunk);

            var localToWorlds = chunk.GetNativeArray(ref LocalToWorld);
#if !ENABLE_TRANSFORM_V1
            var WorldTransforms = chunk.GetNativeArray(ref WorldTransform);
            var LocalTransforms = chunk.GetNativeArray(ref LocalTransform);
            var postTransformScales = chunk.GetNativeArray(ref PostTransformScale);
#else
            var translations = chunk.GetNativeArray(ref Translation);
            var rotations = chunk.GetNativeArray(ref Rotation);
            var scales = chunk.GetNativeArray(ref Scale);
#endif
            var parents = chunk.GetNativeArray(ref Parent);

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
                var chunkHasPostTransformScale = HasFlag(chunkCategory, RequestedTransformCategory.PostTransformScale);
                var requestedHasTransform = HasFlag(requestedMode, RequestedTransformCategory.Transform);
                var requestedHasPostTransformScale = HasFlag(requestedMode, RequestedTransformCategory.PostTransformScale);

                // !!! For now we always add a LocalTransform and WorldTransform
                // There is a potential optimization here.
                var chunkHasLocalTransform = chunkHasTransform;
                var chunkHasWorldTransform = chunkHasTransform;

                var requestedHasWorldTransform = requestedHasTransform;
                var requestedHasLocalTransform = requestedHasTransform;

                float localUniformScale = 1.0f;
                var postTransformScale = float3x3.identity;
                if (IsUniformScale(transforms[i].LocalScale))
                {
                    localUniformScale = transforms[i].LocalScale.x;
                }
                else
                {
                    postTransformScale = float3x3.Scale(transforms[i].LocalScale);
                }

                var localTransform = Unity.Transforms.LocalTransform.FromPositionRotationScale(
                    transforms[i].LocalPosition, transforms[i].LocalRotation, localUniformScale);

                if (chunkHasWorldTransform && requestedHasWorldTransform)
                {
                    WorldTransforms[i] = (WorldTransform)localTransform;
                }
                else if (requestedHasWorldTransform)
                {
                    Commands.AddComponent(unfilteredChunkIndex, entity, (WorldTransform)localTransform);
                }
                else if (chunkHasWorldTransform)
                {
                    Commands.RemoveComponent<WorldTransform>(unfilteredChunkIndex, entity);
                }

                if (chunkHasLocalTransform && requestedHasLocalTransform)
                {
                    LocalTransforms[i] = localTransform;
                }
                else if (requestedHasLocalTransform)
                {
                    Commands.AddComponent(unfilteredChunkIndex, entity, localTransform);
                }
                else if (chunkHasLocalTransform)
                {
                    Commands.RemoveComponent<LocalTransform>(unfilteredChunkIndex, entity);
                }

                if (chunkHasPostTransformScale && requestedHasPostTransformScale)
                {
                    postTransformScales[i] = new PostTransformScale { Value = postTransformScale };
                }
                else if (requestedHasPostTransformScale)
                {
                    var componentTypes = new ComponentTypeSet(ComponentType.ReadWrite<PostTransformScale>(),
                        ComponentType.ReadWrite<PropagateLocalToWorld>());

                    Commands.AddComponent(unfilteredChunkIndex, entity, componentTypes);
                    Commands.SetComponent(unfilteredChunkIndex, entity, new PostTransformScale { Value = postTransformScale });
                }
                else if (chunkHasPostTransformScale)
                {
                    Commands.RemoveComponent<PostTransformScale>(unfilteredChunkIndex, entity);
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
            All = new []{ComponentType.ReadOnly<WorldTransform>()},
            Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
        }).GetEntityQueryMask();

        _Job.HasPostTransformScale = GetEntityQuery(new EntityQueryDesc
        {
            All = new []{ComponentType.ReadOnly<PostTransformScale>()},
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
        _Job.WorldTransform = GetComponentTypeHandle<WorldTransform>();
        _Job.LocalTransform = GetComponentTypeHandle<LocalTransform>();
        _Job.PostTransformScale = GetComponentTypeHandle<PostTransformScale>();
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
