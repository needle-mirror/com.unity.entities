using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.Entities.Serialization
{
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
        /// <param name="sectionIndex">The subscene section index.</param>
        public EntitySceneReference(Hash128 guid, int sectionIndex)
        {
            SceneId = new UntypedWeakReferenceId(new RuntimeGlobalObjectId { AssetGUID = guid, IdentifierType = 1, SceneObjectIdentifier0 = sectionIndex }, WeakReferenceGenerationType.EntityScene);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Initializes and returns an instance of EntitySceneReference.
        /// </summary>
        /// <param name="sceneAsset">The referenced <see cref="SceneAsset"/>.</param>
        public EntitySceneReference(SceneAsset sceneAsset) : this(
            AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(sceneAsset)), 0)
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
