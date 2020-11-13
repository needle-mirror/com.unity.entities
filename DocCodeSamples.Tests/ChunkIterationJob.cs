using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using NUnit.Framework;

// Only used in obsolete IJobChunk docs -- do not update to use IJobEntityBatch
// The files in this namespace are used to compile/test the code samples in the documentation.
namespace Doc.CodeSamples.Tests
{
    //Snippets used in chunk_iteration_job.md
    public struct Rotation : IComponentData
    {
        public quaternion Value;
    }

    public struct RotationSpeed : IComponentData
    {
        public float RadiansPerSecond;
    }

    #region rotationspeedsystem

    public class RotationSpeedSystem : SystemBase
    {
        private EntityQuery m_Query;

        protected override void OnCreate()
        {
            m_Query = GetEntityQuery(ComponentType.ReadOnly<Rotation>(),
                ComponentType.ReadOnly<RotationSpeed>());
            //...
        }

        #endregion

        [BurstCompile]
        struct RotationSpeedJob : IJobChunk
        {
            public float DeltaTime;
            public ComponentTypeHandle<Rotation> RotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<RotationSpeed> RotationSpeedTypeHandle;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                #region chunkiteration

                var chunkRotations = chunk.GetNativeArray(RotationTypeHandle);
                var chunkRotationSpeeds = chunk.GetNativeArray(RotationSpeedTypeHandle);
                for (var i = 0; i < chunk.Count; i++)
                {
                    var rotation = chunkRotations[i];
                    var rotationSpeed = chunkRotationSpeeds[i];

                    // Rotate something about its up vector at the speed given by RotationSpeed.
                    chunkRotations[i] = new Rotation
                    {
                        Value = math.mul(math.normalize(rotation.Value),
                            quaternion.AxisAngle(math.up(), rotationSpeed.RadiansPerSecond * DeltaTime))
                    };
                }

                #endregion
            }
        }

        #region schedulequery

        protected override void OnUpdate()
        {
            var job = new RotationSpeedJob()
            {
                RotationTypeHandle = GetComponentTypeHandle<Rotation>(false),
                RotationSpeedTypeHandle = GetComponentTypeHandle<RotationSpeed>(true),
                DeltaTime = Time.DeltaTime
            };
            this.Dependency =  job.ScheduleParallel(m_Query, this.Dependency);
        }

