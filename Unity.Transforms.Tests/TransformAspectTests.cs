using NUnit.Framework;
using Unity.Mathematics;
using Unity.Transforms;

namespace Unity.Entities.Tests
{
    partial class TransformAspectTests : ECSTestsFixture
    {
        const float k_Tolerance = 0.01f;
        Entity m_Parent;
        Entity m_Child;

        // --- Setup and helpers ---
        public unsafe T GetAspect<T>(Entity entity) where T : struct, IAspect, IAspectCreate<T>
        {
            T aspect = default;
            return aspect.CreateAspect(entity, ref *EmptySystem.CheckedState(), false);
        }

#if !ENABLE_TRANSFORM_V1
        void UpdateSystems()
        {
            World.GetOrCreateSystem<ParentSystem>().Update(World.Unmanaged);
            World.GetOrCreateSystem<TransformHierarchySystem>().Update(World.Unmanaged);
            EmptySystem.Update();
            m_Manager.CompleteAllTrackedJobs();
        }

        public override void Setup()
        {
            base.Setup();

            m_Parent = m_Manager.CreateEntity(new ComponentType[]{typeof(LocalToWorldTransform), typeof(LocalToWorld)});
            m_Manager.AddComponentData(m_Parent, new LocalToWorldTransform { Value = UniformScaleTransform.Identity });
            m_Manager.AddComponentData(m_Parent, new LocalToWorld { Value = float4x4.identity });

            m_Child = m_Manager.CreateEntity(new ComponentType[]{typeof(Parent), typeof(LocalToWorldTransform), typeof(LocalToParentTransform), typeof(LocalToWorld)});
            m_Manager.AddComponentData(m_Child, new Parent { Value = m_Parent });
            m_Manager.AddComponentData(m_Child, new LocalToWorldTransform { Value = UniformScaleTransform.Identity });
            m_Manager.AddComponentData(m_Child, new LocalToParentTransform { Value = UniformScaleTransform.Identity });
            m_Manager.AddComponentData(m_Child, new LocalToWorld { Value = float4x4.identity });

            UpdateSystems();
        }
#else
        void UpdateSystems()
        {
            World.GetOrCreateSystem<ParentSystem>().Update(World.Unmanaged);
            World.GetOrCreateSystem<TRSToLocalToParentSystem>().Update(World.Unmanaged);
            World.GetOrCreateSystem<TRSToLocalToWorldSystem>().Update(World.Unmanaged);
            World.GetOrCreateSystem<LocalToParentSystem>().Update(World.Unmanaged);
            EmptySystem.Update();
            m_Manager.CompleteAllTrackedJobs();
        }

        public override void Setup()
        {
            base.Setup();

            m_Parent = m_Manager.CreateEntity(new ComponentType[]{typeof(Translation), typeof(Rotation), typeof(LocalToWorld)});
            m_Manager.SetComponentData(m_Parent, new Translation { Value = float3.zero });
            m_Manager.SetComponentData(m_Parent, new Rotation { Value = quaternion.identity });
            m_Manager.SetComponentData(m_Parent, new LocalToWorld { Value = float4x4.identity });

            m_Child = m_Manager.CreateEntity(new ComponentType[]{typeof(Parent), typeof(Translation), typeof(Rotation), typeof(LocalToWorld), typeof(LocalToParent)});
            m_Manager.SetComponentData(m_Child, new Parent { Value = m_Parent });
            m_Manager.SetComponentData(m_Child, new Translation { Value = float3.zero });
            m_Manager.SetComponentData(m_Child, new Rotation { Value = quaternion.identity });
            m_Manager.SetComponentData(m_Child, new LocalToWorld { Value = float4x4.identity });
            m_Manager.SetComponentData(m_Child, new LocalToParent { Value = float4x4.identity });

            UpdateSystems();
        }
#endif

        public override void TearDown()
        {
            m_Manager.DestroyEntity(m_Parent);
            m_Manager.DestroyEntity(m_Child);
            UpdateSystems();
            base.TearDown();
        }

