using System.Globalization;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Properties;

#if !ENABLE_TRANSFORM_V1

namespace Unity.Transforms
{
    /// <summary>
    /// For entities with the <see cref="Parent"/> component, this component contains a copy of the parent entity's
    /// <see cref="WorldTransform"/>.
    /// </summary>
    /// <remarks>
    /// This component is automatically added, removed, and updated by the <see cref="LocalToWorldSystem"/>.
    /// You can use it to transform into and out of parent space. It is used to accelerate certain computations
    /// within the <see cref="TransformAspect"/>.
    /// </remarks>
    public struct ParentTransform : IComponentData, ITransformData
    {
        /// <summary>
        /// The position of this transform.
        /// </summary>
        [CreateProperty]
        public float3 Position;

        /// <summary>
        /// The uniform scale of this transform.
        /// </summary>
        [CreateProperty]
        public float Scale;

        /// <summary>
        /// The rotation of this transform.
        /// </summary>
        [CreateProperty]
        public quaternion Rotation;

        /// <summary>
        /// The identity transform.
        /// </summary>
        public static readonly ParentTransform Identity = new ParentTransform { Scale = 1.0f, Rotation = quaternion.identity };

        /// <summary>
        /// Returns the Transform equivalent of a float4x4 matrix.
        /// </summary>
        /// <param name="matrix">The orthogonal matrix to convert.</param>
        /// <remarks>
        /// If the input matrix contains non-uniform scale, the largest value will be used.
        /// </remarks>
        /// <returns>The Transform.</returns>
        public static ParentTransform FromMatrix(float4x4 matrix) => TransformDataHelpers.FromMatrix<ParentTransform>(matrix);

        /// <summary>
        /// Returns a Transform initialized with the given position and rotation. Scale will be 1.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="rotation">The rotation.</param>
        /// <returns>The Transform.</returns>
        public static ParentTransform FromPositionRotation(float3 position, quaternion rotation) => new ParentTransform {Position = position, Scale = 1.0f, Rotation = rotation};

        /// <summary>
        /// Returns a Transform initialized with the given position, rotation and scale.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="rotation">The rotation.</param>
        /// <param name="scale">The scale.</param>
        /// <returns>The Transform.</returns>
        public static ParentTransform FromPositionRotationScale(float3 position, quaternion rotation, float scale) => TransformDataHelpers.FromPositionRotationScale<ParentTransform>(position, rotation, scale);

        /// <summary>
        /// Returns a Transform initialized with the given position. Rotation will be identity, and scale will be 1.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>The Transform.</returns>
        public static ParentTransform FromPosition(float3 position) => TransformDataHelpers.FromPosition<ParentTransform>(position);

        /// <summary>
        /// Returns a Transform initialized with the given position. Rotation will be identity, and scale will be 1.
        /// </summary>
        /// <param name="x">The x coordinate of the position.</param>
        /// <param name="y">The y coordinate of the position.</param>
        /// <param name="z">The z coordinate of the position.</param>
        /// <returns>The Transform.</returns>
        public static ParentTransform FromPosition(float x, float y, float z) => TransformDataHelpers.FromPosition<ParentTransform>(x, y, z);

        /// <summary>
        /// Returns a Transform initialized with the given rotation. Position will be 0,0,0, and scale will be 1.
        /// </summary>
        /// <param name="rotation">The rotation.</param>
        /// <returns>The Transform.</returns>
        public static ParentTransform FromRotation(quaternion rotation) => TransformDataHelpers.FromRotation<ParentTransform>(rotation);

        /// <summary>
        /// Returns a Transform initialized with the given scale. Position will be 0,0,0, and rotation will be identity.
        /// </summary>
        /// <param name="scale">The scale.</param>
        /// <returns>The Transform.</returns>
        public static ParentTransform FromScale(float scale) => TransformDataHelpers.FromScale<ParentTransform>(scale);

        /// <summary>
        /// Explicitly convert a WorldTransform to a ParentTransform.
        /// </summary>
        /// <param name="world">WorldTransform to convert.</param>
        /// <returns>Converted ParentTransform.</returns>
        public static explicit operator ParentTransform(WorldTransform world) => new ParentTransform { Position = world.Position, Scale = world.Scale, Rotation = world.Rotation };

        /// <summary>
        /// Explicitly convert a LocalTransform to a ParentTransform.
        /// </summary>
        /// <param name="local">LocalTransform to convert.</param>
        /// <returns>Converted ParentTransform.</returns>
        public static explicit operator ParentTransform(LocalTransform local) => new ParentTransform { Position = local.Position, Scale = local.Scale, Rotation = local.Rotation };

        /// <summary>
        /// Convert transformation data to a human-readable string
        /// </summary>
        /// <returns>The transform value as a human-readable string</returns>
        public override string ToString()
        {
            return $"Position={Position.ToString()} Rotation={Rotation.ToString()} Scale={Scale.ToString(CultureInfo.InvariantCulture)}";
        }

        /// <summary>
        /// Property so that TransformDataHelpers extension methods can access transform Position.
        /// The Position field should likely be explicitly in most cases.
        /// </summary>
        public float3 _Position { get { return Position; } set { Position = value; } }

        /// <summary>
        /// Property so that TransformDataHelpers extension methods can access transform Scale.
        /// The Scale field should likely be explicitly in most cases.
        /// </summary>
        public float _Scale { get { return Scale; } set { Scale = value; } }

        /// <summary>
        /// Property so that TransformDataHelpers extension methods can access transform Rotation.
        /// The Rotation field should likely be explicitly in most cases.
        /// </summary>
        public quaternion _Rotation { get { return Rotation; } set { Rotation = value; } }
    }
}

#endif
