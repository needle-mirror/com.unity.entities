using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Tests;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace Unity.Entities.PerformanceTests
{
    [TestFixture]
    [Category("Performance")]
    public sealed class GetAllEntitiesPerformanceTests : EntityPerformanceTestFixture
    {
        struct TestData0 : IComponentData
        {
            public int Value;

            public static implicit operator TestData0(int v) => new TestData0()
            {
                Value = v
            };
        }

        struct TestData1 : IComponentData
        {
            public int Value;

            public static implicit operator TestData1(int v) => new TestData1()
            {
                Value = v
            };
        }

        struct TestData2 : IComponentData
        {
            public int Value;

            public static implicit operator TestData2(int v) => new TestData2()
            {
                Value = v
            };
        }

        struct TestData3 : IComponentData
        {
            public int Value;

            public static implicit operator TestData3(int v) => new TestData3()
            {
                Value = v
            };
        }

        struct TestData4 : IComponentData
        {
            public int Value;

            public static implicit operator TestData4(int v) => new TestData4()
            {
                Value = v
            };
        }

        struct TestData5 : IComponentData
        {
            public int Value;

            public static implicit operator TestData5(int v) => new TestData5()
            {
                Value = v
            };
        }

        struct TestData6 : IComponentData
        {
            public int Value;

            public static implicit operator TestData6(int v) => new TestData6()
            {
                Value = v
            };
        }

        struct TestData7 : IComponentData
        {
            public int Value;

            public static implicit operator TestData7(int v) => new TestData7()
            {
                Value = v
            };
        }

        struct TestData8 : IComponentData
        {
            public int Value;

            public static implicit operator TestData8(int v) => new TestData8()
            {
                Value = v
            };
        }

        struct TestData9 : IComponentData
        {
            public int Value;

            public static implicit operator TestData9(int v) => new TestData9()
            {
                Value = v
            };
        }


        Type[] DataTypes =
        {
            typeof(TestData0),
            typeof(TestData1),
            typeof(TestData2),
            typeof(TestData3),
            typeof(TestData4),
            typeof(TestData5),
            typeof(TestData6),
            typeof(TestData7),
            typeof(TestData8),
            typeof(TestData9),
        };

        NativeArray<EntityArchetype> CreateUniqueArchetypes(int size, Allocator allocator)
        {
            var archetypes = new NativeArray<EntityArchetype>(size,allocator);

            for (int i = 0; i < size; i++)
            {
                var typeList = new List<ComponentType>();
                int numCheck = i + 100;
                for(int j  = 0; j < DataTypes.Length; j++)
                {
                    if ((numCheck & j) == 1)
                    {
                        typeList.Add(DataTypes[j]);
                    }
                }

                archetypes[i] = m_Manager.CreateArchetype(typeList.ToArray());
            }

            return archetypes;
        }

        private unsafe void CreateArchetypeChunkArrayPerformance(int entityCount, int archetypeCount)
        {
            var archetypes = CreateUniqueArchetypes(archetypeCount, Allocator.Persistent);

            for (int i = 0; i < archetypeCount; i++)
            {
                var entities = m_Manager.CreateEntity(archetypes[i], entityCount / archetypeCount,Allocator.Persistent);
                Random rng = new Random(0x12345);
                //set data of entitiy to random values
                for (int j = 0; j < entities.Length; j++)
                {
                    var entity = entities[j];
                    if (m_Manager.HasComponent<TestData0>(entity))
                        m_Manager.SetComponentData<TestData0>(entity, rng.Next());
                    if (m_Manager.HasComponent<TestData1>(entity))
                        m_Manager.SetComponentData<TestData1>(entity, rng.Next());
                    if (m_Manager.HasComponent<TestData2>(entity))
                        m_Manager.SetComponentData<TestData2>(entity, rng.Next());
                    if (m_Manager.HasComponent<TestData3>(entity))
                        m_Manager.SetComponentData<TestData3>(entity, rng.Next());
                    if (m_Manager.HasComponent<TestData4>(entity))
                        m_Manager.SetComponentData<TestData4>(entity, rng.Next());
                    if (m_Manager.HasComponent<TestData5>(entity))
                        m_Manager.SetComponentData<TestData5>(entity, rng.Next());
                    if (m_Manager.HasComponent<TestData6>(entity))
                        m_Manager.SetComponentData<TestData6>(entity, rng.Next());
                    if (m_Manager.HasComponent<TestData7>(entity))
                        m_Manager.SetComponentData<TestData7>(entity, rng.Next());
                    if (m_Manager.HasComponent<TestData8>(entity))
                        m_Manager.SetComponentData<TestData8>(entity, rng.Next());
                    if (m_Manager.HasComponent<TestData9>(entity))
                        m_Manager.SetComponentData<TestData9>(entity, rng.Next());
                }

                entities.Dispose();
            }

            var access = m_Manager.GetCheckedEntityDataAccess();
            access->BeforeStructuralChange();
            var query = access->m_UniversalQuery;
            var resultArray = default(NativeArray<ArchetypeChunk>);

            Measure.Method(() =>
                {
                    resultArray = query.CreateArchetypeChunkArray(Allocator.Temp);
                })
                .CleanUp(() =>
                {
                    resultArray.Dispose();
                })
                .WarmupCount(1)
                .MeasurementCount(10)
                .SampleGroup("GetAllEntities")
                .Run();

            archetypes.Dispose();
        }

         [Test, Performance]
        public void CreateArchetypeChunkArray_Performance_SmallScale([Values(1,5,10)] int entityCount,
            [Values(1)] int archetypeCount)
        {
           CreateArchetypeChunkArrayPerformance(entityCount,archetypeCount);
        }

        [Test, Performance]
        public void CreateArchetypeChunkArray_Performance_MediumScale([Values(100, 10000, 50000)] int entityCount,
            [Values(10, 100)] int archetypeCount)
        {
            CreateArchetypeChunkArrayPerformance(entityCount,archetypeCount);
        }

        [Test, Performance]
        public void CreateArchetypeChunkArray_Performance_LargeScale([Values(1000000, 5000000,10000000,20000000)] int entityCount,
            [Values(100,500,1000,5000,10000)] int archetypeCount)
        {
            CreateArchetypeChunkArrayPerformance(entityCount,archetypeCount);
        }
    }
}

