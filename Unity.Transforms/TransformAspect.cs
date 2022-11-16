using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Properties;

#if !ENABLE_TRANSFORM_V1
namespace Unity.Transforms
{
    /// <summary>
    /// The TransformAspect allows access to the entity's transforms.
    /// If the entity has a parent, the TransformAspect will automatically keep LocalTransform and
    /// WorldTransform in sync with each other.
    /// </summary>
    public readonly partial struct TransformAspect : IAspect
    {
        readonly RefRW<LocalTransform>  m_LocalTransform;
        [Optional]
        readonly RefRW<WorldTransform>  m_WorldTransform;
        [Optional]
        // TODO: This should be RO, blocked by DOTS-6308
        readonly RefRW<ParentTransform> m_ParentTransform;

        // --- Properties R/W ---

        /// <summary>
        /// The local to world transform, or how the entity is positioned, rotated and scaled in world space.
        /// </summary>
        [CreateProperty]
        public WorldTransform WorldTransform
        {
            get => m_WorldTransform.ValueRO;
            set
            {
                m_WorldTransform.ValueRW = value;
                if (HasParent())
                {
                    m_LocalTransform.ValueRW = (LocalTransform)ParentTransform.InverseTransformTransform(value);
                }
                else
                {
                    m_LocalTransform.ValueRW = (LocalTransform)value;
                }
            }
        }

        /// <summary>
        /// The local to parent transform, or how the entity is positioned, rotated and scaled relative to its parent.
        /// </summary>
        [CreateProperty]
        public LocalTransform LocalTransform
        {
            get => m_LocalTransform.ValueRO;
            set
            {
                m_LocalTransform.ValueRW = value;
                if (HasParent())
                {
                    m_WorldTransform.ValueRW = (WorldTransform)ParentTransform.TransformTransform(value);
                }
                else
                {
                    m_WorldTransform.ValueRW = (WorldTransform)value;
                }
            }
        }

        /// <summary>The world space position of the entity.</summary>
        /// <remarks>This value may be stale by up to one frame. <see cref="LocalPosition"/> is always up to date.</remarks>
        [CreateProperty]
        public float3 WorldPosition
        {
            get => m_WorldTransform.ValueRO.Position;
            set
            {
                m_WorldTransform.ValueRW.Position = value;
                if (HasParent())
                {
                    m_LocalTransform.ValueRW.Position = ParentTransform.InverseTransformPoint(value);
                }
                else
                {
                    m_LocalTransform.ValueRW.Position = value;
                }
            }
        }
        /// <summary>Obsolete. Use <see cref="WorldPosition"/> instead.</summary>
        [Obsolete("This property is ambiguously named, and will be removed. Use LocalPosition (preferred) or worldPosition instead. (RemovedAfter Entities 1.0)", false)]
        [CreateProperty]
        public float3 Position
        {
            get => WorldPosition;
            set => WorldPosition = value;
        }

        /// <summary>The world space scale of the entity.</summary>
        /// <remarks>This value may be stale by up to one frame. <see cref="LocalScale"/> is always up to date.</remarks>
        [CreateProperty]
        public float WorldScale
        {
            // Gets the cached value, last written by LocalToWorldSystem
            get => m_WorldTransform.ValueRO.Scale;

            // If entity has a parent, this will write to the relative transform, which has not yet been cached
            set
            {
                m_WorldTransform.ValueRW.Scale = value;
                if (HasParent())
                {
                    m_LocalTransform.ValueRW.Scale = ParentTransform.InverseTransformScale(value);
                }
                else
                {
                    m_LocalTransform.ValueRW.Scale = value;
                }
            }
        }

        /// <summary>Obsolete. Use <see cref="WorldScale"/> instead.</summary>
        [Obsolete("This property is ambiguously named, and will be removed. Use LocalScale (preferred) or WorldScale instead. (RemovedAfter Entities 1.0)", false)]
        [CreateProperty]
        public float Scale
        {
            get => WorldScale;
            set => WorldScale = value;
        }

        /// <summary>The world space rotation of the entity.</summary>
        /// <remarks>This value may be stale by up to one frame. <see cref="LocalRotation"/> is always up to date.</remarks>
        [CreateProperty]
        public quaternion WorldRotation
        {
            // Gets the cached value, last written by LocalToWorldSystem
            get => m_WorldTransform.ValueRO.Rotation;

            // If entity has a parent, this will write to the relative transform, which has not yet been cached
            set
            {
                m_WorldTransform.ValueRW.Rotation = value;
                if (HasParent())
                {
                    m_LocalTransform.ValueRW.Rotation = ParentTransform.InverseTransformRotation(value);
                }
                else
                {
                    m_LocalTransform.ValueRW.Rotation = value;
                }
            }
        }

        /// <summary>Obsolete. Use <see cref="WorldRotation"/> instead.</summary>
        [Obsolete("This property is ambiguously named, and will be removed. Use LocalRotation (preferred) or WorldRotation instead. (RemovedAfter Entities 1.0)", false)]
        [CreateProperty]
        public quaternion Rotation
        {
            get => WorldRotation;
            set => WorldRotation = value;
        }

        /// <summary>The position of this entity relative to its parent.</summary>
        [CreateProperty]
        public float3 LocalPosition
        {
            get => m_LocalTransform.ValueRO.Position;
            set
            {
                m_LocalTransform.ValueRW.Position = value;
                if (HasParent())
                {
                    m_WorldTransform.ValueRW.Position = ParentTransform.TransformPoint(value);
                }
                else
                {
                    m_WorldTransform.ValueRW.Position = value;
                }
            }
        }

        /// <summary>The rotation of this entity relative to its parent.</summary>
        [CreateProperty]
        public quaternion LocalRotation
        {
            get => m_LocalTransform.ValueRO.Rotation;
            set
            {
                m_LocalTransform.ValueRW.Rotation = value;
                if (HasParent())
                {
                    m_WorldTransform.ValueRW.Rotation = ParentTransform.TransformRotation(value);
                }
                else
                {
                    m_WorldTransform.ValueRW.Rotation = value;
                }
            }
        }

        /// <summary>The scale of this entity relative to its parent.</summary>
        [CreateProperty]
        public float LocalScale
        {
            get => m_LocalTransform.ValueRO.Scale;
            set
            {
                m_LocalTransform.ValueRW.Scale = value;
                if (HasParent())
                {
                    m_WorldTransform.ValueRW.Scale = ParentTransform.TransformScale(value);
                }
                else
                {
                    m_WorldTransform.ValueRW.Scale = value;
                }
            }
        }

        // Properties Read Only
        // --------------------

        /// <summary>This is a copy of the parent's LocalToWorld transform</summary>
        public ParentTransform ParentTransform
        {
            get => m_ParentTransform.ValueRO;
        }

        /// <summary>The forward direction in world space.</summary>
        public float3 Forward
        {
            get => WorldTransform.Forward();
        }

        /// <summary>The back direction in world space.</summary>
        public float3 Back
        {
            get => -Forward;
        }

        /// <summary>The up direction in world space.</summary>
        public float3 Up
        {
            get => WorldTransform.Up();
        }

        /// <summary>The down direction in world space.</summary>
        public float3 Down
        {
            get => -Up;
        }

        /// <summary>The right direction in world space.</summary>
        public float3 Right
        {
            get => WorldTransform.Right();
        }

        /// <summary>The left direction in world space.</summary>
        public float3 Left
        {
            get => -Right;
        }

        /// <summary>Convert the LocalToWorld transform into a matrix.</summary>
        public float4x4 WorldMatrix
        {
            get => WorldTransform.ToMatrix();
        }

        /// <summary>Convert the inverse of the LocalToWorld transform into a matrix.</summary>
        public float4x4 InverseWorldMatrix
        {
            get => WorldTransform.Inverse().ToMatrix();
        }

        /// <summary>Convert the ParentToWorld transform into a matrix.</summary>
        public float4x4 ParentMatrix
        {
            get => ParentTransform.ToMatrix();
        }

        /// <summary>Convert the inverse of the ParentToWorld transform into a matrix.</summary>
        public float4x4 InverseParentMatrix
        {
            get => ParentTransform.Inverse().ToMatrix();
        }

        /// <summary>Convert the LocalToParent transform into a matrix.</summary>
        public float4x4 LocalMatrix
        {
            get => LocalTransform.ToMatrix();
        }

        /// <summary>Convert the inverse of the LocalToParent transform into a matrix.</summary>
        public float4x4 InverseLocalMatrix
        {
            get => LocalTransform.Inverse().ToMatrix();
        }

        // --- Methods ---

        /// <summary>Translate the entity in world space.</summary>
        /// <param name="translation">The relative translation.</param>
        public void TranslateWorld(float3 translation)
        {
            if (HasParent())
            {
                translation = ParentTransform.InverseTransformDirection(translation);
            }
            TranslateLocal(translation);
        }

        /// <summary>Rotate the entity in world space.</summary>
        /// <param name="rotation">The relative rotation.</param>
        public void RotateWorld(quaternion rotation)
        {
            if (HasParent())
            {
                var childWorldRotation = math.mul(m_ParentTransform.ValueRO.Rotation, LocalRotation);
                rotation = math.mul(math.mul(math.inverse(childWorldRotation), rotation), childWorldRotation);
            }
            RotateLocal(rotation);
        }

        /// <summary>Translate the entity relative to its parent.</summary>
        /// <param name="translation">The relative translation.</param>
        public void TranslateLocal(float3 translation)
        {
            var newLocalPosition = m_LocalTransform.ValueRW.Position + translation;
            m_LocalTransform.ValueRW.Position = newLocalPosition;
            if (HasParent())
            {
                m_WorldTransform.ValueRW.Position = ParentTransform.TransformPoint(newLocalPosition);
            }
            else
            {
                m_WorldTransform.ValueRW.Position = newLocalPosition;
            }
        }

        /// <summary>Rotate the entity relative to its parent.</summary>
        /// <param name="rotation">The relative rotation.</param>
        public void RotateLocal(quaternion rotation)
        {
            var newLocalRotation = math.mul(m_LocalTransform.ValueRO.Rotation, rotation);
            m_LocalTransform.ValueRW.Rotation = math.mul(m_LocalTransform.ValueRO.Rotation, rotation);
            if (HasParent())
            {
                m_WorldTransform.ValueRW.Rotation = ParentTransform.TransformRotation(newLocalRotation);
            }
            else
            {
                m_WorldTransform.ValueRW.Rotation = newLocalRotation;
            }
        }

        /// <summary>Transform a point from parent space into world space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TransformPointParentToWorld(float3 point)
        {
            return ParentTransform.TransformPoint(point);
        }

        /// <summary>Transform a point from world space into parent space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TransformPointWorldToParent(float3 point)
        {
            return ParentTransform.InverseTransformPoint(point);
        }

        /// <summary>Transform a point from local space into world space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TransformPointLocalToWorld(float3 point)
        {
            return WorldTransform.TransformPoint(point);
        }

        /// <summary>Transform a point from world space into local space.</summary>
        /// <param name="point">The point to transform</param>
        /// <returns>The transformed point</returns>>
        public float3 TransformPointWorldToLocal(float3 point)
        {
            return WorldTransform.InverseTransformPoint(point);
        }

        /// <summary>Transform a direction vector from parent space into world space.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionParentToWorld(float3 direction)
        {
            return ParentTransform.TransformDirection(direction);
        }

        /// <summary>Transform a direction vector from world space into parent space.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionWorldToParent(float3 direction)
        {
            return ParentTransform.InverseTransformDirection(direction);
        }

        /// <summary>Transform a direction vector from local space into world space.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionLocalToWorld(float3 direction)
        {
            return WorldTransform.TransformDirection(direction);
        }

        /// <summary>Transform a direction vector from world space into local space.</summary>
        /// <param name="direction">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformDirectionWorldToLocal(float3 direction)
        {
            return WorldTransform.InverseTransformDirection(direction);
        }

        /// <summary>Transform a rotation quaternion from parent space into world space.</summary>
        /// <param name="rotation">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformRotationParentToWorld(float3 rotation)
        {
            return ParentTransform.TransformDirection(rotation);
        }

        /// <summary>Transform a rotation quaternion from world space into parent space.</summary>
        /// <param name="rotation">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformRotationWorldToParent(float3 rotation)
        {
            return ParentTransform.InverseTransformDirection(rotation);
        }

        /// <summary>Transform a rotation quaternion from local space into world space.</summary>
        /// <param name="rotation">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformRotationLocalToWorld(float3 rotation)
        {
            return WorldTransform.TransformDirection(rotation);
        }

        /// <summary>Transform a rotation quaternion from world space into local space.</summary>
        /// <param name="rotation">The direction to transform</param>
        /// <returns>The transformed direction</returns>>
        public float3 TransformRotationWorldToLocal(float3 rotation)
        {
            return WorldTransform.InverseTransformDirection(rotation);
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
                targetPosition = ParentTransform.InverseTransformPoint(targetPosition);
            }

            var targetDir = targetPosition - LocalPosition;
            LocalRotation = quaternion.LookRotationSafe(targetDir, up);
        }

