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
                World.GetOrCreateSystem<LocalToWorldSystem>(),
#else
                World.GetOrCreateSystem<RotationEulerSystem>(),
                World.GetOrCreateSystem<CompositeScaleSystem>(),
                World.GetOrCreateSystem<CompositeRotationSystem>(),
                World.GetOrCreateSystem<PostRotationEulerSystem>(),
                World.GetOrCreateSystem<TRSToLocalToWorldSystem>(),
                World.GetOrCreateSystem<ParentScaleInverseSystem>(),
                World.GetOrCreateSystem<TRSToLocalToParentSystem>(),
                World.GetOrCreateSystem<LocalToParentSystem>(),
                World.GetOrCreateSystem<WorldToLocalSystem>(),
#endif
            };
        }

        const float k_Tolerance = 0.001f;

        static bool AreNearlyEqual(float a, float b, float tolerance)
        {
            return math.abs(a - b) <= tolerance;
        }

        static bool AreNearlyEqual(float4 a, float4 b, float tolerance)
        {
            return AreNearlyEqual(a.x, b.x, tolerance) && AreNearlyEqual(a.y, b.y, tolerance) && AreNearlyEqual(a.z, b.z, tolerance) && AreNearlyEqual(a.w, b.w, tolerance);
        }

        static bool AreNearlyEqual(quaternion a, quaternion b, float tolerance)
        {
            return AreNearlyEqual(a.value.x, b.value.x, tolerance) && AreNearlyEqual(a.value.y, b.value.y, tolerance) && AreNearlyEqual(a.value.z, b.value.z, tolerance) && AreNearlyEqual(a.value.w, b.value.w, tolerance);
        }

        static bool AreNearlyEqual(float3 a, float3 b, float tolerance)
        {
            return AreNearlyEqual(a.x, b.x, tolerance) && AreNearlyEqual(a.y, b.y, tolerance) && AreNearlyEqual(a.z, b.z, tolerance);
        }

        static bool AreNearlyEqual(float4x4 a, float4x4 b, float tolerance)
        {
            return AreNearlyEqual(a.c0, b.c0, tolerance) && AreNearlyEqual(a.c1, b.c1, tolerance) && AreNearlyEqual(a.c2, b.c2, tolerance) && AreNearlyEqual(a.c3, b.c3, tolerance);
        }

        // For many of the following tests, we want to measure the time to run all the transform systems, but it's also
        // nice to see a breakdown of how long each system takes in isolation. These methods present a hacky way to take
        // all of these measurements in a single test run, with minimal code duplication.
        // To measure the time of system index N within the transform systems, we run all systems before N in Setup(),
        // then measure only system N's update, then run all systems after N in Cleanup(). If N is -1, we measure all
        // systems end-to-end. Each measurement is captured in its own SampleGroup.
        // Note that the individual system measurement times will likely add up to more than the "All" time, because
        // we measure the time to complete the jobs scheduled by each system as well. In a full group update, these jobs
        // would overlap with the main-thread system update logic (and, ideally, with each other).
        private int CurrentMeasureSystemIndex = 0;
        void UpdateTransformSystems_Setup(int measureSystemIndex)
        {
            CurrentMeasureSystemIndex = measureSystemIndex;
            for(int i=0; i<measureSystemIndex; ++i)
                TransformSystems[i].Update(World.Unmanaged);
            m_Manager.CompleteAllTrackedJobs();
        }
        void UpdateTransformSystems_Measure()
        {
            if (CurrentMeasureSystemIndex == -1)
            {
                foreach (var sys in TransformSystems)
                    sys.Update(World.Unmanaged);
            }
            else
            {
                TransformSystems[CurrentMeasureSystemIndex].Update(World.Unmanaged);
            }

            m_Manager.CompleteAllTrackedJobs();
        }
        void UpdateTransformSystems_Cleanup()
        {
            if (CurrentMeasureSystemIndex == -1)
                return;
            for(int i=CurrentMeasureSystemIndex+1; i<TransformSystems.Count; ++i)
                TransformSystems[i].Update(World.Unmanaged);
            m_Manager.CompleteAllTrackedJobs();
        }
        void UpdateTransformSystems_All()
        {
            foreach (var sys in TransformSystems)
                sys.Update(World.Unmanaged);
            m_Manager.CompleteAllTrackedJobs();
        }
        string GetTransformSystemName(int systemIndex)
        {
            if (systemIndex == -1)
                return "All";
            string fullName = TypeManager.GetSystemName(World.Unmanaged.GetTypeOfSystem(TransformSystems[systemIndex])).ToString();
            return fullName.Substring(1+fullName.LastIndexOf("."));
        }

        //////////////////////////////////////////////

        // Reassign the Parent component of a large number of entities, and measure the time to update the ParentSystem
        // and re-establish parent/child relationships.
        // TODO(DOTS-7135): this test exposes an O(N^2) loop in the ParentSystem that scales very poorly.
        [Test, Performance]
        public void ParentSystem_ChangeParents()
        {
            int childEntityCount = 10000;
#if !ENABLE_TRANSFORM_V1
            var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalTransform), typeof(WorldTransform));
            var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(WorldTransform),
                typeof(LocalTransform), typeof(Parent), typeof(Prefab));
