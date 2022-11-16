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
    /// by the <see cref="LocalToWorldSystem"/>.
    ///
    /// If a system writes to an entity's <see cref="LocalToWorld"/> using a <see cref="WriteGroupAttribute"/>,
    /// it is also responsible for applying this matrix if it is present.
    ///
    /// An entity with this component must also have the <see cref="PropagateLocalToWorld"/> component if it wants
    /// its descendants to inherit the effects of this matrix.
    /// </remarks>
    public struct PostTransformScale : IComponentData
    {
        /// <summary>
        /// The post-transform scale matrix
        /// </summary>
        public float3x3 Value;
    }
}

#endif