        // --- Private methods ---

        bool HasParent()
        {
            return m_ParentTransform.IsValid;
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
        public float3 WorldPosition
        {
            get => HasParent() ? math.transform(ParentToWorldMatrix, LocalPosition) : LocalPosition;
            set
            {
                LocalPosition = HasParent() ? math.transform(WorldToParentMatrix, value) : value;
            }
        }
        /// <summary>Obsolete. Use <see cref="WorldPosition"/> instead.</summary>
        [Obsolete("This property is ambiguously named, and will be removed. Use LocalPosition (preferred) or WorldPosition instead. (RemovedAfter Entities 1.0)", false)]
        [CreateProperty]
        public float3 Position
        {
            get => WorldPosition;
            set => WorldPosition = value;
        }


        /// <summary>The world space rotation of this TransformAspect.</summary>
        [CreateProperty]
        public quaternion WorldRotation
        {
            get => HasParent() ? math.mul(new quaternion(ParentToWorldMatrix), LocalRotation) : LocalRotation;
            set
            {
                LocalRotation = HasParent() ? math.mul(new quaternion(WorldToParentMatrix), value) : value;
            }
        }
        /// <summary>Obsolete. Use <see cref="WorldRotation"/> instead.</summary>
        [Obsolete("This property is ambiguously named, and will be removed. Use LocalRotation (preferred) or WorldRotation instead. (RemovedAfter Entities 1.0)", false)]
        [CreateProperty]
        public quaternion Rotation
        {
            get => WorldRotation;
            set => WorldRotation = value;
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
