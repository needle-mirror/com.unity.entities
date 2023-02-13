using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Assertions;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;

#if !ENABLE_TRANSFORM_V1

namespace Unity.Transforms
{
    /// <summary>
    /// Entities with this tag will propagate their <see cref="LocalToWorld"/> matrix to their descendants in a transform hierarchy,
    /// instead of their <see cref="WorldTransform"/>.
    /// </summary>
    /// <remarks>
    /// Propagating the full <see cref="LocalToWorld"/> is less efficient, but is necessary in certain cases where an entity has transform
    /// data that is not reflected in <see cref="WorldTransform"/>. For example:
    /// - Entities with the <see cref="PostTransformScale"/> component, since this matrix generally contains transform data which can not
    ///   be represented by <see cref="WorldTransform"/> (such as non-uniform scale)
    /// - Entity whose <see cref="LocalToWorld"/> is written outside of the transform system, generally using <see cref="WriteGroupAttribute"/>.
    ///   A common example is entities which use the interpolation or extrapolation features provided by Unity.Physics or Unity.Netcode.
    /// The presence of this component opts back into a slower transform path which ensures that an entity's descendants will use the correct
    /// data to compute their own <see cref="LocalToWorld"/> matrices.
    ///
    /// This component can be safely omitted on entities that are not parents.
    ///
    /// This component does not stop the transform system from writing to the <see cref="LocalToWorld"/> component. If these are the desired
    /// semantics, the <see cref="WriteGroupAttribute"/> must be used in tandem with this component.
    /// </remarks>
    public struct PropagateLocalToWorld : IComponentData
    {
    }

