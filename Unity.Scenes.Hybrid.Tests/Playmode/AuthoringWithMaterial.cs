using System;
using Unity.Entities;
using Unity.Scenes.Hybrid.Tests.Playmode;
using UnityEngine;

namespace Unity.Scenes.Hybrid.Tests
{
    public struct SharedWithMaterial : ISharedComponentData, IEquatable<SharedWithMaterial>
    {
        public Material material;

        public bool Equals(SharedWithMaterial other)
        {
            return Equals(material, other.material);
        }

        public override bool Equals(object obj)
        {
            return obj is SharedWithMaterial other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (material != null ? material.GetHashCode() : 0);
        }
    }

    [DisallowMultipleComponent]
    public class AuthoringWithMaterial : MonoBehaviour
    {
        public Material material;
    }

    public struct SingletonTag1 : IComponentData {}
    public struct EnableableTag1 : IComponentData, IEnableableComponent {}
    public struct EnableableTag2 : IComponentData, IEnableableComponent {}
    public struct EnableableTag3 : IComponentData, IEnableableComponent {}
    public struct EnableableTag4 : IComponentData, IEnableableComponent {}

    public class AuthoringWithMaterialBaker : Baker<AuthoringWithMaterial>
    {
        public override void Bake(AuthoringWithMaterial authoring)
        {
            // This test shouldn't require transform components
            var entity = GetEntity(TransformUsageFlags.None);
            // The material asset must be created before on the main thread not at import time
            AddSharedComponentManaged(entity, new SharedWithMaterial(){material = SubSceneTests.GetBasicMaterial()});

            AddComponent<SingletonTag1>(entity); // Add a non-enableable tag we can search for as a singleton
            AddComponent<EnableableTag1>(entity);
            SetComponentEnabled<EnableableTag1>(entity, false);
            AddComponent<EnableableTag2>(entity);
            SetComponentEnabled<EnableableTag2>(entity, true);
            AddComponent<EnableableTag3>(entity);
            SetComponentEnabled<EnableableTag3>(entity, false);
            AddComponent<EnableableTag4>(entity);
            SetComponentEnabled<EnableableTag4>(entity, true);
        }
    }
}
