using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

#if !ENABLE_TRANSFORM_V1

namespace Unity.Transforms
{
    /// <summary>
    /// Position, rotation and scale of an entity in world space
    /// </summary>
    /// <remarks>
    /// For entities in a transform hierarchy (specifically those with a <see cref="Parent"/> component), the
    /// <see cref="LocalToParentTransform"/> component takes precedence. The <see cref="TransformHierarchySystem"/> will
    /// automatically update the value of this component based on the transforms of this entity and its ancestors.
    ///
    /// If this component is present, <see cref="TransformToMatrixSystem"/> will use its value to compute the entity's
    /// <see cref="LocalToWorld"/> matrix.
    /// </remarks>
    /// <seealso cref="TransformAspect"/>
    [WriteGroup(typeof(LocalToWorld))]
    public struct LocalToWorldTransform : IComponentData
    {
        /// <summary>
        /// The entity's transform relative to world-space.
        /// </summary>
        public UniformScaleTransform Value;
    }
}

#endif
