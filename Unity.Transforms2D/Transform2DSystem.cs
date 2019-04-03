using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Scripting;

namespace Unity.Transforms2D
{
    [Preserve]
    public class Transform2DSystem : JobComponentSystem
    {
        struct TransGroup
        {
            public ComponentDataArray<TransformMatrix> matrices;
            [ReadOnly] public ComponentDataArray<Position2D> positions;
            [ReadOnly] public SubtractiveComponent<Heading2D> headings;
            public int Length;
        }
        
        [Inject] TransGroup m_TransGroup;
        
        struct RotTransGroup
        {
            public ComponentDataArray<TransformMatrix> matrices;
            [ReadOnly] public ComponentDataArray<Position2D> positions;
            [ReadOnly] public ComponentDataArray<Heading2D> headings;
            public int Length;
        }
        
        [Inject] RotTransGroup m_RotTransGroup;
    
        [BurstCompile]
        struct TransToMatrix : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<Position2D> positions;
            public ComponentDataArray<TransformMatrix> matrices;
        
            public void Execute(int i)
            {
                var position = positions[i].Value;
                matrices[i] = new TransformMatrix
                {
                    Value = math.translate(new float3(position.x,0.0f,position.y))
                };
            }
        }
        
        [BurstCompile]
        struct RotTransToMatrix : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<Position2D> positions;
            [ReadOnly] public ComponentDataArray<Heading2D> headings;
            public ComponentDataArray<TransformMatrix> matrices;
        
            public void Execute(int i)
            {
                float2 position = positions[i].Value;
                float2 heading = math.normalize(headings[i].Value);
                matrices[i] = new TransformMatrix
                {
                    Value = new float4x4
                    {
                        c0 = new float4( heading.y, 0.0f, -heading.x, 0.0f ),
                        c1 = new float4( 0.0f, 1.0f, 0.0f, 0.0f ),
                        c2 = new float4( heading.x, 0.0f, heading.y, 0.0f ),
                        c3 = new float4( position.x, 0.0f, position.y, 1.0f )
                    }
                };
            }
        }
        
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var transToMatrixJob = new TransToMatrix();
            transToMatrixJob.positions = m_TransGroup.positions;
            transToMatrixJob.matrices = m_TransGroup.matrices;
            var transToMatrixJobHandle = transToMatrixJob.Schedule(m_TransGroup.Length, 64, inputDeps);
            
            var rotTransToMatrixJob = new RotTransToMatrix();
            rotTransToMatrixJob.positions = m_RotTransGroup.positions;
            rotTransToMatrixJob.matrices = m_RotTransGroup.matrices;
            rotTransToMatrixJob.headings = m_RotTransGroup.headings;
            
            return rotTransToMatrixJob.Schedule(m_RotTransGroup.Length, 64, transToMatrixJobHandle);
        } 
    }
}
