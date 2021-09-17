using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine.Jobs;

namespace Unity.Transforms
{
    [UnityEngine.ExecuteAlways]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateBefore(typeof(TRSToLocalToWorldSystem))]
    public partial class CopyTransformFromGameObjectSystem : SystemBase
    {
        struct TransformStash
        {
            public float3 position;
            public quaternion rotation;
        }

        [BurstCompile]
        struct StashTransforms : IJobParallelForTransform
        {
            public NativeArray<TransformStash> transformStashes;

            public void Execute(int index, TransformAccess transform)
            {
                transformStashes[index] = new TransformStash
                {
                    rotation       = transform.rotation,
                    position       = transform.position,
                };
            }
        }

        [BurstCompile]
        partial struct CopyTransforms : IJobEntity
        {
            [DeallocateOnJobCompletion] public NativeArray<TransformStash> transformStashes;

            public void Execute([EntityInQueryIndex]int entityInQueryIndex, ref LocalToWorld localToWorld)
            {
                var transformStash = transformStashes[entityInQueryIndex];

                localToWorld = new LocalToWorld
                {
                    Value = float4x4.TRS(
                        transformStash.position,
                        transformStash.rotation,
                        new float3(1.0f, 1.0f, 1.0f))
                };
            }
        }

        EntityQuery m_TransformGroup;

        protected override void OnCreate()
        {
            m_TransformGroup = GetEntityQuery(
                ComponentType.ReadOnly(typeof(CopyTransformFromGameObject)),
                typeof(UnityEngine.Transform),
                ComponentType.ReadWrite<LocalToWorld>());

            //@TODO this should not be required, see https://github.com/Unity-Technologies/dots/issues/1122
            RequireForUpdate(m_TransformGroup);
        }

        protected override void OnUpdate()
        {
            var transforms = m_TransformGroup.GetTransformAccessArray();
            var transformStashes = new NativeArray<TransformStash>(transforms.length, Allocator.TempJob);
            Dependency = new StashTransforms {transformStashes = transformStashes}.Schedule(transforms, Dependency);
            new CopyTransforms {transformStashes = transformStashes}.Schedule(m_TransformGroup);
        }
    }
}
