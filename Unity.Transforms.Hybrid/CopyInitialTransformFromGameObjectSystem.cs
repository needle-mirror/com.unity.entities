using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace Unity.Transforms
{
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class CopyInitialTransformFromGameObjectSystem : SystemBase
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

        struct RemoveCopyInitialTransformFromGameObjectComponent : IJob
        {
            [DeallocateOnJobCompletion][ReadOnly] public NativeArray<Entity> entities;
            public EntityCommandBuffer entityCommandBuffer;

            public void Execute()
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    entityCommandBuffer.RemoveComponent<CopyInitialTransformFromGameObject>(entities[i]);
                }
            }
        }

        EndInitializationEntityCommandBufferSystem m_EntityCommandBufferSystem;

        EntityQuery m_InitialTransformGroup;

        protected override void OnCreate()
        {
            m_EntityCommandBufferSystem = World.GetOrCreateSystem<EndInitializationEntityCommandBufferSystem>();
            m_InitialTransformGroup = GetEntityQuery(
                ComponentType.ReadOnly(typeof(CopyInitialTransformFromGameObject)),
                typeof(UnityEngine.Transform),
                ComponentType.ReadWrite<LocalToWorld>());
        }

        protected override void OnUpdate()
        {
            var transforms = m_InitialTransformGroup.GetTransformAccessArray();
            var entities = m_InitialTransformGroup.ToEntityArray(Allocator.TempJob);

            var transformStashes = new NativeArray<TransformStash>(transforms.length, Allocator.TempJob);
            Dependency = new StashTransforms{transformStashes = transformStashes}.Schedule(transforms, Dependency);
            new CopyTransforms{transformStashes = transformStashes}.Schedule(m_InitialTransformGroup);
            Dependency = new RemoveCopyInitialTransformFromGameObjectComponent
            {
                entities = entities,
                entityCommandBuffer = m_EntityCommandBufferSystem.CreateCommandBuffer()
            }.Schedule(Dependency);

            m_EntityCommandBufferSystem.AddJobHandleForProducer(Dependency);
        }
    }
}
