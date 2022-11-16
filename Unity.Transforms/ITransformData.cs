#if !ENABLE_TRANSFORM_V1
using Unity.Mathematics;

namespace Unity.Transforms
{
    /// <summary>
    /// Interface for extension methods for LocalTransform, ParentTransform and WorldTransform components.
    /// </summary>
    public interface ITransformData
    {
        /// <summary>
        /// Property so that TransformDataHelpers extension methods can access transform Position.
        /// The Position field should likely be explicitly in most cases.
        /// </summary>
        public float3 _Position { get; set; }

        /// <summary>
        /// Property so that TransformDataHelpers extension methods can access transform Scale.
        /// The Scale field should likely be explicitly in most cases.
        /// </summary>
        public float _Scale { get; set; }

        /// <summary>
        /// Property so that TransformDataHelpers extension methods can access transform Rotation.
        /// The Rotation field should likely be explicitly in most cases.
        /// </summary>
        public quaternion _Rotation { get; set; }
    }

    /// <summary>
    /// Provides extension methods for transform components.
    /// </summary>
    public static class TransformDataHelpers
        {
        /// <summary>
        /// Gets the right vector of unit length.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <returns>The right vector.</returns>
        public static float3 Right<T>(this T data) where T : ITransformData
        {
            return data.TransformDirection(math.right());
        }

        /// <summary>
        /// Gets the up vector of unit length.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <returns>The up vector.</returns>
        public static float3 Up<T>(this T data) where T : ITransformData
        {
            return data.TransformDirection(math.up());
        }

        /// <summary>
        /// Gets the forward vector of unit length.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <returns>The forward vector.</returns>
        public static float3 Forward<T>(this T data) where T : ITransformData
        {
            return data.TransformDirection(math.forward());
        }

        /// <summary>
        /// Transforms a point by this transform.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="point">The point to be transformed.</param>
        /// <returns>The point after transformation.</returns>
        public static float3 TransformPoint<T>(this T data, float3 point) where T : ITransformData
        {
            return data._Position + math.rotate(data._Rotation, point) * data._Scale;
        }

        /// <summary>
        /// Transforms a point by the inverse of this transform.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="point">The point to be transformed.</param>
        /// <returns>The point after transformation.</returns>
        public static float3 InverseTransformPoint<T>(this T data, float3 point) where T : ITransformData
        {
            return math.rotate(math.conjugate(data._Rotation), point - data._Position) / data._Scale;
        }

        /// <summary>
        /// Transforms a direction by this transform.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="direction">The direction to be transformed.</param>
        /// <returns>The direction after transformation.</returns>
        public static float3 TransformDirection<T>(this T data, float3 direction) where T : ITransformData
        {
            return math.rotate(data._Rotation, direction);
        }

        /// <summary>
        /// Transforms a direction by the inverse of this transform.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="direction">The direction to be transformed.</param>
        /// <returns>The direction after transformation.</returns>
        public static float3 InverseTransformDirection<T>(this T data, float3 direction) where T : ITransformData
        {
            return math.rotate(math.conjugate(data._Rotation), direction);
        }

        /// <summary>
        /// Transforms a rotation by this transform.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="rotation">The rotation to be transformed.</param>
        /// <returns>The rotation after transformation.</returns>
        public static quaternion TransformRotation<T>(this T data, quaternion rotation) where T : ITransformData
        {
            return math.mul(data._Rotation, rotation);
        }

        /// <summary>
        /// Transforms a rotation by the inverse of this transform.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="rotation">The rotation to be transformed.</param>
        /// <returns>The rotation after transformation.</returns>
        public static quaternion InverseTransformRotation<T>(this T data, quaternion rotation) where T : ITransformData
        {
            return math.mul(math.conjugate(data._Rotation), rotation);
        }

        /// <summary>
        /// Transforms a scale by this transform.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="scale">The scale to be transformed.</param>
        /// <returns>The scale after transformation.</returns>
        public static float TransformScale<T>(this T data, float scale) where T : ITransformData
        {
            return scale * data._Scale;
        }

