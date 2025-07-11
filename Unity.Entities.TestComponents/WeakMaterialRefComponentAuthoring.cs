#if !UNITY_DISABLE_MANAGED_COMPONENTS
using Unity.Entities;
using Unity.Entities.Content;
using UnityEngine;

namespace Unity.Entities.Tests
{
    [DisallowMultipleComponent]
    public class WeakMaterialRefComponentAuthoring : MonoBehaviour
    {
        public WeakObjectSceneReference sceneRef;
        public WeakObjectReference<Material> matRef;
    }

    public struct WeakMaterialRefComponent : IComponentData
    {
        public WeakObjectReference<Material> materialRef;
        public WeakObjectSceneReference sceneRef;
    }

    public class WeakMaterialRefComponentAuthoringBaker : Baker<WeakMaterialRefComponentAuthoring>
    {
        public override void Bake(WeakMaterialRefComponentAuthoring authoring)
        {
            // This test shouldn't require transform components
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new WeakMaterialRefComponent { sceneRef = authoring.sceneRef, materialRef = authoring.matRef });
        }
    }
}
#endif
