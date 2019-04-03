using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;

namespace Unity.Transforms
{
    [UnityEngine.ExecuteInEditMode]
    public class TransformSystem : JobComponentSystem
    {
        [Inject] [ReadOnly] ComponentDataFromEntity<LocalPosition> m_LocalPositions;
        [Inject] [ReadOnly] ComponentDataFromEntity<LocalRotation> m_LocalRotations;
        [Inject] ComponentDataFromEntity<Position>                 m_Positions;
        [Inject] ComponentDataFromEntity<Rotation>                 m_Rotations;
        [Inject] ComponentDataFromEntity<TransformMatrix>          m_TransformMatrices;

        // +Rotation +Position -Heading -TransformMatrix
        struct RootRotTransNoTransformGroup
        {
            [ReadOnly] public SubtractiveComponent<VoidSystem<TransformSystem>> transfromExternal;
            [ReadOnly] public ComponentDataArray<Rotation> rotations;
            [ReadOnly] public SubtractiveComponent<TransformParent> parents;
            [ReadOnly] public SubtractiveComponent<Heading> headings;
            [ReadOnly] public ComponentDataArray<Position> positions;
            [ReadOnly] public EntityArray entities;
            [ReadOnly] public SubtractiveComponent<TransformMatrix> transforms;
            public int Length;
        }
        [Inject] RootRotTransNoTransformGroup m_RootRotTransNoTransformGroup;
        
        // +Rotation +Position -Heading +TransformMatrix
        struct RootRotTransTransformGroup
        {
            [ReadOnly] public SubtractiveComponent<VoidSystem<TransformSystem>> transfromExternal;
            [ReadOnly] public ComponentDataArray<Rotation> rotations;
            [ReadOnly] public SubtractiveComponent<TransformParent> parents;
            [ReadOnly] public SubtractiveComponent<Heading> headings;
            [ReadOnly] public ComponentDataArray<Position> positions;
            [ReadOnly] public EntityArray entities;
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<TransformMatrix> transforms;
            public int Length;
        }
        [Inject] RootRotTransTransformGroup m_RootRotTransTransformGroup;

        // +Rotation -Position -Heading -TransformMatrix
        struct RootRotNoTransformGroup
        {
            [ReadOnly] public SubtractiveComponent<VoidSystem<TransformSystem>> transfromExternal;
            [ReadOnly] public ComponentDataArray<Rotation> rotations;
            [ReadOnly] public SubtractiveComponent<TransformParent> parents;
            [ReadOnly] public SubtractiveComponent<Heading> headings;
            [ReadOnly] public SubtractiveComponent<Position> positions;
            [ReadOnly] public EntityArray entities;
            [ReadOnly] public SubtractiveComponent<TransformMatrix> transforms;
            public int Length;
        }
        [Inject] RootRotNoTransformGroup m_RootRotNoTransformGroup;
        
        // +Rotation -Position -Heading +TransformMatrix
        struct RootRotTransformGroup
        {
            [ReadOnly] public SubtractiveComponent<VoidSystem<TransformSystem>> transfromExternal;
            [ReadOnly] public ComponentDataArray<Rotation> rotations;
            [ReadOnly] public SubtractiveComponent<TransformParent> parents;
            [ReadOnly] public SubtractiveComponent<Heading> headings;
            [ReadOnly] public SubtractiveComponent<Position> positions;
            [ReadOnly] public EntityArray entities;
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<TransformMatrix> transforms;
            public int Length;
        }
        [Inject] RootRotTransformGroup m_RootRotTransformGroup;
        
        // -Rotation +Position -Heading -TransformMatrix
        struct RootTransNoTransformGroup
        {
            [ReadOnly] public SubtractiveComponent<VoidSystem<TransformSystem>> transfromExternal;
            [ReadOnly] public SubtractiveComponent<Rotation> rotations;
            [ReadOnly] public SubtractiveComponent<TransformParent> parents;
            [ReadOnly] public SubtractiveComponent<Heading> headings;
            [ReadOnly] public ComponentDataArray<Position> positions;
            [ReadOnly] public EntityArray entities;
            [ReadOnly] public SubtractiveComponent<TransformMatrix> transforms;
            public int Length;
        }
        [Inject] RootTransNoTransformGroup m_RootTransNoTransformGroup;
        
        // -Rotation +Position -Heading +TransformMatrix
        struct RootTransTransformGroup
        {
            [ReadOnly] public SubtractiveComponent<VoidSystem<TransformSystem>> transfromExternal;
            [ReadOnly] public SubtractiveComponent<Rotation> rotations;
            [ReadOnly] public SubtractiveComponent<TransformParent> parents;
            [ReadOnly] public SubtractiveComponent<Heading> headings;
            [ReadOnly] public ComponentDataArray<Position> positions;
            [ReadOnly] public EntityArray entities;
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<TransformMatrix> transforms;
            public int Length;
        }
        [Inject] RootTransTransformGroup m_RootTransTransformGroup;
        
