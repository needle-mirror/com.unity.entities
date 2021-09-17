using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

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
    public partial struct ParentScaleInverseSystem : ISystem
    {
        private EntityQuery m_Query;

        [BurstCompile]
        struct ToChildParentScaleInverse : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<Scale> ScaleTypeHandle;
            [ReadOnly] public ComponentTypeHandle<NonUniformScale> NonUniformScaleTypeHandle;
            [ReadOnly] public ComponentTypeHandle<CompositeScale> CompositeScaleTypeHandle;
            [ReadOnly] public BufferTypeHandle<Child> ChildTypeHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentDataFromEntity<ParentScaleInverse> ParentScaleInverseFromEntity;
            public uint LastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                var hasScale = batchInChunk.Has(ScaleTypeHandle);
                var hasNonUniformScale = batchInChunk.Has(NonUniformScaleTypeHandle);
                var hasCompositeScale = batchInChunk.Has(CompositeScaleTypeHandle);

                if (hasCompositeScale)
                {
                    var didChange = batchInChunk.DidChange(CompositeScaleTypeHandle, LastSystemVersion) ||
                        batchInChunk.DidChange(ChildTypeHandle, LastSystemVersion);
                    if (!didChange)
                        return;

                    var chunkCompositeScales = batchInChunk.GetNativeArray(CompositeScaleTypeHandle);
                    var chunkChildren = batchInChunk.GetBufferAccessor(ChildTypeHandle);
                    for (var i = 0; i < batchInChunk.Count; i++)
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
                    var didChange = batchInChunk.DidChange(ScaleTypeHandle, LastSystemVersion) ||
                        batchInChunk.DidChange(ChildTypeHandle, LastSystemVersion);
                    if (!didChange)
                        return;

                    var chunkScales = batchInChunk.GetNativeArray(ScaleTypeHandle);
                    var chunkChildren = batchInChunk.GetBufferAccessor(ChildTypeHandle);
                    for (var i = 0; i < batchInChunk.Count; i++)
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
                    var didChange = batchInChunk.DidChange(NonUniformScaleTypeHandle, LastSystemVersion) ||
                        batchInChunk.DidChange(ChildTypeHandle, LastSystemVersion);
                    if (!didChange)
                        return;

                    var chunkNonUniformScales = batchInChunk.GetNativeArray(NonUniformScaleTypeHandle);
                    var chunkChildren = batchInChunk.GetBufferAccessor(ChildTypeHandle);
                    for (var i = 0; i < batchInChunk.Count; i++)
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

        //burst disabled pending burstable entityquerydesc
        //[BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_Query = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<Child>(),
                },
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<Scale>(),
                    ComponentType.ReadOnly<NonUniformScale>(),
                    ComponentType.ReadOnly<CompositeScale>(),
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        //disabling burst in dotsrt until burstable scheduling works
#if !UNITY_DOTSRUNTIME
        [BurstCompile]
#endif
        public void OnUpdate(ref SystemState state)
        {
            var toParentScaleInverseJob = new ToChildParentScaleInverse
            {
                ScaleTypeHandle = state.GetComponentTypeHandle<Scale>(true),
                NonUniformScaleTypeHandle = state.GetComponentTypeHandle<NonUniformScale>(true),
                CompositeScaleTypeHandle = state.GetComponentTypeHandle<CompositeScale>(true),
                ChildTypeHandle = state.GetBufferTypeHandle<Child>(true),
                ParentScaleInverseFromEntity = state.GetComponentDataFromEntity<ParentScaleInverse>(),
                LastSystemVersion = state.LastSystemVersion
            };
            state.Dependency = toParentScaleInverseJob.ScheduleParallel(m_Query, state.Dependency);
        }
    }
}
