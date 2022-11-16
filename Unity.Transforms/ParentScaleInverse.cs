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
    [Serializable]
    [WriteGroup(typeof(LocalToParent))]
    public struct ParentScaleInverse : IComponentData
    {
        public float4x4 Value;

        public float3 Right => new float3(Value.c0.x, Value.c0.y, Value.c0.z);
        public float3 Up => new float3(Value.c1.x, Value.c1.y, Value.c1.z);
        public float3 Forward => new float3(Value.c2.x, Value.c2.y, Value.c2.z);
        public float3 Position => new float3(Value.c3.x, Value.c3.y, Value.c3.z);
    }

    // ParentScaleInverse = Parent.CompositeScale^-1
    // (or) ParentScaleInverse = Parent.Scale^-1
    // (or) ParentScaleInverse = Parent.NonUniformScale^-1
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    public partial struct ParentScaleInverseSystem : ISystem
    {
        private EntityQuery m_Query;

        private ComponentTypeHandle<Scale> ScaleTypeHandleRO;
        private ComponentTypeHandle<NonUniformScale> NonUniformScaleTypeHandleRO;
        private ComponentTypeHandle<CompositeScale> CompositeScaleTypeHandleRO;
        private BufferTypeHandle<Child> ChildTypeHandleRO;
        private ComponentLookup<ParentScaleInverse> ParentScaleInverseFromEntityRW;

        [BurstCompile]
        struct ToChildParentScaleInverse : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<Scale> ScaleTypeHandle;
            [ReadOnly] public ComponentTypeHandle<NonUniformScale> NonUniformScaleTypeHandle;
            [ReadOnly] public ComponentTypeHandle<CompositeScale> CompositeScaleTypeHandle;
            [ReadOnly] public BufferTypeHandle<Child> ChildTypeHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentLookup<ParentScaleInverse> ParentScaleInverseFromEntity;
            public uint LastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);

                var hasScale = chunk.Has(ref ScaleTypeHandle);
                var hasNonUniformScale = chunk.Has(ref NonUniformScaleTypeHandle);
                var hasCompositeScale = chunk.Has(ref CompositeScaleTypeHandle);

                if (hasCompositeScale)
                {
                    var didChange = chunk.DidChange(ref CompositeScaleTypeHandle, LastSystemVersion) ||
                        chunk.DidChange(ref ChildTypeHandle, LastSystemVersion);
                    if (!didChange)
                        return;

                    var chunkCompositeScales = chunk.GetNativeArray(ref CompositeScaleTypeHandle);
                    var chunkChildren = chunk.GetBufferAccessor(ref ChildTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        var inverseScale = math.inverse(chunkCompositeScales[i].Value);
                        var children = chunkChildren[i];
                        for (var j = 0; j < children.Length; j++)
                        {
                            var childEntity = children[j].Value;
                            if (!ParentScaleInverseFromEntity.HasComponent(childEntity))
                                continue;

                            ParentScaleInverseFromEntity[childEntity] = new ParentScaleInverse {Value = inverseScale};
                        }
                    }
                }
                else if (hasScale)
                {
                    var didChange = chunk.DidChange(ref ScaleTypeHandle, LastSystemVersion) ||
                        chunk.DidChange(ref ChildTypeHandle, LastSystemVersion);
                    if (!didChange)
                        return;

                    var chunkScales = chunk.GetNativeArray(ref ScaleTypeHandle);
                    var chunkChildren = chunk.GetBufferAccessor(ref ChildTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        var inverseScale = float4x4.Scale(1.0f / chunkScales[i].Value);
                        var children = chunkChildren[i];
                        for (var j = 0; j < children.Length; j++)
                        {
                            var childEntity = children[j].Value;
                            if (!ParentScaleInverseFromEntity.HasComponent(childEntity))
                                continue;

                            ParentScaleInverseFromEntity[childEntity] = new ParentScaleInverse {Value = inverseScale};
                        }
                    }
                }
                else // if (hasNonUniformScale)
                {
                    var didChange = chunk.DidChange(ref NonUniformScaleTypeHandle, LastSystemVersion) ||
                        chunk.DidChange(ref ChildTypeHandle, LastSystemVersion);
                    if (!didChange)
                        return;

                    var chunkNonUniformScales = chunk.GetNativeArray(ref NonUniformScaleTypeHandle);
                    var chunkChildren = chunk.GetBufferAccessor(ref ChildTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        var inverseScale = float4x4.Scale(1.0f / chunkNonUniformScales[i].Value);
                        var children = chunkChildren[i];
                        for (var j = 0; j < children.Length; j++)
                        {
                            var childEntity = children[j].Value;
                            if (!ParentScaleInverseFromEntity.HasComponent(childEntity))
                                continue;

                            ParentScaleInverseFromEntity[childEntity] = new ParentScaleInverse {Value = inverseScale};
                        }
                    }
                }
            }
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Child>()
                .WithAny<Scale, NonUniformScale, CompositeScale>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup);

            m_Query = state.GetEntityQuery(builder);

            ScaleTypeHandleRO = state.GetComponentTypeHandle<Scale>(true);
            NonUniformScaleTypeHandleRO = state.GetComponentTypeHandle<NonUniformScale>(true);
            CompositeScaleTypeHandleRO = state.GetComponentTypeHandle<CompositeScale>(true);
            ChildTypeHandleRO = state.GetBufferTypeHandle<Child>(true);
            ParentScaleInverseFromEntityRW = state.GetComponentLookup<ParentScaleInverse>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ScaleTypeHandleRO.Update(ref state);
            NonUniformScaleTypeHandleRO.Update(ref state);
            CompositeScaleTypeHandleRO.Update(ref state);
            ChildTypeHandleRO.Update(ref state);
            ParentScaleInverseFromEntityRW.Update(ref state);

            var toParentScaleInverseJob = new ToChildParentScaleInverse
            {
                ScaleTypeHandle = ScaleTypeHandleRO,
                NonUniformScaleTypeHandle = NonUniformScaleTypeHandleRO,
                CompositeScaleTypeHandle = CompositeScaleTypeHandleRO,
                ChildTypeHandle = ChildTypeHandleRO,
                ParentScaleInverseFromEntity = ParentScaleInverseFromEntityRW,
                LastSystemVersion = state.LastSystemVersion
            };
            state.Dependency = toParentScaleInverseJob.ScheduleParallel(m_Query, state.Dependency);
        }
    }
}

#endif
