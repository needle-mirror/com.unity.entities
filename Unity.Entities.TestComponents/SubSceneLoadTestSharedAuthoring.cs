using System;
using Unity.Entities;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.Scenes.Editor.Tests
{
    [AddComponentMenu("")]
    [ConverterVersion("unity", 1)]
    public class SubSceneLoadTestSharedAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public int Int;
        public Object Asset;
        public string String;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddSharedComponentData(entity, new Component
            {
                Int = Int,
                Asset = Asset,
                String = String
            });
        }

        public struct Component : ISharedComponentData, IEquatable<Component>
        {
            // Shared components do not support Entity or BlobAssetReference typed fields, hence not tested
            public int Int;
            public Object Asset;
            public string String;

            public bool Equals(Component other)
            {
                return Int == other.Int && Equals(Asset, other.Asset) && String == other.String;
            }

            public override bool Equals(object obj)
            {
                return obj is Component other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = Int;
                    hashCode = (hashCode * 397) ^ (Asset != null ? Asset.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (String != null ? String.GetHashCode() : 0);
                    return hashCode;
                }
            }
        }
    }
}