        // -Rotation +Position +Heading +TransformMatrix
        struct RootHeadingTransTransformGroup
        {
            [ReadOnly] public SubtractiveComponent<VoidSystem<TransformSystem>> transfromExternal;
            [ReadOnly] public SubtractiveComponent<Rotation> rotations;
            [ReadOnly] public SubtractiveComponent<TransformParent> parents;
            [ReadOnly] public ComponentDataArray<Heading> headings;
            [ReadOnly] public ComponentDataArray<Position> positions;
            [ReadOnly] public EntityArray entities;
            // @todo Why doesn't this throw exception?
            // [ReadOnly] public ComponentDataArray<TransformMatrix> transforms;
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<TransformMatrix> transforms;
            public int Length;
        }
        [Inject] RootHeadingTransTransformGroup m_RootHeadingTransTransformGroup;
        
        // -Rotation +Position +Heading -TransformMatrix
        struct RootHeadingTransNoTransformGroup
        {
            [ReadOnly] public SubtractiveComponent<VoidSystem<TransformSystem>> transfromExternal;
            [ReadOnly] public SubtractiveComponent<Rotation> rotations;
            [ReadOnly] public SubtractiveComponent<TransformParent> parents;
            [ReadOnly] public ComponentDataArray<Heading> headings;
            [ReadOnly] public ComponentDataArray<Position> positions;
            [ReadOnly] public EntityArray entities;
            [ReadOnly] public SubtractiveComponent<TransformMatrix> transforms;
            public int Length;
        }
        [Inject] RootHeadingTransNoTransformGroup m_RootHeadingTransNoTransformGroup;

        struct ParentGroup
        {
            [ReadOnly] public SubtractiveComponent<VoidSystem<TransformSystem>> transfromExternal;
            [ReadOnly] public ComponentDataArray<TransformParent> transformParents;
            [ReadOnly] public EntityArray entities;
            public int Length;
        }
        [Inject] ParentGroup m_ParentGroup;
        
        [BurstCompile]
        struct UpdateRotTransTransformRoots : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<Rotation> rotations;
            [ReadOnly] public ComponentDataArray<Position> positions;
            public NativeArray<float4x4> matrices;
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<TransformMatrix> transforms;

            public void Execute(int index)
            {
                float4x4 matrix = math.rottrans(rotations[index].Value, positions[index].Value);
                matrices[index] = matrix;
                transforms[index] = new TransformMatrix {Value = matrix};
            }
        }

        [BurstCompile]
        struct UpdateRotTransTransformNoHierarchyRoots : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<Rotation> rotations;
            [ReadOnly] public ComponentDataArray<Position> positions;
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<TransformMatrix> transforms;

