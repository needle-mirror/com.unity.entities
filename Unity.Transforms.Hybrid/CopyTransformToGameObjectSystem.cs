using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine.Jobs;

namespace Unity.Transforms
{
    public class CopyTransformToGameObjectSystem : JobComponentSystem
    {
        [Inject] [ReadOnly] ComponentDataFromEntity<Position> m_Positions;
        [Inject] [ReadOnly] ComponentDataFromEntity<Rotation> m_Rotations;

        [Inject] [ReadOnly] ComponentDataFromEntity<LocalPosition> m_LocalPositions;
        [Inject] [ReadOnly] ComponentDataFromEntity<LocalRotation> m_LocalRotations;

        [BurstCompile]
        struct CopyTransforms : IJobParallelForTransform
        {
            [ReadOnly] public ComponentDataFromEntity<Position> positions;
            [ReadOnly] public ComponentDataFromEntity<Rotation> rotations;

            [ReadOnly] public ComponentDataFromEntity<LocalPosition> localPositions;
            [ReadOnly] public ComponentDataFromEntity<LocalRotation> localRotations;
            
            [ReadOnly]
            public EntityArray entities;

            public void Execute(int index, TransformAccess transform)
            {
                var entity = entities[index];

                if (localPositions.Exists(entity))
                    transform.localPosition= localPositions[entity].Value;
                else if (positions.Exists(entity))
                    transform.position = positions[entity].Value;
                    
                if (localRotations.Exists(entity))
                    transform.localRotation = localRotations[entity].Value;
                else if (rotations.Exists(entity))
                    transform.rotation = rotations[entity].Value;
            }
        }

        ComponentGroup m_TransformGroup;

        protected override void OnCreateManager(int capacity)
        {
            m_TransformGroup = GetComponentGroup(ComponentType.ReadOnly(typeof(CopyTransformToGameObject)),typeof(UnityEngine.Transform));
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var transforms = m_TransformGroup.GetTransformAccessArray();
            var entities = m_TransformGroup.GetEntityArray();

            var copyTransformsJob = new CopyTransforms
            {
                positions = m_Positions,
                rotations = m_Rotations,
                localPositions = m_LocalPositions,
                localRotations = m_LocalRotations,
                entities = entities
            };

            var resultDeps = copyTransformsJob.Schedule(transforms,inputDeps);

            return resultDeps;
        }
    }
}
