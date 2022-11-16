using System;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Conversion;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine.Jobs;

namespace Unity.Entities
{
    internal struct TransformAuthoringBaking
    {
        EntityManager               _EntityManager;
        EntityQuery                 _StaticQuery;
        EntityQueryMask             _StaticQueryMask;
        EntityQuery                 _AdditionalEntityParentQuery;
        internal JobHandle          _JobHandle;
        NativeParallelHashMap<int, bool>    _LocalToWorldIndices;
        IncrementalHierarchy        _SceneHierarchy;

        public TransformAuthoringBaking(EntityManager entityManager)
        {
            _EntityManager = entityManager;
            _AdditionalEntityParentQuery = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(AdditionalEntityParent), typeof(TransformAuthoring)},
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab
            });
            _StaticQuery = entityManager.CreateEntityQuery(typeof(Static));
            Assert.IsFalse(_StaticQuery.HasFilter(), "The use of EntityQueryMask in this job will not respect the query's active filter settings.");
            _StaticQueryMask = _StaticQuery.GetEntityQueryMask();
            _LocalToWorldIndices = new NativeParallelHashMap<int, bool>(1024, Allocator.Persistent);
            _JobHandle = default;
            _SceneHierarchy = default;
        }

        public void Dispose()
        {
            _AdditionalEntityParentQuery.Dispose();
            _StaticQuery.Dispose();
            _LocalToWorldIndices.Dispose();
        }

        // Perform any work that doesn't rely on all the entities being created
        public JobHandle Prepare(IncrementalHierarchy hierarchy, NativeList<int> changedTransforms)
        {
            _JobHandle.Complete();

            _SceneHierarchy = hierarchy;
            _LocalToWorldIndices.Clear();

            var sceneHierarchy = hierarchy.AsReadOnly();
            _JobHandle = sceneHierarchy.CollectHierarchyInstanceIdsAndIndicesAsync(changedTransforms, _LocalToWorldIndices);

            var bakeTransformAuthoring = new BakeToTransformAuthoringListJob
            {
                Hierarchy = hierarchy.AsReadOnly(),
                ChangedIndices = _LocalToWorldIndices,
                ChangeVersion = _EntityManager.GlobalSystemVersion,
                TransformAuthorings = _SceneHierarchy.TransformAuthorings.AsArray()
            };
            _JobHandle = bakeTransformAuthoring.ScheduleReadOnly(_SceneHierarchy.TransformArray, 64, _JobHandle);
            return _JobHandle;
        }

        // Perform any work that relies on all entities having been constructed
        public void UpdateTransforms(UnsafeParallelHashMap<int, Entity> gameObjectToEntity, UnsafeParallelHashMap<Entity, TransformUsageFlagCounters> transformUsages, bool hasTransformUsageChanged)
        {
            using var marker = new ProfilerMarker("TransformHierarchyBaking.BakeToTransformAuthoring").Auto();

            var job = new BakeToTransformAuthoringComponentJob()
            {
                Hierarchy = _SceneHierarchy.AsReadOnly(),
                ChangedIndices = _LocalToWorldIndices,
                TransformAuthoring = _EntityManager.GetComponentLookup<TransformAuthoring>(),
                GameObjectToEntity = gameObjectToEntity,
                TransformAuthorings = _SceneHierarchy.TransformAuthorings.AsArray(),
                TransformUsages = transformUsages,
                HasTransformUsageChanged = hasTransformUsageChanged,
                ChangeVersion = _EntityManager.GlobalSystemVersion
            };

            _JobHandle = job.ScheduleParallel(job.TransformAuthorings.Length, 128, _JobHandle);

            var cmd = new EntityCommandBuffer(Allocator.TempJob);
            var additionalEntityJob = new BakeAdditionalEntityTransformAuthoringJob
            {
                TransformAuthoringLookup = _EntityManager.GetComponentLookup<TransformAuthoring>(true),
                TransformAuthoringHandle = _EntityManager.GetComponentTypeHandle<TransformAuthoring>(false),
                HasStatic = _StaticQueryMask,
                AdditionalEntityParent = _EntityManager.GetComponentTypeHandle<AdditionalEntityParent>(false),
                Entities = _EntityManager.GetEntityTypeHandle(),
                Commands = cmd.AsParallelWriter(),
                Hierarchy = _SceneHierarchy.AsReadOnly(),
                GameObjectToEntity = gameObjectToEntity,
                TransformUsages = transformUsages,
                ChangeVersion = _EntityManager.GlobalSystemVersion
            };
            _JobHandle = additionalEntityJob.ScheduleParallelByRef(_AdditionalEntityParentQuery, _JobHandle);
            _JobHandle.Complete();

            cmd.Playback(_EntityManager);
            cmd.Dispose();
        }

        static void CalculateGlobalTransformUsage(ref SceneHierarchy sceneHierarchy, ref UnsafeParallelHashMap<int, Entity> gameObjectToEntity, ref UnsafeParallelHashMap<Entity, TransformUsageFlagCounters> transformUsages, Entity entity, int parentIndex, out Entity outParent, out TransformUsageFlags outUsage)
        {
            transformUsages.TryGetValue(entity, out var entityTransformUsage);
            var computedUsage = entityTransformUsage.Flags;

            // If no one needs the transform on this entity, then there is no reason to check out the parents
            if (computedUsage == TransformUsageFlags.None)
            {
                outParent = Entity.Null;
                outUsage = computedUsage;
                return;
            }

            // If we are writing the global space transform always, then there is no reason to setup a parent transform
            if ((computedUsage & (TransformUsageFlags.WriteGlobalTransform)) != 0)
            {
                outParent = Entity.Null;
                outUsage = computedUsage;
                return;
            }

            // Calculate if this entity is movable.
            // An entity is movable if itself or any parent is movable.
            bool isSelfMovable = (computedUsage & TransformUsageFlags.WriteFlags) != 0;
            bool isMovable = isSelfMovable;
            if (!isMovable)
            {
                while (parentIndex != -1)
                {
                    if (!gameObjectToEntity.TryGetValue(sceneHierarchy.GetInstanceIdForIndex(parentIndex), out var parentEntity))
                        Debug.LogError("InternalError");
                    transformUsages.TryGetValue(parentEntity, out var parentTransformUsage);

                    bool isParentDynamic = (parentTransformUsage.Flags & TransformUsageFlags.WriteFlags) != 0;
                    if (isParentDynamic)
                    {
                        isMovable = true;
                        break;
                    }

                    parentIndex = sceneHierarchy.GetParentForIndex(parentIndex);
                }
            }

            // For movable objects, Find the closest parent that has a transform
            if (isMovable)
            {
                // Keep usage since the object itself is movable
                if (isSelfMovable)
                    outUsage = computedUsage;
                // Add Default usage since the object itself is not movable but the parent makes it movable
                else
                    outUsage = computedUsage | TransformUsageFlags.Default;

                while (parentIndex != -1)
                {
                    if (!gameObjectToEntity.TryGetValue(sceneHierarchy.GetInstanceIdForIndex(parentIndex), out var parentEntity))
                        Debug.LogError("InternalError");
                    transformUsages.TryGetValue(parentEntity, out var parentTransformUsage);

                    if ((parentTransformUsage.Flags & TransformUsageFlags.WriteFlags) != 0 && parentTransformUsage.Flags != TransformUsageFlags.ManualOverride)
                    {
                        outParent = parentEntity;
                        return;
                    }

                    parentIndex = sceneHierarchy.GetParentForIndex(parentIndex);
                }

                outParent = Entity.Null;
                return;
            }
            // For static objects, use no parents and just return the usage of the entity self
            else
            {
                outParent = Entity.Null;
                outUsage = computedUsage;
                return;
            }
        }

        // TODO: DOTS-5468
        [BurstCompile]
        unsafe struct BakeAdditionalEntityTransformAuthoringJob : IJobChunk
        {
            [NativeDisableContainerSafetyRestriction]
            [ReadOnly]
            public ComponentLookup<TransformAuthoring>   TransformAuthoringLookup;
            public EntityQueryMask                               HasStatic;

            public EntityTypeHandle                              Entities;
            public ComponentTypeHandle<TransformAuthoring>       TransformAuthoringHandle;
            public ComponentTypeHandle<AdditionalEntityParent>   AdditionalEntityParent;

            public EntityCommandBuffer.ParallelWriter            Commands;

            [ReadOnly]
            public UnsafeParallelHashMap<Entity, TransformUsageFlagCounters> TransformUsages;
            [ReadOnly]
            public SceneHierarchy                                    Hierarchy;
            [ReadOnly]
            public UnsafeParallelHashMap<int, Entity>                        GameObjectToEntity;
            public uint                                              ChangeVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);
                var count = chunk.Count;

                // By default only get read access to the chunk. Most of the time we don't want to dirty these chunks. Only if something actually changed
                var transformsRO = (TransformAuthoring*)chunk.GetComponentDataPtrRO(ref TransformAuthoringHandle);

                TransformAuthoring* transformsRW = null;

                var parents = chunk.GetNativeArray(ref AdditionalEntityParent);
                var entities = chunk.GetNativeArray(Entities);

                // Replicate static flag onto additional entities from primary entity
                var isStatic = HasStatic.MatchesIgnoreFilter(chunk);
                for (int i = 0; i != count; i++)
                {
                    var isParentStatic = HasStatic.MatchesIgnoreFilter(parents[i].Parent);

                    if (isParentStatic != isStatic)
                    {
                        if (isParentStatic)
                            Commands.AddComponent(unfilteredChunkIndex, entities[i], ComponentType.ReadWrite<Static>());
                        else
                            Commands.RemoveComponent(unfilteredChunkIndex, entities[i], ComponentType.ReadWrite<Static>());
                    }
                }

                // Configure TransformAuthoring onto additional entities by treating primary entities as a parent
                // and the additional entity as a transform with identity local transform relative to parent
                for (int i = 0; i != count; i++)
                {
                    var parentTransform = TransformAuthoringLookup[parents[i].Parent];

                    var parentIndex = Hierarchy.GetIndexForInstanceId(parents[i].ParentInstanceID);
                    CalculateGlobalTransformUsage(ref Hierarchy, ref GameObjectToEntity, ref TransformUsages, entities[i], parentIndex, out var runtimeParent, out var runtimeTransformUsage);


                    if (parentTransform.ChangeVersion != transformsRO[i].ChangeVersion || transformsRO[i].RuntimeParent != runtimeParent || transformsRO[i].RuntimeTransformUsage != runtimeTransformUsage)
                    {
                        TransformAuthoring transform;
                        transform.AuthoringParent = parents[i].Parent;
                        transform.Position = parentTransform.Position;
                        transform.Rotation = parentTransform.Rotation;

                        transform.LocalPosition = new float3(0);
                        transform.LocalRotation = quaternion.identity;
                        transform.LocalScale = new float3(1);
                        transform.LocalToWorld = parentTransform.LocalToWorld;
                        transform.ChangeVersion = ChangeVersion;
                        transform.RuntimeParent = runtimeParent;
                        transform.RuntimeTransformUsage = runtimeTransformUsage;

                        if (transformsRW == null)
                            transformsRW = (TransformAuthoring*)chunk.GetComponentDataPtrRW(ref TransformAuthoringHandle);
                        transformsRW[i] = transform;
                    }
                }
            }
        }

        [BurstCompile]
        struct BakeToTransformAuthoringListJob : IJobParallelForTransform
        {
            [ReadOnly] public SceneHierarchy           Hierarchy;
            public NativeArray<TransformAuthoring>     TransformAuthorings;

            [ReadOnly] public NativeParallelHashMap<int, bool> ChangedIndices;
            public  uint                               ChangeVersion;

            public void Execute(int index, TransformAccess transform)
            {
                if (!ChangedIndices.TryGetValue(index, out var selfChanged))
                     return;

                var parentIndex = Hierarchy.GetParentForIndex(index);
                var parentInstanceID = 0;
                if (parentIndex != -1)
                    parentInstanceID = Hierarchy.GetInstanceIdForIndex(parentIndex);

                TransformAuthoring value;
                value.Position = transform.position;
                value.Rotation = transform.rotation;

                value.LocalPosition = transform.localPosition;
                value.LocalRotation = transform.localRotation;
                value.LocalScale = transform.localScale;

                value.LocalToWorld = transform.localToWorldMatrix;
                value.RuntimeTransformUsage = default;
                value.RuntimeParent = default;
                // This is a bit of a hack, in the flattened NativeList
                value.AuthoringParent.Index = parentInstanceID;
                value.AuthoringParent.Version = 0;
                value.ChangeVersion = ChangeVersion;

                TransformAuthorings[index] = value;
            }
        }


        [BurstCompile]
        struct BakeToTransformAuthoringComponentJob : IJobFor
        {
            [ReadOnly] public SceneHierarchy Hierarchy;
            [ReadOnly] public NativeParallelHashMap<int, bool> ChangedIndices;
            [ReadOnly] public NativeArray<TransformAuthoring> TransformAuthorings;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<TransformAuthoring> TransformAuthoring;

            [ReadOnly]
            public UnsafeParallelHashMap<int, Entity> GameObjectToEntity;
            [ReadOnly]
            public UnsafeParallelHashMap<Entity, TransformUsageFlagCounters> TransformUsages;
            public uint                                              ChangeVersion;
            public bool                                              HasTransformUsageChanged;

            public void Execute(int index)
            {
                var hasPossibleChange = HasTransformUsageChanged || ChangedIndices.TryGetValue(index, out var selfChanged);
                if (!hasPossibleChange)
                    return;

                // instanceID and primary entity we want to apply this to
                int instanceID = Hierarchy.GetInstanceIdForIndex(index);
                if (!GameObjectToEntity.TryGetValue(instanceID, out var entity))
                    return;

                // Need to transform the parent from instanceID to Entity
                TransformAuthoring value = TransformAuthorings[index];
                var parentInstanceID = value.AuthoringParent.Index;
                GameObjectToEntity.TryGetValue(parentInstanceID, out value.AuthoringParent);

                // Calculate hierarchical transform usage
                var parentIndex = Hierarchy.GetIndexForInstanceId(parentInstanceID);
                CalculateGlobalTransformUsage(ref Hierarchy, ref GameObjectToEntity, ref TransformUsages, entity, parentIndex, out value.RuntimeParent, out value.RuntimeTransformUsage);

                // Apply it only if it changed, so that we don't dirty change filtering
                var oldValue = TransformAuthoring[entity];
                if (!oldValue.Equals(value))
                {
                    value.ChangeVersion = ChangeVersion;
                    TransformAuthoring[entity] = value;
                }
            }
        }
    }
}
