using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.Entities.Serialization
{
    /// <summary>
    /// Weak reference to entity prefab. Entity prefabs are GameObjects that have been fully converted into entity data during the import/build process.
    /// </summary>
    public struct EntityPrefabReference : IEquatable<EntityPrefabReference>
    {
        internal UntypedWeakReferenceId PrefabId;

        internal EntityPrefabReference(UntypedWeakReferenceId prefabId)
        {
            PrefabId = prefabId;
        }

        /// <inheritdoc/>
        public static bool operator ==(EntityPrefabReference left, EntityPrefabReference right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(EntityPrefabReference left, EntityPrefabReference right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Construct an EntityPrefabReference from a GUID.
        /// </summary>
        /// <param name="guid">The prefab asset GUID.</param>
        public EntityPrefabReference(Hash128 guid)
        {
            PrefabId = new UntypedWeakReferenceId(new RuntimeGlobalObjectId { AssetGUID = guid, IdentifierType = 1 }, WeakReferenceGenerationType.EntityPrefab);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Construct an EntityPrefabReference from a GameObject.
        /// </summary>
        /// <param name="prefab">The prefab to construct from.</param>
        public EntityPrefabReference(GameObject prefab) : this(
            AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(prefab)))
        {
        }
#endif

        /// <summary>
        /// Checks if this reference holds the same asset GUID as the other reference.
        /// </summary>
        /// <param name="other">The other weak reference object to compare to.</param>
        /// <returns>True if the asset GUID of both are equal.</returns>
        public bool Equals(EntityPrefabReference other)
        {
            return PrefabId.Equals(other.PrefabId);
        }

        /// <summary>
        /// Overrides the default Object.Equals method.
        /// </summary>
        /// <param name="obj">An object to compare for equality.</param>
        /// <returns>True if this EntityPrefabReference and the object are equal.</returns>
        public override bool Equals(object obj)
        {
            return obj is EntityPrefabReference other && Equals(other);
        }

        /// <summary>
        /// Overrides the default Object.GetHashCode method.
        /// </summary>
        /// <returns>The hash code of this EntityPrefabReference.</returns>
        public override int GetHashCode()
        {
            return PrefabId.GetHashCode();
        }
    }
}
