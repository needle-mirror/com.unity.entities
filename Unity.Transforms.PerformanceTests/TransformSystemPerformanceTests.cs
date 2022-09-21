using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Tests;
using Unity.Mathematics;
using Unity.PerformanceTesting;

namespace Unity.Transforms.PerformanceTests
{
    [Category("Performance")]
    public sealed unsafe partial class TransformSystemPerformanceTests : ECSTestsFixture
    {
        private List<SystemHandle> TransformSystems;

        public override void Setup()
        {
            base.Setup();
            TransformSystems = new List<SystemHandle>
            {
                World.GetOrCreateSystem<ParentSystem>(),
#if !ENABLE_TRANSFORM_V1
                World.GetOrCreateSystem<TransformHierarchySystem>(),
                World.GetOrCreateSystem<TransformToMatrixSystem>(),
#else
                World.GetOrCreateSystem<CompositeRotationSystem>(),
                World.GetOrCreateSystem<CompositeScaleSystem>(),
                World.GetOrCreateSystem<ParentScaleInverseSystem>(),
                World.GetOrCreateSystem<TRSToLocalToWorldSystem>(),
                World.GetOrCreateSystem<TRSToLocalToParentSystem>(),
                World.GetOrCreateSystem<LocalToParentSystem>(),
                World.GetOrCreateSystem<WorldToLocalSystem>(),
#endif
            };
        }

        public unsafe void UpdateTransformSystems()
        {
            foreach (var sys in TransformSystems)
                sys.Update(World.Unmanaged);

            // Force complete so that main thread (tests) can have access to direct editing.
            m_Manager.CompleteAllTrackedJobs();
        }

        public void UpdateParentSystem()
        {
            var sys = World.GetOrCreateSystem<ParentSystem>();
            sys.Update(World.Unmanaged);
            m_Manager.CompleteAllTrackedJobs();
        }

        public void UpdateLocalToParentSystem()
        {
#if !ENABLE_TRANSFORM_V1
            var sys = World.GetOrCreateSystem<TransformHierarchySystem>();
#else
            var sys = World.GetOrCreateSystem<LocalToParentSystem>();
#endif
            sys.Update(World.Unmanaged);

            m_Manager.CompleteAllTrackedJobs();
        }

        [Test, Performance]
        public void ChangeParents()
        {
#if !ENABLE_TRANSFORM_V1
            var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalToWorldTransform));
            var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalToWorldTransform),
                typeof(LocalToParentTransform), typeof(Parent), typeof(Prefab));
#else
            var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld));
            var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalToParent), typeof(Parent), typeof(Prefab));
#endif

            var root0 = m_Manager.CreateEntity(rootArchetype);
            var root1 = m_Manager.CreateEntity(rootArchetype);
#if !ENABLE_TRANSFORM_V1
            m_Manager.SetComponentData(root0, new LocalToWorldTransform
            {
                Value = UniformScaleTransform.FromPositionRotationScale(new float3(1,2,3), quaternion.RotateX(1.0f), 0.5f),
            });
            m_Manager.SetComponentData(root1, new LocalToWorldTransform
            {
                Value = UniformScaleTransform.FromPositionRotationScale(new float3(4,5,6), quaternion.RotateY(1.0f), 1.5f),
            });
#else
            m_Manager.SetComponentData(root0, new LocalToWorld
            {
                Value = float4x4.TRS(new float3(1, 0, 0), quaternion.identity, 1.0f),
            });
            m_Manager.SetComponentData(root0, new LocalToWorld
            {
                Value = float4x4.TRS(new float3(-1, 0, 0), quaternion.identity, 1.0f),
            });