    /// <summary>
    /// This system computes a <see cref="LocalToWorld"/> matrix for each entity
    /// </summary>
    /// <remarks>
    /// Entity transformation hierarchies are created using the <see cref="Parent"/> and <see cref="LocalTransform"/>
    /// components, and maintained by the <see cref="ParentSystem"/>.
    ///
    /// For root-level / world-space entities with no <see cref="Parent"/>, the <see cref="LocalToWorld"/> can be
    /// computed directly from the entity's <see cref="LocalTransform"/>. <see cref="WorldTransform"/> is written as a
    /// side effect.
    ///
    /// For child entities, each unique hierarchy is traversed recursively, computing each child's <see cref="LocalToWorld"/>
    /// by composing its <see cref="LocalTransform"/> with its parent's world-space transform. <see cref="WorldTransform"/>
    /// and <see cref="ParentTransform"/> are written as a side effect.
    /// </remarks>
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(ParentSystem))]
    public partial struct LocalToWorldSystem : ISystem
    {
        // Compute the WorldTransform and LocalToWorld of all root-level entities
        [BurstCompile]
        unsafe struct ComputeRootLocalToWorldJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<LocalTransform> LocalTransformTypeHandleRO;
            [ReadOnly] public ComponentTypeHandle<PostTransformScale> PostTransformScaleTypeHandleRO;
            public ComponentTypeHandle<WorldTransform> WorldTransformTypeHandleRW;
            public ComponentTypeHandle<LocalToWorld> LocalToWorldTypeHandleRW;
            public uint LastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);

                LocalTransform* chunkLocalTransforms = (LocalTransform*)chunk.GetRequiredComponentDataPtrRO(ref LocalTransformTypeHandleRO);
                if (Hint.Unlikely(chunk.Has(ref PostTransformScaleTypeHandleRO)))
                {
                    if (chunk.DidChange(ref LocalTransformTypeHandleRO, LastSystemVersion) ||
                        chunk.DidChange(ref PostTransformScaleTypeHandleRO, LastSystemVersion))
                    {
                        WorldTransform* chunkWorldTransforms = (WorldTransform*)chunk.GetRequiredComponentDataPtrRW(ref WorldTransformTypeHandleRW);
                        LocalToWorld* chunkLocalToWorlds = (LocalToWorld*)chunk.GetRequiredComponentDataPtrRW(ref LocalToWorldTypeHandleRW);
                        PostTransformScale* chunkPostTransformScales =
                            (PostTransformScale*)chunk.GetRequiredComponentDataPtrRO(
                                ref PostTransformScaleTypeHandleRO);
                        for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; ++i)
                        {
                            chunkWorldTransforms[i] = (WorldTransform)chunkLocalTransforms[i];
                            chunkLocalToWorlds[i].Value = math.mul(chunkLocalTransforms[i].ToMatrix(),
                                new float4x4(chunkPostTransformScales[i].Value, float3.zero));
                        }
                    }
                }
                else
                {
                    if (chunk.DidChange(ref LocalTransformTypeHandleRO, LastSystemVersion))
                    {
                        WorldTransform* chunkWorldTransforms = (WorldTransform*)chunk.GetRequiredComponentDataPtrRW(ref WorldTransformTypeHandleRW);
                        LocalToWorld* chunkLocalToWorlds = (LocalToWorld*)chunk.GetRequiredComponentDataPtrRW(ref LocalToWorldTypeHandleRW);
                        for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; ++i)
                        {
                            chunkWorldTransforms[i] = (WorldTransform)chunkLocalTransforms[i];
                            chunkLocalToWorlds[i].Value = chunkLocalTransforms[i].ToMatrix();
                        }
                    }
                }
            }
        }

        [BurstCompile]
        unsafe struct ComputeChildLocalToWorldJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<Child> ChildTypeHandleRO;
            [ReadOnly] public BufferLookup<Child> ChildLookupRO;
            [ReadOnly] public ComponentTypeHandle<PropagateLocalToWorld> PropagateLocalToWorldRO;
            public ComponentTypeHandle<WorldTransform> WorldTransformTypeHandleRW;
            public ComponentTypeHandle<LocalToWorld> LocalToWorldTypeHandleRW;

            [ReadOnly] public ComponentLookup<LocalTransform> LocalTransformLookupRO;
            [ReadOnly] public ComponentLookup<PropagateLocalToWorld> PropagateLocalToWorldLookupRO;
            [ReadOnly] public ComponentLookup<PostTransformScale> PostTransformScaleLookupRO;

            [NativeDisableContainerSafetyRestriction] public ComponentLookup<WorldTransform> WorldTransformLookupRW;
            [NativeDisableContainerSafetyRestriction] public ComponentLookup<ParentTransform> ParentTransformLookupRW;
            [NativeDisableContainerSafetyRestriction] public ComponentLookup<LocalToWorld> LocalToWorldLookupRW;
            public uint LastSystemVersion;

            void ChildLocalToWorldFromTransformData(in WorldTransform parentWorldTransformData, Entity childEntity, bool updateChildrenTransform)
            {
                updateChildrenTransform = updateChildrenTransform
                                          || PostTransformScaleLookupRO.DidChange(childEntity, LastSystemVersion)
                                          || LocalTransformLookupRO.DidChange(childEntity, LastSystemVersion);

                WorldTransform worldTransformData;
                float4x4 localToWorld;

                if (updateChildrenTransform)
                {
                    ParentTransformLookupRW[childEntity] = (ParentTransform)parentWorldTransformData;
                    var localTransform = LocalTransformLookupRO[childEntity];
                    worldTransformData = parentWorldTransformData.TransformTransform((WorldTransform)localTransform);
                    WorldTransformLookupRW[childEntity] = worldTransformData;
                    // Compute childEntity's LocalToWorld
                    localToWorld = worldTransformData.ToMatrix();
                    if (PostTransformScaleLookupRO.HasComponent(childEntity))
                        localToWorld = math.mul(localToWorld, new float4x4(PostTransformScaleLookupRO[childEntity].Value, float3.zero));
                    LocalToWorldLookupRW[childEntity] = new LocalToWorld{Value = localToWorld};
                }
                else
                {
                    worldTransformData = WorldTransformLookupRW[childEntity];
                    localToWorld = LocalToWorldLookupRW[childEntity].Value;
                    updateChildrenTransform = WorldTransformLookupRW.DidChange(childEntity, LastSystemVersion);
                }

                if (ChildLookupRO.TryGetBuffer(childEntity, out DynamicBuffer<Child> children))
                {
                    // If this component is present, the entity's descendants should use the parent's LocalToWorld
                    // rather than its WorldTransform to compute their own world-space transform.
                    if (Hint.Unlikely((PropagateLocalToWorldLookupRO.HasComponent(childEntity))))
                    {
                        for (int i = 0, childCount = children.Length; i < childCount; i++)
                        {
                            ChildLocalToWorldFromTransformMatrix(worldTransformData, localToWorld, children[i].Value, updateChildrenTransform);
                        }
                    }
                    else
                    {
                        for (int i = 0, childCount = children.Length; i < childCount; i++)
                        {
                            ChildLocalToWorldFromTransformData(worldTransformData, children[i].Value, updateChildrenTransform);
                        }
                    }

                }
            }

            void ChildLocalToWorldFromTransformMatrix(in WorldTransform parentWorldTransformData, in float4x4 parentLocalToWorld, Entity childEntity, bool updateChildrenTransform)
            {
                updateChildrenTransform = updateChildrenTransform
                                          || PostTransformScaleLookupRO.DidChange(childEntity, LastSystemVersion)
                                          || LocalTransformLookupRO.DidChange(childEntity, LastSystemVersion);

                WorldTransform worldTransformData;
                float4x4 localToWorld;

                if (updateChildrenTransform)
                {
                    ParentTransformLookupRW[childEntity] = (ParentTransform)parentWorldTransformData;
                    var localTransform = LocalTransformLookupRO[childEntity];
                    worldTransformData = parentWorldTransformData.TransformTransform((WorldTransform)localTransform);
                    WorldTransformLookupRW[childEntity] = worldTransformData;
                    // Compute childEntity's LocalToWorld
                    localToWorld = math.mul(parentLocalToWorld, localTransform.ToMatrix());
                    if (PostTransformScaleLookupRO.HasComponent(childEntity))
                        localToWorld = math.mul(localToWorld, new float4x4(PostTransformScaleLookupRO[childEntity].Value, float3.zero));
                    LocalToWorldLookupRW[childEntity] = new LocalToWorld{Value = localToWorld};
                }
                else
                {
                    worldTransformData = WorldTransformLookupRW[childEntity];
                    localToWorld = LocalToWorldLookupRW[childEntity].Value;
                    updateChildrenTransform = LocalToWorldLookupRW.DidChange(childEntity, LastSystemVersion);
                }

                if (ChildLookupRO.TryGetBuffer(childEntity, out DynamicBuffer<Child> children))
                {
                    for (int i = 0, childCount = children.Length; i < childCount; i++)
                    {
                        ChildLocalToWorldFromTransformMatrix(worldTransformData, localToWorld, children[i].Value, updateChildrenTransform);
                    }
                }
            }

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);

                bool updateChildrenTransform =
                    chunk.DidChange(ref WorldTransformTypeHandleRW, LastSystemVersion) ||
                    chunk.DidChange(ref ChildTypeHandleRO, LastSystemVersion);
                WorldTransform* chunkWorldTransforms = (WorldTransform*)chunk.GetRequiredComponentDataPtrRO(ref WorldTransformTypeHandleRW);
                BufferAccessor<Child> chunkChildBuffers = chunk.GetBufferAccessor(ref ChildTypeHandleRO);
                // If this component is present, the entity's descendants should use the parent's LocalToWorld
                // rather than its WorldTransform to compute their own world-space transform.
                if (Hint.Unlikely(chunk.Has(ref PropagateLocalToWorldRO)))
                {
                    updateChildrenTransform = updateChildrenTransform ||
                                              chunk.DidChange(ref LocalToWorldTypeHandleRW, LastSystemVersion);
                    LocalToWorld* chunkLocalToWorlds = (LocalToWorld*)chunk.GetRequiredComponentDataPtrRO(ref LocalToWorldTypeHandleRW);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        var localToWorld = chunkLocalToWorlds[i].Value;
                        var worldTransform = chunkWorldTransforms[i];
                        var children = chunkChildBuffers[i];
                        for (int j = 0, childCount = children.Length; j < childCount; j++)
                        {
                            ChildLocalToWorldFromTransformMatrix(worldTransform, localToWorld, children[j].Value, updateChildrenTransform);
                        }
                    }
                }
                else
                {
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        var worldTransform = chunkWorldTransforms[i];
                        var children = chunkChildBuffers[i];
                        for (int j = 0, childCount = children.Length; j < childCount; j++)
                        {
                            ChildLocalToWorldFromTransformData(worldTransform, children[j].Value, updateChildrenTransform);
                        }
                    }
                }
            }
        }

        EntityQuery _rootsQuery;
        EntityQuery _parentsQuery;
        EntityQuery _parentTransformWithoutParentQuery;
        EntityQuery _parentWithoutParentTransformQuery;
        EntityQuery _entityWithoutWorldTransformQuery;

        ComponentTypeHandle<LocalTransform> _localTransformTypeHandleRO;
        ComponentTypeHandle<PostTransformScale> _postTransformScaleTypeHandleRO;
        ComponentTypeHandle<PropagateLocalToWorld> _propagateLocalToWorldHandleRO;
        ComponentTypeHandle<LocalTransform> _localTransformTypeHandleRW;
        ComponentTypeHandle<WorldTransform> _worldTransformTypeHandleRW;
        ComponentTypeHandle<LocalToWorld> _localToWorldTypeHandleRW;

        BufferTypeHandle<Child> _childTypeHandleRO;
        BufferLookup<Child> _childLookupRO;

        ComponentLookup<LocalTransform> _localTransformLookupRO;
        ComponentLookup<PropagateLocalToWorld> _propagateLocalToWorldLookupRO;
        ComponentLookup<PostTransformScale> _postTransformScaleLookupRO;
        ComponentLookup<WorldTransform> _worldTransformLookupRW;
        ComponentLookup<ParentTransform> _parentTransformLookupRW;
        ComponentLookup<LocalToWorld> _localToWorldLookupRW;

        /// <inheritdoc cref="ISystem.OnCreate"/>
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LocalTransform>()
                .WithAllRW<WorldTransform, LocalToWorld>()
                .WithNone<Parent>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup);
            _rootsQuery = state.GetEntityQuery(builder);

            builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LocalTransform, Child>()
                .WithAllRW<WorldTransform, LocalToWorld>()
                .WithNone<Parent>();
            _parentsQuery = state.GetEntityQuery(builder);

            builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<ParentTransform>()
                .WithNone<Parent>();
            _parentTransformWithoutParentQuery = state.GetEntityQuery(builder);

            builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Parent>()
                .WithNone<ParentTransform>();
            _parentWithoutParentTransformQuery = state.GetEntityQuery(builder);

            builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LocalTransform>()
                .WithNone<WorldTransform>();
            _entityWithoutWorldTransformQuery = state.GetEntityQuery(builder);

            _localTransformTypeHandleRO = state.GetComponentTypeHandle<LocalTransform>(true);
            _postTransformScaleTypeHandleRO = state.GetComponentTypeHandle<PostTransformScale>(true);
            _propagateLocalToWorldHandleRO = state.GetComponentTypeHandle<PropagateLocalToWorld>(true);
            _localTransformTypeHandleRW = state.GetComponentTypeHandle<LocalTransform>(false);
            _worldTransformTypeHandleRW = state.GetComponentTypeHandle<WorldTransform>(false);
            _localToWorldTypeHandleRW = state.GetComponentTypeHandle<LocalToWorld>(false);

            _childTypeHandleRO = state.GetBufferTypeHandle<Child>(true);
            _childLookupRO = state.GetBufferLookup<Child>(true);

            _localTransformLookupRO = state.GetComponentLookup<LocalTransform>(true);
            _propagateLocalToWorldLookupRO = state.GetComponentLookup<PropagateLocalToWorld>(true);
            _postTransformScaleLookupRO = state.GetComponentLookup<PostTransformScale>(true);
            _worldTransformLookupRW = state.GetComponentLookup<WorldTransform>(false);
            _parentTransformLookupRW = state.GetComponentLookup<ParentTransform>(false);
            _localToWorldLookupRW = state.GetComponentLookup<LocalToWorld>(false);
        }

        /// <inheritdoc cref="ISystem.OnDestroy"/>
        /// <inheritdoc cref="ISystem.OnUpdate"/>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!_entityWithoutWorldTransformQuery.IsEmptyIgnoreFilter)
                state.EntityManager.AddComponent(_entityWithoutWorldTransformQuery, ComponentType.ReadWrite<WorldTransform>());
            if (!_parentWithoutParentTransformQuery.IsEmptyIgnoreFilter)
                state.EntityManager.AddComponent(_parentWithoutParentTransformQuery, ComponentType.ReadWrite<ParentTransform>());
            if (!_parentTransformWithoutParentQuery.IsEmptyIgnoreFilter)
                state.EntityManager.RemoveComponent(_parentTransformWithoutParentQuery, ComponentType.ReadWrite<ParentTransform>());

            _localTransformTypeHandleRO.Update(ref state);
            _postTransformScaleTypeHandleRO.Update(ref state);
            _propagateLocalToWorldHandleRO.Update(ref state);
            _localTransformTypeHandleRW.Update(ref state);
            _worldTransformTypeHandleRW.Update(ref state);
            _localToWorldTypeHandleRW.Update(ref state);

            _childTypeHandleRO.Update(ref state);
            _childLookupRO.Update(ref state);

            _localTransformLookupRO.Update(ref state);
            _propagateLocalToWorldLookupRO.Update(ref state);
            _postTransformScaleLookupRO.Update(ref state);
            _worldTransformLookupRW.Update(ref state);
            _parentTransformLookupRW.Update(ref state);
            _localToWorldLookupRW.Update(ref state);

            // Compute WorldTransform and LocalToWorld for all root-level entities
            var rootJob = new ComputeRootLocalToWorldJob
            {
                LocalTransformTypeHandleRO = _localTransformTypeHandleRO,
                PostTransformScaleTypeHandleRO = _postTransformScaleTypeHandleRO,
                WorldTransformTypeHandleRW = _worldTransformTypeHandleRW,
                LocalToWorldTypeHandleRW = _localToWorldTypeHandleRW,
                LastSystemVersion = state.LastSystemVersion,
            };
            state.Dependency = rootJob.ScheduleParallelByRef(_rootsQuery, state.Dependency);

            // Compute WorldTransform, ParentTransform, and LocalToWorld for all child entities
            var childJob = new ComputeChildLocalToWorldJob
            {
                ChildTypeHandleRO = _childTypeHandleRO,
                ChildLookupRO = _childLookupRO,
                PropagateLocalToWorldRO = _propagateLocalToWorldHandleRO,
                WorldTransformTypeHandleRW = _worldTransformTypeHandleRW,
                LocalToWorldTypeHandleRW = _localToWorldTypeHandleRW,
                LocalTransformLookupRO = _localTransformLookupRO,
                PropagateLocalToWorldLookupRO = _propagateLocalToWorldLookupRO,
                PostTransformScaleLookupRO = _postTransformScaleLookupRO,
                WorldTransformLookupRW = _worldTransformLookupRW,
                ParentTransformLookupRW = _parentTransformLookupRW,
                LocalToWorldLookupRW = _localToWorldLookupRW,
                LastSystemVersion = state.LastSystemVersion,
            };
            state.Dependency = childJob.ScheduleParallelByRef(_parentsQuery, state.Dependency);
        }
    }
}

#endif
