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

    public abstract class WorldToLocalSystem : JobComponentSystem
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

        protected override void OnCreate()
        {
            m_Query = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    typeof(WorldToLocal),
                    ComponentType.ReadOnly<LocalToWorld>(),
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var toWorldToLocalJob = new ToWorldToLocal
            {
                LocalToWorldTypeHandle = GetComponentTypeHandle<LocalToWorld>(true),
                WorldToLocalTypeHandle = GetComponentTypeHandle<WorldToLocal>(),
                LastSystemVersion = LastSystemVersion
            };
            var toWorldToLocalJobHandle = toWorldToLocalJob.ScheduleParallel(m_Query, 1, inputDeps);
            return toWorldToLocalJobHandle;
        }
    }
}