        // --- Tests ---
        [Test]
        public void TAT_TranslateParent()
        {
            var parentAspect = GetAspect<TransformAspect>(m_Parent);
            parentAspect.TranslateWorld(math.forward());  // Step forward
            UpdateSystems();
            parentAspect = GetAspect<TransformAspect>(m_Parent); // UpdateSystems invalidates the Aspect
            var parentPosition = parentAspect.Position;
            Assert.AreEqual(0f, parentPosition.x, k_Tolerance);
            Assert.AreEqual(0f, parentPosition.y, k_Tolerance);
            Assert.AreEqual(1f, parentPosition.z, k_Tolerance);
            var childAspect = GetAspect<TransformAspect>(m_Child);
            var childWorldPosition = childAspect.Position;
            Assert.AreEqual(0f, childWorldPosition.x, k_Tolerance);
            Assert.AreEqual(0f, childWorldPosition.y, k_Tolerance);
            Assert.AreEqual(1f, childWorldPosition.z, k_Tolerance);
            var childLocalPosition = childAspect.LocalPosition;
            Assert.AreEqual(0f, childLocalPosition.x, k_Tolerance);
            Assert.AreEqual(0f, childLocalPosition.y, k_Tolerance);
            Assert.AreEqual(0f, childLocalPosition.z, k_Tolerance);
        }

        [Test]
        public void TAT_TranslateParentAndTranslateChildWorld()
        {
            var parentAspect = GetAspect<TransformAspect>(m_Parent);
            parentAspect.TranslateWorld(math.forward());  // Step forward
            var childAspect = GetAspect<TransformAspect>(m_Child);
            childAspect.TranslateWorld(math.right());
            UpdateSystems();
            parentAspect = GetAspect<TransformAspect>(m_Parent); // UpdateSystems invalidates the Aspect
            var parentPosition = parentAspect.Position;
            Assert.AreEqual(0f, parentPosition.x, k_Tolerance);
            Assert.AreEqual(0f, parentPosition.y, k_Tolerance);
            Assert.AreEqual(1f, parentPosition.z, k_Tolerance);
            childAspect = GetAspect<TransformAspect>(m_Child);
            var childWorldPosition = childAspect.Position;
            Assert.AreEqual(1f, childWorldPosition.x, k_Tolerance);
            Assert.AreEqual(0f, childWorldPosition.y, k_Tolerance);
            Assert.AreEqual(1f, childWorldPosition.z, k_Tolerance);
            var childLocalPosition = childAspect.LocalPosition;
            Assert.AreEqual(1f, childLocalPosition.x, k_Tolerance);
            Assert.AreEqual(0f, childLocalPosition.y, k_Tolerance);
            Assert.AreEqual(0f, childLocalPosition.z, k_Tolerance);
        }

        [Test]
        public void TAT_RotateParent()
        {
            var parentAspect = GetAspect<TransformAspect>(m_Parent);
            parentAspect.RotateWorld(quaternion.EulerZXY(0, math.radians(90), 0)); // Turn right
            UpdateSystems();

            var childAspect = GetAspect<TransformAspect>(m_Child);
            var childLocalForward = childAspect.LocalToParentMatrix.c2.xyz;
            // 0 degrees => aligned with Z-axis
            Assert.AreEqual(0f, childLocalForward.x, k_Tolerance);
            Assert.AreEqual(0f, childLocalForward.y, k_Tolerance);
            Assert.AreEqual(1f, childLocalForward.z, k_Tolerance);
            var childWorldForward = childAspect.Forward;
            // 90 degrees => aligned with X-axis
            Assert.AreEqual(1f, childWorldForward.x, k_Tolerance);
            Assert.AreEqual(0f, childWorldForward.y, k_Tolerance);
            Assert.AreEqual(0f, childWorldForward.z, k_Tolerance);
        }

