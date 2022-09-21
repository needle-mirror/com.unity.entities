using Unity.Entities;
using Unity.Mathematics;
using Unity.Properties;

#if !ENABLE_TRANSFORM_V1
namespace Unity.Transforms
{
    /// <summary>
    /// The TransformAspect allows access to the entity's transforms.
    /// If the entity has a parent, the TransformAspect will automatically keep LocalToWorldTransform and
    /// LocalToParentTransform in sync with each other.
    /// </summary>
    public readonly partial struct TransformAspect : IAspect
    {
        readonly RefRW<LocalToWorldTransform>   m_LocalToWorldTransform;
        [Optional]
        readonly RefRW<LocalToParentTransform>  m_LocalToParentTransform;
        [Optional]
        // TODO: This should be RO, blocked by DOTS-6308
        readonly RefRW<ParentToWorldTransform>  m_ParentToWorldTransform;

        // --- Properties R/W ---

        /// <summary>
        /// The local to world transform, or how the entity is positioned, rotated and scaled in world space.
        /// </summary>
        [CreateProperty]
        public UniformScaleTransform LocalToWorld
        {
            get => m_LocalToWorldTransform.ValueRO.Value;
            set
            {
                m_LocalToWorldTransform.ValueRW.Value = value;
                if (HasParent())
                {
                    m_LocalToParentTransform.ValueRW.Value = ParentToWorld.InverseTransformTransform(value);
                }
            }
        }

        /// <summary>
        /// The local to parent transform, or how the entity is positioned, rotated and scaled relative to its parent.
        /// </summary>
        [CreateProperty]
        public UniformScaleTransform LocalToParent
        {
            get => m_LocalToParentTransform.ValueRO.Value;
            set
            {
                m_LocalToParentTransform.ValueRW.Value = value;
                if (HasParent())
                {
                    m_LocalToWorldTransform.ValueRW.Value = ParentToWorld.TransformTransform(value);
                }
            }
        }

        /// <summary>The world space position of the entity.</summary>
        [CreateProperty]
        public float3 Position
        {
            get => m_LocalToWorldTransform.ValueRO.Value.Position;
            set
            {
                m_LocalToWorldTransform.ValueRW.Value.Position = value;
                if (HasParent())
                {
                    m_LocalToParentTransform.ValueRW.Value.Position =
                        ParentToWorld.InverseTransformPoint(value);
                }
            }
        }

        /// <summary>The world space rotation of the entity.</summary>
        [CreateProperty]
        public quaternion Rotation
        {
            // Gets the cached value, last written by TransformHierarchySystem
            get => m_LocalToWorldTransform.ValueRO.Value.Rotation;

            // If entity has a parent, this will write to the relative transform, which has not yet been cached
            set
            {
                m_LocalToWorldTransform.ValueRW.Value.Rotation = value;
                if (HasParent())
                {
                    m_LocalToParentTransform.ValueRW.Value.Rotation =
                        ParentToWorld.InverseTransformRotation(value);
                }
            }
        }

        /// <summary>The position of this entity relative to its parent.</summary>
        [CreateProperty]
        public float3 LocalPosition
        {
            get => HasParent() ? m_LocalToParentTransform.ValueRO.Value.Position : m_LocalToWorldTransform.ValueRO.Value.Position;
            set
            {
                if (HasParent())
                {
                    m_LocalToParentTransform.ValueRW.Value.Position = value;
                    m_LocalToWorldTransform.ValueRW.Value.Position = ParentToWorld.TransformPoint(value);
                }
                else
                {
                    m_LocalToWorldTransform.ValueRW.Value.Position = value;
                }
            }
        }

        /// <summary>The rotation of this entity relative to its parent.</summary>
        [CreateProperty]
        public quaternion LocalRotation
        {
            get => HasParent() ? m_LocalToParentTransform.ValueRO.Value.Rotation : m_LocalToWorldTransform.ValueRO.Value.Rotation;
            set
            {
                if (HasParent())
                {
                    m_LocalToParentTransform.ValueRW.Value.Rotation = value;
                    m_LocalToWorldTransform.ValueRW.Value.Rotation = ParentToWorld.TransformRotation(value);
                }
                else
                {
                    m_LocalToWorldTransform.ValueRW.Value.Rotation = value;
                }
            }
        }

        // Properties Read Only
        // --------------------

        /// <summary>This is a copy of the parent's LocalToWorld transform</summary>
        public UniformScaleTransform ParentToWorld
        {
            get => m_ParentToWorldTransform.ValueRO.Value;
        }

        /// <summary>The forward direction in world space.</summary>
        public float3 Forward
        {
            get => LocalToWorld.Forward();
        }

        /// <summary>The back direction in world space.</summary>
        public float3 Back
        {
            get => -Forward;
        }

        /// <summary>The up direction in world space.</summary>
        public float3 Up
        {
            get => LocalToWorld.Up();
        }

        /// <summary>The down direction in world space.</summary>
        public float3 Down
        {
            get => -Up;
        }

        /// <summary>The right direction in world space.</summary>
        public float3 Right
        {
            get => LocalToWorld.Right();
        }

        /// <summary>The left direction in world space.</summary>
        public float3 Left
        {
            get => -Right;
        }

        /// <summary>Convert the LocalToWorld transform into a matrix.</summary>
        public float4x4 LocalToWorldMatrix
        {
            get => LocalToWorld.ToMatrix();
        }

        /// <summary>Convert the inverse of the LocalToWorld transform into a matrix.</summary>
        public float4x4 WorldToLocalMatrix
        {
            get => LocalToWorld.Inverse().ToMatrix();
        }

        /// <summary>Convert the ParentToWorld transform into a matrix.</summary>
        public float4x4 ParentToWorldMatrix
        {
            get => ParentToWorld.ToMatrix();
        }

        /// <summary>Convert the inverse of the ParentToWorld transform into a matrix.</summary>
        public float4x4 WorldToParentMatrix
        {
            get => ParentToWorld.Inverse().ToMatrix();
        }

        /// <summary>Convert the LocalToParent transform into a matrix.</summary>
        public float4x4 LocalToParentMatrix
        {
            get => LocalToParent.ToMatrix();
        }

        /// <summary>Convert the inverse of the LocalToParent transform into a matrix.</summary>
        public float4x4 ParentToLocalMatrix
        {
            get => LocalToParent.Inverse().ToMatrix();
        }

        // --- Methods ---

        /// <summary>Translate the entity in world space.</summary>
        /// <param name="translation">The relative translation.</param>
        public void TranslateWorld(float3 translation)
        {
            if (HasParent())
            {
                translation = ParentToWorld.InverseTransformDirection(translation);
            }
            TranslateLocal(translation);
        }

        /// <summary>Rotate the entity in world space.</summary>
        /// <param name="rotation">The relative rotation.</param>
        public void RotateWorld(quaternion rotation)
        {
            if (HasParent())
            {
                var childWorldRotation = math.mul(m_ParentToWorldTransform.ValueRO.Value.Rotation, LocalRotation);
                rotation = math.mul(math.mul(math.inverse(childWorldRotation), rotation), childWorldRotation);
            }
            RotateLocal(rotation);
        }

        /// <summary>Translate the entity relative to its parent.</summary>
        /// <param name="translation">The relative translation.</param>
        public void TranslateLocal(float3 translation)
        {
            if (HasParent())
            {
                m_LocalToParentTransform.ValueRW.Value.Position += translation;
                m_LocalToWorldTransform.ValueRW.Value.Position =
                    ParentToWorld.TransformPoint(m_LocalToParentTransform.ValueRO.Value.Position);
            }
            else
            {
                m_LocalToWorldTransform.ValueRW.Value.Position += translation;
            }
        }

        /// <summary>Rotate the entity relative to its parent.</summary>
        /// <param name="rotation">The relative rotation.</param>
        public void RotateLocal(quaternion rotation)
        {
            if (HasParent())
            {
                m_LocalToParentTransform.ValueRW.Value.Rotation = math.mul(m_LocalToParentTransform.ValueRO.Value.Rotation, rotation);
                m_LocalToWorldTransform.ValueRW.Value.Rotation =
                    ParentToWorld.TransformRotation(m_LocalToParentTransform.ValueRO.Value.Rotation);
            }
            else
            {
                m_LocalToWorldTransform.ValueRW.Value.Rotation = math.mul(LocalToWorld.Rotation, rotation);
            }
        }

        /// <summary>Transform a point from parent space into world space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TransformPointParentToWorld(float3 point)
        {
            return ParentToWorld.TransformPoint(point);
        }

        /// <summary>Transform a point from world space into parent space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TransformPointWorldToParent(float3 point)
        {
            return ParentToWorld.InverseTransformPoint(point);
        }

        /// <summary>Transform a point from local space into world space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TransformPointLocalToWorld(float3 point)
        {
            return LocalToWorld.TransformPoint(point);
        }

        /// <summary>Transform a point from world space into local space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TransformPointWorldToLocal(float3 point)
        {
            return LocalToWorld.InverseTransformPoint(point);
        }

        /// <summary>Transform a direction vector from parent space into world space.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionParentToWorld(float3 direction)
        {
            return ParentToWorld.TransformDirection(direction);
        }

        /// <summary>Transform a direction vector from world space into parent space.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionWorldToParent(float3 direction)
        {
            return ParentToWorld.InverseTransformDirection(direction);
        }

        /// <summary>Transform a direction vector from local space into world space.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionLocalToWorld(float3 direction)
        {
            return LocalToWorld.TransformDirection(direction);
        }

        /// <summary>Transform a direction vector from world space into local space.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionWorldToLocal(float3 direction)
        {
            return LocalToWorld.InverseTransformDirection(direction);
        }

        /// <summary>Transform a rotation quaternion from parent space into world space.</summary>
        /// <param name="rotation">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformRotationParentToWorld(float3 rotation)
        {
            return ParentToWorld.TransformDirection(rotation);
        }

        /// <summary>Transform a rotation quaternion from world space into parent space.</summary>
        /// <param name="rotation">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformRotationWorldToParent(float3 rotation)
        {
            return ParentToWorld.InverseTransformDirection(rotation);
        }

        /// <summary>Transform a rotation quaternion from local space into world space.</summary>
        /// <param name="rotation">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformRotationLocalToWorld(float3 rotation)
        {
            return LocalToWorld.TransformDirection(rotation);
        }

        /// <summary>Transform a rotation quaternion from world space into local space.</summary>
        /// <param name="rotation">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformRotationWorldToLocal(float3 rotation)
        {
            return LocalToWorld.InverseTransformDirection(rotation);
        }

        /// <summary>
        /// Compute the rotation so that the forward vector points to the target.
        /// The up vector is assumed to be world up.
        ///</summary>
        /// <param name="targetPosition">The world space point to look at</param>
        public void LookAt(float3 targetPosition)
        {
            LookAt(targetPosition, math.up());
        }

        /// <summary>
        /// Compute the rotation so that the forward vector points to the target.
        /// This version takes an up vector.
        ///</summary>
        /// <param name="targetPosition">The world space point to look at</param>
        /// <param name="up">The up vector</param>
        public void LookAt(float3 targetPosition, float3 up)
        {
            if (HasParent())
            {
                targetPosition = ParentToWorld.InverseTransformPoint(targetPosition);
            }

            var targetDir = targetPosition - LocalPosition;
            LocalRotation = quaternion.LookRotationSafe(targetDir, up);
        }

        // --- Private methods ---

        bool HasParent()
        {
            return m_LocalToParentTransform.IsValid && m_ParentToWorldTransform.IsValid;
        }
    }
}

