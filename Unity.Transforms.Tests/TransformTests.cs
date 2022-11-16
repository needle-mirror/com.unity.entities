using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Unity.Entities.Tests
{
    [TestFixture]
    partial class TransformTests : ECSTestsFixture
    {
        const float k_Tolerance = 0.01f;

        [Test]
        public void TRS_ChildPosition()
        {
#if !ENABLE_TRANSFORM_V1
            var parent = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LocalTransform));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LocalTransform), typeof(Parent));

            m_Manager.SetComponentData(parent, LocalTransform.Identity);
            m_Manager.SetComponentData(child, new Parent {Value = parent});
            m_Manager.SetComponentData(child,
                LocalTransform.FromRotation(quaternion.RotateY(math.PI))
                    .TransformTransform(
                        LocalTransform.FromPosition(new float3(0.0f, 0.0f, 1.0f))));

            World.GetOrCreateSystem<ParentSystem>().Update(World.Unmanaged);                // Connect parent and child
            World.GetOrCreateSystem<LocalToWorldSystem>().Update(World.Unmanaged);    // Write to the child's ParentTransform and WorldTransform
            m_Manager.CompleteAllTrackedJobs();
#else
            var parent = m_Manager.CreateEntity(typeof(LocalToWorld));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));

            m_Manager.SetComponentData(parent, new LocalToWorld {Value = float4x4.identity});
            m_Manager.SetComponentData(child, new Parent {Value = parent});
            m_Manager.SetComponentData(child, new LocalToParent
            {
                Value = math.mul(float4x4.RotateY((float)math.PI), float4x4.Translate(new float3(0.0f, 0.0f, 1.0f)))
            });

            World.GetOrCreateSystem<ParentSystem>().Update(World.Unmanaged);
            World.GetOrCreateSystem<LocalToParentSystem>().Update(World.Unmanaged);
            m_Manager.CompleteAllTrackedJobs();
#endif

            var childWorldPosition = m_Manager.GetComponentData<LocalToWorld>(child).Position;

            Assert.AreEqual(.0f, childWorldPosition.x, k_Tolerance);
            Assert.AreEqual(.0f, childWorldPosition.y, k_Tolerance);
            Assert.AreEqual(-1f, childWorldPosition.z, k_Tolerance);
        }

        [Test]
        public void TRS_RemovedParentDoesNotAffectChildPosition()
        {
#if !ENABLE_TRANSFORM_V1
            var parent = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(WorldTransform));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(WorldTransform), typeof(Parent), typeof(LocalTransform));

            m_Manager.SetComponentData(parent, WorldTransform.Identity);
            m_Manager.SetComponentData(child, new Parent {Value = parent});
            m_Manager.SetComponentData(child,
                LocalTransform.FromPositionRotation(new float3(0.0f, 0.0f, 1.0f), quaternion.RotateY(math.PI)));

            World.GetOrCreateSystem<ParentSystem>().Update(World.Unmanaged);                // Connect parent and child
            World.GetOrCreateSystem<LocalToWorldSystem>().Update(World.Unmanaged);    // Write to the child's ParentTransform and WorldTransform
            m_Manager.CompleteAllTrackedJobs();

            var expectedChildWorldPosition = m_Manager.GetComponentData<LocalToWorld>(child).Position;

            m_Manager.RemoveComponent<Parent>(child);

            m_Manager.SetComponentData(parent,
                WorldTransform.FromPositionRotation(new float3(0.0f, 0.0f, 1.0f), quaternion.RotateY((float)math.PI)));

            World.GetOrCreateSystem<ParentSystem>().Update(World.Unmanaged);                // Connect parent and child
            World.GetOrCreateSystem<LocalToWorldSystem>().Update(World.Unmanaged);    // Write to the child's ParentTransform and WorldTransform
            m_Manager.CompleteAllTrackedJobs();
#else
            var parent = m_Manager.CreateEntity(typeof(LocalToWorld));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));

            m_Manager.SetComponentData(parent, new LocalToWorld {Value = float4x4.identity});
            m_Manager.SetComponentData(child, new Parent {Value = parent});
            m_Manager.SetComponentData(child, new LocalToParent
            {
                Value = math.mul(float4x4.RotateY((float)math.PI), float4x4.Translate(new float3(0.0f, 0.0f, 1.0f)))
            });

            World.GetOrCreateSystem<ParentSystem>().Update(World.Unmanaged);
            World.GetOrCreateSystem<LocalToParentSystem>().Update(World.Unmanaged);
            m_Manager.CompleteAllTrackedJobs();

            var expectedChildWorldPosition = m_Manager.GetComponentData<LocalToWorld>(child).Position;

            m_Manager.RemoveComponent<Parent>(child);

            m_Manager.SetComponentData(parent, new LocalToWorld
            {
                Value = math.mul(float4x4.RotateY((float)math.PI), float4x4.Translate(new float3(0.0f, 0.0f, 1.0f)))
            });

            World.GetOrCreateSystem<ParentSystem>().Update(World.Unmanaged);
            World.GetOrCreateSystem<LocalToParentSystem>().Update(World.Unmanaged);
#endif

            var childWorldPosition = m_Manager.GetComponentData<LocalToWorld>(child).Position;

            Assert.AreEqual(childWorldPosition.x, expectedChildWorldPosition.x, k_Tolerance);
            Assert.AreEqual(childWorldPosition.y, expectedChildWorldPosition.y, k_Tolerance);
            Assert.AreEqual(childWorldPosition.z, expectedChildWorldPosition.z, k_Tolerance);
        }

        class TestHierarchy : IDisposable
        {
            private World World;
            private EntityManager m_Manager;

            private quaternion[] rotations;
            private float3[] translations;

            int[] rotationIndices;
            int[] translationIndices;
            int[] parentIndices;

            private NativeArray<Entity> bodyEntities;

            public void Dispose()
            {
                bodyEntities.Dispose();
            }

            public TestHierarchy(World world, EntityManager manager)
            {
                World = world;
                m_Manager = manager;

                rotations = new quaternion[]
                {
                    quaternion.EulerYZX(new float3(0.125f * (float)math.PI, 0.0f, 0.0f)),
                    quaternion.EulerYZX(new float3(0.5f * (float)math.PI, 0.0f, 0.0f)),
                    quaternion.EulerYZX(new float3((float)math.PI, 0.0f, 0.0f)),
                };
                translations = new float3[]
                {
                    new float3(0.0f, 0.0f, 1.0f),
                    new float3(0.0f, 1.0f, 0.0f),
                    new float3(1.0f, 0.0f, 0.0f),
                    new float3(0.5f, 0.5f, 0.5f),
                };

                //  0: R:[0] T:[0]
                //  1:  - R:[1] T:[1]
                //  2:    - R:[2] T:[0]
                //  3:    - R:[2] T:[1]
                //  4:    - R:[2] T:[2]
                //  5:      - R:[1] T:[0]
                //  6:      - R:[1] T:[1]
                //  7:      - R:[1] T:[2]
                //  8:  - R:[2] T:[2]
                //  9:    - R:[1] T:[0]
                // 10:    - R:[1] T:[1]
                // 11:    - R:[1] T:[2]
                // 12:      - R:[0] T:[0]
                // 13:        - R:[0] T:[1]
                // 14:          - R:[0] T:[2]
                // 15:            - R:[0] T:[2]

                rotationIndices = new int[] {0, 1, 2, 2, 2, 1, 1, 1, 2, 1, 1, 1, 0, 0, 0, 0};
                translationIndices = new int[] {0, 1, 0, 1, 2, 0, 1, 2, 2, 0, 1, 2, 0, 1, 2, 2};
                parentIndices = new int[] {-1, 0, 1, 1, 1, 4, 4, 4, 0, 8, 8, 8, 11, 12, 13, 14};
            }

            public int Count => rotationIndices.Length;
            public NativeArray<Entity> Entities => bodyEntities;

            public float4x4[] ExpectedLocalToParent()
            {
                var expectedLocalToParent = new float4x4[16];
                for (int i = 0; i < 16; i++)
                {
                    var rotationIndex = rotationIndices[i];
                    var translationIndex = translationIndices[i];
                    var localToParent = new float4x4(rotations[rotationIndex], translations[translationIndex]);
                    expectedLocalToParent[i] = localToParent;
                }

                return expectedLocalToParent;
            }

            public float4x4[] ExpectedLocalToWorld(float4x4[] expectedLocalToParent)
            {
                var expectedLocalToWorld = new float4x4[16];
                for (int i = 0; i < 16; i++)
                {
                    var parentIndex = parentIndices[i];
                    if (parentIndex == -1)
                    {
                        expectedLocalToWorld[i] = expectedLocalToParent[i];
                    }
                    else
                    {
                        expectedLocalToWorld[i] = math.mul(expectedLocalToWorld[parentIndex], expectedLocalToParent[i]);
                    }
                }

                return expectedLocalToWorld;
            }

            public void Create()
            {
#if !ENABLE_TRANSFORM_V1
                var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalTransform), typeof(WorldTransform));
                var bodyArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(WorldTransform), typeof(Parent), typeof(LocalTransform));