        [Test]
        public void TAT_RotateAndTranslateParent()
        {
            var parentAspect = GetAspect<TransformAspect>(m_Parent);
            parentAspect.TranslateWorld(parentAspect.Forward);  // Step forward
            parentAspect.RotateWorld(quaternion.EulerZXY(0, math.radians(90), 0)); // Turn
            parentAspect.TranslateWorld(parentAspect.Forward);  // Step forward in the new direction
            UpdateSystems();
            parentAspect = GetAspect<TransformAspect>(m_Parent); // UpdateSystems invalidates the Aspect
            var parentPosition = parentAspect.Position;
            Assert.AreEqual(1f, parentPosition.x, k_Tolerance);
            Assert.AreEqual(0f, parentPosition.y, k_Tolerance);
            Assert.AreEqual(1f, parentPosition.z, k_Tolerance);
            var childAspect = GetAspect<TransformAspect>(m_Child);
            var childPosition = childAspect.Position;
            Assert.AreEqual(1f, childPosition.x, k_Tolerance);
            Assert.AreEqual(0f, childPosition.y, k_Tolerance);
            Assert.AreEqual(1f, childPosition.z, k_Tolerance);
        }

        [Test]
        public void TAT_RotateParentAndTranslateChildLocal()
        {
            var parentAspect = GetAspect<TransformAspect>(m_Parent);
            parentAspect.RotateWorld(quaternion.EulerZXY(0, math.radians(90), 0)); // Turn right
            var childAspect = GetAspect<TransformAspect>(m_Child);
            childAspect.TranslateLocal(math.forward());
            UpdateSystems();
            childAspect = GetAspect<TransformAspect>(m_Child); // UpdateSystems invalidates the Aspect
            var childPosition = childAspect.Position;
            Assert.AreEqual(1f, childPosition.x, k_Tolerance);
            Assert.AreEqual(0f, childPosition.y, k_Tolerance);
            Assert.AreEqual(0f, childPosition.z, k_Tolerance);
        }

        [Test]
        public void TAT_RotateParentAndTranslateChildWorld()
        {
            var parentAspect = GetAspect<TransformAspect>(m_Parent);
            parentAspect.RotateWorld(quaternion.EulerZXY(0, math.radians(90), 0)); // Turn right
            // TODO: This is a serious issue: the LocalToParent component is always one frame behind.
            // This test will fail without an extra UpdateSystems() because any world space transformation on the
            // child needs the new WorldToParent in order to correctly convert the world space operation into a
            // local space one.
            UpdateSystems();
            var childAspect = GetAspect<TransformAspect>(m_Child);
            childAspect.TranslateWorld(math.forward());
            UpdateSystems();
            childAspect = GetAspect<TransformAspect>(m_Child); // UpdateSystems invalidates the Aspect
            var childPosition = childAspect.Position;
            Assert.AreEqual(0f, childPosition.x, k_Tolerance);
            Assert.AreEqual(0f, childPosition.y, k_Tolerance);
            Assert.AreEqual(1f, childPosition.z, k_Tolerance);
        }