#else
            var rootArchetype = m_Manager.CreateArchetype(typeof(Translation), typeof(Rotation), typeof(Scale), typeof(LocalToWorld));
            var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalToParent),
                typeof(Translation), typeof(Rotation), typeof(Scale), typeof(Parent), typeof(Prefab));
#endif

            var root0 = m_Manager.CreateEntity(rootArchetype);
            var root1 = m_Manager.CreateEntity(rootArchetype);
#if !ENABLE_TRANSFORM_V1
            m_Manager.SetComponentData(root0,
                LocalTransform.FromPositionRotationScale(new float3(1,2,3), quaternion.RotateX(1.0f), 0.5f));
            m_Manager.SetComponentData(root1,
                    LocalTransform.FromPositionRotationScale(new float3(4,5,6), quaternion.RotateY(1.0f), 1.5f));
#else
            m_Manager.SetComponentData(root0, new Translation { Value = new float3(1, 2, 3) });
            m_Manager.SetComponentData(root0, new Rotation { Value = quaternion.RotateX(1.0f) });
            m_Manager.SetComponentData(root0, new Scale { Value = 0.5f });
            m_Manager.SetComponentData(root1, new Translation { Value = new float3(4, 5, 6) });
            m_Manager.SetComponentData(root1, new Rotation { Value = quaternion.RotateX(1.0f) });
            m_Manager.SetComponentData(root1, new Scale { Value = 0.5f });
#endif
            var childPrefab = m_Manager.CreateEntity(childArchetype);
            var rng = new Random(17);
            using (var children = new NativeArray<Entity>(childEntityCount, Allocator.Persistent))
            {
                m_Manager.Instantiate(childPrefab, children);
                var rootIndex = 0;
                Measure.Method(UpdateTransformSystems_All)
                    .SetUp(() =>
                    {
                        var parent = (rootIndex == 0) ? root0 : root1;
                        foreach (var t in children)
                        {
                            m_Manager.SetComponentData(t, new Parent { Value = parent });
#if !ENABLE_TRANSFORM_V1
                            m_Manager.SetComponentData(t,
                                LocalTransform.FromPositionRotationScale(
                                    rng.NextFloat3(),
                                    rng.NextQuaternionRotation(),
                                    0.1f + rng.NextFloat()));
#endif
                        }
                    })
                    .CleanUp(() =>
                    {
                        World.UpdateAllocator.Rewind();
#if !ENABLE_TRANSFORM_V1
                        // Expensive validation code -- significantly increases this test's running time
                        //var expectedP2w = m_Manager.GetComponentData<WorldTransform>(rootIndex == 0 ? root0 : root1).Value;
                        //foreach (var t in children)
                        //{
                        //    Assert.AreEqual(expectedP2w, m_Manager.GetComponentData<ParentTransform>(t).Value);
                        //    var expected = expectedP2w.TransformTransform(m_Manager.GetComponentData<LocalTransform>(t).Value);
                        //    var actual = m_Manager.GetComponentData<WorldTransform>(t).Value;
                        //    Assert.AreEqual(expected, actual);
                        //}
#endif
                        rootIndex ^= 1;
                    })
                    .WarmupCount(1)
                    .MeasurementCount(10)
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
                .ForEach((ref LocalTransform transform, ref LocalToWorld l2w, ref ExpectedLocalToWorld expected, ref RngComponent rng) =>
                {
                    transform.Position = rng.Rng.NextFloat3();
                    transform.Rotation = rng.Rng.NextQuaternionRotation();
                    transform.Scale = rng.Rng.NextFloat();
                    l2w.Value = float4x4.identity;
                    expected.Value = transform.ToMatrix();
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

        // Run transform systems on a totally flat hierarchy (no parents/children).
        // Optionally dirty the transforms between each run.
        [Test, Performance]
        public void TransformSystemGroup_Flat([Values] bool dirtyTransforms)
        {
            int entityCount = 10000;
#if !ENABLE_TRANSFORM_V1
            var archetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalTransform), typeof(WorldTransform),
                typeof(RngComponent), typeof(ExpectedLocalToWorld));
#else
            var archetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Translation), typeof(Rotation),
                typeof(Scale), typeof(RngComponent), typeof(ExpectedLocalToWorld));
