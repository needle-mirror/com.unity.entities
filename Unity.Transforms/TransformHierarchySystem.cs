using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Assertions;
using Unity.Burst.Intrinsics;
using UnityEngine;

#if !ENABLE_TRANSFORM_V1

namespace Unity.Transforms
{
    /// <summary>
    /// This system propagates transformation data through hierarchies of entities.
    /// </summary>
    /// <remarks>
    /// Entity transformation hierarchies are created using the <see cref="Parent"/> and <see cref="LocalToParentTransform"/>
    /// components, and maintained by the <see cref="ParentSystem"/>.
    ///
    /// This system processes all root-level parent entities, recursively processing each hierarchy to update each child's
    /// <see cref="LocalToWorldTransform"/> and <see cref="ParentToWorldTransform"/> based on the child's <see cref="LocalToParentTransform"/>
    /// and the parent's <see cref="LocalToWorldTransform"/>.
    /// </remarks>
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(ParentSystem))]
    public partial struct TransformHierarchySystem : ISystem
    {
        private EntityQuery RootsQuery;
        private EntityQueryMask LocalToWorldTransformWriteGroupMask;
        private ComponentTypeHandle<LocalToWorldTransform> LocalToWorldTransformTypeHandleRO;
        private BufferTypeHandle<Child> ChildTypeHandleRO;
        private BufferLookup<Child> ChildrenLookupRO;
        private ComponentLookup<LocalToParentTransform> LocalToParentTransformLookupRO;
        private ComponentLookup<LocalToWorldTransform> LocalToWorldTransformLookupRW;
        private ComponentLookup<ParentToWorldTransform> ParentToWorldTransformLookupRW;
        private EntityQuery ParentToWorldWithoutParentQuery;
        private EntityQuery ParentWithoutParentToWorldQuery;

        [BurstCompile]
        struct UpdateHierarchy : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<LocalToWorldTransform> LocalToWorldTransformTypeHandle;
            [ReadOnly] public BufferTypeHandle<Child> ChildTypeHandle;
            [ReadOnly] public BufferLookup<Child> ChildrenLookup;
            [ReadOnly] public ComponentLookup<LocalToParentTransform> LocalToParentTransformLookup;
            public EntityQueryMask LocalToWorldTransformWriteGroupMask;
            public uint LastSystemVersion;

            [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<LocalToWorldTransform> LocalToWorldTransformLookup;

            [NativeDisableContainerSafetyRestriction]
            public ComponentLookup<ParentToWorldTransform> ParentToWorldTransformLookup;

            void ChildLocalToWorld(UniformScaleTransform parentLocalToWorldTransform, Entity childEntity, bool updateChildrenTransform)
            {
                updateChildrenTransform = updateChildrenTransform
                                          || LocalToParentTransformLookup.DidChange(childEntity, LastSystemVersion);

                UniformScaleTransform localToWorldTransform;

                if (updateChildrenTransform && LocalToWorldTransformWriteGroupMask.MatchesIgnoreFilter(childEntity))
                {
                    ParentToWorldTransformLookup[childEntity] = new ParentToWorldTransform
                        {Value = parentLocalToWorldTransform};
                    var localToParent = LocalToParentTransformLookup[childEntity];
                    localToWorldTransform = parentLocalToWorldTransform.TransformTransform(localToParent.Value);
                    LocalToWorldTransformLookup[childEntity] = new LocalToWorldTransform
                        {Value = localToWorldTransform};
                }
                else //This entity has a component with the WriteGroup(LocalToWorldTransform)
                {
                    localToWorldTransform = LocalToWorldTransformLookup[childEntity].Value;
                    updateChildrenTransform = updateChildrenTransform || LocalToWorldTransformLookup.DidChange(childEntity, LastSystemVersion);
                }

                if (ChildrenLookup.TryGetBuffer(childEntity, out DynamicBuffer<Child> children))
                {
                    for (int i = 0, childCount = children.Length; i < childCount; i++)
                    {
                        ChildLocalToWorld(localToWorldTransform, children[i].Value, updateChildrenTransform);
                    }
                }
            }

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);

                bool updateChildrenTransform =
                    chunk.DidChange(LocalToWorldTransformTypeHandle, LastSystemVersion) ||
                    chunk.DidChange(ChildTypeHandle, LastSystemVersion);

                var chunkLocalToWorld = chunk.GetNativeArray(LocalToWorldTransformTypeHandle);
                var chunkChildren = chunk.GetBufferAccessor(ChildTypeHandle);
                for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                {
                    var localToWorldTransform = chunkLocalToWorld[i].Value;
                    var children = chunkChildren[i];
                    for (int j = 0, childCount = children.Length; j < childCount; j++)
                    {
                        ChildLocalToWorld(localToWorldTransform, children[j].Value, updateChildrenTransform);
                    }
                }
            }
        }

        /// <inheritdoc cref="ISystem.OnCreate"/>
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<LocalToWorldTransform, Child>()
                .WithNone<Parent>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup);
            RootsQuery = state.GetEntityQuery(builder);

            builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<LocalToWorldTransform>()
                .WithAll<LocalToParentTransform, Parent>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup);
            LocalToWorldTransformWriteGroupMask = state.GetEntityQuery(builder).GetEntityQueryMask();

            builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<ParentToWorldTransform>()
                .WithNone<Parent>();
            ParentToWorldWithoutParentQuery = state.GetEntityQuery(builder);

            builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<Parent>()
                .WithNone<ParentToWorldTransform>();
            ParentWithoutParentToWorldQuery = state.GetEntityQuery(builder);

            LocalToWorldTransformTypeHandleRO = state.GetComponentTypeHandle<LocalToWorldTransform>(true);
            ChildTypeHandleRO = state.GetBufferTypeHandle<Child>(true);
            ChildrenLookupRO = state.GetBufferLookup<Child>(true);
            LocalToParentTransformLookupRO = state.GetComponentLookup<LocalToParentTransform>(true);
            LocalToWorldTransformLookupRW = state.GetComponentLookup<LocalToWorldTransform>();
            ParentToWorldTransformLookupRW = state.GetComponentLookup<ParentToWorldTransform>();
        }

        /// <inheritdoc cref="ISystem.OnDestroy"/>
        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        /// <inheritdoc cref="ISystem.OnUpdate"/>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!ParentWithoutParentToWorldQuery.IsEmptyIgnoreFilter)
            {
                state.EntityManager.AddComponent(ParentWithoutParentToWorldQuery, ComponentType.FromTypeIndex(
                    TypeManager.GetTypeIndex<ParentToWorldTransform>()));
            }

            if (!ParentToWorldWithoutParentQuery.IsEmptyIgnoreFilter)
            {
                state.EntityManager.RemoveComponent(ParentToWorldWithoutParentQuery, ComponentType.FromTypeIndex(
                    TypeManager.GetTypeIndex<ParentToWorldTransform>()));
            }

            LocalToWorldTransformTypeHandleRO.Update(ref state);
            ChildTypeHandleRO.Update(ref state);
            ChildrenLookupRO.Update(ref state);
            LocalToParentTransformLookupRO.Update(ref state);
            LocalToWorldTransformLookupRW.Update(ref state);
            ParentToWorldTransformLookupRW.Update(ref state);

            var updateHierarchyJob = new UpdateHierarchy
            {
                LocalToWorldTransformTypeHandle = LocalToWorldTransformTypeHandleRO,
                ChildTypeHandle = ChildTypeHandleRO,
                ChildrenLookup = ChildrenLookupRO,
                ParentToWorldTransformLookup = ParentToWorldTransformLookupRW,
                LocalToWorldTransformLookup = LocalToWorldTransformLookupRW,
                LocalToParentTransformLookup = LocalToParentTransformLookupRO,
                LocalToWorldTransformWriteGroupMask = LocalToWorldTransformWriteGroupMask,
                LastSystemVersion = state.LastSystemVersion
            };
            state.Dependency = updateHierarchyJob.ScheduleParallel(RootsQuery, state.Dependency);
        }
    }
}

#endif
