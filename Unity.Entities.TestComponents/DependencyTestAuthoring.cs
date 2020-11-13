using System;
using UnityEngine;

namespace Unity.Entities.Tests
{
    [AddComponentMenu("")]
    [ConverterVersion("sschoener", 1)]
    public class DependencyTestAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public GameObject GameObject;
        public UnityEngine.Object Asset;
        public Material Material;
        public Texture Texture;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            conversionSystem.DeclareAssetDependency(gameObject, Asset);
            conversionSystem.DeclareAssetDependency(gameObject, Texture);
            conversionSystem.DeclareAssetDependency(gameObject, Material);
            conversionSystem.DeclareDependency(gameObject, GameObject);
            dstManager.AddComponentData(entity, new ConversionDependencyData
            {
                MaterialColor = Material != null ? Material.color : default,
                HasMaterial = Material != null,
                TextureFilterMode = Texture != null ? Texture.filterMode : default,
                HasTexture = Texture != null
            });
        }
    }

    public struct ConversionDependencyData : IComponentData
    {
        public Color MaterialColor;
        public FilterMode TextureFilterMode;
        public bool HasMaterial;
        public bool HasTexture;
    }
}