#else
                var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Rotation),
                    typeof(NonUniformScale), typeof(Translation));
                var bodyArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Rotation),
                    typeof(NonUniformScale), typeof(Translation), typeof(Parent), typeof(LocalToParent));
#endif

                CreateInternal(bodyArchetype, rootArchetype, 1.0f);
            }

            private void CreateInternal(EntityArchetype bodyArchetype, EntityArchetype rootArchetype, float scaleValue)
            {
                bodyEntities = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(16, ref World.UpdateAllocator);

                m_Manager.CreateEntity(bodyArchetype, bodyEntities);

                // replace the first one for loop convenience below
                m_Manager.DestroyEntity(bodyEntities[0]);
                bodyEntities[0] = m_Manager.CreateEntity(rootArchetype);

                for (int i = 0; i < 16; i++)
                {
                    var rotationIndex = rotationIndices[i];
                    var translationIndex = translationIndices[i];

#if !ENABLE_TRANSFORM_V1
                    var transform = LocalTransform.FromPositionRotationScale(
                            translations[translationIndex], rotations[rotationIndex], scaleValue);
                    m_Manager.SetComponentData(bodyEntities[i], transform);
#else
                    var rotation = new Rotation() {Value = rotations[rotationIndex]};
                    var translation = new Translation() {Value = translations[translationIndex]};
                    var scale = new NonUniformScale() {Value = new float3(scaleValue)};

                    m_Manager.SetComponentData(bodyEntities[i], rotation);
                    m_Manager.SetComponentData(bodyEntities[i], translation);
                    m_Manager.SetComponentData(bodyEntities[i], scale);
#endif
                }

                for (int i = 1; i < 16; i++)
                {
                    var parentIndex = parentIndices[i];
                    m_Manager.SetComponentData(bodyEntities[i], new Parent() {Value = bodyEntities[parentIndex]});
                }
            }

            public void CreateWithWorldToLocal()
            {
#if !ENABLE_TRANSFORM_V1
                var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(WorldTransform));
                var bodyArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalTransform));
#else
                var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Rotation),
                    typeof(NonUniformScale), typeof(Translation), typeof(WorldToLocal));
                var bodyArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Rotation),
                    typeof(NonUniformScale), typeof(Translation), typeof(Parent), typeof(LocalToParent),
                    typeof(WorldToLocal));
#endif

                CreateInternal(bodyArchetype, rootArchetype, 1.0f);
            }

#if !ENABLE_TRANSFORM_V1
#else
            public void CreateWithCompositeRotation()
            {
                var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(CompositeRotation),
                    typeof(Rotation),
                    typeof(NonUniformScale), typeof(Translation));
                var bodyArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(CompositeRotation),
                    typeof(Rotation),
                    typeof(NonUniformScale), typeof(Translation), typeof(Parent), typeof(LocalToParent));

                CreateInternal(bodyArchetype, rootArchetype, 1.0f);
            }

            public void CreateWithParentScaleInverse()
            {
                var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(CompositeRotation),
                    typeof(Rotation),
                    typeof(NonUniformScale), typeof(Translation));
                var bodyArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(CompositeRotation),
                    typeof(Rotation),
                    typeof(NonUniformScale), typeof(Translation), typeof(Parent), typeof(LocalToParent),
                    typeof(ParentScaleInverse));

                CreateInternal(bodyArchetype, rootArchetype, 2.0f);
            }

            public void CreateWithCompositeScaleParentScaleInverse()
            {
                var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(CompositeRotation),
                    typeof(Rotation),
                    typeof(NonUniformScale), typeof(Translation), typeof(CompositeScale));
                var bodyArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(CompositeRotation),
                    typeof(Rotation),
                    typeof(NonUniformScale), typeof(Translation), typeof(CompositeScale), typeof(Parent),
                    typeof(LocalToParent), typeof(ParentScaleInverse));

                CreateInternal(bodyArchetype, rootArchetype, 2.0f);
            }
#endif

            public Entity CreateWithWriteGroupChildren()
            {
#if !ENABLE_TRANSFORM_V1
                var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalTransform), typeof(WorldTransform), typeof(Child));
                var childArchetypeWithWriteGroup = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(WorldTransform), typeof(LocalTransform), typeof(Parent), typeof(Child), typeof(TestWriteGroupComponent));
                var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(WorldTransform), typeof(LocalTransform), typeof(Parent));

                var root = m_Manager.CreateEntity(rootArchetype);
                var child = m_Manager.CreateEntity(childArchetypeWithWriteGroup);
                var childChild = m_Manager.CreateEntity(childArchetype);

                m_Manager.SetComponentData(root, LocalTransform.Identity);

                m_Manager.SetComponentData(child, LocalTransform.Identity);
                m_Manager.SetComponentData(child, WorldTransform.Identity);
                m_Manager.SetComponentData(child, new TestWriteGroupComponent{Value = 42});
                m_Manager.SetComponentData(child, new Parent { Value = root });

                m_Manager.SetComponentData(childChild, LocalTransform.Identity);
                m_Manager.SetComponentData(childChild, new Parent { Value = child });
