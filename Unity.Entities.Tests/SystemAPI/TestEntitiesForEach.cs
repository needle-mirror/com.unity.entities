#if !UNITY_DOTSRUNTIME
using System;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Transforms;

namespace Unity.Entities.Tests.TestSystemAPI
{
    [TestFixture]
    public partial class TestEntitiesForEach : ECSTestsFixture
    {
        [SetUp]
        public void SetUp()
        {
            World.CreateSystem<TestTime>();
            World.CreateSystem<TestGetAspectRW>();
            World.CreateSystem<TestGetAspectRO>();
        }

        #region Time
        [Test]
        public void Time() => World.GetExistingSystem<TestTime>().Update(World.Unmanaged);
        #endregion

        #region Aspect
        [Test]
        public void GetAspectRW() => World.GetExistingSystem<TestGetAspectRW>().Update(World.Unmanaged);

        [Test]
        public void GetAspectRO() => World.GetExistingSystem<TestGetAspectRO>().Update(World.Unmanaged);
        #endregion
    }


    #region Time
    partial class TestTime : SystemBase {
        protected override void OnCreate() {}
        protected override void OnDestroy() {}
        protected override void OnUpdate() {
            EntityManager.CreateEntity();
            var time = World.Time;
            Entities.ForEach(() => Assert.AreEqual(time, SystemAPI.Time)).WithoutBurst().ScheduleParallel();
        }
    }
    #endregion

    #region Aspect

    readonly partial struct TestAspect : IAspect
    {
        public readonly RefRW<LocalTransform> Transform;
        public void Move(float3 newPosition)
        {
            Transform.ValueRW.Position = newPosition;
        }
    }

    partial class TestGetAspectRW : SystemBase {
        protected override void OnCreate() {}
        protected override void OnDestroy() {}
        protected override void OnUpdate() {
            var e = EntityManager.CreateEntity(
                typeof(LocalTransform),
                typeof(LocalToWorld));
            var containingEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(containingEntity, new EcsTestDataEntity(1, e));

            Entities.ForEach((in EcsTestDataEntity data) =>
            {
                var transform = SystemAPI.GetAspectRW<TestAspect>(data.value1);
                transform.Move(new float3(5,5,5));
            }).WithoutBurst().Schedule(Dependency).Complete();

            Entities.ForEach((in EcsTestDataEntity data) =>
                Assert.AreEqual(new float3(5), SystemAPI.GetAspectRW<TestAspect>(data.value1).Transform.ValueRO.Position)
            ).WithoutBurst().Schedule(Dependency).Complete();
        }
    }

    /// <summary>
    /// Matches <see cref="TestGetAspectRW"/> so that you can see differences in use.
    /// RO means you can schedule parallel, but also throws if you try to change values.
    /// </summary>
    partial class TestGetAspectRO : SystemBase {
        protected override void OnCreate() {}
        protected override void OnDestroy() {}
        protected override void OnUpdate() {
            var e = EntityManager.CreateEntity(typeof(LocalTransform),
                typeof(LocalToWorld));
            EntityManager.AddComponentData(e, LocalTransform.FromPosition(5, 5, 5));

            var containingEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(containingEntity, new EcsTestDataEntity(1, e));

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Assert.Throws<InvalidOperationException>(() =>
            {
                Entities.ForEach((in EcsTestDataEntity data) =>
                {
                    var transform = SystemAPI.GetAspectRO<TestAspect>(data.value1);
                    transform.Move(new float3(5,5,5));
                }).WithoutBurst().Run();
            });
#endif

            Entities.ForEach((in EcsTestDataEntity data) =>
                Assert.AreEqual(new float3(5), SystemAPI.GetAspectRO<TestAspect>(data.value1).Transform.ValueRO.Position)
            ).WithoutBurst().ScheduleParallel(Dependency).Complete();
        }
    }
#endregion
}
#endif
