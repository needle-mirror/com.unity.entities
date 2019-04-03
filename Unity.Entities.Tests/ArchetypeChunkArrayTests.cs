using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Unity.Entities.Tests
{

    
    [TestFixture]
    public class ArchetypeChunkArrayTest : ECSTestsFixture
    {
        public Entity CreateEntity(int value, int sharedValue)
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestSharedComp));
            m_Manager.SetComponentData(entity, new EcsTestData(value));
            m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp(sharedValue));
            return entity;
        }

        public Entity CreateEntity2(int value, int sharedValue)
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData2), typeof(EcsTestSharedComp));
            m_Manager.SetComponentData(entity, new EcsTestData2(value));
            m_Manager.SetSharedComponentData(entity, new EcsTestSharedComp(sharedValue));
            return entity;
        }

        void CreateEntities(int count)
        {
            for (int i = 0; i != count; i++)
                CreateEntity(i, i % 7);
        }

        void CreateMixedEntities(int count)
        {
            for (int i = 0; i != count; i++)
            {
                if ((i & 1) == 0)
                    CreateEntity(i, i % 7);
                else
                    CreateEntity2(-i, i % 7);
            }
        }

        struct CollectValues : IJobParallelFor
        {
            [ReadOnly] public ArchetypeChunkArray chunks;
            [ReadOnly] public ArchetypeChunkComponentType<EcsTestData> ecsTestData;

            [NativeDisableParallelForRestriction] public NativeArray<int> values;

            public void Execute(int chunkIndex)
            {
                var chunk = chunks[chunkIndex];
                var chunkStartIndex = chunk.StartIndex;
                var chunkCount = chunk.Count;
                var chunkEcsTestData = chunk.GetNativeArray(ecsTestData);

                for (int i = 0; i < chunkCount; i++)
                {
                    values[chunkStartIndex + i] = chunkEcsTestData[i].value;
                }
            }
        }

        [Test]
        public void ACS_BasicIteration()
        {
            CreateEntities(64);

            var chunks = m_Manager.CreateArchetypeChunkArray(
                Array.Empty<ComponentType>(), // any
                Array.Empty<ComponentType>(), // none
                new ComponentType[] {typeof(EcsTestData)}, // all
                Allocator.Temp);

            var ecsTestData = m_Manager.GetArchetypeChunkComponentType<EcsTestData>(true);
            var entityCount = chunks.EntityCount;
            var values = new NativeArray<int>(entityCount, Allocator.TempJob);
            var collectValuesJob = new CollectValues
            {
                chunks = chunks,
                ecsTestData = ecsTestData,
                values = values
            };
            Assert.AreEqual(7,chunks.Length);

            var collectValuesJobHandle = collectValuesJob.Schedule(chunks.Length, 64);
            collectValuesJobHandle.Complete();
            chunks.Dispose();

            ulong foundValues = 0;
            for (int i = 0; i < entityCount; i++)
            {
                foundValues |= ((ulong)1 << values[i]);
            }

            foundValues++;
            Assert.AreEqual(0,foundValues);

            values.Dispose();
        }

        struct CollectMixedValues : IJobParallelFor
        {
            [ReadOnly] public ArchetypeChunkArray chunks;
            [ReadOnly] public ArchetypeChunkComponentType<EcsTestData> ecsTestData;
            [ReadOnly] public ArchetypeChunkComponentType<EcsTestData2> ecsTestData2;

            [NativeDisableParallelForRestriction] public NativeArray<int> values;

            public void Execute(int chunkIndex)
            {
                var chunk = chunks[chunkIndex];
                var chunkStartIndex = chunk.StartIndex;
                var chunkCount = chunk.Count;
                var chunkEcsTestData = chunk.GetNativeArray(ecsTestData);
                var chunkEcsTestData2 = chunk.GetNativeArray(ecsTestData2);

                if (chunkEcsTestData.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        values[chunkStartIndex + i] = chunkEcsTestData[i].value;
                    }
                }
                else if (chunkEcsTestData2.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        values[chunkStartIndex + i] = chunkEcsTestData2[i].value0;
                    }
                }
            }
        }

        [Test]
        public void ACS_FindMixed()
        {
            CreateMixedEntities(64);

            var chunks = m_Manager.CreateArchetypeChunkArray(
                new ComponentType[] {typeof(EcsTestData2), typeof(EcsTestData)}, // any
                Array.Empty<ComponentType>(), // none
                Array.Empty<ComponentType>(), // all
                Allocator.Temp);
            
            Assert.AreEqual(14,chunks.Length);

            var ecsTestData = m_Manager.GetArchetypeChunkComponentType<EcsTestData>(true);
            var ecsTestData2 = m_Manager.GetArchetypeChunkComponentType<EcsTestData2>(true);
            var entityCount = chunks.EntityCount;
            var values = new NativeArray<int>(entityCount, Allocator.TempJob);
            var collectValuesJob = new CollectMixedValues
            {
                chunks = chunks,
                ecsTestData = ecsTestData,
                ecsTestData2 = ecsTestData2,
                values = values
            };

            var collectValuesJobHandle = collectValuesJob.Schedule(chunks.Length, 64);
            collectValuesJobHandle.Complete();

            for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                var chunkCount = chunk.Count;

                Assert.AreEqual(4,math.ceil_pow2(chunkCount-1));
            }
            chunks.Dispose();

            ulong foundValues = 0;
            for (int i = 0; i < entityCount; i++)
            {
                foundValues |= ((ulong) 1 << math.abs(values[i]));
            }

            foundValues++;
            Assert.AreEqual(0,foundValues);

            values.Dispose();
        }

        struct ChangeMixedValues : IJobParallelFor
        {
            [ReadOnly] public ArchetypeChunkArray chunks;
            public ArchetypeChunkComponentType<EcsTestData> ecsTestData;
            public ArchetypeChunkComponentType<EcsTestData2> ecsTestData2;

            public void Execute(int chunkIndex)
            {
                var chunk = chunks[chunkIndex];
                var chunkCount = chunk.Count;
                var chunkEcsTestData = chunk.GetNativeArray(ecsTestData);
                var chunkEcsTestData2 = chunk.GetNativeArray(ecsTestData2);

                if (chunkEcsTestData.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        chunkEcsTestData[i] = new EcsTestData( chunkEcsTestData[i].value + 100 );
                    }
                }
                else if (chunkEcsTestData2.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        chunkEcsTestData2[i] = new EcsTestData2( chunkEcsTestData2[i].value0 - 1000 );
                    }
                }
            }
        }
        
        [Test]
        public void ACS_WriteMixed()
        {
            CreateMixedEntities(64);

            var chunks = m_Manager.CreateArchetypeChunkArray(
                new ComponentType[] {typeof(EcsTestData2), typeof(EcsTestData)}, // any
                Array.Empty<ComponentType>(), // none
                Array.Empty<ComponentType>(), // all
                Allocator.Temp);

            Assert.AreEqual(14,chunks.Length);

            var ecsTestData = m_Manager.GetArchetypeChunkComponentType<EcsTestData>(false);
            var ecsTestData2 = m_Manager.GetArchetypeChunkComponentType<EcsTestData2>(false);
            var changeValuesJobs = new ChangeMixedValues
            {
                chunks = chunks,
                ecsTestData = ecsTestData,
                ecsTestData2 = ecsTestData2,
            };

            var collectValuesJobHandle = changeValuesJobs.Schedule(chunks.Length, 64);
            collectValuesJobHandle.Complete();

            ulong foundValues = 0;
            for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                var chunkCount = chunk.Count;

                Assert.AreEqual(4,math.ceil_pow2(chunkCount-1));

                var chunkEcsTestData = chunk.GetNativeArray(ecsTestData);
                var chunkEcsTestData2 = chunk.GetNativeArray(ecsTestData2);
                if (chunkEcsTestData.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        foundValues |= (ulong)1 << (chunkEcsTestData[i].value-100);
                    }
                }
                else if (chunkEcsTestData2.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        foundValues |= (ulong)1 << (-chunkEcsTestData2[i].value0-1000);
                    }
                }
            }

            foundValues++;
            Assert.AreEqual(0,foundValues);
            
            chunks.Dispose();
        }
        
        struct ChangeMixedValuesSharedFilter : IJobParallelFor
        {
            [ReadOnly] public ArchetypeChunkArray chunks;
            public ArchetypeChunkComponentType<EcsTestData> ecsTestData;
            public ArchetypeChunkComponentType<EcsTestData2> ecsTestData2;
            [ReadOnly] public ArchetypeChunkSharedComponentType<EcsTestSharedComp> ecsTestSharedData;
            public int sharedFilterIndex;

            public void Execute(int chunkIndex)
            {
                var chunk = chunks[chunkIndex];
                var chunkCount = chunk.Count;
                var chunkEcsTestData = chunk.GetNativeArray(ecsTestData);
                var chunkEcsTestData2 = chunk.GetNativeArray(ecsTestData2);
                var chunkEcsSharedDataIndex = chunk.GetSharedComponentIndex(ecsTestSharedData);

                if (chunkEcsSharedDataIndex != sharedFilterIndex)
                    return;
                
                if (chunkEcsTestData.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        chunkEcsTestData[i] = new EcsTestData( chunkEcsTestData[i].value + 100 );
                    }
                }
                else if (chunkEcsTestData2.Length > 0)
                {
                    for (int i = 0; i < chunkCount; i++)
                    {
                        chunkEcsTestData2[i] = new EcsTestData2( chunkEcsTestData2[i].value0 - 1000 );
                    }
                }
            }
        }
        
        [Test]
        public void ACS_WriteMixedFilterShared()
        {
            CreateMixedEntities(64);
            
            Assert.AreEqual(1,m_Manager.GlobalSystemVersion);
            
            // Only update shared value == 1
            var unique = new List<EcsTestSharedComp>(0);
            m_Manager.GetAllUniqueSharedComponentDatas(unique);
            var sharedFilterValue = 1;
            var sharedFilterIndex = -1;
            for (int i = 0; i < unique.Count; i++)
            {
                if (unique[i].value == sharedFilterValue)
                {
                    sharedFilterIndex = i;
                    break;
                }
            }

            var chunks = m_Manager.CreateArchetypeChunkArray(
                new ComponentType[] {typeof(EcsTestData2), typeof(EcsTestData)}, // any
                Array.Empty<ComponentType>(), // none
                new ComponentType[] {typeof(EcsTestSharedComp)}, // all
                Allocator.Temp);

            Assert.AreEqual(14,chunks.Length);

            var ecsTestData = m_Manager.GetArchetypeChunkComponentType<EcsTestData>(false);
            var ecsTestData2 = m_Manager.GetArchetypeChunkComponentType<EcsTestData2>(false);
            var ecsTestSharedData = m_Manager.GetArchetypeChunkSharedComponentType<EcsTestSharedComp>(true);
            var changeValuesJobs = new ChangeMixedValuesSharedFilter
            {
                chunks = chunks,
                ecsTestData = ecsTestData,
                ecsTestData2 = ecsTestData2,
                ecsTestSharedData = ecsTestSharedData,
                sharedFilterIndex = sharedFilterIndex
            };

            var collectValuesJobHandle = changeValuesJobs.Schedule(chunks.Length, 64);
            collectValuesJobHandle.Complete();

            ulong foundValues = 0;
            for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                var chunkCount = chunk.Count;

                Assert.AreEqual(4,math.ceil_pow2(chunkCount-1));
                
                var chunkEcsSharedDataIndex = chunk.GetSharedComponentIndex(ecsTestSharedData);

                var chunkEcsTestData = chunk.GetNativeArray(ecsTestData);
                var chunkEcsTestData2 = chunk.GetNativeArray(ecsTestData2);
                if (chunkEcsTestData.Length > 0)
                {
                    var chunkEcsTestDataVersion = chunk.GetComponentVersion(ecsTestData);

                    Assert.AreEqual(1, chunkEcsTestDataVersion);
                    
                    for (int i = 0; i < chunkCount; i++)
                    {
                        if (chunkEcsSharedDataIndex == sharedFilterIndex)
                        {
                          foundValues |= (ulong)1 << (chunkEcsTestData[i].value-100);
                        }
                        else
                        {
                          foundValues |= (ulong)1 << (chunkEcsTestData[i].value);
                        }
                    }
                }
                else if (chunkEcsTestData2.Length > 0)
                {
                    var chunkEcsTestData2Version = chunk.GetComponentVersion(ecsTestData2);
                    
                    Assert.AreEqual(1, chunkEcsTestData2Version);
                    
                    for (int i = 0; i < chunkCount; i++)
                    {
                        if (chunkEcsSharedDataIndex == sharedFilterIndex)
                        {
                          foundValues |= (ulong)1 << (-chunkEcsTestData2[i].value0-1000);
                        }
                        else
                        {
                          foundValues |= (ulong)1 << (-chunkEcsTestData2[i].value0);
                        }
                    }
                }
            }

            foundValues++;
            Assert.AreEqual(0,foundValues);
            
            chunks.Dispose();
        }
    }
}