        /// <summary>
        /// Transforms a scale by the inverse of this transform.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="scale">The scale to be transformed.</param>
        /// <returns>The scale after transformation.</returns>
        public static float InverseTransformScale<T>(this T data, float scale) where T : ITransformData
        {
            return scale / data._Scale;
        }

        /// <summary>
        /// Transforms a Transform by this transform.
        /// </summary>
        /// <typeparam name="T1">The first ITransformData type</typeparam>
        /// <typeparam name="T2">The second ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="transformData">The Transform to be transformed.</param>
        /// <returns>The Transform after transformation.</returns>
        public static T1 TransformTransform<T1, T2>(this T1 data, T2 transformData)
            where T1 : ITransformData, new()
            where T2 : ITransformData, new()
        {
            var transform = default(T1);
            transform._Position = data.TransformPoint(transformData._Position);
            transform._Scale = data.TransformScale(transformData._Scale);
            transform._Rotation = data.TransformRotation(transformData._Rotation);
            return transform;
        }

        /// <summary>
        /// Transforms a Transform by the inverse of this transform.
        /// </summary>
        /// <typeparam name="T1">The first ITransformData type</typeparam>
        /// <typeparam name="T2">The second ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="transformData">The Transform to be transformed.</param>
        /// <returns>The Transform after transformation.</returns>
        public static T1 InverseTransformTransform<T1, T2>(this T1 data, T2 transformData)
            where T1 : ITransformData, new()
            where T2 : ITransformData, new()
        {
            var transform = default(T1);
            transform._Position = data.InverseTransformPoint(transformData._Position);
            transform._Scale = data.InverseTransformScale(transformData._Scale);
            transform._Rotation = data.InverseTransformRotation(transformData._Rotation);
            return transform;
        }

        /// <summary>
        /// Gets the inverse of this transform.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <returns>The inverse of the transform.</returns>
        public static T Inverse<T>(this T data) where T : ITransformData, new()
        {
            var inverseRotation = math.conjugate(data._Rotation);
            var inverseScale = 1.0f / data._Scale;

            var transform = default(T);
            transform._Position = -math.rotate(inverseRotation, data._Position) * inverseScale;
            transform._Scale = inverseScale;
            transform._Rotation = inverseRotation;
            return transform;
        }

        /// <summary>
        /// Gets the float4x4 equivalent of this transform.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <returns>The float4x4 matrix.</returns>
        public static float4x4 ToMatrix<T>(this T data) where T : ITransformData
        {
            return float4x4.TRS(data._Position, data._Rotation, data._Scale);
        }

        /// <summary>
        /// Gets the float4x4 equivalent of the inverse of this transform.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <returns>The inverse float4x4 matrix.</returns>
        public static float4x4 ToInverseMatrix<T>(this T data) where T : ITransformData, new()
        {
            return data.Inverse().ToMatrix();
        }

        /// <summary>
        /// Returns the Transform equivalent of a float4x4 matrix.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="matrix">The orthogonal matrix to convert.</param>
        /// <remarks>
        /// If the input matrix contains non-uniform scale, the largest value will be used.
        /// </remarks>
        /// <returns>The Transform.</returns>
        internal static T FromMatrix<T>(float4x4 matrix) where T : ITransformData, new()
        {
            var position = matrix.c3.xyz;
            var scaleX = math.length(matrix.c0.xyz);
            var scaleY = math.length(matrix.c1.xyz);
            var scaleZ = math.length(matrix.c2.xyz);

            // TODO(DOTS-7063): This method should throw if its scale/shear/etc. can't be represented in T.
            var scale = math.max(scaleX, math.max(scaleY, scaleZ));

            float3x3 normalizedRotationMatrix = math.orthonormalize(new float3x3(matrix));
            var rotation = new quaternion(normalizedRotationMatrix);

            var transform = default(T);
            transform._Position = position;
            transform._Scale = scale;
            transform._Rotation = rotation;
            return transform;
        }

