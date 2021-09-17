using NUnit.Framework;
using System;
#if !NET_DOTS
using System.Text.RegularExpressions;
#endif
using UnityEngine;
using UnityEngine.TestTools;

//@TODO: We should really design systems / jobs / exceptions / errors
//       so that an error in one system does not affect the next system.
//       Right now failure to set dependencies correctly in one system affects other code,
//       this makes the error messages significantly less useful...
//       So need to redo all tests accordingly

namespace Unity.Entities.Tests
{
    partial class SafetyTests : ECSTestsFixture
    {
        [Test]
        public void RemoveEntityComponentThrows()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            Assert.Throws<ArgumentException>(() => { m_Manager.RemoveComponent(entity, typeof(Entity)); });
            Assert.IsTrue(m_Manager.HasComponent<EcsTestData>(entity));
        }

        [Test]
        public void GetSetComponentThrowsIfNotExist()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            var destroyedEntity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.DestroyEntity(destroyedEntity);

            Assert.Throws<System.ArgumentException>(() => { m_Manager.SetComponentData(entity, new EcsTestData2()); });
            Assert.Throws<System.ArgumentException>(() => { m_Manager.SetComponentData(destroyedEntity, new EcsTestData2()); });

            Assert.Throws<System.ArgumentException>(() => { m_Manager.GetComponentData<EcsTestData2>(entity); });
            Assert.Throws<System.ArgumentException>(() => { m_Manager.GetComponentData<EcsTestData2>(destroyedEntity); });
        }

        [Test]
        public void ComponentDataArrayFromEntityThrowsIfNotExist()
        {
            var entity = m_Manager.CreateEntity(typeof(EcsTestData));
            var destroyedEntity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.DestroyEntity(destroyedEntity);

            var data = EmptySystem.GetComponentDataFromEntity<EcsTestData2>();

            Assert.Throws<System.ArgumentException>(() => { data[entity] = new EcsTestData2(); });
            Assert.Throws<System.ArgumentException>(() => { data[destroyedEntity] = new EcsTestData2(); });

            Assert.Throws<System.ArgumentException>(() => { var p = data[entity]; });
            Assert.Throws<System.ArgumentException>(() => { var p = data[destroyedEntity]; });
        }

        [Test]
        public void AddComponentTwiceIgnored()
        {
            var entity = m_Manager.CreateEntity();

            m_Manager.AddComponentData(entity, new EcsTestData(1));
            m_Manager.AddComponentData(entity, new EcsTestData(2));

            var testData = m_Manager.GetComponentData<EcsTestData>(entity);
            Assert.AreEqual(testData.value, 2);
        }

#if !NET_DOTS
// https://unity3d.atlassian.net/browse/DOTSR-1432
// EntitiesAssert isn't currently supported

        [Test]
        public void RemoveComponentTwiceIgnored()
        {
            var entity = m_Manager.CreateEntity();

            m_Manager.AddComponent<EcsTestData>(entity);

            EntitiesAssert.ContainsOnly(m_Manager, EntityMatch.Exact<EcsTestData>(entity));
            var removed0 = m_Manager.RemoveComponent<EcsTestData>(entity);
            EntitiesAssert.ContainsOnly(m_Manager, EntityMatch.Exact(entity));
            var removed1 = m_Manager.RemoveComponent<EcsTestData>(entity);
            EntitiesAssert.ContainsOnly(m_Manager, EntityMatch.Exact(entity));

            Assert.That(removed0, Is.True);
            Assert.That(removed1, Is.False);
        }

