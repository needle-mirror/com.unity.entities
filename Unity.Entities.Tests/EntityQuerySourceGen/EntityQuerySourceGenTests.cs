using NUnit.Framework;
using Unity.Collections;

namespace Unity.Entities.Tests.EntityQuerySourceGen
{
    [TestFixture]
    partial class EntityQueryCodeGenTests : ECSTestsFixture
    {
        partial class MyTestSystem  : SystemBase
        {
            Entity _entityWithEcsTestData1;
            Entity _entityWithEcsTestData2;
            Entity _entityWithEcsTestData3;
            Entity _entityWithEcsTestData4AndChunkComponentData;
            Entity _entityWithEcsTestDatas123;

            protected override void OnUpdate() {}

            public void SetUp()
            {
                EntityManager.DestroyEntity(EntityManager.UniversalQuery);

                _entityWithEcsTestData1 = EntityManager.CreateEntity(ComponentType.ReadWrite<EcsTestData>());
                _entityWithEcsTestData2 = EntityManager.CreateEntity(ComponentType.ReadWrite<EcsTestData2>());
                _entityWithEcsTestData3 = EntityManager.CreateEntity(ComponentType.ReadWrite<EcsTestData3>());
                _entityWithEcsTestData4AndChunkComponentData = EntityManager.CreateEntity(ComponentType.ReadWrite<EcsTestData4>(), ComponentType.ChunkComponent<EcsTestData5>());
                _entityWithEcsTestDatas123 = EntityManager.CreateEntity(ComponentType.ReadWrite<EcsTestData>(), ComponentType.ReadWrite<EcsTestData2>(), ComponentType.ReadWrite<EcsTestData3>());
            }

