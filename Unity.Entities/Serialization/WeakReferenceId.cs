using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.Entities.Serialization
{
    public enum WeakReferenceGenerationType : short
    {
        Scene,
        Prefab,
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

    public struct EntityPrefabReference : IEquatable<EntityPrefabReference>
    {
        internal UntypedWeakReferenceId PrefabId;

        internal EntityPrefabReference(UntypedWeakReferenceId prefabId)
        {
            PrefabId = prefabId;
        }

        public static bool operator ==(EntityPrefabReference left, EntityPrefabReference right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(EntityPrefabReference left, EntityPrefabReference right)
        {
            return !left.Equals(right);
        }

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
        public EntityPrefabReference(GameObject prefab) : this(
            AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(prefab)))
        {
        }
#endif

        public bool Equals(EntityPrefabReference other)
        {
            return PrefabId.Equals(other.PrefabId);
        }

        public override bool Equals(object obj)
        {
            return obj is EntityPrefabReference other && Equals(other);
        }

        public override int GetHashCode()
        {
            return PrefabId.GetHashCode();
        }
    }

    public struct EntitySceneReference : IEquatable<EntitySceneReference>
    {
        internal UntypedWeakReferenceId SceneId;

        internal EntitySceneReference(UntypedWeakReferenceId sceneId)
        {
            SceneId = sceneId;
        }

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
        public EntitySceneReference(SceneAsset sceneAsset) : this(
            AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(sceneAsset)))
        {
        }
#endif

        public bool Equals(EntitySceneReference other)
        {
            return SceneId.Equals(other.SceneId);
        }

        public override bool Equals(object obj)
        {
            return obj is EntitySceneReference other && Equals(other);
        }

        public override int GetHashCode()
        {
            return SceneId.GetHashCode();
        }
    }
}