#endif
            var childPrefab = m_Manager.CreateEntity(childArchetype);
            var rng = new Random(17);
            using (var children = new NativeArray<Entity>(10000, Allocator.Persistent))
            {
                m_Manager.Instantiate(childPrefab, children);
                var rootIndex = 0;
                Measure.Method(UpdateTransformSystems)
                    .SetUp(() =>
                    {
                        var parent = (rootIndex == 0) ? root0 : root1;
                        foreach (var t in children)
                        {
                            m_Manager.SetComponentData(t, new Parent { Value = parent });
#if !ENABLE_TRANSFORM_V1
                            m_Manager.SetComponentData(t, new LocalToParentTransform
                            {
                                Value = UniformScaleTransform.FromPositionRotationScale(
                                    rng.NextFloat3(),
                                    rng.NextQuaternionRotation(),
                                    0.1f + rng.NextFloat()),
                            });
#endif
                        }
                    })
                    .CleanUp(() =>
                    {
#if !ENABLE_TRANSFORM_V1
                        // Expensive validation code -- significantly increases this test's running time
                        //var expectedP2w = m_Manager.GetComponentData<LocalToWorldTransform>(rootIndex == 0 ? root0 : root1).Value;
                        //foreach (var t in children)
                        //{
                        //    Assert.AreEqual(expectedP2w, m_Manager.GetComponentData<ParentToWorldTransform>(t).Value);
                        //    var expected = expectedP2w.TransformTransform(m_Manager.GetComponentData<LocalToParentTransform>(t).Value);
                        //    var actual = m_Manager.GetComponentData<LocalToWorldTransform>(t).Value;
                        //    Assert.AreEqual(expected, actual);
                        //}
#endif
                        rootIndex ^= 1;
                    })
                    .WarmupCount(10)
                    .MeasurementCount(1000)
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
#if !ENABLE_TRANSFORM_V1
                .ForEach((ref LocalToWorldTransform transform, ref LocalToWorld l2w, ref ExpectedLocalToWorld expected, ref RngComponent rng) =>
                {
                    transform.Value.Position = rng.Rng.NextFloat3();
                    transform.Value.Rotation = rng.Rng.NextQuaternionRotation();
                    transform.Value.Scale = rng.Rng.NextFloat();
                    l2w.Value = float4x4.identity;
                    expected.Value = transform.Value.ToMatrix();
                }).ScheduleParallel(default).Complete();
#else
                .ForEach((ref Translation trans, ref Rotation rot, ref Scale scale, ref LocalToWorld l2w, ref ExpectedLocalToWorld expected, ref RngComponent rng) =>
                {
                    trans.Value = rng.Rng.NextFloat3();
                    rot.Value = rng.Rng.NextQuaternionRotation();
                    scale.Value = rng.Rng.NextFloat();
                    l2w.Value = float4x4.identity;
                    expected.Value = math.mul(new float4x4(rot.Value, trans.Value), float4x4.Scale(new float3(scale.Value)));
                }).ScheduleParallel(default).Complete();
#endif
            }
        }

        [Test, Performance]
        public void ComputeLocalToWorld_DirtyTRS()
        {
#if !ENABLE_TRANSFORM_V1
            var archetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalToWorldTransform),
                typeof(RngComponent), typeof(ExpectedLocalToWorld));
#else
            var archetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Translation), typeof(Rotation),
                typeof(Scale), typeof(RngComponent), typeof(ExpectedLocalToWorld));
#endif

            using(var entities = m_Manager.CreateEntity(archetype, 10000, World.UpdateAllocator.ToAllocator))
            {
                for(int i=0; i < entities.Length; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new RngComponent {Rng = Random.CreateFromIndex((uint)(i+1))});
                }

                Measure.Method(UpdateTransformSystems)
                    .SetUp(() =>
                    {
                        // Randomize each entity's T/R/S to force a LocalToWorld update
                        World.GetOrCreateSystemManaged<RandomizeTransforms>().Update();
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
                    .MeasurementCount(1000)
                    .Run();
            }
        }

        [Test, Performance]
        public void ComputeLocalToWorld_DirtyRootTransform()
        {
#if !ENABLE_TRANSFORM_V1
            var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalToWorldTransform));
            var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalToWorldTransform),
                typeof(LocalToParentTransform), typeof(Parent));
#else
            var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld));
            var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Translation), typeof(Rotation),
                typeof(Scale), typeof(LocalToParent), typeof(Parent));
#endif
            var rng = new Random(17);
            var rootEnt = m_Manager.CreateEntity(rootArchetype);
#if !ENABLE_TRANSFORM_V1
                        m_Manager.SetComponentData(rootEnt,
                            new LocalToWorldTransform
                            {
                                Value = new UniformScaleTransform
                                {
                                    Position = rng.NextFloat3(),
                                    Rotation = rng.NextQuaternionRotation(),
                                    Scale = rng.NextFloat(),
                                }
                            });
#else
            m_Manager.SetComponentData(rootEnt,
                new LocalToWorld
                {
                    Value = float4x4.TRS(rng.NextFloat3(), rng.NextQuaternionRotation(), rng.NextFloat3())
                });
#endif
            using(var entities = m_Manager.CreateEntity(childArchetype, 10000, World.UpdateAllocator.ToAllocator))
            {
                for(int i=0; i < entities.Length; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new Parent {Value = rootEnt});
#if !ENABLE_TRANSFORM_V1
                    m_Manager.SetComponentData(entities[i], new LocalToParentTransform
                        {
                            Value = m_Manager.GetComponentData<LocalToWorldTransform>(entities[i]).Value,
                        });
#else
                    m_Manager.SetComponentData(entities[i],
                        new LocalToWorld
                        {
                            Value = float4x4.TRS(rng.NextFloat3(), rng.NextQuaternionRotation(), rng.NextFloat3())
                        });
#endif
                }

                Measure.Method(UpdateTransformSystems)
                    .SetUp(() =>
                    {
                        // Move root entity, to force the children to update
#if !ENABLE_TRANSFORM_V1
                        m_Manager.SetComponentData(rootEnt,
                            new LocalToWorldTransform
                            {
                                Value = new UniformScaleTransform
                                {
                                    Position = rng.NextFloat3(),
                                    Rotation = rng.NextQuaternionRotation(),
                                    Scale = rng.NextFloat(),
                                }
                            });
#else
                        m_Manager.SetComponentData(rootEnt,
                            new LocalToWorld
                            {
                                Value = float4x4.TRS(rng.NextFloat3(), rng.NextQuaternionRotation(), rng.NextFloat3())
                            });
#endif
                    })
                    .CleanUp(() =>
                    {
#if !ENABLE_TRANSFORM_V1
                        // Only for validation; enabling will significantly affect performance test results
                        //var rootL2w = m_Manager.GetComponentData<LocalToWorldTransform>(rootEnt).Value;
                        //for (int i = 0; i < entities.Length; ++i)
                        //{
                        //    var expected = rootL2w.TransformTransform(m_Manager.GetComponentData<LocalToParentTransform>(entities[i]).Value).ToMatrix();
                        //    var actual = m_Manager.GetComponentData<LocalToWorld>(entities[i]).Value;
                        //    Assert.AreEqual(expected, actual);
                        //}
#endif
                    })
                    .WarmupCount(10)
                    .MeasurementCount(1000)
                    .Run();
            }
        }

        [Test, Performance]
        public void UpdateHierarchy_LocalToParentSystem()
        {
            int roots = 1000;
            int maxDepth = 10;
#if !ENABLE_TRANSFORM_V1
            var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalToWorldTransform));
            var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalToWorldTransform),
                typeof(LocalToParentTransform), typeof(Parent));
