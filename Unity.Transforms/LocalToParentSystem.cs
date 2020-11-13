using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unity.Transforms
{
    public abstract class LocalToParentSystem : JobComponentSystem
    {
        private EntityQuery m_RootsQuery;
        private EntityQueryMask m_LocalToWorldWriteGroupMask;

        // LocalToWorld = Parent.LocalToWorld * LocalToParent
        [BurstCompile]
        struct UpdateHierarchy : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> LocalToWorldTypeHandle;
            [ReadOnly] public BufferTypeHandle<Child> ChildTypeHandle;
            [ReadOnly] public BufferFromEntity<Child> ChildFromEntity;
            [ReadOnly] public ComponentDataFromEntity<LocalToParent> LocalToParentFromEntity;
            [ReadOnly] public EntityQueryMask LocalToWorldWriteGroupMask;
            public uint LastSystemVersion;

            [NativeDisableContainerSafetyRestriction]
            public ComponentDataFromEntity<LocalToWorld> LocalToWorldFromEntity;

            void ChildLocalToWorld(float4x4 parentLocalToWorld, Entity entity, bool updateChildrenTransform)
            {
                updateChildrenTransform = updateChildrenTransform || LocalToParentFromEntity.DidChange(entity, LastSystemVersion);

                float4x4 localToWorldMatrix;

                if (updateChildrenTransform && LocalToWorldWriteGroupMask.Matches(entity))
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

                if (ChildFromEntity.HasComponent(entity))
                {
                    var children = ChildFromEntity[entity];
                    for (int i = 0; i < children.Length; i++)
                    {
                        ChildLocalToWorld(localToWorldMatrix, children[i].Value, updateChildrenTransform);
                    }
                }
            }

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                bool updateChildrenTransform =
                    batchInChunk.DidChange<LocalToWorld>(LocalToWorldTypeHandle, LastSystemVersion) ||
                    batchInChunk.DidChange<Child>(ChildTypeHandle, LastSystemVersion);

                var chunkLocalToWorld = batchInChunk.GetNativeArray(LocalToWorldTypeHandle);
                var chunkChildren = batchInChunk.GetBufferAccessor(ChildTypeHandle);
                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    var localToWorldMatrix = chunkLocalToWorld[i].Value;
                    var children = chunkChildren[i];
                    for (int j = 0; j < children.Length; j++)
                    {
                        ChildLocalToWorld(localToWorldMatrix, children[j].Value, updateChildrenTransform);
                    }
                }
            }
        }

        protected override void OnCreate()
        {
            m_RootsQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<LocalToWorld>(),
                    ComponentType.ReadOnly<Child>()
                },
                None = new ComponentType[]
                {
                    typeof(Parent)
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });

            m_LocalToWorldWriteGroupMask = EntityManager.GetEntityQueryMask(GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    typeof(LocalToWorld),
                    ComponentType.ReadOnly<LocalToParent>(),
                    ComponentType.ReadOnly<Parent>()
                },
                Options = EntityQueryOptions.FilterWriteGroup
            }));
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var localToWorldType = GetComponentTypeHandle<LocalToWorld>(true);
            var childType = GetBufferTypeHandle<Child>(true);
            var childFromEntity = GetBufferFromEntity<Child>(true);
            var localToParentFromEntity = GetComponentDataFromEntity<LocalToParent>(true);
            var localToWorldFromEntity = GetComponentDataFromEntity<LocalToWorld>();

            var updateHierarchyJob = new UpdateHierarchy
            {
                LocalToWorldTypeHandle = localToWorldType,
                ChildTypeHandle = childType,
                ChildFromEntity = childFromEntity,
                LocalToParentFromEntity = localToParentFromEntity,
                LocalToWorldFromEntity = localToWorldFromEntity,
                LocalToWorldWriteGroupMask = m_LocalToWorldWriteGroupMask,
                LastSystemVersion = LastSystemVersion
            };
            var updateHierarchyJobHandle = updateHierarchyJob.ScheduleParallel(m_RootsQuery, 1, inputDeps);
            return updateHierarchyJobHandle;
        }
    }
}