#endif

        [Test]
        public void RemoveSharedComponentTwiceIgnored()
        {
            var entity = m_Manager.CreateEntity();

            m_Manager.AddSharedComponentData(entity, new EcsTestSharedComp());

            var removed0 = m_Manager.RemoveComponent<EcsTestSharedComp>(entity);
            var removed1 = m_Manager.RemoveComponent<EcsTestSharedComp>(entity);

            Assert.That(removed0, Is.True);
            Assert.That(removed1, Is.False);
        }

        [Test]
        public void RemoveChunkComponentTwiceIgnored()
        {
            var entity = m_Manager.CreateEntity();

            m_Manager.AddChunkComponentData(m_Manager.UniversalQuery, new EcsTestData());

            var removed0 = m_Manager.RemoveChunkComponent<EcsTestData>(entity);
            var removed1 = m_Manager.RemoveChunkComponent<EcsTestData>(entity);

            Assert.That(removed0, Is.True);
            Assert.That(removed1, Is.False);
        }

        [Test]
        public void AddComponentOnDestroyedEntityThrows()
        {
            var destroyedEntity = m_Manager.CreateEntity();
            m_Manager.DestroyEntity(destroyedEntity);
            Assert.Throws<System.InvalidOperationException>(() => { m_Manager.AddComponentData(destroyedEntity, new EcsTestData(1)); });
        }

        [Test]
        public void RemoveComponentOnDestroyedEntityIsIgnored()
        {
            var destroyedEntity = m_Manager.CreateEntity(typeof(EcsTestData));
            m_Manager.DestroyEntity(destroyedEntity);
            m_Manager.RemoveComponent<EcsTestData>(destroyedEntity);
        }

        [Test]
        public void RemoveComponentOnEntityIsIgnored()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.RemoveComponent<EcsTestData>(entity);
        }

        [Test]
        public void RemoveChunkComponentOnEntityWithoutChunkComponentIsIgnored()
        {
            var entity = m_Manager.CreateEntity();
            m_Manager.RemoveChunkComponent<EcsTestData>(entity);
        }

        [Test]
        public void CreateDestroyEmptyEntity()
        {
            var entity = m_Manager.CreateEntity();
            Assert.IsTrue(m_Manager.Exists(entity));
            m_Manager.DestroyEntity(entity);
            Assert.IsFalse(m_Manager.Exists(entity));
        }

        [Test]
        public void NotYetCreatedEntityWithSameVersionThrows()
        {
            var notYetCreatedEntitySameVersion = new Entity() {Index = 0, Version = 1};
            Assert.IsFalse(m_Manager.Exists(notYetCreatedEntitySameVersion));
            Assert.Throws<InvalidOperationException>(() => m_Manager.AddComponentData(notYetCreatedEntitySameVersion , new EcsTestData()));
        }

        [Test]
        public void CreateEntityWithNullTypeThrows()
        {
            Assert.Throws<System.NullReferenceException>(() => m_Manager.CreateEntity(null));
        }

        [Test]
        public void CreateEntityWithOneNullTypeThrows()
        {
            Assert.Throws<System.ArgumentException>(() => m_Manager.CreateEntity(null, typeof(EcsTestData)));
        }

        unsafe struct BigComponentData1 : IComponentData
        {
            public fixed int BigArray[10000];
        }

        unsafe struct BigComponentData2 : IComponentData
        {
            public fixed float BigArray[10000];
        }

        [Test]
        public void CreateTooBigArchetypeThrows()
        {
            Assert.Throws<System.ArgumentException>(() =>
            {
                m_Manager.CreateArchetype(typeof(BigComponentData1), typeof(BigComponentData2));
            });
        }

        struct ReadWriteJob : IJobEntityBatch
        {
            public ComponentTypeHandle<EcsTestData> Blah;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {

            }
        }

        internal partial class DependencyTestSystem : SystemBase
        {
            private EntityQuery m_Query;
            private bool m_BadGuy;

            protected DependencyTestSystem(bool badGuy)
            {
                m_BadGuy = badGuy;
            }

            protected override void OnCreate()
            {
                base.OnCreate();
                m_Query = GetEntityQuery(typeof(EcsTestData));
            }
            protected override void OnUpdate()
            {
                var job = new ReadWriteJob { Blah = EntityManager.GetComponentTypeHandle<EcsTestData>(false) };
                if (m_BadGuy)
                    job.Schedule(m_Query);
                else
                    Dependency = job.Schedule(m_Query, Dependency);
            }
        }

        [UpdateBefore(typeof(CorrectSystem))]
        internal class MisbehavingSystem : DependencyTestSystem
        {
            public MisbehavingSystem() : base(true) { }
        }

        [UpdateBefore(typeof(CorrectSystem))]
        internal partial class NestedBrokenSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                World.GetOrCreateSystem<MisbehavingSystem>().Update();
            }
        }

        internal class CorrectSystem : DependencyTestSystem
        {
            public CorrectSystem() : base(false) { }
        }

        [Test]
        [DotsRuntimeFixme("Debug.LogError is not burst compatible (for safety errors reported from bursted code) and LogAssert.Expect is not properly implemented in DOTS Runtime - DOTS-4294")]
        public void MissedDependencyMakesActionableErrorMessage()
        {
            var arch = World.EntityManager.CreateArchetype(typeof(EcsTestData));
            World.EntityManager.CreateEntity(arch, 5000);
            var g = World.GetOrCreateSystem<SimulationSystemGroup>();
            g.AddSystemToUpdateList(World.GetOrCreateSystem<MisbehavingSystem>());
            g.AddSystemToUpdateList(World.GetOrCreateSystem<CorrectSystem>());
            LogAssert.Expect(LogType.Error,
                "The system Unity.Entities.Tests.SafetyTests+MisbehavingSystem writes Unity.Entities.Tests.EcsTestData" +
                " via SafetyTests:ReadWriteJob but that type was not assigned to the Dependency property. To ensure correct" +
                " behavior of other systems, the job or a dependency must be assigned to the Dependency property before " +
                "returning from the OnUpdate method.");
            World.Update();
        }

        [Test]
        [DotsRuntimeFixme("Debug.LogError is not burst compatible (for safety errors reported from bursted code) and LogAssert.Expect is not properly implemented in DOTS Runtime - DOTS-4294")]
        public void MissedDependencyFromNestedUpdateMakesActionableErrorMessage()
        {
            var arch = World.EntityManager.CreateArchetype(typeof(EcsTestData));
            World.EntityManager.CreateEntity(arch, 5000);
            var g = World.GetOrCreateSystem<SimulationSystemGroup>();
            g.AddSystemToUpdateList(World.GetOrCreateSystem<NestedBrokenSystem>());
            g.AddSystemToUpdateList(World.GetOrCreateSystem<CorrectSystem>());
            LogAssert.Expect(LogType.Error,
                "The system Unity.Entities.Tests.SafetyTests+MisbehavingSystem writes Unity.Entities.Tests.EcsTestData" +
                " via SafetyTests:ReadWriteJob but that type was not assigned to the Dependency property. To ensure correct" +
                " behavior of other systems, the job or a dependency must be assigned to the Dependency property before " +
                "returning from the OnUpdate method.");
            World.Update();
        }

        public partial class ForEachReproSystem : SystemBase
        {
            protected override void OnUpdate()
            {
                float deltaTime = Time.DeltaTime;

                var lookup = GetComponentDataFromEntity<EcsTestData>();

                Entities
                    .WithName("RotationSpeedSystem_ForEach")
                    .ForEach((Entity entity, ref EcsTestData rotation, in EcsTestData2 rotationSpeed) =>
                    {
                        var value = lookup[entity];
                    })
                    .ScheduleParallel();
            }
        }

#if !NET_DOTS
        [Test]
        [DotsRuntimeFixme("Debug.LogError is not burst compatible (for safety errors reported from bursted code) and LogAssert.Expect is not properly implemented in DOTS Runtime - DOTS-4294")]
        public void NoExtraMessageFromForEachSystemRepro()
        {
            var arch = World.EntityManager.CreateArchetype(typeof(EcsTestData), typeof(EcsTestData2));
            World.EntityManager.CreateEntity(arch, 5000);
            var g = World.GetOrCreateSystem<SimulationSystemGroup>();
            g.AddSystemToUpdateList(World.GetOrCreateSystem<ForEachReproSystem>());
            World.Update();
            LogAssert.Expect(LogType.Exception,
            new Regex("^InvalidOperationException: RotationSpeedSystem_ForEach(?:_Job)?\\.JobData\\.lookup is not declared \\[ReadOnly\\] in a IJobParallelFor"+
                " job\\. The container does not support parallel writing\\. Please use a more suitable container type\\.$"));
        }
#endif

    }
}
