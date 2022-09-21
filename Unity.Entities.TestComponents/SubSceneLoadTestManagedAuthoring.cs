using System;
using Unity.Entities;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.Scenes.Editor.Tests
{
    [AddComponentMenu("")]
    public class SubSceneLoadTestManagedAuthoring : MonoBehaviour
    {
        public GameObject Entity;
        public int Int;
        public string String;
        public Object Asset;

    }

#if !NET_DOTS && !UNITY_DISABLE_MANAGED_COMPONENTS
    public class SubSceneLoadTestManagedComponent : IComponentData
    {
        // Managed components do not support BlobAssetReference typed fields, hence not tested
        public int Int;
        public string String;
        public Object Asset;
        public Entity Entity;
    }
#endif

    public class SubSceneLoadTestManagedBaker : Baker<SubSceneLoadTestManagedAuthoring>
    {
        public override void Bake(SubSceneLoadTestManagedAuthoring authoring)
        {
#if !NET_DOTS && !UNITY_DISABLE_MANAGED_COMPONENTS
            AddComponentObject(new SubSceneLoadTestManagedComponent
            {
                Entity = GetEntity(authoring.Entity),
                Int = authoring.Int,
                String = authoring.String,
                Asset = authoring.Asset,
            });
#endif
        }
    }
}
