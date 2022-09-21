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

/* **************
   COPY AND PASTE
   **************
 * PostRotationEuler.cs and RotationEuler.cs are copy-and-paste.
 * Any changes to one must be copied to the other.
 * The only differences are:
 *   s/PostRotation/Rotation/g
*/

namespace Unity.Transforms
{
    [Serializable]
    [WriteGroup(typeof(PostRotation))]
    public struct PostRotationEulerXYZ : IComponentData
    {
        public float3 Value;
    }

    [Serializable]
    [WriteGroup(typeof(PostRotation))]
    public struct PostRotationEulerXZY : IComponentData
    {
        public float3 Value;
    }

    [Serializable]
    [WriteGroup(typeof(PostRotation))]
    public struct PostRotationEulerYXZ : IComponentData
    {
        public float3 Value;
    }

    [Serializable]
    [WriteGroup(typeof(PostRotation))]
    public struct PostRotationEulerYZX : IComponentData
    {
        public float3 Value;
    }

    [Serializable]
    [WriteGroup(typeof(PostRotation))]
    public struct PostRotationEulerZXY : IComponentData
    {
        public float3 Value;
    }

    [Serializable]
    [WriteGroup(typeof(PostRotation))]
    public struct PostRotationEulerZYX : IComponentData
    {
        public float3 Value;
    }