#else
                var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Translation), typeof(Rotation), typeof(Scale), typeof(Child));
                var childArchetypeWithWriteGroup = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalToParent), typeof(Translation), typeof(Rotation), typeof(Scale),  typeof(Parent), typeof(Child), typeof(TestWriteGroupComponent));
                var childArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(LocalToParent), typeof(Translation), typeof(Rotation), typeof(Scale), typeof(Parent));

                var root = m_Manager.CreateEntity(rootArchetype);
                var child = m_Manager.CreateEntity(childArchetypeWithWriteGroup);
                var childChild = m_Manager.CreateEntity(childArchetype);

                m_Manager.SetComponentData(root, new Translation{Value = 0});
                m_Manager.SetComponentData(root, new Rotation{Value = quaternion.identity});
                m_Manager.SetComponentData(root, new Scale{Value = 1});

                m_Manager.SetComponentData(child, new Translation{Value = 0});
                m_Manager.SetComponentData(child, new Rotation{Value = quaternion.identity});
                m_Manager.SetComponentData(child, new Scale{Value = 1});
                m_Manager.SetComponentData(child, new TestWriteGroupComponent{Value = 42});
                m_Manager.SetComponentData(child, new Parent { Value = root });

                m_Manager.SetComponentData(childChild, new Translation{Value = 0});
                m_Manager.SetComponentData(childChild, new Rotation{Value = quaternion.identity});
                m_Manager.SetComponentData(childChild, new Scale{Value = 1});
                m_Manager.SetComponentData(childChild, new Parent { Value = child });
#endif

                return childChild;
            }

            public void Update()
            {
#if !ENABLE_TRANSFORM_V1
                World.GetOrCreateSystem<ParentSystem>().Update(World.Unmanaged);                // Connect parent and child
                World.GetOrCreateSystem<LocalToWorldSystem>().Update(World.Unmanaged);    // Write to the child's ParentTransform and WorldTransform
#else
                World.GetOrCreateSystem<ParentSystem>().Update(World.Unmanaged);
                World.GetOrCreateSystem<CompositeRotationSystem>().Update(World.Unmanaged);
                World.GetOrCreateSystem<CompositeScaleSystem>().Update(World.Unmanaged);
                World.GetOrCreateSystem<ParentScaleInverseSystem>().Update(World.Unmanaged);
                World.GetOrCreateSystem<TRSToLocalToWorldSystem>().Update(World.Unmanaged);
                World.GetOrCreateSystem<TRSToLocalToParentSystem>().Update(World.Unmanaged);
                World.GetOrCreateSystem<LocalToParentSystem>().Update(World.Unmanaged);
                World.GetOrCreateSystem<WorldToLocalSystem>().Update(World.Unmanaged);
#endif

                // Force complete so that main thread (tests) can have access to direct editing.
                m_Manager.CompleteAllTrackedJobs();
            }

            internal static unsafe bool AssertCloseEnough(float4x4 expected, float4x4 actual)
            {
                var expectedp = (float*)&expected.c0.x;
                var actualp = (float*)&actual.c0.x;
                for (int i = 0; i < 16; i++)
                {
                    Assert.AreEqual(expectedp[i], actualp[i], k_Tolerance);
                }

                return true;
            }

            public void TestExpectedLocalToParent()
            {
                var expectedLocalToParent = ExpectedLocalToParent();

                // Check all non-root LocalToParent
                for (int i = 0; i < 16; i++)
                {
                    var entity = Entities[i];
                    var parentIndex = parentIndices[i];
                    if (parentIndex == -1)
                    {
                        Assert.IsFalse(m_Manager.HasComponent<Parent>(entity));
#if !ENABLE_TRANSFORM_V1
#else
                        Assert.IsFalse(m_Manager.HasComponent<LocalToParent>(entity));
#endif
                        continue;
                    }

#if !ENABLE_TRANSFORM_V1
                    var localToParent = m_Manager.GetComponentData<LocalTransform>(entity).ToMatrix();
#else
                    var localToParent = m_Manager.GetComponentData<LocalToParent>(entity).Value;
#endif
                    AssertCloseEnough(expectedLocalToParent[i], localToParent);
                }
            }

            public void TestExpectedLocalToWorld()
            {
                var expectedLocalToParent = ExpectedLocalToParent();
                var expectedLocalToWorld = ExpectedLocalToWorld(expectedLocalToParent);

                for (int i = 0; i < 16; i++)
                {
                    var entity = Entities[i];
                    if (m_Manager.Exists(entity))
                    {
                        var localToWorld = m_Manager.GetComponentData<LocalToWorld>(entity).Value;
                        AssertCloseEnough(expectedLocalToWorld[i], localToWorld);
                    }
                }
            }

#if !ENABLE_TRANSFORM_V1
#else
            public void TestExpectedWorldToLocal()
            {
                var expectedLocalToParent = ExpectedLocalToParent();
                var expectedLocalToWorld = ExpectedLocalToWorld(expectedLocalToParent);

                for (int i = 0; i < 16; i++)
                {
                    var entity = Entities[i];
                    if (m_Manager.Exists(entity))
                    {
                        var worldToLocal = m_Manager.GetComponentData<WorldToLocal>(entity).Value;
                        AssertCloseEnough(math.inverse(expectedLocalToWorld[i]), worldToLocal);
                    }
                }
            }
#endif

            public void RemoveSomeParents()
            {
                parentIndices[1] = -1;
                parentIndices[8] = -1;

                m_Manager.RemoveComponent<Parent>(Entities[1]);
                m_Manager.RemoveComponent<Parent>(Entities[8]);
#if !ENABLE_TRANSFORM_V1
                m_Manager.SetComponentData(Entities[1], m_Manager.GetComponentData<LocalTransform>(Entities[1]));
                m_Manager.SetComponentData(Entities[8], m_Manager.GetComponentData<LocalTransform>(Entities[8]));
                m_Manager.RemoveComponent<Parent>(Entities[1]);
                m_Manager.RemoveComponent<Parent>(Entities[8]);
#else
                m_Manager.RemoveComponent<LocalToParent>(Entities[1]);
                m_Manager.RemoveComponent<LocalToParent>(Entities[8]);
#endif
            }

            public void ChangeSomeParents()
            {
                parentIndices[4] = 3;
                parentIndices[8] = 7;

                m_Manager.SetComponentData<Parent>(Entities[4], new Parent {Value = Entities[3]});
                m_Manager.SetComponentData<Parent>(Entities[8], new Parent {Value = Entities[7]});
            }

            public void DeleteSomeParents()
            {
                // Effectively puts children of 0 at the root
                parentIndices[1] = -1;
                parentIndices[8] = -1;

#if !ENABLE_TRANSFORM_V1
                m_Manager.SetComponentData(Entities[1], m_Manager.GetComponentData<LocalTransform>(Entities[1]));
                m_Manager.SetComponentData(Entities[8], m_Manager.GetComponentData<LocalTransform>(Entities[8]));
                m_Manager.RemoveComponent<Parent>(Entities[1]);
                m_Manager.RemoveComponent<Parent>(Entities[8]);
#else
#endif
                m_Manager.DestroyEntity(Entities[0]);
            }

            public void DestroyAll()
            {
                m_Manager.DestroyEntity(Entities);
            }
        }

        [Test]
        public void TRS_TestHierarchyFirstUpdate()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.Create();
            testHierarchy.Update();

            testHierarchy.TestExpectedLocalToParent();
            testHierarchy.TestExpectedLocalToWorld();

            testHierarchy.Dispose();
        }

#if !ENABLE_TRANSFORM_V1
#else
        [Test]
        public void TRS_TestHierarchyFirstUpdateWithWorldtoLocal()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.CreateWithWorldToLocal();
            testHierarchy.Update();

            testHierarchy.TestExpectedLocalToParent();
            testHierarchy.TestExpectedLocalToWorld();
            testHierarchy.TestExpectedWorldToLocal();

            testHierarchy.Dispose();
        }
#endif

