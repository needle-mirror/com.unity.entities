using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

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
    public partial struct PostRotationEulerSystem : ISystem
    {
        private EntityQuery m_Query;

        //burst disabled pending burstable entityquerydesc
        //[BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_Query = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    typeof(PostRotation)
                },
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<PostRotationEulerXYZ>(),
                    ComponentType.ReadOnly<PostRotationEulerXZY>(),
                    ComponentType.ReadOnly<PostRotationEulerYXZ>(),
                    ComponentType.ReadOnly<PostRotationEulerYZX>(),
                    ComponentType.ReadOnly<PostRotationEulerZXY>(),
                    ComponentType.ReadOnly<PostRotationEulerZYX>()
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct PostRotationEulerToPostRotation : IJobEntityBatch
        {
            public ComponentTypeHandle<PostRotation> PostRotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PostRotationEulerXYZ> PostRotationEulerXyzTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PostRotationEulerXZY> PostRotationEulerXzyTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PostRotationEulerYXZ> PostRotationEulerYxzTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PostRotationEulerYZX> PostRotationEulerYzxTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PostRotationEulerZXY> PostRotationEulerZxyTypeHandle;
            [ReadOnly] public ComponentTypeHandle<PostRotationEulerZYX> PostRotationEulerZyxTypeHandle;
            public uint LastSystemVersion;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                if (batchInChunk.Has(PostRotationEulerXyzTypeHandle))
                {
                    if (!batchInChunk.DidChange(PostRotationEulerXyzTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = batchInChunk.GetNativeArray(PostRotationTypeHandle);
                    var chunkPostRotationEulerXYZs = batchInChunk.GetNativeArray(PostRotationEulerXyzTypeHandle);
                    for (var i = 0; i < batchInChunk.Count; i++)
                    {
                        chunkRotations[i] = new PostRotation
                        {
                            Value = quaternion.EulerXYZ(chunkPostRotationEulerXYZs[i].Value)
                        };
                    }
                }
                else if (batchInChunk.Has(PostRotationEulerXzyTypeHandle))
                {
                    if (!batchInChunk.DidChange(PostRotationEulerXzyTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = batchInChunk.GetNativeArray(PostRotationTypeHandle);
                    var chunkPostRotationEulerXZYs = batchInChunk.GetNativeArray(PostRotationEulerXzyTypeHandle);
                    for (var i = 0; i < batchInChunk.Count; i++)
                    {
                        chunkRotations[i] = new PostRotation
                        {
                            Value = quaternion.EulerXZY(chunkPostRotationEulerXZYs[i].Value)
                        };
                    }
                }
                else if (batchInChunk.Has(PostRotationEulerYxzTypeHandle))
                {
                    if (!batchInChunk.DidChange(PostRotationEulerYxzTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = batchInChunk.GetNativeArray(PostRotationTypeHandle);
                    var chunkPostRotationEulerYXZs = batchInChunk.GetNativeArray(PostRotationEulerYxzTypeHandle);
                    for (var i = 0; i < batchInChunk.Count; i++)
                    {
                        chunkRotations[i] = new PostRotation
                        {
                            Value = quaternion.EulerYXZ(chunkPostRotationEulerYXZs[i].Value)
                        };
                    }
                }
                else if (batchInChunk.Has(PostRotationEulerYzxTypeHandle))
                {
                    if (!batchInChunk.DidChange(PostRotationEulerYzxTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = batchInChunk.GetNativeArray(PostRotationTypeHandle);
                    var chunkPostRotationEulerYZXs = batchInChunk.GetNativeArray(PostRotationEulerYzxTypeHandle);
                    for (var i = 0; i < batchInChunk.Count; i++)
                    {
                        chunkRotations[i] = new PostRotation
                        {
                            Value = quaternion.EulerYZX(chunkPostRotationEulerYZXs[i].Value)
                        };
                    }
                }
                else if (batchInChunk.Has(PostRotationEulerZxyTypeHandle))
                {
                    if (!batchInChunk.DidChange(PostRotationEulerZxyTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = batchInChunk.GetNativeArray(PostRotationTypeHandle);
                    var chunkPostRotationEulerZXYs = batchInChunk.GetNativeArray(PostRotationEulerZxyTypeHandle);
                    for (var i = 0; i < batchInChunk.Count; i++)
                    {
                        chunkRotations[i] = new PostRotation
                        {
                            Value = quaternion.EulerZXY(chunkPostRotationEulerZXYs[i].Value)
                        };
                    }
                }
                else if (batchInChunk.Has(PostRotationEulerZyxTypeHandle))
                {
                    if (!batchInChunk.DidChange(PostRotationEulerZyxTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = batchInChunk.GetNativeArray(PostRotationTypeHandle);
                    var chunkPostRotationEulerZYXs = batchInChunk.GetNativeArray(PostRotationEulerZyxTypeHandle);
                    for (var i = 0; i < batchInChunk.Count; i++)
                    {
                        chunkRotations[i] = new PostRotation
                        {
                            Value = quaternion.EulerZYX(chunkPostRotationEulerZYXs[i].Value)
                        };
                    }
                }
            }
        }

        //disabling burst in dotsrt until burstable scheduling works
#if !UNITY_DOTSRUNTIME
        [BurstCompile]
#endif
        public void OnUpdate(ref SystemState state)
        {
            var job = new PostRotationEulerToPostRotation()
            {
                PostRotationTypeHandle = state.GetComponentTypeHandle<PostRotation>(false),
                PostRotationEulerXyzTypeHandle = state.GetComponentTypeHandle<PostRotationEulerXYZ>(true),
                PostRotationEulerXzyTypeHandle = state.GetComponentTypeHandle<PostRotationEulerXZY>(true),
                PostRotationEulerYxzTypeHandle = state.GetComponentTypeHandle<PostRotationEulerYXZ>(true),
                PostRotationEulerYzxTypeHandle = state.GetComponentTypeHandle<PostRotationEulerYZX>(true),
                PostRotationEulerZxyTypeHandle = state.GetComponentTypeHandle<PostRotationEulerZXY>(true),
                PostRotationEulerZyxTypeHandle = state.GetComponentTypeHandle<PostRotationEulerZYX>(true),
                LastSystemVersion = state.LastSystemVersion
            };
            state.Dependency = job.ScheduleParallel(m_Query, state.Dependency);
        }
    }
}