            public void Execute(int index)
            {
                float4x4 matrix = math.rottrans(rotations[index].Value, positions[index].Value);
                transforms[index] = new TransformMatrix {Value = matrix};
            }
        }

        [BurstCompile]
        struct UpdateRotTransNoTransformRoots : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<Rotation> rotations;
            [ReadOnly] public ComponentDataArray<Position> positions;
            public NativeArray<float4x4> matrices;

            public void Execute(int index)
            {
                float4x4 matrix = math.rottrans(rotations[index].Value, positions[index].Value);
                matrices[index] = matrix;
            }
        }
        
        [BurstCompile]
        struct UpdateRotTransformRoots : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<Rotation> rotations;
            public NativeArray<float4x4> matrices;
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<TransformMatrix> transforms;

            public void Execute(int index)
            {
                float4x4 matrix = math.rottrans(rotations[index].Value, new float3());
                matrices[index] = matrix;
                transforms[index] = new TransformMatrix {Value = matrix};
            }
        }
        
        [BurstCompile]
        struct UpdateRotTransformNoHierarchyRoots : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<Rotation> rotations;
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<TransformMatrix> transforms;

            public void Execute(int index)
            {
                float4x4 matrix = math.rottrans(rotations[index].Value, new float3());
                transforms[index] = new TransformMatrix {Value = matrix};
            }
        }

        [BurstCompile]
        struct UpdateRotNoTransformRoots : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<Rotation> rotations;
            public NativeArray<float4x4> matrices;

            public void Execute(int index)
            {
                float4x4 matrix = math.rottrans(rotations[index].Value, new float3());
                matrices[index] = matrix;
            }
        }

        [BurstCompile]
        struct UpdateTransTransformRoots : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<Position> positions;
            public NativeArray<float4x4> matrices;
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<TransformMatrix> transforms;

            public void Execute(int index)
            {
                float4x4 matrix = math.translate(positions[index].Value);
                matrices[index] = matrix;
                transforms[index] = new TransformMatrix {Value = matrix};
            }
        }
        
        [BurstCompile]
        struct UpdateTransTransformNoHierarchyRoots : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<Position> positions;
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<TransformMatrix> transforms;

            public void Execute(int index)
            {
                float4x4 matrix = math.translate(positions[index].Value);
                transforms[index] = new TransformMatrix {Value = matrix};
            }
        }

        [BurstCompile]
        struct UpdateTransNoTransformRoots : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<Position> positions;
            public NativeArray<float4x4> matrices;

            public void Execute(int index)
            {
                float4x4 matrix = math.translate(positions[index].Value);
                matrices[index] = matrix;
            }
        }
        
        [BurstCompile]
        struct UpdateHeadingTransTransformRoots : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<Position> positions;
            [ReadOnly] public ComponentDataArray<Heading> headings;
            public NativeArray<float4x4> matrices;
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<TransformMatrix> transforms;

            public void Execute(int index)
            {
                var matrix = math.lookRotationToMatrix(positions[index].Value, headings[index].Value, math.up());
                matrices[index] = matrix;
                transforms[index] = new TransformMatrix {Value = matrix};
            }
        }
        
        [BurstCompile]
        struct UpdateHeadingTransTransformNoHierarchyRoots : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<Position> positions;
            [ReadOnly] public ComponentDataArray<Heading> headings;
            [NativeDisableContainerSafetyRestriction]
            public ComponentDataArray<TransformMatrix> transforms;

            public void Execute(int index)
            {
                var matrix = math.lookRotationToMatrix(positions[index].Value, headings[index].Value, math.up());
                transforms[index] = new TransformMatrix {Value = matrix};
            }
        }

        [BurstCompile]
        struct UpdateHeadingTransNoTransformRoots : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<Position> positions;
            [ReadOnly] public ComponentDataArray<Heading> headings;
            public NativeArray<float4x4> matrices;

            public void Execute(int index)
            {
                var matrix = math.lookRotationToMatrix(positions[index].Value, headings[index].Value, math.up());
                matrices[index] = matrix;
            }
        }
        
        [BurstCompile]
        struct BuildHierarchy : IJobParallelFor
        {
            public NativeMultiHashMap<Entity, Entity>.Concurrent hierarchy;
            [ReadOnly] public ComponentDataArray<TransformParent> transformParents;
            [ReadOnly] public EntityArray entities;

            public void Execute(int index)
            {
                hierarchy.Add(transformParents[index].Value,entities[index]);
            }
        }

        [BurstCompile]
        struct UpdateSubHierarchy : IJobParallelFor
        {
            [ReadOnly] public NativeMultiHashMap<Entity, Entity>                                  hierarchy;
            [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<Entity>                     roots;
            [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<float4x4>                   rootMatrices;
            
            [ReadOnly] public ComponentDataFromEntity<LocalPosition>                              localPositions;
            [ReadOnly] public ComponentDataFromEntity<LocalRotation>                              localRotations;
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<Position>        positions;
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<Rotation>        rotations;
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<TransformMatrix> transformMatrices;

            void TransformTree(Entity entity,float4x4 parentMatrix)
            {
                var position = new float3();
                var rotation = quaternion.identity;
                
                if (positions.Exists(entity))
                {
                    position = positions[entity].Value;
                }
                
                if (rotations.Exists(entity))
                {
                    rotation = rotations[entity].Value;
                }
                
                if (localPositions.Exists(entity))
                {
                    var worldPosition = math.mul(parentMatrix,new float4(localPositions[entity].Value,1.0f));
                    position = new float3(worldPosition.x,worldPosition.y,worldPosition.z);
                    if (positions.Exists(entity))
                    {
                        positions[entity] = new Position {Value = position};
                    }
                }
                
                if (localRotations.Exists(entity))
                {
                    var parentRotation = math.matrixToQuat(parentMatrix.c0.xyz, parentMatrix.c1.xyz, parentMatrix.c2.xyz);
                    var localRotation = localRotations[entity].Value;
                    rotation = math.mul(parentRotation, localRotation);
                    if (rotations.Exists(entity))
                    {
                        rotations[entity] = new Rotation { Value = rotation };
                    }
                }

                float4x4 matrix = math.rottrans(rotation, position);
                if (transformMatrices.Exists(entity))
                {
                    transformMatrices[entity] = new TransformMatrix {Value = matrix};
                }

                Entity child;
                NativeMultiHashMapIterator<Entity> iterator;
                bool found = hierarchy.TryGetFirstValue(entity, out child, out iterator);
                while (found)
                {
                    TransformTree(child,matrix);
                    found = hierarchy.TryGetNextValue(out child, ref iterator);
                }
            }

            public void Execute(int i)
            {
                Entity entity = roots[i];
                float4x4 matrix = rootMatrices[i];
                Entity child;
                NativeMultiHashMapIterator<Entity> iterator;
                bool found = hierarchy.TryGetFirstValue(entity, out child, out iterator);
                while (found)
                {
                    TransformTree(child,matrix);
                    found = hierarchy.TryGetNextValue(out child, ref iterator);
                }
            }
        }

        [BurstCompile]
        struct ClearHierarchy : IJob
        {
            public  NativeMultiHashMap<Entity, Entity> hierarchy;

            public void Execute()
            {
                hierarchy.Clear();
            }
        }
        
        NativeMultiHashMap<Entity, Entity> m_Hierarchy;

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            int rootCount = m_RootRotTransTransformGroup.Length + m_RootRotTransNoTransformGroup.Length +
                            m_RootRotTransformGroup.Length + m_RootRotNoTransformGroup.Length +
                            m_RootTransTransformGroup.Length + m_RootTransNoTransformGroup.Length +
                            m_RootHeadingTransTransformGroup.Length + m_RootHeadingTransNoTransformGroup.Length;
            if (rootCount == 0)
            {
                return inputDeps;
            }
            
            var updateRootsDeps = inputDeps;
            JobHandle? updateRootsBarrierJobHandle = null;
            
            //
            // Update Roots (No Hierachies)
            //
            
            if (m_ParentGroup.Length == 0)
            {
                if (m_RootRotTransTransformGroup.Length > 0)
                {
                    var updateRotTransTransformRootsJob = new UpdateRotTransTransformNoHierarchyRoots
                    {
                        rotations = m_RootRotTransTransformGroup.rotations,
                        positions = m_RootRotTransTransformGroup.positions,
                        transforms = m_RootRotTransTransformGroup.transforms
                    };
                    var updateRotTransTransformRootsJobHandle = updateRotTransTransformRootsJob.Schedule(m_RootRotTransTransformGroup.Length, 64, updateRootsDeps);
                    updateRootsBarrierJobHandle = (updateRootsBarrierJobHandle == null)?updateRotTransTransformRootsJobHandle: JobHandle.CombineDependencies(updateRootsBarrierJobHandle.Value, updateRotTransTransformRootsJobHandle);
                }
                
                if (m_RootRotTransformGroup.Length > 0)
                {
                    var updateRotTransformRootsJob = new UpdateRotTransformNoHierarchyRoots
                    {
                        rotations = m_RootRotTransformGroup.rotations,
                        transforms = m_RootRotTransformGroup.transforms
                    };
                    var updateRotTransformRootsJobHandle = updateRotTransformRootsJob.Schedule(m_RootRotTransformGroup.Length, 64, updateRootsDeps);
                    updateRootsBarrierJobHandle = (updateRootsBarrierJobHandle == null)?updateRotTransformRootsJobHandle: JobHandle.CombineDependencies(updateRootsBarrierJobHandle.Value, updateRotTransformRootsJobHandle);
                }
                
                if (m_RootTransTransformGroup.Length > 0)
                {
                    var updateTransTransformRootsJob = new UpdateTransTransformNoHierarchyRoots
                    {
                        positions = m_RootTransTransformGroup.positions,
                        transforms = m_RootTransTransformGroup.transforms
                    };
                    var updateTransTransformRootsJobHandle = updateTransTransformRootsJob.Schedule(m_RootTransTransformGroup.Length, 64, updateRootsDeps);
                    updateRootsBarrierJobHandle = (updateRootsBarrierJobHandle == null)?updateTransTransformRootsJobHandle: JobHandle.CombineDependencies(updateRootsBarrierJobHandle.Value, updateTransTransformRootsJobHandle);
                }
                
                if (m_RootHeadingTransTransformGroup.Length > 0)
                {
                    var updateHeadingTransTransformRootsJob = new UpdateHeadingTransTransformNoHierarchyRoots
                    {
                        headings = m_RootHeadingTransTransformGroup.headings,
                        positions = m_RootHeadingTransTransformGroup.positions,
                        transforms = m_RootHeadingTransTransformGroup.transforms
                    };
                    var updateHeadingTransTransformRootsJobHandle = updateHeadingTransTransformRootsJob.Schedule(m_RootHeadingTransTransformGroup.Length, 64, updateRootsDeps);
                    updateRootsBarrierJobHandle = (updateRootsBarrierJobHandle == null)?updateHeadingTransTransformRootsJobHandle: JobHandle.CombineDependencies(updateRootsBarrierJobHandle.Value, updateHeadingTransTransformRootsJobHandle);
                }

                return (updateRootsBarrierJobHandle == null) ? updateRootsDeps : updateRootsBarrierJobHandle.Value;
            }

            //
            // Update Roots (Hierarchies exist)
            //

            if (m_ParentGroup.Length > 0)
            {
                m_Hierarchy.Capacity = math.max(m_ParentGroup.Length + rootCount,m_Hierarchy.Capacity);

                var clearHierarchyJob = new ClearHierarchy
                {
                    hierarchy = m_Hierarchy
                };
                var clearHierarchyJobHandle = clearHierarchyJob.Schedule(updateRootsDeps);

                var buildHierarchyJob = new BuildHierarchy
                {
                    hierarchy = m_Hierarchy,
                    transformParents = m_ParentGroup.transformParents,
                    entities = m_ParentGroup.entities
                };
                var buildHierarchyJobHandle = buildHierarchyJob.Schedule(m_ParentGroup.Length, 64, clearHierarchyJobHandle);
                updateRootsBarrierJobHandle = buildHierarchyJobHandle;
            }

            NativeArray<float4x4>? rotTransTransformRootMatrices = null;
            if (m_RootRotTransTransformGroup.Length > 0)
            {
                rotTransTransformRootMatrices = new NativeArray<float4x4>(m_RootRotTransTransformGroup.Length, Allocator.TempJob,NativeArrayOptions.UninitializedMemory);
                var updateRotTransTransformRootsJob = new UpdateRotTransTransformRoots
                {
                    rotations = m_RootRotTransTransformGroup.rotations,
                    positions = m_RootRotTransTransformGroup.positions,
                    matrices = rotTransTransformRootMatrices.Value,
                    transforms = m_RootRotTransTransformGroup.transforms
                };
                var updateRotTransTransformRootsJobHandle = updateRotTransTransformRootsJob.Schedule(m_RootRotTransTransformGroup.Length, 64, updateRootsDeps);
                updateRootsBarrierJobHandle = JobHandle.CombineDependencies(updateRootsBarrierJobHandle.Value, updateRotTransTransformRootsJobHandle);
            }
            
            NativeArray<float4x4>? rotTransNoTransformRootMatrices = null;
            if (m_RootRotTransNoTransformGroup.Length > 0)
            {
                rotTransNoTransformRootMatrices = new NativeArray<float4x4>(m_RootRotTransNoTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var updateRotTransNoTransformRootsJob = new UpdateRotTransNoTransformRoots
                {
                    rotations = m_RootRotTransNoTransformGroup.rotations,
                    positions = m_RootRotTransNoTransformGroup.positions,
                    matrices = rotTransNoTransformRootMatrices.Value
                };
                var updateRotTransNoTransformRootsJobHandle = updateRotTransNoTransformRootsJob.Schedule(m_RootRotTransNoTransformGroup.Length, 64, updateRootsDeps);
                updateRootsBarrierJobHandle = JobHandle.CombineDependencies(updateRootsBarrierJobHandle.Value, updateRotTransNoTransformRootsJobHandle);
            }
            
            NativeArray<float4x4>? rotTransformRootMatrices = null;
            if (m_RootRotTransformGroup.Length > 0)
            {
                rotTransformRootMatrices = new NativeArray<float4x4>(m_RootRotTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var updateRotTransformRootsJob = new UpdateRotTransformRoots
                {
                    rotations = m_RootRotTransformGroup.rotations,
                    matrices = rotTransformRootMatrices.Value,
                    transforms = m_RootRotTransformGroup.transforms
                };
                var updateRotTransformRootsJobHandle = updateRotTransformRootsJob.Schedule(m_RootRotTransformGroup.Length, 64, updateRootsDeps);
                updateRootsBarrierJobHandle = JobHandle.CombineDependencies(updateRootsBarrierJobHandle.Value, updateRotTransformRootsJobHandle);
            }
            
            NativeArray<float4x4>? rotNoTransformRootMatrices = null;
            if (m_RootRotNoTransformGroup.Length > 0)
            {
                rotNoTransformRootMatrices = new NativeArray<float4x4>(m_RootRotNoTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var updateRotNoTransformRootsJob = new UpdateRotNoTransformRoots
                {
                    rotations = m_RootRotNoTransformGroup.rotations,
                    matrices = rotNoTransformRootMatrices.Value
                };
                var updateRotNoTransformRootsJobHandle = updateRotNoTransformRootsJob.Schedule(m_RootRotNoTransformGroup.Length, 64, updateRootsDeps);
                updateRootsBarrierJobHandle = JobHandle.CombineDependencies(updateRootsBarrierJobHandle.Value, updateRotNoTransformRootsJobHandle);
            }
            
            NativeArray<float4x4>? transTransformRootMatrices = null;
            if (m_RootTransTransformGroup.Length > 0)
            {
                transTransformRootMatrices = new NativeArray<float4x4>(m_RootTransTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var updateTransTransformRootsJob = new UpdateTransTransformRoots
                {
                    positions = m_RootTransTransformGroup.positions,
                    matrices = transTransformRootMatrices.Value,
                    transforms = m_RootTransTransformGroup.transforms
                };
                var updateTransTransformRootsJobHandle = updateTransTransformRootsJob.Schedule(m_RootTransTransformGroup.Length, 64, updateRootsDeps);
                updateRootsBarrierJobHandle = JobHandle.CombineDependencies(updateRootsBarrierJobHandle.Value, updateTransTransformRootsJobHandle);
            }
            
            NativeArray<float4x4>? transNoTransformRootMatrices = null;
            if (m_RootTransNoTransformGroup.Length > 0)
            {
                transNoTransformRootMatrices = new NativeArray<float4x4>(m_RootTransNoTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var updateTransNoTransformRootsJob = new UpdateTransNoTransformRoots
                {
                    positions = m_RootTransNoTransformGroup.positions,
                    matrices = transNoTransformRootMatrices.Value
                };
                var updateTransNoTransformRootsJobHandle = updateTransNoTransformRootsJob.Schedule(m_RootTransNoTransformGroup.Length, 64, updateRootsDeps);
                updateRootsBarrierJobHandle = JobHandle.CombineDependencies(updateRootsBarrierJobHandle.Value, updateTransNoTransformRootsJobHandle);
            }
            
            NativeArray<float4x4>? headingTransTransformRootMatrices = null;
            if (m_RootHeadingTransTransformGroup.Length > 0)
            {
                headingTransTransformRootMatrices = new NativeArray<float4x4>(m_RootHeadingTransTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var updateHeadingTransTransformRootsJob = new UpdateHeadingTransTransformRoots
                {
                    positions = m_RootHeadingTransTransformGroup.positions,
                    headings = m_RootHeadingTransTransformGroup.headings,
                    matrices = headingTransTransformRootMatrices.Value,
                    transforms= m_RootHeadingTransTransformGroup.transforms
                };
                var updateHeadingTransTransformRootsJobHandle = updateHeadingTransTransformRootsJob.Schedule(m_RootHeadingTransTransformGroup.Length, 64, updateRootsDeps);
                updateRootsBarrierJobHandle = JobHandle.CombineDependencies(updateRootsBarrierJobHandle.Value, updateHeadingTransTransformRootsJobHandle);
            }
            
            NativeArray<float4x4>? headingTransNoTransformRootMatrices = null;
            if (m_RootHeadingTransNoTransformGroup.Length > 0)
            {
                headingTransNoTransformRootMatrices = new NativeArray<float4x4>(m_RootHeadingTransNoTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var updateHeadingTransNoTransformRootsJob = new UpdateHeadingTransNoTransformRoots
                {
                    positions = m_RootHeadingTransNoTransformGroup.positions,
                    headings = m_RootHeadingTransTransformGroup.headings,
                    matrices = headingTransNoTransformRootMatrices.Value
                };
                var updateHeadingTransNoTransformRootsJobHandle = updateHeadingTransNoTransformRootsJob.Schedule(m_RootHeadingTransNoTransformGroup.Length, 64, updateRootsDeps);
                updateRootsBarrierJobHandle = JobHandle.CombineDependencies(updateRootsBarrierJobHandle.Value, updateHeadingTransNoTransformRootsJobHandle);
            }
            
            //
            // Copy Root Entities for Sub Hierarchy Transform
            //

            var copyRootEntitiesDeps = updateRootsBarrierJobHandle.Value;
            var copyRootEntitiesBarrierJobHandle = new JobHandle();

            NativeArray<Entity>? rotTransTransformRoots = null;
            if (m_RootRotTransTransformGroup.Length > 0)
            {
                rotTransTransformRoots = new NativeArray<Entity>(m_RootRotTransTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var copyRotTransTransformRootsJob = new CopyEntities
                {
                    Source = m_RootRotTransTransformGroup.entities,
                    Results = rotTransTransformRoots.Value
                };
                var copyRotTransTransformRootsJobHandle = copyRotTransTransformRootsJob.Schedule(m_RootRotTransTransformGroup.Length, 64, copyRootEntitiesDeps);
                copyRootEntitiesBarrierJobHandle = JobHandle.CombineDependencies(copyRootEntitiesBarrierJobHandle,copyRotTransTransformRootsJobHandle);
            }
            
            NativeArray<Entity>? rotTransNoTransformRoots = null;
            if (m_RootRotTransNoTransformGroup.Length > 0)
            {
                rotTransNoTransformRoots = new NativeArray<Entity>(m_RootRotTransNoTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var copyRotTransNoTransformRootsJob = new CopyEntities
                {
                    Source = m_RootRotTransNoTransformGroup.entities,
                    Results = rotTransNoTransformRoots.Value
                };
                var copyRotTransNoTransformRootsJobHandle = copyRotTransNoTransformRootsJob.Schedule(m_RootRotTransNoTransformGroup.Length, 64, copyRootEntitiesDeps);
                copyRootEntitiesBarrierJobHandle = JobHandle.CombineDependencies(copyRootEntitiesBarrierJobHandle, copyRotTransNoTransformRootsJobHandle);
            }
            
            NativeArray<Entity>? rotTransformRoots = null;
            if (m_RootRotTransformGroup.Length > 0)
            {
                rotTransformRoots = new NativeArray<Entity>(m_RootRotTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var copyRotTransformRootsJob = new CopyEntities
                {
                    Source = m_RootRotTransformGroup.entities,
                    Results = rotTransformRoots.Value
                };
                var copyRotTransformRootsJobHandle = copyRotTransformRootsJob.Schedule(m_RootRotTransformGroup.Length, 64, copyRootEntitiesDeps);
                copyRootEntitiesBarrierJobHandle = JobHandle.CombineDependencies(copyRootEntitiesBarrierJobHandle,copyRotTransformRootsJobHandle);
            }
            
            NativeArray<Entity>? rotNoTransformRoots = null;
            if (m_RootRotNoTransformGroup.Length > 0)
            {
                rotNoTransformRoots = new NativeArray<Entity>(m_RootRotNoTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var copyRotNoTransformRootsJob = new CopyEntities
                {
                    Source = m_RootRotNoTransformGroup.entities,
                    Results = rotNoTransformRoots.Value
                };
                var copyRotNoTransformRootsJobHandle = copyRotNoTransformRootsJob.Schedule(m_RootRotNoTransformGroup.Length, 64, copyRootEntitiesDeps);
                copyRootEntitiesBarrierJobHandle = JobHandle.CombineDependencies(copyRootEntitiesBarrierJobHandle, copyRotNoTransformRootsJobHandle);
            }
            
            NativeArray<Entity>? transTransformRoots = null;
            if (m_RootTransTransformGroup.Length > 0)
            {
                transTransformRoots = new NativeArray<Entity>(m_RootTransTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var copyTransTransformRootsJob = new CopyEntities
                {
                    Source = m_RootTransTransformGroup.entities,
                    Results = transTransformRoots.Value
                };
                var copyTransTransformRootsJobHandle = copyTransTransformRootsJob.Schedule(m_RootTransTransformGroup.Length, 64, copyRootEntitiesDeps);
                copyRootEntitiesBarrierJobHandle = JobHandle.CombineDependencies(copyRootEntitiesBarrierJobHandle,copyTransTransformRootsJobHandle);
            }
            
            NativeArray<Entity>? transNoTransformRoots = null;
            if (m_RootTransNoTransformGroup.Length > 0)
            {
                transNoTransformRoots = new NativeArray<Entity>(m_RootTransNoTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var copyTransNoTransformRootsJob = new CopyEntities
                {
                    Source = m_RootTransNoTransformGroup.entities,
                    Results = transNoTransformRoots.Value
                };
                var copyTransNoTransformRootsJobHandle = copyTransNoTransformRootsJob.Schedule(m_RootTransNoTransformGroup.Length, 64, copyRootEntitiesDeps);
                copyRootEntitiesBarrierJobHandle = JobHandle.CombineDependencies(copyRootEntitiesBarrierJobHandle, copyTransNoTransformRootsJobHandle);
            }
            
            NativeArray<Entity>? headingTransTransformRoots = null;
            if (m_RootHeadingTransTransformGroup.Length > 0)
            {
                headingTransTransformRoots = new NativeArray<Entity>(m_RootHeadingTransTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var copyHeadingTransTransformRootsJob = new CopyEntities
                {
                    Source = m_RootHeadingTransTransformGroup.entities,
                    Results = headingTransTransformRoots.Value
                };
                var copyHeadingTransTransformRootsJobHandle = copyHeadingTransTransformRootsJob.Schedule(m_RootHeadingTransTransformGroup.Length, 64, copyRootEntitiesDeps);
                copyRootEntitiesBarrierJobHandle = JobHandle.CombineDependencies(copyRootEntitiesBarrierJobHandle,copyHeadingTransTransformRootsJobHandle);
            }
            
            NativeArray<Entity>? headingTransNoTransformRoots = null;
            if (m_RootHeadingTransNoTransformGroup.Length > 0)
            {
                headingTransNoTransformRoots = new NativeArray<Entity>(m_RootHeadingTransNoTransformGroup.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var copyHeadingTransNoTransformRootsJob = new CopyEntities
                {
                    Source = m_RootHeadingTransNoTransformGroup.entities,
                    Results = headingTransNoTransformRoots.Value
                };
                var copyHeadingTransNoTransformRootsJobHandle = copyHeadingTransNoTransformRootsJob.Schedule(m_RootHeadingTransNoTransformGroup.Length, 64, copyRootEntitiesDeps);
                copyRootEntitiesBarrierJobHandle = JobHandle.CombineDependencies(copyRootEntitiesBarrierJobHandle, copyHeadingTransNoTransformRootsJobHandle);
            }
            
            //
            // Update Sub Hierarchy
            //

            var updateSubHierarchyDeps = copyRootEntitiesBarrierJobHandle;
            var updateSubHierarchyBarrierJobHandle = new JobHandle();
            
            if (m_RootRotTransTransformGroup.Length > 0)
            {
                var updateRotTransTransformHierarchyJob = new UpdateSubHierarchy
                {
                    hierarchy = m_Hierarchy,
                    roots = rotTransTransformRoots.Value,
                    rootMatrices = rotTransTransformRootMatrices.Value,
                    localPositions = m_LocalPositions,
                    localRotations = m_LocalRotations,
                    positions = m_Positions,
                    rotations = m_Rotations,
                    transformMatrices = m_TransformMatrices
                };
                var updateRotTransTransformHierarchyJobHandle = updateRotTransTransformHierarchyJob.Schedule(rotTransTransformRoots.Value.Length,64,updateSubHierarchyDeps);
                updateSubHierarchyDeps = updateRotTransTransformHierarchyJobHandle;
                updateSubHierarchyBarrierJobHandle = JobHandle.CombineDependencies(updateSubHierarchyBarrierJobHandle,updateRotTransTransformHierarchyJobHandle);
            }
            
            if (m_RootRotTransNoTransformGroup.Length > 0)
            {
                var updateRotTransNoTransformHierarchyJob = new UpdateSubHierarchy
                {
                    hierarchy = m_Hierarchy,
                    roots = rotTransNoTransformRoots.Value,
                    rootMatrices = rotTransNoTransformRootMatrices.Value,
                    localPositions = m_LocalPositions,
                    localRotations = m_LocalRotations,
                    positions = m_Positions,
                    rotations = m_Rotations,
                    transformMatrices = m_TransformMatrices
                };
                var updateRotTransNoTransformHierarchyJobHandle = updateRotTransNoTransformHierarchyJob.Schedule(rotTransNoTransformRoots.Value.Length,64,updateSubHierarchyDeps);
                updateSubHierarchyDeps = updateRotTransNoTransformHierarchyJobHandle;
                updateSubHierarchyBarrierJobHandle = JobHandle.CombineDependencies(updateSubHierarchyBarrierJobHandle,updateRotTransNoTransformHierarchyJobHandle);
            }
            
            if (m_RootRotTransformGroup.Length > 0)
            {
                var updateRotTransformHierarchyJob = new UpdateSubHierarchy
                {
                    hierarchy = m_Hierarchy,
                    roots = rotTransformRoots.Value,
                    rootMatrices = rotTransformRootMatrices.Value,
                    localPositions = m_LocalPositions,
                    localRotations = m_LocalRotations,
                    positions = m_Positions,
                    rotations = m_Rotations,
                    transformMatrices = m_TransformMatrices
                };
                var updateRotTransformHierarchyJobHandle = updateRotTransformHierarchyJob.Schedule(rotTransformRoots.Value.Length,1,updateSubHierarchyDeps);
                updateSubHierarchyDeps = updateRotTransformHierarchyJobHandle;
                updateSubHierarchyBarrierJobHandle = JobHandle.CombineDependencies(updateSubHierarchyBarrierJobHandle,updateRotTransformHierarchyJobHandle);
            }
            
            if (m_RootRotNoTransformGroup.Length > 0)
            {
                var updateRotNoTransformHierarchyJob = new UpdateSubHierarchy
                {
                    hierarchy = m_Hierarchy,
                    roots = rotNoTransformRoots.Value,
                    rootMatrices = rotNoTransformRootMatrices.Value,
                    localPositions = m_LocalPositions,
                    localRotations = m_LocalRotations,
                    positions = m_Positions,
                    rotations = m_Rotations,
                    transformMatrices = m_TransformMatrices
                };
                var updateRotNoTransformHierarchyJobHandle = updateRotNoTransformHierarchyJob.Schedule(rotNoTransformRoots.Value.Length,1,updateSubHierarchyDeps);
                updateSubHierarchyDeps = updateRotNoTransformHierarchyJobHandle;
                updateSubHierarchyBarrierJobHandle = JobHandle.CombineDependencies(updateSubHierarchyBarrierJobHandle,updateRotNoTransformHierarchyJobHandle);
            }
            
            if (m_RootTransTransformGroup.Length > 0)
            {
                var updateTransTransformHierarchyJob = new UpdateSubHierarchy
                {
                    hierarchy = m_Hierarchy,
                    roots = transTransformRoots.Value,
                    rootMatrices = transTransformRootMatrices.Value,
                    localPositions = m_LocalPositions,
                    localRotations = m_LocalRotations,
                    positions = m_Positions,
                    rotations = m_Rotations,
                    transformMatrices = m_TransformMatrices
                };
                var updateTransTransformHierarchyJobHandle = updateTransTransformHierarchyJob.Schedule(transTransformRoots.Value.Length,1,updateSubHierarchyDeps);
                updateSubHierarchyDeps = updateTransTransformHierarchyJobHandle;
                updateSubHierarchyBarrierJobHandle = JobHandle.CombineDependencies(updateSubHierarchyBarrierJobHandle,updateTransTransformHierarchyJobHandle);
            }
            
            if (m_RootTransNoTransformGroup.Length > 0)
            {
                var updateTransNoTransformHierarchyJob = new UpdateSubHierarchy
                {
                    hierarchy = m_Hierarchy,
                    roots = transNoTransformRoots.Value,
                    rootMatrices = transNoTransformRootMatrices.Value,
                    localPositions = m_LocalPositions,
                    localRotations = m_LocalRotations,
                    positions = m_Positions,
                    rotations = m_Rotations,
                    transformMatrices = m_TransformMatrices
                };
                var updateTransNoTransformHierarchyJobHandle = updateTransNoTransformHierarchyJob.Schedule(transNoTransformRoots.Value.Length,1,updateSubHierarchyDeps);
                updateSubHierarchyDeps = updateTransNoTransformHierarchyJobHandle;
                updateSubHierarchyBarrierJobHandle = JobHandle.CombineDependencies(updateSubHierarchyBarrierJobHandle,updateTransNoTransformHierarchyJobHandle);
            }
            
            if (m_RootHeadingTransTransformGroup.Length > 0)
            {
                var updateHeadingTransTransformHierarchyJob = new UpdateSubHierarchy
                {
                    hierarchy = m_Hierarchy,
                    roots = headingTransTransformRoots.Value,
                    rootMatrices = headingTransTransformRootMatrices.Value,
                    localPositions = m_LocalPositions,
                    localRotations = m_LocalRotations,
                    positions = m_Positions,
                    rotations = m_Rotations,
                    transformMatrices = m_TransformMatrices
                };
                var updateHeadingTransTransformHierarchyJobHandle = updateHeadingTransTransformHierarchyJob.Schedule(headingTransTransformRoots.Value.Length,1,updateSubHierarchyDeps);
                updateSubHierarchyDeps = updateHeadingTransTransformHierarchyJobHandle;
                updateSubHierarchyBarrierJobHandle = JobHandle.CombineDependencies(updateSubHierarchyBarrierJobHandle,updateHeadingTransTransformHierarchyJobHandle);
            }
            
            if (m_RootHeadingTransNoTransformGroup.Length > 0)
            {
                var updateHeadingTransNoTransformHierarchyJob = new UpdateSubHierarchy
                {
                    hierarchy = m_Hierarchy,
                    roots = headingTransNoTransformRoots.Value,
                    rootMatrices = headingTransNoTransformRootMatrices.Value,
                    localPositions = m_LocalPositions,
                    localRotations = m_LocalRotations,
                    positions = m_Positions,
                    rotations = m_Rotations,
                    transformMatrices = m_TransformMatrices
                };
                var updateHeadingTransNoTransformHierarchyJobHandle = updateHeadingTransNoTransformHierarchyJob.Schedule(headingTransNoTransformRoots.Value.Length,1,updateSubHierarchyDeps);
                updateSubHierarchyDeps = updateHeadingTransNoTransformHierarchyJobHandle;
                updateSubHierarchyBarrierJobHandle = JobHandle.CombineDependencies(updateSubHierarchyBarrierJobHandle,updateHeadingTransNoTransformHierarchyJobHandle);
            }

            return updateSubHierarchyBarrierJobHandle;
        } 
        
        protected override void OnCreateManager(int capacity)
        {
            m_Hierarchy = new NativeMultiHashMap<Entity, Entity>(capacity, Allocator.Persistent);
        }

        protected override void OnDestroyManager()
        {
            m_Hierarchy.Dispose();
        }
        
    }
}