#if !ENABLE_TRANSFORM_V1
#else
        [Test]
        public void TRS_TestHierarchyFirstUpdateWithCompositeRotation()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.CreateWithCompositeRotation();
            testHierarchy.Update();

            testHierarchy.TestExpectedLocalToParent();
            testHierarchy.TestExpectedLocalToWorld();

            testHierarchy.Dispose();
        }
#endif

#if !ENABLE_TRANSFORM_V1
#else
        [Test]
        public void TRS_TestHierarchyFirstUpdateWitParentScaleInverse()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.CreateWithParentScaleInverse();
            testHierarchy.Update();

            testHierarchy.TestExpectedLocalToParent();

            testHierarchy.Dispose();
        }
#endif

#if !ENABLE_TRANSFORM_V1
#else
        [Test]
        public void TRS_TestHierarchyFirstUpdateWitCompositeScaleParentScaleInverse()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.CreateWithCompositeScaleParentScaleInverse();
            testHierarchy.Update();

            testHierarchy.TestExpectedLocalToParent();

            testHierarchy.Dispose();
        }
#endif

        [Test]
        public void TRS_TestHierarchyAfterParentRemoval()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.Create();
            testHierarchy.Update();

            testHierarchy.RemoveSomeParents();
            testHierarchy.Update();

            testHierarchy.TestExpectedLocalToParent();
            testHierarchy.TestExpectedLocalToWorld();

            testHierarchy.Dispose();
        }

        [Test]
        public void TRS_TestHierarchyAfterParentChange()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.Create();
            testHierarchy.Update();

            testHierarchy.ChangeSomeParents();
            testHierarchy.Update();

            testHierarchy.TestExpectedLocalToParent();
            testHierarchy.TestExpectedLocalToWorld();

            testHierarchy.Dispose();
        }

        [Test]
        public void TRS_TestHierarchyAfterParentDeleted()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.Create();
            testHierarchy.Update();

            testHierarchy.DeleteSomeParents();
            testHierarchy.Update();

            testHierarchy.TestExpectedLocalToParent();
            testHierarchy.TestExpectedLocalToWorld();

            testHierarchy.Dispose();
        }

        [Test]
        public void TRS_TestHierarchyDestroyAll()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.Create();
            testHierarchy.Update();

            // Make sure can handle destroying all parents and children on same frame
            testHierarchy.DestroyAll();
            testHierarchy.Update();

            // Make sure remaining cleanup handled cleanly.
            testHierarchy.Update();

            var entities = m_Manager.GetAllEntities();
            Assert.IsTrue(entities.Length == 0);

            testHierarchy.Dispose();
        }

#if !ENABLE_TRANSFORM_V1
        [WriteGroup(typeof(WorldTransform))]
#else
        [WriteGroup(typeof(LocalToWorld))]
#endif
        struct LocalToWorldWriteGroupComponent : IComponentData
        {
        }

        [Test,Ignore("write groups on WorldTransform shouldn't happen")] // TODO(DOTS-7271): update/remove test
        public void LocalToParentConsidersWriteGroups()
        {
#if !ENABLE_TRANSFORM_V1
            var testHierarchy = new TestHierarchy(World, m_Manager);

            var parent = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LocalTransform), typeof(WorldTransform));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(WorldTransform), typeof(Parent), typeof(LocalTransform));
            var childChild = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(WorldTransform), typeof(Parent), typeof(LocalTransform), typeof(ParentTransform));

            m_Manager.SetComponentData(parent, WorldTransform.Identity);
            m_Manager.SetComponentData(child, new Parent {Value = parent});
            m_Manager.SetComponentData(child, LocalTransform.Identity);
            m_Manager.SetComponentData(childChild, new Parent {Value = child});
            m_Manager.SetComponentData(childChild, LocalTransform.Identity);

            m_Manager.SetComponentData(parent, WorldTransform.FromPosition(2, 2, 2));
            testHierarchy.Update();
            Assert.AreEqual(new float3(2, 2, 2), m_Manager.GetComponentData<WorldTransform>(child).Position);
            Assert.AreEqual(new float3(2, 2, 2), m_Manager.GetComponentData<WorldTransform>(childChild).Position);

            m_Manager.AddComponentData(child, new LocalToWorldWriteGroupComponent());
            m_Manager.SetComponentData(parent, WorldTransform.FromPosition(3, 3, 3));
            m_Manager.SetComponentData(child, WorldTransform.FromPosition(4, 4, 4));
            testHierarchy.Update();
            Assert.AreEqual(new float3(4, 4, 4), m_Manager.GetComponentData<WorldTransform>(child).Position);
            Assert.AreEqual(new float3(4, 4, 4), m_Manager.GetComponentData<WorldTransform>(childChild).Position);
#else
            var testHierarchy = new TestHierarchy(World, m_Manager);

            var parent = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Translation));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));
            var childChild = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));

            m_Manager.SetComponentData(parent, new LocalToWorld {Value = float4x4.identity});
            m_Manager.SetComponentData(child, new Parent {Value = parent});
            m_Manager.SetComponentData(child, new LocalToParent {Value = float4x4.identity});
            m_Manager.SetComponentData(childChild, new Parent {Value = child});
            m_Manager.SetComponentData(childChild, new LocalToParent {Value = float4x4.identity});

            m_Manager.SetComponentData(parent, new Translation {Value = new float3(2, 2, 2)});
            testHierarchy.Update();
            Assert.AreEqual(new float3(2, 2, 2), m_Manager.GetComponentData<LocalToWorld>(child).Position);
            Assert.AreEqual(new float3(2, 2, 2), m_Manager.GetComponentData<LocalToWorld>(childChild).Position);

            m_Manager.AddComponentData(child, new LocalToWorldWriteGroupComponent());
            m_Manager.SetComponentData(parent, new Translation {Value = new float3(3, 3, 3)});

            m_Manager.SetComponentData(child,
                new LocalToWorld {Value = math.mul(float4x4.Translate(new float3(4, 4, 4)), float4x4.identity)});
            testHierarchy.Update();
            Assert.AreEqual(new float3(4, 4, 4), m_Manager.GetComponentData<LocalToWorld>(child).Position);
            Assert.AreEqual(new float3(4, 4, 4), m_Manager.GetComponentData<LocalToWorld>(childChild).Position);
#endif
        }

        [Test]
        public void TRS_TestHierarchyAddExtraChildren()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.Create();
            testHierarchy.Update();

            // Add a lot more than parent count children on same frame
            var childCount = 5;
            for (int i = 0; i < testHierarchy.Entities.Length; i++)
            {
                var parent = testHierarchy.Entities[i];
                for (int j = 0; j < childCount; j++)
                {
#if !ENABLE_TRANSFORM_V1
                    var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(WorldTransform), typeof(Parent), typeof(LocalTransform));
                    m_Manager.SetComponentData(child, new Parent {Value = parent});
                    m_Manager.SetComponentData(child, LocalTransform.Identity);
#else
                    var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));
                    m_Manager.SetComponentData(child, new Parent {Value = parent});
                    m_Manager.SetComponentData(child, new LocalToParent {Value = float4x4.identity});
