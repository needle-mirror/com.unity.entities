using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;

namespace Doc.CodeSamples.Tests
{
    public partial struct MyTransformSystem : ISystem
    {
        #region computeworld
        public void Foo(ref SystemState state)
        {
            // Create a simple hierarchy
            Entity parentEntity = state.EntityManager.CreateEntity(typeof(LocalToWorld), typeof(LocalTransform));
            state.EntityManager.SetComponentData(parentEntity, new LocalToWorld() { Value = float4x4.identity });
            state.EntityManager.SetComponentData(parentEntity, LocalTransform.Identity);

            Entity childEntity = state.EntityManager.CreateEntity(typeof(LocalToWorld), typeof(LocalTransform), typeof(Parent));
            state.EntityManager.SetComponentData(childEntity, new Parent() { Value = parentEntity });
            state.EntityManager.SetComponentData(childEntity, new LocalToWorld() { Value = float4x4.identity });
            state.EntityManager.SetComponentData(childEntity, LocalTransform.Identity);

            // Move the parent
            state.EntityManager.SetComponentData(parentEntity, LocalTransform.FromPosition(1, 2, 3));

            // At this point, both the child's and the parent's LocalToWorld will still be identity, because
            // ParentSystem and LocalToWorldSystem have not run yet.
            float4x4 childLocalToWorldMatrix = SystemAPI.GetComponent<LocalToWorld>(childEntity).Value; // Will be identity

            // The following should really be in an OnCreate() or similar. In that case, you will need to call
            // Lookup.Update in the OnUpdate().
            ComponentLookup<LocalTransform> localTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
            ComponentLookup<Parent> parentLookup = SystemAPI.GetComponentLookup<Parent>(true);
            ComponentLookup<PostTransformMatrix> postTransformLookup = SystemAPI.GetComponentLookup<PostTransformMatrix>(true);

            // If you absolutely need the child's up to date LocalToWorld before LocalToWorldSystem has run, this is how
            // you do it. It is an expensive operation, so use it carefully.
            TransformHelpers.ComputeWorldTransformMatrix(childEntity, out childLocalToWorldMatrix, ref localTransformLookup, ref parentLookup,
                ref postTransformLookup);
        }
        #endregion

        public void MyRotation()
        {
            #region lookatrotation
            float3 eyeWorldPosition = new float3(1, 2, 3);
            float3 targetWorldPosition = new float3(4, 5, 6);
            quaternion lookRotation = TransformHelpers.LookAtRotation(eyeWorldPosition, targetWorldPosition, math.up());
            #endregion
        }

        public void TransformPointExample()
        {
            #region transform-point-example
            float3 point = new float3(1, 2, 3);
            LocalTransform t = LocalTransform.FromPositionRotationScale(position: new float3(1, 2, 3),
                rotation: quaternion.identity, scale: 1);
            float3 point2 = t.TransformPoint(point);
            //point3 should be the same as point, within the limits of floating-point precision
            float3 point3 = t.InverseTransformPoint(point);
            #endregion
        }

        public void TransformDirExample()
        {
            #region transform-direction-example
            float3 dir = new float3(2, 1, 0);
            LocalTransform translationTransform = LocalTransform.FromPosition(new float3(1, 2, 3));
            // dir2 will be identical to dir; transforming a direction by a translation does not change its direction.
            float3 dir2 = translationTransform.TransformDirection(dir);

            // Rotate the direction by pi/2 radians around the X axis, and back again.
            LocalTransform rotationTransform = LocalTransform.FromRotation(quaternion.RotateX(math.PI2));
            float3 dir3 = rotationTransform.TransformDirection(dir);
            // dir4 should be identical to dir, within the limits of floating-point precision
            float3 dir4 = rotationTransform.InverseTransformDirection(dir3);
            #endregion
        }

    }


}


