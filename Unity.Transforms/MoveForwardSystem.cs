using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Mathematics.Experimental;
using Unity.Transforms;
using UnityEngine;

namespace Unity.Transforms
{
    [UpdateAfter(typeof(TransformInputBarrier))]
    [UpdateBefore(typeof(TransformSystem))]
    public class MoveForwardSystem : JobComponentSystem
    {
        [BurstCompile]
        struct MoveForwardRotation : IJobParallelFor
        {
            public ComponentDataArray<Position> positions;
            [ReadOnly] public ComponentDataArray<Rotation> rotations;
            [ReadOnly] public ComponentDataArray<MoveSpeed> moveSpeeds;
            public float dt;
        
            public void Execute(int i)
            {
                positions[i] = new Position
                {
                    Value = positions[i].Value + (dt * moveSpeeds[i].speed * math.forward(rotations[i].Value))
                };
            }
        }
        
        [BurstCompile]
        struct MoveForwardHeading : IJobParallelFor
        {
            public ComponentDataArray<Position> positions;
            [ReadOnly] public ComponentDataArray<Heading> headings;
            [ReadOnly] public ComponentDataArray<MoveSpeed> moveSpeeds;
            public float dt;
        
            public void Execute(int i)
            {
                positions[i] = new Position
                {
                    Value = positions[i].Value + (dt * moveSpeeds[i].speed * math_experimental.normalizeSafe(headings[i].Value))
                };
            }
        }
        
        ComponentGroup m_MoveForwardRotationGroup;
        ComponentGroup m_MoveForwardHeadingGroup;

        protected override void OnCreateManager(int capacity)
        {
            m_MoveForwardRotationGroup = GetComponentGroup(
                ComponentType.ReadOnly(typeof(MoveForward)),
                ComponentType.ReadOnly(typeof(Rotation)),
                ComponentType.ReadOnly(typeof(MoveSpeed)),
                typeof(Position));
            
            m_MoveForwardHeadingGroup = GetComponentGroup(
                ComponentType.ReadOnly(typeof(MoveForward)),
                ComponentType.Subtractive(typeof(Rotation)),
                ComponentType.ReadOnly(typeof(Heading)),
                ComponentType.ReadOnly(typeof(MoveSpeed)),
                typeof(Position));
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var moveForwardRotationJob = new MoveForwardRotation
            {
                positions = m_MoveForwardRotationGroup.GetComponentDataArray<Position>(),
                rotations = m_MoveForwardRotationGroup.GetComponentDataArray<Rotation>(),
                moveSpeeds = m_MoveForwardRotationGroup.GetComponentDataArray<MoveSpeed>(),
                dt = Time.deltaTime
            };
            var moveForwardRotationJobHandle = moveForwardRotationJob.Schedule(m_MoveForwardRotationGroup.CalculateLength(), 64, inputDeps);
            
            var moveForwardHeadingJob = new MoveForwardHeading
            {
                positions = m_MoveForwardHeadingGroup.GetComponentDataArray<Position>(),
                headings = m_MoveForwardHeadingGroup.GetComponentDataArray<Heading>(),
                moveSpeeds = m_MoveForwardHeadingGroup.GetComponentDataArray<MoveSpeed>(),
                dt = Time.deltaTime
            };
            var moveForwardHeadingJobHandle = moveForwardHeadingJob.Schedule(m_MoveForwardHeadingGroup.CalculateLength(), 64, inputDeps);
            
            return JobHandle.CombineDependencies(moveForwardHeadingJobHandle,moveForwardRotationJobHandle);
        }
    }
}
