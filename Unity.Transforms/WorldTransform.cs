using System.Globalization;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Properties;
using Unity.Transforms;

#if !ENABLE_TRANSFORM_V1

namespace Unity.Transforms
{
    /// <summary>
    /// Cached position, rotation and scale of an entity in world space.
    /// </summary>
    /// <remarks>
    /// For entities with a <see cref="LocalTransform"/> component), WorldTransform will be updated by the
    /// <see cref="LocalToWorldSystem"/>. If an entity also has a <see cref="Parent"/>, then the updated
    /// WorldTransform will differ from the LocalTransform by taking the parent's WorldTransform into account.
    /// If an entity does not have a parent, WorldTransform will updated to be the same as LocalTransform.
    ///
    /// This component is a derived quantity, and should not be written to directly by application code. It may also lag
    /// behind an object's true transform by up to a full frame. <see cref="LocalTransform"/> stores the authoritative,
    /// up-to-date copy of an entity's transform.
    ///
    /// This component will be automatically added to any entity with a <see cref="LocalTransform"/>.
    ///
    /// If this component is present, <see cref="LocalToWorldSystem"/> will use its value to compute the entity's
    /// <see cref="LocalToWorld"/> matrix.
    /// </remarks>
    /// <seealso cref="TransformAspect"/>
    [WriteGroup(typeof(LocalToWorld))] // TODO(DOTS-7271): remove this write group
    public struct WorldTransform : IComponentData, ITransformData
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
        public static readonly WorldTransform Identity = new WorldTransform { Scale = 1.0f, Rotation = quaternion.identity };

        /// <summary>
        /// Returns the Transform equivalent of a float4x4 matrix.
        /// </summary>
        /// <param name="matrix">The orthogonal matrix to convert.</param>
        /// <remarks>
        /// If the input matrix contains non-uniform scale, the largest value will be used.
        /// </remarks>
        /// <returns>The Transform.</returns>
        public static WorldTransform FromMatrix(float4x4 matrix) => TransformDataHelpers.FromMatrix<WorldTransform>(matrix);

        /// <summary>
        /// Returns a Transform initialized with the given position and rotation. Scale will be 1.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="rotation">The rotation.</param>
        /// <returns>The Transform.</returns>
        public static WorldTransform FromPositionRotation(float3 position, quaternion rotation) => new WorldTransform {Position = position, Scale = 1.0f, Rotation = rotation};

        /// <summary>
        /// Returns a Transform initialized with the given position, rotation and scale.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="rotation">The rotation.</param>
        /// <param name="scale">The scale.</param>
        /// <returns>The Transform.</returns>
        public static WorldTransform FromPositionRotationScale(float3 position, quaternion rotation, float scale) => TransformDataHelpers.FromPositionRotationScale<WorldTransform>(position, rotation, scale);

        /// <summary>
        /// Returns a Transform initialized with the given position. Rotation will be identity, and scale will be 1.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>The Transform.</returns>
        public static WorldTransform FromPosition(float3 position) => TransformDataHelpers.FromPosition<WorldTransform>(position);

        /// <summary>
        /// Returns a Transform initialized with the given position. Rotation will be identity, and scale will be 1.
        /// </summary>
        /// <param name="x">The x coordinate of the position.</param>
        /// <param name="y">The y coordinate of the position.</param>
        /// <param name="z">The z coordinate of the position.</param>
        /// <returns>The Transform.</returns>
        public static WorldTransform FromPosition(float x, float y, float z) => TransformDataHelpers.FromPosition<WorldTransform>(x, y, z);

        /// <summary>
        /// Returns a Transform initialized with the given rotation. Position will be 0,0,0, and scale will be 1.
        /// </summary>
        /// <param name="rotation">The rotation.</param>
        /// <returns>The Transform.</returns>
        public static WorldTransform FromRotation(quaternion rotation) => TransformDataHelpers.FromRotation<WorldTransform>(rotation);

        /// <summary>
        /// Returns a Transform initialized with the given scale. Position will be 0,0,0, and rotation will be identity.
        /// </summary>
        /// <param name="scale">The scale.</param>
        /// <returns>The Transform.</returns>
        public static WorldTransform FromScale(float scale) => TransformDataHelpers.FromScale<WorldTransform>(scale);

        /// <summary>
        /// Explicitly convert a LocalTransform to a WorldTransform.
        /// </summary>
        /// <param name="local">LocalTransform to convert.</param>
        /// <returns>Converted WorldTransform.</returns>
        public static explicit operator WorldTransform(LocalTransform local) =>new WorldTransform { Position = local.Position, Scale = local.Scale, Rotation = local.Rotation };

        /// <summary>
        /// Explicitly convert a ParentTransform to a WorldTransform.
        /// </summary>
        /// <param name="parent">ParentTransform to convert.</param>
        /// <returns>Converted WorldTransform.</returns>
        public static explicit operator WorldTransform(ParentTransform parent) => new WorldTransform { Position = parent.Position, Scale = parent.Scale, Rotation = parent.Rotation };

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
