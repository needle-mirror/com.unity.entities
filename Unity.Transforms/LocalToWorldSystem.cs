using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Assertions;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unity.Transforms
{
    /// <summary>
    /// This system computes a <see cref="LocalToWorld"/> matrix for each entity
    /// </summary>
    /// <remarks>
    /// Entity transformation hierarchies are created using the <see cref="Parent"/> and <see cref="LocalTransform"/>
    /// components, and maintained by the <see cref="ParentSystem"/>.
    ///
    /// For root-level / world-space entities with no <see cref="Parent"/>, the <see cref="LocalToWorld"/> can be
    /// computed directly from the entity's <see cref="LocalTransform"/>.
    ///
    /// For child entities, each unique hierarchy is traversed recursively, computing each child's <see cref="LocalToWorld"/>
    /// by composing its <see cref="LocalTransform"/> with its parent's world-space transform.
    /// </remarks>
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(ParentSystem))]
    public partial struct LocalToWorldSystem : ISystem
    {
        [BurstCompile]
        unsafe struct ComputeWorldSpaceLocalToWorldJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<LocalTransform> LocalTransformTypeHandleRO;
            [ReadOnly] public ComponentTypeHandle<PostTransformMatrix> PostTransformMatrixTypeHandleRO;
            public ComponentTypeHandle<LocalToWorld> LocalToWorldTypeHandleRW;
            public uint LastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);

                LocalTransform* chunkLocalTransforms = (LocalTransform*)chunk.GetRequiredComponentDataPtrRO(ref LocalTransformTypeHandleRO);
                if (chunk.DidChange(ref LocalTransformTypeHandleRO, LastSystemVersion) ||
                    chunk.DidChange(ref PostTransformMatrixTypeHandleRO, LastSystemVersion))
                {
                    LocalToWorld* chunkLocalToWorlds = (LocalToWorld*)chunk.GetRequiredComponentDataPtrRW(ref LocalToWorldTypeHandleRW);
                    PostTransformMatrix* chunkPostTransformMatrices = (PostTransformMatrix*)chunk.GetComponentDataPtrRO(ref PostTransformMatrixTypeHandleRO);
                    if (chunkPostTransformMatrices != null)
                    {
                        for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; ++i)
                        {
                            chunkLocalToWorlds[i].Value = math.mul(chunkLocalTransforms[i].ToMatrix(),
                                chunkPostTransformMatrices[i].Value);
                        }
                    }
                    else
                    {
                        for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; ++i)
                        {
                            chunkLocalToWorlds[i].Value = chunkLocalTransforms[i].ToMatrix();
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct ComputeHierarchyLocalToWorldJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<Entity> RootEntities;
            [ReadOnly] public EntityQueryMask LocalToWorldWriteGroupMask;

            [ReadOnly] public BufferLookup<Child> ChildLookupRO;
            [ReadOnly] public ComponentLookup<LocalTransform> LocalTransformLookupRO;
            [ReadOnly] public ComponentLookup<PostTransformMatrix> PostTransformMatrixLookupRO;
            [NativeDisableContainerSafetyRestriction] public ComponentLookup<LocalToWorld> LocalToWorldLookupRW;
            public uint LastSystemVersion;

            void ChildLocalToWorldFromTransformMatrix(in float4x4 parentLocalToWorld, Entity childEntity, bool updateChildrenTransform)
            {
                updateChildrenTransform = updateChildrenTransform
                                          || PostTransformMatrixLookupRO.DidChange(childEntity, LastSystemVersion)
                                          || LocalTransformLookupRO.DidChange(childEntity, LastSystemVersion);

                float4x4 localToWorld;
                bool hasChildBuffer = ChildLookupRO.TryGetBuffer(childEntity, out DynamicBuffer<Child> children);

                // If false, some other system is responsible for writing this entity's LocalToWorld (e.g. physics interpolation),
                // and this system shouldn't write it.
                bool canWriteLocalToWorld = LocalToWorldWriteGroupMask.MatchesIgnoreFilter(childEntity);
                if (updateChildrenTransform && canWriteLocalToWorld)
                {
                    // this entity (or its ancestors) has a dirty transform, AND this system is responsible for updating it LocalToWorld.
                    var localTransform = LocalTransformLookupRO[childEntity];
                    localToWorld = math.mul(parentLocalToWorld, localTransform.ToMatrix());
                    if (PostTransformMatrixLookupRO.HasComponent(childEntity))
                    {
                        localToWorld = math.mul(localToWorld, PostTransformMatrixLookupRO[childEntity].Value);
                    }
                    LocalToWorldLookupRW[childEntity] = new LocalToWorld{Value = localToWorld};
                    if (hasChildBuffer)
                    {
                        for (int i = 0, childCount = children.Length; i < childCount; i++)
                        {
                            ChildLocalToWorldFromTransformMatrix(localToWorld, children[i].Value, true);
                        }
                    }
                }
                else
                {
                    // either ancestors are not dirty, or we didn't match the write group. We still need to recurse
                    // to any children (if any), which may themselves have dirty local transforms.
                    if (hasChildBuffer)
                    {
                        localToWorld = LocalToWorldLookupRW[childEntity].Value;
                        // If another system may have written this entity's LocalToWorld, we need to treat this node's
                        // transform as dirty while processing its children.
                        if (!canWriteLocalToWorld)
                            updateChildrenTransform = LocalToWorldLookupRW.DidChange(childEntity, LastSystemVersion);
                        for (int i = 0, childCount = children.Length; i < childCount; i++)
                        {
                            ChildLocalToWorldFromTransformMatrix(localToWorld, children[i].Value, updateChildrenTransform);
                        }
                    }
                }
            }

            public void Execute(int index)
            {
                Entity root = RootEntities[index];
                if (ChildLookupRO.TryGetBuffer(root, out DynamicBuffer<Child> children))
                {
                    bool updateChildrenTransform = ChildLookupRO.DidChange(root, LastSystemVersion) ||
                                                   LocalToWorldLookupRW.DidChange(root, LastSystemVersion);
                    float4x4 localToWorldMatrix = LocalToWorldLookupRW[root].Value;
                    for (int j = 0, childCount = children.Length; j < childCount; j++)
                    {
                        ChildLocalToWorldFromTransformMatrix(localToWorldMatrix, children[j].Value, updateChildrenTransform);
                    }
                }
            }
        }

        EntityQuery _worldSpaceQuery;
        EntityQuery _hierarchyRootsQuery;
        EntityQueryMask _localToWorldWriteGroupMask;

        ComponentTypeHandle<LocalTransform> _localTransformTypeHandleRO;
        ComponentTypeHandle<PostTransformMatrix> _postTransformMatrixTypeHandleRO;
        ComponentTypeHandle<LocalToWorld> _localToWorldTypeHandleRW;

        BufferLookup<Child> _childLookupRO;
        ComponentLookup<LocalTransform> _localTransformLookupRO;
        ComponentLookup<PostTransformMatrix> _postTransformMatrixLookupRO;
        ComponentLookup<LocalToWorld> _localToWorldLookupRW;

        /// <inheritdoc cref="ISystem.OnCreate"/>
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LocalTransform>()
                .WithAllRW<LocalToWorld>()
                .WithNone<Parent>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup);
            _worldSpaceQuery = state.GetEntityQuery(builder);
            // Ideally we'd use a change-version filter on worldSpaceQuery, but we need to process chunks with PostTransformMatrix,
            // which isn't required by the query. Instead, we use chunk.DidChange() inside the job as an early-out.

            builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LocalTransform, Child>()
                .WithAllRW<LocalToWorld>()
                .WithNone<Parent>();
            _hierarchyRootsQuery = state.GetEntityQuery(builder);

            builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LocalTransform, Parent>()
                .WithAllRW<LocalToWorld>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup);
            _localToWorldWriteGroupMask = state.GetEntityQuery(builder).GetEntityQueryMask();

            _localTransformTypeHandleRO = state.GetComponentTypeHandle<LocalTransform>(true);
            _postTransformMatrixTypeHandleRO = state.GetComponentTypeHandle<PostTransformMatrix>(true);
            _localToWorldTypeHandleRW = state.GetComponentTypeHandle<LocalToWorld>(false);

            _childLookupRO = state.GetBufferLookup<Child>(true);
            _localTransformLookupRO = state.GetComponentLookup<LocalTransform>(true);
            _postTransformMatrixLookupRO = state.GetComponentLookup<PostTransformMatrix>(true);
            _localToWorldLookupRW = state.GetComponentLookup<LocalToWorld>(false);
        }

        /// <inheritdoc cref="ISystem.OnUpdate"/>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _localTransformTypeHandleRO.Update(ref state);
            _postTransformMatrixTypeHandleRO.Update(ref state);
            _localToWorldTypeHandleRW.Update(ref state);
            _childLookupRO.Update(ref state);
            _localTransformLookupRO.Update(ref state);
            _postTransformMatrixLookupRO.Update(ref state);
            _localToWorldLookupRW.Update(ref state);

            // Compute LocalToWorld for all world-space entities
            var worldSpaceJob = new ComputeWorldSpaceLocalToWorldJob
            {
                LocalTransformTypeHandleRO = _localTransformTypeHandleRO,
                PostTransformMatrixTypeHandleRO = _postTransformMatrixTypeHandleRO,
                LocalToWorldTypeHandleRW = _localToWorldTypeHandleRW,
                LastSystemVersion = state.LastSystemVersion,
            };
            var worldSpaceJobHandle = worldSpaceJob.ScheduleParallelByRef(_worldSpaceQuery, state.Dependency);
            if (_hierarchyRootsQuery.IsEmptyIgnoreFilter)
            {
                state.Dependency = worldSpaceJobHandle;
            }
            else
            {
                // Gather all hierarchy root entities into a list
                var rootEntityList =
                    _hierarchyRootsQuery.ToEntityListAsync(state.WorldUpdateAllocator, state.Dependency,
                        out JobHandle gatherJobHandle);
                // Compute LocalToWorld for all hierarchies.
                // The root LTWs are already up-to-date from the previous job, and are not recomputed.
                var hierarchyJob = new ComputeHierarchyLocalToWorldJob
                {
                    RootEntities = rootEntityList.AsDeferredJobArray(),
                    LocalToWorldWriteGroupMask = _localToWorldWriteGroupMask,
                    ChildLookupRO = _childLookupRO,
                    LocalTransformLookupRO = _localTransformLookupRO,
                    PostTransformMatrixLookupRO = _postTransformMatrixLookupRO,
                    LocalToWorldLookupRW = _localToWorldLookupRW,
                    LastSystemVersion = state.LastSystemVersion,
                };
                state.Dependency = hierarchyJob.ScheduleByRef(rootEntityList, 1,
                    JobHandle.CombineDependencies(worldSpaceJobHandle, gatherJobHandle));
            }
        }
    }
}
