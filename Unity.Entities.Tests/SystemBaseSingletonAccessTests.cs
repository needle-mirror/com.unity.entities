
using System;
#if !UNITY_DOTSPLAYER
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
#pragma warning disable 649

namespace Unity.Entities.Tests
{
    class SystemBaseSingletonAccessTests : ECSTestsFixture
    {
        SystemBase_TestSystem TestSystem;

        [SetUp]
        public void SetUp()
        {
            TestSystem = World.GetOrCreateSystem<SystemBase_TestSystem>();
        }
        
        public class SystemBase_TestSystem : SystemBase
        {
            protected override void OnUpdate() {}
            
            public void GetSetSingleton()
            {
                EntityManager.CreateEntity(typeof(EcsTestData));

                SetSingleton(new EcsTestData(10));
                Assert.AreEqual(10, GetSingleton<EcsTestData>().value);
            }
            
            public void GetSetSingletonMultipleComponents()
            {
                var entity = EntityManager.CreateEntity(typeof(EcsTestData3), typeof(EcsTestData), typeof(EcsTestData2));

                EntityManager.SetComponentData(entity, new EcsTestData(10));
                Assert.AreEqual(10, GetSingleton<EcsTestData>().value);
                
                SetSingleton(new EcsTestData2(100));
                Assert.AreEqual(100, EntityManager.GetComponentData<EcsTestData2>(entity).value0);
            }
            
            public void GetSetSingletonInEntitiesForEach()
            {
                EntityManager.CreateEntity(typeof(EcsTestData2));
                EntityManager.CreateEntity(typeof(EcsTestData));

                Entities.WithoutBurst().ForEach((in EcsTestData2 data2) => { SetSingleton(new EcsTestData(10)); }).Run();
                int value = 0;
                Entities.WithoutBurst().ForEach((in EcsTestData2 data2) => { value = GetSingleton<EcsTestData>().value; }).Run();

                Assert.AreEqual(10, value);
            }

            public void GetSetSingletonZeroThrows()
            {
                Assert.Throws<InvalidOperationException>(() => SetSingleton(new EcsTestData()));
                Assert.Throws<InvalidOperationException>(() => GetSingleton<EcsTestData>());
            }
            
            // Throws due to a singleton component only being allowed on a single Entity
            public void GetSetSingletonMultipleThrows()
            {
                EntityManager.CreateEntity(typeof(EcsTestData));
                EntityManager.CreateEntity(typeof(EcsTestData));

                Assert.Throws<InvalidOperationException>(() => SetSingleton(new EcsTestData()));
                Assert.Throws<InvalidOperationException>(() => GetSingleton<EcsTestData>());
            }
            
            public void RequireSingletonWorks()
            {
                RequireSingletonForUpdate<EcsTestData>();
                GetEntityQuery(typeof(EcsTestData2));
                
                EntityManager.CreateEntity(typeof(EcsTestData2));
                Assert.IsFalse(ShouldRunSystem());
                EntityManager.CreateEntity(typeof(EcsTestData));
                Assert.IsTrue(ShouldRunSystem());
            }

            public void HasSingletonWorks()
            {
                Assert.IsFalse(HasSingleton<EcsTestData>());
                EntityManager.CreateEntity(typeof(EcsTestData));
                Assert.IsTrue(HasSingleton<EcsTestData>());
            }

            public void HasSingleton_ReturnsTrueWithEntityWithOnlyComponent()
            {
                Assert.IsFalse(HasSingleton<EcsTestData>());
                
                EntityManager.CreateEntity(typeof(EcsTestData));
                Assert.IsTrue(HasSingleton<EcsTestData>());
                
                EntityManager.CreateEntity(typeof(EcsTestData));
                Assert.IsFalse(HasSingleton<EcsTestData>());
            }
            
            public void GetSingletonEntityWorks()
            {
                var entity = EntityManager.CreateEntity(typeof(EcsTestData));

                var singletonEntity = GetSingletonEntity<EcsTestData>();
                Assert.AreEqual(entity, singletonEntity);
            }

            public void GetSingletonThroughQueryWorks()
            {
                EntityQuery query = GetEntityQuery(ComponentType.ReadOnly<EcsTestData>(), ComponentType.ReadOnly<EcsTestData2>());
                RequireForUpdate(query);
                var entity = EntityManager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));
                EntityManager.SetComponentData(entity, new EcsTestData() { value = 3 });
                
                Assert.AreEqual(3, query.GetSingleton<EcsTestData>().value);
            }

    #if !UNITY_DISABLE_MANAGED_COMPONENTS
            public void GetSetSingleton_ManagedComponents()
            {
                var entity = EntityManager.CreateEntity(typeof(EcsTestManagedComponent));

                const string kTestVal = "SomeString";
                this.SetSingleton(new EcsTestManagedComponent() { value = kTestVal });
                Assert.AreEqual(kTestVal, this.GetSingleton<EcsTestManagedComponent>().value);
            }

            public void HasSingletonWorks_ManagedComponents()
            {
                Assert.IsFalse(this.HasSingleton<EcsTestManagedComponent>());
                EntityManager.CreateEntity(typeof(EcsTestManagedComponent));
                Assert.IsTrue(this.HasSingleton<EcsTestManagedComponent>());
            }
    #endif
        }

        [Test]
        public void SystemBase_GetSetSingleton()
        {
            TestSystem.GetSetSingleton();
        }
        
        [Test]
        public void SystemBase_GetSetSingletonMultipleComponents()
        {
            TestSystem.GetSetSingletonMultipleComponents();
        }
        
        [Test]
        public void SystemBase_GetSetSingletonInEntitiesForEach()
        {
            TestSystem.GetSetSingletonInEntitiesForEach();
        }

        [Test]
        public void SystemBase_GetSetSingletonZeroThrows()
        {
            TestSystem.GetSetSingletonZeroThrows();
        }
        
        [Test]
        public void SystemBase_GetSetSingletonMultipleThrows()
        {
            TestSystem.GetSetSingletonMultipleThrows();
        }
        
        [Test]
        public void SystemBase_RequireSingletonWorks()
        {
            TestSystem.RequireSingletonWorks();
        }

        [Test]
        public void SystemBase_HasSingletonWorks()
        {
            TestSystem.HasSingletonWorks();
        }

        [Test]
        public void SystemBase_HasSingleton_ReturnsTrueWithEntityWithOnlyComponent()
        {
            TestSystem.HasSingleton_ReturnsTrueWithEntityWithOnlyComponent();
        }
        
        [Test]
        public void SystemBase_GetSingletonEntityWorks()
        {
            TestSystem.GetSingletonEntityWorks();
        }

        [Test]
        public void SystemBase_GetSingletonThroughQueryWorks()
        {
            TestSystem.GetSingletonThroughQueryWorks();
        }

#if !UNITY_DISABLE_MANAGED_COMPONENTS
        [Test]
        public void SystemBase_GetSetSingleton_ManagedComponents()
        {
            TestSystem.GetSetSingleton_ManagedComponents();
        }

        [Test]
        public void SystemBase_HasSingletonWorks_ManagedComponents()
        {
            TestSystem.HasSingletonWorks_ManagedComponents();
        }
#endif
    }
}
#endif
