using System.Globalization;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Properties;

#if !ENABLE_TRANSFORM_V1

namespace Unity.Transforms
{
    /// <summary>
    /// Position, rotation and scale of this entity, relative to the parent, or on world space, if no parent exists.
    /// </summary>
    /// <remarks>
    /// If the entity has a <see cref="Parent"/> component, LocalTransform is relative to that parent.
    /// Otherwise, it is in world space.
    /// </remarks>
    /// <seealso cref="TransformAspect"/>
    public struct LocalTransform : IComponentData, ITransformData
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
        public static readonly LocalTransform Identity = new LocalTransform { Scale = 1.0f, Rotation = quaternion.identity };

        /// <summary>
        /// Returns the Transform equivalent of a float4x4 matrix.
        /// </summary>
        /// <param name="matrix">The orthogonal matrix to convert.</param>
        /// <remarks>
        /// If the input matrix contains non-uniform scale, the largest value will be used.
        /// </remarks>
        /// <returns>The Transform.</returns>
        public static LocalTransform FromMatrix(float4x4 matrix) => TransformDataHelpers.FromMatrix<LocalTransform>(matrix);

        /// <summary>
        /// Returns a Transform initialized with the given position and rotation. Scale will be 1.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="rotation">The rotation.</param>
        /// <returns>The Transform.</returns>
        public static LocalTransform FromPositionRotation(float3 position, quaternion rotation) => new LocalTransform {Position = position, Scale = 1.0f, Rotation = rotation};

        /// <summary>
        /// Returns a Transform initialized with the given position, rotation and scale.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="rotation">The rotation.</param>
        /// <param name="scale">The scale.</param>
        /// <returns>The Transform.</returns>
        public static LocalTransform FromPositionRotationScale(float3 position, quaternion rotation, float scale) => TransformDataHelpers.FromPositionRotationScale<LocalTransform>(position, rotation, scale);

        /// <summary>
        /// Returns a Transform initialized with the given position. Rotation will be identity, and scale will be 1.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>The Transform.</returns>
        public static LocalTransform FromPosition(float3 position) => TransformDataHelpers.FromPosition<LocalTransform>(position);

        /// <summary>
        /// Returns a Transform initialized with the given position. Rotation will be identity, and scale will be 1.
        /// </summary>
        /// <param name="x">The x coordinate of the position.</param>
        /// <param name="y">The y coordinate of the position.</param>
        /// <param name="z">The z coordinate of the position.</param>
        /// <returns>The Transform.</returns>
        public static LocalTransform FromPosition(float x, float y, float z) => TransformDataHelpers.FromPosition<LocalTransform>(x, y, z);

        /// <summary>
        /// Returns a Transform initialized with the given rotation. Position will be 0,0,0, and scale will be 1.
        /// </summary>
        /// <param name="rotation">The rotation.</param>
        /// <returns>The Transform.</returns>
        public static LocalTransform FromRotation(quaternion rotation) => TransformDataHelpers.FromRotation<LocalTransform>(rotation);

        /// <summary>
        /// Returns a Transform initialized with the given scale. Position will be 0,0,0, and rotation will be identity.
        /// </summary>
        /// <param name="scale">The scale.</param>
        /// <returns>The Transform.</returns>
        public static LocalTransform FromScale(float scale) => TransformDataHelpers.FromScale<LocalTransform>(scale);

        /// <summary>
        /// Explicitly convert a WorldTransform to a LocalTransform.
        /// </summary>
        /// <param name="world">WorldTransform to convert.</param>
        /// <returns>Converted LocalTransform.</returns>
        public static explicit operator LocalTransform(WorldTransform world) => new LocalTransform { Position = world.Position, Scale = world.Scale, Rotation = world.Rotation };

        /// <summary>
        /// Explicitly convert a ParentTransform to a LocalTransform.
        /// </summary>
        /// <param name="parent">ParentTransform to convert.</param>
        /// <returns>Converted LocalTransform.</returns>
        public static explicit operator LocalTransform(ParentTransform parent) => new LocalTransform { Position = parent.Position, Scale = parent.Scale, Rotation = parent.Rotation };

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
