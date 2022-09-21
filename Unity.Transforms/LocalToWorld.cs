using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Unity.Transforms
{
    /// <summary>
    /// The local-to-world transformation matrix for an entity
    /// </summary>
    /// <remarks>
    /// This matrix is primarily intended for consumption by the rendering systems.
    ///
    /// The matrix value is generally updated automatically by <see cref="TransformToMatrixSystem"/> based on the entity's
    /// transform data (<see cref="LocalToWorldTransform"/> for world-space entities, or <see cref="LocalToParentTransform"/>
    /// for entities in a transform hierarchy). These components are the preferred interface for application code to read
    /// and write an entity's transformation data.
    /// </remarks>
    [Serializable]
#if !ENABLE_TRANSFORM_V1
#else
    [WriteGroup(typeof(WorldToLocal))]
#endif
    public struct LocalToWorld : IComponentData
    {
        /// <summary>
        /// The transformation matrix
        /// </summary>
        public float4x4 Value;

        /// <summary>
        /// The "right" vector, in the entity's local space.
        /// </summary>
        public float3 Right => new float3(Value.c0.x, Value.c0.y, Value.c0.z);
        /// <summary>
        /// The "up" vector, in the entity's local space.
        /// </summary>
        public float3 Up => new float3(Value.c1.x, Value.c1.y, Value.c1.z);
        /// <summary>
        /// The "forward" vector, in the entity's local space.
        /// </summary>
        public float3 Forward => new float3(Value.c2.x, Value.c2.y, Value.c2.z);
        /// <summary>
        /// The "entity's" position in world-space.
        /// </summary>
        public float3 Position => new float3(Value.c3.x, Value.c3.y, Value.c3.z);

        /// <summary>
        /// The "entity's" orientation in world-space.
        /// </summary>
        /// <remarks>It is generally more efficient to read this value from <see cref="LocalToWorldTransform"/>, rather
        /// than extracting it from the local-to-world matrix.</remarks>
        public quaternion Rotation => new quaternion(Value);
    }
}