#else

namespace Unity.Transforms
{
    public readonly partial struct TransformAspect : IAspect
    {
        readonly RefRW<Translation>   m_Translation;
        readonly RefRW<Rotation>      m_Rotation;
        readonly RefRW<LocalToWorld>  m_LocalToWorld;
        [Optional]
        readonly RefRW<LocalToParent> m_LocalToParent;

        // --- Properties R/W ---

        /// <summary>The world space translation of this TransformAspect.</summary>
        [CreateProperty]
        public float3 Position
        {
            get => HasParent() ? math.transform(ParentToWorldMatrix, LocalPosition) : LocalPosition;
            set
            {
                LocalPosition = HasParent() ? math.transform(WorldToParentMatrix, value) : value;
            }
        }

        /// <summary>The world space rotation of this TransformAspect.</summary>
        [CreateProperty]
        public quaternion Rotation
        {
            get => HasParent() ? math.mul(new quaternion(ParentToWorldMatrix), LocalRotation) : LocalRotation;
            set
            {
                LocalRotation = HasParent() ? math.mul(new quaternion(WorldToParentMatrix), value) : value;
            }
        }

        /// <summary>The translation of this TransformAspect relative to its parent.</summary>
        [CreateProperty]
        public float3 LocalPosition
        {
            get => m_Translation.ValueRO.Value;
            set
            {
                m_Translation.ValueRW.Value = value;
                UpdateLocalToWorld();
            }
        }

        /// <summary>The rotation of this TransformAspect relative to its parent.</summary>
        [CreateProperty]
        public quaternion LocalRotation
        {
            get => m_Rotation.ValueRO.Value;
            set
            {
                m_Rotation.ValueRW.Value = value;
                UpdateLocalToWorld();
            }
        }

        // Properties Read Only
        // --------------------

        /// <summary>The matrix representing the transformation from local space into world space.</summary>
        public float4x4 LocalToWorldMatrix
        {
            get => m_LocalToWorld.ValueRO.Value;
        }

        /// <summary>The matrix representing the transformation from world space into local space.</summary>
        public float4x4 WorldToLocalMatrix
        {
            get => math.fastinverse(m_LocalToWorld.ValueRO.Value);
        }

        /// <summary>The matrix representing the transformation from parent space into world space.</summary>
        public float4x4 ParentToWorldMatrix
        {
            get => HasParent()
                ? math.mul(m_LocalToWorld.ValueRO.Value, math.fastinverse(m_LocalToParent.ValueRO.Value))
                : float4x4.identity;
        }

        /// <summary>The matrix representing the transformation from world space into parent space.</summary>
        public float4x4 WorldToParentMatrix
        {
            get => HasParent()
                ? math.mul(m_LocalToParent.ValueRO.Value, math.fastinverse(m_LocalToWorld.ValueRO.Value))
                : float4x4.identity;
        }

        /// <summary>The matrix representing the transformation from parent space into world space.</summary>
        public float4x4 ParentToLocalMatrix
        {
            get => HasParent()
                ? math.fastinverse(m_LocalToParent.ValueRO.Value)
                : float4x4.identity;
        }

        /// <summary>The matrix representing the transformation from world space into parent space.</summary>
        public float4x4 LocalToParentMatrix
        {
            get => HasParent()
                ? m_LocalToParent.ValueRO.Value
                : float4x4.identity;
        }

        /// <summary>The forward direction in world space.</summary>
        public float3 Forward
        {
            get => LocalToWorldMatrix.c2.xyz;
        }

        /// <summary>The back direction in world space.</summary>
        public float3 Back
        {
            get => -Forward;
        }
        /// <summary>The up direction in world space.</summary>
        public float3 Up
        {
            get => LocalToWorldMatrix.c1.xyz;
        }

        /// <summary>The down direction in world space.</summary>
        public float3 Down
        {
            get => -Up;
        }
        /// <summary>The right direction in world space.</summary>
        public float3 Right
        {
            get => LocalToWorldMatrix.c0.xyz;
        }

        /// <summary>The left direction in world space.</summary>
        public float3 Left
        {
            get => -Right;
        }

        // --- Methods ---

        /// <summary>Translate the entity in world space.</summary>
        /// <param name="translation">The relative translation.</param>
        public void TranslateWorld(float3 translation)
        {
            if (HasParent())
            {
                translation = math.rotate(WorldToParentMatrix, translation);
            }
            TranslateLocal(translation);
        }

        /// <summary>Rotate the entity in world space.</summary>
        /// <param name="rotation">The relative rotation.</param>
        public void RotateWorld(quaternion rotation)
        {
            if (HasParent())
            {
                var childWorldRotation = math.mul(new quaternion(ParentToWorldMatrix), LocalRotation);
                rotation = math.mul(math.mul(math.inverse(childWorldRotation), rotation), childWorldRotation);
            }
            RotateLocal(rotation);
        }
        /// <summary>Translate the entity relative to its parent.</summary>
        /// <param name="translation">The relative translation.</param>
        public void TranslateLocal(float3 translation)
        {
            LocalPosition += translation;
        }

        /// <summary>Rotate the entity relative to its parent.</summary>
        /// <param name="rotation">The relative rotation.</param>
        public void RotateLocal(quaternion rotation)
        {
            LocalRotation = math.mul(LocalRotation, rotation);
        }

        /// <summary>
        /// Compute the rotation so that the forward vector points to the target.
        /// The up vector is assumed to be world up.
        ///</summary>
        /// <param name="targetPosition">The world space point to look at</param>
        public void LookAt(float3 targetPosition)
        {
            LookAt(targetPosition, math.up());
        }
        /// <summary>
        /// Compute the rotation so that the forward vector points to the target.
        /// This version takes an up vector.
        ///</summary>
        /// <param name="targetPosition">The world space point to look at</param>
        /// <param name="up">The up vector</param>
        public void LookAt(float3 targetPosition, float3 up)
        {
            if (HasParent())
            {
                targetPosition = math.transform(WorldToParentMatrix, targetPosition);
            }

            var targetDir = targetPosition - LocalPosition;
            LocalRotation = quaternion.LookRotationSafe(targetDir, up);
        }

        /// <summary>Transform a point from parent space into world space.</summary>
        /// <param name="v">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TransformPointParentToWorld(float3 v)
        {
            return math.mul(ParentToWorldMatrix, new float4(v, 1)).xyz;
        }

        /// <summary>Transform a point from world space into parent space.</summary>
        /// <param name="v">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TransformPointWorldToParent(float3 v)
        {
            return math.mul(WorldToParentMatrix, new float4(v, 1)).xyz;
        }

        /// <summary>Transform a point from local space into world space.</summary>
        /// <param name="v">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TransformPointLocalToWorld(float3 v)
        {
            return math.mul(LocalToWorldMatrix, new float4(v, 1)).xyz;
        }

        /// <summary>Transform a point from world space into local space.</summary>
        /// <param name="v">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TransformPointWorldToLocal(float3 v)
        {
            return math.mul(WorldToLocalMatrix, new float4(v, 1)).xyz;
        }

        /// <summary>Transform a direction vector from parent space into world space.</summary>
        /// <param name="v">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionParentToWorld(float3 v)
        {
            return math.mul(ParentToWorldMatrix, new float4(v, 0)).xyz;
        }

        /// <summary>Transform a direction vector from world space into parent space.</summary>
        /// <param name="v">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionWorldToParent(float3 v)
        {
            return math.mul(WorldToParentMatrix, new float4(v, 0)).xyz;
        }

        /// <summary>Transform a direction vector from local space into world space.</summary>
        /// <param name="v">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionLocalToWorld(float3 v)
        {
            return math.mul(LocalToWorldMatrix, new float4(v, 0)).xyz;
        }

        /// <summary>Transform a direction vector from world space into local space.</summary>
        /// <param name="v">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionWorldToLocal(float3 v)
        {
            return math.mul(WorldToLocalMatrix, new float4(v, 0)).xyz;
        }

        // --- Private methods ---

        void UpdateLocalToWorld()
        {
            if (HasParent())
            {
                var newParentToLocal = float4x4.TRS(LocalPosition, LocalRotation, new float3(1, 1, 1));
                var newLocalToParent = math.fastinverse(newParentToLocal);
                var newLocalToWorld = math.mul(ParentToWorldMatrix, newLocalToParent);

                m_LocalToParent.ValueRW.Value = newLocalToParent;
                m_LocalToWorld.ValueRW.Value = newLocalToWorld;
            }
            else
            {
                m_LocalToWorld.ValueRW.Value = float4x4.TRS(LocalPosition, LocalRotation, new float3(1, 1, 1));
            }
        }

        bool HasParent()
        {
            return m_LocalToParent.IsValid;
        }
    }
}

#endif