        [Test]
        public void TAT_RotateParentAndRotateChildWorld()
        {
            var parentAspect = GetAspect<TransformAspect>(m_Parent);
            parentAspect.RotateWorld(quaternion.EulerZXY(0, math.radians(90), 0)); // Rotate parent around world Y
            var childAspect = GetAspect<TransformAspect>(m_Child);
            childAspect.RotateWorld(quaternion.EulerZXY(math.radians(90), 0, 0)); // Rotate child around world X, which is now local Z
            UpdateSystems();
            childAspect = GetAspect<TransformAspect>(m_Child);  // UpdateSystems invalidates the Aspect

            // Local forward is now local down
            var childLocalForward = childAspect.LocalToParentMatrix.c2.xyz;
            Assert.AreEqual(0, childLocalForward.x, k_Tolerance);
            Assert.AreEqual(-1, childLocalForward.y, k_Tolerance);
            Assert.AreEqual(0, childLocalForward.z, k_Tolerance);

            // Local up is now local forward
            var childLocalUp = childAspect.LocalToParentMatrix.c1.xyz;
            Assert.AreEqual(0, childLocalUp.x, k_Tolerance);
            Assert.AreEqual(0, childLocalUp.y, k_Tolerance);
            Assert.AreEqual(1, childLocalUp.z, k_Tolerance);

            // Forward is now down
            var childWorldForward = childAspect.Forward;
            Assert.AreEqual(0, childWorldForward.x, k_Tolerance);
            Assert.AreEqual(-1, childWorldForward.y, k_Tolerance);
            Assert.AreEqual(0, childWorldForward.z, k_Tolerance);
            // Up is now right
            var childWorldUp = childAspect.Up;
            Assert.AreEqual(1, childWorldUp.x, k_Tolerance);
            Assert.AreEqual(0, childWorldUp.y, k_Tolerance);
            Assert.AreEqual(0, childWorldUp.z, k_Tolerance);
        }

        [Test]
        public void TAT_RotateParentAndRotateChildLocal()
        {
            var parentAspect = GetAspect<TransformAspect>(m_Parent);
            parentAspect.RotateWorld(quaternion.EulerZXY(0, math.radians(90), 0)); // Rotate parent around Y
            var childAspect = GetAspect<TransformAspect>(m_Child);
            childAspect.RotateLocal(quaternion.EulerZXY(math.radians(90), 0, 0)); // Rotate child around X in local space
            UpdateSystems();
            childAspect = GetAspect<TransformAspect>(m_Child);  // UpdateSystems invalidates the Aspect

            // Local forward is now local down
            var childLocalForward = childAspect.LocalToParentMatrix.c2.xyz;
            Assert.AreEqual(0, childLocalForward.x, k_Tolerance);
            Assert.AreEqual(-1, childLocalForward.y, k_Tolerance);
            Assert.AreEqual(0, childLocalForward.z, k_Tolerance);

            // Local up is now local forward
            var childLocalUp = childAspect.LocalToParentMatrix.c1.xyz;
            Assert.AreEqual(0, childLocalUp.x, k_Tolerance);
            Assert.AreEqual(0, childLocalUp.y, k_Tolerance);
            Assert.AreEqual(1, childLocalUp.z, k_Tolerance);

            // Forward is now down
            var childWorldForward = childAspect.Forward;
            Assert.AreEqual(0, childWorldForward.x, k_Tolerance);
            Assert.AreEqual(-1, childWorldForward.y, k_Tolerance);
            Assert.AreEqual(0, childWorldForward.z, k_Tolerance);
            // Up is now right
            var childWorldUp = childAspect.Up;
            Assert.AreEqual(1, childWorldUp.x, k_Tolerance);
            Assert.AreEqual(0, childWorldUp.y, k_Tolerance);
            Assert.AreEqual(0, childWorldUp.z, k_Tolerance);
        }
        void MoveAndRotateParentAndChild()
        {
            var parentAspect = GetAspect<TransformAspect>(m_Parent);
            parentAspect.TranslateWorld(parentAspect.Forward * 2);
            parentAspect.RotateWorld(quaternion.EulerZXY(0, math.radians(90), 0));
            UpdateSystems();
            var childAspect = GetAspect<TransformAspect>(m_Child);
            childAspect.RotateWorld(quaternion.EulerZXY(math.radians(90), 0 , 0));
            childAspect.TranslateWorld(childAspect.Forward * 3);
            UpdateSystems();
        }

        [Test]
        public void TAT_TransformPointLocalToWorld()
        {
            MoveAndRotateParentAndChild();
            var childAspect = GetAspect<TransformAspect>(m_Child);
            var transformedPoint = childAspect.TransformPointLocalToWorld(new float3(10, 20, 30));

            Assert.AreEqual(33, transformedPoint.x, k_Tolerance);
            Assert.AreEqual(10, transformedPoint.y, k_Tolerance);
            Assert.AreEqual(22, transformedPoint.z, k_Tolerance);
        }

