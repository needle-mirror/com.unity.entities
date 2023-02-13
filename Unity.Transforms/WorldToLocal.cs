using System;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

#if !ENABLE_TRANSFORM_V1
#else

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
    [RequireMatchingQueriesForUpdate]
    public partial struct WorldToLocalSystem : ISystem
    {
        private EntityQuery m_Query;
        ComponentTypeHandle<LocalToWorld> m_LocalToWorldTypeHandle;
        ComponentTypeHandle<WorldToLocal> m_WorldToLocalTypeHandle;

        [BurstCompile]
        struct ToWorldToLocal : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> LocalToWorldTypeHandle;
            public ComponentTypeHandle<WorldToLocal> WorldToLocalTypeHandle;
            public uint LastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);

                if (!chunk.DidChange(ref LocalToWorldTypeHandle, LastSystemVersion))
                    return;

                var chunkLocalToWorld = chunk.GetNativeArray(ref LocalToWorldTypeHandle);
                var chunkWorldToLocal = chunk.GetNativeArray(ref WorldToLocalTypeHandle);

                for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                {
                    var localToWorld = chunkLocalToWorld[i].Value;
                    chunkWorldToLocal[i] = new WorldToLocal {Value = math.inverse(localToWorld)};
                }
            }
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<WorldToLocal>()
                .WithAll<LocalToWorld>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup);
            m_Query = state.GetEntityQuery(builder);

            m_LocalToWorldTypeHandle = state.GetComponentTypeHandle<LocalToWorld>(true);
            m_WorldToLocalTypeHandle = state.GetComponentTypeHandle<WorldToLocal>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_LocalToWorldTypeHandle.Update(ref state);
            m_WorldToLocalTypeHandle.Update(ref state);

            var toWorldToLocalJob = new ToWorldToLocal
            {
                LocalToWorldTypeHandle = m_LocalToWorldTypeHandle,
                WorldToLocalTypeHandle = m_WorldToLocalTypeHandle,
                LastSystemVersion = state.LastSystemVersion
            };
            state.Dependency = toWorldToLocalJob.ScheduleParallel(m_Query, state.Dependency);
        }
    }
}

#endif
