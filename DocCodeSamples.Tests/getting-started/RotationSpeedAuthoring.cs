namespace Doc.CodeSamples.Tests.GettingStarted
{
    #region example
    #region MonoBehaviour
    using Unity.Entities;
    using Unity.Mathematics;
    using UnityEngine;

    // The authoring component provides a way to define the rotation speed of
    // a GameObject in the Editor. ECS does not use the authoring component at 
    // runtime, but converts it into an entity component using the Baker class.    
    public class RotationSpeedAuthoring : MonoBehaviour
    {
        public float DegreesPerSecond = 360.0f;
    }
    #endregion

    #region baker
    // In the baking process, this Baker runs once for every RotationSpeedAuthoring
    // instance in a subscene.
    class RotationSpeedBaker : Baker<RotationSpeedAuthoring>
    {
        public override void Bake(RotationSpeedAuthoring authoring)
        {
            // GetEntity returns an entity that ECS creates from the GameObject using
            // pre-built ECS baker methods. TransformUsageFlags.Dynamic instructs the
            // Bake method to add the Transforms.LocalTransform component to the entity.
            var entity = GetEntity(authoring, TransformUsageFlags.Dynamic);

            var rotationSpeed = new RotationSpeed
            {
                // The math class is from the Unity.Mathematics namespace.
                // Unity.Mathematics is optimized for Burst-compiled code.
                RadiansPerSecond = math.radians(authoring.DegreesPerSecond)
            };

            AddComponent(entity, rotationSpeed);
        }
    }
    #endregion
    #endregion
}