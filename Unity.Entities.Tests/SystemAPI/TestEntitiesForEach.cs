#pragma warning disable CS0618 // Disable Entities.ForEach obsolete warnings
using NUnit.Framework;
using Unity.Core;
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
            World.CreateSystem<TestGetComponentRW>();
            World.CreateSystem<WithStructuralChangeNoCapture>();
        }

        #region Time
        [Test]
        public void Time() => World.GetExistingSystem<TestTime>().Update(World.Unmanaged);
        #endregion

        #region Aspect
        [Test]
        public void GetAspectRW() => World.GetExistingSystem<TestGetAspectRW>().Update(World.Unmanaged);
        #endregion

        #region GetComponentRW
        [Test]
        public void GetComponentRW() => World.GetExistingSystem<TestGetComponentRW>().Update(World.Unmanaged);
        #endregion

        #region StructuralChange
        [Test]
        public void WithStructuralChangeNoCapture() => World.GetExistingSystem<WithStructuralChangeNoCapture>().Update(World.Unmanaged);
        #endregion
    }


    #region Time
    partial class TestTime : SystemBase {
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
        protected override void OnUpdate() {
            var e = EntityManager.CreateEntity(
                typeof(LocalTransform),
                typeof(LocalToWorld));
            var containingEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(containingEntity, new EcsTestDataEntity(1, e));

            Entities.ForEach((in EcsTestDataEntity data) =>
            {
                var transform = SystemAPI.GetAspect<TestAspect>(data.value1);
                transform.Move(new float3(5,5,5));
            }).WithoutBurst().Schedule(Dependency).Complete();

            Entities.ForEach((in EcsTestDataEntity data) =>
                Assert.AreEqual(new float3(5), SystemAPI.GetAspect<TestAspect>(data.value1).Transform.ValueRO.Position)
            ).WithoutBurst().Schedule(Dependency).Complete();
        }
    }

    #endregion

    #region GetComponentRW

    partial class TestGetComponentRW : SystemBase {
        protected override void OnUpdate() {
            var e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, new EcsTestData(1));
            var containingEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(containingEntity, new EcsTestDataEntity(1, e));
            
            Entities.ForEach((in EcsTestDataEntity dataEntity) =>
            {
                var data = SystemAPI.GetComponentRW<EcsTestData>(dataEntity.value1);
                data.ValueRW = new EcsTestData(2);
            }).Run();

            Entities.ForEach((in EcsTestDataEntity dataEntity) =>
                Assert.AreEqual(2, SystemAPI.GetComponentRO<EcsTestData>(dataEntity.value1).ValueRO.value)
            ).Run();
        }
    }

    #endregion

    #region StructuralChange
    partial class WithStructuralChangeNoCapture : SystemBase {
        protected override void OnUpdate()
        {
            EntityManager.CreateEntity();
            Entities.WithStructuralChanges().ForEach(() => Assert.AreEqual(default(TimeData), SystemAPI.Time)).Run();
        }
    }
    #endregion
}
