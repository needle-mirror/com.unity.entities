#if ENABLE_PROFILER
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Profiling;

namespace Unity.Entities
{
    static partial class MemoryProfiler
    {
        [DisableAutoCreation]
        unsafe partial class RecordingSystem : SystemBase
        {
            NativeList<EntityArchetype> m_Archetypes;
            NativeList<ArchetypeMemoryData> m_ArchetypesMemoryData;

            [BurstCompile]
            struct GetArchetypesMemoryDataJob : IJob
            {
                [ReadOnly] public ulong WorldSequenceNumber;
                [ReadOnly] public NativeArray<EntityArchetype> EntityArchetypes;
                [WriteOnly] public NativeArray<ArchetypeMemoryData> ArchetypesMemoryData;

                public void Execute()
                {
                    for (var i = 0; i < EntityArchetypes.Length; ++i)
                    {
                        var archetype = EntityArchetypes[i].Archetype;
                        var archetypeMemoryData = new ArchetypeMemoryData(WorldSequenceNumber, archetype);
                        SharedAllocatedBytesCounter.Ref.Data.Value += archetypeMemoryData.CalculateAllocatedBytes();
                        SharedUnusedBytesCounter.Ref.Data.Value += archetypeMemoryData.CalculateUnusedBytes(archetype);
                        ArchetypesMemoryData[i] = archetypeMemoryData;
                    }
                }
            }

            protected override void OnCreate()
            {
                m_Archetypes = new NativeList<EntityArchetype>(16, Allocator.Persistent);
                m_ArchetypesMemoryData = new NativeList<ArchetypeMemoryData>(16, Allocator.Persistent);
            }

            protected override void OnDestroy()
            {
                m_ArchetypesMemoryData.Dispose();
                m_Archetypes.Dispose();
            }

            protected override void OnUpdate()
            {
                if (!Profiler.enabled)
                    return;

                //@TODO: Here we should test if profiler category is enabled, and bail if its not... but that API is currently not available.

                m_Archetypes.Clear();
                EntityManager.GetAllArchetypes(m_Archetypes);

                m_ArchetypesMemoryData.Resize(m_Archetypes.Length, NativeArrayOptions.UninitializedMemory);
                new GetArchetypesMemoryDataJob
                {
                    WorldSequenceNumber = World.SequenceNumber,
                    EntityArchetypes = m_Archetypes.AsArray(),
                    ArchetypesMemoryData = m_ArchetypesMemoryData.AsArray()
                }.Run();

                Profiler.EmitFrameMetaData(Guid, 0, m_ArchetypesMemoryData.AsArray());
            }
        }
    }
}
#endif