#endif
                }
            }

            testHierarchy.Update();
            testHierarchy.Update();

            testHierarchy.Dispose();
        }

        [Test]
        public void TRS_TestHierarchyAddExtraChildrenChangeArchetype()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.Create();
            testHierarchy.Update();

            // Add a lot more than parent count children on same frame
            var childCount = 5;
            for (int i = 0; i < testHierarchy.Entities.Length; i++)
            {
                var parent = testHierarchy.Entities[i];
                for (int j = 0; j < childCount; j++)
                {
                    var child = m_Manager.CreateEntity(typeof(LocalToWorld));
                    m_Manager.AddComponentData(child, new Parent {Value = parent});
#if !ENABLE_TRANSFORM_V1
                    m_Manager.AddComponentData(child, WorldTransform.Identity);
                    m_Manager.AddComponentData(child, LocalTransform.Identity);
#else
                    m_Manager.AddComponentData(child, new LocalToParent {Value = float4x4.identity});
#endif
                }
            }

            testHierarchy.Update();
            testHierarchy.Update();

            testHierarchy.Dispose();
        }

        [Test]
        public void TRS_TestHierarchyAddExtraChildrenTwiceChangeArchetype()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.Create();
            testHierarchy.Update();

            // Add a lot more than parent count children on same frame
            var childCount = 5;
            for (int i = 0; i < testHierarchy.Entities.Length; i++)
            {
                var parent = testHierarchy.Entities[i];
                for (int j = 0; j < childCount; j++)
                {
                    var child = m_Manager.CreateEntity(typeof(LocalToWorld));
                    m_Manager.AddComponentData(child, new Parent {Value = parent});
#if !ENABLE_TRANSFORM_V1
                    m_Manager.AddComponentData(child, WorldTransform.Identity);
                    m_Manager.AddComponentData(child, LocalTransform.Identity);
#else
                    m_Manager.AddComponentData(child, new LocalToParent {Value = float4x4.identity});
#endif
                }
            }

            testHierarchy.Update();

            // Add more children to same parents
            for (int i = 0; i < testHierarchy.Entities.Length; i++)
            {
                var parent = testHierarchy.Entities[i];
                for (int j = 0; j < childCount; j++)
                {
                    var child = m_Manager.CreateEntity(typeof(LocalToWorld));
                    m_Manager.AddComponentData(child, new Parent {Value = parent});
#if !ENABLE_TRANSFORM_V1
                    m_Manager.AddComponentData(child, WorldTransform.Identity);
                    m_Manager.AddComponentData(child, LocalTransform.Identity);
#else
                    m_Manager.AddComponentData(child, new LocalToParent {Value = float4x4.identity});
#endif
                }
            }

            testHierarchy.Update();

            testHierarchy.Dispose();
        }

        [Test]
        public void TRS_TestHierarchyAddExtraChildrenInnerChangeArchetype()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.Create();
            testHierarchy.Update();

            // Add a lot more than parent count children on same frame
            var childCount = 5;
            for (int i = 0; i < testHierarchy.Entities.Length; i++)
            {
                var parent = testHierarchy.Entities[i];
                for (int j = 0; j < childCount; j++)
                {
                    var child = m_Manager.CreateEntity(typeof(LocalToWorld));
                    m_Manager.AddComponentData(child, new Parent {Value = parent});
#if !ENABLE_TRANSFORM_V1
                    m_Manager.AddComponentData(child, WorldTransform.Identity);
                    m_Manager.AddComponentData(child, LocalTransform.Identity);
#else
                    m_Manager.AddComponentData(child, new LocalToParent {Value = float4x4.identity});
#endif

                    for (int k = 0; k < childCount; k++)
                    {
                        var nextChild = m_Manager.CreateEntity(typeof(LocalToWorld));
                        m_Manager.AddComponentData(nextChild, new Parent {Value = child});
#if !ENABLE_TRANSFORM_V1
                        m_Manager.AddComponentData(nextChild, LocalTransform.Identity);
                        m_Manager.AddComponentData(nextChild, WorldTransform.Identity);
#else
                        m_Manager.AddComponentData(nextChild, new LocalToParent {Value = float4x4.identity});
#endif
                    }
                }
            }

            testHierarchy.Update();
            testHierarchy.Update();

            testHierarchy.Dispose();
        }

        [Test]
        public void TRS_TestHierarchyAddExtraChildrenChangeParent()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.Create();
            testHierarchy.Update();

            // Add a lot more than parent count children on same frame
            var childCount = 5;
            var children = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(testHierarchy.Entities.Length * childCount, ref World.UpdateAllocator);
            for (int i = 0; i < testHierarchy.Entities.Length; i++)
            {
                var parent = testHierarchy.Entities[i];
                for (int j = 0; j < childCount; j++)
                {
#if !ENABLE_TRANSFORM_V1
                    var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalTransform));
                    m_Manager.SetComponentData(child, new Parent {Value = parent});
                    m_Manager.AddComponentData(child, WorldTransform.Identity);
                    m_Manager.AddComponentData(child, LocalTransform.Identity);
#else
                    var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));
                    m_Manager.SetComponentData(child, new Parent {Value = parent});
                    m_Manager.SetComponentData(child, new LocalToParent {Value = float4x4.identity});
#endif
                    children[(i * childCount) + j] = child;
                }
            }

            testHierarchy.Update();

            // Change parents
            for (int i = 0; i < children.Length; i++)
            {
                var parent = testHierarchy.Entities[0];
                var child = children[i];
                m_Manager.SetComponentData(child, new Parent {Value = parent});
            }

            testHierarchy.Update();

            testHierarchy.Dispose();
        }

        [Test]
        public void TRS_TestHierarchyAddExtraChildrenChangeParentAgain()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);
            testHierarchy.Create();
            testHierarchy.Update();

            // Add a lot more than parent count children on same frame
            var childCount = 5;

            var children = CollectionHelper.CreateNativeArray<Entity, RewindableAllocator>(testHierarchy.Entities.Length * childCount, ref World.UpdateAllocator);
            for (int i = 0; i < testHierarchy.Entities.Length; i++)
            {
                var parent = testHierarchy.Entities[i];
                for (int j = 0; j < childCount; j++)
                {
#if !ENABLE_TRANSFORM_V1
                    var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalTransform));
                    m_Manager.SetComponentData(child, new Parent {Value = parent});
                    m_Manager.AddComponentData(child, WorldTransform.Identity);
#else
                    var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));
                    m_Manager.SetComponentData(child, new Parent {Value = parent});
                    m_Manager.SetComponentData(child, new LocalToParent {Value = float4x4.identity});
#endif
                    children[(i * childCount) + j] = child;
                }
            }

            testHierarchy.Update();

            // Change parents
            for (int i = 0; i < testHierarchy.Entities.Length; i++)
            {
                var parent = testHierarchy.Entities[i];
                for (int j = 0; j < childCount; j++)
                {
                    var child = children[(i * childCount) + j];
                    m_Manager.SetComponentData(child, new Parent {Value = parent});
                }
            }

            testHierarchy.Update();
            testHierarchy.Dispose();
        }

#if !ENABLE_TRANSFORM_V1
        [WriteGroup(typeof(WorldTransform))]
#else
        [WriteGroup(typeof(LocalToWorld))]
