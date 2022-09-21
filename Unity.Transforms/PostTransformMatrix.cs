using Unity.Entities;
using Unity.Mathematics;

#if !ENABLE_TRANSFORM_V1

namespace Unity.Transforms
{
    /// <summary>
    /// An optional transformation matrix used to implement non-affine
    /// transformation effects such as non-uniform scale.
    /// </summary>
    /// <remarks>
    /// If this component is present, it is applied to the entity's <see cref="LocalToWorld"/> matrix
    /// by the <see cref="TransformToMatrixSystem"/>.
    /// </remarks>
    public struct PostTransformMatrix : IComponentData
    {
        /// <summary>
        /// The post-transform matrix
        /// </summary>
        public float4x4 Value;
    }
}

#endif
