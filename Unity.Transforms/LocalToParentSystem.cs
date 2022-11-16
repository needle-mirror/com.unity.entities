using System;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

#if !ENABLE_TRANSFORM_V1
#else

namespace Unity.Transforms
{
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    public partial struct LocalToParentSystem : ISystem
    {
        private EntityQuery m_RootsQuery;
        private EntityQueryMask m_LocalToWorldWriteGroupMask;
        private ComponentTypeHandle<LocalToWorld> LocalToWorldTypeHandleRO;
        private BufferTypeHandle<Child> ChildTypeHandleRO;
        private BufferLookup<Child> _childLookupRo;
        private ComponentLookup<LocalToParent> LocalToParentFromEntityRO;
        private ComponentLookup<LocalToWorld> LocalToWorldFromEntityRW;

        // LocalToWorld = Parent.LocalToWorld * LocalToParent
        [BurstCompile]
        struct UpdateHierarchy : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> LocalToWorldTypeHandle;
            [ReadOnly] public BufferTypeHandle<Child> ChildTypeHandle;
            [ReadOnly] public BufferLookup<Child> ChildLookup;
            [ReadOnly] public ComponentLookup<LocalToParent> LocalToParentFromEntity;
            [ReadOnly] public EntityQueryMask LocalToWorldWriteGroupMask;
            public uint LastSystemVersion;

            [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<LocalToWorld> LocalToWorldFromEntity;

            void ChildLocalToWorld(float4x4 parentLocalToWorld, Entity entity, bool updateChildrenTransform)
            {
                updateChildrenTransform = updateChildrenTransform || LocalToParentFromEntity.DidChange(entity, LastSystemVersion);

                float4x4 localToWorldMatrix;

                if (updateChildrenTransform && LocalToWorldWriteGroupMask.MatchesIgnoreFilter(entity))
                {
                    var localToParent = LocalToParentFromEntity[entity];
                    localToWorldMatrix = math.mul(parentLocalToWorld, localToParent.Value);
                    LocalToWorldFromEntity[entity] = new LocalToWorld {Value = localToWorldMatrix};
                }
                else //This entity has a component with the WriteGroup(LocalToWorld)
                {
                    localToWorldMatrix = LocalToWorldFromEntity[entity].Value;
                    updateChildrenTransform = updateChildrenTransform || LocalToWorldFromEntity.DidChange(entity, LastSystemVersion);
                }

                if (ChildLookup.HasBuffer(entity))
                {
                    var children = ChildLookup[entity];
                    for (int i = 0, childCount = children.Length; i < childCount; i++)
                    {
                        ChildLocalToWorld(localToWorldMatrix, children[i].Value, updateChildrenTransform);
                    }
                }
            }

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);

                bool updateChildrenTransform =
                    chunk.DidChange<LocalToWorld>(ref LocalToWorldTypeHandle, LastSystemVersion) ||
                    chunk.DidChange<Child>(ref ChildTypeHandle, LastSystemVersion);

                var chunkLocalToWorld = chunk.GetNativeArray(ref LocalToWorldTypeHandle);
                var chunkChildren = chunk.GetBufferAccessor(ref ChildTypeHandle);
                for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                {
                    var localToWorldMatrix = chunkLocalToWorld[i].Value;
                    var children = chunkChildren[i];
                    for (int j = 0, childCount = children.Length; j < childCount; j++)
                    {
                        ChildLocalToWorld(localToWorldMatrix, children[j].Value, updateChildrenTransform);
                    }
                }
            }
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LocalToWorld, Child>()
                .WithNone<Parent>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup);
            m_RootsQuery = state.GetEntityQuery(builder);

            var builder2 = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<LocalToWorld>()
                .WithAll<LocalToParent, Parent>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup);
            m_LocalToWorldWriteGroupMask = state.GetEntityQuery(builder2).GetEntityQueryMask();

            LocalToWorldTypeHandleRO = state.GetComponentTypeHandle<LocalToWorld>(true);
            ChildTypeHandleRO = state.GetBufferTypeHandle<Child>(true);
            _childLookupRo = state.GetBufferLookup<Child>(true);
            LocalToParentFromEntityRO = state.GetComponentLookup<LocalToParent>(true);
            LocalToWorldFromEntityRW = state.GetComponentLookup<LocalToWorld>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            LocalToWorldTypeHandleRO.Update(ref state);
            ChildTypeHandleRO.Update(ref state);
            _childLookupRo.Update(ref state);
            LocalToParentFromEntityRO.Update(ref state);
            LocalToWorldFromEntityRW.Update(ref state);

            var updateHierarchyJob = new UpdateHierarchy
            {
                LocalToWorldTypeHandle = LocalToWorldTypeHandleRO,
                ChildTypeHandle = ChildTypeHandleRO,
                ChildLookup = _childLookupRo,
                LocalToParentFromEntity = LocalToParentFromEntityRO,
                LocalToWorldFromEntity = LocalToWorldFromEntityRW,
                LocalToWorldWriteGroupMask = m_LocalToWorldWriteGroupMask,
                LastSystemVersion = state.LastSystemVersion
            };
            state.Dependency = updateHierarchyJob.ScheduleParallel(m_RootsQuery, state.Dependency);
        }
    }
}

#endif
