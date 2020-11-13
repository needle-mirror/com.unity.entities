#if ROSLYN_SOURCEGEN_ENABLED
using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Tests;

partial class ForEachWithFilterEntityArrayTests : ECSTestsFixture
{
    MyTestSystem TestSystem;

    [SetUp]
    public void SetUp()
    {
        TestSystem = World.GetOrCreateSystem<MyTestSystem>();
    }

    [Test]
    public void WithEntitiesRunWithoutBurst()
    {
        TestSystem.WithEntitiesRunWithoutBurst();
    }

    [Test]
    public void WithEntitiesRun()
    {
        TestSystem.WithEntitiesRun();
    }

    [Test]
    public void WithEntitiesSchedule()
    {
        TestSystem.WithEntitiesSchedule();
    }

    [Test]
    public void WithEntitiesSharedFilterRun()
    {
        TestSystem.WithEntitiesSharedFilterRun();
    }

    [Test]
    public void WithEntitiesRunStructuralChange()
    {
        TestSystem.WithEntitiesRunStructuralChange();
    }

    partial class MyTestSystem : SystemBase
    {
        NativeArray<Entity> FilteredArray101()
        {
            var entity0 = EntityManager.CreateEntity(typeof(EcsTestData));
            EntityManager.SetComponentData(entity0, new EcsTestData(1));
            var entity1 = EntityManager.CreateEntity(typeof(EcsTestData));
            EntityManager.SetComponentData(entity1, new EcsTestData(10));
            var entity2 = EntityManager.CreateEntity(typeof(EcsTestData));
            EntityManager.SetComponentData(entity2, new EcsTestData(100));

            return new NativeArray<Entity>(new[]{entity0, entity2}, Allocator.TempJob);
        }

        public void WithEntitiesRunWithoutBurst()
        {
            var array = FilteredArray101();

            var em = EntityManager;
            int sum = 0;
            Entities
                .WithFilter(array)
                .WithoutBurst()
                .ForEach((ref EcsTestData e1) =>
                {
                    sum += e1.value;
                    Check(EntityManager);
                })
                .Run();
            Assert.AreEqual(101, sum);
            array.Dispose();
        }

        public void WithEntitiesRun()
        {
            var array = FilteredArray101();

            var em = EntityManager;
            int sum = 0;
            Entities
                .WithFilter(array)
                .ForEach((ref EcsTestData e1) =>
                {
                    sum += e1.value;
                })
                .Run();
            Assert.AreEqual(101, sum);
            array.Dispose();
        }
        unsafe public void WithEntitiesSharedFilterRun()
        {
            var entity0 = EntityManager.CreateEntity(typeof(EcsTestData));
            EntityManager.SetComponentData(entity0, new EcsTestData(1));
            EntityManager.AddSharedComponentData(entity0, new SharedData1(1));

            var entity1 = EntityManager.CreateEntity(typeof(EcsTestData));
            EntityManager.SetComponentData(entity1, new EcsTestData(10));
            EntityManager.AddSharedComponentData(entity0, new SharedData1(1));

            var entity2 = EntityManager.CreateEntity(typeof(EcsTestData));
            EntityManager.SetComponentData(entity2, new EcsTestData(100));
            EntityManager.AddSharedComponentData(entity0, new SharedData1(2));

            var array = new NativeArray<Entity>(new[]{entity0, entity2}, Allocator.TempJob);

            var em = EntityManager;
            int sum = 0;
            Entities
                .WithFilter(array)
                .WithSharedComponentFilter(new SharedData1(1))
                .ForEach((ref EcsTestData e1) =>
                {
                    sum += e1.value;
                    Check(em);
                })
                .Run();
            Assert.AreEqual(1, sum);
            array.Dispose();
        }
        public void WithEntitiesSchedule()
        {
            var array = FilteredArray101();

            var em = EntityManager;
            var value = new NativeReference<int>(Allocator.TempJob);
            Entities
                .WithFilter(array)
                .WithoutBurst()
                .ForEach((ref EcsTestData e1) =>
                {
                    value.Value += e1.value;
                    Check(em);
                })
                .Schedule();
            CompleteDependency();
            Assert.AreEqual(101, value.Value);
            array.Dispose();
            value.Dispose();
        }

        public void WithEntitiesRunStructuralChange()
        {
            var array = FilteredArray101();

            var em = EntityManager;
            int sum = 0;
            Entities
                .WithFilter(array)
                .WithStructuralChanges()
                .ForEach((Entity entity, ref EcsTestData e1) =>
                {
                    sum += e1.value;
                    em.DestroyEntity(entity);
                })
                .Run();
            Assert.AreEqual(101, sum);
            array.Dispose();

            Assert.AreEqual(1, EntityManager.Debug.EntityCount);
        }

        public static void Check(EntityManager em)
        {
            Assert.Throws<InvalidOperationException>(() => em.CreateEntity());
        }

        protected override void OnUpdate()
        {
        }
    }
 }
#endif
