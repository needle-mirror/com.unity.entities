using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.Entities.Serialization
{
    /// <summary>
    /// The type of weak reference generated during the baking of a scene.
    /// </summary>
    internal enum WeakReferenceGenerationType : short
    {
        /// <summary>
        /// The weak reference points to a Scene asset.
        /// </summary>
        Scene,
        /// <summary>
        /// The weak reference points to a Prefab asset.
        /// </summary>
        Prefab,
        /// <summary>
        /// The weak reference points to a Texture asset.
        /// </summary>
        Texture
        //Support will be added in the future
        //UnityObject,
        //Blob
    };

    internal struct UntypedWeakReferenceId : IEquatable<UntypedWeakReferenceId>
    {
        public Hash128 AssetId;
        public long LfId;
        public WeakReferenceGenerationType GenerationType;

        public static bool operator ==(UntypedWeakReferenceId left, UntypedWeakReferenceId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(UntypedWeakReferenceId left, UntypedWeakReferenceId right)
        {
            return !left.Equals(right);
        }

        public bool Equals(UntypedWeakReferenceId other)
        {
            return AssetId.Equals(other.AssetId) && LfId == other.LfId && GenerationType == other.GenerationType;
        }

        public override bool Equals(object obj)
        {
            return obj is UntypedWeakReferenceId other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = AssetId.GetHashCode();
                hashCode = (hashCode * 397) ^ LfId.GetHashCode();
                hashCode = (hashCode * 397) ^ (int) GenerationType;
                return hashCode;
            }
        }
    }

    /// <summary>
    /// Encapsulates a serializable reference to a Prefab asset.
    /// </summary>
    public struct EntityPrefabReference : IEquatable<EntityPrefabReference>
    {
        internal UntypedWeakReferenceId PrefabId;

        internal EntityPrefabReference(UntypedWeakReferenceId prefabId)
        {
            PrefabId = prefabId;
        }

        /// <summary>
        /// Checks if two reference objects are equal.
        /// </summary>
        /// <remarks>Two <see cref="EntityPrefabReference"/> are equal if they store the same asset GUIDs.</remarks>
        /// <param name="left">The first reference object to compare for equality.</param>
        /// <param name="right">The second reference object to compare for equality.</param>
        /// <returns>True if the two reference objects are equal.</returns>
        public static bool operator ==(EntityPrefabReference left, EntityPrefabReference right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Checks if two reference objects are unequal.
        /// </summary>
        /// <remarks>Two <see cref="EntityPrefabReference"/> are equal if they store the same asset GUIDs.</remarks>
        /// <param name="left">The first reference object to compare for inequality.</param>
        /// <param name="right">The second reference object to compare for inequality.</param>
        /// <returns>True if the two reference objects are unequal.</returns>
        public static bool operator !=(EntityPrefabReference left, EntityPrefabReference right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Initializes and returns an instance of EntityPrefabReference.
        /// </summary>
        /// <param name="guid">The guid of the prefab asset.</param>
        public EntityPrefabReference(Hash128 guid)
        {
            PrefabId = new UntypedWeakReferenceId
            {
                AssetId = guid,
                LfId = 0,
                GenerationType = WeakReferenceGenerationType.Prefab
            };
        }

#if UNITY_EDITOR
        /// <summary>
        /// Initializes and returns an instance of EntityPrefabReference.
        /// </summary>
        /// <param name="prefab">The referenced prefab asset.</param>
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

    /// <summary>
    /// Encapsulates a serializable reference to a Scene asset.
    /// </summary>
    public struct EntitySceneReference : IEquatable<EntitySceneReference>
    {
        internal UntypedWeakReferenceId SceneId;

        internal EntitySceneReference(UntypedWeakReferenceId sceneId)
        {
            SceneId = sceneId;
        }

        /// <summary>
        /// Initializes and returns an instance of EntitySceneReference.
        /// </summary>
        /// <param name="guid">The guid of the scene asset.</param>
        public EntitySceneReference(Hash128 guid)
        {
            SceneId = new UntypedWeakReferenceId
            {
                AssetId = guid,
                LfId = 0,
                GenerationType = WeakReferenceGenerationType.Scene
            };
        }

#if UNITY_EDITOR
        /// <summary>
        /// Initializes and returns an instance of EntitySceneReference.
        /// </summary>
        /// <param name="sceneAsset">The referenced <see cref="SceneAsset"/>.</param>
        public EntitySceneReference(SceneAsset sceneAsset) : this(
            AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(sceneAsset)))
        {
        }
#endif

        /// <summary>
        /// Checks if this reference holds the same asset GUID as the other reference.
        /// </summary>
        /// <param name="other">The other weak reference object to compare to.</param>
        /// <returns>True if the asset GUID of both are equal.</returns>
        public bool Equals(EntitySceneReference other)
        {
            return SceneId.Equals(other.SceneId);
        }

        /// <summary>
        /// Overrides the default Object.Equals method.
        /// </summary>
        /// <param name="obj">An object to compare for equality.</param>
        /// <returns>True if this EntitySceneReference and the object are equal.</returns>
        public override bool Equals(object obj)
        {
            return obj is EntitySceneReference other && Equals(other);
        }

        /// <summary>
        /// Overrides the default Object.GetHashCode method.
        /// </summary>
        /// <returns>The hash code of this EntitySceneReference.</returns>
        public override int GetHashCode()
        {
            return SceneId.GetHashCode();
        }
    }
}
