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
    partial class TestGetAspectRW : SystemBase {
        protected override void OnCreate() {}
        protected override void OnDestroy() {}
        protected override void OnUpdate() {
            var e = EntityManager.CreateEntity(
#if !ENABLE_TRANSFORM_V1
                typeof(LocalTransform),
                typeof(LocalToWorld), typeof(WorldTransform));
#else
                typeof(Translation), typeof(Rotation),
                typeof(LocalToWorld), typeof(LocalToParent));
#endif
            var containingEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(containingEntity, new EcsTestDataEntity(1, e));

            Entities.ForEach((in EcsTestDataEntity data) =>
            {
                var transform = SystemAPI.GetAspectRW<TransformAspect>(data.value1);
                transform.TranslateLocal(5);
            }).WithoutBurst().Schedule(Dependency).Complete();

            Entities.ForEach((in EcsTestDataEntity data) =>
                Assert.AreEqual(new float3(5), SystemAPI.GetAspectRW<TransformAspect>(data.value1).LocalPosition)
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
#if !ENABLE_TRANSFORM_V1
            var e = EntityManager.CreateEntity(typeof(LocalTransform),
                typeof(LocalToWorld), typeof(WorldTransform));
            EntityManager.AddComponentData(e, LocalTransform.FromPosition(5, 5, 5));
#else
            var e = EntityManager.CreateEntity(typeof(Rotation),
                typeof(LocalToWorld), typeof(LocalToParent));
            EntityManager.AddComponentData(e, new Translation{Value = 5});
#endif
            var containingEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(containingEntity, new EcsTestDataEntity(1, e));

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Assert.Throws<InvalidOperationException>(() =>
            {
                Entities.ForEach((in EcsTestDataEntity data) =>
                {
                    var transform = SystemAPI.GetAspectRO<TransformAspect>(data.value1);
                    transform.TranslateLocal(5);
                }).WithoutBurst().Run();
            });
#endif

            Entities.ForEach((in EcsTestDataEntity data) =>
                Assert.AreEqual(new float3(5), SystemAPI.GetAspectRO<TransformAspect>(data.value1).LocalPosition)
            ).WithoutBurst().ScheduleParallel(Dependency).Complete();
        }
    }
#endregion
}
#endif