            public void WithAll()
            {
                Entities.WithAll<EcsTestData, EcsTestData2>().AddComponent<EcsTestData5>();

                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestDatas123));

                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData1));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData2));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData3));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData4AndChunkComponentData));
            }

            public void WithAny()
            {
                Entities.WithAny<EcsTestData, EcsTestData2>().AddComponent<EcsTestData5>();

                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData1));
                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData2));
                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestDatas123));

                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData3));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData4AndChunkComponentData));
            }

            public void WithNone()
            {
                Entities.WithNone<EcsTestData, EcsTestData2>().AddComponent<EcsTestData5>();

                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData3));
                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData4AndChunkComponentData));

                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData1));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData2));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestDatas123));
            }
            public void WithAny_WithNone()
            {
                Entities.WithAny<EcsTestData, EcsTestData2>().WithNone<EcsTestData3>().AddComponent<EcsTestData5>();

                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData1));
                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData2));

                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData3));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData4AndChunkComponentData));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestDatas123));
            }

            public void WithChangeFilter()
            {
                AfterUpdateVersioning();
                BeforeUpdateVersioning();

                EntityManager.SetComponentData(_entityWithEcsTestData3, new EcsTestData3(1));
                EntityManager.AddComponentData(_entityWithEcsTestData4AndChunkComponentData, new EcsTestData3(1));

                Entities.WithChangeFilter<EcsTestData3>().AddComponent<EcsTestData5>();

                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData3));
                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData4AndChunkComponentData));

                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData1));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData2));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestDatas123));
            }

            public void AddComponent_Generic()
            {
                Entities.WithAll<EcsTestData>().AddComponent<EcsTestData5>();

                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData1));
                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestDatas123));

                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData2));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData3));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData3));
            }

            public void AddComponent_Type()
            {
                Entities.WithAll<EcsTestData>().AddComponent(typeof(EcsTestData5));

                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData1));
                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestDatas123));

                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData2));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData3));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData4AndChunkComponentData));
            }

            public void AddComponent_Types()
            {
                Entities.WithAll<EcsTestData>().AddComponent(new ComponentTypeSet(ComponentType.ReadOnly<EcsTestData4>(), ComponentType.ReadOnly<EcsTestData5>()));

                Assert.IsTrue(EntityManager.HasComponent<EcsTestData4>(_entityWithEcsTestData1));
                Assert.IsTrue(EntityManager.HasComponent<EcsTestData4>(_entityWithEcsTestDatas123));

                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData1));
                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestDatas123));

                Assert.IsFalse(EntityManager.HasComponent<EcsTestData4>(_entityWithEcsTestData2));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData4>(_entityWithEcsTestData3));

                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData2));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData3));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData4AndChunkComponentData));
            }

            public void AddComponentData()
            {
                var ecsTestData5s = new NativeArray<EcsTestData5>(new[] { new EcsTestData5(), new EcsTestData5() }, Allocator.Temp);

                Entities.WithAll<EcsTestData>().AddComponentData(ecsTestData5s);

                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData1));
                Assert.IsTrue(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestDatas123));

                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData2));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData3));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestData5>(_entityWithEcsTestData4AndChunkComponentData));

                ecsTestData5s.Dispose();
            }

            public void AddChunkComponentData()
            {
                Entities.WithAll<EcsTestData>().AddChunkComponentData(new EcsTestData5(1));

                Assert.IsTrue(EntityManager.HasChunkComponent<EcsTestData5>(_entityWithEcsTestData1));
                Assert.IsTrue(EntityManager.HasChunkComponent<EcsTestData5>(_entityWithEcsTestDatas123));

                Assert.IsFalse(EntityManager.HasChunkComponent<EcsTestData5>(_entityWithEcsTestData2));
                Assert.IsFalse(EntityManager.HasChunkComponent<EcsTestData5>(_entityWithEcsTestData3));
            }

            public void RemoveChunkComponentData()
            {
                Entities.WithAll<EcsTestData4>().RemoveChunkComponentData<EcsTestData5>();
                Assert.IsFalse(EntityManager.HasChunkComponent<EcsTestData5>(_entityWithEcsTestData4AndChunkComponentData));
            }

            public void AddSharedComponentData()
            {
                Entities.WithAll<EcsTestData>().AddSharedComponent(new EcsTestSharedComp());

                Assert.IsTrue(EntityManager.HasComponent<EcsTestSharedComp>(_entityWithEcsTestData1));
                Assert.IsTrue(EntityManager.HasComponent<EcsTestSharedComp>(_entityWithEcsTestDatas123));

                Assert.IsFalse(EntityManager.HasComponent<EcsTestSharedComp>(_entityWithEcsTestData2));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestSharedComp>(_entityWithEcsTestData3));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestSharedComp>(_entityWithEcsTestData4AndChunkComponentData));
            }

            public void RemoveComponent()
            {
                Entities.WithAll<EcsTestData>().RemoveComponent<EcsTestData3>();

                Assert.IsFalse(EntityManager.HasComponent<EcsTestData3>(_entityWithEcsTestDatas123));
                Assert.IsTrue(EntityManager.HasComponent<EcsTestData3>(_entityWithEcsTestData3));
            }

            public void SetSharedComponentData()
            {
                Entities.WithAll<EcsTestData>().AddSharedComponent(new EcsTestSharedComp());
                Entities.WithAll<EcsTestData>().SetSharedComponent(new EcsTestSharedComp());

                Assert.IsTrue(EntityManager.HasComponent<EcsTestSharedComp>(_entityWithEcsTestData1));
                Assert.IsTrue(EntityManager.HasComponent<EcsTestSharedComp>(_entityWithEcsTestDatas123));

                Assert.IsFalse(EntityManager.HasComponent<EcsTestSharedComp>(_entityWithEcsTestData2));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestSharedComp>(_entityWithEcsTestData3));
                Assert.IsFalse(EntityManager.HasComponent<EcsTestSharedComp>(_entityWithEcsTestData4AndChunkComponentData));
            }

            public void ToQuery()
            {
                var entityQuery = Entities.WithAll<EcsTestData>().ToQuery();
                Assert.AreEqual(expected: 2, actual: entityQuery.CalculateEntityCount());
            }

            public void DestroyEntity()
            {
                Entities.WithAll<EcsTestData>().DestroyEntity();

                Assert.IsFalse(EntityManager.Exists(_entityWithEcsTestData1));
                Assert.IsFalse(EntityManager.Exists(_entityWithEcsTestDatas123));

                Assert.IsTrue(EntityManager.Exists(_entityWithEcsTestData2));
                Assert.IsTrue(EntityManager.Exists(_entityWithEcsTestData3));
                Assert.IsTrue(EntityManager.Exists(_entityWithEcsTestData4AndChunkComponentData));
            }
        }

        MyTestSystem _testSystem;

        [SetUp]
        public void SetUp()
        {
            _testSystem = World.GetOrCreateSystemManaged<MyTestSystem>();
            _testSystem.SetUp();
        }

        [Test]
        public void WithAll() => _testSystem.WithAll();

        [Test]
        public void WithAny() => _testSystem.WithAny();

        [Test]
        public void WithNone() => _testSystem.WithNone();

        [Test]
        public void WithAny_WithNone() => _testSystem.WithAny_WithNone();

        [Test]
        public void WithChangeFilter() => _testSystem.WithChangeFilter();

        [Test]
        public void AddComponent_Generic() => _testSystem.AddComponent_Generic();

        [Test]
        public void AddComponent_Type() => _testSystem.AddComponent_Type();

        [Test]
        public void AddComponent_Types() => _testSystem.AddComponent_Types();

        [Test]
        public void AddComponentData() => _testSystem.AddComponentData();

        [Test]
        public void AddChunkComponentData() => _testSystem.AddChunkComponentData();

        [Test]
        public void RemoveChunkComponentData() => _testSystem.RemoveChunkComponentData();

        [Test]
        public void AddSharedComponentData() => _testSystem.AddSharedComponentData();

        [Test]
        public void RemoveComponent() => _testSystem.RemoveComponent();

        [Test]
        public void SetSharedComponentData() => _testSystem.SetSharedComponentData();

        [Test]
        public void ToQuery() => _testSystem.ToQuery();

        [Test]
        public void DestroyEntity() => _testSystem.DestroyEntity();
    }
}
