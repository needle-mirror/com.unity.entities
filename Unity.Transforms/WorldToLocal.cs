using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unity.Transforms
{
    [Serializable]
    public struct WorldToLocal : IComponentData
    {
        public float4x4 Value;

        public float3 Right => new float3(Value.c0.x, Value.c0.y, Value.c0.z);
        public float3 Up => new float3(Value.c1.x, Value.c1.y, Value.c1.z);
        public float3 Forward => new float3(Value.c2.x, Value.c2.y, Value.c2.z);
        public float3 Position => new float3(Value.c3.x, Value.c3.y, Value.c3.z);
    }

    [BurstCompile]
    public partial struct WorldToLocalSystem : ISystem
    {
        private EntityQuery m_Query;

        [BurstCompile]
        struct ToWorldToLocal : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> LocalToWorldTypeHandle;
            public ComponentTypeHandle<WorldToLocal> WorldToLocalTypeHandle;
            public uint LastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                if (!batchInChunk.DidChange(LocalToWorldTypeHandle, LastSystemVersion))
                    return;

                var chunkLocalToWorld = batchInChunk.GetNativeArray(LocalToWorldTypeHandle);
                var chunkWorldToLocal = batchInChunk.GetNativeArray(WorldToLocalTypeHandle);

                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    var localToWorld = chunkLocalToWorld[i].Value;
                    chunkWorldToLocal[i] = new WorldToLocal {Value = math.inverse(localToWorld)};
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
                    typeof(WorldToLocal),
                    ComponentType.ReadOnly<LocalToWorld>(),
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
            var toWorldToLocalJob = new ToWorldToLocal
            {
                LocalToWorldTypeHandle = state.GetComponentTypeHandle<LocalToWorld>(true),
                WorldToLocalTypeHandle = state.GetComponentTypeHandle<WorldToLocal>(),
                LastSystemVersion = state.LastSystemVersion
            };
            state.Dependency = toWorldToLocalJob.ScheduleParallel(m_Query, state.Dependency);
        }
    }
}
