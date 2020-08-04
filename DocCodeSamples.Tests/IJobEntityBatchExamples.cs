using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using NUnit.Framework;

// The files in this namespace are used to compile/test the code samples in the documentation.
namespace Doc.CodeSamples.Tests
{

    #region basic-ijobentitybatch
    [GenerateAuthoringComponent]
    public struct ExpensiveTarget : IComponentData
    {
        public Entity entity;
    }

    public class BatchedChaserSystem : SystemBase
    {
        private EntityQuery query; // Initialized in Oncreate()

        [BurstCompile]
        private struct BatchedChaserSystemJob : IJobEntityBatch
        {
            // Read-write data in the current chunk
            public ComponentTypeHandle<Translation> PositionTypeHandleAccessor;

            // Read-only data in the current chunk
            [ReadOnly]
            public ComponentTypeHandle<Target> TargetTypeHandleAccessor;

            // Read-only data stored (potentially) in other chunks
            [ReadOnly]
            //[NativeDisableParallelForRestriction]
            public ComponentDataFromEntity<LocalToWorld> EntityPositions;

            // Non-entity data
            public float deltaTime;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                // Within Execute(), the scope of the ArchetypeChunk is limited to the current batch.
                // For example, these NativeArrays will have Length = batchInChunk.BatchEntityCount,
                // where batchInChunk.BatchEntityCount is roughly batchInChunk.Capacity divided by the
                // batchesInChunk parameter passed to ScheduleParallelBatched().
                NativeArray<Translation> positions = batchInChunk.GetNativeArray<Translation>(PositionTypeHandleAccessor);
                NativeArray<Target> targets = batchInChunk.GetNativeArray<Target>(TargetTypeHandleAccessor);

                for (int i = 0; i < positions.Length; i++)
                {
                    Entity targetEntity = targets[i].entity;
                    float3 targetPosition = EntityPositions[targetEntity].Position;
                    float3 chaserPosition = positions[i].Value;

                    float3 displacement = (targetPosition - chaserPosition);
                    positions[i] = new Translation { Value = chaserPosition + displacement * deltaTime };
                }
            }
        }

        protected override void OnCreate()
        {
            query = this.GetEntityQuery(typeof(Translation), ComponentType.ReadOnly<Target>());
        }

        protected override void OnUpdate()
        {
            var job = new BatchedChaserSystemJob();
            job.PositionTypeHandleAccessor = this.GetComponentTypeHandle<Translation>(false);
            job.TargetTypeHandleAccessor = this.GetComponentTypeHandle<Target>(true);

            job.EntityPositions = this.GetComponentDataFromEntity<LocalToWorld>(true);
            job.deltaTime = this.Time.DeltaTime;

            int batchesPerChunk = 4; // Partition each chunk into this many batches. Each batch will be processed concurrently.
            this.Dependency = job.ScheduleParallel(query, batchesPerChunk, this.Dependency);
        }
    }
    #endregion
}
