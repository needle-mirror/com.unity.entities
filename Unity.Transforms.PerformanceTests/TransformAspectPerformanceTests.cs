using NUnit.Framework;
using Unity.Burst;
using Unity.Entities;
using Unity.Entities.Tests;
using Unity.Mathematics;
using Unity.PerformanceTesting;
using Unity.Properties;

namespace Unity.Transforms.PerformanceTests
{
#if ENABLE_TRANSFORM_V1
#else
    // Temporary TransformAspect variants with no [Optional] components, to test a theory.
    readonly partial struct TransformPosWorldAspect : IAspect
    {
        readonly RefRW<WorldTransform>   m_WorldTransform;

        // --- Properties R/W ---

        /// <summary>The world space position of the entity.</summary>
        [CreateProperty]
        public float3 Position
        {
            get => m_WorldTransform.ValueRO.Position;
            set
            {
                m_WorldTransform.ValueRW.Position = value;
            }
        }
    }
    readonly partial struct TransformPosParentAspect : IAspect
    {
        readonly RefRW<WorldTransform> m_WorldTransform;
        readonly RefRW<LocalTransform> LocalTransform;
        readonly RefRW<ParentTransform> m_ParentTransform;

        // --- Properties R/W ---

        /// <summary>The world space position of the entity.</summary>
        [CreateProperty]
        public float3 Position
        {
            get => m_WorldTransform.ValueRO.Position;
            set
            {
                m_WorldTransform.ValueRW.Position = value;
                LocalTransform.ValueRW.Position =
                    ParentToWorld.InverseTransformPoint(value);
            }
        }

        // Properties Read Only
        // --------------------

        /// <summary>This is a copy of the parent's WorldTransform</summary>
        public ParentTransform ParentToWorld
        {
            get => m_ParentTransform.ValueRO;
        }
    }

    [Category("Performance")]
    public unsafe partial class TransformAspectPerformanceTests : ECSTestsFixture
    {
        [SetUp]
        public void SetUp()
        {
            World.GetOrCreateSystem<AspectPerfTestSystem>();
        }

        private ref AspectPerfTestSystem GetTestSystemUnsafe()
        {
            var systemHandle = World.GetExistingSystem<AspectPerfTestSystem>();
            if (systemHandle == default)
                throw new System.InvalidOperationException("This system does not exist any more");
            return ref World.Unmanaged.GetUnsafeSystemRef<AspectPerfTestSystem>(systemHandle);
        }

        private ref SystemState GetSystemStateRef()
        {
            var systemHandle = World.GetExistingSystem<AspectPerfTestSystem>();
            var statePtr = World.Unmanaged.ResolveSystemState(systemHandle);
            if (statePtr == null)
                throw new System.InvalidOperationException("No system state exists any more for this system");
            return ref *statePtr;
        }

        [BurstCompile]
        partial struct AspectPerfTestSystem : ISystem
        {
            [BurstCompile]
            public void OnCreate(ref SystemState state)
            {
            }

            [BurstCompile]
            public void OnDestroy(ref SystemState state)
            {
            }

            [BurstCompile]
            public void OnUpdate(ref SystemState state)
            {
            }

            // Set Position with direct WorldTransform access
            [BurstCompile]
            partial struct SetPositionLocalJob_Transform : IJobEntity
            {
                void Execute(ref WorldTransform transform)
                {
                    transform.Position.x += 1;
                }
            }
            public void SetPosition_Local_Transform(ref SystemState state)
            {
                new SetPositionLocalJob_Transform().Run();
            }

            // Set Position through TransformAspect access
            [BurstCompile]
            partial struct SetPositionLocalJob_Aspect : IJobEntity
            {
                void Execute(ref TransformAspect transform)
                {
                    var pos = transform.LocalPosition;
                    pos.x += 1;
                    transform.LocalPosition = pos;
                }
            }
            public void SetPosition_Local_Aspect(ref SystemState state)
            {
                new SetPositionLocalJob_Aspect().Run();
            }

            // Set Position through TransformPosWorldAspect (TEMP -- no optional components)
            [BurstCompile]
            partial struct SetPositionWorldJob_PosAspect : IJobEntity
            {
                void Execute(ref TransformPosWorldAspect transform)
                {
                    var pos = transform.Position;
                    pos.x += 1;
                    transform.Position = pos;
                }
            }
            public void SetPosition_World_PosAspect(ref SystemState state)
            {
                new SetPositionWorldJob_PosAspect().Run();
            }

            // Set Position through direct LocalTransform access
            [BurstCompile]
            partial struct SetPositionParentJob_Transform : IJobEntity
            {
                void Execute(ref LocalTransform transform)
                {
                    transform.Position.x += 1;
                }
            }
            public void SetPosition_Parent_Transform(ref SystemState state)
            {
                new SetPositionParentJob_Transform().Run();
            }

