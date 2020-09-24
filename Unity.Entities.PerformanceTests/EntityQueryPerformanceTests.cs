using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.PerformanceTesting;
using Unity.Entities.Tests;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace Unity.Entities.PerformanceTests
{
    [TestFixture]
    [Category("Performance")]
    public sealed class EntityQueryPerformanceTests : EntityPerformanceTestFixture
    {
        struct TestTag0 : IComponentData{}
        struct TestTag1 : IComponentData{}
        struct TestTag2 : IComponentData{}
        struct TestTag3 : IComponentData{}
        struct TestTag4 : IComponentData{}
        struct TestTag5 : IComponentData{}
        struct TestTag6 : IComponentData{}
        struct TestTag7 : IComponentData{}
        struct TestTag8 : IComponentData{}
        struct TestTag9 : IComponentData{}
        struct TestTag10 : IComponentData{}
        struct TestTag11 : IComponentData{}
        struct TestTag12 : IComponentData{}
        struct TestTag13 : IComponentData{}
        struct TestTag14 : IComponentData{}
        struct TestTag15 : IComponentData{}
        struct TestTag16 : IComponentData{}
        struct TestTag17 : IComponentData{}
        struct TestTag18 : IComponentData{}
        struct TestTag19 : IComponentData{}
        struct TestTag20 : IComponentData{}
        struct TestTag21 : IComponentData{}
        struct TestTag22 : IComponentData{}
        struct TestTag23 : IComponentData{}
        struct TestTag24 : IComponentData{}
        struct TestTag25 : IComponentData{}
        struct TestTag26 : IComponentData{}
        struct TestTag27 : IComponentData{}
        struct TestTag28 : IComponentData{}
        struct TestTag29 : IComponentData{}
        struct TestTag30 : IComponentData{}
        struct TestTag31 : IComponentData{}
        struct TestTag32 : IComponentData{}
        struct TestTag33 : IComponentData{}
        struct TestTag34 : IComponentData{}
        struct TestTag35 : IComponentData{}
        struct TestTag36 : IComponentData{}
        struct TestTag37 : IComponentData{}
        struct TestTag38 : IComponentData{}
        struct TestTag39 : IComponentData{}
        struct TestTag40 : IComponentData{}
        struct TestTag41 : IComponentData{}
        struct TestTag42 : IComponentData{}
        struct TestTag43 : IComponentData{}
        struct TestTag44 : IComponentData{}
        struct TestTag45 : IComponentData{}
        struct TestTag46 : IComponentData{}
        struct TestTag47 : IComponentData{}
        struct TestTag48 : IComponentData{}
        struct TestTag49 : IComponentData{}
        struct TestTag50 : IComponentData{}
        struct TestTag51 : IComponentData{}
        struct TestTag52 : IComponentData{}
        struct TestTag53 : IComponentData{}
        struct TestTag54 : IComponentData{}
        struct TestTag55 : IComponentData{}
        struct TestTag56 : IComponentData{}
        struct TestTag57 : IComponentData{}
        struct TestTag58 : IComponentData{}
        struct TestTag59 : IComponentData{}
        struct TestTag60 : IComponentData{}
        struct TestTag61 : IComponentData{}
        struct TestTag62 : IComponentData{}
        struct TestTag63 : IComponentData{}
        struct TestTag64 : IComponentData{}
        struct TestTag65 : IComponentData{}
        struct TestTag66 : IComponentData{}
        struct TestTag67 : IComponentData{}
        struct TestTag68 : IComponentData{}
        struct TestTag69 : IComponentData{}
        struct TestTag70 : IComponentData{}
        struct TestTag71 : IComponentData{}
        struct TestTag72 : IComponentData{}
        struct TestTag73 : IComponentData{}
        struct TestTag74 : IComponentData{}
        struct TestTag75 : IComponentData{}
        struct TestTag76 : IComponentData{}
        struct TestTag77 : IComponentData{}
        struct TestTag78 : IComponentData{}
        struct TestTag79 : IComponentData{}
        struct TestTag80 : IComponentData{}
        struct TestTag81 : IComponentData{}
        struct TestTag82 : IComponentData{}
        struct TestTag83 : IComponentData{}
        struct TestTag84 : IComponentData{}
        struct TestTag85 : IComponentData{}
        struct TestTag86 : IComponentData{}
        struct TestTag87 : IComponentData{}
        struct TestTag88 : IComponentData{}
        struct TestTag89 : IComponentData{}
        struct TestTag90 : IComponentData{}
        struct TestTag91 : IComponentData{}
        struct TestTag92 : IComponentData{}
        struct TestTag93 : IComponentData{}
        struct TestTag94 : IComponentData{}
        struct TestTag95 : IComponentData{}
        struct TestTag96 : IComponentData{}
        struct TestTag97 : IComponentData{}
        struct TestTag98 : IComponentData{}
        struct TestTag99 : IComponentData{}
        struct TestTag100 : IComponentData{}

        struct LargeComponent : IComponentData
        {
            public int data0;
            public int data1;
            public int data2;
            public int data3;
            public int data4;
            public int data5;
            public int data6;
            public int data7;
            public int data8;
            public int data9;
        }

        Type[] TagTypes =
        {
            typeof(TestTag0),
            typeof(TestTag1),
            typeof(TestTag2),
            typeof(TestTag3),
            typeof(TestTag4),
            typeof(TestTag5),
            typeof(TestTag6),
            typeof(TestTag7),
            typeof(TestTag8),
            typeof(TestTag9),
            typeof(TestTag10),
            typeof(TestTag11),
            typeof(TestTag12),
            typeof(TestTag13),
            typeof(TestTag14),
            typeof(TestTag15),
            typeof(TestTag16),
            typeof(TestTag17),
            typeof(TestTag18),
            typeof(TestTag19),
            typeof(TestTag20),
            typeof(TestTag21),
            typeof(TestTag22),
            typeof(TestTag23),
            typeof(TestTag24),
            typeof(TestTag25),
            typeof(TestTag26),
            typeof(TestTag27),
            typeof(TestTag28),
            typeof(TestTag29),
            typeof(TestTag30),
            typeof(TestTag31),
            typeof(TestTag32),
            typeof(TestTag33),
            typeof(TestTag34),
            typeof(TestTag35),
            typeof(TestTag36),
            typeof(TestTag37),
            typeof(TestTag38),
            typeof(TestTag39),
            typeof(TestTag40),
            typeof(TestTag41),
            typeof(TestTag42),
            typeof(TestTag43),
            typeof(TestTag44),
            typeof(TestTag45),
            typeof(TestTag46),
            typeof(TestTag47),
            typeof(TestTag48),
            typeof(TestTag49),
            typeof(TestTag50),
            typeof(TestTag51),
            typeof(TestTag52),
            typeof(TestTag53),
            typeof(TestTag54),
            typeof(TestTag55),
            typeof(TestTag56),
            typeof(TestTag57),
            typeof(TestTag58),
            typeof(TestTag59),
            typeof(TestTag60),
            typeof(TestTag61),
            typeof(TestTag62),
            typeof(TestTag63),
            typeof(TestTag64),
            typeof(TestTag65),
            typeof(TestTag66),
            typeof(TestTag67),
            typeof(TestTag68),
            typeof(TestTag69),
            typeof(TestTag70),
            typeof(TestTag71),
            typeof(TestTag72),
            typeof(TestTag73),
            typeof(TestTag74),
            typeof(TestTag75),
            typeof(TestTag76),
            typeof(TestTag77),
            typeof(TestTag78),
            typeof(TestTag79),
            typeof(TestTag80),
            typeof(TestTag81),
            typeof(TestTag82),
            typeof(TestTag83),
            typeof(TestTag84),
            typeof(TestTag85),
            typeof(TestTag86),
            typeof(TestTag87),
            typeof(TestTag88),
            typeof(TestTag89),
            typeof(TestTag90),
            typeof(TestTag91),
            typeof(TestTag92),
            typeof(TestTag93),
            typeof(TestTag94),
            typeof(TestTag95),
            typeof(TestTag96),
            typeof(TestTag97),
            typeof(TestTag98),
            typeof(TestTag99),
            typeof(TestTag100),
        };

        //not an actually useful function. Used to do work on largeComponent to prevent "Never assigned to" error
        private void dummyWorkLargeComponentData()
        {
            var entity  = m_Manager.CreateEntity(typeof(LargeComponent));
            var component = m_Manager.GetComponentData<LargeComponent>(entity);

            //work done purely so I do not throw a "never assigned to" error on LargeComponent
            component.data0 = entity.Index;
            component.data1 = entity.Index;
            component.data2 = entity.Index;
            component.data3 = entity.Index;
            component.data4 = entity.Index;
            component.data5 = entity.Index;
            component.data5 = entity.Index;
            component.data6 = entity.Index;
            component.data7 = entity.Index;
            component.data8 = entity.Index;
            component.data9 = entity.Index;

            m_Manager.SetComponentData(entity,component);
        }

        NativeArray<EntityArchetype> CreateUniqueArchetypes(int size, params ComponentType[] extraTypes)
        {
            var archetypes = new NativeArray<EntityArchetype>(size, Allocator.TempJob);

            for (int i = 0; i < size; i++)
            {
                var typeCount = CollectionHelper.Log2Ceil(i);
                var typeList = new List<ComponentType>();
                for (int typeIndex = 0; typeIndex < typeCount; typeIndex++)
                {
                    if ((i & (1 << typeIndex)) != 0)
                        typeList.Add(TagTypes[typeIndex]);
                }

                typeList.Add(typeof(EcsTestData));
                typeList.Add(typeof(EcsTestSharedComp));

                foreach(var extra in extraTypes)
                    typeList.Add(extra);

                var types = typeList.ToArray();
                archetypes[i] = m_Manager.CreateArchetype(types);
            }

            return archetypes;
        }

        [Test, Performance]
        public void EntityQuery_IsEmptyIgnoreFilter_N_Archetypes_SparseMatch([Values(1, 10, 100, 1000, 10000)] int archetypeCount)
        {
            // This measures the cost of EntityQueries filtering a large number of archetypes it matches
            var archetypes = CreateUniqueArchetypes(archetypeCount);
            var lastArchetype = archetypes[archetypes.Length - 1];
            m_Manager.CreateEntity(lastArchetype);

            var query = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestData>());

            Measure.Method(
                () =>
                {
                    // To get a useful metric
                    for (int i = 0; i < 1000; i++)
                    {
                        var r = query.IsEmptyIgnoreFilter;
                    }
                })
                .SampleGroup("IsEmptyIgnoreFilter")
                .Run();

            archetypes.Dispose();
        }

        unsafe ComponentType[] CreateRandomMatchingQueryTypes(NativeArray<EntityArchetype> archetypes)
        {
            var random = new Random(34343);
            var typeSet = new HashSet<ComponentType>();

            for (int i = 0; i < archetypes.Length; i++)
            {
                if (random.NextBool())
                {
                    for (int typeIndex = 0; typeIndex < archetypes[i].Archetype->TypesCount; typeIndex++)
                    {
                        typeSet.Add(archetypes[i].Archetype->Types[0].ToComponentType());
                    }
                }
            }

            return typeSet.ToArray();
        }

        [Test, Performance]
        public void EntityQuery_IsEmptyIgnoreFilter_N_Archetypes_RandomMatch([Values(1, 10, 100, 1000, 10000)] int archetypeCount)
        {
            // This measures the cost of EntityQueries filtering a large number of archetypes it matches
            var archetypes = CreateUniqueArchetypes(archetypeCount);
            var lastArchetype = archetypes[archetypes.Length - 1];
            m_Manager.CreateEntity(lastArchetype);

            var query = m_Manager.CreateEntityQuery(CreateRandomMatchingQueryTypes(archetypes));

            Measure.Method(
                () =>
                {
                    // To get a useful metric
                    for (int i = 0; i < 1000; i++)
                    {
                        var r = query.IsEmptyIgnoreFilter;
                    }
                })
                .SampleGroup("IsEmptyIgnoreFilter")
                .Run();

            archetypes.Dispose();
        }

        [Test, Performance]
        public void CalculateEntityCount_N_Archetypes_M_ChunksPerArchetype([Values(1, 10, 100)] int archetypeCount, [Values(1, 10, 100)] int chunkCount)
        {
            var archetypes = CreateUniqueArchetypes(archetypeCount);
            for (int i = 0; i < archetypes.Length; ++i)
            {
                var entities = new NativeArray<Entity>(archetypes[i].ChunkCapacity * chunkCount, Allocator.Temp);
                m_Manager.CreateEntity(archetypes[i], entities);
            }

            var query = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestData>(), ComponentType.ReadWrite<EcsTestSharedComp>());

            Measure.Method(
                () =>
                {
                    query.CalculateEntityCount();
                })
                .SampleGroup("CalculateEntityCount")
                .Run();

            query.SetSharedComponentFilter(new EcsTestSharedComp {value = archetypeCount + chunkCount});

            Measure.Method(
                () =>
                {
                    query.CalculateEntityCount();
                })
                .SampleGroup("CalculateEntityCount with Filtering")
                .Run();

            using (var entities = m_Manager.UniversalQuery.ToEntityArray(Allocator.TempJob))
            {
                m_Manager.DestroyEntity(entities);
            }
            archetypes.Dispose();
            query.Dispose();
        }

        private void ToEntityArray_Performance(int entityCount, bool unique)
        {
            NativeArray<EntityArchetype> archetypes;
            if (unique)
            {
                archetypes = CreateUniqueArchetypes(entityCount);
                for (int entIter = 0; entIter < entityCount; entIter++)
                {
                    m_Manager.CreateEntity(archetypes[entIter]);
                }
            }
            else
            {
                archetypes = CreateUniqueArchetypes(1);
                m_Manager.CreateEntity(archetypes[0], entityCount);
            }
            archetypes.Dispose();

            var query = m_Manager.CreateEntityQuery(typeof(EcsTestData), typeof(EcsTestSharedComp));

            var result =  default(NativeArray<Entity>);
            Measure.Method(
                    () =>
                    {
                        result = query.ToEntityArray(Allocator.TempJob);
                    })

                    .CleanUp( () =>
                    {
                        Assert.AreEqual(entityCount, result.Length);
                        result.Dispose();
                    })
                .SampleGroup("ToEntityArray")
                .WarmupCount(1) // make sure we're not timing job compilation on the first run
                .MeasurementCount(100)
                .Run();

            query.Dispose();
        }

        private void ToComponentDataArray_Performance_SmallComponent(int entityCount, bool unique)
        {
            NativeArray<EntityArchetype> archetypes;

            if (unique)
            {
                archetypes = CreateUniqueArchetypes(entityCount);
                for (int entIter = 0; entIter < entityCount; entIter++)
                {
                    m_Manager.CreateEntity(archetypes[entIter]);
                }
            }
            else
            {
                archetypes = CreateUniqueArchetypes(1);
                m_Manager.CreateEntity(archetypes[0],entityCount);
            }

            archetypes.Dispose();

            EntityQuery query = m_Manager.CreateEntityQuery(ComponentType.ReadWrite(typeof(EcsTestData)));

            var result =  default(NativeArray<EcsTestData>);

            Measure.Method(
                    () => { result = query.ToComponentDataArray<EcsTestData>(Allocator.TempJob); })
                .CleanUp(() =>
                {
                    Assert.AreEqual(entityCount, result.Length);
                    result.Dispose();
                })
                .SampleGroup("ToComponentDataArray")
                .WarmupCount(1) // make sure we're not timing job compilation on the first run
                .MeasurementCount(100)
                .Run();

            query.Dispose();
        }

        private void ToComponentDataArray_Performance_LargeComponent(int entityCount, bool unique)
        {
            NativeArray<EntityArchetype> archetypes;

            if (unique)
            {
                archetypes = CreateUniqueArchetypes(entityCount,typeof(LargeComponent));
                for (int entIter = 0; entIter < entityCount; entIter++)
                {
                    m_Manager.CreateEntity(archetypes[entIter]);
                }
            }
            else
            {
                archetypes = CreateUniqueArchetypes(1,typeof(LargeComponent));
                m_Manager.CreateEntity(archetypes[0],entityCount);
            }

            archetypes.Dispose();

            EntityQuery query = default;
            query = m_Manager.CreateEntityQuery(ComponentType.ReadOnly(typeof(LargeComponent)));

            var result =  default(NativeArray<LargeComponent>);

            Measure.Method(
                    () => {result = query.ToComponentDataArray<LargeComponent>(Allocator.TempJob); })
                .CleanUp(() =>
                {
                    Assert.AreEqual(entityCount, result.Length);
                    result.Dispose();
                })
                .SampleGroup("ToComponentDataArray")
                .WarmupCount(1) // make sure we're not timing job compilation on the first run
                .MeasurementCount(100)
                .Run();

            query.Dispose();
        }

        private void CopyFromComponentDataArray_Performance_SmallComponent(int entityCount, bool unique)
        {
            NativeArray<EntityArchetype> archetypes;

            if (unique)
            {
                archetypes = CreateUniqueArchetypes(entityCount);
                for (int entIter = 0; entIter < entityCount; entIter++)
                {
                    m_Manager.CreateEntity(archetypes[entIter]);
                }
            }
            else
            {
                archetypes = CreateUniqueArchetypes(1);
                m_Manager.CreateEntity(archetypes[0],entityCount);
            }

            archetypes.Dispose();

            EntityQuery query = m_Manager.CreateEntityQuery(ComponentType.ReadWrite(typeof(EcsTestData)));

            var result =  query.ToComponentDataArray<EcsTestData>(Allocator.TempJob);
            Assert.AreEqual(entityCount, result.Length);

            Measure.Method(
                    () => { query.CopyFromComponentDataArray(result); })
                .SampleGroup("ToComponentDataArray")
                .WarmupCount(1) // make sure we're not timing job compilation on the first run
                .MeasurementCount(100)
                .Run();

            result.Dispose();
            query.Dispose();
        }

        private void CopyFromComponentDataArray_Performance_LargeComponent(int entityCount, bool unique)
        {
            NativeArray<EntityArchetype> archetypes;

            if (unique)
            {
                archetypes = CreateUniqueArchetypes(entityCount,typeof(LargeComponent));
                for (int entIter = 0; entIter < entityCount; entIter++)
                {
                    m_Manager.CreateEntity(archetypes[entIter]);
                }
            }
            else
            {
                archetypes = CreateUniqueArchetypes(1,typeof(LargeComponent));
                m_Manager.CreateEntity(archetypes[0],entityCount);
            }

            archetypes.Dispose();

            EntityQuery query = default;
            query = m_Manager.CreateEntityQuery(ComponentType.ReadOnly(typeof(LargeComponent)));

            var result =  query.ToComponentDataArray<LargeComponent>(Allocator.TempJob);
            Assert.AreEqual(entityCount, result.Length);

            Measure.Method(
                    () => { query.CopyFromComponentDataArray(result); })
                .SampleGroup("CopyFromComponentDataArray")
                .WarmupCount(1) // make sure we're not timing job compilation on the first run
                .MeasurementCount(100)
                .Run();

            result.Dispose();
            query.Dispose();
        }

        [Test, Performance]
        public void ToEntityArray_Performance_Same([Values(1,100,1000,10000,1000000)] int entityCount)
        {
            ToEntityArray_Performance(entityCount,false);
        }

        [Test, Performance]
        public void ToEntityArray_Performance_Unique([Values(1,10,100,1000,10000)] int entityCount)
        {
            ToEntityArray_Performance(entityCount,true);
        }

        [Test, Performance]
        public void ToComponentDataArray_Performance_Same_SmallComponent(
            [Values(1,100,1000,10000,1000000)] int entityCount)
        {
            ToComponentDataArray_Performance_SmallComponent(entityCount,false);
        }

        [Test, Performance]
        public void ToComponentDataArray_Performance_Same_LargeComponent(
            [Values(1,100,1000,10000,1000000)] int entityCount)
        {
            ToComponentDataArray_Performance_LargeComponent(entityCount,false);
        }

        [Test, Performance]
        public void ToComponentDataArray_Performance_Unique_SmallComponent([Values(1,10,100,1000,10000)] int entityCount)
        {
            ToComponentDataArray_Performance_SmallComponent(entityCount,true);
        }

        [Test, Performance]
        public void ToComponentDataArray_Performance_Unique_LargeComponent([Values(1,10,100,1000,10000)] int entityCount)
        {
            ToComponentDataArray_Performance_LargeComponent(entityCount,true);
        }

        [Test, Performance]
        public void CopyFromComponentDataArray_Performance_Same_SmallComponent([Values(1,100,1000,10000,1000000)] int entityCount)
        {
            CopyFromComponentDataArray_Performance_SmallComponent(entityCount,false);
        }

        [Test, Performance]
        public void CopyFromComponentDataArray_Performance_Same_LargeComponent([Values(1,100,1000,10000,1000000)] int entityCount)
        {
            CopyFromComponentDataArray_Performance_LargeComponent(entityCount,false);
        }

        [Test, Performance]
        public void CopyFromComponentDataArray_Performance_Unique_SmallComponent([Values(1,10,100,1000,10000)] int entityCount)
        {
            CopyFromComponentDataArray_Performance_SmallComponent(entityCount,true);
        }

        [Test, Performance]
        public void CopyFromComponentDataArray_Performance_Unique_LargeComponent([Values(1,10,100,1000,10000)] int entityCount)
        {
            CopyFromComponentDataArray_Performance_LargeComponent(entityCount,true);
        }

    }
}