    // PostRotation = PostRotationEulerXYZ
    // (or) PostRotation = PostRotationEulerXZY
    // (or) PostRotation = PostRotationEulerYXZ
    // (or) PostRotation = PostRotationEulerYZX
    // (or) PostRotation = PostRotationEulerZXY
    // (or) PostRotation = PostRotationEulerZYX
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    public partial struct PostRotationEulerSystem : ISystem
    {
        private EntityQuery m_Query;

        public ComponentTypeHandle<PostRotationEulerZYX> PostRotationEulerZyxType;
        public ComponentTypeHandle<PostRotationEulerZXY> PostRotationEulerZxyType;
        public ComponentTypeHandle<PostRotationEulerYZX> PostRotationEulerYzxType;
        public ComponentTypeHandle<PostRotationEulerYXZ> PostRotationEulerYxzType;
        public ComponentTypeHandle<PostRotationEulerXZY> PostRotationEulerXzyType;
        public ComponentTypeHandle<PostRotationEulerXYZ> PostRotationEulerXyzType;
        public ComponentTypeHandle<PostRotation> PostRotationType;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            var builder = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<PostRotation>()
                .WithAny<PostRotationEulerXYZ>()
                .WithAny<PostRotationEulerXZY>()
                .WithAny<PostRotationEulerYXZ>()
                .WithAny<PostRotationEulerYZX>()
                .WithAny<PostRotationEulerZXY>()
                .WithAny<PostRotationEulerZYX>()
                .WithOptions(EntityQueryOptions.FilterWriteGroup);
            m_Query = state.GetEntityQuery(builder);

            PostRotationType = state.GetComponentTypeHandle<PostRotation>(false);
            PostRotationEulerXyzType = state.GetComponentTypeHandle<PostRotationEulerXYZ>(true);
            PostRotationEulerXzyType = state.GetComponentTypeHandle<PostRotationEulerXZY>(true);
            PostRotationEulerYxzType = state.GetComponentTypeHandle<PostRotationEulerYXZ>(true);
            PostRotationEulerYzxType = state.GetComponentTypeHandle<PostRotationEulerYZX>(true);
            PostRotationEulerZxyType = state.GetComponentTypeHandle<PostRotationEulerZXY>(true);
            PostRotationEulerZyxType = state.GetComponentTypeHandle<PostRotationEulerZYX>(true);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct PostRotationEulerToPostRotation : IJobChunk
        {
            public ComponentTypeHandle<PostRotation> PostRotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PostRotationEulerXYZ> PostRotationEulerXyzTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PostRotationEulerXZY> PostRotationEulerXzyTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PostRotationEulerYXZ> PostRotationEulerYxzTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PostRotationEulerYZX> PostRotationEulerYzxTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PostRotationEulerZXY> PostRotationEulerZxyTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PostRotationEulerZYX> PostRotationEulerZyxTypeHandle;
            public uint LastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);

                if (chunk.Has(PostRotationEulerXyzTypeHandle))
                {
                    if (!chunk.DidChange(PostRotationEulerXyzTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = chunk.GetNativeArray(PostRotationTypeHandle);
                    var chunkPostRotationEulerXYZs = chunk.GetNativeArray(PostRotationEulerXyzTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        chunkRotations[i] = new PostRotation
                        {
                            Value = quaternion.EulerXYZ(chunkPostRotationEulerXYZs[i].Value)
                        };
                    }
                }
                else if (chunk.Has(PostRotationEulerXzyTypeHandle))
                {
                    if (!chunk.DidChange(PostRotationEulerXzyTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = chunk.GetNativeArray(PostRotationTypeHandle);
                    var chunkPostRotationEulerXZYs = chunk.GetNativeArray(PostRotationEulerXzyTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        chunkRotations[i] = new PostRotation
                        {
                            Value = quaternion.EulerXZY(chunkPostRotationEulerXZYs[i].Value)
                        };
                    }
                }
                else if (chunk.Has(PostRotationEulerYxzTypeHandle))
                {
                    if (!chunk.DidChange(PostRotationEulerYxzTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = chunk.GetNativeArray(PostRotationTypeHandle);
                    var chunkPostRotationEulerYXZs = chunk.GetNativeArray(PostRotationEulerYxzTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        chunkRotations[i] = new PostRotation
                        {
                            Value = quaternion.EulerYXZ(chunkPostRotationEulerYXZs[i].Value)
                        };
                    }
                }
                else if (chunk.Has(PostRotationEulerYzxTypeHandle))
                {
                    if (!chunk.DidChange(PostRotationEulerYzxTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = chunk.GetNativeArray(PostRotationTypeHandle);
                    var chunkPostRotationEulerYZXs = chunk.GetNativeArray(PostRotationEulerYzxTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        chunkRotations[i] = new PostRotation
                        {
                            Value = quaternion.EulerYZX(chunkPostRotationEulerYZXs[i].Value)
                        };
                    }
                }
                else if (chunk.Has(PostRotationEulerZxyTypeHandle))
                {
                    if (!chunk.DidChange(PostRotationEulerZxyTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = chunk.GetNativeArray(PostRotationTypeHandle);
                    var chunkPostRotationEulerZXYs = chunk.GetNativeArray(PostRotationEulerZxyTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        chunkRotations[i] = new PostRotation
                        {
                            Value = quaternion.EulerZXY(chunkPostRotationEulerZXYs[i].Value)
                        };
                    }
                }
                else if (chunk.Has(PostRotationEulerZyxTypeHandle))
                {
                    if (!chunk.DidChange(PostRotationEulerZyxTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = chunk.GetNativeArray(PostRotationTypeHandle);
                    var chunkPostRotationEulerZYXs = chunk.GetNativeArray(PostRotationEulerZyxTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        chunkRotations[i] = new PostRotation
                        {
                            Value = quaternion.EulerZYX(chunkPostRotationEulerZYXs[i].Value)
                        };
                    }
                }
            }
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            PostRotationEulerZyxType.Update(ref state);
            PostRotationEulerZxyType.Update(ref state);
            PostRotationEulerYzxType.Update(ref state);
            PostRotationEulerYxzType.Update(ref state);
            PostRotationEulerXzyType.Update(ref state);
            PostRotationEulerXyzType.Update(ref state);
            PostRotationType.Update(ref state);

            var job = new PostRotationEulerToPostRotation()
            {
                PostRotationTypeHandle = PostRotationType,
                PostRotationEulerXyzTypeHandle = PostRotationEulerXyzType,
                PostRotationEulerXzyTypeHandle = PostRotationEulerXzyType,
                PostRotationEulerYxzTypeHandle = PostRotationEulerYxzType,
                PostRotationEulerYzxTypeHandle = PostRotationEulerYzxType,
                PostRotationEulerZxyTypeHandle = PostRotationEulerZxyType,
                PostRotationEulerZyxTypeHandle = PostRotationEulerZyxType,
                LastSystemVersion = state.LastSystemVersion
            };
            state.Dependency = job.ScheduleParallel(m_Query, state.Dependency);
        }
    }
}

#endif