#endif
        struct TestWriteGroupComponent : IComponentData
        {
            public int Value;
        }

        [UpdateInGroup(typeof(SimulationSystemGroup))]
        [UpdateBefore(typeof(TransformSystemGroup))]
        public partial class TestTransformWriteGroupSystem : SystemBase
        {
            protected override void OnUpdate()
            {
#if !ENABLE_TRANSFORM_V1
                Entities.ForEach((ref WorldTransform worldTransform, in TestWriteGroupComponent test) =>
                {
                    worldTransform = WorldTransform.FromPosition(new float3(test.Value));
                }).ScheduleParallel();
#else
                Entities.ForEach((ref LocalToWorld localToWorld, in TestWriteGroupComponent test) =>
                {
                    localToWorld.Value = new float4x4(float3x3.identity, new float3(test.Value));
                }).ScheduleParallel();
#endif
            }
        }

        [Test,Ignore("Write groups on WorldTransform again")] // TODO(DOTS-7271): update/remove test
        public void TRS_TestHierarchyUpdatesNestedParentsChildWithWriteGroups()
        {
            // P
            // -- C1 <- This has an archetype with writegroup for LocalToWorld
            // -- -- C2 <- Normal child, this should be updated by LocalToParentSystem if I modify C1's LocalToWorld in a custom system

            var testHierarchy = new TestHierarchy(World, m_Manager);
            var c2Entity = testHierarchy.CreateWithWriteGroupChildren();
            testHierarchy.Update();
            World.GetOrCreateSystemManaged<TestTransformWriteGroupSystem>().Update();
            testHierarchy.Update();

            var localToWorld = m_Manager.GetComponentData<LocalToWorld>(c2Entity);
            var expectedLocalToWorld = new float4x4(float3x3.identity, new float3(42));

            TestHierarchy.AssertCloseEnough(expectedLocalToWorld, localToWorld.Value);
        }

#if !ENABLE_TRANSFORM_V1
        struct ScaledEntityHierarchy
        {
            public Entity entA, entB, entC, entD, entE, entF, entG, entH;
            public LocalTransform ltA, ltB, ltC, ltD, ltE, ltF, ltG, ltH;
            public float3x3 ptmC, ptmD, ptmE, ptmF;
        }
        void CreateScaledEntityHierarchy(out ScaledEntityHierarchy h)
        {
            // Create a hierarchy of entities. All have LocalTransform and LocalToWorld. In addition:
            //
            //    A         B has uniform scale
            //   / \        C has a PostTransformMatrix
            //  B   C       D has a PostTransformMatrix, and should combine B's scale and D's PTM
            // /   / \      E has a PostTransformMatrix, and should combine C+E's PTM
            // D  E   F     F has a PostTransformMatrix, and should combine C+F's PTM
            //       / \    G has uniform scale, and should inherit C+F's PTM values
            //      G   H   H should inherit C+F's PTM values
            EntityArchetype archetype = m_Manager.CreateArchetype(typeof(EcsTestData), typeof(LocalTransform),
                typeof(LocalToWorld));
            h = new ScaledEntityHierarchy { };
            h.entA = m_Manager.CreateEntity(archetype);
            h.entB = m_Manager.CreateEntity(archetype);
            h.entC = m_Manager.CreateEntity(archetype);
            h.entD = m_Manager.CreateEntity(archetype);
            h.entE = m_Manager.CreateEntity(archetype);
            h.entF = m_Manager.CreateEntity(archetype);
            h.entG = m_Manager.CreateEntity(archetype);
            h.entH = m_Manager.CreateEntity(archetype);
            m_Manager.AddComponentData(h.entB, new Parent { Value = h.entA });
            m_Manager.AddComponentData(h.entC, new Parent { Value = h.entA });
            m_Manager.AddComponentData(h.entD, new Parent { Value = h.entB });
            m_Manager.AddComponentData(h.entE, new Parent { Value = h.entC });
            m_Manager.AddComponentData(h.entF, new Parent { Value = h.entC });
            m_Manager.AddComponentData(h.entG, new Parent { Value = h.entF });
            m_Manager.AddComponentData(h.entH, new Parent { Value = h.entF });
            // Initialize LocalTransforms
            h.ltA = LocalTransform.FromPositionRotationScale(new float3(1,2,3), quaternion.RotateX(math.PI), 1.0f);
            h.ltB = LocalTransform.FromPositionRotationScale(new float3(1,2,3), quaternion.RotateX(math.PI), 2.0f);
            h.ltC = LocalTransform.FromPositionRotationScale(new float3(1,2,3), quaternion.RotateX(math.PI), 1.0f);
            h.ltD = LocalTransform.FromPositionRotationScale(new float3(1,2,3), quaternion.RotateX(math.PI), 1.0f);
            h.ltE = LocalTransform.FromPositionRotationScale(new float3(1,2,3), quaternion.RotateX(math.PI), 1.0f);
            h.ltF = LocalTransform.FromPositionRotationScale(new float3(1,2,3), quaternion.RotateX(math.PI), 1.0f);
            h.ltG = LocalTransform.FromPositionRotationScale(new float3(1,2,3), quaternion.RotateX(math.PI), 3.0f);
            h.ltH = LocalTransform.FromPositionRotationScale(new float3(1,2,3), quaternion.RotateX(math.PI), 1.0f);
            m_Manager.SetComponentData(h.entA, h.ltA);
            m_Manager.SetComponentData(h.entB, h.ltB);
            m_Manager.SetComponentData(h.entC, h.ltC);
            m_Manager.SetComponentData(h.entD, h.ltD);
            m_Manager.SetComponentData(h.entE, h.ltE);
            m_Manager.SetComponentData(h.entF, h.ltF);
            m_Manager.SetComponentData(h.entG, h.ltG);
            m_Manager.SetComponentData(h.entH, h.ltH);
            // Add PostTransformMatrix and PropagateLocalToWorld where applicable
            var ptmTypes = new ComponentTypeSet(typeof(PostTransformScale), typeof(PropagateLocalToWorld));
            m_Manager.AddComponent(h.entC, ptmTypes);
            m_Manager.AddComponent(h.entD, ptmTypes);
            m_Manager.AddComponent(h.entE, ptmTypes);
            m_Manager.AddComponent(h.entF, ptmTypes);
            h.ptmC = float3x3.Scale(1,2,3);
            h.ptmD = float3x3.Scale(4,5,6);
            h.ptmE = float3x3.Scale(7,8,9);
            h.ptmF = float3x3.Scale(10,11,12);
            m_Manager.SetComponentData(h.entC, new PostTransformScale{Value = h.ptmC});
            m_Manager.SetComponentData(h.entD, new PostTransformScale{Value = h.ptmD});
            m_Manager.SetComponentData(h.entE, new PostTransformScale{Value = h.ptmE});
            m_Manager.SetComponentData(h.entF, new PostTransformScale{Value = h.ptmF});
        }

        void ValidateScaledEntityHierarchy(in ScaledEntityHierarchy h)
        {
            float4x4 ltwA = m_Manager.GetComponentData<LocalToWorld>(h.entA).Value;
            float4x4 ltwB = m_Manager.GetComponentData<LocalToWorld>(h.entB).Value;
            float4x4 ltwC = m_Manager.GetComponentData<LocalToWorld>(h.entC).Value;
            float4x4 ltwD = m_Manager.GetComponentData<LocalToWorld>(h.entD).Value;
            float4x4 ltwE = m_Manager.GetComponentData<LocalToWorld>(h.entE).Value;
            float4x4 ltwF = m_Manager.GetComponentData<LocalToWorld>(h.entF).Value;
            float4x4 ltwG = m_Manager.GetComponentData<LocalToWorld>(h.entG).Value;
            float4x4 ltwH = m_Manager.GetComponentData<LocalToWorld>(h.entH).Value;
            Assert.AreEqual(h.ltA.ToMatrix(), ltwA);
            Assert.AreEqual(h.ltA.TransformTransform(h.ltB).ToMatrix(), ltwB);
            Assert.AreEqual(math.mul(h.ltA.TransformTransform(h.ltC).ToMatrix(), new float4x4(h.ptmC, float3.zero)), ltwC);
            Assert.AreEqual(math.mul(h.ltA.TransformTransform(h.ltB).TransformTransform(h.ltD).ToMatrix(), new float4x4(h.ptmD, float3.zero)), ltwD);
            Assert.AreEqual(math.mul(math.mul(ltwC, h.ltE.ToMatrix()), new float4x4(h.ptmE, float3.zero)), ltwE);
            Assert.AreEqual(math.mul(math.mul(ltwC, h.ltF.ToMatrix()), new float4x4(h.ptmF, float3.zero)), ltwF);
            Assert.AreEqual(math.mul(ltwF, h.ltG.ToMatrix()), ltwG);
            Assert.AreEqual(math.mul(ltwF, h.ltH.ToMatrix()), ltwH);
        }

        [Test]
        public void TransformHierarchy_ComplexScale_Works()
        {
            // Create the TransformSystemGroup
            var transformSystemGroup = World.CreateSystemManaged<TransformSystemGroup>();
            var parentSystem = World.CreateSystem<ParentSystem>();
            var localToWorldSystem = World.CreateSystem<LocalToWorldSystem>();

            transformSystemGroup.AddSystemToUpdateList(parentSystem);
            transformSystemGroup.AddSystemToUpdateList(localToWorldSystem);
            transformSystemGroup.SortSystems();
            // Create the hierarchy
            CreateScaledEntityHierarchy(out ScaledEntityHierarchy h);
            // Tick & validate
            transformSystemGroup.Update();
            ValidateScaledEntityHierarchy(h);
        }
