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
            m_Manager.CompleteAllJobs();

            var childWorldPosition = m_Manager.GetComponentData<LocalToWorld>(child).Position;

            Assert.AreEqual(childWorldPosition.x, 0f, k_Tolerance);
            Assert.AreEqual(childWorldPosition.y, 0f, k_Tolerance);
            Assert.AreEqual(childWorldPosition.z, -1f, k_Tolerance);
        }

        [Test]
        public void TRS_RemovedParentDoesNotAffectChildPosition()
        {
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
            m_Manager.CompleteAllJobs();

            var expectedChildWorldPosition = m_Manager.GetComponentData<LocalToWorld>(child).Position;

            m_Manager.RemoveComponent<Parent>(child);

            m_Manager.SetComponentData(parent, new LocalToWorld
            {
                Value = math.mul(float4x4.RotateY((float)math.PI), float4x4.Translate(new float3(0.0f, 0.0f, 1.0f)))
            });

            World.GetOrCreateSystem<ParentSystem>().Update(World.Unmanaged);
            World.GetOrCreateSystem<LocalToParentSystem>().Update(World.Unmanaged);

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
                var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Rotation),
                    typeof(NonUniformScale), typeof(Translation));
                var bodyArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Rotation),
                    typeof(NonUniformScale), typeof(Translation), typeof(Parent), typeof(LocalToParent));

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

                    var rotation = new Rotation() {Value = rotations[rotationIndex]};
                    var translation = new Translation() {Value = translations[translationIndex]};
                    var scale = new NonUniformScale() {Value = new float3(scaleValue)};

                    m_Manager.SetComponentData(bodyEntities[i], rotation);
                    m_Manager.SetComponentData(bodyEntities[i], translation);
                    m_Manager.SetComponentData(bodyEntities[i], scale);
                }

                for (int i = 1; i < 16; i++)
                {
                    var parentIndex = parentIndices[i];
                    m_Manager.SetComponentData(bodyEntities[i], new Parent() {Value = bodyEntities[parentIndex]});
                }
            }

            public void CreateWithWorldToLocal()
            {
                var rootArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Rotation),
                    typeof(NonUniformScale), typeof(Translation), typeof(WorldToLocal));
                var bodyArchetype = m_Manager.CreateArchetype(typeof(LocalToWorld), typeof(Rotation),
                    typeof(NonUniformScale), typeof(Translation), typeof(Parent), typeof(LocalToParent),
                    typeof(WorldToLocal));

                CreateInternal(bodyArchetype, rootArchetype, 1.0f);
            }

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

            public Entity CreateWithWriteGroupChildren()
            {
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

                return childChild;
            }

            public void Update()
            {
                World.GetOrCreateSystem<ParentSystem>().Update(World.Unmanaged);
                World.GetOrCreateSystem<CompositeRotationSystem>().Update(World.Unmanaged);
                World.GetOrCreateSystem<CompositeScaleSystem>().Update(World.Unmanaged);
                World.GetOrCreateSystem<ParentScaleInverseSystem>().Update(World.Unmanaged);
                World.GetOrCreateSystem<TRSToLocalToWorldSystem>().Update(World.Unmanaged);
                World.GetOrCreateSystem<TRSToLocalToParentSystem>().Update(World.Unmanaged);
                World.GetOrCreateSystem<LocalToParentSystem>().Update(World.Unmanaged);
                World.GetOrCreateSystem<WorldToLocalSystem>().Update(World.Unmanaged);

                // Force complete so that main thread (tests) can have access to direct editing.
                m_Manager.CompleteAllJobs();
            }

            internal static unsafe bool AssertCloseEnough(float4x4 a, float4x4 b)
            {
                float* ap = (float*)&a.c0.x;
                float* bp = (float*)&b.c0.x;
                for (int i = 0; i < 16; i++)
                {
                    Assert.AreEqual(bp[i], ap[i], k_Tolerance);
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
                        Assert.IsFalse(m_Manager.HasComponent<LocalToParent>(entity));
                        continue;
                    }

                    var localToParent = m_Manager.GetComponentData<LocalToParent>(entity).Value;

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

            public void RemoveSomeParents()
            {
                parentIndices[1] = -1;
                parentIndices[8] = -1;

                m_Manager.RemoveComponent<Parent>(Entities[1]);
                m_Manager.RemoveComponent<Parent>(Entities[8]);
                m_Manager.RemoveComponent<LocalToParent>(Entities[1]);
                m_Manager.RemoveComponent<LocalToParent>(Entities[8]);
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

        [Test]
        public void TRS_TestHierarchyFirstUpdateWitParentScaleInverse()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.CreateWithParentScaleInverse();
            testHierarchy.Update();

            testHierarchy.TestExpectedLocalToParent();

            testHierarchy.Dispose();
        }

        [Test]
        public void TRS_TestHierarchyFirstUpdateWitCompositeScaleParentScaleInverse()
        {
            var testHierarchy = new TestHierarchy(World, m_Manager);

            testHierarchy.CreateWithCompositeScaleParentScaleInverse();
            testHierarchy.Update();

            testHierarchy.TestExpectedLocalToParent();

            testHierarchy.Dispose();
        }

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

        [WriteGroup(typeof(LocalToWorld))]
        struct LocalToWorldWriteGroupComponent : IComponentData
        {
        }

        [Test]
        public void LocalToParentConsidersWriteGroups()
        {
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
                    var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));
                    m_Manager.SetComponentData(child, new Parent {Value = parent});
                    m_Manager.SetComponentData(child, new LocalToParent {Value = float4x4.identity});
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
                    m_Manager.AddComponentData(child, new LocalToParent {Value = float4x4.identity});
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
                    m_Manager.AddComponentData(child, new LocalToParent {Value = float4x4.identity});
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
                    m_Manager.AddComponentData(child, new LocalToParent {Value = float4x4.identity});
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
                    m_Manager.AddComponentData(child, new LocalToParent {Value = float4x4.identity});

                    for (int k = 0; k < childCount; k++)
                    {
                        var nextChild = m_Manager.CreateEntity(typeof(LocalToWorld));
                        m_Manager.AddComponentData(nextChild, new Parent {Value = child});
                        m_Manager.AddComponentData(nextChild, new LocalToParent {Value = float4x4.identity});
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
                    var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));
                    m_Manager.SetComponentData(child, new Parent {Value = parent});
                    m_Manager.SetComponentData(child, new LocalToParent {Value = float4x4.identity});
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
                    var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));
                    m_Manager.SetComponentData(child, new Parent {Value = parent});
                    m_Manager.SetComponentData(child, new LocalToParent {Value = float4x4.identity});
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

        [WriteGroup(typeof(LocalToWorld))]
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
                Entities.ForEach((ref LocalToWorld localToWorld, in TestWriteGroupComponent test) =>
                {
                    localToWorld.Value = new float4x4(float3x3.identity, new float3(test.Value));
                }).ScheduleParallel();
            }
        }

        [Test]
        public void TRS_TestHierarchyUpdatesNestedParentsChildWithWriteGroups()
        {
            // P
            // -- C1 <- This has an archetype with writegroup for LocalToWorld
            // -- -- C2 <- Normal child, this should be updated by LocalToParentSystem if I modify C1's LocalToWorld in a custom system

            var testHierarchy = new TestHierarchy(World, m_Manager);
            var c2Entity = testHierarchy.CreateWithWriteGroupChildren();
            testHierarchy.Update();
            World.GetOrCreateSystem<TestTransformWriteGroupSystem>().Update();
            testHierarchy.Update();

            var localToWorld = m_Manager.GetComponentData<LocalToWorld>(c2Entity);
            var expectedLocalToWorld = new float4x4(float3x3.identity, new float3(42));

            TestHierarchy.AssertCloseEnough(expectedLocalToWorld, localToWorld.Value);
        }

        [Test]
        public void EnabledHierarchy_CreatesChildBuffer()
        {
            Entity SimpleHiearchy()
            {
                var parent = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(LinkedEntityGroup));
                var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));

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
        public void TRS_SetParent_OldParentIsDestroyed([Values] bool withSystemState)
        {
            var parentSystem = World.GetOrCreateSystem<ParentSystem>();

            var parentA = m_Manager.CreateEntity(typeof(LocalToWorld));
            var parentB = m_Manager.CreateEntity(typeof(LocalToWorld));
            var child = m_Manager.CreateEntity(typeof(LocalToWorld), typeof(Parent), typeof(LocalToParent));

            if (withSystemState)
            {
                // Having a system state or not is important to validate the check in the
                // parenting system that it should not try to update a child buffer when it doesn't exist.
                // Because that's the difference between having a destroyed parent entity,
                // or a parent entity without the buffer.
                m_Manager.AddComponent<EcsStateTag1>(parentA);
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
    }
}
