using NUnit.Framework;
using Unity.Collections;

namespace Unity.Entities.Tests
{
    class IJobEntityBatchTests : ECSTestsFixture
    {
        struct WriteBatchIndex : IJobEntityBatch
        {
            public ComponentTypeHandle<EcsTestData> EcsTestTypeHandle;

            public void Execute(ArchetypeChunk batch, int batchIndex)
            {
                var testDataArray = batch.GetNativeArray(EcsTestTypeHandle);
                testDataArray[0] = new EcsTestData
                {
                    value = batchIndex
                };
            }
        }

        [Test]
        public void IJobEntityBatchProcess([Values(1, 4, 17, 100)] int jobsPerChunk)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            var entityCount = 100;

            var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.Temp);
            var job = new WriteBatchIndex
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.ScheduleParallel(query, jobsPerChunk).Complete();

            for (int batchIndex = 0; batchIndex < jobsPerChunk; ++batchIndex)
            {
                ArchetypeChunk.CalculateBatchSizeAndStartIndex(entityCount, jobsPerChunk, batchIndex, out var batchCount, out var startIndex);

                Assert.AreEqual(batchIndex, m_Manager.GetComponentData<EcsTestData>(entities[startIndex]).value);
            }

            query.Dispose();
        }

        struct WriteEntityIndex : IJobEntityBatchWithIndex
        {
            public ComponentTypeHandle<EcsTestData> EcsTestTypeHandle;

            public void Execute(ArchetypeChunk batch, int batchIndex, int indexOfFirstEntityInQuery)
            {
                var testDataArray = batch.GetNativeArray(EcsTestTypeHandle);
                testDataArray[0] = new EcsTestData
                {
                    value = indexOfFirstEntityInQuery
                };
            }
        }

        [Test]
        public void IJobEntityBatchWithIndex([Values(1, 4, 17, 100)] int jobsPerChunk)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData));

            var entityCount = 100;
            var expectedEntitiesPerBatch = entityCount / jobsPerChunk;

            var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.Temp);
            var job = new WriteEntityIndex
            {
                EcsTestTypeHandle = m_Manager.GetComponentTypeHandle<EcsTestData>(false)
            };
            job.ScheduleParallel(query, jobsPerChunk).Complete();

            for (int batchIndex = 0; batchIndex < jobsPerChunk; ++batchIndex)
            {
                ArchetypeChunk.CalculateBatchSizeAndStartIndex(entityCount, jobsPerChunk, batchIndex, out var batchCount, out var startIndex);

                Assert.AreEqual(startIndex, m_Manager.GetComponentData<EcsTestData>(entities[startIndex]).value);
            }

            query.Dispose();
        }
    }
}