        [Test]
        public void TAT_TransformPointWorldToLocal()
        {
            MoveAndRotateParentAndChild();
            var childAspect = GetAspect<TransformAspect>(m_Child);
            var transformedPoint = childAspect.TransformPointWorldToLocal(new float3(33, 10, 22));

            Assert.AreEqual(10, transformedPoint.x, k_Tolerance);
            Assert.AreEqual(20, transformedPoint.y, k_Tolerance);
            Assert.AreEqual(30, transformedPoint.z, k_Tolerance);
        }

        [Test]
        public void TAT_TransformPointParentToWorld()
        {
            MoveAndRotateParentAndChild();
            var childAspect = GetAspect<TransformAspect>(m_Child);
            var transformedPoint = childAspect.TransformPointParentToWorld(new float3(10, 20, 30));

            Assert.AreEqual(30, transformedPoint.x, k_Tolerance);
            Assert.AreEqual(20, transformedPoint.y, k_Tolerance);
            Assert.AreEqual(-8, transformedPoint.z, k_Tolerance);
        }

        [Test]
        public void TAT_TransformPointWorldToParent()
        {
            MoveAndRotateParentAndChild();
            var childAspect = GetAspect<TransformAspect>(m_Child);
            var transformedPoint = childAspect.TransformPointWorldToParent(new float3(30, 20, -8));

            Assert.AreEqual(10, transformedPoint.x, k_Tolerance);
            Assert.AreEqual(20, transformedPoint.y, k_Tolerance);
            Assert.AreEqual(30, transformedPoint.z, k_Tolerance);
        }

        [Test]
        public void TAT_TransformDirectionLocalToWorld()
        {
            MoveAndRotateParentAndChild();
            var childAspect = GetAspect<TransformAspect>(m_Child);
            var transformedDirection = childAspect.TransformDirectionLocalToWorld(new float3(10, 20, 30));

            Assert.AreEqual(30, transformedDirection.x, k_Tolerance);
            Assert.AreEqual(10, transformedDirection.y, k_Tolerance);
            Assert.AreEqual(20, transformedDirection.z, k_Tolerance);
        }

        [Test]
        public void TAT_TransformDirectionWorldToLocal()
        {
            MoveAndRotateParentAndChild();
            var childAspect = GetAspect<TransformAspect>(m_Child);
            var transformedDirection = childAspect.TransformDirectionWorldToLocal(new float3(30, 10, 20));

            Assert.AreEqual(10, transformedDirection.x, k_Tolerance);
            Assert.AreEqual(20, transformedDirection.y, k_Tolerance);
            Assert.AreEqual(30, transformedDirection.z, k_Tolerance);
        }

        [Test]
        public void TAT_TransformDirectionParentToWorld()
        {
            MoveAndRotateParentAndChild();
            var childAspect = GetAspect<TransformAspect>(m_Child);
            var transformedDirection = childAspect.TransformDirectionParentToWorld(new float3(10, 20, 30));

            Assert.AreEqual(30, transformedDirection.x, k_Tolerance);
            Assert.AreEqual(20, transformedDirection.y, k_Tolerance);
            Assert.AreEqual(-10, transformedDirection.z, k_Tolerance);
        }

        [Test]
        public void TAT_TransformDirectionWorldToParent()
        {
            MoveAndRotateParentAndChild();
            var childAspect = GetAspect<TransformAspect>(m_Child);
            var transformedDirection = childAspect.TransformDirectionWorldToParent(new float3(30, 20, -10));

            Assert.AreEqual(10, transformedDirection.x, k_Tolerance);
            Assert.AreEqual(20, transformedDirection.y, k_Tolerance);
            Assert.AreEqual(30, transformedDirection.z, k_Tolerance);
        }
    }
}