        #endregion
    }


    public class RotationSpeedSystemExample2 : SystemBase
    {
        private EntityQuery m_Query;

        #region oncreate2

        protected override void OnCreate()
        {
            var queryDescription = new EntityQueryDesc()
            {
                None = new ComponentType[]
                {
                    typeof(Static)
                },
                All = new ComponentType[]
                {
                    ComponentType.ReadWrite<Rotation>(),
                    ComponentType.ReadOnly<RotationSpeed>()
                }
            };
            m_Query = GetEntityQuery(queryDescription);
        }

        #endregion

        #region speedjob

        [BurstCompile]
        struct RotationSpeedJob : IJobChunk
        {
            public float DeltaTime;
            public ComponentTypeHandle<Rotation> RotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<RotationSpeed> RotationSpeedTypeHandle;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                // ...
            }
        }

        #endregion

        protected override void OnUpdate()
        {
            var job = new RotationSpeedJob()
            {
                RotationTypeHandle = GetComponentTypeHandle<Rotation>(false),
                RotationSpeedTypeHandle = GetComponentTypeHandle<RotationSpeed>(true),
                DeltaTime = Time.DeltaTime
            };

            this.Dependency =  job.ScheduleParallel(m_Query, this.Dependency);
        }
    }

    public class RotationSpeedSystemExample3 : SystemBase
    {
        private EntityQuery m_Query;

        #region oncreate3

        protected override void OnCreate()
        {
            var queryDescription0 = new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(Rotation)}
            };

            var queryDescription1 = new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(RotationSpeed)}
            };

            m_Query = GetEntityQuery(new EntityQueryDesc[] {queryDescription0, queryDescription1});
        }

        #endregion

        [BurstCompile]
        struct RotationSpeedJob : IJobChunk
        {
            public float DeltaTime;
            public ComponentTypeHandle<Rotation> RotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<RotationSpeed> RotationSpeedTypeHandle;

            #region execsignature

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)

                #endregion
            {
                #region getcomponents

                var chunkRotations = chunk.GetNativeArray(RotationTypeHandle);
                var chunkRotationSpeeds = chunk.GetNativeArray(RotationSpeedTypeHandle);

                #endregion
            }
        }

        protected override void OnUpdate()
        {
            var job = new RotationSpeedJob()
            {
                RotationTypeHandle = GetComponentTypeHandle<Rotation>(false),
                RotationSpeedTypeHandle = GetComponentTypeHandle<RotationSpeed>(true),
                DeltaTime = Time.DeltaTime
            };

            this.Dependency = job.ScheduleParallel(m_Query, this.Dependency);
        }
    }

    public struct Output : IComponentData
    {
        public float Value;
    }

    public struct InputA : IComponentData
    {
        public float Value;
    }

    public struct InputB : IComponentData
    {
        public float Value;
    }

    public class UpdateSystemExample : SystemBase
    {
        #region changefilter

        private EntityQuery m_Query;

        protected override void OnCreate()
        {
            m_Query = GetEntityQuery(
                ComponentType.ReadWrite<Output>(),
                ComponentType.ReadOnly<InputA>(),
                ComponentType.ReadOnly<InputB>());
            m_Query.SetChangedVersionFilter(
                new ComponentType[]
                {
                    ComponentType.ReadWrite<InputA>(),
                    ComponentType.ReadWrite<InputB>()
                });
        }

        #endregion

        #region changefilterjobstruct

        [BurstCompile]
        struct UpdateJob : IJobChunk
        {
            public ComponentTypeHandle<InputA> InputATypeHandle;
            public ComponentTypeHandle<InputB> InputBTypeHandle;
            [ReadOnly] public ComponentTypeHandle<Output> OutputTypeHandle;
            public uint LastSystemVersion;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var inputAChanged = chunk.DidChange(InputATypeHandle, LastSystemVersion);
                var inputBChanged = chunk.DidChange(InputBTypeHandle, LastSystemVersion);

                // If neither component changed, skip the current chunk
                if (!(inputAChanged || inputBChanged))
                    return;

                var inputAs = chunk.GetNativeArray(InputATypeHandle);
                var inputBs = chunk.GetNativeArray(InputBTypeHandle);
                var outputs = chunk.GetNativeArray(OutputTypeHandle);

                for (var i = 0; i < outputs.Length; i++)
                {
                    outputs[i] = new Output { Value = inputAs[i].Value + inputBs[i].Value };
                }
            }
        }

        #endregion

        #region changefilteronupdate

        protected override void OnUpdate()
        {
            var job = new UpdateJob();

            job.LastSystemVersion = this.LastSystemVersion;

            job.InputATypeHandle = GetComponentTypeHandle<InputA>(true);
            job.InputBTypeHandle = GetComponentTypeHandle<InputB>(true);
            job.OutputTypeHandle = GetComponentTypeHandle<Output>(false);

            this.Dependency = job.ScheduleParallel(m_Query, this.Dependency);
        }

        #endregion
    }

    #region basic-ijobchunk

    [GenerateAuthoringComponent]
    public struct Target : IComponentData
    {
        public Entity entity;
    }

    public class ChaserSystem : SystemBase
    {
        private EntityQuery query; // Initialized in Oncreate()

        [BurstCompile]
        private struct ChaserSystemJob : IJobChunk
        {
            // Read-write data in the current chunk
            public ComponentTypeHandle<Translation> PositionTypeHandle;

            // Read-only data in the current chunk
            [ReadOnly]
            public ComponentTypeHandle<Target> TargetTypeHandle;

            // Read-only data stored (potentially) in other chunks
            [ReadOnly]
            //[NativeDisableParallelForRestriction]
            public ComponentDataFromEntity<LocalToWorld> EntityPositions;

            // Non-entity data
            public float deltaTime;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                NativeArray<Translation> positions = chunk.GetNativeArray<Translation>(PositionTypeHandle);
                NativeArray<Target> targets = chunk.GetNativeArray<Target>(TargetTypeHandle);

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
            var job = new ChaserSystemJob();
            job.PositionTypeHandle = this.GetComponentTypeHandle<Translation>(false);
            job.TargetTypeHandle = this.GetComponentTypeHandle<Target>(true);

            job.EntityPositions = this.GetComponentDataFromEntity<LocalToWorld>(true);
            job.deltaTime = this.Time.DeltaTime;

            this.Dependency = job.Schedule(query, this.Dependency);
        }
    }
    #endregion

}