#endif

            using(var entities = m_Manager.CreateEntity(archetype, entityCount, Allocator.Persistent))
            {
                for(int i=0; i < entities.Length; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new RngComponent {Rng = Random.CreateFromIndex((uint)(i+1))});
                }
                World.GetOrCreateSystemManaged<RandomizeTransforms>().Update();
                // One tick to prime the results
                UpdateTransformSystems_All();

                for (int measureSystemIndex = -1; measureSystemIndex < TransformSystems.Count; ++measureSystemIndex)
                {
                    string measureSystemName = GetTransformSystemName(measureSystemIndex);
                    Measure.Method(UpdateTransformSystems_Measure)
                        .SetUp(() =>
                        {
                            if (dirtyTransforms)
                            {
                                World.GetOrCreateSystemManaged<RandomizeTransforms>().Update();
                            }
                            UpdateTransformSystems_Setup(measureSystemIndex);
                        })
                        .CleanUp(() =>
                        {
                            UpdateTransformSystems_Cleanup();
                            World.UpdateAllocator.Rewind();
                            // Only for validation; enabling will significantly affect performance test results
                            //for (int i = 0; i < entities.Length; ++i)
                            //{
                            //    var expected = m_Manager.GetComponentData<ExpectedLocalToWorld>(entities[i]).Value;
                            //    var actual = m_Manager.GetComponentData<LocalToWorld>(entities[i]).Value;
                            //    Assert.AreEqual(expected, actual);
                            //}
                        })
                        .SampleGroup(new SampleGroup(measureSystemName, SampleUnit.Microsecond))
                        .WarmupCount(1)
                        .MeasurementCount(10)
                        .Run();
                }
            }
        }

        // Run the transform systems on a hierarchy with one root entity and a large number of children.
        // Only the root entity's transform changes if dirtyRootTransform is true.
        [Test, Performance]
        public void TransformSystemGroup_OneRootManyChildren([Values] bool dirtyRootTransform)
        {
            int childEntityCount = 10000;
#if !ENABLE_TRANSFORM_V1
            var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalTransform), typeof(WorldTransform));
            var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(WorldTransform),
                typeof(LocalTransform), typeof(Parent));
#else
            var rootArchetype = m_Manager.CreateArchetype(typeof(Translation), typeof(Rotation), typeof(Scale), typeof(LocalToWorld));
            var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Translation), typeof(Rotation),
                typeof(Scale), typeof(LocalToParent), typeof(Parent));
#endif
            var rng = new Random(17);
            var rootEnt = m_Manager.CreateEntity(rootArchetype);
#if !ENABLE_TRANSFORM_V1
            m_Manager.SetComponentData(rootEnt, LocalTransform.FromPositionRotationScale(
                rng.NextFloat3(),
                rng.NextQuaternionRotation(),
                rng.NextFloat()));
#else
            m_Manager.SetComponentData(rootEnt, new Translation { Value = rng.NextFloat3() });
            m_Manager.SetComponentData(rootEnt, new Rotation { Value = rng.NextQuaternionRotation() });
            m_Manager.SetComponentData(rootEnt, new Scale { Value = rng.NextFloat() });
