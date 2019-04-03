using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace Unity.Rendering
{
    [UnityEngine.ExecuteInEditMode]
    public class MeshLODSystem : JobComponentSystem
    {
        struct LODGroup
        {
            public ComponentDataArray<MeshLODGroupComponent> lod;
            [ReadOnly] public ComponentDataArray<TransformMatrix> transform;
            public int Length;
        }
        [Inject] private LODGroup lodGroups;
        struct LODMesh
        {
            public ComponentDataArray<MeshLODComponent> lod;
            public EntityArray entities;
            public int Length;
        }
        [Inject] private LODMesh lodMeshes;
        [Inject] [ReadOnly] private ComponentDataFromEntity<MeshLODGroupComponent> allGroups;
        [Inject] private EndFrameBarrier barrier;

        struct LodGroupJob : IJobParallelFor
        {
            public ComponentDataArray<MeshLODGroupComponent> lod;
            [ReadOnly]public ComponentDataArray<TransformMatrix> transform;
            [DeallocateOnJobCompletion][ReadOnly]public NativeArray<float4> cameras;
            public unsafe void Execute(int i)
            {
                float3 center = math.mul(transform[i].Value, new float4(0, 0, 0, 1)).xyz;
                float size = 0;
                for (int cam = 0; cam < cameras.Length; ++cam)
                {
                    float dist = math.length(center - cameras[cam].xyz);
                    size = math.max(size, lod[i].size * 0.5F / (dist * cameras[cam].w));
                }

                float bias = lod[i].biasMinusOne + 1;
                size /= bias;

                var curLod = lod[i];
                float* limit = &curLod.limit0;
                int active = 0;
                while (active < 3 && size < limit[active])
                    ++active;
                if (active != curLod.activeLod)
                {
                    curLod.activeLod = active;
                    lod[i] = curLod;
                }
            }
        }
        struct LodMeshJob : IJob
        {
            public ComponentDataArray<MeshLODComponent> lod;
            [ReadOnly]public EntityArray entities;
            [ReadOnly]public ComponentDataFromEntity<MeshLODGroupComponent> allGroups;
            public EntityCommandBuffer commandBuffer;
            public void Execute()
            {
                for (int i = 0; i < lod.Length; ++i)
                {
                    var mesh = lod[i];
                    var group = allGroups[mesh.group];
                    if (mesh.lod == group.activeLod)
                    {
                        if (mesh.isInactive != 0)
                        {
                            commandBuffer.RemoveComponent<MeshLODInactive>(entities[i]);
                            mesh.isInactive = 0;
                            lod[i] = mesh;
                        }
                    }
                    else
                    {
                        if (mesh.isInactive == 0)
                        {
                            commandBuffer.AddComponent(entities[i], new MeshLODInactive());
                            mesh.isInactive = 1;
                            lod[i] = mesh;
                        }
                    }
                }
            }
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
            var cameras = new NativeArray<float4>(numCameras, Allocator.TempJob);

#if UNITY_EDITOR
            for (int cam = 0; cam < SceneView.GetAllSceneCameras().Length; ++cam)
            {
                var curCamera = SceneView.GetAllSceneCameras()[cam];
                float halfAngle = math.tan(math.radians(curCamera.fieldOfView) * 0.5F);
                cameras[cam] = new float4(curCamera.transform.position, halfAngle);
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
#endif
            {
                for (int i = 0; i < Camera.allCamerasCount; ++i)
                {
                    // FIXME: assums perspective projection
                    float halfAngle = math.tan(math.radians(Camera.allCameras[i].fieldOfView) * 0.5F);
                    cameras[numCameras - Camera.allCamerasCount + i] = new float4(Camera.allCameras[i].transform.position, halfAngle);
                }
            }

            var groupJob = new LodGroupJob
            {
                lod = lodGroups.lod,
                transform = lodGroups.transform,
                cameras = cameras
            };
            var groupHandle = groupJob.Schedule(lodGroups.Length, 1, inputDeps);

            var meshJob = new LodMeshJob
            {
                lod = lodMeshes.lod,
                entities = lodMeshes.entities,
                allGroups = allGroups,
                commandBuffer = barrier.CreateCommandBuffer()
            };
            return meshJob.Schedule(groupHandle);
        }
    }
}
