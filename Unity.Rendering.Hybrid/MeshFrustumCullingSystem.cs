using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace Unity.Rendering
{
    [UnityEngine.ExecuteInEditMode]
    [UpdateAfter(typeof(MeshFrustumCullingSystem))]
    public class MeshCullingBarrier : BarrierSystem
    {
    }

    [UnityEngine.ExecuteInEditMode]
    public class MeshFrustumCullingSystem : JobComponentSystem
    {
        struct BoundingSphere
        {
            public ComponentDataArray<MeshCullingComponent> sphere;
            public ComponentDataArray<TransformMatrix> transform;
            public EntityArray entities;
            public int Length;
        }

        [BurstCompile]
        struct TransformCenterJob : IJobParallelForBatch
        {
            [NativeDisableParallelForRestriction]
            public NativeArray<float4> output;
            [NativeDisableParallelForRestriction]
            public NativeArray<float4> oldCullStatus;

            [ReadOnly]
            public ComponentDataArray<MeshCullingComponent> sphere;
            [ReadOnly]
            public ComponentDataArray<TransformMatrix> transform;
            public void Execute(int start, int count)
            {
                float4 x = (float4)0.0f;
                float4 y = (float4)0.0f;
                float4 z = (float4)0.0f;
                float4 r = (float4)0.0f;
                float4 cull = (float4)0.0f;
                for (int i = 0; i < count; ++i)
                {
                    var center = math.mul(transform[start+i].Value, new float4(sphere[start+i].BoundingSphereCenter, 1.0f)).xyz;
                    x[i] = center.x;
                    y[i] = center.y;
                    z[i] = center.z;
                    float scale = math.max(math.max(transform[start + i].Value.c0.x, transform[start + i].Value.c1.y),
                        transform[start + i].Value.c2.z);
                    r[i] = sphere[start + i].BoundingSphereRadius * scale;
                    cull[i] = sphere[start + i].CullStatus;
                }
                output[start] = x;
                output[start + 1] = y;
                output[start + 2] = z;
                output[start + 3] = r;
                oldCullStatus[start/4] = cull;
            }
        }

        struct FrustumPlanes
        {
            public float4 leftX;
            public float4 leftY;
            public float4 leftZ;
            public float4 leftDist;
            public float4 rightX;
            public float4 rightY;
            public float4 rightZ;
            public float4 rightDist;
            public float4 topX;
            public float4 topY;
            public float4 topZ;
            public float4 topDist;
            public float4 bottomX;
            public float4 bottomY;
            public float4 bottomZ;
            public float4 bottomDist;
        }
        [BurstCompile]
        struct FrustumCullJob : IJobParallelFor
        {
            [DeallocateOnJobCompletion][ReadOnly] public NativeArray<float4> center;
            public NativeArray<float4> culled;

            [DeallocateOnJobCompletion][ReadOnly] public NativeArray<FrustumPlanes> planes;
            public void Execute(int i)
            {
                var x = center[i * 4];
                var y = center[i * 4 + 1];
                var z = center[i * 4 + 2];
                var r = center[i * 4 + 3];

                float4 cullDist = float.MinValue;
                for (int p = 0; p < planes.Length; ++p)
                {
                    var leftDist = planes[p].leftX * x + planes[p].leftY * y + planes[p].leftZ * z - planes[p].leftDist + r;
                    var rightDist = planes[p].rightX * x + planes[p].rightY * y + planes[p].rightZ * z - planes[p].rightDist + r;
                    var topDist = planes[p].topX * x + planes[p].topY * y + planes[p].topZ * z - planes[p].topDist + r;
                    var bottomDist = planes[p].bottomX * x + planes[p].bottomY * y + planes[p].bottomZ * z - planes[p].bottomDist + r;

                    var newCullDist = leftDist;
                    newCullDist = math.min(newCullDist, rightDist);
                    newCullDist = math.min(newCullDist, topDist);
                    newCullDist = math.min(newCullDist, bottomDist);
                    cullDist = math.max(cullDist, newCullDist);
                }

                // set to 1 if culled - 0 if visible
                culled[i] = math.select((float4) 1f, (float4) 0.0f, cullDist >= (float4) 0.0f);;
            }
        }

        struct CullStatusUpdatejob : IJob
        {
            public EntityCommandBuffer commandBuffer;
            [ReadOnly] public EntityArray entities;
            public ComponentDataArray<MeshCullingComponent> spheres;
            [DeallocateOnJobCompletion][ReadOnly] public NativeArray<float4> cullStatus;
            [DeallocateOnJobCompletion][ReadOnly] public NativeArray<float4> oldCullStatus;
            public void Execute()
            {
                // Check for meshes which changed culling status, 4 at a time
                for (int i = 0; i < spheres.Length / 4; ++i)
                {
                    if (math.any(oldCullStatus[i] != cullStatus[i]))
                    {
                        for (int j = 0; j < 4; ++j)
                        {
                            if (oldCullStatus[i][j] != cullStatus[i][j])
                            {
                                var temp = spheres[i * 4 + j];
                                temp.CullStatus = cullStatus[i][j];
                                spheres[i * 4 + j] = temp;
                                if (cullStatus[i][j] == 0.0f)
                                    commandBuffer.RemoveComponent<MeshCulledComponent>(entities[i*4 + j]);
                                else
                                    commandBuffer.AddComponent(entities[i*4+j], new MeshCulledComponent());
                            }
                        }
                    }
                }
                if ((spheres.Length & 3) != 0)
                {
                    int baseIndex = spheres.Length / 4;
                    for (int i = 0; i < (spheres.Length & 3); ++i)
                    {
                        if (oldCullStatus[baseIndex][i] != cullStatus[baseIndex][i])
                        {
                            var temp = spheres[baseIndex * 4 + i];
                            temp.CullStatus = cullStatus[baseIndex][i];
                            spheres[baseIndex * 4 + i] = temp;
                            if (cullStatus[baseIndex][i] == 0.0f)
                                commandBuffer.RemoveComponent<MeshCulledComponent>(entities[baseIndex*4 + i]);
                            else
                                commandBuffer.AddComponent(entities[baseIndex*4 + i], new MeshCulledComponent());
                        }
                    }
                }
            }
        }

        FrustumPlanes generatePlane(Camera cam)
        {
            GeometryUtility.CalculateFrustumPlanes(cam, cameraPlanes);
            float3 leftPlaneNormal = cameraPlanes[0].normal;
            float leftPlaneDist = -cameraPlanes[0].distance;
            float3 rightPlaneNormal = cameraPlanes[1].normal;
            float rightPlaneDist = -cameraPlanes[1].distance;
            float3 bottomPlaneNormal = cameraPlanes[2].normal;
            float bottomPlaneDist = -cameraPlanes[2].distance;
            float3 topPlaneNormal = cameraPlanes[3].normal;
            float topPlaneDist = -cameraPlanes[3].distance;

            return new FrustumPlanes
            {
                leftX = leftPlaneNormal.xxxx,
                leftY = leftPlaneNormal.yyyy,
                leftZ = leftPlaneNormal.zzzz,
                leftDist = new float4(leftPlaneDist),
                rightX = rightPlaneNormal.xxxx,
                rightY = rightPlaneNormal.yyyy,
                rightZ = rightPlaneNormal.zzzz,
                rightDist = new float4(rightPlaneDist),
                topX = topPlaneNormal.xxxx,
                topY = topPlaneNormal.yyyy,
                topZ = topPlaneNormal.zzzz,
                topDist = new float4(topPlaneDist),
                bottomX = bottomPlaneNormal.xxxx,
                bottomY = bottomPlaneNormal.yyyy,
                bottomZ = bottomPlaneNormal.zzzz,
                bottomDist = new float4(bottomPlaneDist),
            };
        }
        [Inject] private BoundingSphere boundingSpheres;
        [Inject] private MeshCullingBarrier barrier;
        private Plane[] cameraPlanes;
        protected override void OnCreateManager(int capacity)
        {
            cameraPlanes = new Plane[6];
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            int numCameras = Camera.allCamerasCount;
#if UNITY_EDITOR
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                numCameras += SceneView.GetAllSceneCameras().Length;
            else
                numCameras = SceneView.GetAllSceneCameras().Length;
#endif
            if (numCameras == 0)
                return inputDeps;
            var planes = new NativeArray<FrustumPlanes>(numCameras, Allocator.TempJob);

#if UNITY_EDITOR
            for (int cam = 0; cam < SceneView.GetAllSceneCameras().Length; ++cam)
                planes[cam] = generatePlane(SceneView.GetAllSceneCameras()[cam]);

            if (EditorApplication.isPlayingOrWillChangePlaymode)
#endif
            {
                for (int i = 0; i < Camera.allCamerasCount; ++i)
                    planes[numCameras-Camera.allCamerasCount+i] = generatePlane(Camera.allCameras[i]);
            }

            var centers = new NativeArray<float4>((boundingSpheres.Length + 3) & ~3, Allocator.TempJob);
            var cullStatus = new NativeArray<float4>((boundingSpheres.Length + 3) & ~3, Allocator.TempJob);
            var oldCullStatus = new NativeArray<float4>((boundingSpheres.Length + 3) & ~3, Allocator.TempJob);
            var transJob = new TransformCenterJob {output = centers, oldCullStatus = oldCullStatus, sphere = boundingSpheres.sphere, transform = boundingSpheres.transform};
            var cullJob = new FrustumCullJob
            {
                center = centers, culled = cullStatus,
                planes = planes
            };

            // First run a job which calculates the center positions of the meshes and stores them as float4(x1,x2,x3,x4), float4(y1,y2,y3,y4), ..., float4(x5, x6, x7, x8)
            var trans = transJob.ScheduleBatch(boundingSpheres.Length, 4, inputDeps);
            // Check four meshes at a time agains the plains, possible since we changed how positions are stored in the previous job
            var cullHandle = cullJob.Schedule((boundingSpheres.Length + 3) / 4, 1, trans);

            var cullStatusUpdateJob = new CullStatusUpdatejob
            {
                commandBuffer = barrier.CreateCommandBuffer(),
                entities = boundingSpheres.entities,
                spheres = boundingSpheres.sphere,
                cullStatus = cullStatus,
                oldCullStatus = oldCullStatus
            };
            
            return cullStatusUpdateJob.Schedule(cullHandle);
        }
    }
}