#endif
            using(var entities = m_Manager.CreateEntity(childArchetype, childEntityCount, Allocator.Persistent))
            {
                for(int i=0; i < entities.Length; ++i)
                {
                    m_Manager.SetComponentData(entities[i], new Parent {Value = rootEnt});
#if !ENABLE_TRANSFORM_V1
                    m_Manager.SetComponentData(entities[i], LocalTransform.FromPositionRotationScale(
                        rng.NextFloat3(),
                        rng.NextQuaternionRotation(),
                        rng.NextFloat()));

#else
                    m_Manager.SetComponentData(entities[i], new Translation { Value = rng.NextFloat3() });
                    m_Manager.SetComponentData(entities[i], new Rotation { Value = rng.NextQuaternionRotation() });
                    m_Manager.SetComponentData(entities[i], new Scale { Value = rng.NextFloat() });
#endif
                }
                // One tick to prime the results
                UpdateTransformSystems_All();

                for (int measureSystemIndex = -1; measureSystemIndex < TransformSystems.Count; ++measureSystemIndex)
                {
                    string measureSystemName = GetTransformSystemName(measureSystemIndex);
                    Measure.Method(UpdateTransformSystems_Measure)
                        .SetUp(() =>
                        {
                            if (dirtyRootTransform)
                            {
                                // Move root entity, to force the children to update
#if !ENABLE_TRANSFORM_V1
                                m_Manager.SetComponentData(rootEnt, LocalTransform.FromPositionRotationScale(
                                    rng.NextFloat3(),
                                    rng.NextQuaternionRotation(),
                                    rng.NextFloat()));
#else
                                m_Manager.SetComponentData(rootEnt, new Translation { Value = rng.NextFloat3() });
                                m_Manager.SetComponentData(rootEnt, new Rotation { Value = rng.NextQuaternionRotation() });
                                m_Manager.SetComponentData(rootEnt, new Scale { Value = rng.NextFloat() });
#endif
                                UpdateTransformSystems_Setup(measureSystemIndex);
                            }
                        })
                        .CleanUp(() =>
                        {
                            UpdateTransformSystems_Cleanup();
                            World.UpdateAllocator.Rewind();
#if !ENABLE_TRANSFORM_V1
                            // Only for validation; enabling will significantly affect performance test results
                            //var rootL2w = m_Manager.GetComponentData<WorldTransform>(rootEnt).Value;
                            //for (int i = 0; i < entities.Length; ++i)
                            //{
                            //    var expected = rootL2w.TransformTransform(m_Manager.GetComponentData<LocalTransform>(entities[i]).Value).ToMatrix();
                            //    var actual = m_Manager.GetComponentData<LocalToWorld>(entities[i]).Value;
                            //    Assert.AreEqual(expected, actual);
                            //}
#endif
                        })
                        .SampleGroup(new SampleGroup(measureSystemName, SampleUnit.Microsecond))
                        .WarmupCount(1)
                        .MeasurementCount(10)
                        .Run();
                }
            }
        }

        // Run the transform systems on many hierarchies, each reasonably deep
        [Test, Performance]
        public void TransformSystemGroup_ManyDeepHierarchies([Values] bool dirtyChildTransforms)
        {
            int rootEntityCount = 1000;
            int childEntityCountPerRoot = 10;
#if !ENABLE_TRANSFORM_V1
            var rootArchetype =
                m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalTransform), typeof(WorldTransform));
            var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(WorldTransform),
                typeof(LocalTransform), typeof(Parent));
#else
            var rootArchetype = m_Manager.CreateArchetype(typeof(Translation), typeof(Rotation), typeof(Scale), typeof(LocalToWorld));
            var childArchetype = m_Manager.CreateArchetype(typeof(Translation), typeof(Rotation), typeof(Scale),
                typeof(LocalToWorld), typeof(LocalToParent), typeof(Parent));
