using Unity.Entities;

#if !ENABLE_TRANSFORM_V1

namespace Unity.Transforms
{
    /// <summary>
    /// For entities with the <see cref="Parent"/> component, this component contains a copy of the parent entity's
    /// <see cref="LocalToWorldTransform"/>.
    /// </summary>
    /// <remarks>
    /// This component is automatically added, removed, and updated by the transform systems. It is used to accelerate
    /// certain computations within the <see cref="TransformAspect"/>.
    /// </remarks>
    public struct ParentToWorldTransform : IComponentData
    {
        /// <summary>
        /// The parent entity's local-to-world transform.
        /// </summary>
        public UniformScaleTransform Value;
    }
}

#endif