#endif

        [Test]
        public void EnabledHierarchy_CreatesChildBuffer()
        {
            Entity SimpleHiearchy()
            {
#if !ENABLE_TRANSFORM_V1
                var parent = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LinkedEntityGroup), typeof(LocalTransform));
                var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalTransform));
#else
                var parent = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LinkedEntityGroup));
                var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));
#endif
                var linkedEntityGroup = m_Manager.GetBuffer<LinkedEntityGroup>(parent);
                linkedEntityGroup.Add(new LinkedEntityGroup {Value = parent});
                linkedEntityGroup.Add(new LinkedEntityGroup {Value = child});
                m_Manager.SetComponentData(child, new Parent {Value = parent});

                return parent;
            }

            var parentA = SimpleHiearchy();
            var parentB = SimpleHiearchy();

            var parentSystem = World.GetOrCreateSystem<ParentSystem>();

            // with the hierarchy disabled, updating the transform system doesn't create a child buffer
            m_Manager.SetEnabled(parentA, false);

            Assert.IsFalse(m_Manager.HasComponent<Child>(parentA));
            Assert.IsFalse(m_Manager.HasComponent<Child>(parentB));
            parentSystem.Update(World.Unmanaged);
            Assert.IsFalse(m_Manager.HasComponent<Child>(parentA));
            Assert.IsTrue(m_Manager.HasComponent<Child>(parentB));

            // with the hierarchy enabled, updating the transform system creates a child buffer
            m_Manager.SetEnabled(parentA, true);

            Assert.IsFalse(m_Manager.HasComponent<Child>(parentA));
            Assert.IsTrue(m_Manager.HasComponent<Child>(parentB));
            parentSystem.Update(World.Unmanaged);
            Assert.IsTrue(m_Manager.HasComponent<Child>(parentA));
            Assert.IsTrue(m_Manager.HasComponent<Child>(parentB));
        }

        [Test]
        public void EnabledHierarchy_CreatesChildBuffer_ECB()
        {
            Entity SimpleHiearchy()
            {
#if !ENABLE_TRANSFORM_V1
                var parent = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LinkedEntityGroup), typeof(WorldTransform));
                var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalTransform), typeof(WorldTransform));
#else
                var parent = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LinkedEntityGroup));
                var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));
#endif

                var linkedEntityGroup = m_Manager.GetBuffer<LinkedEntityGroup>(parent);
                linkedEntityGroup.Add(new LinkedEntityGroup {Value = parent});
                linkedEntityGroup.Add(new LinkedEntityGroup {Value = child});
                m_Manager.SetComponentData(child, new Parent {Value = parent});

                return parent;
            }

            var parentA = SimpleHiearchy();
            var parentB = SimpleHiearchy();

            var parentSystem = World.GetOrCreateSystem<ParentSystem>();

            // with the hierarchy disabled, updating the transform system doesn't create a child buffer
            using (var ecbDisabled = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                ecbDisabled.SetEnabled(parentA, false);
                ecbDisabled.Playback(m_Manager);
            }

            Assert.IsFalse(m_Manager.HasComponent<Child>(parentA));
            Assert.IsFalse(m_Manager.HasComponent<Child>(parentB));
            parentSystem.Update(World.Unmanaged);
            Assert.IsFalse(m_Manager.HasComponent<Child>(parentA));
            Assert.IsTrue(m_Manager.HasComponent<Child>(parentB));

            // with the hierarchy enabled, updating the transform system creates a child buffer
            using (var ecbEnabled = new EntityCommandBuffer(World.UpdateAllocator.ToAllocator))
            {
                ecbEnabled.SetEnabled(parentA, true);
                ecbEnabled.Playback(m_Manager);
            }

            Assert.IsFalse(m_Manager.HasComponent<Child>(parentA));
            Assert.IsTrue(m_Manager.HasComponent<Child>(parentB));
            parentSystem.Update(World.Unmanaged);
            Assert.IsTrue(m_Manager.HasComponent<Child>(parentA));
            Assert.IsTrue(m_Manager.HasComponent<Child>(parentB));
        }

        [Test]
        public void TRS_SetParent_OldParentIsDestroyed([Values] bool withCleanup)
        {
            var parentSystem = World.GetOrCreateSystem<ParentSystem>();

#if !ENABLE_TRANSFORM_V1
            var parentA = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(WorldTransform));
            var parentB = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(WorldTransform));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalTransform), typeof(WorldTransform));
#else
            var parentA = m_Manager.CreateEntity(typeof(LocalToWorld));
            var parentB = m_Manager.CreateEntity(typeof(LocalToWorld));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));
#endif

            if (withCleanup)
            {
                // Having a cleanup component or not is important to validate the check in the
                // parenting system that it should not try to update a child buffer when it doesn't exist.
                // Because that's the difference between having a destroyed parent entity,
                // or a parent entity without the buffer.
                m_Manager.AddComponent<EcsCleanupTag1>(parentA);
            }

            m_Manager.SetComponentData(child, new Parent {Value = parentA});

            parentSystem.Update(World.Unmanaged);

            m_Manager.DestroyEntity(parentA);
            m_Manager.SetComponentData(child, new Parent {Value = parentB});

            parentSystem.Update(World.Unmanaged);

            Assert.AreEqual(parentB, m_Manager.GetComponentData<Parent>(child).Value);

            var children = m_Manager.GetBuffer<Child>(parentB).AsNativeArray();
            CollectionAssert.AreEqual(children.Reinterpret<Entity>(), new[] { child });
        }

        [Test]
        public void TRS_SetParent_AddsExpectedComponents()
        {
            var parentSystem = World.GetOrCreateSystem<ParentSystem>();

#if !ENABLE_TRANSFORM_V1
            var localToWorldSystem = World.GetOrCreateSystem<LocalToWorldSystem>();
            var parent = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LocalTransform));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LocalTransform));