        /// <summary>
        /// Returns a Transform initialized with the given position and rotation. Scale will be 1.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="position">The position.</param>
        /// <param name="rotation">The rotation.</param>
        /// <returns>The Transform.</returns>
        internal static T FromPositionRotation<T>(float3 position, quaternion rotation) where T : ITransformData, new()
        {
            var transform = default(T);
            transform._Position = position;
            transform._Scale = 1.0f;
            transform._Rotation = rotation;
            return transform;
        }

        /// <summary>
        /// Returns a Transform initialized with the given position, rotation and scale.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="position">The position.</param>
        /// <param name="rotation">The rotation.</param>
        /// <param name="scale">The scale.</param>
        /// <returns>The Transform.</returns>
        internal static T FromPositionRotationScale<T>(float3 position, quaternion rotation, float scale)
            where T : ITransformData, new()
        {
            var transform = default(T);
            transform._Position = position;
            transform._Scale = scale;
            transform._Rotation = rotation;
            return transform;
        }

        /// <summary>
        /// Returns a Transform initialized with the given position. Rotation will be identity, and scale will be 1.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="position">The position.</param>
        /// <returns>The Transform.</returns>
        internal static T FromPosition<T>(float3 position)
            where T : ITransformData, new()
        {
            var transform = default(T);
            transform._Position = position;
            transform._Scale = 1.0f;
            transform._Rotation = quaternion.identity;
            return transform;
        }

        /// <summary>
        /// Returns a Transform initialized with the given position. Rotation will be identity, and scale will be 1.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="x">The x coordinate of the position.</param>
        /// <param name="y">The y coordinate of the position.</param>
        /// <param name="z">The z coordinate of the position.</param>
        /// <returns>The Transform.</returns>
        internal static T FromPosition<T>(float x, float y, float z)
            where T : ITransformData, new()
        {
            var transform = default(T);
            transform._Position = new float3(x, y, z);
            transform._Scale = 1.0f;
            transform._Rotation = quaternion.identity;
            return transform;
        }

        /// <summary>
        /// Returns a Transform initialized with the given rotation. Position will be 0,0,0, and scale will be 1.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="rotation">The rotation.</param>
        /// <returns>The Transform.</returns>
        internal static T FromRotation<T>(quaternion rotation)
            where T : ITransformData, new()
        {
            var transform = default(T);
            transform._Scale = 1.0f;
            transform._Rotation = rotation;
            return transform;
        }

        /// <summary>
        /// Returns a Transform initialized with the given scale. Position will be 0,0,0, and rotation will be identity.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="scale">The scale.</param>
        /// <returns>The Transform.</returns>
        internal static T FromScale<T>(float scale)
            where T : ITransformData, new()
        {
            var transform = default(T);
            transform._Scale = scale;
            transform._Rotation = quaternion.identity;
            return transform;
        }

        /// <summary>
        /// Gets an identical transform with a new position value.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="position">The position.</param>
        /// <returns>The transform.</returns>
        public static T WithPosition<T>(this T data, float3 position)
            where T : ITransformData, new()
        {
            var transform = default(T);
            transform._Position = position;
            transform._Scale = data._Scale;
            transform._Rotation = data._Rotation;
            return transform;
        }

        /// <summary>
        /// Creates a transform that is identical but with a new position value.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="x">The x coordinate of the new position.</param>
        /// <param name="y">The y coordinate of the new position.</param>
        /// <param name="z">The z coordinate of the new position.</param>
        /// <returns>The new transform.</returns>
        public static T WithPosition<T>(this T data, float x, float y, float z)
            where T : ITransformData, new()
        {
            var transform = default(T);
            transform._Position = new float3(x, y, z);
            transform._Scale = data._Scale;
            transform._Rotation = data._Rotation;
            return transform;
        }

        /// <summary>
        /// Gets an identical transform with a new rotation value.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="rotation">The rotation.</param>
        /// <returns>The transform.</returns>
        public static T WithRotation<T>(this T data, quaternion rotation)
            where T : ITransformData, new()
        {
            var transform = default(T);
            transform._Position = data._Position;
            transform._Scale = data._Scale;
            transform._Rotation = rotation;
            return transform;
        }

