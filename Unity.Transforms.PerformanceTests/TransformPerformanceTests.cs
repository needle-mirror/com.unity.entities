using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Tests;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.PerformanceTesting;
using Unity.Transforms;

namespace Unity.Entities.PerformanceTests
{
    [Category("Performance")]
    public sealed unsafe partial class TransformPerformanceTests : ECSTestsFixture
    {
        private List<ComponentSystemBase> TransformSystems;

        public override void Setup()
        {
            base.Setup();
            TransformSystems = new List<ComponentSystemBase>
            {
                World.GetOrCreateSystem<EndFrameParentSystem>(),
                World.GetOrCreateSystem<EndFrameCompositeRotationSystem>(),
                World.GetOrCreateSystem<EndFrameCompositeScaleSystem>(),
                World.GetOrCreateSystem<EndFrameParentScaleInverseSystem>(),
                World.GetOrCreateSystem<EndFrameTRSToLocalToWorldSystem>(),
                World.GetOrCreateSystem<EndFrameTRSToLocalToParentSystem>(),
                World.GetOrCreateSystem<EndFrameLocalToParentSystem>(),
                World.GetOrCreateSystem<EndFrameWorldToLocalSystem>(),
            };
        }

        public void UpdateTransformSystems()
        {
            foreach (var sys in TransformSystems)
                sys.Update();
            // Force complete so that main thread (tests) can have access to direct editing.
            m_Manager.CompleteAllJobs();
        }

        public void UpdateLocalToParentSystem()
        {
            var sys = World.GetOrCreateSystem<EndFrameLocalToParentSystem>();
            sys.Update();
            m_Manager.CompleteAllJobs();
        }

        [Test, Performance]
        public void ChangeParents()
        {
            var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld));
            var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalToParent), typeof(Parent), typeof(Prefab));

            var root0 = m_Manager.CreateEntity(rootArchetype);
            var root1 = m_Manager.CreateEntity(rootArchetype);
            var childPrefab = m_Manager.CreateEntity(childArchetype);
            using (var children = new NativeArray<Entity>(10000, Allocator.Persistent))
            {
                m_Manager.Instantiate(childPrefab, children);
                var rootIndex = 0;
                Measure.Method(UpdateTransformSystems)
                    .SetUp(() =>
                    {
                        var parent = (rootIndex == 0) ? root0 : root1;
                        foreach (var t in children)
                            m_Manager.SetComponentData(t, new Parent {Value = parent});
                        rootIndex ^= 1;
                    })
                    .WarmupCount(10)
                    .MeasurementCount(100)
                    .Run();
            }
        }

        struct RngComponent : IComponentData
        {
            public Random Rng;
        }
        struct ExpectedLocalToWorld : IComponentData
        {
            public float4x4 Value;
        }
        partial class RandomizeTransforms : SystemBase
        {
            protected override void OnUpdate()
            {
                Entities
                    .ForEach((ref Translation trans, ref Rotation rot, ref Scale scale, ref LocalToWorld l2w, ref ExpectedLocalToWorld expected, ref RngComponent rng) =>
                {
                    trans.Value = rng.Rng.NextFloat3();
                    rot.Value = rng.Rng.NextQuaternionRotation();
                    scale.Value = rng.Rng.NextFloat();
                    l2w.Value = float4x4.identity;
                    expected.Value = math.mul(new float4x4(rot.Value, trans.Value), float4x4.Scale(new float3(scale.Value)));
                }).ScheduleParallel(default).Complete();
            }
        }

        [Test, Performance]
        public void ComputeLocalToWorld_DirtyTRS()
        {
            var archetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Translation), typeof(Rotation),
                typeof(Scale), typeof(RngComponent), typeof(ExpectedLocalToWorld));

            using(var entities = m_Manager.CreateEntity(archetype, 10000, Allocator.TempJob))
            {
                for(int i=0; i < entities.Length; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new RngComponent {Rng = new Random((uint)(i+1))});
                }

                Measure.Method(UpdateTransformSystems)
                    .SetUp(() =>
                    {
                        // Randomize each entity's T/R/S to force a LocalToWorld update
                        World.GetOrCreateSystem<RandomizeTransforms>().Update();
                    })
                    .CleanUp(() =>
                    {
                        // Only for validation; enabling will significantly affect performance test results
                        //for (int i = 0; i < entities.Length; ++i)
                        //{
                        //    var expected = m_Manager.GetComponentData<ExpectedLocalToWorld>(entities[i]).Value;
                        //    var actual = m_Manager.GetComponentData<LocalToWorld>(entities[i]).Value;
                        //    Assert.AreEqual(expected, actual);
                        //}
                    })
                    .WarmupCount(10)
                    .MeasurementCount(100)
                    .Run();
            }
        }

        [Test, Performance]
        public void ComputeLocalToWorld_DirtyRootTransform()
        {
            var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(RngComponent), typeof(ExpectedLocalToWorld));
            var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Translation), typeof(Rotation),
                typeof(Scale), typeof(RngComponent), typeof(ExpectedLocalToWorld), typeof(LocalToParent), typeof(Parent));
            var rootEnt = m_Manager.CreateEntity(rootArchetype);
            using(var entities = m_Manager.CreateEntity(childArchetype, 10000, Allocator.TempJob))
            {
                for(int i=0; i < entities.Length; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new RngComponent {Rng = new Random((uint)(i+1))});
                }
                World.GetOrCreateSystem<RandomizeTransforms>().Update(); // randomize initial transforms
                for(int i=0; i < entities.Length; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new Parent {Value = rootEnt});
                }

                var rng = new Random(17);
                Measure.Method(UpdateTransformSystems)
                    .SetUp(() =>
                    {
                        // Move root entity, to force the children to update
                        m_Manager.SetComponentData(rootEnt,
                            new LocalToWorld
                            {
                                Value = float4x4.TRS(rng.NextFloat3(), rng.NextQuaternionRotation(), rng.NextFloat3())
                            });
                    })
                    .WarmupCount(10)
                    .MeasurementCount(100)
                    .Run();
            }
        }

        [Test, Performance]
        public void UpdateHierarchy_LocalToParentSystem()
        {
            int roots = 1000;
            int maxDepth = 10;
            var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Child));
            var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalToParent), typeof(Parent), typeof(Child));
            var rng = new Random(17);

            void AddChild(Entity parent, int depth)
            {
                var childEntity = m_Manager.CreateEntity(childArchetype);
                m_Manager.SetComponentData(childEntity,
                new LocalToParent
                {
                    Value = float4x4.TRS(rng.NextFloat3(), rng.NextQuaternionRotation(), rng.NextFloat3())
                });

                var parentChild = m_Manager.GetBuffer<Child>(parent);
                parentChild.Add(new Child { Value = childEntity });

                var childParent = m_Manager.GetComponentData<Parent>(childEntity);
                childParent.Value = parent;
                m_Manager.SetComponentData(childEntity, childParent);

                if(depth < maxDepth)
                    AddChild(childEntity, depth+1);
            }

            using(var rootEntities = m_Manager.CreateEntity(rootArchetype, roots, Allocator.TempJob))
            {
                for (int i = 0; i < rootEntities.Length; i++)
                {
                    m_Manager.SetComponentData(rootEntities[i],
                    new LocalToWorld
                    {
                        Value = float4x4.TRS(rng.NextFloat3(), rng.NextQuaternionRotation(), rng.NextFloat3())
                    });

                    AddChild(rootEntities[i], 0);
                }
            }

            Measure.Method(UpdateLocalToParentSystem)
                .WarmupCount(10)
                .MeasurementCount(100)
                .Run();
        }
    }
}
