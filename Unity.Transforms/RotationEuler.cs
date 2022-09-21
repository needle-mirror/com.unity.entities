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
    [WriteGroup(typeof(Rotation))]
    public struct RotationEulerXYZ : IComponentData
    {
        public float3 Value;
    }

    [Serializable]
    [WriteGroup(typeof(Rotation))]
    public struct RotationEulerXZY : IComponentData
    {
        public float3 Value;
    }

    [Serializable]
    [WriteGroup(typeof(Rotation))]
    public struct RotationEulerYXZ : IComponentData
    {
        public float3 Value;
    }

    [Serializable]
    [WriteGroup(typeof(Rotation))]
    public struct RotationEulerYZX : IComponentData
    {
        public float3 Value;
    }

    [Serializable]
    [WriteGroup(typeof(Rotation))]
    public struct RotationEulerZXY : IComponentData
    {
        public float3 Value;
    }

    [Serializable]
    [WriteGroup(typeof(Rotation))]
    public struct RotationEulerZYX : IComponentData
    {
        public float3 Value;
    }

    // Rotation = RotationEulerXYZ
    // (or) Rotation = RotationEulerXZY
    // (or) Rotation = RotationEulerYXZ
    // (or) Rotation = RotationEulerYZX
    // (or) Rotation = RotationEulerZXY
    // (or) Rotation = RotationEulerZYX
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    public partial struct RotationEulerSystem : ISystem
    {
        private EntityQuery m_Query;

        public ComponentTypeHandle<RotationEulerZYX> RotationEulerZyxType;
        public ComponentTypeHandle<RotationEulerZXY> RotationEulerZxyType;
        public ComponentTypeHandle<RotationEulerYZX> RotationEulerYzxType;
        public ComponentTypeHandle<RotationEulerYXZ> RotationEulerYxzType;
        public ComponentTypeHandle<RotationEulerXZY> RotationEulerXzyType;
        public ComponentTypeHandle<RotationEulerXYZ> RotationEulerXyzType;
        public ComponentTypeHandle<Rotation> RotationType;

        //burst disabled pending burstable entityquerydesc
        //[BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_Query = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    typeof(Rotation)
                },
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<RotationEulerXYZ>(),
                    ComponentType.ReadOnly<RotationEulerXZY>(),
                    ComponentType.ReadOnly<RotationEulerYXZ>(),
                    ComponentType.ReadOnly<RotationEulerYZX>(),
                    ComponentType.ReadOnly<RotationEulerZXY>(),
                    ComponentType.ReadOnly<RotationEulerZYX>()
                },
                Options = EntityQueryOptions.FilterWriteGroup
            });

            RotationType = state.GetComponentTypeHandle<Rotation>(false);
            RotationEulerXyzType = state.GetComponentTypeHandle<RotationEulerXYZ>(true);
            RotationEulerXzyType = state.GetComponentTypeHandle<RotationEulerXZY>(true);
            RotationEulerYxzType = state.GetComponentTypeHandle<RotationEulerYXZ>(true);
            RotationEulerYzxType = state.GetComponentTypeHandle<RotationEulerYZX>(true);
            RotationEulerZxyType = state.GetComponentTypeHandle<RotationEulerZXY>(true);
            RotationEulerZyxType = state.GetComponentTypeHandle<RotationEulerZYX>(true);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        struct RotationEulerToRotation : IJobChunk
        {
            public ComponentTypeHandle<Rotation> RotationTypeHandle;
            [ReadOnly] public ComponentTypeHandle<RotationEulerXYZ> RotationEulerXyzTypeHandle;
            [ReadOnly] public ComponentTypeHandle<RotationEulerXZY> RotationEulerXzyTypeHandle;
            [ReadOnly] public ComponentTypeHandle<RotationEulerYXZ> RotationEulerYxzTypeHandle;
            [ReadOnly] public ComponentTypeHandle<RotationEulerYZX> RotationEulerYzxTypeHandle;
            [ReadOnly] public ComponentTypeHandle<RotationEulerZXY> RotationEulerZxyTypeHandle;
            [ReadOnly] public ComponentTypeHandle<RotationEulerZYX> RotationEulerZyxTypeHandle;
            public uint LastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);

                if (chunk.Has(RotationEulerXyzTypeHandle))
                {
                    if (!chunk.DidChange(RotationEulerXyzTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = chunk.GetNativeArray(RotationTypeHandle);
                    var chunkRotationEulerXYZs = chunk.GetNativeArray(RotationEulerXyzTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        chunkRotations[i] = new Rotation
                        {
                            Value = quaternion.EulerXYZ(chunkRotationEulerXYZs[i].Value)
                        };
                    }
                }
                else if (chunk.Has(RotationEulerXzyTypeHandle))
                {
                    if (!chunk.DidChange(RotationEulerXzyTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = chunk.GetNativeArray(RotationTypeHandle);
                    var chunkRotationEulerXZYs = chunk.GetNativeArray(RotationEulerXzyTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        chunkRotations[i] = new Rotation
                        {
                            Value = quaternion.EulerXZY(chunkRotationEulerXZYs[i].Value)
                        };
                    }
                }
                else if (chunk.Has(RotationEulerYxzTypeHandle))
                {
                    if (!chunk.DidChange(RotationEulerYxzTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = chunk.GetNativeArray(RotationTypeHandle);
                    var chunkRotationEulerYXZs = chunk.GetNativeArray(RotationEulerYxzTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        chunkRotations[i] = new Rotation
                        {
                            Value = quaternion.EulerYXZ(chunkRotationEulerYXZs[i].Value)
                        };
                    }
                }
                else if (chunk.Has(RotationEulerYzxTypeHandle))
                {
                    if (!chunk.DidChange(RotationEulerYzxTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = chunk.GetNativeArray(RotationTypeHandle);
                    var chunkRotationEulerYZXs = chunk.GetNativeArray(RotationEulerYzxTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        chunkRotations[i] = new Rotation
                        {
                            Value = quaternion.EulerYZX(chunkRotationEulerYZXs[i].Value)
                        };
                    }
                }
                else if (chunk.Has(RotationEulerZxyTypeHandle))
                {
                    if (!chunk.DidChange(RotationEulerZxyTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = chunk.GetNativeArray(RotationTypeHandle);
                    var chunkRotationEulerZXYs = chunk.GetNativeArray(RotationEulerZxyTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        chunkRotations[i] = new Rotation
                        {
                            Value = quaternion.EulerZXY(chunkRotationEulerZXYs[i].Value)
                        };
                    }
                }
                else if (chunk.Has(RotationEulerZyxTypeHandle))
                {
                    if (!chunk.DidChange(RotationEulerZyxTypeHandle, LastSystemVersion))
                        return;

                    var chunkRotations = chunk.GetNativeArray(RotationTypeHandle);
                    var chunkRotationEulerZYXs = chunk.GetNativeArray(RotationEulerZyxTypeHandle);
                    for (int i = 0, chunkEntityCount = chunk.Count; i < chunkEntityCount; i++)
                    {
                        chunkRotations[i] = new Rotation
                        {
                            Value = quaternion.EulerZYX(chunkRotationEulerZYXs[i].Value)
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
            RotationEulerZyxType.Update(ref state);
            RotationEulerZxyType.Update(ref state);
            RotationEulerYzxType.Update(ref state);
            RotationEulerYxzType.Update(ref state);
            RotationEulerXzyType.Update(ref state);
            RotationEulerXyzType.Update(ref state);
            RotationType.Update(ref state);

            var job = new RotationEulerToRotation()
            {
                RotationTypeHandle = RotationType,
                RotationEulerXyzTypeHandle = RotationEulerXyzType,
                RotationEulerXzyTypeHandle = RotationEulerXzyType,
                RotationEulerYxzTypeHandle = RotationEulerYxzType,
                RotationEulerYzxTypeHandle = RotationEulerYzxType,
                RotationEulerZxyTypeHandle = RotationEulerZxyType,
                RotationEulerZyxTypeHandle = RotationEulerZyxType,
                LastSystemVersion = state.LastSystemVersion
            };
            state.Dependency = job.ScheduleParallel(m_Query, state.Dependency);
        }
    }
}

#endif
