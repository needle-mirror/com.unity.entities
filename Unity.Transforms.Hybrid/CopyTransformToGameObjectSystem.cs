using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace Unity.Transforms
{
    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(EndFrameLocalToParentSystem))]
    public class CopyTransformToGameObjectSystem : JobComponentSystem
    {
        [BurstCompile]
        struct CopyTransforms : IJobParallelForTransform
        {
            [ReadOnly] public ComponentDataFromEntity<LocalToWorld> LocalToWorlds;

            [ReadOnly]
            [DeallocateOnJobCompletion]
            public NativeArray<Entity> entities;

            public void Execute(int index, TransformAccess transform)
            {
                var entity = entities[index];

                var value = LocalToWorlds[entity];
                transform.position = math.transform(value.Value, float3.zero);
                transform.rotation = new quaternion(value.Value);
            }
        }

        ComponentGroup m_TransformGroup;

        protected override void OnCreateManager()
        {
            m_TransformGroup = GetComponentGroup(ComponentType.ReadOnly(typeof(CopyTransformToGameObject)), ComponentType.ReadOnly<LocalToWorld>(), typeof(UnityEngine.Transform));
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var transforms = m_TransformGroup.GetTransformAccessArray();
            var entities = m_TransformGroup.ToEntityArray(Allocator.TempJob);

            var copyTransformsJob = new CopyTransforms
            {
                LocalToWorlds = GetComponentDataFromEntity<LocalToWorld>(true),
                entities = entities
            };

            return copyTransformsJob.Schedule(transforms,inputDeps);
        }
    }
}