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
#pragma warning disable 618 // IJobEntityBatch is obsolete
    #region basic-ijobentitybatch

    public struct ExpensiveTarget : IComponentData
    {
        public Entity entity;
    }

    [RequireMatchingQueriesForUpdate]
    public partial class BatchedChaserSystem : SystemBase
    {
        private EntityQuery query; // Initialized in Oncreate()

        [BurstCompile]
        private struct BatchedChaserSystemJob : IJobEntityBatch
        {
            // Read-write data in the current chunk
            public ComponentTypeHandle<ObjectPosition> PositionTypeHandleAccessor;

            // Read-only data in the current chunk
            [ReadOnly]
            public ComponentTypeHandle<Target> TargetTypeHandleAccessor;

            // Read-only data stored (potentially) in other chunks
            [ReadOnly]
            //[NativeDisableParallelForRestriction]
            public ComponentLookup<LocalToWorld> EntityPositions;

            // Non-entity data
            public float deltaTime;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                // Within Execute(), the scope of the ArchetypeChunk is limited to the current batch.
                // For example, these NativeArrays will have Length = batchInChunk.BatchEntityCount,
                // where batchInChunk.BatchEntityCount may be less than the full chunk entity count.
                NativeArray<ObjectPosition> positions = batchInChunk.GetNativeArray<ObjectPosition>(PositionTypeHandleAccessor);
                NativeArray<Target> targets = batchInChunk.GetNativeArray<Target>(TargetTypeHandleAccessor);

                for (int i = 0; i < positions.Length; i++)
                {
                    Entity targetEntity = targets[i].entity;
                    float3 targetPosition = EntityPositions[targetEntity].Position;
                    float3 chaserPosition = positions[i].Value;

                    float3 displacement = (targetPosition - chaserPosition);
                    positions[i] = new ObjectPosition { Value = chaserPosition + displacement * deltaTime };
                }
            }
        }

        protected override void OnCreate()
        {
            query = this.GetEntityQuery(typeof(ObjectPosition), ComponentType.ReadOnly<Target>());
        }

        protected override void OnUpdate()
        {
            var job = new BatchedChaserSystemJob();
            job.PositionTypeHandleAccessor = this.GetComponentTypeHandle<ObjectPosition>(false);
            job.TargetTypeHandleAccessor = this.GetComponentTypeHandle<Target>(true);

            job.EntityPositions = SystemAPI.GetComponentLookup<LocalToWorld>(true);
            job.deltaTime = SystemAPI.Time.DeltaTime;

            this.Dependency = job.ScheduleParallel(query, this.Dependency);
        }
    }
    #endregion

    public struct VelocityVector: IComponentData
    {
        public float3 Value;
    }

    #region typical-struct
    public struct UpdateTranslationFromVelocityJob : IJobEntityBatch
    {
        public ComponentTypeHandle<VelocityVector> velocityTypeHandle;
        public ComponentTypeHandle<ObjectPosition> translationTypeHandle;
        public float DeltaTime;

        [BurstCompile]
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            NativeArray<VelocityVector> velocityVectors =
                batchInChunk.GetNativeArray(velocityTypeHandle);
            NativeArray<ObjectPosition> translations =
                batchInChunk.GetNativeArray(translationTypeHandle);

            for(int i = 0; i < batchInChunk.Count; i++)
            {
                float3 translation = translations[i].Value;
                float3 velocity = velocityVectors[i].Value;
                float3 newTranslation = translation + velocity * DeltaTime;

                translations[i] = new ObjectPosition() { Value = newTranslation };
            }
        }
    }
    #endregion

    #region schedule-job
    [RequireMatchingQueriesForUpdate]
    public partial class UpdateTranslationFromVelocitySystem : SystemBase
    {
        EntityQuery query;

        protected override void OnCreate()
        {
            // Set up the query
            var description = new EntityQueryDesc()
            {
                All = new ComponentType[]
                       {ComponentType.ReadWrite<ObjectPosition>(),
                        ComponentType.ReadOnly<VelocityVector>()}
            };
            query = this.GetEntityQuery(description);
        }

        protected override void OnUpdate()
        {
            // Instantiate the job struct
            var updateFromVelocityJob
                = new UpdateTranslationFromVelocityJob();

            // Set the job component type handles
            // "this" is your SystemBase subclass
            updateFromVelocityJob.translationTypeHandle
                = this.GetComponentTypeHandle<ObjectPosition>(false);
            updateFromVelocityJob.velocityTypeHandle
                = this.GetComponentTypeHandle<VelocityVector>(true);

            // Set other data need in job, such as time
            updateFromVelocityJob.DeltaTime = World.Time.DeltaTime;

            // Schedule the job
            this.Dependency
                = updateFromVelocityJob.ScheduleParallel(query, this.Dependency);
        }
        #endregion
    }

    //For extracting one or a few lines for the docs
    public struct UpdateTranslationAndAlignToVelocityJob : IJobEntityBatch
    {
        public ComponentTypeHandle<VelocityVector> velocityTypeHandle;
        #region component-handle
        public ComponentTypeHandle<ObjectPosition> translationTypeHandle;
        #endregion
        public ComponentTypeHandle<Rotation> rotationTypeHandle;
        public ComponentTypeHandle<LocalToWorld> l2wTypeHandle;
        public float DeltaTime;

        [BurstCompile]
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            NativeArray<VelocityVector> velocityVectors =
                batchInChunk.GetNativeArray(velocityTypeHandle);
            #region component-array
            NativeArray<ObjectPosition> translations =
                batchInChunk.GetNativeArray(translationTypeHandle);
            #endregion
            for (int i = 0; i < batchInChunk.Count; i++)
            {
                float3 translation = translations[i].Value;
                float3 velocity = velocityVectors[i].Value;
                float3 newTranslation = translation + velocity * DeltaTime;

                translations[i] = new ObjectPosition() { Value = newTranslation };
            }

            #region batch-has-component
            // If entity has Rotation and LocalToWorld components,
            // slerp to align to the velocity vector
            if (batchInChunk.Has<Rotation>(rotationTypeHandle) &&
                batchInChunk.Has<LocalToWorld>(l2wTypeHandle))
            {
                NativeArray<Rotation> rotations
                    = batchInChunk.GetNativeArray(rotationTypeHandle);
                NativeArray<LocalToWorld> transforms
                    = batchInChunk.GetNativeArray(l2wTypeHandle);

                // By putting the loop inside the check for the
                // optional components, we can check once per batch
                // rather than once per entity.
                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    float3 direction = math.normalize(velocityVectors[i].Value);
                    float3 up = transforms[i].Up;
                    quaternion rotation = rotations[i].Value;

                    quaternion look = quaternion.LookRotation(direction, up);
                    quaternion newRotation = math.slerp(rotation, look, DeltaTime);

                    rotations[i] = new Rotation() { Value = newRotation };
                }
            }
            #endregion
        }
    }

    [RequireMatchingQueriesForUpdate]
    public partial class OneLinerSystem : SystemBase
    {
        EntityQuery query;

        protected override void OnCreate()
        {
            var description = new EntityQueryDesc()
            {
                All = new ComponentType[]
                       {ComponentType.ReadWrite<ObjectPosition>(),
                        ComponentType.ReadOnly<VelocityVector>()}
            };
            query = this.GetEntityQuery(description);
        }

        protected override void OnUpdate()
        {
            var updateFromVelocityJob
                = new UpdateTranslationAndAlignToVelocityJob();
            #region component-set-handle
            // "this" is your SystemBase subclass
            updateFromVelocityJob.translationTypeHandle
                = this.GetComponentTypeHandle<ObjectPosition>(false);
            #endregion
            updateFromVelocityJob.rotationTypeHandle
                = this.GetComponentTypeHandle<Rotation>(false);
            updateFromVelocityJob.velocityTypeHandle
                = this.GetComponentTypeHandle<VelocityVector>(true);
            updateFromVelocityJob.l2wTypeHandle
                = this.GetComponentTypeHandle<LocalToWorld>(true);
            updateFromVelocityJob.DeltaTime = World.Time.DeltaTime;

            this.Dependency = updateFromVelocityJob.ScheduleParallel(query, this.Dependency);
        }
    }

    #region skip-unchanged-batches-job
    struct UpdateOnChangeJob : IJobEntityBatch
    {
        public ComponentTypeHandle<InputA> InputATypeHandle;
        public ComponentTypeHandle<InputB> InputBTypeHandle;
        [ReadOnly] public ComponentTypeHandle<Output> OutputTypeHandle;
        public uint LastSystemVersion;

        [BurstCompile]
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var inputAChanged = batchInChunk.DidChange(InputATypeHandle, LastSystemVersion);
            var inputBChanged = batchInChunk.DidChange(InputBTypeHandle, LastSystemVersion);

            // If neither component changed, skip the current batch
            if (!(inputAChanged || inputBChanged))
                return;

            var inputAs = batchInChunk.GetNativeArray(InputATypeHandle);
            var inputBs = batchInChunk.GetNativeArray(InputBTypeHandle);
            var outputs = batchInChunk.GetNativeArray(OutputTypeHandle);

            for (var i = 0; i < outputs.Length; i++)
            {
                outputs[i] = new Output { Value = inputAs[i].Value + inputBs[i].Value };
            }
        }
    }
    #endregion

    #region skip-unchanged-batches-system
    [RequireMatchingQueriesForUpdate]
    public partial class UpdateDataOnChangeSystem : SystemBase {

        EntityQuery query;

        protected override void OnUpdate()
        {
            var job = new UpdateOnChangeJob();

            job.LastSystemVersion = this.LastSystemVersion;

            job.InputATypeHandle = GetComponentTypeHandle<InputA>(true);
            job.InputBTypeHandle = GetComponentTypeHandle<InputB>(true);
            job.OutputTypeHandle = GetComponentTypeHandle<Output>(false);

            this.Dependency = job.ScheduleParallel(query, this.Dependency);
        }

        protected override void OnCreate()
        {
            query = GetEntityQuery(
                new ComponentType[]
                {
                    ComponentType.ReadOnly<InputA>(),
                    ComponentType.ReadOnly<InputB>(),
                    ComponentType.ReadWrite<Output>()
                }
            );
        }
    }
    #endregion
    [RequireMatchingQueriesForUpdate]
    public partial class UpdateFilteredDataSystem : SystemBase
    {

        #region filter-query
        EntityQuery query;

        protected override void OnCreate()
        {
            query = GetEntityQuery(
                new ComponentType[]
                {
                    ComponentType.ReadOnly<InputA>(),
                    ComponentType.ReadOnly<InputB>(),
                    ComponentType.ReadWrite<Output>()
                }
            );

            query.SetChangedVersionFilter(
                    new ComponentType[]
                    {
                        typeof(InputA),
                        typeof(InputB)
                    }
                );
        }
        #endregion

        protected override void OnUpdate()
        {
            var job = new UpdateOnChangeJob();

            job.LastSystemVersion = this.LastSystemVersion;

            job.InputATypeHandle = GetComponentTypeHandle<InputA>(true);
            job.InputBTypeHandle = GetComponentTypeHandle<InputB>(true);
            job.OutputTypeHandle = GetComponentTypeHandle<Output>(false);

            this.Dependency = job.ScheduleParallel(query, this.Dependency);
        }

    }
#pragma warning restore 618 // IJobEntityBatch is obsolete
}
