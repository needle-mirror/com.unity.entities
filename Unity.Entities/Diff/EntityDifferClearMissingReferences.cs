using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Unity.Entities
{
    static unsafe partial class EntityDiffer
    {
        [BurstCompile]
        struct ClearMissingReferencesJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> Chunks;

            public void Execute(int index)
            {
                var chunk = Chunks[index].m_Chunk;

                ChunkDataUtility.ClearMissingReferences(chunk);
            }
        }

        static void ClearMissingReferences(EntityManager entityManager, NativeArray<ArchetypeChunk> chunks, out JobHandle jobHandle, JobHandle dependsOn)
        {
            jobHandle = new ClearMissingReferencesJob
            {
                Chunks = chunks,
            }.Schedule(chunks.Length, 64, dependsOn);
        }
    }
}
