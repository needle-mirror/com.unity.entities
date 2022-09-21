using System.Linq;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities.Tests;
using Unity.Mathematics;
using Unity.PerformanceTesting;
using Unity.Transforms;

namespace Unity.Entities.PerformanceTests
{
    public struct SpeedModifier : IComponentData
    {
        public float Value;
    }
    public readonly partial struct RotateAspect : IAspect
    {
#if !ENABLE_TRANSFORM_V1
        readonly RefRW<LocalToWorldTransform> Transform;

        public void Rotate(float time, float speedModifier) =>
            Transform.ValueRW.Value.Rotation =
                math.mul(
                    math.normalize(Transform.ValueRO.Value.Rotation),
                    quaternion.AxisAngle(math.up(), time * speedModifier));
#else
        readonly RefRW<Rotation> Rotation;

        public void Rotate(float time, float speedModifier) =>
            Rotation.ValueRW.Value =
                math.mul(
                    math.normalize(Rotation.ValueRO.Value),
                    quaternion.AxisAngle(math.up(), time * speedModifier));
#endif
    }

    [BurstCompile(CompileSynchronously = true)]
    partial struct IterateAndUseAspectSystem : ISystem
    {
        public void OnCreate(ref SystemState state) { }
        public void OnDestroy(ref SystemState state) { }

        [BurstCompile(CompileSynchronously = true)]
        public void OnUpdate(ref SystemState state)
        {
            var time = SystemAPI.Time.DeltaTime;
            foreach (var (rotateAspect, speedModifierRef) in SystemAPI.Query<RotateAspect, RefRO<SpeedModifier>>())
                rotateAspect.Rotate(time, speedModifierRef.ValueRO.Value);
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    partial struct IterateAndUseComponentsSystem : ISystem
    {
        public void OnCreate(ref SystemState state) { }
        public void OnDestroy(ref SystemState state) { }

        [BurstCompile(CompileSynchronously = true)]
        public void OnUpdate(ref SystemState state)
        {
            var time = SystemAPI.Time.DeltaTime;
#if !ENABLE_TRANSFORM_V1
            foreach (var (transform, speedModifierRef) in SystemAPI.Query<RefRW<LocalToWorldTransform>, RefRO<SpeedModifier>>())
            {
                transform.ValueRW.Value.Rotation =
                    math.mul(
                        math.normalize(transform.ValueRO.Value.Rotation),
                        quaternion.AxisAngle(math.up(), time * speedModifierRef.ValueRO.Value));
            }
#else
            foreach (var (rotation, speedModifierRef) in SystemAPI.Query<RefRW<Rotation>, RefRO<SpeedModifier>>())
            {
                rotation.ValueRW.Value =
                    math.mul(
                        math.normalize(rotation.ValueRO.Value),
                        quaternion.AxisAngle(math.up(), time * speedModifierRef.ValueRO.Value));
            }
#endif
        }
    }

    [TestFixture]
    public class IdiomaticForEachISystemPerformanceTests : ECSTestsFixture
    {
        EntityArchetype _archetype;

        [SetUp]
        public void SetUp() =>
            _archetype = m_Manager.CreateArchetype(RotateAspect.RequiredComponents.Append(ComponentType.ReadWrite<SpeedModifier>()).ToArray());

        [Test, Performance]
        [Category("Performance")]
        public unsafe void IterateAndUseAspects([Values(100, 100000)] int entityCount)
        {
            var system = World.GetOrCreateSystem<IterateAndUseAspectSystem>();
            var systemPtr = &system;
            using var entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, World.UpdateAllocator.ToAllocator);
            m_Manager.CreateEntity(_archetype, entities);
            Measure.Method(() => systemPtr->Update(World.Unmanaged))
                .WarmupCount(1)
                .MeasurementCount(100)
                .Run();
        }

        [Test, Performance]
        [Category("Performance")]
        public unsafe void IterateAndUseComponents([Values(100, 100000)] int entityCount)
        {
            var system = World.GetOrCreateSystem<IterateAndUseAspectSystem>();
            var systemPtr = &system;
            using var entities = CollectionHelper.CreateNativeArray<Entity>(entityCount, World.UpdateAllocator.ToAllocator);
            m_Manager.CreateEntity(_archetype, entities);
            Measure.Method(() => systemPtr->Update(World.Unmanaged))
                .WarmupCount(1)
                .MeasurementCount(100)
                .Run();
        }
    }
}