#else
            var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld));
            var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalToParent), typeof(Parent));
#endif
            var rng = new Random(17);
            using var allChildEntities = new NativeList<Entity>(roots * (maxDepth+1), World.UpdateAllocator.ToAllocator);

            void AddChild(Entity parent, int depth)
            {
                var childEntity = m_Manager.CreateEntity(childArchetype);
                m_Manager.SetComponentData(childEntity,
#if !ENABLE_TRANSFORM_V1
                    new LocalToParentTransform
                    {
                        Value = UniformScaleTransform.FromPositionRotationScale(
                            rng.NextFloat3(),
                            rng.NextQuaternionRotation(),
                            0.1f + rng.NextFloat())
                    });
#else
                    new LocalToParent
                    {
                        Value = float4x4.TRS(rng.NextFloat3(), rng.NextQuaternionRotation(), rng.NextFloat3())
                    });
#endif
                allChildEntities.Add(childEntity);

                m_Manager.SetComponentData(childEntity, new Parent{Value = parent});

                if(depth < maxDepth)
                    AddChild(childEntity, depth+1);
            }

            using(var rootEntities = m_Manager.CreateEntity(rootArchetype, roots, World.UpdateAllocator.ToAllocator))
            {
                for (int i = 0; i < rootEntities.Length; i++)
                {
                    m_Manager.SetComponentData(rootEntities[i],
#if !ENABLE_TRANSFORM_V1
                        new LocalToWorldTransform
                        {
                            Value = UniformScaleTransform.FromPositionRotationScale(
                                rng.NextFloat3(),
                                rng.NextQuaternionRotation(),
                                0.1f + rng.NextFloat())
                        });
#else
                        new LocalToWorld
                        {
                            Value = float4x4.TRS(rng.NextFloat3(), rng.NextQuaternionRotation(), rng.NextFloat3())
                        });
#endif
                    AddChild(rootEntities[i], 0);
                }
            }
            // Manually tick the parent system to correctly establish parent/child links
            UpdateParentSystem();

            int iteration = 0;
            Measure.Method(UpdateLocalToParentSystem)
                .SetUp(() =>
                {
                    // move all child entities
                    foreach (var childEnt in allChildEntities)
                    {
#if !ENABLE_TRANSFORM_V1
                        m_Manager.SetComponentData(childEnt, new LocalToParentTransform
                            {
                                Value = UniformScaleTransform.FromPositionRotationScale(
                                    rng.NextFloat3(),
                                    rng.NextQuaternionRotation(),
                                    0.1f + rng.NextFloat())
                            });
#else
                        m_Manager.SetComponentData(childEnt,
                            new LocalToParent
                            {
                                Value = float4x4.TRS(rng.NextFloat3(), rng.NextQuaternionRotation(), rng.NextFloat3())
                            });
#endif
                    }
                })
                .CleanUp(() =>
                {
#if !ENABLE_TRANSFORM_V1
                    // Only for validation; enabling will significantly affect performance test results
                    //for(int i=0; i<allChildEntities.Length; ++i)
                    //{
                    //    var childEnt = allChildEntities[i];
                    //    var parentEnt = m_Manager.GetComponentData<Parent>(childEnt).Value;
                    //    var parentL2w = m_Manager.GetComponentData<LocalToWorldTransform>(parentEnt).Value;
                    //    Assert.AreEqual(parentL2w, m_Manager.GetComponentData<ParentToWorldTransform>(childEnt).Value);
                    //    var expected = parentL2w.TransformTransform(m_Manager.GetComponentData<LocalToParentTransform>(childEnt).Value);
                    //    var actual = m_Manager.GetComponentData<LocalToWorldTransform>(childEnt).Value;
                    //    Assert.AreEqual(expected, actual, $"Mismatch in iteration {iteration} on child {i} of {allChildEntities.Length}");
                    //}
#endif
                    iteration++;
                })
                .WarmupCount(10)
                .MeasurementCount(1000)
                .Run();
        }
    }
}