            // Set Position through direct TransformAspect (with Parent)
            [BurstCompile]
            [WithAll(typeof(Parent))]
            partial struct SetPositionParentJob_Aspect : IJobEntity
            {
                void Execute(ref TransformAspect transform)
                {
                    var pos = transform.LocalPosition;
                    pos.x += 1;
                    transform.LocalPosition = pos;
                }
            }
            public void SetPosition_Parent_Aspect(ref SystemState state)
            {
                new SetPositionParentJob_Aspect().Run();
            }

            // Set Position through direct TransformPosParentAspect (TEMP -- no optional components)
            [BurstCompile]
            [WithAll(typeof(Parent))]
            partial struct SetPositionParentJob_PosAspect : IJobEntity
            {
                void Execute(ref TransformPosParentAspect transform)
                {
                    var pos = transform.Position;
                    pos.x += 1;
                    transform.Position = pos;
                }
            }
            public void SetPosition_Parent_PosAspect(ref SystemState state)
            {
                new SetPositionParentJob_PosAspect().Run();
            }
        }

        [Test, Performance]
        public void TA_SetPosition_Local_Perf()
        {
            var archetype = m_Manager.CreateArchetype(typeof(WorldTransform), typeof(LocalToWorld));
            int entityCount = 10000;
            using var entities = m_Manager.CreateEntity(archetype, entityCount, World.UpdateAllocator.ToAllocator);
            foreach (var e in entities)
            {
                m_Manager.SetComponentData(e, WorldTransform.Identity);
            }

            Measure.Method(() => { GetTestSystemUnsafe().SetPosition_Local_Transform(ref GetSystemStateRef()); })
                .WarmupCount(10)
                .MeasurementCount(100)
                .SampleGroup(new SampleGroup($"Transform_10000x", SampleUnit.Microsecond))
                .Run();

            Measure.Method(() => { GetTestSystemUnsafe().SetPosition_Local_Aspect(ref GetSystemStateRef()); })
                .WarmupCount(10)
                .MeasurementCount(100)
                .SampleGroup(new SampleGroup($"Aspect_10000x", SampleUnit.Microsecond))
                .Run();

            Measure.Method(() => { GetTestSystemUnsafe().SetPosition_World_PosAspect(ref GetSystemStateRef()); })
                .WarmupCount(10)
                .MeasurementCount(100)
                .SampleGroup(new SampleGroup($"PosAspect_10000x", SampleUnit.Microsecond))
                .Run();
        }

        [Test, Performance]
        public void TA_SetPosition_Parent_Perf()
        {
            var rootArchetype = m_Manager.CreateArchetype(typeof(WorldTransform), typeof(LocalToWorld));
            var rootEnt = m_Manager.CreateEntity(rootArchetype);
            m_Manager.SetComponentData(rootEnt, WorldTransform.Identity);

            var childArchetype = m_Manager.CreateArchetype(typeof(WorldTransform), typeof(LocalToWorld),
                typeof(Parent), typeof(LocalTransform), typeof(ParentTransform));
            int entityCount = 10000;
            using var entities = m_Manager.CreateEntity(childArchetype, entityCount, World.UpdateAllocator.ToAllocator);
            foreach (var e in entities)
            {
                m_Manager.SetComponentData(e, new Parent { Value = rootEnt });
                m_Manager.SetComponentData(e, LocalTransform.Identity);
                m_Manager.SetComponentData(e, WorldTransform.Identity);
                m_Manager.SetComponentData(e, ParentTransform.Identity);
            }

            World.GetOrCreateSystem<ParentSystem>().Update(World.Unmanaged); // establish parent/child links

            Measure.Method(() => { GetTestSystemUnsafe().SetPosition_Parent_Transform(ref GetSystemStateRef()); })
                .WarmupCount(10)
                .MeasurementCount(100)
                .SampleGroup(new SampleGroup($"Transform_10000x", SampleUnit.Microsecond))
                .Run();

            Measure.Method(() => { GetTestSystemUnsafe().SetPosition_Parent_Aspect(ref GetSystemStateRef()); })
                .WarmupCount(10)
                .MeasurementCount(100)
                .SampleGroup(new SampleGroup($"Aspect_10000x", SampleUnit.Microsecond))
                .Run();

            Measure.Method(() => { GetTestSystemUnsafe().SetPosition_Parent_PosAspect(ref GetSystemStateRef()); })
                .WarmupCount(10)
                .MeasurementCount(100)
                .SampleGroup(new SampleGroup($"PosAspect_10000x", SampleUnit.Microsecond))
                .Run();
        }
    }
#endif
}