#endif
            var rng = new Random(17);
            using var allChildEntities = new NativeList<Entity>(rootEntityCount * (childEntityCountPerRoot + 1),
                Allocator.Persistent);

            void AddChild(Entity parent, int depth)
            {
                var childEntity = m_Manager.CreateEntity(childArchetype);
#if !ENABLE_TRANSFORM_V1
                m_Manager.SetComponentData(childEntity, LocalTransform.FromPositionRotationScale(
                    rng.NextFloat3(),
                    rng.NextQuaternionRotation(),
                    0.1f + rng.NextFloat()));
#else
                m_Manager.SetComponentData(childEntity, new Translation { Value = rng.NextFloat3() });
                m_Manager.SetComponentData(childEntity, new Rotation { Value = rng.NextQuaternionRotation() });
                m_Manager.SetComponentData(childEntity, new Scale { Value = 0.1f + rng.NextFloat() });
#endif
                allChildEntities.Add(childEntity);

                m_Manager.SetComponentData(childEntity, new Parent { Value = parent });

                if (depth < childEntityCountPerRoot)
                    AddChild(childEntity, depth + 1);
            }

            using (var rootEntities =
                   m_Manager.CreateEntity(rootArchetype, rootEntityCount, Allocator.Persistent))
            {
                for (int i = 0; i < rootEntities.Length; i++)
                {

#if !ENABLE_TRANSFORM_V1
                    m_Manager.SetComponentData(rootEntities[i], LocalTransform.FromPositionRotationScale(
                        rng.NextFloat3(),
                        rng.NextQuaternionRotation(),
                        0.1f + rng.NextFloat()));
#else
                    m_Manager.SetComponentData(rootEntities[i], new Translation { Value = rng.NextFloat3() });
                    m_Manager.SetComponentData(rootEntities[i], new Rotation { Value = rng.NextQuaternionRotation() });
                    m_Manager.SetComponentData(rootEntities[i], new Scale { Value = 0.1f + rng.NextFloat() });
#endif
                    AddChild(rootEntities[i], 0);
                }
            }

            // One tick to prime the results
            UpdateTransformSystems_All();

            for (int measureSystemIndex = -1; measureSystemIndex < TransformSystems.Count; ++measureSystemIndex)
            {
                int iteration = 0;
                string measureSystemName = GetTransformSystemName(measureSystemIndex);
                Measure.Method(UpdateTransformSystems_Measure)
                    .SetUp(() =>
                    {
                        if (dirtyChildTransforms)
                        {
                            // move all child entities
                            foreach (var childEnt in allChildEntities)
                            {
#if !ENABLE_TRANSFORM_V1
                                m_Manager.SetComponentData(childEnt, LocalTransform.FromPositionRotationScale(
                                    rng.NextFloat3(),
                                    rng.NextQuaternionRotation(),
                                    0.1f + rng.NextFloat()));
#else
                                m_Manager.SetComponentData(childEnt, new Translation { Value = rng.NextFloat3() });
                                m_Manager.SetComponentData(childEnt, new Rotation { Value = rng.NextQuaternionRotation() });
                                m_Manager.SetComponentData(childEnt, new Scale { Value = 0.1f + rng.NextFloat() });
#endif
                            }
                        }
                        UpdateTransformSystems_Setup(measureSystemIndex);
                    })
                    .CleanUp(() =>
                    {
                        UpdateTransformSystems_Cleanup();
                        World.UpdateAllocator.Rewind();
#if !ENABLE_TRANSFORM_V1
                        // Only for validation; enabling will significantly affect performance test results
                        //for(int i=0; i<allChildEntities.Length; ++i)
                        //{
                        //    var childEnt = allChildEntities[i];
                        //    var parentEnt = m_Manager.GetComponentData<Parent>(childEnt).Value;
                        //    var parentWorldTransform = m_Manager.GetComponentData<WorldTransform>(parentEnt).Value;
                        //    Assert.AreEqual(parentWorldTransform, m_Manager.GetComponentData<ParentTransform>(childEnt).Value);
                        //    var expected = parentWorldTransform.TransformTransform(m_Manager.GetComponentData<LocalTransform>(childEnt).Value);
                        //    var actual = m_Manager.GetComponentData<WorldTransform>(childEnt).Value;
                        //    Assert.AreEqual(expected, actual, $"Mismatch in iteration {iteration} on child {i} of {allChildEntities.Length}");
                        //    var childWorldTransform = m_Manager.GetComponentData<WorldTransform>(childEnt);
                        //    var childLocalToWorld = m_Manager.GetComponentData<LocalToWorld>(childEnt);
                        //    Assert.IsTrue(AreNearlyEqual(childWorldTransform.Value.ToMatrix(), childLocalToWorld.Value, 0.00001f));
                        //}
#endif
                        iteration++;
                    })
                    .SampleGroup(new SampleGroup(measureSystemName, SampleUnit.Microsecond))
                    .WarmupCount(1)
                    .MeasurementCount(10)
                    .Run();
            }
        }
    }
}
