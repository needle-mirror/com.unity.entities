using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

#if !ENABLE_TRANSFORM_V1

namespace Unity.Transforms
{
    /// <summary>
    /// Position, rotation and scale relative to the parent of this entity.
    /// </summary>
    /// <remarks>
    /// You must add and assign this component to your code whenever the entity's <see cref="Parent"/> component is
    /// added, removed, or modified.
    /// </remarks>
    /// <seealso cref="TransformAspect"/>
    public struct LocalToParentTransform : IComponentData
    {
        /// <summary>
        /// The transform relative to the entity's parent.
        /// </summary>
        public UniformScaleTransform Value;
    }
}

#endif