#else
            var parent = m_Manager.CreateEntity(typeof(LocalToWorld));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld));
#endif

            m_Manager.AddComponentData(child, new Parent { Value = parent });
#if ENABLE_TRANSFORM_V1
            m_Manager.AddComponent<LocalToParent>(child); // in transform V1, LocalToParent must be added by the user, but is removed automatically if the parent is deleted
#endif

            parentSystem.Update(World.Unmanaged);
#if !ENABLE_TRANSFORM_V1
            localToWorldSystem.Update(World.Unmanaged);
#endif
            // Make sure child has expected components
            Assert.IsTrue(m_Manager.HasComponent<Parent>(child));
            Assert.IsTrue(m_Manager.HasComponent<PreviousParent>(child));
            Assert.AreEqual(parent, m_Manager.GetComponentData<PreviousParent>(child).Value);
#if !ENABLE_TRANSFORM_V1
            Assert.IsTrue(m_Manager.HasComponent<WorldTransform>(child));
            Assert.IsTrue(m_Manager.HasComponent<ParentTransform>(child));
#else
            Assert.IsTrue(m_Manager.HasComponent<LocalToParent>(child));
#endif
            // Make sure parent has expected components
            Assert.IsTrue(m_Manager.HasComponent<Child>(parent));
            var children = m_Manager.GetBuffer<Child>(parent);
            Assert.AreEqual(1, children.Length);
            Assert.AreEqual(child, children[0].Value);
        }

        [Test]
        public void TRS_ChangeParent_SetsExpectedComponents()
        {
            var parentSystem = World.GetOrCreateSystem<ParentSystem>();

#if !ENABLE_TRANSFORM_V1
            var localToWorldSystem = World.GetOrCreateSystem<LocalToWorldSystem>();
            var parentA = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LocalTransform));
            var parentB = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LocalTransform));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LocalTransform));
#else
            var parentA = m_Manager.CreateEntity(typeof(LocalToWorld));
            var parentB = m_Manager.CreateEntity(typeof(LocalToWorld));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld));
#endif

            // set initial parent
            m_Manager.AddComponentData(child, new Parent { Value = parentA });
#if ENABLE_TRANSFORM_V1
            m_Manager.AddComponent<LocalToParent>(child); // in transform V1, LocalToParent must be added by the user, but is removed automatically if the parent is deleted
#endif
            parentSystem.Update(World.Unmanaged);
#if !ENABLE_TRANSFORM_V1
            localToWorldSystem.Update(World.Unmanaged);
#endif

            // change parent
            m_Manager.SetComponentData(child, new Parent { Value = parentB });
            parentSystem.Update(World.Unmanaged);
#if !ENABLE_TRANSFORM_V1
            localToWorldSystem.Update(World.Unmanaged);
#endif

            // Make sure child has expected components
            Assert.IsTrue(m_Manager.HasComponent<Parent>(child));
            Assert.IsTrue(m_Manager.HasComponent<PreviousParent>(child));
            Assert.AreEqual(parentB, m_Manager.GetComponentData<PreviousParent>(child).Value);
#if !ENABLE_TRANSFORM_V1
            Assert.IsTrue(m_Manager.HasComponent<WorldTransform>(child));
            Assert.IsTrue(m_Manager.HasComponent<ParentTransform>(child));
#else
            Assert.IsTrue(m_Manager.HasComponent<LocalToParent>(child));
#endif
            // Make sure old parent has expected components.
            Assert.IsFalse(m_Manager.HasComponent<Child>(parentA));
            // Make sure new parent has expected components
            Assert.IsTrue(m_Manager.HasComponent<Child>(parentB));
            var childrenB = m_Manager.GetBuffer<Child>(parentB);
            Assert.AreEqual(1, childrenB.Length);
            Assert.AreEqual(child, childrenB[0].Value);
        }

        [Test]
        public void TRS_RemoveParent_RemovesExpectedComponents()
        {
            var parentSystem = World.GetOrCreateSystem<ParentSystem>();

#if !ENABLE_TRANSFORM_V1
            var localToWorldSystem = World.GetOrCreateSystem<LocalToWorldSystem>();
            var parent = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LocalTransform));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LocalTransform));
#else
            var parent = m_Manager.CreateEntity(typeof(LocalToWorld));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld));
#endif

            // Establish parent/child relationship
            m_Manager.AddComponentData(child, new Parent { Value = parent });
#if ENABLE_TRANSFORM_V1
            m_Manager.AddComponent<LocalToParent>(child); // in transform V1, LocalToParent must be added by the user, but is removed automatically if the parent is deleted
#endif
            parentSystem.Update(World.Unmanaged);
#if !ENABLE_TRANSFORM_V1
            localToWorldSystem.Update(World.Unmanaged);
#endif

            // Break parent/child relationship
            m_Manager.RemoveComponent<Parent>(child);
            parentSystem.Update(World.Unmanaged);
#if !ENABLE_TRANSFORM_V1
            localToWorldSystem.Update(World.Unmanaged);
#endif

            // Make sure child has expected components
            Assert.IsFalse(m_Manager.HasComponent<Parent>(child));
            Assert.IsFalse(m_Manager.HasComponent<PreviousParent>(child));
#if !ENABLE_TRANSFORM_V1
            Assert.IsFalse(m_Manager.HasComponent<ParentTransform>(child));
#else
            // LocalToParent is not automatically deleted when the child is deparented
            Assert.IsTrue(m_Manager.HasComponent<LocalToParent>(child));
#endif
            // Make sure parent has expected components
            Assert.IsFalse(m_Manager.HasComponent<Child>(parent));
        }

        [Test]
        public void TRS_DestroyParent_RemovesExpectedComponents()
        {
            var parentSystem = World.GetOrCreateSystem<ParentSystem>();

#if !ENABLE_TRANSFORM_V1
            var localToWorldSystem = World.GetOrCreateSystem<LocalToWorldSystem>();
            var parent = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LocalTransform));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LocalTransform));
#else
            var parent = m_Manager.CreateEntity(typeof(LocalToWorld));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld));
#endif

            // Establish parent/child relationship
            m_Manager.AddComponentData(child, new Parent { Value = parent });
#if ENABLE_TRANSFORM_V1
            m_Manager.AddComponent<LocalToParent>(child); // in transform V1, LocalToParent must be added by the user, but is removed automatically if the parent is deleted
#endif
            parentSystem.Update(World.Unmanaged);
#if !ENABLE_TRANSFORM_V1
            localToWorldSystem.Update(World.Unmanaged);
#endif

            // Delete parent entity
            m_Manager.DestroyEntity(parent);
            parentSystem.Update(World.Unmanaged);
#if !ENABLE_TRANSFORM_V1
            localToWorldSystem.Update(World.Unmanaged);
#endif

            // Make sure child has expected components
            Assert.IsFalse(m_Manager.HasComponent<Parent>(child));
            Assert.IsFalse(m_Manager.HasComponent<PreviousParent>(child));
#if !ENABLE_TRANSFORM_V1
            Assert.IsFalse(m_Manager.HasComponent<ParentTransform>(child));
#else
            // LocalToParent *is* automatically deleted when the parent is destroyed
            Assert.IsFalse(m_Manager.HasComponent<LocalToParent>(child));
#endif
        }
    }
}