        /// <summary>
        /// Gets an identical transform with a new scale value.
        /// </summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="scale">The scale.</param>
        /// <returns>The T.</returns>
        public static T WithScale<T>(this T data, float scale)
            where T : ITransformData, new()
        {
            var transform = default(T);
            transform._Position = data._Position;
            transform._Scale = scale;
            transform._Rotation = data._Rotation;
            return transform;
        }

        /// <summary>
        /// Translates this transform by the specified vector.
        /// </summary>
        /// <remarks>
        /// Note that this doesn't modify the original transform. Rather it returns a new one.
        /// </remarks>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="translation">The translation vector.</param>
        /// <returns>A new, translated Transform.</returns>
        public static T Translate<T>(this T data, float3 translation)
            where T : ITransformData, new()
        {
            var transform = default(T);
            transform._Position = data._Position + translation;
            transform._Scale = data._Scale;
            transform._Rotation = data._Rotation;
            return transform;
        }

        /// <summary>
        /// Scales this transform by the specified factor.
        /// </summary>
        /// <remarks>
        /// Note that this doesn't modify the original transform. Rather it returns a new one.
        /// </remarks>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="scale">The scaling factor.</param>
        /// <returns>A new, scaled Transform.</returns>
        public static T ApplyScale<T>(this T data, float scale)
            where T : ITransformData, new()
        {
            var transform = default(T);
            transform._Position = data._Position;
            transform._Scale = data._Scale * scale;
            transform._Rotation = data._Rotation;
            return transform;
        }

        /// <summary>
        /// Rotates this Transform by the specified quaternion.
        /// </summary>
        /// <remarks>
        /// Note that this doesn't modify the original transform. Rather it returns a new one.
        /// </remarks>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="rotation">The rotation quaternion of unit length.</param>
        /// <returns>A new, rotated Transform.</returns>
        public static T Rotate<T>(this T data, quaternion rotation)
            where T : ITransformData, new()
        {
            var transform = default(T);
            transform._Position = data._Position;
            transform._Scale = data._Scale;
            transform._Rotation = math.mul(data._Rotation, rotation);
            return transform;
        }

        /// <summary>
        /// Rotates this Transform around the X axis.
        /// </summary>
        /// <remarks>
        /// Note that this doesn't modify the original transform. Rather it returns a new one.
        /// </remarks>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="angle">The X rotation.</param>
        /// <returns>A new, rotated Transform.</returns>
        public static T RotateX<T>(this T data, float angle)
            where T : ITransformData, new()
        {
            return data.Rotate(quaternion.RotateX(angle));
        }

        /// <summary>
        /// Rotates this Transform around the Y axis.
        /// </summary>
        /// <remarks>
        /// Note that this doesn't modify the original transform. Rather it returns a new one.
        /// </remarks>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="angle">The Y rotation.</param>
        /// <returns>A new, rotated Transform.</returns>
        public static T RotateY<T>(this T data, float angle)
            where T : ITransformData, new()
        {
            return data.Rotate(quaternion.RotateY(angle));
        }

        /// <summary>
        /// Rotates this Transform around the Z axis.
        /// </summary>
        /// <remarks>
        /// Note that this doesn't modify the original transform. Rather it returns a new one.
        /// </remarks>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="angle">The Z rotation.</param>
        /// <returns>A new, rotated Transform.</returns>
        public static T RotateZ<T>(this T data, float angle)
            where T : ITransformData, new()
        {
            return data.Rotate(quaternion.RotateZ(angle));
        }

        /// <summary>Checks if a transform has equal position, rotation, and scale to another.</summary>
        /// <typeparam name="T">The ITransformData type</typeparam>
        /// <param name="data">The target transform object for this extension method.</param>
        /// <param name="other">The Transform to compare.</param>
        /// <returns>Returns true if the position, rotation, and scale are equal.</returns>
        public static bool Equals<T>(this T data, in T other) where T : ITransformData
        {
            return data._Position.Equals(other._Position) && data._Rotation.Equals(other._Rotation) && data._Scale.Equals(other._Scale);
        }
    }
}
#endif
