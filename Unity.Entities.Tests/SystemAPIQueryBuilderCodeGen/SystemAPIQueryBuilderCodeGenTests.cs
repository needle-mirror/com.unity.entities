using NUnit.Framework;

namespace Unity.Entities.Tests.SystemAPIQueryBuilderCodeGen
{
    [TestFixture]
    partial class SystemAPIQueryBuilderCodeGenTests : ECSTestsFixture
    {
        MyTestSystem _testSystem;

        [SetUp]
        public void SetUp()
        {
            _testSystem = World.GetOrCreateSystemManaged<MyTestSystem>();
        }

        [Test] public void WithAll_WithAny_WithNone_WithOptions_Test() => _testSystem.WithAll_WithAny_WithNone_WithOptions();
        [Test] public void CreateTwoArchetypeQueries_WithRO_AndRW_Test() => _testSystem.CreateArchetypeQueries_WithRO_AndRW();
        [Test] public void CreateMultipleArchetypeQueries_Test() => _testSystem.CreateMultipleArchetypeQueries();
        [Test] public void ChainedWithEntityQueryMethodAfterBuilding_Test() => _testSystem.ChainedWithEntityQueryMethodAfterBuilding();

        partial class MyTestSystem : SystemBase
        {
            public unsafe void WithAll_WithAny_WithNone_WithOptions()
            {
                var query = SystemAPI.QueryBuilder().WithAll<EcsTestData>().WithAny<EcsTestData2>().WithNone<EcsTestData3>().WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build();
                var queryData = query._GetImpl()->_QueryData;

                Assert.AreEqual(1, queryData->ArchetypeQueryCount);

                var archetypeQuery = queryData->ArchetypeQueries[0];

                Assert.AreEqual(1, archetypeQuery.AllCount);
                Assert.AreEqual(1, archetypeQuery.NoneCount);
                Assert.AreEqual(1, archetypeQuery.AnyCount);

                Assert.AreEqual(ComponentType.ReadOnly<EcsTestData>().TypeIndex, archetypeQuery.All[0]);
                Assert.AreEqual(ComponentType.ReadOnly<EcsTestData2>().TypeIndex, archetypeQuery.Any[0]);
                Assert.AreEqual(ComponentType.ReadOnly<EcsTestData3>().TypeIndex, archetypeQuery.None[0]);

                Assert.AreEqual(EntityQueryOptions.IncludeDisabledEntities, archetypeQuery.Options);
            }

            public unsafe void CreateArchetypeQueries_WithRO_AndRW()
            {
                var query =
                    SystemAPI.QueryBuilder()
                        .WithAll<EcsTestData>().WithNone<EcsTestData2>()
                        .AddAdditionalQuery()
                        .WithAllRW<EcsTestData2>().WithAnyRW<EcsTestData3, EcsTestData4>()
                        .Build();

                var queryData = query._GetImpl()->_QueryData;

                Assert.AreEqual(2, queryData->ArchetypeQueryCount);

                var archetypeQuery1 = queryData->ArchetypeQueries[0];
                Assert.AreEqual(1, archetypeQuery1.AllCount);
                Assert.AreEqual(1, archetypeQuery1.NoneCount);
                Assert.AreEqual(0, archetypeQuery1.AnyCount);

                Assert.AreEqual(ComponentType.ReadOnly<EcsTestData>().TypeIndex, archetypeQuery1.All[0]);
                Assert.AreEqual(ComponentType.ReadOnly<EcsTestData2>().TypeIndex, archetypeQuery1.None[0]);

                var archetypeQuery2 = queryData->ArchetypeQueries[1];

                Assert.AreEqual(1, archetypeQuery2.AllCount);
                Assert.AreEqual(0, archetypeQuery2.NoneCount);
                Assert.AreEqual(2, archetypeQuery2.AnyCount);

                Assert.AreEqual(ComponentType.ReadWrite<EcsTestData2>().TypeIndex, archetypeQuery2.All[0]);
                Assert.AreEqual(ComponentType.ReadWrite<EcsTestData3>().TypeIndex, archetypeQuery2.Any[0]);
                Assert.AreEqual(ComponentType.ReadWrite<EcsTestData4>().TypeIndex, archetypeQuery2.Any[1]);
            }

            public unsafe void CreateMultipleArchetypeQueries()
            {
                var query =
                    SystemAPI.QueryBuilder()
                        .WithAll<EcsTestData>()
                        .AddAdditionalQuery()
                        .WithNone<EcsTestData2>()
                        .AddAdditionalQuery()
                        .WithAny<EcsTestData3>()
                        .AddAdditionalQuery()
                        .WithOptions(EntityQueryOptions.IncludePrefab)
                        .Build();

                Assert.AreEqual(4, query._GetImpl()->_QueryData->ArchetypeQueryCount);
            }

            public void ChainedWithEntityQueryMethodAfterBuilding()
            {
                var entity = EntityManager.CreateEntity(ComponentType.ReadOnly<EcsTestData>(), ComponentType.ReadOnly<EcsTestData2>());

                int numEntitiesMatchingQuery =
                    SystemAPI.QueryBuilder()
                        .WithNone<EcsTestData3>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .WithAny<EcsTestData2>()
                        .WithAll<EcsTestData>()
                        .Build()
                        .CalculateEntityCount();

                Assert.AreEqual(1,numEntitiesMatchingQuery);

                EntityManager.DestroyEntity(entity);
            }

            protected override void OnUpdate()
            {
            }
        }
    }
}
