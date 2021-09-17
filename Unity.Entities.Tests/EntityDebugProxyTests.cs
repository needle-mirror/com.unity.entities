using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
namespace Unity.Entities.Tests
{
    class EntityDebugProxyTests : ECSTestsFixture
    {
        static Entity CreateEntity(EntityManager em)
        {
            var entity = em.CreateEntity();
            em.SetName(entity, "Test");

            em.AddComponentData(entity, new EcsTestData(1));
            em.AddBuffer<EcsIntElement>(entity).Add(2);
            em.AddSharedComponentData(entity, new EcsTestSharedComp{value = 3});
            #if !UNITY_DISABLE_MANAGED_COMPONENTS
            em.AddComponentData(entity, new EcsTestManagedComponent(){ value = "boing" });
            #endif

            return entity;
        }

        static T Get<T>(object[] array)
        {
            for (int i = 0; i != array.Length; i++)
            {
                if (array[i].GetType() == typeof(T))
                    return (T)array[i];
            }

            return default;
        }

        #if NET_DOTS
        static object[] GetArrayOf<T>(object[] array)
        {
            for (int i = 0; i != array.Length; i++)
            {
                if (array[i] is object[] arr && arr.Length > 0)
                {
                    if (arr[0].GetType() == typeof(T))
                        return arr;
                }
            }

            return default;
        }
        #endif

        static void CheckEntity(Entity entity)
        {
            #if !DOTS_DISABLE_DEBUG_NAMES
                Assert.AreEqual("'Test' Entity(0:1) Test World", EntityDebugProxy.GetDebugName(entity.Index, entity.Version));
            #else
                Assert.AreEqual("'' Entity(0:1) Test World", EntityDebugProxy.GetDebugName(entity.Index, entity.Version));
            #endif
            var proxy = new EntityDebugProxy(entity);
            var components = proxy.Components;
            #if !UNITY_DISABLE_MANAGED_COMPONENTS
            Assert.AreEqual(4, components.Length);
            #else
            Assert.AreEqual(3, components.Length);
            #endif

            Assert.AreEqual(1, Get<EcsTestData>(proxy.Components).value);

            #if !NET_DOTS
            Assert.AreEqual(1, Get<EcsIntElement[]>(proxy.Components).Length);
            Assert.AreEqual(2, Get<EcsIntElement[]>(proxy.Components)[0].Value);
            #else
            // in NET_DOTS, we can't construct an array of the appropriate type, so it shows up
            // as object[].  We hack around it so that we can still verify the internal values.
            Assert.AreEqual(1, GetArrayOf<EcsIntElement>(proxy.Components).Length);
            Assert.AreEqual(2, ((EcsIntElement) GetArrayOf<EcsIntElement>(proxy.Components)[0]).Value);
            #endif

            Assert.AreEqual(3, Get<EcsTestSharedComp>(proxy.Components).value);

            #if !UNITY_DISABLE_MANAGED_COMPONENTS
            Assert.AreEqual("boing", Get<EcsTestManagedComponent>(proxy.Components).value);
            #endif
        }

        [Test]
        public void TestEntityDebugProxyComponents()
        {
            var entity = CreateEntity(m_Manager);
            CheckEntity(entity);
        }

        struct Job : IJob
        {
            public NativeReference<bool> Success;
            public Entity Entity;

            public void Execute()
            {
                CheckEntity(Entity);
                Success.Value = true;
            }
        }

        [Test]
        public void AccessDebugProxyFromJob()
        {
            var entity = CreateEntity(m_Manager);
            var job = new Job
            {
                Entity = entity,
                Success = new NativeReference<bool>(false, Allocator.Persistent)
            };
            job.Schedule().Complete();
            Assert.IsTrue(job.Success.Value);
            job.Success.Dispose();
        }

        partial class CheckDebugProxyWorldSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                var entity = EntityManager.CreateEntity();
                Assert.AreEqual(World, EntityDebugProxy.GetWorld(entity));
            }
        }

        [Test]
        public void DisambiguateWorldBasedOnExecutingSystem()
        {
            var world = new World("TestWorld2");

            world.GetOrCreateSystem<CheckDebugProxyWorldSystem>().Update();
            World.GetOrCreateSystem<CheckDebugProxyWorldSystem>().Update();

            world.Dispose();
        }
    }
}
