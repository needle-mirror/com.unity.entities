#if !UNITY_DOTSRUNTIME // These tests are broken in AOT builds
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
    public void WithEntitiesRun([Values] bool withoutBurst)
    {
        TestSystem.WithEntitiesRun(withoutBurst);
    }

    [Test]
    public void WithEntitiesSchedule([Values] bool withoutBurst)
    {
        TestSystem.WithEntitiesSchedule(withoutBurst);
    }

    [Test]
    public void WithEntitiesSharedFilterRun([Values] bool withoutBurst)
    {
        TestSystem.WithEntitiesSharedFilterRun(withoutBurst);
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

            return CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(new[]{entity0, entity2}, ref World.UpdateAllocator);
        }

        public void WithEntitiesRun(bool withoutBurst)
        {
            var array = FilteredArray101();
            var sum = 0;

            if (withoutBurst)
            {
                var em = EntityManager;
                Entities
                    .WithFilter(array)
                    .WithoutBurst()
                    .ForEach((ref EcsTestData e1) =>
                    {
                        sum += e1.value;
                        Check(em);
                    })
                    .Run();
            }
            else
            {
                Entities.WithFilter(array)
                    .ForEach((ref EcsTestData e1) =>
                    {
                        sum += e1.value;
                    })
                    .Run();
            }

            Assert.AreEqual(101, sum);
            array.Dispose();
        }

        public unsafe void WithEntitiesSharedFilterRun(bool withoutBurst)
        {
            var entity0 = EntityManager.CreateEntity(typeof(EcsTestData));
            EntityManager.SetComponentData(entity0, new EcsTestData(1));
            EntityManager.AddSharedComponentData(entity0, new SharedData1(1));

            var entity1 = EntityManager.CreateEntity(typeof(EcsTestData));
            EntityManager.SetComponentData(entity1, new EcsTestData(10));
            EntityManager.AddSharedComponentData(entity1, new SharedData1(1));

            var entity2 = EntityManager.CreateEntity(typeof(EcsTestData));
            EntityManager.SetComponentData(entity2, new EcsTestData(100));
            EntityManager.AddSharedComponentData(entity2, new SharedData1(2));

            using (var array = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(new[] {entity0, entity2}, ref World.UpdateAllocator))
            {
                var sum = 0;
                if (withoutBurst)
                {
                    var em = EntityManager;
                    Entities
                        .WithFilter(array)
                        .WithoutBurst()
                        .WithSharedComponentFilter(new SharedData1(1))
                        .ForEach((ref EcsTestData e1) =>
                        {
                            sum += e1.value;
                            Check(em);
                        })
                        .Run();
                }
                else
                {
                    Entities
                        .WithFilter(array)
                        .WithSharedComponentFilter(new SharedData1(1))
                        .ForEach((ref EcsTestData e1) => { sum += e1.value; })
                        .Run();
                }
                Assert.AreEqual(1, sum);
            }
        }

        public void WithEntitiesSchedule(bool withoutBurst)
        {
            var array = FilteredArray101();
            var value = new NativeReference<int>(World.UpdateAllocator.ToAllocator);

            if (withoutBurst)
            {
                var em = EntityManager;
                Entities
                    .WithFilter(array)
                    .WithoutBurst()
                    .ForEach((ref EcsTestData e1) =>
                    {
                        value.Value += e1.value;
                        Check(em);
                    })
                    .Schedule();
            }
            else
            {
                Entities
                    .WithFilter(array)
                    .ForEach((ref EcsTestData e1) =>
                    {
                        value.Value += e1.value;
                    })
                    .Schedule();
            }

            CompleteDependency();
            Assert.AreEqual(101, value.Value);
            array.Dispose();
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

        static void Check(EntityManager em)
        {
            Assert.Throws<InvalidOperationException>(() => em.CreateEntity());
        }

        protected override void OnUpdate()
        {
        }
    }
 }
#endif
